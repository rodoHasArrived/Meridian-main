"""Operational outcomes must explain refusal without widening write authority."""
from __future__ import annotations

import copy
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from tests.scripts.test_generated_repair import BASE, HEAD, REPOSITORY, pull, repair, request, source_run


def labelled(**updates):
    return pull(labels=[{"name": repair.REPAIR_LABEL}], **updates)


def ci_api(responses, stale_heads=()):
    """A mock API: queued responses, plus Meridian CI lookups that flag only stale_heads."""
    queue = list(responses)
    api = mock.Mock(repository=REPOSITORY)
    looked_up = []

    def get(path):
        if path.startswith(f"/actions/workflows/{repair.CI_WORKFLOW}/runs?"):
            head = path.split("head_sha=", 1)[1].split("&", 1)[0]
            looked_up.append(head)
            return {"workflow_runs": [{"id": 500, "run_number": 3, "run_attempt": 1,
                                       "head_sha": head, "status": "completed"}]}
        if path.startswith("/actions/runs/500/attempts/1/jobs"):
            conclusion = "failure" if looked_up[-1] in stale_heads else "skipped"
            return {"jobs": [{"name": repair.CI_DOCS_JOB,
                              "steps": [{"name": repair.STALE_STEP, "conclusion": conclusion}]}]}
        return queue.pop(0)

    api.get.side_effect = get
    return api


