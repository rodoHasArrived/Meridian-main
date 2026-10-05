import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("ci_metrics", Path(__file__).resolve().parents[2] / "build/scripts/ci/ci-metrics.py")
metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metrics)


class CiMetricsTests(unittest.TestCase):
    def pairs(self):
        item = dict(commitSha="a" * 40, runnerLabel="ubuntu-latest", testIdentityDigest="b" * 64,
                    passed=100, skipped=2, failed=0, conclusion="success", resourceFailure=False)
        return [dict(pairId=str(i), baseline=dict(item, testSeconds=100),
                     candidate=dict(item, testSeconds=80)) for i in range(5)]

    def test_adopts_only_comparable_successful_measurements(self):
        self.assertTrue(metrics.evaluate_pairs(self.pairs())["eligibleForAdoption"])
        for field, value in [("commitSha", "changed"), ("passed", 99), ("skipped", 3),
                             ("resourceFailure", True), ("testSeconds", 90), ("failed", 1)]:
            pairs = self.pairs()
            for p in pairs:
                p["candidate"][field] = value
            self.assertFalse(metrics.evaluate_pairs(pairs)["eligibleForAdoption"], field)

    def test_missing_or_duplicate_pairs_fail_closed(self):
        self.assertFalse(metrics.evaluate_pairs(self.pairs()[:4])["eligibleForAdoption"])
        self.assertFalse(metrics.evaluate_pairs([self.pairs()[0]] * 5)["eligibleForAdoption"])

    def test_queue_and_retries_are_separate(self):
        run = dict(id=1, name="CI", event="pull_request", run_attempt=1, head_sha="abc",
                   conclusion="success", jobs=[dict(name="test", conclusion="success",
                   created_at="2026-09-28T00:00:00Z", started_at="2026-09-28T00:05:00Z",
                   completed_at="2026-09-28T00:06:00Z")])
        retry = copy.deepcopy(run)
        retry["run_attempt"] = 2
        result = metrics.summarize({"workflow_runs": [run, retry]})["groups"]
        self.assertEqual(2, len(result))
        first = result["CI / pull_request / attempt 1"]
        self.assertEqual(60, first["medianRunnerSeconds"])
        self.assertEqual(300, first["runs"][0]["jobs"][0]["queueSeconds"])


