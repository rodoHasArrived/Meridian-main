"""Hosted .NET shards must together run the default roster exactly once."""
from __future__ import annotations

import argparse
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CI_DIR = REPO_ROOT / "build" / "scripts" / "ci"


def load(name: str, filename: str):
    spec = importlib.util.spec_from_file_location(name, CI_DIR / filename)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


RUNNER = load("run_dotnet_ci_tests_shards", "run-dotnet-ci-tests.py")
MERGE = load("merge_dotnet_shard_summaries", "merge-dotnet-shard-summaries.py")
ROSTER = [name for name, _, _ in RUNNER.DEFAULT_TEST_PROJECTS]


class PartitionTests(unittest.TestCase):
    def test_every_shard_count_covers_the_roster_exactly_once(self) -> None:
        projects = RUNNER.parse_project_entries([])
        for total in range(1, len(projects) + 1):
            with self.subTest(total=total):
                groups = RUNNER.partition_projects(projects, total)
                self.assertEqual(len(groups), total)
                names = [project.name for group in groups for project in group]
                self.assertCountEqual(names, ROSTER)
                self.assertEqual(len(names), len(set(names)))
                self.assertTrue(all(groups), "no shard may be empty while entries remain")

    def test_partition_is_deterministic_and_keeps_roster_order(self) -> None:
        projects = RUNNER.parse_project_entries([])
        first = RUNNER.partition_projects(projects, 3)
        self.assertEqual(first, RUNNER.partition_projects(list(projects), 3))
        for group in first:
            positions = [ROSTER.index(project.name) for project in group]
            self.assertEqual(positions, sorted(positions))

    def test_longest_entries_are_spread_before_anything_shares_a_shard(self) -> None:
        projects = RUNNER.parse_project_entries([])
        longest = sorted(RUNNER.SHARD_DURATION_HINTS, key=RUNNER.SHARD_DURATION_HINTS.get, reverse=True)[:3]
        groups = RUNNER.partition_projects(projects, 3)
        owners = {name: index for index, group in enumerate(groups) for name in (p.name for p in group)}
        self.assertEqual(len({owners[name] for name in longest}), 3)

    def test_one_shard_is_the_complete_roster(self) -> None:
        projects = RUNNER.parse_project_entries([])
        self.assertEqual(RUNNER.partition_projects(projects, 1), [projects])

    def test_shard_argument_rejects_out_of_range_and_malformed_values(self) -> None:
        self.assertIsNone(RUNNER.parse_shard(""))
        self.assertEqual(RUNNER.parse_shard("2/3"), (2, 3))
        for value in ("0/3", "4/3", "1/0", "1", "a/b", "1/3/5", "-1/3", " 1/3"):
            with self.subTest(value=value), self.assertRaises(argparse.ArgumentTypeError):
                RUNNER.parse_shard(value)

    def test_dry_run_shards_select_disjoint_complete_slices_and_record_identity(self) -> None:
        seen: list[str] = []
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            for index in (1, 2, 3):
                output = Path(tmp) / str(index)
                completed = subprocess.run(
                    [sys.executable, str(CI_DIR / "run-dotnet-ci-tests.py"), "--dry-run",
                     "--shard", f"{index}/3", "--results-dir", str(output),
                     "--summary-output", str(output / "summary.md"),
                     "--json-output", str(output / "summary.json")],
                    cwd=REPO_ROOT, capture_output=True, text=True, check=False,
                )
                self.assertEqual(completed.returncode, 0, completed.stderr)
                payload = json.loads((output / "summary.json").read_text(encoding="utf-8"))
                self.assertEqual(payload["shard"], {"index": index, "total": 3})
                seen.extend(result["name"] for result in payload["results"])
        self.assertCountEqual(seen, ROSTER)

    def test_shard_cannot_be_combined_with_project_override(self) -> None:
        completed = subprocess.run(
            [sys.executable, str(CI_DIR / "run-dotnet-ci-tests.py"), "--dry-run", "--shard", "1/2",
             "--project", "demo=tests/Meridian.Demo.Tests/Meridian.Demo.Tests.csproj"],
            cwd=REPO_ROOT, capture_output=True, text=True, check=False,
        )
        self.assertEqual(completed.returncode, 2)
        self.assertIn("cannot be combined with --project", completed.stderr)


