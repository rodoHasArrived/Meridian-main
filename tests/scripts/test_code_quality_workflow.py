from __future__ import annotations

import unittest
from pathlib import Path


WORKFLOW_PATH = Path(__file__).resolve().parents[2] / ".github" / "workflows" / "code-quality.yml"


class CodeQualityWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_cancelled_quality_run_does_not_report_issues(self) -> None:
        self.assertIn("QUALITY_CANCELLED=0", self.workflow)
        self.assertIn('${{ steps.format-check.outcome }}" = "cancelled"', self.workflow)
        self.assertIn('${{ steps.build.outcome }}" = "cancelled"', self.workflow)
        self.assertIn('echo "quality_cancelled=$QUALITY_CANCELLED"', self.workflow)
        self.assertIn('if [ "$QUALITY_CANCELLED" -eq 0 ] && { [ "$FORMAT_ISSUES" -eq 1 ] || [ "$BUILD_ISSUES" -eq 1 ]; }; then', self.workflow)

    def test_downstream_issue_steps_skip_cancelled_quality_runs(self) -> None:
        guarded_steps = [
            "Upload build log",
            "Extract top warnings for AI",
            "AI code quality suggestions",
            "Create Copilot PR request for quality issues",
            "Fail workflow when quality issues exist",
        ]

        for step_name in guarded_steps:
            with self.subTest(step=step_name):
                step_index = self.workflow.find(f"- name: {step_name}")
                self.assertNotEqual(step_index, -1)
                next_step_index = self.workflow.find("\n      - name:", step_index + 1)
                step_block = self.workflow[step_index: next_step_index if next_step_index != -1 else None]
                self.assertIn("steps.analyze.outputs.quality_cancelled != '1'", step_block)

    def test_summary_explains_cancelled_quality_verdict(self) -> None:
        self.assertIn("Canceled before a complete quality verdict; no code-quality issue opened", self.workflow)
        self.assertIn("Canceled before build analysis completed", self.workflow)


if __name__ == "__main__":
    unittest.main()
