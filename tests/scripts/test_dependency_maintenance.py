"""Dependency candidate adoption and scheduled reporting fail-closed contracts."""
from __future__ import annotations

import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import yaml


ROOT = Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "build/scripts/ci" / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


locks = load("dependency-locks")
outcomes = load("summarize-repair-outcomes")


class LockCandidatesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "repo"
        self.root.mkdir()
        self.candidate = Path(self.temp.name) / "candidate"
        (self.candidate / "locks").mkdir(parents=True)
        subprocess.run(["git", "init", "-q"], cwd=self.root, check=True)
        for name, data in {
            "global.json": '{"sdk":{"version":"10.0.100"}}',
            "Directory.Build.props": "<Project />",
            "Meridian.sln": 'Project("{guid}") = "Example", "src/Example/Example.csproj", "{guid2}"\n',
            "src/Example/Example.csproj": "<Project />",
        }.items():
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(data)
        subprocess.run(["git", "add", "."], cwd=self.root, check=True)
        self.name = "Example.packages.lock.json"
        data = b'{"version":1,"dependencies":{"net10.0":{}}}\n'
        (self.candidate / "locks" / self.name).write_bytes(data)
        self.manifest = dict(schemaVersion=1, profile="linux-portable", sourceCommit="a" * 40,
                             context=locks.PROFILES["linux-portable"], status="verified",
                             inputs=locks.restore_inputs(self.root), locks={self.name: locks.digest(data)})
        self.write_manifest()

    def write_manifest(self):
        (self.candidate / "manifest.json").write_text(json.dumps(self.manifest))

    def test_adoption_copies_only_verified_profile_and_preserves_other_contexts(self):
        unrelated = self.root / locks.LOCK_ROOT / "win-arm64" / self.name
        unrelated.parent.mkdir(parents=True)
        unrelated.write_text("preserve")
        result = locks.adopt(self.root, self.candidate)
        self.assertEqual(result["status"], "adopted")
        self.assertEqual(unrelated.read_text(), "preserve")
        self.assertEqual((self.root / locks.LOCK_ROOT / "linux-portable" / self.name).read_bytes(),
                         (self.candidate / "locks" / self.name).read_bytes())

    def test_changed_restore_inputs_refuse_before_any_write(self):
        (self.root / "global.json").write_text('{"sdk":{"version":"10.0.200"}}')
        with self.assertRaisesRegex(ValueError, "inputs differ"):
            locks.adopt(self.root, self.candidate)
        self.assertFalse((self.root / locks.LOCK_ROOT).exists())

    def test_untracked_restore_inputs_cannot_escape_the_fingerprint(self):
        (self.root / "src/Example/Directory.Build.props").write_text("<Project />")
        with self.assertRaisesRegex(ValueError, "Untracked restore inputs"):
            locks.adopt(self.root, self.candidate)
        self.assertFalse((self.root / locks.LOCK_ROOT).exists())

    def test_corrupted_missing_or_unexpected_candidate_bytes_are_rejected(self):
        target = self.candidate / "locks" / self.name
        original = target.read_bytes()
        target.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "digest differs"):
            locks.adopt(self.root, self.candidate)
        target.unlink()
        with self.assertRaisesRegex(ValueError, "missing or unexpected"):
            locks.adopt(self.root, self.candidate)
        target.write_bytes(original)
        (target.parent / "extra.json").write_text("{}")
        with self.assertRaisesRegex(ValueError, "missing or unexpected"):
            locks.adopt(self.root, self.candidate)

    def test_path_traversal_or_unknown_project_is_rejected(self):
        data = (self.candidate / "locks" / self.name).read_bytes()
        (self.candidate / "locks" / self.name).unlink()
        self.manifest["locks"] = {"Other.packages.lock.json": locks.digest(data)}
        (self.candidate / "locks/Other.packages.lock.json").write_bytes(data)
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "missing solution project|Unknown project"):
            locks.adopt(self.root, self.candidate)

    def test_partial_solution_inventory_cannot_be_adopted_even_with_matching_inputs(self):
        (self.root / "src/Second").mkdir()
        (self.root / "src/Second/Second.csproj").write_text("<Project />")
        with (self.root / "Meridian.sln").open("a") as handle:
            handle.write('Project("{guid}") = "Second", "src/Second/Second.csproj", "{guid3}"\n')
        subprocess.run(["git", "add", "."], cwd=self.root, check=True)
        self.manifest["inputs"] = locks.restore_inputs(self.root)
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "missing solution project"):
            locks.adopt(self.root, self.candidate)
        self.assertFalse((self.root / locks.LOCK_ROOT).exists())

    def test_failure_manifest_cannot_be_adopted(self):
        self.manifest["status"] = "failed"
        self.write_manifest()
        with self.assertRaisesRegex(ValueError, "not a verified"):
            locks.adopt(self.root, self.candidate)

    def test_restore_modes_are_separate_and_rid_is_explicit(self):
        generate = locks.restore_command("dotnet", "win-arm64", generate=True)
        verify = locks.restore_command("dotnet", "win-arm64", generate=False)
        self.assertIn("--force-evaluate", generate)
        self.assertNotIn("--locked-mode", generate)
        self.assertIn("--locked-mode", verify)
        self.assertNotIn("--force-evaluate", verify)
        self.assertEqual(verify[-2:], ["--runtime", "win-arm64"])

    def test_wrong_native_os_is_rejected_before_clone(self):
        with patch.object(locks.platform, "system", return_value="Linux"):
            with self.assertRaisesRegex(ValueError, "requires native Windows"):
                locks.stage(self.root, "win-x64", self.root / "out", "dotnet")
        self.assertFalse((self.root / "out").exists())

    def test_existing_profile_inventory_cannot_be_silently_pruned(self):
        existing = self.root / locks.LOCK_ROOT / "linux-portable"
        existing.mkdir(parents=True)
        (existing / "Removed.packages.lock.json").write_text("retain")
        with self.assertRaisesRegex(ValueError, "inventory differs"):
            locks.adopt(self.root, self.candidate)
        self.assertEqual((existing / "Removed.packages.lock.json").read_text(), "retain")


