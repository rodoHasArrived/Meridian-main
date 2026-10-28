"""Evidence integrity and metric semantics for the throughput pilot."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

MODULE_PATH = Path(__file__).resolve().parents[2] / "build/scripts/ai/throughput_metrics.py"
spec = importlib.util.spec_from_file_location("throughput_metrics", MODULE_PATH)
metrics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(metrics)
DIGEST = "a" * 64
SHA = "b" * 40


def event(kind, seconds=0, trial="one", **fields):
    return {"trial": trial, "event": kind, "timestamp": f"2026-10-28T12:00:{seconds:02d}+00:00", **fields}


def start(trial="one", cohort="baseline", pair="pair"):
    return event("task_started", trial=trial, pair=pair, cohort=cohort)


def finish(seconds=20, trial="one", complete=True):
    return event("task_finished", seconds, trial=trial, instrumentationComplete=complete)


class ThroughputMetricsTests(unittest.TestCase):
    def test_validated_completion_includes_discovery_and_all_validation_wait(self):
        events = [start(), event("useful_edit", 3, path="a.py", sha256=DIGEST),
                  event("validation_started", 4, validationId="first"),
                  event("validation_finished", 6, validationId="first", result="fail"),
                  event("correction", 7), event("validation_started", 8, validationId="retry"),
                  event("validation_finished", 12, validationId="retry", result="pass"), finish(15)]
        row = metrics.summarize_trial(events)
        self.assertEqual(row["validatedCompletionSeconds"], 15)
        self.assertEqual(row["validationWaitSeconds"], 6)
        self.assertTrue(row["comparisonEligible"])
        self.assertIsNone(metrics.summarize_trial(events[:-1])["validatedCompletionSeconds"])
        self.assertIsNone(metrics.summarize_trial(events[:-1] + [finish(15, complete=False)])["validatedCompletionSeconds"])
        failed = [start(), event("validation_started", 2, validationId="v"),
                  event("validation_finished", 4, validationId="v", result="fail"), finish()]
        self.assertIsNone(metrics.summarize_trial(failed)["validatedCompletionSeconds"])

    def test_first_edit_and_identical_reads_only(self):
        events = [start(), event("file_read", 1, path="a.py", sha256=DIGEST),
                  event("file_read", 2, path="a.py", sha256=DIGEST),
                  event("file_read", 3, path="a.py", sha256="b" * 64),
                  event("useful_edit", 4, path="a.py", sha256="b" * 64),
                  event("correction", 5), finish()]
        row = metrics.summarize_trial(events)
        self.assertEqual(row["firstUsefulEditSeconds"], 4)
        self.assertEqual(row["repeatedFileReads"], 1)
        self.assertEqual(row["correctionCycles"], 1)
        self.assertIsNone(row["passingPrSeconds"])

    def test_partial_trace_does_not_claim_zero_counts(self):
        row = metrics.summarize_trial([start()])
        for key in metrics.METRICS:
            self.assertIsNone(row[key])
        self.assertEqual(row["observedRepeatedFileReads"], 0)

    def test_validation_intervals_union_not_sum(self):
        row = metrics.summarize_trial([
            start(), event("validation_started", 1, validationId="a"),
            event("validation_started", 3, validationId="b"),
            event("validation_finished", 5, validationId="a", result="fail"),
            event("validation_finished", 8, validationId="b", result="pass"),
            event("validation_started", 10, validationId="c"),
            event("validation_finished", 12, validationId="c", result="pass"), finish()])
        self.assertEqual(row["validationWaitSeconds"], 9)
        self.assertEqual(row["validationCounts"], {"pass": 2, "fail": 1})
        self.assertEqual([item["result"] for item in row["validationOutcomes"]], ["fail", "pass", "pass"])
        self.assertTrue(row["comparisonEligible"])
        self.assertIsNone(row["passingPrSeconds"])

    def test_unmatched_validation_is_unavailable(self):
        row = metrics.summarize_trial([start(), event("validation_started", 1, validationId="a"), finish()])
        self.assertIsNone(row["validationWaitSeconds"])
        self.assertEqual(row["unmatchedValidations"], ["a"])

    def test_invalid_transition_is_rejected(self):
        for events in (
            [event("correction")],
            [start(), start()],
            [start(), finish(), event("correction", 21)],
            [start(), event("correction", 4), event("correction", 3)],
            [start(), event("validation_finished", 1, validationId="a", result="pass")],
            [start(), event("validation_started", 1, validationId="a"),
             event("validation_started", 2, validationId="a")],
        ):
            with self.subTest(events=events), self.assertRaises(ValueError):
                metrics.summarize_trial(events)

    def test_passing_pr_requires_checks_for_same_revision(self):
        passing = event("passing_pr", 25, url="https://github.com/o/r/pull/1", commitSha=SHA,
                        requiredChecks=[{"name": "quality-gate", "conclusion": "success", "commitSha": SHA,
                                         "evidenceUrl": "https://github.com/o/r/actions/runs/1"}])
        row = metrics.summarize_trial([start(), finish(), passing])
        self.assertEqual(row["passingPrSeconds"], 25)
        for bad in ({"requiredChecks": []}, {"commitSha": "bad"},
                    {"url": "http://github.com/o/r/pull/1"},
                    {"requiredChecks": [{"name": "quality-gate", "conclusion": "failure", "commitSha": SHA,
                                         "evidenceUrl": "https://github.com/o/r/actions/runs/1"}]},
                    {"requiredChecks": [{"name": "quality-gate", "conclusion": "success", "commitSha": "c" * 40,
                                         "evidenceUrl": "https://github.com/o/r/actions/runs/1"}]}):
            with self.subTest(bad=bad), self.assertRaises(ValueError):
                metrics.validate_event({**passing, **bad})

    def test_matched_comparisons_exclude_missing_evidence(self):
        events = [start(), event("useful_edit", 10, path="x", sha256=DIGEST),
                  event("validation_started", 11, validationId="v"),
                  event("validation_finished", 12, validationId="v", result="pass"), finish(),
                  start("two", "treatment"), event("useful_edit", 5, trial="two", path="x", sha256=DIGEST),
                  event("validation_started", 11, trial="two", validationId="v"),
                  event("validation_finished", 12, trial="two", validationId="v", result="pass"),
                  finish(trial="two")]
        data = metrics.report(events, [Path("raw.jsonl")])
        comparison = data["comparisons"]["firstUsefulEditSeconds"]
        self.assertEqual(comparison["matchedPairs"], 1)
        self.assertEqual(comparison["medianPairedDifference"], -5)
        self.assertEqual(data["comparisons"]["passingPrSeconds"]["matchedPairs"], 0)
        self.assertIsNone(data["comparisons"]["passingPrSeconds"]["baselineMedian"])

    def test_passing_pr_before_task_finish_and_stale_revision_rejected(self):
        passing = event("passing_pr", 25, url="https://github.com/o/r/pull/1", commitSha=SHA,
                        requiredChecks=[{"name": "quality-gate", "conclusion": "success", "commitSha": SHA,
                                         "evidenceUrl": "https://github.com/o/r/actions/runs/1"}])
        # A pre-existing passing PR cannot precede the new task's edit or completion.
        with self.assertRaisesRegex(ValueError, "must follow task_finished"):
            metrics.summarize_trial([start(), passing,
                                     event("useful_edit", 26, path="x", sha256=DIGEST), finish(27)])
        with self.assertRaisesRegex(ValueError, "completed task revision"):
            metrics.summarize_trial([start(), {**finish(), "commitSha": "c" * 40}, passing])
        row = metrics.summarize_trial([start(), {**finish(), "commitSha": SHA}, passing])
        self.assertEqual(row["deliveryCommitSha"], SHA)
        with self.assertRaisesRegex(ValueError, "full commit SHA"):
            metrics.validate_event({**finish(), "commitSha": "bad"})

    def test_failed_only_validation_excluded_from_every_comparison(self):
        events = []
        for trial, cohort, result in (("one", "baseline", "pass"), ("two", "treatment", "fail")):
            events.extend([start(trial, cohort), event("useful_edit", 3, trial=trial, path="x", sha256=DIGEST),
                           event("validation_started", 4, trial=trial, validationId="v"),
                           event("validation_finished", 8, trial=trial, validationId="v", result=result),
                           finish(trial=trial)])
        data = metrics.report(events, [])
        failed = data["trials"][1]
        self.assertEqual(failed["validationWaitSeconds"], 4)
        self.assertEqual(failed["validationCounts"], {"pass": 0, "fail": 1})
        self.assertFalse(failed["comparisonEligible"])
        for comparison in data["comparisons"].values():
            self.assertEqual(comparison["matchedPairs"], 0)
        # A later successful validation makes the trial eligible; failed wait remains included.
        events.pop()
        events.extend([event("validation_started", 9, trial="two", validationId="retry"),
                       event("validation_finished", 11, trial="two", validationId="retry", result="pass"),
                       finish(trial="two")])
        data = metrics.report(events, [])
        self.assertTrue(data["trials"][1]["comparisonEligible"])
        self.assertEqual(data["trials"][1]["validationWaitSeconds"], 6)
        self.assertEqual(data["comparisons"]["firstUsefulEditSeconds"]["matchedPairs"], 1)

    def test_incomplete_first_edit_is_observed_but_never_comparable(self):
        events = []
        for trial, cohort, complete in (("one", "baseline", True), ("two", "treatment", False)):
            events.extend([start(trial, cohort), event("useful_edit", 3, trial=trial, path="x", sha256=DIGEST),
                           event("validation_started", 4, trial=trial, validationId="v"),
                           event("validation_finished", 8, trial=trial, validationId="v", result="pass"),
                           finish(trial=trial, complete=complete)])
        data = metrics.report(events, [])
        incomplete = data["trials"][1]
        self.assertEqual(incomplete["observedFirstUsefulEditSeconds"], 3)
        self.assertIsNone(incomplete["firstUsefulEditSeconds"])
        self.assertFalse(incomplete["comparisonEligible"])
        for comparison in data["comparisons"].values():
            self.assertEqual(comparison["matchedPairs"], 0)

    def test_duplicate_cohort_pair_rejected(self):
        with self.assertRaisesRegex(ValueError, "at most one"):
            metrics.report([start(), start("two")], [])

    def test_cli_real_events_and_fail_closed_append(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "source.py").write_text("first\n")
            log = root / "events.jsonl"
            common = ["record", "--root", str(root), "--log", str(log), "--trial", "trial"]
            self.assertEqual(metrics.main([*common, "--event", "task_started", "--pair", "p", "--cohort", "baseline"]), 0)
            self.assertEqual(metrics.main([*common, "--event", "file_read", "--path", "source.py"]), 0)
            before = log.read_bytes()
            self.assertEqual(metrics.main([*common, "--event", "file_read", "--path", "../outside"]), 1)
            self.assertEqual(log.read_bytes(), before)
            self.assertEqual(metrics.main([*common, "--event", "task_finished", "--instrumentation-complete"]), 0)
            output = root / "report.json"
            self.assertEqual(metrics.main(["report", "--log", str(log), "--output", str(output)]), 0)
            row = json.loads(output.read_text())["trials"][0]
            self.assertEqual(row["repeatedFileReads"], 0)
            self.assertIsNone(row["firstUsefulEditSeconds"])
            self.assertIsNone(row["validationWaitSeconds"])
            before = log.read_bytes()
            self.assertEqual(metrics.main(["report", "--log", str(log), "--output", str(log)]), 1)
            self.assertEqual(log.read_bytes(), before)
            original_output = output.read_bytes()
            self.assertEqual(metrics.main(["report", "--log", str(log), "--output", str(output),
                                           "--markdown-output", str(log)]), 1)
            self.assertEqual(output.read_bytes(), original_output)
            self.assertEqual(metrics.main(["report", "--log", str(log), "--output", str(output),
                                           "--markdown-output", str(output)]), 1)
            self.assertEqual(output.read_bytes(), original_output)

    def test_read_log_errors_have_location(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.jsonl"
            path.write_text('{"event": "unknown"}\n')
            with self.assertRaisesRegex(ValueError, "bad.jsonl:1"):
                metrics.read_logs([path])

    def test_source_symlink_escape_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory) / "root"
            root.mkdir()
            outside = Path(directory) / "outside"
            outside.write_text("outside")
            (root / "link").symlink_to(outside)
            with self.assertRaises(ValueError):
                metrics.source(root, "link")

    def test_bad_timestamp_or_digest_rejected(self):
        for bad in (event("task_started", pair="p", cohort="baseline", timestamp="2026-10-28"),
                    event("file_read", path="a", sha256="bad")):
            with self.assertRaises(ValueError):
                metrics.validate_event(bad)


if __name__ == "__main__":
    unittest.main()
