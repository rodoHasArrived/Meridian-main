import unittest
from pathlib import Path


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
        checkout_index = self.workflow.index("regenerate-docs:")
        diff_index = self.workflow.index("Compare dashboard readiness deltas vs previous commit")
        regenerate_block = self.workflow[checkout_index:diff_index]

        self.assertIn("uses: actions/checkout@v5", regenerate_block)
        self.assertIn("fetch-depth: 0", regenerate_block)


if __name__ == "__main__":
    unittest.main()