class ActionsReportTests(unittest.TestCase):
    def policy(self):
        return {"baseline": {"start": "2026-09-01T00:00:00Z", "end": "2026-09-15T00:00:00Z"},
                "rollout": {"start": "2026-09-15T00:00:00Z", "end": "2026-10-01T00:00:00Z"},
                "requiredChecks": [{"name": "quality-gate", "workflow": "Meridian CI"},
                                   {"name": "Secret Scan", "workflow": "CI"}],
                "requiredChecksEvidence": "Reviewed check snapshots baseline.json / rollout.json"}

    def runs(self, index=1, day=2, scale=1, event="pull_request"):
        from datetime import datetime, timedelta, timezone
        origin = datetime(2026, 9, day, tzinfo=timezone.utc)
        def time(seconds):
            return (origin + timedelta(seconds=seconds * scale)).isoformat().replace("+00:00", "Z")
        def job(name, offset, finish, number):
            return {"id": index * 100 + number, "name": name, "status": "completed",
                    "conclusion": "success", "created_at": time(0), "started_at": time(offset),
                    "completed_at": time(finish), "labels": ["ubuntu-latest"]}
        base = {"event": event, "run_attempt": 1, "head_sha": f"sha{index}", "head_branch": "main",
                "change_key": f"{event}:sha{index}", "created_at": time(0), "run_started_at": time(0),
                "status": "completed", "conclusion": "success", "collectionComplete": True,
                "changes": {"complete": True, "paths": ["src/Meridian.Core/Options.cs"]}}
        return [dict(base, id=index * 10, name="Meridian CI", path=".github/workflows/meridian-ci.yml",
                     jobs=[job("test", 10, 80, 1), job("quality-gate", 80, 100, 2)]),
                dict(base, id=index * 10 + 1, name="CI", path=".github/workflows/ci.yml",
                     jobs=[job("Secret Scan", 20, 130, 3)])]

    def payload(self, runs):
        return {"workflow_runs": runs, "collectedAt": "2026-10-02T00:00:00Z", "window": {"since": "2026-09-01T00:00:00Z", "until": "2026-10-01T00:00:00Z"}, "collection": {"complete": True, "runDataComplete": True, "requiredChecksComplete": True,
                                                      "requiredChecks": self.policy()["requiredChecks"]}}

    def test_critical_path_spans_workflows_and_runner_minutes_add(self):
        result = metrics.summarize(self.payload(self.runs()), self.policy())
        change = result["changes"][0]
        self.assertEqual(130, change["allRequiredChecksSeconds"])
        self.assertEqual(100, change["qualityGateLatencySeconds"])
        self.assertEqual(90, change["qualityGateExecutionSeconds"])
        self.assertEqual(20, change["qualityGateJobExecutionSeconds"])
        self.assertEqual(110, change["queueSeconds"])
        self.assertAlmostEqual(200 / 60, change["runnerMinutes"])
        self.assertTrue(change["comparable"], change["exclusionReasons"])

    def test_p95_nearest_rank_and_missing_values(self):
        self.assertEqual({"samples": 20, "missing": 1, "median": 10.5, "p95": 19},
                         metrics.distribution(list(range(1, 21)) + [None]))
        self.assertEqual(None, metrics.seconds("bad", "also-bad"))
        self.assertEqual(None, metrics.seconds("2026-09-01T00:00:05Z", "2026-09-01T00:00:01Z"))

    def test_required_checks_missing_ambiguous_or_wrong_app_reject(self):
        for kind in ("missing", "ambiguous", "app"):
            runs, policy = self.runs(), self.policy()
            if kind == "missing":
                runs.pop()
            elif kind == "ambiguous":
                runs[1]["jobs"].append(dict(runs[1]["jobs"][0], id=199))
            else:
                policy["requiredChecks"][0]["appId"] = 12345
            change = metrics.summarize(self.payload(runs), policy)["changes"][0]
            self.assertFalse(change["comparable"], kind)
            self.assertIsNone(change["allRequiredChecksSeconds"], kind)

    def test_partial_retry_inherits_success_and_accounts_all_work(self):
        runs = self.runs()
        runs[0]["conclusion"] = "failure"
        runs[0]["jobs"][1]["conclusion"] = "failure"
        retry = copy.deepcopy(runs[0])
        retry.update(run_attempt=2, conclusion="success")
        retry["jobs"] = [dict(retry["jobs"][1], id=150, conclusion="success",
                              started_at="2026-09-02T00:03:00Z", completed_at="2026-09-02T00:03:20Z")]
        runs.append(retry)
        result = metrics.summarize(self.payload(runs), self.policy())
        change = result["changes"][0]
        self.assertTrue(change["comparable"], change["exclusionReasons"])
        self.assertEqual(200, change["allRequiredChecksSeconds"])
        self.assertEqual(110, change["qualityGateExecutionSeconds"])
        self.assertEqual(190, change["qualityGateElapsedSeconds"])
        self.assertAlmostEqual(220 / 60, change["runnerMinutes"])
        cost = result["events"]["pull_request"]["runnerAccounting"]
        self.assertEqual(1, cost["failures"]["attempts"])
        self.assertAlmostEqual(90 / 60, cost["failures"]["runnerMinutes"])
        self.assertAlmostEqual(20 / 60, cost["retries"]["runnerMinutes"])
        self.assertEqual(1, len(cost["unsuccessfulAttempts"]))

    def test_cancelled_retry_buckets_overlap_without_double_counting(self):
        runs = self.runs()
        runs[0].update(run_attempt=2, conclusion="cancelled")
        cost = metrics.summarize(self.payload(runs), self.policy())["events"]["pull_request"]["runnerAccounting"]
        self.assertEqual(cost["retries"]["runnerMinutes"], cost["cancellations"]["runnerMinutes"])
        self.assertEqual(cost["retries"]["runnerMinutes"], cost["failureCancellationOrRetry"]["runnerMinutes"])

    def test_missing_timestamps_keep_known_lower_bound_and_reject_comparison(self):
        runs = self.runs()
        runs[0]["jobs"][1]["completed_at"] = None
        result = metrics.summarize(self.payload(runs), self.policy())
        self.assertIsNone(result["changes"][0]["runnerMinutes"])
        cost = result["events"]["pull_request"]["runnerAccounting"]["total"]
        self.assertIsNone(cost["runnerMinutes"])
        self.assertEqual(3, cost["knownRunnerMinutes"])
        self.assertEqual(1, cost["unknownAttempts"])

    def test_prior_attempts_required_and_nonterminal_never_comparable(self):
        for update in ({"run_attempt": 2}, {"status": "in_progress", "conclusion": None},
                       {"collectionComplete": False}):
            runs = self.runs()
            runs[0].update(update)
            change = metrics.summarize(self.payload(runs), self.policy())["changes"][0]
            self.assertFalse(change["comparable"])

    def test_inherited_job_ids_and_duplicate_run_records_count_once(self):
        runs = self.runs()
        retry = copy.deepcopy(runs[0])
        retry["run_attempt"] = 2
        retry["jobs"].append(dict(retry["jobs"][1], id=199))
        retry["jobs"][-1].update(started_at="2026-09-02T00:03:00Z", completed_at="2026-09-02T00:03:20Z")
        result = metrics.summarize(self.payload([retry] + runs + [copy.deepcopy(runs[1])]), self.policy())
        self.assertAlmostEqual(220 / 60, result["changes"][0]["runnerMinutes"])
        self.assertEqual(3, result["changes"][0]["workflowAttempts"])

    def cohort_runs(self, count=20):
        return [run for i in range(count) for run in self.runs(i + 1, 2) + self.runs(i + 101, 20, .6)]

    def test_twenty_matched_changes_evaluate_original_targets(self):
        result = metrics.summarize(self.payload(self.cohort_runs()), self.policy())
        event = result["comparison"]["events"]["pull_request"]
        self.assertEqual(20, event["matchedCompletedRuns"])
        self.assertTrue(event["eligibleForTargetEvaluation"])
        self.assertTrue(event["qualityGateExecutionTarget"]["met"])
        self.assertTrue(event["runnerMinuteTarget"]["met"])
        self.assertAlmostEqual(.4, event["runnerMinuteTarget"]["observedReduction"])

    def test_nineteen_pairs_never_claim_savings(self):
        result = metrics.summarize(self.payload(self.cohort_runs(19)), self.policy())
        event = result["comparison"]["events"]["pull_request"]
        self.assertFalse(event["eligibleForTargetEvaluation"])
        self.assertIsNone(event["qualityGateExecutionTarget"]["observedReduction"])
        self.assertIsNone(event["runnerMinuteTarget"]["met"])

    def test_twenty_post_observations_are_reported_without_a_baseline(self):
        runs = [run for i in range(20) for run in self.runs(i + 101, 20, .6)]
        result = metrics.summarize(self.payload(runs), self.policy())
        event = result["comparison"]["events"]["pull_request"]
        self.assertTrue(event["rolloutSampleRequirementMet"])
        self.assertEqual(20, event["rolloutComparableCompleted"])
        self.assertEqual(0, event["matchedCompletedRuns"])
        self.assertIsNone(event["runnerMinuteTarget"]["observedReduction"])

    def test_events_categories_runner_classes_and_attempts_must_match(self):
        for mutate in (lambda r: r.update(event="push", change_key="push:" + r["head_sha"]),
                       lambda r: r.update(changes={"complete": True, "paths": ["docs/readme.md"]}),
                       lambda r: r["jobs"][0].update(labels=["windows-latest"])):
            runs = self.cohort_runs()
            for run in runs:
                if "2026-09-20" in run["created_at"]:
                    mutate(run)
            result = metrics.summarize(self.payload(runs), self.policy())
            for event in result["comparison"]["events"].values():
                self.assertEqual(0, event["matchedCompletedRuns"])
                self.assertIsNone(event["runnerMinuteTarget"]["met"])

    def test_unmatched_outlier_does_not_create_savings(self):
        runs = self.cohort_runs()
        unmatched = self.runs(1000, 20, .01)
        for run in unmatched:
            run["changes"]["paths"] = ["docs/only.md"]
        event = metrics.summarize(self.payload(runs + unmatched), self.policy())["comparison"]["events"]["pull_request"]
        self.assertEqual(20, event["matchedCompletedRuns"])
        self.assertEqual(1, event["unmatchedRollout"])
        self.assertAlmostEqual(.4, event["runnerMinuteTarget"]["observedReduction"])

    def test_incomplete_collection_or_historical_rules_prevents_claim(self):
        payload, policy = self.payload(self.cohort_runs()), self.policy()
        payload["collection"]["runDataComplete"] = False
        result = metrics.summarize(payload, policy)["comparison"]["events"]["pull_request"]
        self.assertFalse(result["eligibleForTargetEvaluation"])
        self.assertEqual(20, result["matchedCompletedRuns"])
        payload["collection"]["runDataComplete"] = True
        policy.pop("requiredChecksEvidence")
        result = metrics.summarize(payload, policy)["comparison"]["events"]["pull_request"]
        self.assertEqual(20, result["rolloutComparableCompleted"])
        self.assertFalse(result["eligibleForTargetEvaluation"])

    def test_invalid_window_or_missing_change_metadata_fails_closed(self):
        for period in ({"start": "not-a-date", "end": "2026-09-01T00:00:00Z"},
                       {"start": "2026-09-02T00:00:00Z", "end": "2026-09-04T00:00:00Z"}):
            policy = self.policy()
            policy["rollout"] = period
            self.assertEqual({}, metrics.summarize(self.payload(self.runs()), policy)["comparison"]["events"])
        runs = self.runs()
        runs[0]["changes"]["complete"] = False
        self.assertFalse(metrics.summarize(self.payload(runs), self.policy())["changes"][0]["comparable"])

    def test_markdown_and_backward_compatible_groups(self):
        result = metrics.summarize(self.payload(self.runs()), self.policy())
        report = metrics.render_markdown(result)
        self.assertIn("25% execution reduction: unavailable", report)
        self.assertIn("qualityGateExecutionSeconds", report)
        self.assertEqual(90, result["groups"]["Meridian CI / pull_request / attempt 1"]["medianRunnerSeconds"])

    def test_local_or_missing_benchmark_times_fail_closed(self):
        for field, value in (("testSeconds", None), ("testSeconds", "80"), ("testSeconds", float("nan")),
                             ("executionEnvironment", "local")):
            pairs = CiMetricsTests().pairs()
            pairs[0]["candidate"][field] = value
            self.assertFalse(metrics.evaluate_pairs(pairs)["eligibleForAdoption"])

    def test_valid_local_pairs_keep_measured_improvement_without_promoting_hosted_defaults(self):
        pairs = CiMetricsTests().pairs()
        for pair in pairs:
            for variant in ("baseline", "candidate"):
                pair[variant].update(executionEnvironment="local", runnerLabel="local-Linux-x86_64")
        result = metrics.evaluate_pairs(pairs)
        self.assertAlmostEqual(.2, result["medianPairedImprovement"])
        self.assertFalse(result["eligibleForAdoption"])
        self.assertEqual(["Local benchmark evidence cannot promote hosted defaults."], result["reasons"])
        pairs[0]["candidate"]["executionEnvironment"] = "github-actions"
        self.assertIsNone(metrics.evaluate_pairs(pairs)["medianPairedImprovement"])


    def test_execution_union_excludes_parallel_double_count_and_retry_idle(self):
        jobs = [dict(started_at="2026-09-02T00:00:10Z", completed_at="2026-09-02T00:01:10Z"),
                dict(started_at="2026-09-02T00:00:30Z", completed_at="2026-09-02T00:01:30Z"),
                dict(started_at="2026-09-02T01:00:00Z", completed_at="2026-09-02T01:00:20Z")]
        self.assertEqual(100, metrics.active_execution_seconds(jobs, "2026-09-02T01:00:20Z"))

    def test_failed_checks_keep_terminal_latency_outside_success_cohort(self):
        runs = self.runs()
        runs[1]["conclusion"] = "failure"
        runs[1]["jobs"][0]["conclusion"] = "failure"
        result = metrics.summarize(self.payload(runs), self.policy())
        self.assertEqual(130, result["changes"][0]["allRequiredChecksSeconds"])
        event = result["events"]["pull_request"]
        self.assertEqual(130, event["terminalRequiredCheckLatencySeconds"]["median"])
        self.assertEqual(0, event["metrics"]["allRequiredChecksSeconds"]["samples"])

    def test_app_binding_requires_identity_and_complete_check_collection(self):
        runs, policy = self.runs(), self.policy()
        policy["requiredChecks"][0]["appId"] = 15368
        runs[0]["jobs"][1]["check_app_id"] = 15368
        payload = self.payload(runs)
        payload["collection"]["checkDataComplete"] = True
        self.assertTrue(metrics.summarize(payload, policy)["changes"][0]["comparable"])

    def test_requested_windows_must_be_covered_by_export(self):
        payload = self.payload(self.cohort_runs())
        payload["window"]["since"] = "2026-09-02T00:00:00Z"
        result = metrics.summarize(payload, self.policy())["comparison"]["events"]["pull_request"]
        self.assertEqual(20, result["matchedCompletedRuns"])
        self.assertFalse(result["eligibleForTargetEvaluation"])
        self.assertIsNone(result["runnerMinuteTarget"]["observedReduction"])

    def test_interrupted_pair_produces_rejection_without_a_false_improvement(self):
        pairs = CiMetricsTests().pairs()
        pairs[0].pop("candidate")
        result = metrics.evaluate_pairs(pairs)
        self.assertFalse(result["eligibleForAdoption"])
        self.assertIsNone(result["medianPairedImprovement"])


    def test_quality_gate_scoped_execution_excludes_independent_work(self):
        runs, policy = self.runs(), self.policy()
        # Independent integrations overlap a queue-only gap between the quality lanes.
        runs[0]["jobs"][0]["completed_at"] = "2026-09-02T00:00:30Z"
        runs[0]["jobs"].append(dict(runs[0]["jobs"][0], name="independent-integration", id=999,
                                   started_at="2026-09-02T00:00:25Z", completed_at="2026-09-02T00:01:20Z"))
        policy["qualityGate"] = {"name": "quality-gate", "workflow": "Meridian CI",
                                 "executionJobs": ["test", "quality-gate"]}
        change = metrics.summarize(self.payload(runs), policy)["changes"][0]
        self.assertEqual(40, change["qualityGateExecutionSeconds"])
        self.assertEqual(90, change["qualityGateElapsedSeconds"])
        policy["qualityGate"]["executionJobs"].append("missing-validation")
        change = metrics.summarize(self.payload(runs), policy)["changes"][0]
        self.assertIsNone(change["qualityGateExecutionSeconds"])
        self.assertFalse(change["comparable"])

    def test_disjoint_collected_windows_do_not_imply_coverage_between_them(self):
        payload = self.payload(self.cohort_runs())
        payload.pop("window")
        payload["sourceCollectionWindows"] = [{"since": "2026-09-01T00:00:00Z", "until": "2026-09-15T00:00:00Z"},
                                              {"since": "2026-09-20T00:00:00Z", "until": "2026-10-01T00:00:00Z"}]
        policy = self.policy()
        result = metrics.summarize(payload, policy)["comparison"]["events"]["pull_request"]
        self.assertFalse(result["eligibleForTargetEvaluation"])
        policy["rollout"]["start"] = "2026-09-20T00:00:00Z"
        self.assertTrue(metrics.summarize(payload, policy)["comparison"]["events"]["pull_request"]["eligibleForTargetEvaluation"])

    def test_future_collection_window_cannot_claim_a_completed_period(self):
        payload = self.payload(self.cohort_runs())
        payload["collectedAt"] = "2026-09-25T00:00:00Z"
        result = metrics.summarize(payload, self.policy())["comparison"]["events"]["pull_request"]
        self.assertFalse(result["eligibleForTargetEvaluation"])
        self.assertIsNone(result["runnerMinuteTarget"]["observedReduction"])


    def test_partial_required_check_inventory_never_reports_all_checks_complete(self):
        payload = self.payload(self.runs())
        payload["collection"]["requiredChecksComplete"] = False
        result = metrics.summarize(payload)
        self.assertFalse(result["requiredCheckSetComplete"])
        self.assertIsNone(result["changes"][0]["allRequiredChecksSeconds"])
        self.assertFalse(result["changes"][0]["comparable"])
        # An explicit configured check set can define descriptive cohorts; historical
        # provenance is still required independently before any target claim.
        self.assertTrue(metrics.summarize(payload, self.policy())["requiredCheckSetComplete"])


    def test_explicit_second_resolution_inclusive_end_covers_exclusive_policy_end(self):
        payload = self.payload(self.cohort_runs())
        payload["window"].update(until="2026-09-30T23:59:59Z", untilInclusive=True)
        self.assertTrue(metrics.summarize(payload, self.policy())["comparison"]["events"]["pull_request"]["eligibleForTargetEvaluation"])
        payload["window"].pop("untilInclusive")
        self.assertFalse(metrics.summarize(payload, self.policy())["comparison"]["events"]["pull_request"]["eligibleForTargetEvaluation"])


    def test_explicit_skipped_job_has_no_execution_despite_api_placeholder_times(self):
        skipped = {"name": "nightly-coverage", "conclusion": "skipped",
                   "started_at": "2026-09-02T00:00:01Z", "completed_at": "2026-09-02T00:00:00Z"}
        self.assertEqual(0, metrics.job_record(skipped)["executionSeconds"])
        skipped["conclusion"] = "cancelled"
        self.assertIsNone(metrics.job_record(skipped)["executionSeconds"])


    def test_verified_empty_diff_is_a_separate_category_not_missing_evidence(self):
        self.assertEqual("no-file-change / 0 files", metrics.change_category({"changes": {"complete": True, "paths": []}}))
        self.assertIsNone(metrics.change_category({"changes": {"complete": False, "paths": []}}))


    def test_incomplete_attempts_are_separate_from_failures_cancellations_and_retries(self):
        runs = self.runs()
        runs[0].update(status="in_progress", conclusion=None)
        cost = metrics.summarize(self.payload(runs), self.policy())["events"]["pull_request"]["runnerAccounting"]
        self.assertEqual(1, cost["incomplete"]["attempts"])
        self.assertEqual(1, cost["unsuccessful"]["attempts"])
        self.assertEqual(0, cost["failureCancellationOrRetry"]["attempts"])


    def test_missing_optional_specialist_work_never_counts_as_savings(self):
        runs = self.cohort_runs()
        specialists = []
        for run in runs:
            if run["name"] == "CI" and run["created_at"] < "2026-09-15":
                specialist = copy.deepcopy(run)
                specialist.update(id=run["id"] + 10000, name="Specialist", path=".github/workflows/specialist.yml")
                specialist["jobs"][0].update(id=run["jobs"][0]["id"] + 10000, name="specialist-check")
                specialists.append(specialist)
        event = metrics.summarize(self.payload(runs + specialists), self.policy())["comparison"]["events"]["pull_request"]
        self.assertEqual(20, event["rolloutComparableCompleted"])
        self.assertEqual(0, event["matchedCompletedRuns"])
        self.assertIsNone(event["runnerMinuteTarget"]["observedReduction"])

    def test_same_coarse_category_cannot_hide_inconsistent_exact_scope(self):
        for difference in ({"baseSha": "different-base"}, {"headSha": "different-head"},
                           {"paths": ["src/Meridian.Core/Another.cs"]}):
            runs = copy.deepcopy(self.runs())
            runs[0]["changes"] = dict(runs[0]["changes"], **difference)
            change = metrics.summarize(self.payload(runs), self.policy())["changes"][0]
            self.assertFalse(change["comparable"])
            self.assertIn("Exact base/head/path snapshots differ across workflows for this change.", change["exclusionReasons"])


    def test_adjacent_collected_windows_prove_continuous_coverage(self):
        payload = self.payload(self.cohort_runs())
        payload.pop("window")
        payload["sourceCollectionWindows"] = [{"since": "2026-09-01T00:00:00Z", "until": "2026-09-15T00:00:00Z"},
                                              {"since": "2026-09-15T00:00:00Z", "until": "2026-09-25T00:00:00Z"},
                                              {"since": "2026-09-25T00:00:00Z", "until": "2026-10-01T00:00:00Z"}]
        self.assertTrue(metrics.summarize(payload, self.policy())["comparison"]["events"]["pull_request"]["eligibleForTargetEvaluation"])


    def test_plain_and_gzip_cli_inputs_produce_identical_reports(self):
        import gzip
        import json
        import subprocess
        import sys
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            plain = root / "source.json"
            compressed = root / "source.json.gz"
            content = json.dumps(self.payload(self.runs())).encode()
            plain.write_bytes(content)
            compressed.write_bytes(gzip.compress(content, mtime=0))
            outputs = []
            for index, source in enumerate((plain, compressed)):
                output = root / f"report-{index}.json"
                subprocess.run([sys.executable, spec.origin, "--input", str(source), "--output", str(output)],
                               check=True, stdout=subprocess.DEVNULL)
                outputs.append(json.loads(output.read_text()))
            self.assertEqual(outputs[0], outputs[1])


if __name__ == "__main__":
    unittest.main()
