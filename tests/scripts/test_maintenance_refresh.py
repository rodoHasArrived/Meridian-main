from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import yaml


ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / "build/scripts/ci"
sys.path.insert(0, str(SCRIPTS))
SPEC = importlib.util.spec_from_file_location("maintenance_refresh", SCRIPTS / "maintenance_refresh.py")
refresh = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(refresh)


class MaintenanceRefreshTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "repo"
        self.root.mkdir()
        self.git("init", "-b", "main")
        self.git("config", "user.name", "Test")
        self.git("config", "user.email", "test@example.invalid")
        for name in refresh.OUTPUTS:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("baseline\n")
        (self.root / "authored.md").write_text("reviewed documentation\n")
        self.git("add", ".")
        self.git("commit", "-m", "baseline")
        self.base = self.git("rev-parse", "HEAD")
        self.artifact = Path(self.temporary.name) / "artifact"
        self.artifact.mkdir()

    def git(self, *arguments):
        return refresh.git(self.root, *arguments).decode().strip()

    def artifact_for(self, name=None, text="refreshed\n"):
        name = name or refresh.OUTPUTS[0]
        (self.root / name).write_text(text)
        data = refresh.git(self.root, "diff", "--binary", "--full-index", self.base)
        report = {"schema_version": 1, "base_sha": self.base,
                  "paths": [name], "patch_sha256": hashlib.sha256(data).hexdigest(),
                  "freshness_max_age_days": 14}
        (self.artifact / "maintenance.patch").write_bytes(data)
        (self.artifact / "report.json").write_text(json.dumps(report))
        self.git("restore", ".")
        return report

    def test_patch_applies_only_to_private_index_and_preserves_authored_content(self):
        self.artifact_for()
        tree, paths = refresh.prepare_tree(self.root, self.artifact, self.base)
        self.assertEqual([refresh.OUTPUTS[0]], paths)
        self.assertEqual("refreshed", self.git("show", f"{tree}:{paths[0]}"))
        self.assertEqual("reviewed documentation", self.git("show", f"{tree}:authored.md"))
        self.assertEqual("", self.git("status", "--porcelain"))

    def test_writer_rejects_authored_content_even_when_artifact_claims_it(self):
        self.artifact_for("authored.md")
        with self.assertRaisesRegex(ValueError, "unauthorized"):
            refresh.prepare_tree(self.root, self.artifact, self.base)

    def test_writer_rejects_wrong_base_digest_path_list_and_freshness_policy(self):
        report = self.artifact_for()
        for field, invalid in (("base_sha", "0" * 40), ("patch_sha256", "0" * 64),
                               ("paths", []), ("freshness_max_age_days", 90)):
            with self.subTest(field=field):
                (self.artifact / "report.json").write_text(json.dumps(dict(report, **{field: invalid})))
                with self.assertRaises(ValueError):
                    refresh.prepare_tree(self.root, self.artifact, self.base)

    def test_policy_rejects_symlinks_executables_and_removed_outputs(self):
        name = refresh.OUTPUTS[0]
        for mode in ("120000", "100755", "delete"):
            with self.subTest(mode=mode):
                self.git("read-tree", self.base)
                if mode == "delete":
                    self.git("update-index", "--force-remove", name)
                else:
                    blob = self.git("rev-parse", f"{self.base}:{name}")
                    self.git("update-index", "--cacheinfo", f"{mode},{blob},{name}")
                tree = self.git("write-tree")
                with self.assertRaisesRegex(ValueError, "regular file"):
                    refresh.validate_paths(self.root, self.base, tree)
        self.git("read-tree", self.base)

    def test_existing_maintenance_branch_with_handwritten_changes_is_rejected(self):
        (self.root / "authored.md").write_text("human branch edit\n")
        self.git("add", ".")
        self.git("commit", "-m", "human edit")
        previous = self.git("rev-parse", "HEAD")
        tree = self.git("rev-parse", f"{self.base}^{{tree}}")
        with self.assertRaisesRegex(ValueError, "unauthorized"):
            refresh.make_commit(self.root, self.base, tree, previous)

    def test_synthesized_merge_preserves_latest_main_and_fast_forwards_existing_branch(self):
        (self.root / refresh.OUTPUTS[0]).write_text("old refresh\n")
        self.git("add", ".")
        self.git("commit", "-m", "old refresh")
        previous = self.git("rev-parse", "HEAD")
        self.git("checkout", "--detach", self.base)
        (self.root / "authored.md").write_text("new reviewed main content\n")
        self.git("add", ".")
        self.git("commit", "-m", "advance main")
        self.base = self.git("rev-parse", "HEAD")
        self.artifact_for()
        tree, _ = refresh.prepare_tree(self.root, self.artifact, self.base)
        commit = refresh.make_commit(self.root, self.base, tree, previous)
        self.assertEqual(f"{previous} {self.base}", self.git("show", "-s", "--format=%P", commit))
        self.git("merge-base", "--is-ancestor", previous, commit)
        self.assertEqual("new reviewed main content", self.git("show", f"{commit}:authored.md"))
        self.assertEqual("refreshed", self.git("show", f"{commit}:{refresh.OUTPUTS[0]}"))

    def test_branch_races_stop_before_a_push(self):
        self.artifact_for()
        for heads in (("f" * 40,), (self.base, None, self.base, "e" * 40)):
            with self.subTest(heads=heads), patch.dict(os.environ, {"GH_TOKEN": "test-token"}):
                with patch.object(refresh, "remote_head", side_effect=heads):
                    with patch.object(refresh, "git", wraps=refresh.git) as calls:
                        with self.assertRaisesRegex(ValueError, "advanced"):
                            refresh.publish(self.root, self.artifact, self.base, "main", "owner/repo")
                self.assertFalse(any("push" in call.args for call in calls.call_args_list))

    def test_missing_app_token_has_explicit_manual_fallback(self):
        with patch.dict(os.environ, {}, clear=True):
            with self.assertRaisesRegex(ValueError, "Download the maintenance patch"):
                refresh.publish(self.root, self.artifact, self.base, "main", "owner/repo")

    def test_publication_pushes_real_commit_without_force_and_opens_pr(self):
        remote = Path(self.temporary.name) / "remote.git"
        subprocess.run(["git", "init", "--bare", str(remote)], check=True, capture_output=True)
        self.git("remote", "add", "origin", str(remote))
        self.git("push", "origin", "main")
        self.artifact_for()
        original_run = subprocess.run
        api_calls = []

        def run(arguments, **options):
            if arguments[0] == "gh":
                api_calls.append((arguments, options))
                response = "[]" if "GET" in arguments else '{"html_url":"https://github.com/owner/repo/pull/1"}'
                return subprocess.CompletedProcess(arguments, 0, response, "")
            return original_run(arguments, **options)

        with patch.dict(os.environ, {"GH_TOKEN": "test-token"}), patch.object(refresh.subprocess, "run", side_effect=run):
            result = refresh.publish(self.root, self.artifact, self.base, "main", "owner/repo")
        self.assertEqual("published", result["status"])
        self.assertEqual(result["head_sha"], refresh.remote_head(self.root, refresh.BRANCH, os.environ))
        self.assertEqual(self.base, self.git("show", "-s", "--format=%P", result["head_sha"]))
        self.assertEqual(2, len(api_calls))
        self.assertEqual(refresh.BRANCH, json.loads(api_calls[-1][1]["input"])["head"])

    def test_stale_revision_requeues_once_then_fails_explicitly(self):
        with patch.object(refresh, "publish", side_effect=refresh.StaleBranch("base advanced")):
            with patch.object(refresh.subprocess, "run") as run:
                result = refresh.publish_with_retry(self.root, self.artifact, self.base, "main", "owner/repo", 0)
                self.assertEqual("retry-queued", result["status"])
                self.assertEqual({"ref": "main", "inputs": {"retry_count": "1"}}, json.loads(run.call_args.kwargs["input"]))
            with patch.object(refresh.subprocess, "run") as run:
                with self.assertRaisesRegex(ValueError, "retry exhausted"):
                    refresh.publish_with_retry(self.root, self.artifact, self.base, "main", "owner/repo", 1)
                run.assert_not_called()

    def test_generation_rejects_missing_required_output_before_running_generators(self):
        self.git("rm", refresh.OUTPUTS[0])
        self.git("commit", "-m", "remove output")
        with self.assertRaisesRegex(ValueError, "must already be a tracked regular file"):
            refresh.generate(self.root, self.artifact)

    def test_generation_refuses_dirty_input(self):
        (self.root / "authored.md").write_text("work in progress\n")
        with self.assertRaisesRegex(ValueError, "clean checkout"):
            refresh.generate(self.root, self.artifact)


