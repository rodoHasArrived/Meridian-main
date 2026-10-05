import copy
import json
import subprocess
import sys
import tempfile
import importlib.util
from pathlib import Path
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/ci/ci-metrics.py"
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
        # Explicit per-job attempts prove that earlier IDs in the retry are inherited.
        for run in runs:
            for job in run["jobs"]:
                job["run_attempt"] = 1
        for job in retry["jobs"]:
            job["run_attempt"] = 2 if job["id"] == 199 else 1
        result = metrics.summarize(self.payload([retry] + runs), self.policy())
        self.assertAlmostEqual(220 / 60, result["changes"][0]["runnerMinutes"])
        self.assertEqual(3, result["changes"][0]["workflowAttempts"])
        with self.assertRaises(ValueError):
            metrics.summarize(self.payload(runs + [copy.deepcopy(runs[1])]), self.policy())

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
        self.assertIsNone(metrics.job_record(skipped)["executionSeconds"])
        self.assertEqual(0, metrics.job_record(skipped)["accountedExecutionSeconds"])
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


class UpstreamCiMetricsTests(unittest.TestCase):
    def job(self, **changes):
        job = dict(id=101, name="test", status="completed", conclusion="success",
                   created_at="2026-09-28T00:00:00Z", started_at="2026-09-28T00:05:00Z",
                   completed_at="2026-09-28T00:06:00Z", runner_id=7,
                   runner_name="Hosted Agent", runner_group_name="GitHub Actions",
                   labels=["ubuntu-latest"], steps=[dict(name="test", status="completed")])
        job.update(changes)
        return job

    def run_record(self, **changes):
        run = dict(id=1, name="CI", event="pull_request", run_attempt=1, head_sha="abc",
                   status="completed", conclusion="success", jobs=[self.job()])
        run.update(changes)
        return run

    def summarize_job(self, job, **payload_fields):
        result = metrics.summarize(dict(workflow_runs=[self.run_record(jobs=[job])],
                                        **payload_fields))
        return result["groups"]["CI / pull_request / attempt 1"]["runs"][0]["jobs"][0]

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
        run = self.run_record()
        retry = copy.deepcopy(run)
        retry["run_attempt"] = 2
        result = metrics.summarize({"workflow_runs": [run, retry]})
        self.assertEqual(2, result["schemaVersion"])
        self.assertEqual(2, len(result["groups"]))
        first = result["groups"]["CI / pull_request / attempt 1"]
        self.assertEqual(60, first["medianRunnerSeconds"])
        self.assertEqual(300, first["runs"][0]["jobs"][0]["queueSeconds"])
        self.assertFalse(first["runs"][0]["isRetry"])
        second = result["groups"]["CI / pull_request / attempt 2"]["runs"][0]
        self.assertTrue(second["isRetry"])
        self.assertEqual(2, second["runAttempt"])
        self.assertEqual(2, second["jobs"][0]["runAttempt"])

    def test_seconds_distinguishes_unavailable_from_zero(self):
        start = "2026-09-28T00:00:00Z"
        for begin, end in [(None, start), (start, None), ("", start),
                           ("not-a-date", start), (start, "not-a-date"),
                           ("2026-09-28T00:00:00", start),
                           (start, "2026-09-28T00:00:00"),
                           (start, "2026-09-27T23:59:59Z")]:
            with self.subTest(begin=begin, end=end):
                self.assertIsNone(metrics.seconds(begin, end))
        self.assertEqual(0, metrics.seconds(start, start))
        self.assertEqual(60, metrics.seconds(start, "2026-09-27T17:01:00-07:00"))

    def test_started_job_retains_runner_and_timing_evidence(self):
        job = self.summarize_job(self.job())
        for field, expected in dict(jobId=101, status="completed", runAttempt=1,
                                    runnerId=7, runnerName="Hosted Agent",
                                    runnerGroupName="GitHub Actions", labels=["ubuntu-latest"],
                                    createdAt="2026-09-28T00:00:00Z",
                                    startedAt="2026-09-28T00:05:00Z",
                                    completedAt="2026-09-28T00:06:00Z",
                                    startState="started", timingIssue=None, queueSeconds=300,
                                    waitSeconds=300, waitEnd="started", executionSeconds=60).items():
            with self.subTest(field=field):
                self.assertEqual(expected, job[field])

    def test_cancelled_before_start_keeps_wait_without_execution(self):
        job = self.summarize_job(self.job(conclusion="cancelled", started_at=None,
                                          runner_id=0, runner_name="", steps=[]))
        self.assertEqual("not_started", job["startState"])
        self.assertEqual(360, job["waitSeconds"])
        self.assertEqual("completed", job["waitEnd"])
        self.assertIsNone(job["queueSeconds"])
        self.assertIsNone(job["executionSeconds"])

    def test_synthetic_start_on_cancelled_job_is_not_execution(self):
        job = self.summarize_job(self.job(conclusion="cancelled", runner_id=0,
                                          runner_name="", steps=[]))
        self.assertEqual("2026-09-28T00:05:00Z", job["startedAt"])
        self.assertEqual("not_started", job["startState"])
        self.assertEqual(360, job["waitSeconds"])
        self.assertEqual("completed", job["waitEnd"])
        self.assertIsNone(job["queueSeconds"])
        self.assertIsNone(job["executionSeconds"])

    def test_null_runner_cancellation_with_reversed_synthetic_times_is_unavailable(self):
        job = self.summarize_job(self.job(
            conclusion="cancelled", runner_id=None, runner_name=None, steps=[],
            created_at="2026-09-28T20:25:47Z", started_at="2026-09-28T20:25:47Z",
            completed_at="2026-09-28T20:25:46Z"))
        self.assertEqual("not_started", job["startState"])
        for field in ("queueSeconds", "waitSeconds", "waitEnd", "executionSeconds"):
            self.assertIsNone(job[field], field)

    def test_queued_statuses_use_snapshot_wait(self):
        for status in ("queued", "waiting", "pending", "requested"):
            with self.subTest(status=status):
                job = self.summarize_job(
                    self.job(status=status, conclusion=None, started_at=None, completed_at=None,
                             runner_id=0, runner_name="", steps=[]),
                    observed_at="2026-09-28T00:10:00Z")
                self.assertEqual("not_started", job["startState"])
                self.assertEqual(600, job["waitSeconds"])
                self.assertEqual("observed", job["waitEnd"])
                self.assertIsNone(job["queueSeconds"])
                self.assertIsNone(job["executionSeconds"])

    def test_queued_job_without_snapshot_has_unknown_wait(self):
        job = self.summarize_job(self.job(status="queued", conclusion=None, started_at=None,
                                          completed_at=None, runner_id=0, runner_name="", steps=[]))
        self.assertIsNone(job["waitSeconds"])
        self.assertIsNone(job["waitEnd"])

    def test_terminal_job_does_not_keep_waiting_until_snapshot(self):
        job = self.summarize_job(
            self.job(conclusion="cancelled", started_at=None, completed_at=None,
                     runner_id=0, runner_name="", steps=[]),
            observed_at="2026-09-28T00:10:00Z")
        self.assertIsNone(job["waitSeconds"])
        self.assertIsNone(job["waitEnd"])

    def test_skipped_job_has_no_execution_even_with_timestamps(self):
        job = self.summarize_job(self.job(conclusion="skipped", runner_id=0,
                                          runner_name="", steps=[]))
        self.assertEqual("not_started", job["startState"])
        self.assertIsNone(job["queueSeconds"])
        self.assertIsNone(job["executionSeconds"])

    def test_missing_start_with_runner_or_steps_is_unknown(self):
        examples = [self.job(started_at=None, steps=[]),
                    self.job(started_at=None, runner_id=0, runner_name="", steps=[
                        dict(name="test", status="completed", conclusion="success",
                             started_at="2026-09-28T00:05:00Z"),
                    ])]
        for fixture in examples:
            with self.subTest(fixture=fixture):
                job = self.summarize_job(fixture)
                self.assertEqual("unknown", job["startState"])
                self.assertIsNone(job["executionSeconds"])

    def test_executed_step_without_timestamps_does_not_claim_job_never_started(self):
        for conclusion in ("success", "failure", "timed_out"):
            with self.subTest(conclusion=conclusion):
                job = self.summarize_job(self.job(
                    started_at=None, runner_id=None, runner_name=None,
                    steps=[dict(name="test", status="completed", conclusion=conclusion)]))
                self.assertEqual("unknown", job["startState"])
                self.assertIsNone(job["queueSeconds"])
                self.assertIsNone(job["executionSeconds"])

    def test_skipped_steps_do_not_prove_runner_execution(self):
        job = self.summarize_job(self.job(
            conclusion="cancelled", runner_id=None, runner_name=None,
            steps=[dict(name="test", status="completed", conclusion="skipped",
                        started_at="2026-09-28T00:05:00Z")]))
        self.assertEqual("not_started", job["startState"])
        self.assertIsNone(job["executionSeconds"])

    def test_missing_start_and_runner_evidence_is_unknown(self):
        for conclusion in ("success", "cancelled"):
            for include_null_start in (False, True):
                with self.subTest(conclusion=conclusion, null_start=include_null_start):
                    fixture = dict(name="legacy job", status="completed", conclusion=conclusion,
                                   created_at="2026-09-28T00:00:00Z",
                                   completed_at="2026-09-28T00:06:00Z")
                    if include_null_start:
                        fixture["started_at"] = None
                    result = metrics.summarize({"workflow_runs": [
                        self.run_record(conclusion=conclusion, jobs=[fixture]),
                    ]})
                    job = result["groups"]["CI / pull_request / attempt 1"]["runs"][0]["jobs"][0]
                    self.assertEqual("unknown", job["startState"])
                    for field in ("queueSeconds", "waitSeconds", "waitEnd", "executionSeconds"):
                        self.assertIsNone(job[field], field)
                    self.assertEqual(0, result["summary"]["notStartedJobs"])
                    self.assertEqual(0, result["summary"]["cancelledBeforeStartJobs"])

    def test_legacy_timestamp_records_remain_measurable(self):
        fixture = self.job()
        for field in ("runner_id", "runner_name", "runner_group_name", "steps"):
            fixture.pop(field)
        job = self.summarize_job(fixture)
        self.assertEqual("started", job["startState"])
        self.assertEqual(300, job["queueSeconds"])
        self.assertEqual(60, job["executionSeconds"])

    def test_retry_carried_forward_timestamps_do_not_count_again(self):
        run = self.run_record(run_attempt=2, jobs=[self.job(
            created_at="2026-09-28T00:10:00Z", run_attempt=2)])
        group = metrics.summarize({"workflow_runs": [run]})["groups"]["CI / pull_request / attempt 2"]
        job = group["runs"][0]["jobs"][0]
        self.assertEqual("started_before_created", job["timingIssue"])
        for field in ("queueSeconds", "waitSeconds", "executionSeconds"):
            self.assertIsNone(job[field], field)
        self.assertIsNone(group["runs"][0]["runnerSeconds"])
        self.assertIsNone(group["runs"][0]["knownRunnerSeconds"])
        self.assertEqual(0, group["successfulSamples"])

    def test_invalid_started_timestamp_is_reported_without_crashing(self):
        for timestamp in ("invalid", "2026-09-28T00:05:00"):
            with self.subTest(timestamp=timestamp):
                job = self.summarize_job(self.job(started_at=timestamp))
                self.assertEqual("invalid_started_at", job["timingIssue"])
                self.assertIsNone(job["queueSeconds"])
                self.assertIsNone(job["executionSeconds"])

    def test_started_job_can_have_incomplete_execution(self):
        job = self.summarize_job(self.job(status="in_progress", conclusion=None,
                                          completed_at=None),
                                 observed_at="2026-09-28T00:10:00Z")
        self.assertEqual("started", job["startState"])
        self.assertEqual(300, job["queueSeconds"])
        self.assertEqual(300, job["waitSeconds"])
        self.assertEqual("started", job["waitEnd"])
        self.assertIsNone(job["executionSeconds"])

    def test_summary_counts_attempts_cancellations_and_partial_execution(self):
        cancelled = self.run_record(conclusion="cancelled", jobs=[
            self.job(conclusion="cancelled"),
            self.job(id=102, conclusion="cancelled", started_at=None,
                     runner_id=0, runner_name="", steps=[]),
        ])
        retry = self.run_record(run_attempt=2, jobs=[self.job(completed_at="2026-09-28T00:07:00Z")])
        queued = self.run_record(id=2, status="queued", conclusion=None, jobs=[
            self.job(status="queued", conclusion=None, started_at=None, completed_at=None,
                     runner_id=0, runner_name="", steps=[]),
        ])
        no_jobs = self.run_record(id=3, jobs=[])
        missing_jobs = self.run_record(id=4, conclusion="cancelled")
        missing_jobs.pop("jobs")
        result = metrics.summarize(dict(workflow_runs=[cancelled, retry, queued, no_jobs, missing_jobs],
                                        observed_at="2026-09-28T00:10:00Z"))
        self.assertEqual(dict(runAttempts=5, uniqueRuns=4, retryAttempts=1, cancelledRunAttempts=2,
                              jobs=4, notStartedJobs=2, cancelledJobs=2, cancelledBeforeStartJobs=1,
                              queuedJobs=1, unavailableExecutionJobs=2, knownRunnerSeconds=180,
                              cancelledRunKnownRunnerSeconds=60, retryKnownRunnerSeconds=120,
                              queueSamples=2, medianQueueSeconds=300,
                              executionSamples=2, medianExecutionSeconds=90,
                              waitSamples=4, medianWaitSeconds=330),
                         result["summary"])
        first = result["groups"]["CI / pull_request / attempt 1"]
        self.assertEqual(4, first["summary"]["runAttempts"])
        self.assertEqual(60, first["summary"]["knownRunnerSeconds"])
        self.assertIsNone(first["runs"][0]["runnerSeconds"])
        self.assertEqual(60, first["runs"][0]["knownRunnerSeconds"])
        self.assertEqual("cancelled", first["runs"][0]["conclusion"])
        self.assertEqual("completed", first["runs"][0]["status"])
        self.assertEqual(0, first["successfulSamples"])

    def test_no_measured_execution_is_null_instead_of_zero(self):
        result = metrics.summarize({"workflow_runs": [self.run_record(conclusion="cancelled", jobs=[
            self.job(conclusion="cancelled", started_at=None, runner_id=0, runner_name="", steps=[]),
        ])]})
        for field in ("knownRunnerSeconds", "cancelledRunKnownRunnerSeconds", "retryKnownRunnerSeconds"):
            self.assertIsNone(result["summary"][field], field)

    def test_measured_zero_execution_remains_valid(self):
        result = metrics.summarize({"workflow_runs": [self.run_record(jobs=[
            self.job(completed_at="2026-09-28T00:05:00Z"),
        ])]})
        group = result["groups"]["CI / pull_request / attempt 1"]
        self.assertEqual(0, result["summary"]["knownRunnerSeconds"])
        self.assertEqual(0, group["runs"][0]["runnerSeconds"])
        self.assertEqual(0, group["medianRunnerSeconds"])
        self.assertEqual(1, group["successfulSamples"])

    def test_empty_and_missing_job_lists_remain_distinct(self):
        missing = self.run_record(id=2)
        missing.pop("jobs")
        result = metrics.summarize({"workflow_runs": [self.run_record(jobs=[]), missing]})
        runs = result["groups"]["CI / pull_request / attempt 1"]["runs"]
        self.assertEqual([True, False], [run["jobsAvailable"] for run in runs])
        for run in runs:
            self.assertEqual([], run["jobs"])
            self.assertIsNone(run["runnerSeconds"])
            self.assertIsNone(run["knownRunnerSeconds"])
        self.assertEqual(2, result["summary"]["runAttempts"])
        self.assertEqual(0, result["summary"]["jobs"])

    def test_success_median_excludes_failed_and_incomplete_runs(self):
        result = metrics.summarize({"workflow_runs": [
            self.run_record(),
            self.run_record(id=2, conclusion="failure", jobs=[
                self.job(conclusion="failure", completed_at="2026-09-28T00:15:00Z"),
            ]),
            self.run_record(id=3, jobs=[self.job(), self.job(id=102, conclusion="skipped",
                            started_at=None, completed_at=None, runner_id=0, runner_name="", steps=[])]),
        ]})
        group = result["groups"]["CI / pull_request / attempt 1"]
        self.assertEqual(1, group["successfulSamples"])
        self.assertEqual(60, group["medianRunnerSeconds"])

    def test_retry_history_reports_missing_attempts(self):
        result = metrics.summarize({"workflow_runs": [
            self.run_record(run_attempt=4), self.run_record(run_attempt=2),
        ]})
        self.assertEqual([dict(runId=1, observedAttempts=[2, 4], missingAttempts=[1, 3])],
                         result["retryHistory"])
        self.assertEqual(2, result["summary"]["retryAttempts"])
        self.assertEqual(1, result["summary"]["uniqueRuns"])

    def test_duplicate_attempt_is_rejected_instead_of_double_counted(self):
        with self.assertRaises(ValueError):
            metrics.summarize({"workflow_runs": [self.run_record(), self.run_record()]})

    def test_jobs_from_a_different_attempt_are_rejected(self):
        with self.assertRaises(ValueError):
            metrics.summarize({"workflow_runs": [self.run_record(run_attempt=2, jobs=[
                self.job(run_attempt=1),
            ])]})

    def test_cli_round_trips_json_and_creates_output_directory(self):
        payload = dict(observed_at="2026-09-28T00:10:00Z", workflow_runs=[
            self.run_record(status="queued", conclusion=None, jobs=[self.job(
                status="queued", conclusion=None, started_at=None, completed_at=None,
                runner_id=0, runner_name="", steps=[])]),
        ])
        with tempfile.TemporaryDirectory() as directory:
            input_path = Path(directory) / "runs.json"
            output_path = Path(directory) / "nested" / "summary.json"
            input_path.write_text(json.dumps(payload), encoding="utf-8")
            process = subprocess.run([sys.executable, str(SCRIPT), "--input", str(input_path),
                                      "--output", str(output_path)], capture_output=True, text=True)
            self.assertEqual(0, process.returncode, process.stderr)
            result = json.loads(output_path.read_text(encoding="utf-8"))
            self.assertEqual(metrics.summarize(payload), result)
            self.assertEqual(result, json.loads(process.stdout))


