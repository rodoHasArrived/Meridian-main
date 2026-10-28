"""Observe repair CI from exact commit checks without inventing missing policy."""
from __future__ import annotations

import copy
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "repair_outcome_metrics", ROOT / "build/scripts/ci/summarize-repair-outcomes.py")
OUTCOMES = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(OUTCOMES)


class RepairedRevisionTests(unittest.TestCase):
    def setUp(self):
        self.commit = "d" * 40
        self.job = {
            "id": 11, "name": "quality-gate", "status": "completed", "conclusion": "success",
            "run_attempt": 1, "check_run_url": "https://api.github.com/repos/o/r/check-runs/31",
            "check_app_id": 42, "created_at": "2026-10-20T01:01:00Z",
            "started_at": "2026-10-20T01:02:00Z", "completed_at": "2026-10-20T01:03:00Z",
        }
        self.check = {"id": 31, "head_sha": self.commit, "name": "quality-gate",
                      "status": "completed", "conclusion": "success", "app": {"id": 42}}
        self.run = {
            "id": 12, "name": "Meridian CI", "path": ".github/workflows/meridian-ci.yml",
            "run_attempt": 1, "event": "pull_request", "head_sha": self.commit,
            "status": "completed", "conclusion": "success", "collectionComplete": True,
            "jobs": [self.job], "pull_requests": [{"number": 23, "head": {"sha": self.commit}}],
        }
        self.snapshot = {
            "workflow_runs": [self.run], "collectedAt": "2026-10-20T03:00:00Z",
            "check_runs_by_sha": {self.commit: [self.check]},
            "collection": {"runDataComplete": True, "checkDataComplete": True,
                           "requiredChecksComplete": True, "requiredChecksProvenance": "current_only",
                           "requiredChecks": [{"name": "quality-gate", "appId": 42}]},
        }
        self.row = {
            "runId": 10, "attempt": 1, "kind": "publish", "createdAt": "2026-10-20T01:00:00Z",
            "publishedObservedAt": "2026-10-20T01:00:30Z", "workflowConclusion": "success",
            "receipt": {"outcome": "published", "commit_sha": self.commit,
                        "request": {"pr_number": 23, "root_sha": "a" * 40,
                                    "head_sha": "a" * 40, "base_sha": "b" * 40}},
        }

    def report(self, rows=None):
        return OUTCOMES.first_revision_success(self.snapshot, rows or [self.row])

    def test_exact_published_commit_with_complete_bound_checks_is_measured(self):
        result = self.report()
        self.assertEqual((result["successRate"], result["successful"], result["evaluated"]), (1, 1, 1))
        self.assertEqual(result["requiredChecksProvenance"], "current_only")
        self.assertIn("earlier publications outside", result["qualification"])
        self.assertEqual(result["observations"][0]["commitSha"], self.commit)

    def test_terminal_failure_cancellation_skip_and_neutral_are_in_denominator(self):
        for conclusion in ("failure", "cancelled", "skipped", "neutral", "timed_out"):
            with self.subTest(conclusion=conclusion):
                self.job["conclusion"] = self.check["conclusion"] = conclusion
                result = self.report()
                self.assertEqual((result["successRate"], result["unsuccessful"], result["evaluated"]), (0, 1, 1))

    def test_pending_checks_are_unavailable_instead_of_failed_or_successful(self):
        self.job.update(status="in_progress", conclusion=None)
        self.check.update(status="in_progress", conclusion=None)
        self.run.update(status="in_progress", conclusion=None)
        result = self.report()
        self.assertIsNone(result["successRate"])
        self.assertEqual((result["evaluated"], result["unavailable"]), (0, 1))
        self.assertIn("not terminal", str(result["observations"][0]["reasons"]))

    def test_missing_or_partial_required_policy_never_means_success(self):
        for field, value in (("requiredChecksComplete", False), ("requiredChecks", []),
                             ("checkDataComplete", False), ("runDataComplete", False)):
            original = copy.deepcopy(self.snapshot["collection"])
            with self.subTest(field=field):
                self.snapshot["collection"][field] = value
                self.assertIsNone(self.report()["successRate"])
            self.snapshot["collection"] = original

    def test_required_context_must_explicitly_bind_a_positive_app_id(self):
        for app_id in (None, -1, 0, True, "42"):
            with self.subTest(app_id=app_id):
                self.snapshot["collection"]["requiredChecks"][0]["appId"] = app_id
                result = self.report()
                self.assertIsNone(result["successRate"])
                self.assertIn("App binding", str(result["observations"][0]["reasons"]))

    def test_wrong_commit_pr_app_workflow_or_raw_check_cannot_satisfy_policy(self):
        original = copy.deepcopy(self.snapshot)
        mutations = (
            lambda: self.snapshot["workflow_runs"][0].update(head_sha="e" * 40),
            lambda: self.snapshot["workflow_runs"][0]["pull_requests"][0].update(number=99),
            lambda: self.snapshot["workflow_runs"][0]["jobs"][0].update(check_app_id=77),
            lambda: self.snapshot["collection"]["requiredChecks"][0].update(workflow="Other workflow"),
            lambda: self.snapshot["check_runs_by_sha"][self.commit][0].update(head_sha="e" * 40),
            lambda: self.snapshot["check_runs_by_sha"][self.commit][0].update(conclusion="failure"),
            lambda: self.snapshot.update(check_runs_by_sha={}),
        )
        for mutation in mutations:
            self.snapshot = copy.deepcopy(original)
            mutation()
            self.assertIsNone(self.report()["successRate"])

    def test_merge_checkout_check_requires_immutable_head_and_pr_binding(self):
        merged = "e" * 40
        self.check["head_sha"] = merged
        self.run["changes"] = {"headSha": self.commit, "pullNumber": 23,
                               "mergeEvidence": {"headSha": self.commit, "pullNumber": 23,
                                                 "mergeShas": [merged]}}
        self.assertEqual(self.report()["successRate"], 1)
        self.run["changes"]["mergeEvidence"]["headSha"] = "f" * 40
        self.assertIsNone(self.report()["successRate"])

    def test_duplicate_independent_runs_or_missing_attempts_are_unavailable(self):
        self.snapshot["workflow_runs"].append(dict(self.run, id=99))
        self.assertIsNone(self.report()["successRate"])
        self.snapshot["workflow_runs"].pop()
        self.run["run_attempt"] = 2
        self.assertIsNone(self.report()["successRate"])

    def test_partial_rerun_inherits_only_verified_prior_execution(self):
        retry = dict(self.run, run_attempt=2, jobs=[dict(self.job)])
        self.snapshot["workflow_runs"].append(retry)
        self.assertEqual(self.report()["successRate"], 1)
        retry["jobs"][0]["completed_at"] = "2026-10-20T01:03:01Z"
        self.assertIsNone(self.report()["successRate"])

    def test_only_first_observed_publication_per_root_is_evaluated(self):
        later = copy.deepcopy(self.row)
        later.update(runId=20, publishedObservedAt="2026-10-20T02:00:00Z")
        later["receipt"]["commit_sha"] = "e" * 40
        result = self.report([later, self.row])
        self.assertEqual(result["observedGroups"], 1)
        self.assertEqual(result["successRate"], 1)
        self.assertEqual(result["observations"][0]["observedPublications"], 2)

    def test_publication_time_or_pinned_identity_gaps_are_counted_unavailable(self):
        self.row["publishedObservedAt"] = None
        self.assertEqual(self.report()["unavailable"], 1)
        self.assertIsNone(self.report()["successRate"])
        self.row["receipt"]["request"].pop("root_sha")
        result = self.report()
        self.assertEqual(result["unavailable"], 1)
        self.assertEqual(result["observedGroups"], 0)

    def test_producer_success_and_noop_are_not_repaired_revision_success(self):
        self.row["receipt"]["outcome"] = "noop"
        self.row["receipt"].pop("commit_sha")
        result = self.report()
        self.assertIsNone(result["successRate"])
        self.assertEqual(result["observedGroups"], 0)

    def test_pushed_commit_before_stale_ref_recheck_remains_first_revision(self):
        self.row["receipt"]["outcome"] = "stale-retry"
        later = copy.deepcopy(self.row)
        later.update(runId=20, publishedObservedAt="2026-10-20T02:00:00Z")
        later["receipt"].update(outcome="published", commit_sha="e" * 40)
        result = self.report([later, self.row])
        self.assertEqual(result["successRate"], 1)
        self.assertEqual(result["observations"][0]["publisherOutcome"], "stale-retry")
        self.assertEqual(result["observations"][0]["commitSha"], self.commit)
        self.row["receipt"]["outcome"] = "review-required"
        result = self.report([later, self.row])
        self.assertEqual(result["observations"][0]["commitSha"], self.commit)
        self.assertEqual(result["observations"][0]["publisherOutcome"], "review-required")
        self.row["receipt"]["commit_sha"] = "malformed"
        self.assertEqual(self.report()["unavailable"], 1)

    def test_verified_archive_flows_through_summary_into_measured_rate(self):
        receipt = dict(self.row["receipt"], schema_version=1, workflow_run_id=10, run_attempt=1)
        data = io.BytesIO()
        with zipfile.ZipFile(data, "w") as archive:
            archive.writestr("generated-repair-result.json", json.dumps(receipt))
        publisher = {
            "id": 10, "run_attempt": 1, "head_sha": "b" * 40, "event": "workflow_run",
            "path": OUTCOMES.WORKFLOW, "created_at": self.row["createdAt"], "conclusion": "success",
            "jobs": [{"name": "Validate and publish generated repair", "conclusion": "success"},
                     {"name": "Refresh stale open PR branches after main advances", "conclusion": "skipped"}],
        }
        artifact = {
            "id": 9, "name": "generated-repair-outcome-publish-10-1", "expired": False,
            "created_at": self.row["publishedObservedAt"],
            "workflow_run": {"id": 10, "head_sha": "b" * 40},
        }

        class API:
            def artifacts(self, run_id):
                return [artifact]

            def download(self, entry):
                return data.getvalue()

        self.snapshot["workflow_runs"].append(publisher)
        result = OUTCOMES.summarize(self.snapshot, API())
        self.assertEqual(result["firstRepairedRevisionCiSuccessRate"], 1)
        self.assertIn("1/1 evaluated", OUTCOMES.markdown(result))


