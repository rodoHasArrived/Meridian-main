"""Adversarial coverage for the generated-output writer's trust boundary."""
from __future__ import annotations

import copy
import importlib.util
import io
import hashlib
import shutil
import json
import os
import stat
import subprocess
import tempfile
import unittest
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("generated_repair", ROOT / "build/scripts/ci/generated_repair.py")
repair = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(repair)
HEAD, BASE, NEW = "1" * 40, "2" * 40, "3" * 40
REPOSITORY = "meridian/test"
POLICY = {"whole_files": ["docs/generated.md", "docs/coverage.md"], "whole_trees": ["assets"],
          "hybrid_files": {"docs/HELP.md": [["<!-- begin -->", "<!-- end -->"]]},
          "protected_suffixes": {"docs/coverage.md": "<!-- protected -->"}}
HELP = "User prose\n<!-- begin -->\nold\n<!-- end -->\nMore user prose\n"


def request(**updates):
    return dict({"pr_number": 7, "head_sha": HEAD, "base_sha": BASE, "attempt": 0, "root_sha": HEAD}, **updates)


def pull(**updates):
    value = {"number": 7, "state": "open", "merged": False,
             "head": {"sha": HEAD, "ref": "topic", "repo": {"full_name": REPOSITORY}},
             "base": {"sha": BASE, "ref": "main", "repo": {"full_name": REPOSITORY}}}
    value.update(updates)
    return value


def run_title(workflow_sha=BASE, execution_ref="refs/heads/main", **updates):
    values = request(**updates)
    return (f"generated-repair-v2 pr={values['pr_number']} head={values['head_sha']} "
            f"base={values['base_sha']} attempt={values['attempt']} root={values['root_sha']} "
            f"workflow_sha={workflow_sha} ref={execution_ref}")


def source_run(**updates):
    value = {"id": 99, "run_attempt": 1, "path": repair.WORKFLOW, "status": "completed",
             "conclusion": "success", "event": "workflow_dispatch", "head_sha": BASE, "head_branch": "main",
             "repository": {"full_name": REPOSITORY}, "head_repository": {"full_name": REPOSITORY},
             "display_title": run_title()}
    value.update(updates)
    return value


