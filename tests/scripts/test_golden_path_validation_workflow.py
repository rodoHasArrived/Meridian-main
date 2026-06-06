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
        self.assertIn("artifacts/pilot-acceptance/latest/pilot-readiness.md", self.workflow)
        self.assertIn("artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md", self.workflow)
        self.assertIn("name: pilot-acceptance-evidence", self.workflow)

    def test_workflow_validates_pilot_readiness_dashboard_renderer(self) -> None:
        self.assertIn("Validate pilot readiness dashboard renderer", self.workflow)
        self.assertIn(
            "python3 -m unittest build/scripts/docs/tests/test_pilot_readiness_dashboard.py",
            self.workflow,
        )

    def test_workflow_blocks_pilot_acceptance_on_w4_parity_filters(self) -> None:
        for expected in [
            "browser-w4-parity:",
            "wpf-w4-acceptance:",
            "npm --prefix src/Meridian.Ui/dashboard run test:w4",
            "--filter \"Category=W4Acceptance\"",
            "needs:",
            "- browser-w4-parity",
            "- wpf-w4-acceptance",
        ]:
            self.assertIn(expected, self.workflow)

    def test_workflow_triggers_on_w4_browser_and_desktop_surfaces(self) -> None:
        for watched_path in [
            "src/Meridian.Ui/dashboard/package.json",
            "src/Meridian.Ui/dashboard/src/screens/**",
            "src/Meridian.Wpf/**",
            "tests/Meridian.Wpf.Tests/**",
        ]:
            self.assertIn(watched_path, self.workflow)

    def test_workflow_triggers_on_shared_golden_path_surfaces(self) -> None:
        for watched_path in [
            "src/Meridian.Application/Commands/LedgerCliCommand.cs",
            "src/Meridian.FinancialOperations/Ledger/TextJournal/**",
            "src/Meridian.Contracts/Workstation/**",
            "src/Meridian.Execution/**",
            "src/Meridian.Ledger/**",
            "src/Meridian.Strategies/**",
            "src/Meridian.Ui.Shared/**",
            "tests/Meridian.Tests/Application/Commands/LedgerCliCommandTests.cs",
        ]:
            self.assertIn(watched_path, self.workflow)


if __name__ == "__main__":
    unittest.main()