class RefreshObservationTests(unittest.TestCase):
    def row(self, hour, waiting=None):
        waiting = waiting if waiting is not None else [
            {"pr_number": 23, "head_sha": "a" * 40, "base_sha": "b" * 40}]
        observed = f"2026-10-20T{hour:02}:00:00Z"
        return {"kind": "refresh", "runId": hour, "attempt": 1, "createdAt": observed,
                "receipt": {"outcome": "refreshed", "observed_at": observed,
                            "backlog": len(waiting), "waiting_requests": waiting}}

    def test_waiting_age_is_exact_pair_observation_lower_bound(self):
        result = OUTCOMES.backlog_observations([self.row(1), self.row(3)], [], run_data_complete=True)
        self.assertTrue(result["available"])
        self.assertEqual(result["waiting"][0]["observedAgeLowerBoundSeconds"], 7200)
        self.assertIn("not PR age", result["qualification"])

    def test_missing_scan_changed_head_or_disappearance_reset_observed_age(self):
        missing = [{"kind": "refresh", "runId": 2, "attempt": 1, "createdAt": "2026-10-20T02:00:00Z"}]
        result = OUTCOMES.backlog_observations([self.row(1), self.row(3)], missing, run_data_complete=True)
        self.assertEqual(result["waiting"][0]["observedAgeLowerBoundSeconds"], 0)
        changed = self.row(3)
        changed["receipt"]["waiting_requests"][0]["head_sha"] = "c" * 40
        result = OUTCOMES.backlog_observations([self.row(1), changed], [], run_data_complete=True)
        self.assertEqual(result["waiting"][0]["observedAgeLowerBoundSeconds"], 0)
        result = OUTCOMES.backlog_observations([self.row(1), self.row(2, []), self.row(3)], [], run_data_complete=True)
        self.assertEqual(result["waiting"][0]["observedAgeLowerBoundSeconds"], 0)

    def test_legacy_or_inconsistent_refresh_has_no_age(self):
        row = self.row(1)
        row["receipt"]["backlog"] = 5
        result = OUTCOMES.backlog_observations([row], [])
        self.assertFalse(result["available"])
        self.assertEqual(result["waiting"], [])
        row["receipt"].pop("observed_at")
        self.assertFalse(OUTCOMES.backlog_observations([row], [])["available"])

    def test_empty_latest_backlog_retains_observation_time(self):
        result = OUTCOMES.backlog_observations([self.row(1), self.row(2, [])], [], run_data_complete=True)
        self.assertTrue(result["available"])
        self.assertEqual(result["waiting"], [])
        self.assertEqual(result["observedAt"], "2026-10-20T02:00:00+00:00")

    def test_rerun_order_uses_scan_observation_not_original_run_creation(self):
        later_rerun = self.row(3)
        later_rerun.update(createdAt="2026-10-20T00:00:00Z", attempt=2)
        result = OUTCOMES.backlog_observations([later_rerun, self.row(1)], [], run_data_complete=True)
        self.assertTrue(result["available"])
        self.assertEqual(result["waiting"][0]["observedAgeLowerBoundSeconds"], 7200)

    def test_incomplete_run_inventory_cannot_establish_observed_continuity(self):
        result = OUTCOMES.backlog_observations([self.row(1), self.row(3)], [], run_data_complete=False)
        self.assertFalse(result["available"])
        self.assertIsNone(result["waiting"][0]["firstObservedAt"])
        self.assertIsNone(result["waiting"][0]["observedAgeLowerBoundSeconds"])
        self.assertIsNone(result["oldestObservedAgeLowerBoundSeconds"])
        self.assertIn("missing scans", result["unavailableReasons"][0])

    def test_review_causes_are_receipt_counts_not_inferred_categories(self):
        row = self.row(1)
        row["receipt"].update(outcome="review-required", reason="Novel combined module",
                              manual_paths=["src/Example/README.md"])
        result = OUTCOMES.review_causes([row, row])
        self.assertEqual(result["byReason"], {"Novel combined module": 2})
        self.assertEqual(result["manualPaths"], {"src/Example/README.md": 2})


