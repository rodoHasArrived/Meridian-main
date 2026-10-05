import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "build/scripts/ci/ci-metrics.py"
spec = importlib.util.spec_from_file_location("ci_metrics", SCRIPT)
metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metrics)


class CiMetricsTests(unittest.TestCase):
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


if __name__ == "__main__":
    unittest.main()
