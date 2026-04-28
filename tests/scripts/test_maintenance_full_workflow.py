import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
MAINTENANCE_SCRIPT = REPO_ROOT / "scripts" / "ai" / "maintenance-full.sh"
FSHARP_TEST_PROJECT = REPO_ROOT / "tests" / "Meridian.FSharp.Tests" / "Meridian.FSharp.Tests.fsproj"
CENTRAL_PACKAGES = REPO_ROOT / "Directory.Packages.props"


class MaintenanceFullWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.script = MAINTENANCE_SCRIPT.read_text(encoding="utf-8")
        cls.fsharp_project = FSHARP_TEST_PROJECT.read_text(encoding="utf-8")
        cls.central_packages = CENTRAL_PACKAGES.read_text(encoding="utf-8")

    def test_full_maintenance_does_not_apply_category_filter_to_entire_solution(self) -> None:
        self.assertNotIn("dotnet test Meridian.sln", self.script)
        self.assertNotIn('Meridian.sln -c Release --no-build --nologo --filter "Category!=Integration"', self.script)

    def test_fsharp_maintenance_lane_runs_project_without_category_filter(self) -> None:
        line = next(
            line for line in self.script.splitlines()
            if 'run_step "dotnet-test-fsharp"' in line
        )

        self.assertIn("tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj", line)
        self.assertNotIn("--filter", line)

    def test_wpf_tests_are_left_to_desktop_validation_lane(self) -> None:
        self.assertIn('record_step "dotnet-test-wpf" "skipped"', self.script)
        self.assertIn("desktop validation lane", self.script)

    def test_fsharp_test_project_declares_coverage_collector(self) -> None:
        self.assertIn('<PackageReference Include="coverlet.collector">', self.fsharp_project)
        self.assertIn("<PrivateAssets>all</PrivateAssets>", self.fsharp_project)
        self.assertIn("<IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>", self.fsharp_project)

    def test_fsharp_xunit_v3_runtime_matches_visual_studio_adapter_line(self) -> None:
        self.assertIn('<PackageVersion Include="xunit.v3" Version="3.2.2" />', self.central_packages)
        self.assertIn('<PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />', self.central_packages)
        self.assertIn("<GenerateProgramFile>false</GenerateProgramFile>", self.fsharp_project)


if __name__ == "__main__":
    unittest.main()