class CollectionHealthTests(unittest.TestCase):
    def setUp(self):
        self.snapshot = {"collection": {
            "complete": True, "runDataComplete": True, "checkDataComplete": True,
            "workflowInventoryComplete": True, "changesComplete": True,
            "requiredChecksComplete": True, "errors": [],
        }}

    def test_complete_collection_stays_complete(self):
        self.assertEqual(OUTCOMES.collection_health(self.snapshot)["status"], "complete")

    def test_known_admin_denial_and_missing_history_are_partial_not_complete(self):
        collection = self.snapshot["collection"]
        collection.update(complete=False, requiredChecksComplete=False, changesComplete=False)
        collection["errors"].append({"scope": "requiredChecks", "status": 403,
                                     "message": "Resource not accessible by integration"})
        original = copy.deepcopy(self.snapshot)
        health = OUTCOMES.collection_health(self.snapshot)
        self.assertEqual(health["status"], "partial")
        self.assertEqual(len(health["unavailable"]), 2)
        self.assertEqual(health["failures"], [])
        self.assertEqual(self.snapshot, original, "Never rewrite collector evidence as complete.")

    def test_other_endpoint_denials_rate_limits_and_pagination_faults_remain_failures(self):
        errors = (
            {"scope": "runs", "status": 403, "message": "Resource not accessible by integration"},
            {"scope": "requiredChecks", "status": 403, "message": "API rate limit exceeded"},
            {"scope": "requiredChecks", "status": 500, "message": "Server error"},
            {"scope": "checks", "message": "Duplicate records across pages; snapshot is unstable."},
            {"scope": "changes", "status": 403, "message": "Resource not accessible by integration"},
        )
        for error in errors:
            with self.subTest(error=error):
                self.snapshot["collection"]["errors"] = [error]
                self.assertEqual(OUTCOMES.collection_health(self.snapshot)["status"], "failed")

    def test_unexplained_or_core_inventory_incompleteness_remains_failure(self):
        for field in ("runDataComplete", "checkDataComplete", "workflowInventoryComplete",
                      "requiredChecksComplete", "complete"):
            collection = copy.deepcopy(self.snapshot["collection"])
            with self.subTest(field=field):
                self.snapshot["collection"][field] = False
                self.assertEqual(OUTCOMES.collection_health(self.snapshot)["status"], "failed")
            self.snapshot["collection"] = collection

    def test_cli_classifies_without_github_credentials_or_repo_argument(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "snapshot.json"
            for complete in (True, False):
                self.snapshot["collection"]["runDataComplete"] = complete
                path.write_text(json.dumps(self.snapshot))
                result = subprocess.run(
                    [sys.executable, str(ROOT / "build/scripts/ci/summarize-repair-outcomes.py"),
                     "--check-collection", "--input", str(path)], capture_output=True, text=True)
                self.assertEqual(result.returncode, 0 if complete else 1, result.stderr)


if __name__ == "__main__":
    unittest.main()
