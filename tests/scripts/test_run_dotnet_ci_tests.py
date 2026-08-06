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
                "core-reporting",
                "fsharp",
                "ui",
                "backtesting",
                "directlending",
                "fundstructure",
                "quantscript",
                "designmodules",
                "lifecycle",
                "core-remainder",
            ],
        )
        self.assertTrue(projects[0].filter_expression)
        self.assertTrue(projects[-1].filter_expression, "core-remainder must carry the catch-all filter")

    def test_core_reporting_shard_includes_all_reporting_governance_tests(self):
        projects = MODULE.parse_project_entries([])
        reporting = next(project for project in projects if project.name == "core-reporting")

        self.assertEqual(reporting.filter_expression, "FullyQualifiedName~Meridian.Tests.Reporting")

    def test_core_remainder_filter_excludes_every_explicit_core_prefix(self):
        remainder = MODULE.build_core_remainder_filter(MODULE.DEFAULT_TEST_PROJECTS[:-1])

        self.assertTrue(remainder.startswith("FullyQualifiedName~Meridian.Tests&"))
        self.assertIn("FullyQualifiedName!~Meridian.Tests.Application", remainder)
        self.assertIn("FullyQualifiedName!~Meridian.Tests.Reporting", remainder)
        self.assertIn("FullyQualifiedName!~Meridian.Tests.Storage", remainder)
        # The core-ui-other shard's own negation term must not leak in as a positive prefix.
        self.assertNotIn("FullyQualifiedName!~FullyQualifiedName", remainder)
        # Prefixes from non-core projects (paths other than Meridian.Tests) are irrelevant.
        self.assertNotIn("Meridian.Ui.Tests", remainder)

    def test_verify_test_project_coverage_flags_unwired_projects(self):
        import tempfile

        with tempfile.TemporaryDirectory() as tmp:
            repo_root = Path(tmp)
            wired = repo_root / "tests" / "Meridian.Wired.Tests"
            unwired = repo_root / "tests" / "Meridian.Orphan.Tests"
            wired.mkdir(parents=True)
            unwired.mkdir(parents=True)
            (wired / "Meridian.Wired.Tests.csproj").write_text("<Project />", encoding="utf-8")
            (unwired / "Meridian.Orphan.Tests.csproj").write_text("<Project />", encoding="utf-8")

            projects = [MODULE.TestProject("wired", "tests/Meridian.Wired.Tests/Meridian.Wired.Tests.csproj")]

            missing = MODULE.verify_test_project_coverage(repo_root, projects)

            self.assertEqual(missing, ["tests/Meridian.Orphan.Tests/Meridian.Orphan.Tests.csproj"])

    def test_verify_test_project_coverage_accepts_current_repository_roster(self):
        repo_root = Path(__file__).resolve().parents[2]
        projects = MODULE.parse_project_entries([])

        missing = MODULE.verify_test_project_coverage(repo_root, projects)

        self.assertEqual(missing, [], "every tests/ project must be wired to the ubuntu or windows lane")

    def test_process_helper_is_classified_as_support_instead_of_a_test_project(self):
        helper_path = "tests/Meridian.ProcessTestHelper/Meridian.ProcessTestHelper.csproj"

        self.assertIn(helper_path, MODULE.SUPPORT_TEST_PROJECTS)
        self.assertNotIn(helper_path, {project[1] for project in MODULE.DEFAULT_TEST_PROJECTS})

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

    def test_build_dotnet_test_command_enables_blame_hang_timeout(self):
        project = MODULE.TestProject("core", "tests/Meridian.Tests/Meridian.Tests.csproj")
        results_dir = Path("artifacts/test-results/dotnet")

        command = MODULE.build_dotnet_test_command(
            project,
            configuration="Release",
            test_filter="Category!=Integration&Category!=Performance",
            results_dir=results_dir,
        )

        self.assertIn("--blame-hang", command)
        self.assertIn("--blame-hang-timeout", command)
        timeout_value = command[command.index("--blame-hang-timeout") + 1]
        self.assertEqual(timeout_value, "10m")

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
