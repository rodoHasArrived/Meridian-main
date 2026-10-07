import copy
import hashlib
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit


spec = importlib.util.spec_from_file_location(
    "actions_collector", Path(__file__).resolve().parents[2] / "build/scripts/ci/collect-actions.py")
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)

HEAD, BASE, MERGE = "a" * 40, "b" * 40, "c" * 40
PREFIX = "/repos/acme/repo"
START = "2026-09-28T00:00:00Z"
END = "2026-09-28T00:00:03Z"


def run(number=1, **overrides):
    return dict(dict(id=number, name="CI", head_sha=HEAD, head_branch="feature", event="pull_request",
                     run_attempt=1, status="completed", conclusion="success", created_at=START,
                     referenced_workflows=[dict(sha=MERGE, ref="refs/pull/7/merge")],
                     pull_requests=[dict(number=7, base=dict(sha=BASE), head=dict(sha=HEAD))]), **overrides)


class FakeAPI:
    def __init__(self, routes):
        self.routes = {collector.endpoint_key(key): value for key, value in routes.items()}
        self.requests = []

    def get(self, endpoint):
        self.requests.append(endpoint)
        value = self.routes.get(collector.endpoint_key(endpoint), collector.CollectionError("Missing fake response"))
        if isinstance(value, Exception):
            raise value
        return copy.deepcopy(value)

    def get_text(self, endpoint):
        return self.get(endpoint)


