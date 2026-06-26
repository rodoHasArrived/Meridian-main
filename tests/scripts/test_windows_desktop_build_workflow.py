from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "windows-desktop-build.yml"


class WindowsDesktopBuildWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_workflow_uses_isolated_wpf_validation_script(self) -> None:
        self.assertIn("Run isolated WPF validation", self.workflow)
        self.assertIn("scripts/dev/validate-wpf-dev.ps1", self.workflow)
        self.assertIn("-Restore", self.workflow)
        self.assertIn("artifacts/wpf-validation/windows-desktop-build", self.workflow)
        self.assertNotIn("dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj", self.workflow)

    def test_smoke_publish_is_conditionally_gated(self) -> None:
        self.assertIn("Decide desktop smoke publish", self.workflow)
        self.assertIn("steps.desktop-smoke.outputs.run == 'true'", self.workflow)
        self.assertIn("run_smoke_publish:", self.workflow)
        self.assertIn("fetch-depth: 2", self.workflow)
        self.assertIn('"HEAD^1" "HEAD^2"', self.workflow)
        self.assertIn("git fetch origin $env:BASE_REF --depth=100", self.workflow)
        self.assertIn("git diff --name-only", self.workflow)
        self.assertIn("^build/scripts/(publish|install)/", self.workflow)

    def test_validation_artifact_is_uploaded_on_every_run(self) -> None:
        self.assertIn("Upload WPF validation artifacts", self.workflow)
        self.assertIn("if: always()", self.workflow)
        self.assertIn("windows-desktop-validation-${{ github.run_number }}", self.workflow)

    def test_path_filters_include_workflow_and_validation_scripts(self) -> None:
        self.assertIn('"scripts/dev/validate-wpf-dev.ps1"', self.workflow)
        self.assertIn('"scripts/dev/SharedBuild.ps1"', self.workflow)
        self.assertIn('".github/workflows/windows-desktop-build.yml"', self.workflow)


if __name__ == "__main__":
    unittest.main()
