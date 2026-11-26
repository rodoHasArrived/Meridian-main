"""Generated Repair runs only on request: a label, a stale-output CI signal or a dispatch.

ci-health-013: every pull request update used to start a Generated Repair run and, through
the publisher, a second trusted generation, saturating the hosted runner limit. These tests
keep the workflow conditions, the verify-docs signal and the trusted script's rules in step.
"""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import tempfile
import unittest

import yaml

from tests.scripts.test_generated_repair import repair

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github/workflows"


def load(name: str) -> dict:
    return yaml.load((WORKFLOWS / name).read_text(encoding="utf-8"), Loader=yaml.BaseLoader)


class GeneratedRepairTriggerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.repair = load("generated-repair.yml")
        cls.publisher = load("generated-repair-publish.yml")
        cls.ci = load("meridian-ci.yml")

    def test_pull_request_updates_need_the_label_and_a_ready_pull_request(self):
        target = self.repair["on"]["pull_request_target"]
        self.assertEqual(target["types"], ["opened", "synchronize", "reopened", "ready_for_review", "labeled"])
        condition = self.repair["jobs"]["generate"]["if"]
        self.assertIn(f"contains(github.event.pull_request.labels.*.name, '{repair.REPAIR_LABEL}')", condition)
        self.assertIn(f"github.event.label.name == '{repair.REPAIR_LABEL}'", condition)
        self.assertIn("github.event.pull_request.draft != true", condition)
        self.assertIn("github.event_name == 'workflow_dispatch'", condition)

    def test_ci_signal_runs_only_for_failed_same_repository_pull_request_ci(self):
        signal = self.repair["on"]["workflow_run"]
        self.assertEqual(signal["workflows"], [self.ci["name"]])
        self.assertEqual(signal["types"], ["completed"])
        self.assertEqual(signal["branches-ignore"], ["main", "gh-readonly-queue/**"])
        condition = self.repair["jobs"]["generate"]["if"]
        for clause in ("github.event.workflow_run.event == 'pull_request'",
                       "github.event.workflow_run.conclusion == 'failure'",
                       "github.event.workflow_run.head_repository.full_name == github.repository"):
            self.assertIn(clause, condition)
        self.assertEqual(Path(repair.CI_WORKFLOW).stem, "meridian-ci")

    def test_skipped_runs_never_cancel_a_repair_or_wake_the_publisher(self):
        self.assertNotIn("concurrency", self.repair, "Workflow-level concurrency would let skipped runs cancel repairs.")
        group = self.repair["jobs"]["generate"]["concurrency"]["group"]
        self.assertIn("ci-signal-", group)
        self.assertIn("conclusion != 'skipped'", self.publisher["jobs"]["publish"]["if"])

    def test_verify_docs_flag_step_matches_the_trusted_lookup_and_the_lane_step(self):
        steps = self.ci["jobs"][repair.CI_DOCS_JOB]["steps"]
        names = [step["name"] for step in steps]
        flag = steps[names.index(repair.STALE_STEP)]
        self.assertEqual(flag["if"], "failure()")
        self.assertEqual(names.index(repair.STALE_STEP), names.index("Run docs/source/AI lane") + 1)
        lane = (ROOT / "scripts/ci.sh").read_text(encoding="utf-8")
        self.assertIn(f'run_step "{flag["env"]["FRESHNESS_STEP"]}"', lane)
        self.assertEqual(flag["env"]["STEPS_TSV"], "artifacts/ci-summary/verify-docs/steps.tsv")

        def run_flag(rows):
            with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as temporary:
                tsv = Path(temporary) / "steps.tsv"
                if rows is not None:
                    tsv.write_text("".join(f"{name}\t{status}\t1\n" for name, status in rows))
                environment = dict(os.environ, STEPS_TSV=str(tsv), FRESHNESS_STEP=flag["env"]["FRESHNESS_STEP"],
                                   GITHUB_STEP_SUMMARY=str(Path(temporary) / "summary.md"))
                return subprocess.run(["bash", "-c", flag["run"]], env=environment,
                                      capture_output=True, text=True, check=False).returncode

        freshness = flag["env"]["FRESHNESS_STEP"]
        self.assertEqual(run_flag([("Generate maintained documentation", 0), (freshness, 1)]), 1)
        for rows in (None, [], [(freshness, 0), ("Enforce mode escalation policy", 1)],
                     [("Generate maintained documentation", 1)]):
            with self.subTest(rows=rows):
                self.assertEqual(run_flag(rows), 0)


if __name__ == "__main__":
    unittest.main()
