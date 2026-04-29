import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "golden-path-validation.yml"


class GoldenPathValidationWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_workflow_runs_pilot_acceptance_harness(self) -> None:
        self.assertIn("Run pilot acceptance harness", self.workflow)
        self.assertIn("FullyQualifiedName~PilotAcceptanceHarnessTests", self.workflow)
        self.assertIn("tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs", self.workflow)

    def test_workflow_uses_current_dotnet_sdk(self) -> None:
        self.assertIn("DOTNET_VERSION: '10.0.x'", self.workflow)
        self.assertNotIn("DOTNET_VERSION: '9.0.x'", self.workflow)

    def test_workflow_publishes_pilot_readiness_artifact(self) -> None:
        self.assertIn("generate-pilot-readiness-dashboard.py", self.workflow)
        self.assertIn("artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md", self.workflow)
        self.assertIn("name: pilot-acceptance-evidence", self.workflow)

    def test_workflow_triggers_on_shared_golden_path_surfaces(self) -> None:
        for watched_path in [
            "src/Meridian.Contracts/Workstation/**",
            "src/Meridian.Execution/**",
            "src/Meridian.Ledger/**",
            "src/Meridian.Strategies/**",
            "src/Meridian.Ui.Shared/**",
        ]:
            self.assertIn(watched_path, self.workflow)


if __name__ == "__main__":
    unittest.main()