class RepairOutcomeTests(unittest.TestCase):
    def test_unlabelled_or_draft_target_events_never_request_generation(self):
        for pr in (pull(), labelled(draft=True)):
            api = mock.Mock(repository=REPOSITORY)
            api.get.side_effect = [source_run(event="pull_request_target", head_sha=HEAD), pr]
            with self.subTest(pr=pr), mock.patch.object(repair, "fetch_pr") as fetch, \
                 mock.patch.object(repair, "git") as git:
                result = repair.inspect(Path("."), api, 99, 1, "main")
            self.assertEqual(result["status"], "noop")
            self.assertIn("ready for review" if pr.get("draft") else repair.REPAIR_LABEL, result["reason"])
            fetch.assert_not_called()
            git.assert_not_called()
            api.request.assert_not_called()

    def test_ci_signal_requests_generation_only_when_verify_docs_flagged_stale_outputs(self):
        for stale, expected in ((True, "retry"), (False, "noop")):
            api = ci_api([source_run(event="workflow_run", head_sha=BASE), pull()],
                         stale_heads={HEAD} if stale else ())
            with self.subTest(stale=stale), mock.patch.object(repair, "fetch_pr"), \
                 mock.patch.object(repair, "loop_suppressed", return_value=False):
                result = repair.inspect(Path("."), api, 99, 1, "main")
            self.assertEqual(result["status"], expected)
            api.request.assert_not_called()

    def test_ci_signal_defers_to_the_label_and_skips_drafts(self):
        for pr in (labelled(), pull(draft=True)):
            api = ci_api([source_run(event="workflow_run", head_sha=BASE), pr], stale_heads={HEAD})
            with self.subTest(pr=pr):
                result = repair.inspect(Path("."), api, 99, 1, "main")
            self.assertEqual(result["status"], "noop")

    def test_ci_signal_prepare_rebinds_base_and_ignores_superseded_heads(self):
        moved = pull()
        moved["base"]["sha"] = "4" * 40
        superseded = pull()
        superseded["head"]["sha"] = "5" * 40
        for pr, expected in ((moved, "request"), (superseded, "noop")):
            api = ci_api([source_run(event="workflow_run", head_sha=BASE), pr], stale_heads={HEAD})
            with self.subTest(expected=expected), tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp, \
                 mock.patch.dict(os.environ, {"TRUSTED_DISPATCH_CONFIGURED": "true"}), \
                 mock.patch.object(repair, "fetch_pr") as fetch, \
                 mock.patch.object(repair, "loop_suppressed", return_value=False):
                result = repair.prepare_generation(Path("."), api, 99, "main", Path(temp) / "result.json")
            self.assertEqual(result["status"], expected)
            if expected == "request":
                fetch.assert_called_once_with(Path("."), 7, HEAD, "4" * 40)
            else:
                fetch.assert_not_called()

    def test_ci_stale_lookup_reads_only_the_latest_completed_run_for_the_head(self):
        api = mock.Mock(repository=REPOSITORY)
        runs = [{"id": 1, "run_number": 4, "run_attempt": 1, "head_sha": HEAD, "status": "completed"},
                {"id": 2, "run_number": 4, "run_attempt": 2, "head_sha": HEAD, "status": "completed"},
                {"id": 3, "run_number": 9, "run_attempt": 1, "head_sha": BASE, "status": "completed"}]
        api.get.side_effect = [{"workflow_runs": runs}, {"jobs": [
            {"name": "verify-dotnet", "steps": [{"name": repair.STALE_STEP, "conclusion": "failure"}]},
            {"name": repair.CI_DOCS_JOB, "steps": [{"name": repair.STALE_STEP, "conclusion": "failure"}]}]}]
        self.assertTrue(repair.ci_reports_stale(api, HEAD))
        self.assertEqual(api.get.call_args_list[1].args[0], "/actions/runs/2/attempts/2/jobs?per_page=100")
        api.get.side_effect = [{"workflow_runs": []}]
        self.assertFalse(repair.ci_reports_stale(api, HEAD))

    def test_feature_base_is_ineligible_before_candidate_or_git_access(self):
        pr = pull()
        pr["base"]["ref"] = "feature-base"
        # Mirrors the observed hosted metadata: the source run's head is the PR
        # head, not the requested base. It must not obscure target eligibility.
        run = source_run(event="pull_request_target", head_sha=HEAD)
        api = mock.Mock(repository=REPOSITORY)
        api.get.side_effect = [run, pr]
        with mock.patch.object(repair, "git") as git:
            result = repair.inspect(Path("."), api, 99, 1, "main")
        self.assertEqual(result["outcome"], "ineligible")
        self.assertIn("default branch", result["reason"])
        git.assert_not_called()
        api.download.assert_not_called()
        api.request.assert_not_called()

    def test_eligible_pr_still_rejects_unbound_run(self):
        api = mock.Mock(repository=REPOSITORY)
        api.get.side_effect = [source_run(head_sha=HEAD), pull()]
        with mock.patch.object(repair, "git") as git:
            with self.assertRaisesRegex(repair.RepairError, "not bound to its default-branch workflow execution"):
                repair.inspect(Path("."), api, 99, 1, "main")
        git.assert_not_called()
        api.download.assert_not_called()

    def test_target_event_only_requests_trusted_generation_and_never_opens_candidate(self):
        api = mock.Mock(repository=REPOSITORY)
        api.get.side_effect = [source_run(event="pull_request_target", head_sha=HEAD, head_branch="topic"), labelled()]
        with mock.patch.object(repair, "fetch_pr") as fetch, \
             mock.patch.object(repair, "loop_suppressed", return_value=False), \
             mock.patch.object(repair, "git") as git:
            result = repair.inspect(Path("."), api, 99, 1, "main")
        self.assertEqual(result["outcome"], "trusted-generation-requested")
        self.assertEqual(result["status"], "retry")
        self.assertEqual(result["request"]["attempt"], 0)
        fetch.assert_called_once_with(Path("."), 7, HEAD, BASE)
        git.assert_not_called()
        api.download.assert_not_called()
        api.request.assert_not_called()

    def test_configured_target_avoids_duplicate_generation_and_preserves_artifact_fallback(self):
        for fork, configured in ((False, True), (False, False), (True, True), (True, False)):
            pr = labelled()
            if fork:
                pr["head"]["repo"]["full_name"] = "contributor/fork"
            api = mock.Mock(repository=REPOSITORY)
            api.get.side_effect = [source_run(event="pull_request_target", head_sha=HEAD), pr]
            with self.subTest(fork=fork, configured=configured), tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp, \
                 mock.patch.dict(os.environ, {"TRUSTED_DISPATCH_CONFIGURED": str(configured).lower()}), \
                 mock.patch.object(repair, "fetch_pr"), \
                 mock.patch.object(repair, "loop_suppressed", return_value=False):
                result = repair.prepare_generation(Path("."), api, 99, "main", Path(temp) / "result.json")
            self.assertEqual(result["status"], "request" if configured and not fork else "generate")
            api.request.assert_not_called()

    def test_ineligible_target_does_not_excuse_forged_run_identity(self):
        api = mock.Mock(repository=REPOSITORY)
        api.get.return_value = source_run(path=".github/workflows/forged.yml")
        with self.assertRaises(repair.RepairError):
            repair.inspect(Path("."), api, 99, 1, "main")
        self.assertEqual(api.get.call_count, 1)

    def test_prepare_reports_unsupported_base_without_fetching(self):
        api = mock.Mock(repository=REPOSITORY)
        pr = pull()
        pr["base"]["ref"] = "feature-base"
        api.get.side_effect = [source_run(), pr]
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp, mock.patch.object(repair, "fetch_pr") as fetch:
            result = repair.prepare_generation(Path("."), api, 99, "main", Path(temp) / "result.json")
        self.assertEqual(result["status"], "ineligible")
        fetch.assert_not_called()

    def test_publisher_never_mutates_ineligible_or_failed_results(self):
        api = mock.Mock(repository=REPOSITORY)
        for status in ("ineligible", "failed", "manual", "noop"):
            with self.subTest(status=status), mock.patch.object(repair, "git") as git:
                prepared = {"status": status, "request": request()}
                self.assertEqual(repair.publish(Path("."), api, prepared, "main"), prepared)
                git.assert_not_called()
        api.get.assert_not_called()
        api.request.assert_not_called()

    def test_machine_outcomes_keep_routing_status_and_record_attempt(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            output = Path(temp) / "out.json"
            summary = Path(temp) / "summary.md"
            step = Path(temp) / "outputs.txt"
            env = {"GITHUB_OUTPUT": str(step), "GITHUB_STEP_SUMMARY": str(summary),
                   "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "2",
                   "SOURCE_RUN_ID": "99", "SOURCE_RUN_ATTEMPT": "1"}
            with mock.patch.dict(os.environ, env):
                repair.write_outputs({"status": "manual", "reason": "Review source and prose",
                                      "manual_paths": ["src/Example/README.md"]}, output)
            report = json.loads(output.read_text())
            self.assertEqual(report["schema_version"], 1)
            self.assertEqual(report["outcome"], "review-required")
            self.assertEqual(report["source_run_id"], 99)
            self.assertEqual(report["run_attempt"], 2)
            self.assertEqual(step.read_text(), "status=manual\n")
            self.assertIn("src/Example/README.md", summary.read_text())
            self.assertIn("Next action:", summary.read_text())

    def test_artifact_only_requires_no_credentials_or_network(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            output = Path(temp) / "result.json"
            output.write_text(json.dumps({"status": "ready", "request": request()}))
            argv = ["generated_repair.py", "artifact-only", "--result", str(output)]
            with mock.patch("sys.argv", argv), mock.patch.object(repair, "GitHub") as api:
                self.assertEqual(repair.main(), 0)
            api.assert_not_called()
            result = json.loads(output.read_text())
            self.assertEqual(result["outcome"], "configuration-missing")
            self.assertEqual(result["request"]["base_sha"], BASE)
            self.assertIn("administrator", result["next_action"])

    def test_published_receipt_preserves_inspected_source_evidence(self):
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temp:
            output = Path(temp) / "result.json"
            prepared = {"status": "ready", "request": request(), "source_run_id": 99,
                        "source_run_attempt": 1, "workflow_execution_sha": BASE,
                        "execution_ref": "refs/heads/main", "workflow_ref_derived": True,
                        "workflow_ref": f"{REPOSITORY}/{repair.WORKFLOW}@refs/heads/main"}
            output.write_text(json.dumps(prepared))
            argv = ["generated_repair.py", "publish", "--result", str(output)]
            with mock.patch("sys.argv", argv), mock.patch.dict(os.environ, {"GH_TOKEN": "test-token"}), \
                 mock.patch.object(repair, "GitHub"), \
                 mock.patch.object(repair, "publish", return_value={"status": "published", "commit_sha": HEAD}):
                self.assertEqual(repair.main(), 0)
            result = json.loads(output.read_text())
            for key in ("request", "source_run_id", "source_run_attempt",
                        "workflow_execution_sha", "execution_ref", "workflow_ref", "workflow_ref_derived"):
                self.assertEqual(result[key], prepared[key])
            self.assertNotIn("test-token", output.read_text())

    def test_closed_or_deleted_pr_is_not_retried_as_stale(self):
        for pr in (pull(state="closed"), pull(merged=True)):
            with self.subTest(pr=pr):
                result = repair.eligibility(pr, REPOSITORY, "main")
                self.assertEqual(result["status"], "ineligible")
        pr = copy.deepcopy(pull())
        pr["head"]["repo"] = None
        self.assertEqual(repair.eligibility(pr, REPOSITORY, "main")["status"], "ineligible")


if __name__ == "__main__":
    unittest.main()
