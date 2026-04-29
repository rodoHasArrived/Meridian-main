import json
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_ROOT = REPO_ROOT / ".github" / "workflows"
SCHEDULED_MAINTENANCE_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "scheduled-maintenance.yml"
SETUP_DOTNET_CACHE_ACTION = REPO_ROOT / ".github" / "actions" / "setup-dotnet-cache" / "action.yml"
CODEQL_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "codeql.yml"


def target_framework(project_path: str) -> str:
    root = ET.parse(REPO_ROOT / project_path).getroot()
    target = root.find("./PropertyGroup/TargetFramework")
    if target is None or not target.text:
        raise AssertionError(f"{project_path} does not declare TargetFramework")
    return target.text


def projects_referencing(project_path: str) -> list[str]:
    referenced_project = (REPO_ROOT / project_path).resolve()
    matching_projects: list[str] = []

    for candidate in sorted(REPO_ROOT.glob("**/*.*proj")):
        if any(part in {"bin", "obj", "node_modules", ".git"} for part in candidate.parts):
            continue

        root = ET.parse(candidate).getroot()
        for reference in root.findall(".//ProjectReference"):
            include = reference.attrib.get("Include")
            if include and (candidate.parent / include).resolve() == referenced_project:
                matching_projects.append(candidate.relative_to(REPO_ROOT).as_posix())
                break

    return matching_projects


class ProjectTargetFrameworkAlignmentTests(unittest.TestCase):
    def test_global_sdk_matches_host_target_framework(self) -> None:
        global_json = json.loads((REPO_ROOT / "global.json").read_text(encoding="utf-8"))

        self.assertEqual(global_json["sdk"]["version"], "10.0.100")
        self.assertTrue(global_json["sdk"]["allowPrerelease"])
        self.assertEqual(target_framework("src/Meridian/Meridian.csproj"), "net10.0")

    def test_projects_referencing_host_match_host_target_framework(self) -> None:
        host_target = target_framework("src/Meridian/Meridian.csproj")
        host_references = projects_referencing("src/Meridian/Meridian.csproj")

        self.assertIn("tests/Meridian.Tests/Meridian.Tests.csproj", host_references)
        self.assertIn("benchmarks/Meridian.Benchmarks/Meridian.Benchmarks.csproj", host_references)

        for project_path in host_references:
            with self.subTest(project_path=project_path):
                self.assertEqual(target_framework(project_path), host_target)

    def test_scheduled_maintenance_restores_with_current_sdk(self) -> None:
        workflow = SCHEDULED_MAINTENANCE_WORKFLOW.read_text(encoding="utf-8")

        self.assertIn("DOTNET_VERSION: '10.0.x'", workflow)
        self.assertNotIn("DOTNET_VERSION: '9.0.x'", workflow)
        self.assertIn("dotnet restore Meridian.sln /p:EnableWindowsTargeting=true", workflow)

    def test_github_workflows_do_not_pin_previous_sdk_family(self) -> None:
        workflow_paths = sorted([*WORKFLOW_ROOT.glob("*.yml"), *WORKFLOW_ROOT.glob("*.yaml")])
        offenders = [
            path.relative_to(REPO_ROOT).as_posix()
            for path in workflow_paths
            if "9.0.x" in path.read_text(encoding="utf-8")
        ]

        self.assertEqual([], offenders)

    def test_setup_dotnet_cache_action_defaults_to_current_sdk(self) -> None:
        action = SETUP_DOTNET_CACHE_ACTION.read_text(encoding="utf-8")

        self.assertIn("default: '10.0.x'", action)
        self.assertNotIn("default: '9.0.x'", action)

    def test_codeql_csharp_analysis_does_not_restore_with_previous_sdk(self) -> None:
        workflow = CODEQL_WORKFLOW.read_text(encoding="utf-8")

        self.assertIn("- language: csharp\n          build-mode: none", workflow)
        self.assertNotIn("dotnet-version: '9.0.x'", workflow)
        self.assertNotIn("Restore C# solution", workflow)
        self.assertNotIn("Build C# solution", workflow)


if __name__ == "__main__":
    unittest.main()
