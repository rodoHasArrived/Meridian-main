from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "meridian-ci.yml"


class MeridianCiWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_quality_gate_is_stable_aggregator(self) -> None:
        self.assertIn("quality-gate:", self.workflow)
        self.assertIn("name: quality-gate", self.workflow)
        self.assertIn("needs:", self.workflow)
        self.assertIn("- verify-dotnet", self.workflow)
        self.assertIn("- verify-browser", self.workflow)
        self.assertIn("- verify-docs", self.workflow)
        self.assertIn("- verify-workflows", self.workflow)
        self.assertIn("Evaluate Meridian CI lanes", self.workflow)

    def test_parallel_lanes_use_scripts_ci_named_lanes(self) -> None:
        for lane in ("verify-dotnet", "verify-browser", "verify-docs", "verify-workflows"):
            with self.subTest(lane=lane):
                self.assertIn(f"bash scripts/ci.sh --lane {lane}", self.workflow)

    def test_lane_artifacts_are_uploaded(self) -> None:
        self.assertIn("artifacts/ci-summary/", self.workflow)
        self.assertIn("artifacts/build-logs/", self.workflow)
        self.assertIn("artifacts/test-results/dotnet/", self.workflow)
        self.assertIn("src/Meridian.Ui/dashboard/dist/", self.workflow)

    def test_browser_lane_runs_full_dashboard_contract_checks(self) -> None:
        self.assertIn("bash scripts/ci.sh --lane verify-browser", self.workflow)

        ci_script = (REPO_ROOT / "scripts" / "ci.sh").read_text(encoding="utf-8")
        self.assertIn("Generated UI contract drift gate", ci_script)
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run lint", ci_script)
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run typecheck:strict", ci_script)
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run test", ci_script)


if __name__ == "__main__":
    unittest.main()
