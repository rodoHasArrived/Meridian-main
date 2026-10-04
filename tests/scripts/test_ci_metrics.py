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


if __name__ == "__main__":
    unittest.main()