def archive(receipt, filename="generated-repair-result.json"):
    data = io.BytesIO()
    with zipfile.ZipFile(data, "w") as output:
        output.writestr(filename, json.dumps(receipt))
    return data.getvalue()


class OutcomeTests(unittest.TestCase):
    def setUp(self):
        self.run = dict(id=42, run_attempt=2, head_sha="a" * 40, created_at="2026-10-20T01:00:00Z",
                        path=outcomes.WORKFLOW, event="workflow_run", head_branch="main",
                        conclusion="success", jobs=[
                            {"name": "Validate and publish generated repair", "conclusion": "success"},
                            {"name": "Refresh stale open PR branches after main advances", "conclusion": "skipped"},
                        ])
        self.snapshot = {"workflow_runs": [self.run], "collection": {"runDataComplete": True, "complete": False}}
        self.receipt = dict(schema_version=1, workflow_run_id=42, run_attempt=2, outcome="noop")
        self.artifact = dict(id=7, name="generated-repair-outcome-publish-42-2", expired=False,
                             size_in_bytes=200, workflow_run=dict(id=42, head_sha="a" * 40))
        outer = self

        class API:
            def artifacts(self, run_id):
                return [outer.artifact]

            def download(self, artifact):
                return archive(outer.receipt)

        self.api = API()

    def test_workflow_success_does_not_become_repair_or_authoritative_ci_success(self):
        result = outcomes.summarize(self.snapshot, self.api)
        self.assertEqual(result["outcomeCounts"], {"noop": 1})
        self.assertIsNone(result["firstRepairedRevisionCiSuccessRate"])
        self.assertIsNone(result["latestRefreshBacklog"])
        self.assertFalse(result["collectionComplete"])
        self.assertEqual(result["missingReceipts"], [])

    def test_wrong_attempt_receipt_is_unavailable_not_reused(self):
        self.receipt["run_attempt"] = 1
        result = outcomes.summarize(self.snapshot, self.api)
        self.assertEqual(result["outcomeCounts"], {})
        self.assertEqual(len(result["missingReceipts"]), 1)

    def test_trusted_dispatch_is_counted_as_request_not_publication(self):
        self.receipt["outcome"] = "trusted-generation-requested"
        result = outcomes.summarize(self.snapshot, self.api)
        self.assertEqual(result["outcomeCounts"], {"trusted-generation-requested": 1})
        self.assertIsNone(result["firstRepairedRevisionCiSuccessRate"])

    def test_untrusted_pr_or_feature_dispatch_receipts_are_unavailable(self):
        for event in ("pull_request", "workflow_dispatch"):
            with self.subTest(event=event):
                self.run.update(event=event, head_branch="feature")
                result = outcomes.summarize(self.snapshot, self.api)
                self.assertEqual(result["outcomeCounts"], {})
                self.assertEqual(len(result["missingReceipts"]), 1)

    def test_expired_or_wrong_commit_artifact_is_unavailable(self):
        for update in ({"expired": True}, {"workflow_run": {"id": 42, "head_sha": "b" * 40}}):
            with self.subTest(update=update):
                previous = dict(self.artifact)
                self.artifact.update(update)
                result = outcomes.summarize(self.snapshot, self.api)
                self.assertEqual(result["outcomeCounts"], {})
                self.assertEqual(len(result["missingReceipts"]), 1)
                self.artifact = previous

    def test_zip_path_and_extra_members_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "exact expected"):
            outcomes.decode_receipt(archive(self.receipt, "../generated-repair-result.json"), "publish", self.run)
        data = io.BytesIO()
        with zipfile.ZipFile(data, "w") as output:
            output.writestr("generated-repair-result.json", json.dumps(self.receipt))
            output.writestr("unexpected", "data")
        with self.assertRaisesRegex(ValueError, "exact expected"):
            outcomes.decode_receipt(data.getvalue(), "publish", self.run)

    def test_duplicate_attempts_cannot_inflate_counts(self):
        self.snapshot["workflow_runs"].append(self.run)
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            outcomes.summarize(self.snapshot, self.api)

    def test_latest_refresh_backlog_is_not_summed_across_scans(self):
        self.run["jobs"][0]["conclusion"] = "skipped"
        self.run["jobs"][1]["conclusion"] = "success"
        self.artifact["name"] = "generated-repair-outcome-refresh-42-2"
        self.receipt.update(outcome="refreshed", backlog=12)
        self.api.download = lambda _: archive(self.receipt, "generated-repair-refresh.json")
        result = outcomes.summarize(self.snapshot, self.api)
        self.assertEqual(result["latestRefreshBacklog"], 12)

    def test_incomplete_workflow_inventory_preserves_count_but_never_backlog_age(self):
        self.run["jobs"][0]["conclusion"] = "skipped"
        self.run["jobs"][1]["conclusion"] = "success"
        self.artifact["name"] = "generated-repair-outcome-refresh-42-2"
        self.receipt.update(outcome="refreshed", backlog=1, observed_at="2026-10-20T01:00:00Z",
                            waiting_requests=[{"pr_number": 23, "head_sha": "a" * 40, "base_sha": "b" * 40}])
        self.api.download = lambda _: archive(self.receipt, "generated-repair-refresh.json")
        self.snapshot["collection"]["runDataComplete"] = False
        result = outcomes.summarize(self.snapshot, self.api)
        self.assertEqual(result["latestRefreshBacklog"], 1)
        backlog = result["observedRefreshBacklog"]
        self.assertFalse(backlog["available"])
        self.assertIsNone(backlog["waiting"][0]["observedAgeLowerBoundSeconds"])


