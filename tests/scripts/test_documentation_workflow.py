import unittest
from pathlib import Path

import yaml

from tests.scripts.workflow_assertions import assert_pinned_action


REPO_ROOT = Path(__file__).resolve().parents[2]
DOCUMENTATION_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "documentation.yml"


class DocumentationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = DOCUMENTATION_WORKFLOW.read_text(encoding="utf-8")

    def test_dashboard_diff_gate_skips_severe_failure_when_baseline_is_unavailable(self) -> None:
        self.assertIn("baseline_available = bool(previous.strip())", self.workflow)
        self.assertIn("'baseline_available': baseline_available", self.workflow)
        self.assertIn("'missing_previous_files': missing_previous", self.workflow)
        self.assertIn(
            "Baseline dashboard source was unavailable; severe regression gating was skipped",
            self.workflow,
        )
        self.assertIn("severe = baseline_available and (", self.workflow)

    def test_regenerate_docs_job_fetches_history_for_dashboard_diff(self) -> None:
        checkout = assert_pinned_action(self, self.workflow, "regenerate-docs", "actions/checkout")
        self.assertEqual(checkout.get("with", {}).get("persist-credentials"), "false")
        self.assertEqual(checkout.get("with", {}).get("fetch-depth"), "0")
        steps = yaml.load(self.workflow, Loader=yaml.BaseLoader)["jobs"]["regenerate-docs"]["steps"]
        comparisons = [
            step for step in steps
            if step.get("name") == "Compare dashboard readiness deltas vs previous commit"
        ]
        self.assertEqual(len(comparisons), 1, "Expected exactly one history comparison step")
        self.assertLess(steps.index(checkout), steps.index(comparisons[0]))

    def test_diagram_dependencies_use_root_lockfile(self) -> None:
        self.assertIn('"package-lock.json"', self.workflow)
        self.assertIn("npm ci --no-fund --no-audit", self.workflow)
        self.assertNotIn("npm install --no-fund --no-audit", self.workflow)

    def test_whole_repository_inputs_and_merge_groups_receive_drift_validation(self) -> None:
        events = yaml.load(self.workflow, Loader=yaml.BaseLoader)["on"]
        self.assertIn("merge_group", events)
        for event in ("pull_request", "push"):
            self.assertNotIn("paths", events[event])
            self.assertNotIn("paths-ignore", events[event])

    def test_drift_check_follows_convergence_and_exports_failure_evidence(self) -> None:
        steps = yaml.load(self.workflow, Loader=yaml.BaseLoader)["jobs"]["regenerate-docs"]["steps"]
        converge = next(step for step in steps if step.get("name") == "Converge maintained documentation")
        gate = next(step for step in steps if step.get("name") == "Check generated docs are current")
        upload = next(step for step in steps if step.get("name") == "Upload documentation repair evidence")
        self.assertLess(steps.index(converge), steps.index(gate))
        self.assertIn("--docs-only", converge["run"])
        self.assertIn("check-generated-output.py", gate["run"])
        self.assertEqual("always()", upload["if"])
        self.assertIn("artifacts/docs/", upload["with"]["path"])


if __name__ == "__main__":
    unittest.main()