class MaintenanceWorkflowTests(unittest.TestCase):
    def workflow(self, name):
        return yaml.safe_load((ROOT / ".github/workflows" / name).read_text())

    def test_scheduled_and_manual_runs_cannot_be_cancelled_by_main_pushes(self):
        for name in ("ci.yml", "maintenance.yml"):
            with self.subTest(name=name):
                concurrency = self.workflow(name)["concurrency"]
                self.assertIn("github.event_name", concurrency["group"])
                self.assertIn("github.run_id", concurrency["group"])
                self.assertIn("github.event_name != 'schedule'", concurrency["cancel-in-progress"])
                self.assertIn("github.event_name != 'workflow_dispatch'", concurrency["cancel-in-progress"])

    def test_only_proven_duplicates_defer_to_canonical_lanes_on_pr_push(self):
        steps = {step["name"]: step for step in self.workflow("maintenance.yml")["jobs"]["workflow-hygiene"]["steps"]}
        canonical = (ROOT / "scripts/ci.sh").read_text()
        for step, command in (
            ("Run Meridian workflow hygiene checks", "check-workflow-hygiene.py"),
            ("Validate AI contract drift", "check-ai-contract-drift.py"),
            ("Validate AI navigation freshness", "check-ai-navigation-freshness.py"),
        ):
            with self.subTest(step=step):
                self.assertEqual("github.event_name == 'schedule' || github.event_name == 'workflow_dispatch'", steps[step]["if"])
                self.assertIn(command, canonical)
        for name in ("Verify tooling metadata integrity", "Validate workflow syntax with actionlint"):
            self.assertNotIn("if", steps[name])

    def test_writer_is_separate_pinned_default_branch_job_and_uses_app(self):
        workflow = self.workflow("ai-navigation-refresh.yml")
        self.assertEqual({"contents": "read"}, workflow["permissions"])
        jobs = workflow["jobs"]
        for name in ("refresh", "publish"):
            self.assertIn("github.event.repository.default_branch", jobs[name]["if"])
            checkout = next(s for s in jobs[name]["steps"] if s.get("uses", "").startswith("actions/checkout@"))
            self.assertFalse(checkout["with"]["persist-credentials"])
            self.assertEqual("${{ github.sha }}", checkout["with"]["ref"])
        self.assertNotIn("secrets.", json.dumps(jobs["refresh"]))
        self.assertEqual("refresh", jobs["publish"]["needs"])
        self.assertEqual("generated-repair", jobs["publish"]["environment"])
        token = next(s for s in jobs["publish"]["steps"] if s.get("uses", "").startswith("actions/create-github-app-token@"))
        self.assertEqual("write", token["with"]["permission-contents"])
        self.assertEqual("write", token["with"]["permission-pull-requests"])
        self.assertEqual("${{ github.event.repository.name }}", token["with"]["repositories"])


if __name__ == "__main__":
    unittest.main()
