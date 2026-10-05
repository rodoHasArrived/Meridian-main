from __future__ import annotations

import unittest
from pathlib import Path

import yaml


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "windows-desktop-build.yml"


class WindowsDesktopBuildWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_workflow_uses_isolated_wpf_validation_script(self) -> None:
        self.assertIn("Run isolated Windows validation", self.workflow)
        self.assertIn("python build/scripts/ci/run-windows-ci-tests.py", self.workflow)
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

    def test_only_successful_smoke_binaries_have_shorter_retention(self) -> None:
        workflow = yaml.load(self.workflow, Loader=yaml.BaseLoader)
        steps = workflow["jobs"]["desktop"]["steps"]
        uploads = {
            step["name"]: step for step in steps
            if step.get("uses", "").startswith("actions/upload-artifact@")
        }
        validation = uploads["Upload WPF validation artifacts"]
        smoke = uploads["Upload desktop smoke publish artifacts"]
        self.assertEqual(validation["if"], "always()")
        self.assertEqual(validation["with"]["retention-days"], "14")
        self.assertEqual(smoke["if"], "always()")
        self.assertEqual(smoke["with"]["path"], "artifacts/publish/desktop-smoke/")
        self.assertEqual(smoke["with"]["retention-days"], "${{ job.status == 'success' && 7 || 14 }}")
        # Retention must not replace publishing or the executable existence gate.
        for name in ("Restore WPF publish graph", "Smoke publish WPF app", "Confirm desktop executable"):
            step = next(step for step in steps if step.get("name") == name)
            self.assertEqual(step["if"], "steps.desktop-smoke.outputs.run == 'true'")
            self.assertNotIn("continue-on-error", step)
            self.assertLess(steps.index(step), steps.index(smoke))

    def test_path_filters_include_workflow_and_validation_scripts(self) -> None:
        self.assertIn('"scripts/dev/validate-wpf-dev.ps1"', self.workflow)
        self.assertIn('"scripts/dev/SharedBuild.ps1"', self.workflow)
        self.assertIn('".github/workflows/windows-desktop-build.yml"', self.workflow)


if __name__ == "__main__":
    unittest.main()