class TimingAccountingIntegrationTests(unittest.TestCase):
    def test_new_id_carried_success_requires_prior_execution_and_costs_zero(self):
        fixture = UpstreamCiMetricsTests()
        first = fixture.run_record(jobs=[fixture.job(run_attempt=1, runner_group_id=0)])
        retry = fixture.run_record(run_attempt=2, jobs=[fixture.job(
            id=201, run_attempt=2, created_at="2026-09-28T00:10:00Z")])
        result = metrics.summarize({"workflow_runs": [retry, first]})
        run = result["groups"]["CI / pull_request / attempt 2"]["runs"][0]
        job = run["jobs"][0]
        self.assertEqual("started_before_created", job["timingIssue"])
        self.assertEqual(1, job["inheritedFromAttempt"])
        self.assertIsNone(job["executionSeconds"])
        self.assertEqual(0, job["accountedExecutionSeconds"])
        self.assertIsNone(run["runnerSeconds"])
        self.assertEqual(0, run["runnerMinutes"])
        self.assertEqual(60, result["summary"]["knownRunnerSeconds"])
        alone = metrics.summarize({"workflow_runs": [retry]})
        self.assertIsNone(alone["groups"]["CI / pull_request / attempt 2"]["runs"][0]["runnerMinutes"])
        mismatched = copy.deepcopy(retry)
        mismatched["jobs"][0]["runner_id"] = 99
        unproven = metrics.summarize({"workflow_runs": [first, mismatched]})
        self.assertIsNone(unproven["groups"]["CI / pull_request / attempt 2"]["runs"][0]["runnerMinutes"])

    def test_prior_skipped_job_with_source_attempt_is_retained_without_execution(self):
        fixture = UpstreamCiMetricsTests()
        skipped = fixture.job(conclusion="skipped", runner_id=0, runner_name="", steps=[], run_attempt=1)
        first, retry = fixture.run_record(jobs=[skipped]), fixture.run_record(run_attempt=2, jobs=[skipped])
        result = metrics.summarize({"workflow_runs": [first, retry]})
        job = result["groups"]["CI / pull_request / attempt 2"]["runs"][0]["jobs"][0]
        self.assertEqual(1, job["inheritedFromAttempt"])
        self.assertIsNone(job["executionSeconds"])
        self.assertEqual(0, job["accountedExecutionSeconds"])

    def test_terminal_unstarted_cancellation_has_wait_but_no_runner_cost(self):
        fixture = UpstreamCiMetricsTests()
        run = fixture.run_record(conclusion="cancelled", jobs=[fixture.job(
            conclusion="cancelled", runner_id=0, runner_name="", steps=[])])
        result = metrics.summarize({"workflow_runs": [run]})
        raw = result["groups"]["CI / pull_request / attempt 1"]["runs"][0]
        self.assertIsNone(raw["runnerSeconds"])
        self.assertIsNone(raw["knownRunnerSeconds"])
        self.assertEqual(0, raw["runnerMinutes"])
        self.assertEqual(360, raw["jobs"][0]["waitSeconds"])
        self.assertEqual(0, result["events"]["pull_request"]["runnerAccounting"]["cancellations"]["runnerMinutes"])

    def test_duplicate_job_id_and_invalid_run_metadata_are_rejected(self):
        fixture = UpstreamCiMetricsTests()
        bad_runs = [fixture.run_record(jobs=[fixture.job(), fixture.job()]),
                    fixture.run_record(jobs={"jobs": []}), fixture.run_record(run_attempt=0),
                    fixture.run_record(run_attempt=True), fixture.run_record(run_attempt="2")]
        for run in bad_runs:
            with self.subTest(run=run), self.assertRaises(ValueError):
                metrics.summarize({"workflow_runs": [run]})

    def test_synthetic_or_unproven_carried_intervals_cannot_inflate_quality_execution(self):
        fixture = UpstreamCiMetricsTests()
        synthetic = fixture.job(conclusion="cancelled", runner_id=0, runner_name="", steps=[])
        real = fixture.job(started_at="2026-09-28T00:10:00Z", completed_at="2026-09-28T00:11:00Z")
        self.assertEqual(60, metrics.active_execution_seconds([synthetic, real], "2026-09-28T00:11:00Z"))
        copied = fixture.job(created_at="2026-09-28T00:10:00Z")
        self.assertIsNone(metrics.active_execution_seconds([copied, real], "2026-09-28T00:11:00Z"))


if __name__ == "__main__":
    unittest.main()
