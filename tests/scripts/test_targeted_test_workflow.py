from __future__ import annotations

import json
import re
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_PATH = REPO_ROOT / ".github" / "workflows" / "targeted-test.yml"
WORKFLOW_README_PATH = REPO_ROOT / ".github" / "workflows" / "README.md"
START_README_PATH = REPO_ROOT / "docs" / "start" / "README.md"
ENGINEERING_README_PATH = REPO_ROOT / "docs" / "engineering" / "README.md"
WORKFLOW_MANIFEST_PATH = REPO_ROOT / "docs" / "status" / "workflow-manifest.json"


class TargetedTestWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")

    def test_workflow_is_manual_dispatch_only(self) -> None:
        self.assertIn("workflow_dispatch:", self.workflow)
        self.assertNotRegex(self.workflow, r"(?m)^\s+(push|pull_request|schedule):")

    def test_dotnet_slice_inputs_accept_project_path_and_filter(self) -> None:
        for input_name in (
            "mode:",
            "dotnet_project:",
            "dotnet_filter:",
            "runner:",
            "configuration:",
            "enable_windows_targeting:",
            "enable_full_wpf_build:",
            "wpf_route:",
        ):
            with self.subTest(input=input_name):
                self.assertIn(input_name, self.workflow)

    def test_curated_hosted_modes_are_declared(self) -> None:
        for mode in (
            "dotnet-filtered",
            "browser-workstation",
            "docs-source",
            "wpf-dev-loop",
            "wpf-route",
            "desktop-smoke",
        ):
            with self.subTest(mode=mode):
                self.assertIn(f"- {mode}", self.workflow)

    def test_dotnet_step_validates_target_path_before_running_tests(self) -> None:
        self.assertIn("DOTNET_PROJECT: ${{ inputs.dotnet_project }}", self.workflow)
        self.assertIn("DOTNET_FILTER: ${{ inputs.dotnet_filter }}", self.workflow)
        self.assertIn("Unsupported dotnet_project", self.workflow)
        self.assertIn("Test-Path -LiteralPath $project", self.workflow)
        self.assertIn("'^tests/[A-Za-z0-9._/-]+\\.(csproj|fsproj)$'", self.workflow)
        self.assertIn("dotnet_filter is required so Targeted Test runs the exact failing slice", self.workflow)
        self.assertIn("if: inputs.mode == 'dotnet-filtered'", self.workflow)
        self.assertIn("$normalizedFilter = $filter -replace '\\s+', ''", self.workflow)
        self.assertIn("Category!=Integration&Category!=Performance", self.workflow)
        self.assertIn("dotnet_filter '$filter' is too broad for Targeted Test", self.workflow)
        self.assertIn("$normalizedFilter -notmatch '(?<![!<>])(?:=|~)'", self.workflow)
        self.assertIn("must include a positive class, method, trait, or fully qualified name selector", self.workflow)

    def test_non_dotnet_modes_are_whitelisted_commands(self) -> None:
        self.assertIn("npm --prefix src/Meridian.Ui/dashboard run test", self.workflow)
        self.assertIn("python3 build/scripts/docs/validate-roadmap-registry.py --summary", self.workflow)
        self.assertIn("scripts/dev/validate-wpf-dev.ps1", self.workflow)
        self.assertIn("scripts/dev/validate-position-blotter-route.ps1", self.workflow)
        self.assertIn("scripts/dev/validate-operator-inbox-route.ps1", self.workflow)
        self.assertIn("artifacts/publish/targeted-desktop-smoke", self.workflow)
        self.assertNotIn("${{ inputs.command }}", self.workflow)

    def test_dotnet_step_runs_exact_selected_project_with_required_filter(self) -> None:
        self.assertIn("& dotnet restore $project @props", self.workflow)
        self.assertIn("'test',", self.workflow)
        self.assertIn("$project,", self.workflow)
        self.assertIn("'--filter',", self.workflow)
        self.assertIn("$filter,", self.workflow)
        self.assertNotIn("if ($filter -ne '')", self.workflow)
        self.assertIn("& dotnet @testArgs @props", self.workflow)
        self.assertIn("if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }", self.workflow)

    def test_docs_show_remote_dispatch_examples_for_project_and_filter(self) -> None:
        for path in (WORKFLOW_README_PATH, START_README_PATH, ENGINEERING_README_PATH):
            with self.subTest(path=path):
                text = path.read_text(encoding="utf-8")
                self.assertIn("gh workflow run targeted-test.yml", text)
                self.assertIn("mode=dotnet-filtered", text)
                self.assertIn("dotnet_project=", text)
                self.assertIn("dotnet_filter=", text)

    def test_workflow_readme_documents_targeted_lane_mapping(self) -> None:
        readme = WORKFLOW_README_PATH.read_text(encoding="utf-8")
        self.assertRegex(
            readme,
            re.compile(r"\|\s*`targeted-test`\s*\|\s*`Targeted Test`", re.MULTILINE),
        )

    def test_workflow_manifest_declares_targeted_test_command(self) -> None:
        manifest = json.loads(WORKFLOW_MANIFEST_PATH.read_text(encoding="utf-8"))
        targeted = next(workflow for workflow in manifest["workflows"] if workflow["id"] == "targeted-test")

        self.assertIn("tests/", " ".join(targeted["requiredPrerequisites"]))
        self.assertIn("filter", " ".join(targeted["requiredPrerequisites"]).lower())
        self.assertIn("mode", " ".join(targeted["requiredPrerequisites"]).lower())
        self.assertIn("artifacts/test-results/targeted-dotnet", targeted["expectedArtifacts"])
        commands = [entry["command"] for entry in targeted["executionCommands"]]
        self.assertTrue(any("dispatch-targeted-test.py" in command for command in commands))
        self.assertTrue(any("gh workflow run targeted-test.yml" in command for command in commands))


if __name__ == "__main__":
    unittest.main()