class ActionsCollectorTests(unittest.TestCase):
    def fixture(self, attempts=1):
        latest = run(run_attempt=attempts)
        routes = {
            PREFIX + "/actions/workflows?per_page=100&page=1": dict(total_count=1, workflows=[dict(id=9)]),
            PREFIX + "/branches/main/protection/required_status_checks": dict(contexts=["quality-gate"], checks=[]),
            PREFIX + "/rules/branches/main?per_page=100&page=1": [],
            PREFIX + f"/actions/runs?created={START}..{END}&per_page=100&page=1": dict(total_count=1, workflow_runs=[latest]),
            PREFIX + "/actions/runs/1": latest,
            PREFIX + f"/compare/{BASE}...{HEAD}": dict(status="ahead", files=[dict(filename="src/core.cs")]),
            PREFIX + f"/commits/{MERGE}": dict(parents=[dict(sha=BASE), dict(sha=HEAD)]),
            PREFIX + f"/commits/{HEAD}/check-runs?filter=all&per_page=100&page=1":
                dict(total_count=1, check_runs=[dict(id=91, app=dict(id=15368))]),
        }
        for attempt in range(1, attempts + 1):
            detail = run(run_attempt=attempt, conclusion="success" if attempt == attempts else "failure")
            routes[PREFIX + f"/actions/runs/1/attempts/{attempt}"] = detail
            routes[PREFIX + f"/actions/runs/1/attempts/{attempt}/jobs?per_page=100&page=1"] = dict(
                total_count=1, jobs=[dict(id=attempt, name="quality-gate", run_attempt=attempt,
                                        check_run_url=f"https://api.github.com{PREFIX}/check-runs/91")])
        return routes

    def collect(self, routes, **kwargs):
        api = FakeAPI(routes)
        export = collector.Collector(api, "acme/repo", collector.timestamp(START), collector.timestamp(END), **kwargs)
        return export.collect(), api

    def test_all_attempts_retained_with_original_conclusion_and_app_identity(self):
        result, api = self.collect(self.fixture(attempts=3))
        self.assertTrue(result["collection"]["complete"], result["collection"]["errors"])
        self.assertEqual([1, 2, 3], [item["run_attempt"] for item in result["workflow_runs"]])
        self.assertEqual(["failure", "failure", "success"], [item["conclusion"] for item in result["workflow_runs"]])
        self.assertEqual(15368, result["workflow_runs"][0]["jobs"][0]["check_app_id"])
        self.assertEqual("current_only", result["requiredCheckConfiguration"]["provenance"])
        self.assertEqual(1, sum("/check-runs?" in url for url in api.requests))

    def test_workflows_runs_and_jobs_are_paginated(self):
        routes = self.fixture()
        for endpoint, key in (("/actions/workflows", "workflows"), ("/actions/runs/1/attempts/1/jobs", "jobs")):
            routes[PREFIX + endpoint + "?per_page=100&page=1"] = {"total_count": 101, key: [dict(id=i) for i in range(100)]}
            routes[PREFIX + endpoint + "?per_page=100&page=2"] = {"total_count": 101, key: [dict(id=100)]}
        result, _ = self.collect(routes)
        self.assertTrue(result["collection"]["complete"])
        self.assertEqual(101, len(result["workflows"]))
        self.assertEqual(101, len(result["workflow_runs"][0]["jobs"]))
        api = FakeAPI({
            PREFIX + "/x?per_page=100&page=1": dict(total_count=101, workflow_runs=[run(i) for i in range(100)]),
            PREFIX + "/x?per_page=100&page=2": dict(total_count=101, workflow_runs=[run(100)]),
        })
        export = collector.Collector(api, "acme/repo", collector.timestamp(START), collector.timestamp(END))
        self.assertEqual(101, len(export.pages(PREFIX + "/x", "workflow_runs", "runs")))

    def test_filtered_run_cap_splits_time_without_overlap(self):
        class SplitAPI:
            requests = []

            def get(self, endpoint):
                window = parse_qs(urlsplit(endpoint).query)["created"][0]
                self.requests.append(window)
                if window == f"{START}..{END}":
                    return dict(total_count=1000, workflow_runs=[])
                return dict(total_count=1, workflow_runs=[run(len(self.requests))])

        api = SplitAPI()
        export = collector.Collector(api, "acme/repo", collector.timestamp(START), collector.timestamp(END))
        self.assertEqual(2, len(export.runs(export.since, export.until)))
        self.assertEqual([], export.errors)
        self.assertEqual([f"{START}..{END}", f"{START}..2026-09-28T00:00:01Z",
                          f"2026-09-28T00:00:02Z..{END}"], api.requests)

    def test_unresolvable_cap_retains_available_records_and_fails_closed(self):
        routes = {PREFIX + f"/actions/runs?created={START}..{START}&per_page=100&page=1":
                  dict(total_count=1000, workflow_runs=[run()])}
        export = collector.Collector(FakeAPI(routes), "acme/repo", collector.timestamp(START), collector.timestamp(START))
        self.assertEqual(1, len(export.runs(export.since, export.until)))
        self.assertTrue(any("cap prevents" in error["message"] for error in export.errors))

    def test_missing_jobs_page_keeps_prior_page_and_marks_incomplete(self):
        routes = self.fixture()
        routes[PREFIX + "/actions/runs/1/attempts/1/jobs?per_page=100&page=1"] = dict(
            total_count=101, jobs=[dict(id=i) for i in range(100)])
        result, _ = self.collect(routes)
        self.assertFalse(result["collection"]["runDataComplete"])
        self.assertFalse(result["workflow_runs"][0]["collectionComplete"])
        self.assertEqual(100, len(result["workflow_runs"][0]["jobs"]))

    def test_repeated_pages_fail_closed_instead_of_looping(self):
        page = dict(total_count=200, jobs=[dict(id=i) for i in range(100)])
        api = FakeAPI({PREFIX + f"/x?per_page=100&page={number}": page for number in (1, 2)})
        export = collector.Collector(api, "acme/repo", collector.timestamp(START), collector.timestamp(END))
        self.assertEqual(100, len(export.pages(PREFIX + "/x", "jobs", "jobs")))
        self.assertTrue(any("Duplicate" in error["message"] for error in export.errors))

    def test_missing_attempt_metadata_never_borrows_latest_success(self):
        routes = self.fixture(attempts=2)
        del routes[PREFIX + "/actions/runs/1/attempts/1"]
        result, _ = self.collect(routes)
        self.assertIsNone(result["workflow_runs"][0]["conclusion"])
        self.assertFalse(result["collection"]["runDataComplete"])
        self.assertEqual("success", result["workflow_runs"][1]["conclusion"])

    def test_inherited_successful_jobs_keep_original_attempt_and_id(self):
        routes = self.fixture(attempts=2)
        original = routes[PREFIX + "/actions/runs/1/attempts/1/jobs?per_page=100&page=1"]["jobs"][0]
        routes[PREFIX + "/actions/runs/1/attempts/2/jobs?per_page=100&page=1"]["jobs"].append(original)
        routes[PREFIX + "/actions/runs/1/attempts/2/jobs?per_page=100&page=1"]["total_count"] = 2
        result, _ = self.collect(routes)
        self.assertTrue(result["collection"]["runDataComplete"])
        self.assertEqual(1, result["workflow_runs"][1]["jobs"][1]["run_attempt"])

    def test_push_without_before_is_not_misrepresented_as_single_commit_diff(self):
        export = collector.Collector(FakeAPI(self.fixture()), "acme/repo", collector.timestamp(START), collector.timestamp(END))
        push = run(event="push", head_branch="main", pull_requests=[])
        self.assertFalse(export.changes(push)["complete"])
        export.push_bases[HEAD] = BASE
        self.assertTrue(export.changes(push)["complete"])
        self.assertEqual("provided_push_before_sha", export.changes(push)["source"])

    def test_compare_truncation_and_renames_are_explicit(self):
        routes = self.fixture()
        routes[PREFIX + f"/compare/{BASE}...{HEAD}"] = dict(files=[dict(filename=f"src/{i}.cs") for i in range(300)])
        result, _ = self.collect(routes)
        self.assertFalse(result["collection"]["changesComplete"])
        routes[PREFIX + f"/compare/{BASE}...{HEAD}"] = dict(files=[dict(filename="src/new.cs", previous_filename="docs/old.md")])
        result, _ = self.collect(routes)
        self.assertEqual(["docs/old.md", "src/new.cs"], result["workflow_runs"][0]["changed_files"])

    def test_cache_normalizes_query_and_never_fetches_missing_entries(self):
        api = collector.CachedAPI({"/repos/a/b/x?page=1&per_page=100": dict(ok=True)})
        self.assertEqual(dict(ok=True), api.get("/repos/a/b/x?per_page=100&page=1"))
        api.get("/repos/a/b/x?per_page=100&page=1")["derived"] = "report-only"
        self.assertNotIn("derived", api.get("/repos/a/b/x?per_page=100&page=1"))
        with self.assertRaises(collector.CollectionError):
            api.get("/repos/a/b/missing")

    def test_transport_uses_explicit_get_and_does_not_invoke_shell(self):
        with patch.object(collector.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "{}", "")) as command:
            collector.GitHubAPI().get(PREFIX + "/actions/runs")
        args, kwargs = command.call_args
        self.assertEqual(["gh", "api", "--method", "GET"], args[0][:4])
        self.assertNotIn("shell", kwargs)

    def test_naive_or_fractional_time_windows_are_rejected(self):
        for value in ("2026-09-28", "2026-09-28T00:00:00.123Z"):
            with self.assertRaises(ValueError):
                collector.timestamp(value)

    def test_future_collection_window_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "future"):
            collector.Collector(FakeAPI({}), "acme/repo", collector.timestamp(START), collector.timestamp("2099-01-01T00:00:00Z"))

    def test_classic_absence_is_distinguished_from_denied_access(self):
        routes = self.fixture()
        endpoint = PREFIX + "/branches/main/protection/required_status_checks"
        routes[endpoint] = collector.APIError("Branch not protected", 404)
        result, _ = self.collect(routes)
        self.assertTrue(result["collection"]["requiredChecksComplete"])
        routes[endpoint] = collector.APIError("Not Found", 404)
        result, _ = self.collect(routes)
        self.assertFalse(result["collection"]["requiredChecksComplete"])
        routes[PREFIX + "/branches/main/protection"] = {"url": "https://api.github.com/protection"}
        result, _ = self.collect(routes)
        self.assertTrue(result["collection"]["requiredChecksComplete"])

    def test_missing_commit_check_inventory_uses_direct_job_check_identity(self):
        routes = self.fixture()
        routes[PREFIX + f"/commits/{HEAD}/check-runs?filter=all&per_page=100&page=1"] = dict(total_count=0, check_runs=[])
        routes[PREFIX + "/check-runs/91"] = dict(id=91, app=dict(id=100))
        result, _ = self.collect(routes)
        self.assertEqual(100, result["workflow_runs"][0]["jobs"][0]["check_app_id"])

    def test_merge_reference_recovers_immutable_pr_base_and_rejects_wrong_parent(self):
        routes = self.fixture()
        merge = "c" * 40
        routes[PREFIX + f"/commits/{merge}"] = dict(parents=[dict(sha=BASE), dict(sha=HEAD)])
        export = collector.Collector(FakeAPI(routes), "acme/repo", collector.timestamp(START), collector.timestamp(END))
        item = run(pull_requests=[], referenced_workflows=[dict(sha=merge, ref="refs/pull/7/merge")])
        change = export.changes(item)
        self.assertTrue(change["complete"])
        self.assertEqual(7, change["pullNumber"])
        self.assertEqual(BASE, change["baseSha"])
        self.assertFalse(change["mergeEvidence"]["inferredFromPeerWorkflow"])
        export.merge_cache[merge]["parents"][1]["sha"] = "d" * 40
        self.assertFalse(export.changes(item)["complete"])

    def test_cross_run_merge_recovery_does_not_override_known_other_pr(self):
        routes = self.fixture()
        merge = "c" * 40
        routes[PREFIX + f"/commits/{merge}"] = dict(parents=[dict(sha=BASE), dict(sha=HEAD)])
        export = collector.Collector(FakeAPI(routes), "acme/repo", collector.timestamp(START), collector.timestamp(END))
        item = run(pull_requests=[dict(number=8, head=dict(sha=HEAD))], referenced_workflows=[])
        export.reference_candidates[export.reference_key(item)] = [(merge, 7)]
        self.assertFalse(export.changes(item)["complete"])
        self.assertNotEqual(export.reference_key(run(head_repository=dict(id=1))),
                            export.reference_key(run(head_repository=dict(id=2))))

    def test_workflow_catalog_failure_does_not_hide_complete_bounded_run_inventory(self):
        routes = self.fixture()
        routes[PREFIX + "/actions/workflows?per_page=100&page=1"] = collector.APIError("Forbidden", 403)
        result, _ = self.collect(routes)
        self.assertFalse(result["collection"]["workflowInventoryComplete"])
        self.assertTrue(result["collection"]["runDataComplete"])

    def checkout_fixture(self):
        merge, job_id = "c" * 40, 88
        log = ("2026-09-28T00:00:01Z [command]/usr/bin/git -c protocol.version=2 fetch --depth=1 origin "
               f"+{merge}:refs/remotes/pull/7/merge\n"
               "2026-09-28T00:00:02Z [command]/usr/bin/git log -1 --format=%H\n"
               f"2026-09-28T00:00:02Z {merge}\n")
        evidence = dict(runId=1, jobId=job_id, headSha=HEAD, checkoutMergeSha=merge, pullNumber=7,
                        checkoutRef="refs/pull/7/merge", jobLogUrl=f"https://api.github.com{PREFIX}/actions/jobs/{job_id}/logs",
                        logSha256=hashlib.sha256(log.encode()).hexdigest())
        routes = self.fixture()
        routes[PREFIX + f"/actions/jobs/{job_id}"] = dict(id=job_id, run_id=1, head_sha=HEAD)
        routes[PREFIX + "/actions/runs/1/attempts/1/jobs?per_page=100&page=1"]["jobs"][0].update(
            id=job_id, run_id=1, head_sha=HEAD)
        routes[PREFIX + f"/actions/jobs/{job_id}/logs"] = log
        routes[PREFIX + f"/commits/{merge}"] = dict(parents=[dict(sha=BASE), dict(sha=HEAD)])
        item = run(pull_requests=[], referenced_workflows=[])
        for endpoint in ("/actions/runs/1", "/actions/runs/1/attempts/1"):
            routes[PREFIX + endpoint] = item
        routes[PREFIX + f"/actions/runs?created={START}..{END}&per_page=100&page=1"]["workflow_runs"] = [item]
        return routes, evidence

    def test_checkout_log_proves_missing_immutable_pr_boundary(self):
        routes, evidence = self.checkout_fixture()
        result, _ = self.collect(routes, checkout_evidence=[evidence])
        self.assertTrue(result["collection"]["changesComplete"], result["collection"]["errors"])
        change = result["workflow_runs"][0]["changes"]
        self.assertEqual("actions_checkout_log_merge_commit_parents", change["source"])
        self.assertEqual(BASE, change["baseSha"])
        self.assertTrue(change["mergeEvidence"]["checkoutLogs"][0]["verifiedLogBytes"])
        self.assertFalse(change["mergeEvidence"]["inferredFromPeerWorkflow"])

    def test_checkout_saved_log_digest_is_checked_against_actual_bytes(self):
        routes, evidence = self.checkout_fixture()
        with tempfile.TemporaryDirectory() as directory:
            log_path = Path(directory) / "checkout.log"
            log_path.write_text(routes[PREFIX + "/actions/jobs/88/logs"], encoding="utf-8")
            evidence["logPath"] = str(log_path)
            del routes[PREFIX + "/actions/jobs/88/logs"]
            result, _ = self.collect(routes, checkout_evidence=[evidence])
            self.assertTrue(result["collection"]["changesComplete"])
            log_path.write_text("different log bytes", encoding="utf-8")
            result, _ = self.collect(routes, checkout_evidence=[evidence])
            self.assertFalse(result["collection"]["changesComplete"])

    def test_checkout_evidence_rejects_wrong_job_head_ref_hash_and_graph(self):
        for field, value in (("jobId", 99), ("headSha", "d" * 40), ("pullNumber", 8),
                             ("checkoutRef", "refs/heads/main"), ("logSha256", "0" * 64)):
            with self.subTest(field=field):
                routes, evidence = self.checkout_fixture()
                evidence[field] = value
                result, _ = self.collect(routes, checkout_evidence=[evidence])
                self.assertFalse(result["collection"]["changesComplete"])
        routes, evidence = self.checkout_fixture()
        routes[PREFIX + "/actions/runs/1/attempts/1/jobs?per_page=100&page=1"]["jobs"][0]["run_id"] = 2
        result, _ = self.collect(routes, checkout_evidence=[evidence])
        self.assertFalse(result["collection"]["changesComplete"])
        routes, evidence = self.checkout_fixture()
        routes[PREFIX + f"/commits/{'c' * 40}"]["parents"][1]["sha"] = "d" * 40
        result, _ = self.collect(routes, checkout_evidence=[evidence])
        self.assertFalse(result["collection"]["changesComplete"])

    def test_checkout_fetch_line_alone_does_not_prove_checked_out_commit(self):
        routes, evidence = self.checkout_fixture()
        raw = routes[PREFIX + "/actions/jobs/88/logs"].splitlines()[0] + "\n"
        routes[PREFIX + "/actions/jobs/88/logs"] = raw
        evidence["logSha256"] = hashlib.sha256(raw.encode()).hexdigest()
        result, _ = self.collect(routes, checkout_evidence=[evidence])
        self.assertFalse(result["collection"]["changesComplete"])

    def test_checkout_proof_overrides_reused_branch_mutable_pr_association(self):
        routes, evidence = self.checkout_fixture()
        changed_association = [dict(number=99, head=dict(sha="d" * 40), base=dict(sha=BASE))]
        for item in routes.values():
            if isinstance(item, dict) and item.get("id") == 1 and "head_sha" in item:
                item["pull_requests"] = changed_association
            if isinstance(item, dict) and "workflow_runs" in item:
                item["workflow_runs"][0]["pull_requests"] = changed_association
        result, _ = self.collect(routes, checkout_evidence=[evidence])
        self.assertTrue(result["collection"]["changesComplete"])
        self.assertEqual(f"pull_request:7:{HEAD}", result["workflow_runs"][0]["change_key"])

    def test_matching_current_pr_head_alone_does_not_prove_historical_base(self):
        export = collector.Collector(FakeAPI(self.fixture()), "acme/repo", collector.timestamp(START), collector.timestamp(END))
        self.assertFalse(export.changes(run(referenced_workflows=[]))["complete"])


if __name__ == "__main__":
    unittest.main()
