from __future__ import annotations

import argparse
from contextlib import redirect_stderr, redirect_stdout
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest


MODULE_PATH = Path(__file__).resolve().parents[2] / "build/python/cli/validation_metrics.py"
spec = importlib.util.spec_from_file_location("validation_metrics_under_test", MODULE_PATH)
metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metrics)


def receipt(run_id="run", **updates):
    return {
        "schemaVersion": 2, "runId": run_id, "status": "passed", "project": "tests/Test.csproj",
        "configuration": "Release", "filter": "Example", "framework": None, "runtime": None,
        "startedAt": "2026-10-28T00:00:00Z", "finishedAt": "2026-10-28T00:00:30Z",
        "queueSeconds": 5, "executionSeconds": 25,
        "steps": [{"name": "restore", "exitCode": 0, "durationSeconds": 10},
                  {"name": "build", "exitCode": 0, "durationSeconds": 10},
                  {"name": "test", "exitCode": 0, "durationSeconds": 5}],
        **updates,
    }


class ValidationMetricsTests(unittest.TestCase):
    def test_empty_observations_are_null_not_zero(self):
        report = metrics.summarize([])
        self.assertEqual(report["runs"], 0)
        self.assertEqual(report["metrics"]["feedbackSeconds"],
                         {"samples": 0, "missing": 0, "median": None, "p95": None})
        self.assertEqual(report["tasks"]["groups"], [])

    def test_distributions_include_missing_and_nearest_rank_p95(self):
        distribution = metrics.distribution(list(range(1, 21)) + [None, -1, float("nan"), float("inf"), True, "10"])
        self.assertEqual(distribution, {"samples": 20, "missing": 6, "median": 10.5, "p95": 19})
        self.assertEqual(metrics.distribution([0])["median"], 0)
        self.assertIsNone(metrics.number(10 ** 500))

    def test_legacy_receipts_do_not_infer_waiting(self):
        legacy = receipt()
        for field in ("schemaVersion", "queueSeconds", "executionSeconds"):
            legacy.pop(field)
        report = metrics.summarize([legacy])
        self.assertEqual(report["metrics"]["feedbackSeconds"]["median"], 30)
        self.assertEqual(report["metrics"]["queueSeconds"]["missing"], 1)
        self.assertIsNone(report["metrics"]["queueSeconds"]["median"])
        self.assertEqual(report["steps"]["test"]["median"], 5)

    def test_explicit_subsecond_feedback_precedes_rounded_timestamps(self):
        record = receipt(feedbackSeconds=.125, finishedAt="2026-10-28T00:00:00Z")
        self.assertEqual(metrics.feedback_seconds(record), .125)
        self.assertEqual(metrics.summarize([record])["metrics"]["feedbackSeconds"]["median"], .125)
        self.assertIsNone(metrics.feedback_seconds(receipt(status="started", feedbackSeconds=.125)))

    def test_invalid_explicit_feedback_is_not_masked_by_legacy_timestamps(self):
        for value in (None, -1, True, "0.5", float("nan"), float("inf")):
            with self.subTest(value=value):
                self.assertIsNone(metrics.feedback_seconds(receipt(feedbackSeconds=value)))

    def test_monotonic_feedback_keeps_explicit_task_grouping(self):
        records = [receipt("a", taskId="task", status="failed", feedbackSeconds=.125),
                   receipt("b", taskId="task", feedbackSeconds=.25,
                           startedAt="2026-10-28T00:01:00Z", finishedAt="2026-10-28T00:01:00Z")]
        group = metrics.summarize(records)["tasks"]["groups"][0]
        self.assertEqual(group["repeatedAttemptFeedbackSeconds"], .25)
        self.assertEqual(group["timeToFirstPassSeconds"], 60)

    def test_explicit_feedback_does_not_hide_invalid_task_completion_timestamp(self):
        for finish in (None, "invalid", "2026-10-27T00:00:00Z"):
            with self.subTest(finish=finish):
                record = receipt(taskId="task", feedbackSeconds=.125, finishedAt=finish)
                report = metrics.summarize([record])
                self.assertEqual(report["metrics"]["feedbackSeconds"]["median"], .125)
                self.assertIsNone(report["tasks"]["groups"][0]["timeToFirstPassSeconds"])

    def test_invalid_timestamps_and_unfinished_are_unavailable(self):
        records = [receipt("a", startedAt="2026-10-28T00:00:00"),
                   receipt("b", finishedAt="2026-10-27T00:00:00Z"),
                   receipt("c", status="started"), receipt("d", finishedAt=None)]
        report = metrics.summarize(records)
        self.assertEqual(report["metrics"]["feedbackSeconds"]["missing"], 4)
        self.assertEqual(report["statuses"]["started"], 1)
        self.assertIsNone(metrics.timestamp("not-a-timestamp"))
        self.assertEqual(metrics.elapsed("2026-10-28T01:00:00+01:00", "2026-10-28T00:00:30Z"), 30)

    def test_first_failed_step_is_reported_without_inferring_cause(self):
        failed = receipt(status="failed", failurePhase="test", steps=[
            {"name": "restore", "exitCode": 0}, {"name": "build", "exitCode": 1},
            {"name": "test", "exitCode": 1}])
        report = metrics.summarize([failed, receipt("b", status="failed", steps=[]),
                                    receipt("c", status="blocked", steps=[])])
        self.assertEqual(report["failurePhases"], {"build": 1, "unavailable": 1})
        self.assertEqual(report["steps"]["build"]["missing"], 1)
        self.assertEqual(report["statuses"]["blocked"], 1)

    def test_explicit_comparable_task_retries(self):
        first = receipt("first", taskId="task", status="failed")
        second = receipt("second", taskId="task", startedAt="2026-10-28T00:01:00Z",
                         finishedAt="2026-10-28T00:01:45Z")
        report = metrics.summarize([second, first, receipt("ungrouped")])
        group = report["tasks"]["groups"][0]
        self.assertEqual(group["attempts"], 2)
        self.assertEqual(group["repeatedAttempts"], 1)
        self.assertEqual(group["timeToFirstPassSeconds"], 105)
        self.assertEqual(group["repeatedAttemptFeedbackSeconds"], 45)
        self.assertEqual(report["tasks"]["ungroupedRuns"], 1)

    def test_different_scope_or_task_is_not_merged(self):
        records = [receipt("a", taskId="task"), receipt("b", taskId="other")]
        for field in metrics.SCOPE_FIELDS:
            records.append(receipt(field, taskId="task", **{field: "different"}))
        report = metrics.summarize(records)
        self.assertEqual(len(report["tasks"]["groups"]), 7)
        self.assertTrue(all(item["attempts"] == 1 for item in report["tasks"]["groups"]))

    def test_missing_start_makes_group_order_and_latency_unknown(self):
        report = metrics.summarize([receipt("a", taskId="task", startedAt=None),
                                    receipt("b", taskId="task")])
        group = report["tasks"]["groups"][0]
        self.assertIsNone(group["timeToFirstPassSeconds"])
        self.assertIsNone(group["repeatedAttemptFeedbackSeconds"])

    def test_unfinished_repeat_effort_is_unknown(self):
        report = metrics.summarize([receipt("a", taskId="task"),
                                   receipt("b", taskId="task", status="started",
                                           startedAt="2026-10-28T00:01:00Z", finishedAt=None)])
        self.assertIsNone(report["tasks"]["groups"][0]["repeatedAttemptFeedbackSeconds"])

    def test_loader_warns_for_bad_inputs_and_filters_tasks(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            (directory / "good.json").write_text(json.dumps(receipt(taskId="selected")))
            (directory / "other.json").write_text(json.dumps(receipt("other", taskId="other")))
            for name, value in {"array": [], "schema": receipt(schemaVersion=99),
                                "steps": receipt(steps=[None]), "status": receipt(status="mystery")}.items():
                (directory / f"{name}.json").write_text(json.dumps(value))
            (directory / "bad.json").write_text("{broken")
            records, warnings = metrics.load_receipts(directory, "selected")
            self.assertEqual(len(records), 1)
            self.assertEqual(len(warnings), 5)
            self.assertTrue(any("unsupported schemaVersion" in item for item in warnings))

    def test_duplicate_run_ids_are_excluded_not_double_counted(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            for name in ("first.json", "copy.json"):
                (directory / name).write_text(json.dumps(receipt()))
            records, warnings = metrics.load_receipts(directory)
        self.assertEqual(records, [])
        self.assertTrue(any("duplicate runId" in item for item in warnings))

    def test_command_writes_report_without_modifying_source(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            directory = root / ".ai/validation-runs"
            directory.mkdir(parents=True)
            source = directory / "run.json"
            source.write_text(json.dumps(receipt()))
            before = source.read_bytes()
            args = self.parse("--json-output", "report.json", "--summary")
            stdout = io.StringIO()
            with redirect_stdout(stdout):
                code = metrics.cmd_validation_metrics(args, root)
            self.assertEqual(code, 0)
            self.assertEqual(source.read_bytes(), before)
            self.assertEqual(list(directory.iterdir()), [source])
            self.assertEqual(json.loads((root / "report.json").read_text())["runs"], 1)
            self.assertIn("not developer work time", stdout.getvalue())

    def test_default_command_prints_json_and_empty_directory_warning(self):
        with tempfile.TemporaryDirectory() as temp, redirect_stdout(io.StringIO()) as output:
            self.assertEqual(metrics.cmd_validation_metrics(self.parse(), Path(temp)), 0)
            report = json.loads(output.getvalue())
        self.assertEqual(report["runs"], 0)
        self.assertTrue(report["warnings"])

    def test_report_cannot_overwrite_receipt_through_external_hardlink(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            directory = root / ".ai/validation-runs"
            directory.mkdir(parents=True)
            source = directory / "run.json"
            source.write_text(json.dumps(receipt()))
            before = source.read_bytes()
            target = root / "report.json"
            target.hardlink_to(source)
            with redirect_stderr(io.StringIO()) as error:
                code = metrics.cmd_validation_metrics(self.parse("--json-output", str(target)), root)
            self.assertEqual(code, 2)
            self.assertIn("must not alias a source receipt", error.getvalue())
            self.assertEqual(source.read_bytes(), before)
            self.assertEqual(target.read_bytes(), before)

    def test_report_cannot_replace_or_create_source_receipts(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            directory = root / ".ai/validation-runs"
            directory.mkdir(parents=True)
            source = directory / "run.json"
            source.write_text(json.dumps(receipt()))
            before = source.read_bytes()
            for name in ("run.json", "new.json"):
                with redirect_stderr(io.StringIO()):
                    code = metrics.cmd_validation_metrics(self.parse("--json-output", str(directory / name)), root)
                self.assertEqual(code, 2)
            self.assertEqual(source.read_bytes(), before)
            self.assertFalse((directory / "new.json").exists())

    def parse(self, *arguments):
        parser = argparse.ArgumentParser()
        metrics.add_parser(parser.add_subparsers(dest="command", required=True))
        return parser.parse_args(["validation-metrics", *arguments])


if __name__ == "__main__":
    unittest.main()
