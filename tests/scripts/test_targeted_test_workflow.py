from __future__ import annotations

import re
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "targeted-test.yml"
WORKFLOW_README_PATH = REPO_ROOT / ".github" / "workflows" / "README.md"
START_README_PATH = REPO_ROOT / "docs" / "start" / "README.md"
ENGINEERING_README_PATH = REPO_ROOT / "docs" / "engineering" / "README.md"


class TargetedTestWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_workflow_is_manual_dispatch_only(self) -> None:
        self.assertIn("workflow_dispatch:", self.workflow)
        self.assertNotRegex(self.workflow, r"(?m)^\s+(push|pull_request|schedule):")

    def test_dotnet_slice_inputs_accept_project_path_and_filter(self) -> None:
        for input_name in (
            "dotnet_project:",
            "dotnet_filter:",
            "runner:",
            "configuration:",
            "enable_windows_targeting:",
            "enable_full_wpf_build:",
        ):
            with self.subTest(input=input_name):
                self.assertIn(input_name, self.workflow)

    def test_dotnet_step_validates_target_path_before_running_tests(self) -> None:
        self.assertIn("DOTNET_PROJECT: ${{ inputs.dotnet_project }}", self.workflow)
        self.assertIn("DOTNET_FILTER: ${{ inputs.dotnet_filter }}", self.workflow)
        self.assertIn("Unsupported dotnet_project", self.workflow)
        self.assertIn("Test-Path -LiteralPath $project", self.workflow)
        self.assertIn("'^(tests|src)/[A-Za-z0-9._/-]+\\.(csproj|fsproj|sln|slnf)$'", self.workflow)
        self.assertIn("dotnet_filter is required for lane=dotnet", self.workflow)

    def test_dotnet_step_runs_exact_selected_project_with_optional_filter(self) -> None:
        self.assertIn("& dotnet restore $project @props", self.workflow)
        self.assertIn("'test',", self.workflow)
        self.assertIn("$project,", self.workflow)
        self.assertIn("$testArgs += @('--filter', $filter)", self.workflow)
        self.assertIn("& dotnet @testArgs @props", self.workflow)

    def test_browser_slice_remains_constrained_to_known_dashboard_targets(self) -> None:
        self.assertIn("browser-dashboard", self.workflow)
        self.assertIn("Unsupported browser_script", self.workflow)
        self.assertIn("vitest_file and vitest_name are only supported with browser_script=test:vitest.", self.workflow)
        self.assertIn("$vitestFile.Contains('..')", self.workflow)

    def test_docs_show_remote_dispatch_examples_for_project_and_filter(self) -> None:
        for path in (WORKFLOW_README_PATH, START_README_PATH, ENGINEERING_README_PATH):
            with self.subTest(path=path):
                text = path.read_text(encoding="utf-8")
                self.assertIn("gh workflow run targeted-test.yml", text)
                self.assertIn("dotnet_project=", text)
                self.assertIn("dotnet_filter=", text)

    def test_workflow_readme_documents_targeted_lane_mapping(self) -> None:
        readme = WORKFLOW_README_PATH.read_text(encoding="utf-8")
        self.assertRegex(
            readme,
            re.compile(r"\|\s*`targeted-test`\s*\|\s*`Targeted Test`", re.MULTILINE),
        )


if __name__ == "__main__":
    unittest.main()