class MergeTests(unittest.TestCase):
    def shard_payload(self, index: int, names: list[str], *, total: int = 3, commit: str = "abc", failed: str | None = None):
        return {
            "shard": {"index": index, "total": total}, "commitSha": commit, "runId": "7", "runAttempt": "1",
            "cacheHit": "true", "testSeconds": 10.0 * index, "build_results": [{"name": "build:default-roster"}],
            "results": [
                {"name": name, "path": f"tests/{name}.csproj", "exit_code": 1 if name == failed else 0,
                 "evidence": {"counts": {"passed": 2, "failed": 0, "skipped": 0, "other": 0},
                              "testIdentities": [f"{name}.A", f"{name}.B"]}}
                for name in names
            ],
        }

    def write(self, root: Path, payloads: list[dict]) -> None:
        for position, payload in enumerate(payloads):
            directory = root / f"artifact-{position}" / "test-results" / "dotnet"
            directory.mkdir(parents=True)
            (directory / MERGE.SUMMARY_NAME).write_text(json.dumps(payload), encoding="utf-8")

    def merge(self, payloads: list[dict], roster: list[str]):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            self.write(Path(tmp), payloads)
            return MERGE.merge(Path(tmp), expected_shards=3, roster=roster)

    def test_complete_disjoint_shards_are_accepted_in_roster_order(self) -> None:
        roster = ["a", "b", "c", "d"]
        combined, errors = self.merge(
            [self.shard_payload(3, ["d"]), self.shard_payload(1, ["a", "c"]), self.shard_payload(2, ["b"])], roster)
        self.assertEqual(errors, [])
        self.assertTrue(combined["accepted"])
        self.assertEqual([result["name"] for result in combined["results"]], roster)
        self.assertEqual((combined["total"], combined["passed"], combined["failed"]), (4, 4, 0))
        self.assertEqual(combined["counts"]["passed"], 8)
        self.assertEqual(combined["testSeconds"], 30.0)
        self.assertEqual(combined["testRunnerSeconds"], 60.0)
        self.assertRegex(combined["testIdentityDigest"], r"^[0-9a-f]{64}$")
        self.assertEqual([shard["index"] for shard in combined["shards"]], [1, 2, 3])

    def test_identity_digest_does_not_depend_on_how_entries_were_sharded(self) -> None:
        roster = ["a", "b", "c"]
        one, _ = self.merge(
            [self.shard_payload(1, ["a"]), self.shard_payload(2, ["b"]), self.shard_payload(3, ["c"])], roster)
        other, _ = self.merge(
            [self.shard_payload(1, ["c"]), self.shard_payload(2, ["a"]), self.shard_payload(3, ["b"])], roster)
        self.assertEqual(one["testIdentityDigest"], other["testIdentityDigest"])

    def test_rejections(self) -> None:
        roster = ["a", "b", "c"]
        good = [self.shard_payload(1, ["a"]), self.shard_payload(2, ["b"]), self.shard_payload(3, ["c"])]
        build_failed = self.shard_payload(3, ["c"])
        del build_failed["build_results"]
        cases = {
            "no summary was produced": good[:2],
            "did not run in any shard": [good[0], good[1], self.shard_payload(3, [])],
            "ran in more than one shard": [good[0], good[1], self.shard_payload(3, ["c", "a"])],
            "failed in shard 2": [good[0], self.shard_payload(2, ["b"], failed="b"), good[2]],
            "different commits": [good[0], good[1], self.shard_payload(3, ["c"], commit="other")],
            "duplicate summary for shard 1": [good[0], self.shard_payload(1, ["b"]), good[2]],
            "not one of the 3 expected shards": [good[0], good[1], self.shard_payload(3, ["c"], total=4)],
            "is not a default roster entry": [good[0], good[1], self.shard_payload(3, ["c", "stray"])],
            "tests did not start": [good[0], good[1], build_failed],
        }
        for expected, payloads in cases.items():
            with self.subTest(expected=expected):
                combined, errors = self.merge(payloads, roster)
                self.assertFalse(combined["accepted"])
                self.assertTrue(any(expected in error for error in errors), errors)

    def test_unsharded_or_unreadable_summaries_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            unsharded = self.shard_payload(1, ["a"])
            unsharded["shard"] = None
            self.write(root, [unsharded])
            broken = root / "broken"
            broken.mkdir()
            (broken / MERGE.SUMMARY_NAME).write_text("{", encoding="utf-8")
            _, errors = MERGE.merge(root, expected_shards=3, roster=["a"])
        self.assertTrue(any("unreadable summary" in error for error in errors))
        self.assertTrue(any("not one of the 3 expected shards" in error for error in errors))

    def test_cli_fails_closed_when_no_evidence_was_downloaded(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            completed = subprocess.run(
                [sys.executable, str(CI_DIR / "merge-dotnet-shard-summaries.py"),
                 "--shards-dir", str(root / "absent"), "--expected-shards", "3",
                 "--json-output", str(root / "out" / "summary.json"),
                 "--summary-output", str(root / "out" / "summary.md")],
                cwd=REPO_ROOT, capture_output=True, text=True, check=False,
            )
            self.assertEqual(completed.returncode, 1)
            self.assertIn("no summary was produced", completed.stderr)
            payload = json.loads((root / "out" / "summary.json").read_text(encoding="utf-8"))
            self.assertFalse(payload["accepted"])
            self.assertIn("rejected", (root / "out" / "summary.md").read_text(encoding="utf-8"))

    def test_cli_accepts_real_dry_run_shards_of_the_default_roster(self) -> None:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
            root = Path(tmp)
            for index in (1, 2, 3):
                output = root / "shards" / f"shard-{index}" / "test-results" / "dotnet"
                subprocess.run(
                    [sys.executable, str(CI_DIR / "run-dotnet-ci-tests.py"), "--dry-run",
                     "--shard", f"{index}/3", "--results-dir", str(output),
                     "--summary-output", str(output / "ci-dotnet-test-summary.md"),
                     "--json-output", str(output / MERGE.SUMMARY_NAME)],
                    cwd=REPO_ROOT, capture_output=True, text=True, check=True,
                )
            completed = subprocess.run(
                [sys.executable, str(CI_DIR / "merge-dotnet-shard-summaries.py"),
                 "--shards-dir", str(root / "shards"), "--expected-shards", "3",
                 "--json-output", str(root / "summary.json"), "--summary-output", str(root / "summary.md")],
                cwd=REPO_ROOT, capture_output=True, text=True, check=False,
            )
            self.assertEqual(completed.returncode, 0, completed.stderr)
            payload = json.loads((root / "summary.json").read_text(encoding="utf-8"))
            self.assertEqual([result["name"] for result in payload["results"]], ROSTER)


if __name__ == "__main__":
    unittest.main()