class MaintenanceWorkflowTests(unittest.TestCase):
    def workflow(self, name):
        return yaml.load((ROOT / ".github/workflows" / name).read_text(), Loader=yaml.BaseLoader)

    def test_python_updates_cover_every_maintained_requirements_owner(self):
        data = yaml.safe_load((ROOT / ".github/dependabot.yml").read_text())
        pip = [item for item in data["updates"] if item["package-ecosystem"] == "pip"]
        self.assertEqual(len(pip), 1)
        self.assertEqual(set(pip[0]["directories"]),
                         {"/build/scripts/ci", "/build/scripts/docs", "/tools/source_docs", "/tools/schema_control"})
        self.assertNotEqual(pip[0]["schedule"]["day"], "monday")

    def test_lock_candidates_require_native_profiles_and_read_only_dispatch(self):
        workflow = self.workflow("dependency-locks.yml")
        self.assertEqual(set(workflow["on"]), {"workflow_dispatch", "pull_request"})
        self.assertTrue({"**/*.csproj", "**/*.fsproj", "**/*.targets", "**/*.props"}
                        .issubset(workflow["on"]["pull_request"]["paths"]))
        self.assertEqual(workflow["permissions"], {"contents": "read"})
        rows = workflow["jobs"]["validate"]["strategy"]["matrix"]["include"]
        self.assertEqual({row["profile"] for row in rows}, set(locks.PROFILES))
        for row in rows:
            expected = "ubuntu-latest" if row["profile"] == "linux-portable" else "windows-latest"
            self.assertEqual(row["os"], expected)

    def test_metrics_keep_incomplete_collection_red_and_do_not_claim_comparison(self):
        workflow = self.workflow("ci-metrics.yml")
        self.assertTrue(all(value == "read" for value in workflow["permissions"].values()))
        steps = workflow["jobs"]["report"]["steps"]
        collector = next(step for step in steps if step.get("id") == "collect")
        self.assertEqual(collector["continue-on-error"], "true")
        commands = "\n".join(step.get("run", "") for step in steps)
        self.assertNotIn("--comparison", commands)
        self.assertEqual(steps[-1]["if"], "always()")
        self.assertIn("--check-collection", steps[-1]["run"])
        self.assertIn("--input artifacts/ci-metrics/runs.json", steps[-1]["run"])
        self.assertIn("timedelta(seconds=1)", commands)


if __name__ == "__main__":
    unittest.main()