class MetadataTests(unittest.TestCase):
    def test_server_run_identity_and_attempt_are_required(self):
        for key, value in [("id", 98), ("run_attempt", 2), ("path", ".github/workflows/evil.yml"),
                           ("event", "pull_request"), ("status", "in_progress"), ("head_sha", HEAD),
                           ("repository", {"full_name": "attacker/repo"})]:
            with self.subTest(key=key), self.assertRaises(repair.RepairError):
                repair.verify_source_run(source_run(**{key: value}), 99, 1, REPOSITORY, "main")

    def test_target_metadata_can_identify_request_but_never_authorize_candidate(self):
        # Hosted REST target runs use PR head_sha. Neither that form nor the
        # formerly assumed base_sha form proves workflow execution provenance.
        for sha in (HEAD, BASE):
            run = source_run(event="pull_request_target", head_sha=sha,
                             head_branch="topic", head_repository={"full_name": "contributor/fork"})
            self.assertEqual(repair.verify_run_identity(run, 99, 1, REPOSITORY), request())
            with self.assertRaisesRegex(repair.RepairError, "cannot authorize"):
                repair.verify_source_run(run, 99, 1, REPOSITORY, "main")

    def test_dispatch_must_originate_from_default_branch(self):
        for field, value in [("head_branch", "evil"), ("head_repository", {"full_name": "attacker/repo"})]:
            with self.subTest(field=field), self.assertRaises(repair.RepairError):
                repair.verify_source_run(source_run(event="workflow_dispatch", **{field: value}),
                                         99, 1, REPOSITORY, "main")

    def test_title_cannot_smuggle_paths_or_unbounded_attempts(self):
        title = source_run()["display_title"]
        for value in [title + "\n", title.replace("attempt=0", "attempt=3"),
                      title.replace(HEAD, "--upload-pack=evil"), title.replace("pr=7", "pr=../7")]:
            with self.subTest(value=value), self.assertRaises(repair.RepairError):
                repair.parse_request(value)

    def test_old_title_requires_explicit_workflow_upgrade(self):
        with self.assertRaisesRegex(repair.RepairError, "upgrading the workflow"):
            repair.parse_request(f"generated-repair pr=7 head={HEAD} base={BASE} attempt=0 root={HEAD}")

    def test_execution_identity_is_separate_from_five_dispatch_inputs(self):
        title = run_title(workflow_sha=NEW)
        self.assertEqual(repair.parse_request(title), request())
        self.assertEqual(repair.parse_run_metadata(title)["workflow_execution_sha"], NEW)
        self.assertEqual(repair.verify_source_run(source_run(head_sha=NEW, display_title=title),
                                                 99, 1, REPOSITORY, "main"), request())
        api = mock.Mock()
        repair.dispatch_retry(api, repair.parse_request(title), "main")
        self.assertEqual(set(api.request.call_args.args[2]["inputs"]), set(repair.DISPATCH_INPUTS))
        with self.assertRaisesRegex(repair.RepairError, "exactly the five"):
            repair.dispatch_retry(api, dict(request(), workflow_execution_sha=NEW), "main")

    def test_spoofed_execution_sha_and_ref_are_rejected(self):
        for title in (run_title(workflow_sha=HEAD),
                      run_title(execution_ref="refs/heads/topic"),
                      run_title(execution_ref="refs/heads/main/../topic"),
                      run_title(execution_ref="refs/tags/main"),
                      run_title() + " workflow_sha=" + HEAD):
            with self.subTest(title=title), self.assertRaises(repair.RepairError):
                repair.verify_source_run(source_run(display_title=title), 99, 1, REPOSITORY, "main")

    def test_tag_named_main_cannot_impersonate_default_branch_dispatch(self):
        run = source_run(head_branch="main", display_title=run_title(execution_ref="refs/tags/main"))
        with self.assertRaisesRegex(repair.RepairError, "default-branch workflow execution"):
            repair.verify_source_run(run, 99, 1, REPOSITORY, "main")

    def test_both_head_and_base_must_match_open_default_branch_pr(self):
        self.assertTrue(repair.request_current(pull(), request(), REPOSITORY, "main"))
        for field in ("head", "base"):
            value = pull()
            value[field]["sha"] = NEW
            self.assertFalse(repair.request_current(value, request(), REPOSITORY, "main"))
        self.assertFalse(repair.request_current(pull(state="closed"), request(), REPOSITORY, "main"))

    def test_retry_budget_and_forks_do_not_loop(self):
        changed = pull()
        changed["base"]["sha"] = NEW
        result = repair.stale_result(changed, request(), REPOSITORY, "main")
        self.assertEqual(result["status"], "retry")
        self.assertEqual(result["request"]["base_sha"], NEW)
        self.assertEqual(result["request"]["attempt"], 1)
        self.assertEqual(repair.stale_result(changed, request(attempt=2), REPOSITORY, "main")["status"], "manual")
        changed["head"]["repo"]["full_name"] = "contributor/fork"
        self.assertEqual(repair.stale_result(changed, request(), REPOSITORY, "main")["status"], "manual")

    def archive(self, members):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            for name, content in members:
                archive.writestr(name, content)
        return stream.getvalue()

    def test_artifact_accepts_only_exact_expected_members(self):
        files = repair.read_artifact(self.archive([("repair.patch", b""), ("report.json", b"{}")]))
        self.assertEqual(set(files), {"repair.patch", "report.json"})
        for name in ("../report.json", "/report.json", "bin/executable", "repair.patch/child"):
            with self.subTest(name=name), self.assertRaises(repair.RepairError):
                repair.read_artifact(self.archive([("repair.patch", b""), ("report.json", b"{}"), (name, b"evil")]))

    def test_artifact_rejects_symlink_and_duplicate_members(self):
        link = zipfile.ZipInfo("report.json")
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        with self.assertRaises(repair.RepairError):
            repair.read_artifact(self.archive([("repair.patch", b""), (link, b"/etc/passwd")]))
        with self.assertWarns(UserWarning), self.assertRaises(repair.RepairError):
            repair.read_artifact(self.archive([("repair.patch", b""), ("report.json", b"{}"), ("report.json", b"{}")]))

    def test_artifact_limits_uncompressed_size(self):
        with mock.patch.object(repair, "MAX_ARTIFACT", 2), self.assertRaises(repair.RepairError):
            repair.read_artifact(self.archive([("repair.patch", b"abc"), ("report.json", b"{}")]))


class GitBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "-b", "main")
        self.git("config", "user.name", "Repair test")
        self.git("config", "user.email", "repair@example.invalid")
        self.git("config", "commit.gpgsign", "false")
        self.write("docs/generated.md", "old\n")
        self.write("docs/HELP.md", HELP)
        self.write("src/code.py", "original\n")
        self.write("assets/old.js", "old asset\n")
        self.git("add", "-A")
        self.git("commit", "-m", "base")
        self.head = self.git("rev-parse", "HEAD").decode().strip()

    def git(self, *args):
        return repair.git(self.repo, *args)

    def write(self, name, text):
        path = self.repo / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def tree(self):
        self.git("add", "-A")
        return self.git("write-tree").decode().strip()

    def violations(self):
        return repair.validate_delta(self.repo, self.head, self.tree(), POLICY)

    def test_generated_changes_and_deleted_hashed_assets_are_allowed(self):
        self.write("docs/generated.md", "new\n")
        (self.repo / "assets/old.js").unlink()
        self.write("assets/new.js", "new asset\n")
        self.assertEqual(self.violations(), [])

    def test_source_changes_cannot_hide_in_full_patch(self):
        self.write("src/code.py", "injected code\n")
        patch = self.git("diff", "--binary", "HEAD")
        before = self.git("status", "--porcelain", "-z")
        tree = repair.apply_patch_tree(self.repo, self.head, patch)
        self.assertEqual(repair.validate_delta(self.repo, self.head, tree, POLICY), ["src/code.py"])
        self.assertEqual(self.git("status", "--porcelain", "-z"), before)

    def test_existing_head_prose_is_preserved_while_blocks_change(self):
        self.write("docs/HELP.md", HELP.replace("User prose", "Reviewed PR prose"))
        self.git("add", "-A")
        self.git("commit", "-m", "reviewed prose change")
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        self.write("docs/HELP.md", HELP.replace("User prose", "Reviewed PR prose").replace("old", "generated new"))
        self.assertEqual(self.violations(), [])
        self.write("docs/HELP.md", HELP.replace("User prose", "Injected prose").replace("old", "generated new"))
        self.assertEqual(self.violations(), ["docs/HELP.md"])

    def test_generated_markers_cannot_expand_authority(self):
        for content in [HELP.replace("<!-- begin -->", ""), HELP + "<!-- begin -->", HELP.replace("<!-- end -->", "<!-- begin -->")]:
            with self.subTest(content=content):
                self.write("docs/HELP.md", content)
                self.assertEqual(self.violations(), ["docs/HELP.md"])

    def test_hybrid_file_cannot_be_deleted(self):
        (self.repo / "docs/HELP.md").unlink()
        self.assertEqual(self.violations(), ["docs/HELP.md"])

    def test_whole_generated_output_cannot_become_executable_or_symlink(self):
        path = self.repo / "docs/generated.md"
        path.chmod(0o755)
        self.assertEqual(self.violations(), ["docs/generated.md"])
        path.unlink()
        path.symlink_to("../src/code.py")
        self.assertEqual(self.violations(), ["docs/generated.md"])

    def test_protected_suffix_cannot_be_modified_or_removed(self):
        self.write("docs/coverage.md", "generated\n<!-- protected --> human report\n")
        self.git("add", "-A")
        self.git("commit", "-m", "coverage suffix")
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        self.write("docs/coverage.md", "new generated\n<!-- protected --> human report\n")
        self.assertEqual(self.violations(), [])
        self.write("docs/coverage.md", "new generated\n")
        self.assertEqual(self.violations(), ["docs/coverage.md"])

    def test_path_traversal_patch_never_materializes_on_disk(self):
        patch = b"diff --git a/../escaped b/../escaped\nnew file mode 100644\n--- /dev/null\n+++ b/../escaped\n@@ -0,0 +1 @@\n+bad\n"
        with self.assertRaises(repair.RepairError):
            repair.apply_patch_tree(self.repo, self.head, patch)
        self.assertFalse((self.repo.parent / "escaped").exists())

    def test_unsafe_modes_are_rejected_before_merge_helper_loads(self):
        (self.repo / "link").symlink_to("/tmp/target")
        self.git("add", "-A")
        self.git("commit", "-m", "unsafe link")
        head = self.git("rev-parse", "HEAD").decode().strip()
        with mock.patch.object(repair, "import_trusted") as load, self.assertRaises(repair.RepairError):
            repair.recreate_merge(self.repo, head, self.head)
        load.assert_not_called()

    def test_bot_loop_guard_allows_new_base(self):
        self.git("commit", "--allow-empty", "-m", repair.REPAIR_MARKER)
        bot = self.git("rev-parse", "HEAD").decode().strip()
        req = request(head_sha=bot, base_sha=self.head)
        self.assertTrue(repair.loop_suppressed(self.repo, req))
        self.assertFalse(repair.loop_suppressed(self.repo, dict(req, attempt=1)))
        self.git("checkout", "-b", "base-new", self.head)
        self.write("src/new.py", "new main source")
        self.git("add", "-A")
        self.git("commit", "-m", "main advance")
        latest = self.git("rev-parse", "HEAD").decode().strip()
        self.assertFalse(repair.loop_suppressed(self.repo, dict(req, base_sha=latest)))


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.api = mock.Mock(repository=REPOSITORY, token="app-secret")
        self.pr = pull()
        self.api.get.side_effect = lambda path: copy.deepcopy(self.pr)
        self.prepared = {"status": "ready", "request": request(), "branch": "topic", "tree_sha": "4" * 40}
        self.git_calls = []

    def fake_git(self, root, *args, **kwargs):
        self.git_calls.append((args, kwargs))
        if args[0] == "merge-base":
            return BASE.encode()
        if args[0] == "commit-tree":
            return NEW.encode()
        if args[0] == "push":
            self.pr["head"]["sha"] = NEW
        return b""

    def test_publish_uses_nonforce_push_no_candidate_execution_and_app_auth(self):
        with mock.patch.object(repair, "git", side_effect=self.fake_git):
            result = repair.publish(Path("."), self.api, self.prepared, "main")
        self.assertEqual(result["status"], "published")
        push = next((args, kwargs) for args, kwargs in self.git_calls if args[0] == "push")
        self.assertEqual(push[0], ("push", f"https://github.com/{REPOSITORY}.git", f"{NEW}:refs/heads/topic"))
        self.assertNotIn("app-secret", " ".join(push[0]))
        self.assertEqual(push[1]["env"]["GENERATED_REPAIR_PUSH_TOKEN"], "app-secret")
        self.assertEqual([args[0] for args, _ in self.git_calls], ["merge-base", "commit-tree", "push"])
        self.assertEqual(self.api.get.call_count, 3)

    def test_stale_head_never_gets_a_commit(self):
        self.pr["head"]["sha"] = NEW
        with mock.patch.object(repair, "git") as git:
            result = repair.publish(Path("."), self.api, self.prepared, "main")
        git.assert_not_called()
        self.assertEqual(result["status"], "retry")
        self.assertEqual(self.api.request.call_args.args[2]["inputs"]["attempt"], "1")

    def test_initial_trusted_dispatch_uses_current_refs_without_spending_retry_budget(self):
        self.pr["head"]["sha"], self.pr["base"]["sha"] = NEW, "4" * 40
        prepared = {"status": "retry", "outcome": "trusted-generation-requested",
                    "request": request()}
        with mock.patch.object(repair, "git") as git:
            result = repair.publish(Path("."), self.api, prepared, "main")
        git.assert_not_called()
        sent = self.api.request.call_args.args[2]
        self.assertEqual(sent["ref"], "main")
        self.assertEqual(sent["inputs"], {"pr_number": "7", "head_sha": NEW,
                                         "base_sha": "4" * 40, "attempt": "0", "root_sha": NEW})
        self.assertEqual(result["outcome"], "trusted-generation-requested")
        self.assertIn("Dispatched", result["reason"])

    def test_initial_trusted_dispatch_refuses_retargeted_or_fork_request(self):
        prepared = {"status": "retry", "outcome": "trusted-generation-requested",
                    "request": request()}
        for section, key, value in (("base", "ref", "topic"), ("head", "repo", {"full_name": "other/fork"})):
            self.pr = pull()
            self.pr[section][key] = value
            with self.subTest(section=section), mock.patch.object(repair, "git") as git:
                result = repair.publish(Path("."), self.api, prepared, "main")
            self.assertEqual(result["outcome"], "ineligible")
            git.assert_not_called()
        self.api.request.assert_not_called()

    def test_base_advance_in_push_window_requeues_actual_new_head(self):
        def push_advance(root, *args, **kwargs):
            result = self.fake_git(root, *args, **kwargs)
            if args[0] == "push":
                self.pr["base"]["sha"] = "5" * 40
            return result
        with mock.patch.object(repair, "git", side_effect=push_advance):
            result = repair.publish(Path("."), self.api, self.prepared, "main")
        self.assertEqual(result["status"], "retry")
        payload = self.api.request.call_args.args[2]["inputs"]
        self.assertEqual(payload["head_sha"], NEW)
        self.assertEqual(payload["base_sha"], "5" * 40)
        self.assertEqual(payload["attempt"], "1")

    def test_non_fast_forward_rejection_requeues_without_force(self):
        def rejected_push(root, *args, **kwargs):
            if args[0] == "push":
                self.pr["head"]["sha"] = "6" * 40
                raise repair.RepairError("non-fast-forward")
            return self.fake_git(root, *args, **kwargs)
        with mock.patch.object(repair, "git", side_effect=rejected_push):
            result = repair.publish(Path("."), self.api, self.prepared, "main")
        self.assertEqual(result["status"], "retry")
        self.assertEqual(result["request"]["head_sha"], "6" * 40)

    def test_refresh_bounded_fanout_and_duplicate_pair_suppression(self):
        pulls = []
        for number in range(1, 14):
            pr = pull(number=number, created_at="2020-01-01T00:00:00Z")
            pulls.append(pr)
        existing = source_run(display_title=run_title(pr_number=1))
        def get(path):
            if path.startswith("/branches/"):
                return {"commit": {"sha": BASE}}
            if path.startswith("/actions/"):
                return {"workflow_runs": [existing]}
            if path.startswith("/pulls?"):
                return pulls
            return {"merge_base_commit": {"sha": "7" * 40}}
        self.api.get.side_effect = get
        with mock.patch.object(repair, "datetime") as clock:
            clock.now.return_value = datetime(2026, 10, 20, 12, 34, 56, tzinfo=timezone.utc)
            result = repair.refresh(self.api, "main")
            clock.now.assert_called_once_with(timezone.utc)
        self.assertEqual(result["dispatched"], 10)
        self.assertEqual(result["backlog"], 2)
        numbers = [call.args[2]["inputs"]["pr_number"] for call in self.api.request.call_args_list]
        self.assertNotIn("1", numbers)
        self.assertEqual(len(numbers), 10)
        self.assertEqual(result["observed_at"], "2026-10-20T12:34:56Z")
        self.assertEqual(result["waiting_requests"], [
            {"pr_number": number, "head_sha": HEAD, "base_sha": BASE} for number in (12, 13)])
        self.assertEqual(result["backlog"], len(result["waiting_requests"]))
        self.assertNotIn("created_at", json.dumps(result))
        for call in self.api.request.call_args_list:
            self.assertEqual(set(call.args[2]["inputs"]), set(repair.DISPATCH_INPUTS))

    def test_refresh_empty_backlog_has_no_waiting_requests(self):
        def get(path):
            if path.startswith("/branches/"):
                return {"commit": {"sha": BASE}}
            if path.startswith("/actions/"):
                return {"workflow_runs": []}
            if path.startswith("/pulls?"):
                return [pull()]
            return {"merge_base_commit": {"sha": "7" * 40}}
        self.api.get.side_effect = get
        with mock.patch.object(repair, "datetime") as clock:
            clock.now.return_value = datetime(2026, 10, 20, 12, 34, 56, tzinfo=timezone.utc)
            result = repair.refresh(self.api, "main")
        self.assertEqual(result["dispatched"], 1)
        self.assertEqual(result["backlog"], 0)
        self.assertEqual(result["waiting_requests"], [])
        self.assertEqual(result["observed_at"], "2026-10-20T12:34:56Z")


class InspectIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.fixture = GitBoundaryTests("test_generated_changes_and_deleted_hashed_assets_are_allowed")
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.repo, self.git, self.write = self.fixture.repo, self.fixture.git, self.fixture.write
        for name in ["repair-generated.py", "resolve-generated-merge-conflicts.py", "merge-reviewed-doc-hashes.py"]:
            target = self.repo / "build/scripts" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / "build/scripts" / name, target)
        self.write(repair.WORKFLOW, (ROOT / repair.WORKFLOW).read_text())
        self.git("add", "-A")
        self.git("commit", "-m", "trusted helpers")
        self.git("checkout", "-b", "topic")
        self.write("docs/HELP.md", HELP.replace("User prose", "Reviewed head prose"))
        self.git("add", "-A")
        self.git("commit", "-m", "feature prose")
        self.head = self.git("rev-parse", "HEAD").decode().strip()
        self.git("checkout", "main")
        self.write("src/base.py", "legitimate merged main source\n")
        self.git("add", "-A")
        self.git("commit", "-m", "main advances")
        self.base = self.git("rev-parse", "HEAD").decode().strip()
        self.git("remote", "add", "origin", str(self.repo))
        self.git("update-ref", "refs/pull/7/head", self.head)
        self.baseline = repair.recreate_merge(self.repo, self.head, self.base)
        self.candidate = self.with_blob(self.baseline, "docs/generated.md", b"converged generated output\n")
        self.patch = self.git("diff", "--binary", "--full-index", self.head, self.candidate)
        self.report = {"successful": True, "head_sha": self.head, "base_sha": self.base,
                       "patch_sha256": hashlib.sha256(self.patch).hexdigest()}
        self.run = source_run(head_sha=self.base,
            display_title=run_title(head_sha=self.head, base_sha=self.base, root_sha=self.head,
                                   workflow_sha=self.base))
        self.pr = pull()
        self.pr["head"]["sha"], self.pr["base"]["sha"] = self.head, self.base
        self.artifact = {"id": 5, "name": "generated-repair-99-1", "expired": False, "size_in_bytes": 4096,
                         "workflow_run": {"id": 99, "head_sha": self.base}}
        self.api = mock.Mock(repository=REPOSITORY)
        def get(path):
            if path == "/actions/runs/99/attempts/1":
                return copy.deepcopy(self.run)
            if path == "/pulls/7":
                return copy.deepcopy(self.pr)
            if path == "/actions/runs/99/artifacts?per_page=100":
                return {"artifacts": [copy.deepcopy(self.artifact)]}
            self.fail("Unexpected API endpoint: " + path)
        self.api.get.side_effect = get
        self.api.download.side_effect = lambda _: MetadataTests.archive(self, [
            ("repair.patch", self.patch), ("report.json", json.dumps(self.report).encode())])

    def with_blob(self, before, path, content):
        with tempfile.TemporaryDirectory() as temp:
            env = {"GIT_INDEX_FILE": str(Path(temp) / "index")}
            repair.git(self.repo, "read-tree", before, env=env)
            blob = repair.git(self.repo, "hash-object", "-w", "--stdin", data=content).decode().strip()
            repair.git(self.repo, "update-index", "--add", "--cacheinfo", f"100644,{blob},{path}", env=env)
            return repair.git(self.repo, "write-tree", env=env).decode().strip()

    def inspect(self):
        with mock.patch.object(repair, "load_policy", return_value=POLICY):
            return repair.inspect(self.repo, self.api, 99, 1, "main")

    def test_pinned_merge_and_generated_patch_pass_without_source_or_prose_loss(self):
        result = self.inspect()
        self.assertEqual(result["status"], "ready")
        self.assertEqual(result["tree_sha"], self.candidate)
        self.assertEqual(result["execution_ref"], "refs/heads/main")
        self.assertEqual(result["workflow_ref"], f"{REPOSITORY}/{repair.WORKFLOW}@refs/heads/main")
        self.assertTrue(result["workflow_ref_derived"])
        self.assertEqual(self.git("show", f"{self.candidate}:src/base.py"), b"legitimate merged main source\n")
        self.assertIn(b"Reviewed head prose", self.git("show", f"{self.candidate}:docs/HELP.md"))
        self.assertEqual(self.git("rev-parse", "HEAD").decode().strip(), self.base)

    def test_artifact_provenance_mismatches_are_rejected(self):
        for field, value in [("id", 100), ("head_sha", HEAD)]:
            with self.subTest(field=field):
                original = self.artifact["workflow_run"][field]
                self.artifact["workflow_run"][field] = value
                with self.assertRaisesRegex(repair.RepairError, "provenance"):
                    self.inspect()
                self.artifact["workflow_run"][field] = original
        self.api.download.assert_not_called()

    def test_report_head_base_and_digest_are_independently_verified(self):
        for field in ("head_sha", "base_sha", "patch_sha256"):
            with self.subTest(field=field):
                original = self.report[field]
                self.report[field] = "f" * 40
                with self.assertRaisesRegex(repair.RepairError, "report does not match"):
                    self.inspect()
                self.report[field] = original

    def test_validly_bound_patch_still_cannot_inject_source(self):
        bad = self.with_blob(self.candidate, "src/base.py", b"injected source\n")
        self.patch = self.git("diff", "--binary", "--full-index", self.head, bad)
        self.report["patch_sha256"] = hashlib.sha256(self.patch).hexdigest()
        with self.assertRaisesRegex(repair.RepairError, "unauthorized.*src/base.py"):
            self.inspect()

    def test_changed_trusted_workflow_bytes_reject_source_run(self):
        self.write(repair.WORKFLOW, "different current trusted workflow\n")
        with self.assertRaisesRegex(repair.RepairError, "Source workflow differs"):
            self.inspect()
        self.api.download.assert_not_called()

    def test_forks_get_manual_artifact_fallback_without_downloading_candidate(self):
        self.pr["head"]["repo"]["full_name"] = "contributor/fork"
        self.assertEqual(self.inspect()["status"], "manual")
        self.api.download.assert_not_called()


if __name__ == "__main__":
    unittest.main()
