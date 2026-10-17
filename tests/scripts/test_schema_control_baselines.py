from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from contextlib import ExitStack
from pathlib import Path
from unittest.mock import patch

from tools.schema_control import cli
from tools.schema_control.migrations import git_base_file_reader, resolve_git_commit


class SchemaControlBaselineTests(unittest.TestCase):
    def setUp(self) -> None:
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.git("init", "--quiet")
        self.git("config", "user.name", "Schema Test")
        self.git("config", "user.email", "schema-test@example.invalid")
        self.write(".gitignore", "/build/\n")
        self.write("db/Migrations/001_initial.sql", "CREATE TABLE db.items (id integer);\n")
        self.config = {
            "migration_sets": [
                {"id": "db", "directory": "db/Migrations", "schema": "db", "immutable": True}
            ],
            "migration_search_roots": ["db"],
            "contract_sets": [],
        }
        self.write("database/schema-control.json", json.dumps(self.config))
        self.write("database/policies/schema-control.json", "{}")
        self.write("database/policies/migration-waivers.json", "{}")
        self.baseline = self.commit("baseline")
        self.git("update-ref", "refs/remotes/origin/main", self.baseline)

        # Only the PostgreSQL boundary is stubbed. Git, migration safety,
        # rendering, promotion, artifact drift and evidence use real code.
        self.stack = ExitStack()
        self.addCleanup(self.stack.close)
        self.apply = self.stack.enter_context(patch("tools.schema_control.cli.apply_migrations"))
        self.apply.return_value.applied = ()
        self.apply.return_value.skipped = ()
        self.stack.enter_context(
            patch("tools.schema_control.catalog.extract_catalog", return_value={"schemas": []})
        )
        self.write("db/Migrations/002_new.sql", "CREATE TABLE db.new_items (id integer);\n")
        self.assertEqual(0, self.run_command("snapshot", self.baseline))
        self.assertEqual(0, cli.main(["--root", str(self.root), "promote"]))
        self.candidate = self.commit("candidate with current generated artifacts")

    def git(self, *args: str) -> str:
        return subprocess.run(
            ["git", *args], cwd=self.root, check=True, capture_output=True, text=True
        ).stdout.strip()

    def write(self, path: str, value: str) -> None:
        destination = self.root / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(value, encoding="utf-8")

    def commit(self, message: str) -> str:
        self.git("add", ".")
        self.git("commit", "--quiet", "-m", message)
        return self.git("rev-parse", "HEAD")

    def run_command(self, command: str, baseline: str) -> int:
        return cli.main(
            [
                "--root", str(self.root), command,
                "--database-url", "postgresql://example", "--base-ref", baseline,
            ]
        )

    def artifacts(self) -> dict[str, bytes]:
        candidate = self.root / "build/schema-control/candidate"
        return {
            path.relative_to(candidate).as_posix(): path.read_bytes()
            for path in candidate.rglob("*") if path.is_file()
        }

    def evidence(self) -> dict:
        return json.loads(self.artifacts()["reports/revisions.json"])

    def advance_main(self) -> str:
        self.git("checkout", "--quiet", "--detach", self.baseline)
        self.write("retired/Migrations/001_main_only.sql", "SELECT 1;\n")
        config = {
            **self.config,
            "migration_sets": self.config["migration_sets"] + [
                {"id": "retired", "directory": "retired/Migrations", "schema": "retired"}
            ],
        }
        self.write("database/schema-control.json", json.dumps(config))
        self.write("db/Migrations/001_initial.sql", "CREATE TABLE db.items (id bigint);\n")
        advanced = self.commit("advance main with different schema history")
        self.git("update-ref", "refs/remotes/origin/main", advanced)
        self.git("checkout", "--quiet", "--detach", self.candidate)
        return advanced

    def test_verify_is_unchanged_when_origin_main_advances(self) -> None:
        self.assertEqual(0, self.run_command("verify", self.baseline))
        before = self.artifacts()
        self.assertEqual(
            {
                "format": "meridian.schema-control-revisions.v1",
                "baseline_sha": self.baseline,
                "candidate_sha": self.candidate,
                "candidate_dirty": False,
            },
            self.evidence(),
        )
        advanced = self.advance_main()
        self.assertNotEqual(self.baseline, advanced)
        self.assertEqual(0, self.run_command("verify", self.baseline))
        self.assertEqual(before, self.artifacts())

        # Control: the fixture really would change the result if moving main
        # were still used for history, registry or deletion checks.
        self.assertEqual(1, self.run_command("verify", "origin/main"))
        self.assertEqual(advanced, self.evidence()["baseline_sha"])
        findings = json.loads(self.artifacts()["reports/migration-report.json"])["findings"]
        rules = {item["rule_id"] for item in findings}
        self.assertIn("migration-set-registry-removed", rules)
        self.assertIn("migration-immutable-file-removed", rules)
        self.assertTrue(any("immutable" in rule for rule in rules))

    def test_failed_verification_retains_same_evidence_after_main_advances(self) -> None:
        self.write("db/Migrations/001_initial.sql", "DROP TABLE db.items;\n")
        self.candidate = self.commit("candidate with forbidden historical edit")
        self.apply.reset_mock()
        self.assertEqual(1, self.run_command("verify", self.baseline))
        before = self.artifacts()
        self.assertEqual(self.baseline, self.evidence()["baseline_sha"])
        self.assertEqual(self.candidate, self.evidence()["candidate_sha"])
        summary = before["reports/summary.md"].decode()
        self.assertIn(self.baseline, summary)
        self.assertIn(self.candidate, summary)
        self.advance_main()
        self.assertEqual(1, self.run_command("verify", self.baseline))
        self.assertEqual(before, self.artifacts())
        self.apply.assert_not_called()

    def test_symbolic_baseline_is_pinned_before_any_history_checks(self) -> None:
        advanced = self.advance_main()
        self.git("update-ref", "refs/remotes/origin/main", self.baseline)
        original = cli._base_schema_control_config

        def move_ref(root: Path, ref: str) -> dict | None:
            self.assertEqual(self.baseline, ref)
            self.git("update-ref", "refs/remotes/origin/main", advanced)
            return original(root, ref)

        with patch("tools.schema_control.cli._base_schema_control_config", side_effect=move_ref):
            self.assertEqual(0, self.run_command("verify", "origin/main"))
        self.assertEqual(self.baseline, self.evidence()["baseline_sha"])

    def test_lazy_baseline_reader_does_not_follow_a_moving_ref(self) -> None:
        self.write("db/Migrations/001_initial.sql", "SELECT 'candidate edit';\n")
        self.candidate = self.commit("edit an existing migration")
        reader = git_base_file_reader(self.root, "origin/main")
        self.git("update-ref", "refs/remotes/origin/main", self.candidate)
        self.assertEqual(
            b"CREATE TABLE db.items (id integer);\n", reader("db/Migrations/001_initial.sql")
        )

    def test_manual_tag_resolves_to_commit_and_dirty_checkout_is_disclosed(self) -> None:
        self.git("tag", "-a", "reviewed-baseline", "-m", "reviewed", self.baseline)
        self.write("local-note.txt", "uncommitted local input\n")
        self.assertEqual(0, self.run_command("snapshot", "reviewed-baseline"))
        self.assertEqual(self.baseline, self.evidence()["baseline_sha"])
        self.assertEqual(self.candidate, self.evidence()["candidate_sha"])
        self.assertTrue(self.evidence()["candidate_dirty"])
        self.assertNotIn(b"candidate_sha", self.artifacts()["manifest/migrations.json"])

    def test_invalid_explicit_baselines_fail_before_database_work(self) -> None:
        self.apply.reset_mock()
        for ref in ("missing-ref", "", "--all"):
            with self.subTest(ref=ref):
                with self.assertRaises(ValueError):
                    resolve_git_commit(self.root, ref)
        self.assertEqual(2, self.run_command("verify", "missing-ref"))
        self.apply.assert_not_called()

    def test_database_failure_keeps_resolved_revision_evidence(self) -> None:
        self.apply.side_effect = RuntimeError("Database preflight failed")
        self.assertEqual(2, self.run_command("verify", self.baseline))
        self.assertEqual(self.baseline, self.evidence()["baseline_sha"])
        self.assertEqual(self.candidate, self.evidence()["candidate_sha"])


if __name__ == "__main__":
    unittest.main()
