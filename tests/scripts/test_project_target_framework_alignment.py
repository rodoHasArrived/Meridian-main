import json
import os
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOW_ROOT = REPO_ROOT / ".github" / "workflows"
CI_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "ci.yml"
MAINTENANCE_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "maintenance.yml"
CODEQL_WORKFLOW = REPO_ROOT / ".github" / "workflows" / "codeql.yml"
PRODUCTION_DOCKERFILE = REPO_ROOT / "deploy" / "docker" / "Dockerfile"
SKIPPED_PROJECT_DIRS = {".git", ".vs", "artifacts", "bin", "dist", "node_modules", "obj", "publish", "TestResults"}


def target_framework(project_path: str) -> str:
    root = ET.parse(REPO_ROOT / project_path).getroot()
    target = root.find("./PropertyGroup/TargetFramework")
    if target is None or not target.text:
        raise AssertionError(f"{project_path} does not declare TargetFramework")
    return target.text


def projects_referencing(project_path: str) -> list[str]:
    referenced_project = (REPO_ROOT / project_path).resolve()
    matching_projects: list[str] = []

    for root, dirnames, filenames in os.walk(REPO_ROOT):
        dirnames[:] = [name for name in dirnames if name not in SKIPPED_PROJECT_DIRS]

        for filename in filenames:
            if not filename.endswith("proj"):
                continue

            candidate = Path(root) / filename

            project_root = ET.parse(candidate).getroot()
            for reference in project_root.findall(".//ProjectReference"):
                include = reference.attrib.get("Include")
                if include and (candidate.parent / include.replace("\\", "/")).resolve() == referenced_project:
                    matching_projects.append(candidate.relative_to(REPO_ROOT).as_posix())
                    break

    return sorted(matching_projects)


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

    def test_ci_verify_full_restores_with_current_sdk(self) -> None:
        workflow = CI_WORKFLOW.read_text(encoding="utf-8")

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

    def test_maintenance_workflow_validates_current_workflow_surface(self) -> None:
        workflow = MAINTENANCE_WORKFLOW.read_text(encoding="utf-8")

        self.assertIn("python3 build/scripts/ci/check-workflow-hygiene.py", workflow)
        self.assertIn("rhysd/actionlint@v1.7.12", workflow)
        self.assertIn("check-ai-contract-drift.py", workflow)

    def test_codeql_csharp_analysis_builds_with_current_sdk(self) -> None:
        workflow = CODEQL_WORKFLOW.read_text(encoding="utf-8")

        self.assertIn("- language: csharp", workflow)
        self.assertIn("build-mode: manual", workflow)
        self.assertIn("DOTNET_VERSION: '10.0.x'", workflow)
        self.assertIn("dotnet-version: ${{ env.DOTNET_VERSION }}", workflow)
        self.assertNotIn("dotnet-version: '9.0.x'", workflow)
        self.assertIn("Restore C# solution", workflow)
        self.assertIn("Build C# solution", workflow)

    def test_production_dockerfile_uses_current_sdk_and_runtime(self) -> None:
        dockerfile = PRODUCTION_DOCKERFILE.read_text(encoding="utf-8")

        self.assertIn("mcr.microsoft.com/dotnet/sdk:10.0-alpine", dockerfile)
        self.assertIn("mcr.microsoft.com/dotnet/aspnet:10.0-alpine", dockerfile)
        self.assertNotIn("mcr.microsoft.com/dotnet/sdk:9", dockerfile)
        self.assertNotIn("mcr.microsoft.com/dotnet/aspnet:9", dockerfile)


if __name__ == "__main__":
    unittest.main()
