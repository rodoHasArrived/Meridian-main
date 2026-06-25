import importlib.util
import json
import sys
import unittest
from pathlib import Path

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "run-dotnet-ci-tests.py"
SPEC = importlib.util.spec_from_file_location("run_dotnet_ci_tests", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class RunDotnetCiTestsTests(unittest.TestCase):
    def test_default_projects_are_used_when_no_overrides_are_supplied(self):
        projects = MODULE.parse_project_entries([])

        self.assertEqual(
            [project.name for project in projects],
            [
                "core-application",
                "core-ui-workstation-endpoints",
                "core-ui-other",
                "core-infrastructure",
                "core-storage",
                "core-data",
                "core-execution-strategy",
                "core-market-instruments",
                "core-platform-domain-root",
                "fsharp",
                "ui",
                "backtesting",
                "directlending",
                "fundstructure",
                "quantscript",
            ],
        )
        self.assertTrue(projects[0].filter_expression)
        self.assertIsNone(projects[-1].filter_expression)

    def test_build_dotnet_test_command_uses_ci_filter_and_trx_prefix(self):
        project = MODULE.TestProject("core", "tests/Meridian.Tests/Meridian.Tests.csproj")
        results_dir = Path("artifacts/test-results/dotnet")

        command = MODULE.build_dotnet_test_command(
            project,
            configuration="Release",
            test_filter="Category!=Integration&Category!=Performance",
            results_dir=results_dir,
        )

        self.assertEqual(command[:4], ["dotnet", "test", "tests/Meridian.Tests/Meridian.Tests.csproj", "-c"])
        self.assertIn("--no-restore", command)
        self.assertIn("--no-build", command)
        self.assertIn("Category!=Integration&Category!=Performance", command)
        self.assertIn("trx;LogFilePrefix=core", command)
        self.assertIn("/p:EnableWindowsTargeting=true", command)

    def test_build_dotnet_build_command_uses_no_restore_and_windows_targeting(self):
        project = MODULE.TestProject("core", "tests/Meridian.Tests/Meridian.Tests.csproj")

        command = MODULE.build_dotnet_build_command(project, configuration="Release")

        self.assertEqual(command[:4], ["dotnet", "build", "tests/Meridian.Tests/Meridian.Tests.csproj", "-c"])
        self.assertIn("--no-restore", command)
        self.assertIn("/p:EnableWindowsTargeting=true", command)

    def test_unique_build_projects_deduplicates_sharded_project_paths(self):
        projects = MODULE.parse_project_entries([])

        unique_projects = MODULE.get_unique_build_projects(projects)

        self.assertEqual(
            [project.path for project in unique_projects].count("tests/Meridian.Tests/Meridian.Tests.csproj"),
            1,
        )
        self.assertLess(len(unique_projects), len(projects))

    def test_build_dotnet_test_command_combines_project_filter(self):
        project = MODULE.TestProject(
            "core-application",
            "tests/Meridian.Tests/Meridian.Tests.csproj",
            "FullyQualifiedName~Meridian.Tests.Application",
        )
        results_dir = Path("artifacts/test-results/dotnet")

        command = MODULE.build_dotnet_test_command(
            project,
            configuration="Release",
            test_filter="Category!=Integration&Category!=Performance",
            results_dir=results_dir,
        )

        self.assertIn(
            "(Category!=Integration&Category!=Performance)&(FullyQualifiedName~Meridian.Tests.Application)",
            command,
        )
        self.assertIn("trx;LogFilePrefix=core-application", command)

    def test_project_override_does_not_apply_default_shards(self):
        projects = MODULE.parse_project_entries(["custom=tests/Custom.Tests/Custom.Tests.csproj"])

        self.assertEqual(len(projects), 1)
        self.assertEqual(projects[0].name, "custom")
        self.assertEqual(projects[0].path, "tests/Custom.Tests/Custom.Tests.csproj")
        self.assertIsNone(projects[0].filter_expression)

    def test_write_summaries_records_all_project_statuses(self):
        with self.subTest("summary output"):
            tmp_path = Path("artifacts/test-results/unit-summary")
            tmp_path.mkdir(parents=True, exist_ok=True)
            summary_output = tmp_path / "summary.md"
            json_output = tmp_path / "summary.json"

            results = [
                MODULE.TestResult("core", "tests/Meridian.Tests/Meridian.Tests.csproj", 0, ["dotnet", "test"]),
                MODULE.TestResult("ui", "tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj", 1, ["dotnet", "test"]),
            ]

            MODULE.write_summaries(results, summary_output=summary_output, json_output=json_output)

            payload = json.loads(json_output.read_text(encoding="utf-8"))
            self.assertEqual(payload["total"], 2)
            self.assertEqual(payload["passed"], 1)
            self.assertEqual(payload["failed"], 1)
            summary = summary_output.read_text(encoding="utf-8")
            self.assertIn("tests/Meridian.Tests/Meridian.Tests.csproj", summary)
            self.assertIn("tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj", summary)
            self.assertIn("❌ failed", summary)


if __name__ == "__main__":
    unittest.main()
