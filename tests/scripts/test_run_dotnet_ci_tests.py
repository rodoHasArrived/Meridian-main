import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import threading
import unittest
from concurrent.futures import ThreadPoolExecutor
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
from unittest.mock import patch

SCRIPT_PATH = Path(__file__).resolve().parents[2] / "build" / "scripts" / "ci" / "run-dotnet-ci-tests.py"
SPEC = importlib.util.spec_from_file_location("run_dotnet_ci_tests", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class RunDotnetCiTestsTests(unittest.TestCase):
    def run_projects(self, projects, results_dir, **kwargs):
        return MODULE.run_tests(
            projects, configuration="Release", test_filter="Category!=Integration&Category!=Performance",
            results_dir=Path(results_dir), dry_run=False, **kwargs,
        )

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

    def test_solution_filter_contains_exactly_default_roots_with_solution_relative_paths(self):
        projects = MODULE.parse_project_entries([])
        repo_root = SCRIPT_PATH.parents[3]
        with tempfile.TemporaryDirectory(prefix="meridian build results ") as tmp:
            filter_path = Path(tmp) / "custom results" / "test build.slnf"
            MODULE.write_build_solution_filter(projects, repo_root=repo_root, filter_path=filter_path)
            solution = json.loads(filter_path.read_text(encoding="utf-8"))["solution"]
            resolved_solution = (filter_path.parent / solution["path"]).resolve()
            self.assertEqual(resolved_solution, repo_root / "Meridian.sln")
            self.assertEqual(solution["projects"], [
                project.path.replace("/", "\\") for project in MODULE.get_unique_build_projects(projects)
            ])
            self.assertEqual(len(solution["projects"]), 9)
            self.assertTrue(all((resolved_solution.parent / path.replace("\\", "/")).is_file()
                                for path in solution["projects"]))

    def test_solution_filter_preserves_spaces_and_resolves_relative_results_directory(self):
        with tempfile.TemporaryDirectory(prefix="meridian repo ") as tmp:
            repo_root = Path(tmp) / "source tree"
            repo_root.mkdir()
            project_path = "tests/My Tests/My Tests.csproj"
            solution_entry = project_path.replace("/", "\\")
            (repo_root / "Meridian.sln").write_text(
                f'Project("{{type}}") = "My Tests", "{solution_entry}", "{{project}}"\nEndProject\n'
                '{project}.Release|Any CPU.ActiveCfg = Release|Any CPU\n'
                '{project}.Release|Any CPU.Build.0 = Release|Any CPU\n',
                encoding="utf-8",
            )
            projects = [MODULE.TestProject("my-tests", project_path)]
            absolute_filter = Path(tmp) / "nested results" / "build.slnf"
            # Exercise a relative --results-dir without changing the test process cwd.
            filter_path = Path(os.path.relpath(absolute_filter))
            MODULE.write_build_solution_filter(projects, repo_root=repo_root, filter_path=filter_path)
            solution = json.loads(filter_path.read_text(encoding="utf-8"))["solution"]
            self.assertFalse(Path(solution["path"]).is_absolute())
            self.assertEqual((filter_path.parent / solution["path"]).resolve(), repo_root / "Meridian.sln")
            self.assertEqual(solution["projects"], [solution_entry])

    def test_solution_filter_uses_absolute_solution_when_results_are_on_another_drive(self):
        with tempfile.TemporaryDirectory() as tmp:
            filter_path = Path(tmp) / "build.slnf"
            with patch.object(MODULE.os.path, "relpath", side_effect=ValueError("different drives")):
                MODULE.write_build_solution_filter(
                    MODULE.parse_project_entries([]), repo_root=SCRIPT_PATH.parents[3], filter_path=filter_path,
                )
            solution = json.loads(filter_path.read_text(encoding="utf-8"))["solution"]
            self.assertEqual(Path(solution["path"]), SCRIPT_PATH.parents[3] / "Meridian.sln")

    def test_solution_filter_rejects_missing_roots_before_writing_filter(self):
        with tempfile.TemporaryDirectory() as tmp:
            filter_path = Path(tmp) / "build.slnf"
            with self.assertRaisesRegex(ValueError, "missing from Meridian.sln"):
                MODULE.write_build_solution_filter(
                    [MODULE.TestProject("missing", "tests/Missing.csproj")],
                    repo_root=SCRIPT_PATH.parents[3], filter_path=filter_path,
                )
            self.assertFalse(filter_path.exists())

    def test_solution_filter_rejects_disabled_missing_or_remapped_release_builds(self):
        valid_mappings = {
            "ActiveCfg": "Release|Any CPU",
            "Build.0": "Release|Any CPU",
        }
        for invalid_mapping in valid_mappings:
            for value in (None, "Debug|Any CPU", "Release|x64"):
                with self.subTest(mapping=invalid_mapping, value=value), tempfile.TemporaryDirectory() as tmp:
                    repo_root = Path(tmp)
                    mappings = valid_mappings | {invalid_mapping: value}
                    (repo_root / "Meridian.sln").write_text(
                        'Project("{type}") = "Tests", "tests\\Tests.csproj", "{project}"\nEndProject\n' +
                        "".join(f"{{project}}.Release|Any CPU.{name} = {target}\n"
                                for name, target in mappings.items() if target is not None),
                        encoding="utf-8",
                    )
                    filter_path = repo_root / "results" / "build.slnf"
                    with self.assertRaisesRegex(ValueError, "requires Release"):
                        MODULE.write_build_solution_filter(
                            [MODULE.TestProject("tests", "tests/Tests.csproj")],
                            repo_root=repo_root, filter_path=filter_path,
                        )
                    self.assertFalse(filter_path.exists())

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

    def test_parallel_option_defaults_to_serial_and_can_be_set_by_environment_or_cli(self):
        with patch.dict(os.environ, {}, clear=True), patch.object(sys, "argv", [str(SCRIPT_PATH)]):
            self.assertEqual(MODULE.parse_args().max_parallel, 1)
        with patch.dict(os.environ, {"MERIDIAN_CI_TEST_MAX_PARALLEL": "2"}):
            with patch.object(sys, "argv", [str(SCRIPT_PATH)]):
                self.assertEqual(MODULE.parse_args().max_parallel, 2)
            with patch.object(sys, "argv", [str(SCRIPT_PATH), "--max-parallel", "3"]):
                self.assertEqual(MODULE.parse_args().max_parallel, 3)

    def test_invalid_parallel_values_fail_before_any_process_starts(self):
        for value in ("0", "-1", "1.5", "invalid", ""):
            with self.subTest(value=value), patch.object(MODULE.subprocess, "run") as run:
                with patch.dict(os.environ, {"MERIDIAN_CI_TEST_MAX_PARALLEL": value}):
                    with patch.object(sys, "argv", [str(SCRIPT_PATH)]), redirect_stderr(io.StringIO()):
                        with self.assertRaises(SystemExit) as exit_result:
                            MODULE.main()
                self.assertEqual(exit_result.exception.code, 2)
                run.assert_not_called()
        with patch.object(sys, "argv", [str(SCRIPT_PATH), "--max-parallel", "0"]):
            with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
                MODULE.parse_args()

    def test_project_names_cannot_escape_or_share_shard_directories(self):
        for name in ("../outside", "a/b", "a\\b", ".", "..", "bad name", "CON", "lpt1", "-option", "a" * 65):
            with self.subTest(name=name), self.assertRaises(ValueError):
                MODULE.parse_project_entries([f"{name}=tests/Custom.Tests.csproj"])
        with self.assertRaises(ValueError):
            MODULE.parse_project_entries(["core=a.csproj", "CORE=b.csproj"])

    def test_two_shards_overlap_but_active_processes_never_exceed_limit(self):
        projects = [MODULE.TestProject(f"shard-{i}", f"tests/{i}.csproj") for i in range(6)]
        pair_started = threading.Barrier(2, timeout=10)
        lock = threading.Lock()
        active = 0
        peak_active = 0
        attempted = []

        def complete_shard(command, **kwargs):
            nonlocal active, peak_active
            with lock:
                active += 1
                peak_active = max(peak_active, active)
                attempted.append(command[2])
            try:
                pair_started.wait()
                return subprocess.CompletedProcess(command, 0)
            finally:
                with lock:
                    active -= 1

        with tempfile.TemporaryDirectory() as tmp, patch.object(MODULE.subprocess, "run", side_effect=complete_shard):
            results = self.run_projects(projects, tmp, max_parallel=2)

        self.assertEqual(peak_active, 2)
        self.assertCountEqual(attempted, [project.path for project in projects])
        self.assertEqual([result.name for result in results], [project.name for project in projects])
        self.assertTrue(all(result.exit_code == 0 for result in results))

    def test_serial_default_waits_for_previous_process_to_exit(self):
        projects = [MODULE.TestProject("first", "tests/first.csproj"), MODULE.TestProject("second", "tests/second.csproj")]
        first_started = threading.Event()
        release_first = threading.Event()
        events = []

        def complete_shard(command, **kwargs):
            name = Path(command[2]).stem
            events.append(f"start:{name}")
            if name == "first":
                first_started.set()
                self.assertTrue(release_first.wait(10))
            events.append(f"finish:{name}")
            return subprocess.CompletedProcess(command, 0)

        with tempfile.TemporaryDirectory() as tmp, patch.object(MODULE.subprocess, "run", side_effect=complete_shard):
            with ThreadPoolExecutor(max_workers=1) as coordinator:
                future = coordinator.submit(self.run_projects, projects, tmp)
                try:
                    self.assertTrue(first_started.wait(10))
                    self.assertEqual(events, ["start:first"])
                finally:
                    release_first.set()
                results = future.result(timeout=10)

        self.assertEqual(events, ["start:first", "finish:first", "start:second", "finish:second"])
        self.assertEqual(len(results), 2)

    def test_failed_and_unlaunchable_shards_do_not_stop_remaining_shards_or_reorder_summary(self):
        projects = [MODULE.TestProject(name, f"tests/{name}.csproj") for name in ("slow", "failed", "unlaunchable", "last")]
        last_finished = threading.Event()
        completion_order = []

        def complete_shard(command, **kwargs):
            name = Path(command[2]).stem
            if name == "slow":
                self.assertTrue(last_finished.wait(10))
            if name == "unlaunchable":
                raise OSError("test executable missing")
            if name == "failed":
                kwargs["stdout"].write("assertion failed: expected complete ledger evidence\n")
            completion_order.append(name)
            if name == "last":
                last_finished.set()
            return subprocess.CompletedProcess(command, 1 if name == "failed" else 0)

        with tempfile.TemporaryDirectory() as tmp, patch.object(MODULE.subprocess, "run", side_effect=complete_shard):
            output = io.StringIO()
            with redirect_stdout(output):
                results = self.run_projects(projects, tmp, max_parallel=2)
            summary_output = Path(tmp) / "summary.md"
            json_output = Path(tmp) / "summary.json"
            MODULE.write_summaries(results, summary_output=summary_output, json_output=json_output)
            payload = json.loads(json_output.read_text(encoding="utf-8"))
            summary = summary_output.read_text(encoding="utf-8")
            self.assertEqual([result.exit_code for result in results], [0, 1, 127, 0])
            self.assertEqual([row["name"] for row in payload["results"]], [project.name for project in projects])
            self.assertEqual(payload["failed"], 2)
            self.assertIn("assertion failed: expected complete ledger evidence", output.getvalue())
            self.assertIn("test executable missing", output.getvalue())
            self.assertIn("test executable missing", Path(results[2].log_path).read_text(encoding="utf-8"))
            self.assertIn("Duration (s)", summary)
            self.assertTrue(all(row["duration_seconds"] >= 0 for row in payload["results"]))
            self.assertTrue(all(row["log_path"] for row in payload["results"]))
            self.assertNotIn("::group::", output.getvalue())
            self.assertNotIn("::endgroup::", output.getvalue())
        self.assertLess(completion_order.index("last"), completion_order.index("slow"))

    def test_real_children_inherit_environment_and_cwd_with_isolated_logs_results_and_temp(self):
        projects = [MODULE.TestProject(name, f"tests/{name}.csproj") for name in ("one", "two")]
        child_script = (
            "import json, os, pathlib, sys; "
            "temporary = pathlib.Path(os.environ['TMPDIR']); "
            "(temporary / 'shared-fixture-name').write_text(sys.argv[1]); "
            "print(json.dumps({'name': sys.argv[1], 'cwd': os.getcwd(), "
            "'inherited': os.environ['MERIDIAN_TEST_SENTINEL'], "
            "'tmp': [os.environ[key] for key in ('TMPDIR', 'TMP', 'TEMP')]}), flush=True); "
            "print('stderr from ' + sys.argv[1], file=sys.stderr)"
        )
        result_directories = []

        def child_command(project, **kwargs):
            result_directories.append(kwargs["results_dir"])
            return [sys.executable, "-c", child_script, project.name]

        with tempfile.TemporaryDirectory() as tmp:
            with patch.dict(os.environ, {"MERIDIAN_TEST_SENTINEL": "preserved"}):
                parent_temps = {name: os.environ.get(name) for name in ("TMPDIR", "TMP", "TEMP")}
                with patch.object(MODULE, "build_dotnet_test_command", side_effect=child_command):
                    results = self.run_projects(projects, tmp, max_parallel=2)
                self.assertEqual({name: os.environ.get(name) for name in parent_temps}, parent_temps)
            temporary_dirs = []
            for result in results:
                self.assertEqual(result.exit_code, 0)
                self.assertGreater(result.duration_seconds, 0)
                lines = Path(result.log_path).read_text(encoding="utf-8").splitlines()
                observed = json.loads(next(line for line in lines if line.startswith('{"name":')))
                self.assertEqual(observed["name"], result.name)
                self.assertEqual(observed["cwd"], os.getcwd())
                self.assertEqual(observed["inherited"], "preserved")
                self.assertEqual(len(set(observed["tmp"])), 1)
                temporary_dir = Path(observed["tmp"][0])
                self.assertFalse(temporary_dir.exists(), "fixture temporary data must be removed after exit")
                self.assertFalse(temporary_dir.is_relative_to(Path(tmp)), "fixtures must stay outside uploaded results")
                temporary_dirs.append(temporary_dir)
                self.assertIn(f"stderr from {result.name}", lines)
                self.assertEqual(Path(result.log_path).parent, Path(tmp).resolve() / result.name)
            self.assertEqual(len(set(temporary_dirs)), 2)
            self.assertCountEqual(result_directories, [Path(tmp).resolve() / project.name for project in projects])

    def test_noisy_failure_keeps_full_disk_log_but_only_prints_bounded_tail(self):
        project = MODULE.TestProject("noisy", "tests/noisy.csproj")

        def noisy_failure(command, **kwargs):
            self.assertEqual(kwargs["stderr"], subprocess.STDOUT)
            self.assertNotIn("capture_output", kwargs)
            self.assertNotIn("cwd", kwargs)
            kwargs["stdout"].write("x" * 100_000 + "\nfinal failure detail\n")
            return subprocess.CompletedProcess(command, 1)

        with tempfile.TemporaryDirectory() as tmp, patch.object(MODULE.subprocess, "run", side_effect=noisy_failure):
            output = io.StringIO()
            with redirect_stdout(output):
                results = self.run_projects([project], tmp, max_parallel=2)
            self.assertGreater(Path(results[0].log_path).stat().st_size, 100_000)
            self.assertLess(len(output.getvalue()), 18_000)
            self.assertIn("final failure detail", output.getvalue())

    def test_parallel_default_roster_preserves_every_filter_timeout_and_result_directory(self):
        projects = MODULE.parse_project_entries([])
        with tempfile.TemporaryDirectory() as tmp:
            results = MODULE.run_tests(
                projects, configuration="Release", test_filter="Category!=Integration&Category!=Performance",
                results_dir=Path(tmp), dry_run=True, max_parallel=2,
            )
            for project, result in zip(projects, results, strict=True):
                command = result.command
                self.assertEqual(command[command.index("--filter") + 1], MODULE.combine_filters(
                    "Category!=Integration&Category!=Performance", project.filter_expression,
                ))
                self.assertEqual(command[command.index("--blame-hang-timeout") + 1], "10m")
                self.assertIn("--no-build", command)
                self.assertIn("--no-restore", command)
                self.assertEqual(Path(command[command.index("--results-directory") + 1]), Path(tmp).resolve() / project.name)
            self.assertEqual(len(results), len(projects))

    def test_builds_complete_serially_once_per_project_before_any_parallel_tests_start(self):
        projects = [MODULE.TestProject("one", "tests/shared.csproj"), MODULE.TestProject("two", "tests/shared.csproj"),
                    MODULE.TestProject("other", "tests/other.csproj")]
        events = []

        def complete(command, **kwargs):
            events.append((command[1], command[2]))
            return subprocess.CompletedProcess(command, 0)

        with tempfile.TemporaryDirectory() as tmp:
            args = self.main_args(tmp, project=["custom=tests/shared.csproj"], max_parallel=2)
            with patch.object(MODULE, "parse_args", return_value=args), patch.object(MODULE, "parse_project_entries", return_value=projects):
                with patch.object(MODULE.subprocess, "run", side_effect=complete):
                    self.assertEqual(MODULE.main(), 0)
        self.assertEqual(events[:2], [("build", "tests/shared.csproj"), ("build", "tests/other.csproj")])
        self.assertTrue(all(kind == "test" for kind, _ in events[2:]))
        self.assertEqual(len(events), 5)

    def test_default_roster_builds_once_before_every_unchanged_parallel_test_slice(self):
        projects = MODULE.parse_project_entries([])
        commands = []

        def complete(command, **kwargs):
            commands.append(command)
            return subprocess.CompletedProcess(command, 0)

        with tempfile.TemporaryDirectory() as tmp:
            args = self.main_args(tmp, project=[])
            with patch.object(MODULE, "parse_args", return_value=args):
                with patch.object(MODULE.subprocess, "run", side_effect=complete):
                    with patch.object(MODULE, "run_tests", wraps=MODULE.run_tests) as tests:
                        self.assertEqual(MODULE.main(), 0)
            self.assertEqual(tests.call_args.kwargs["max_parallel"], 2)
            filter_path = Path(args.results_dir).resolve() / "ci-dotnet-test-build.slnf"
            self.assertEqual(commands[0], [
                "dotnet", "build", str(filter_path), "-c", "Release", "--no-restore",
                "/p:EnableWindowsTargeting=true",
            ])
            self.assertEqual(len(commands), 1 + len(projects))
            expected = [MODULE.build_dotnet_test_command(
                project, configuration="Release", test_filter=args.filter,
                results_dir=Path(args.results_dir).resolve() / project.name,
            ) for project in projects]
            self.assertCountEqual(commands[1:], expected)
            payload = json.loads(Path(args.json_output).read_text(encoding="utf-8"))
            self.assertEqual(payload["total"], len(projects))
            self.assertEqual(payload["passed"], len(projects))
            self.assertEqual([row["name"] for row in payload["results"]], [p.name for p in projects])
            self.assertEqual(len(payload["build_results"]), 1)
            build = payload["build_results"][0]
            self.assertEqual(build["status"], "passed")
            self.assertGreaterEqual(build["duration_seconds"], 0)
            self.assertEqual(Path(build["log_path"]), filter_path.parent / "dotnet-build.log")
            self.assertTrue(Path(build["log_path"]).is_file())
            self.assertIn("Build evidence", Path(args.summary_output).read_text(encoding="utf-8"))

    def test_default_group_failure_keeps_original_failure_after_all_diagnostic_builds_pass(self):
        projects = MODULE.parse_project_entries([])
        roots = MODULE.get_unique_build_projects(projects)
        for failure in (7, OSError("dotnet executable missing")):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as tmp:
                args = self.main_args(tmp, project=[])
                commands = []

                def complete(command, **kwargs):
                    commands.append(command)
                    if command[2].endswith(".slnf"):
                        if isinstance(failure, OSError):
                            raise failure
                        kwargs["stdout"].write("group build failure evidence\n")
                        return subprocess.CompletedProcess(command, failure)
                    return subprocess.CompletedProcess(command, 0)

                with patch.object(MODULE, "parse_args", return_value=args):
                    with patch.object(MODULE.subprocess, "run", side_effect=complete):
                        with patch.object(MODULE, "run_tests") as tests, redirect_stderr(io.StringIO()):
                            self.assertEqual(MODULE.main(), 1)
                tests.assert_not_called()
                self.assertEqual([command[2] for command in commands[1:]], [root.path for root in roots])
                self.assertTrue(all(command[1] == "build" for command in commands))
                payload = json.loads(Path(args.json_output).read_text(encoding="utf-8"))
                self.assertEqual(payload["total"], 1 + len(roots))
                self.assertEqual(payload["failed"], 1)
                self.assertEqual(payload["results"][0]["exit_code"], 127 if isinstance(failure, OSError) else failure)
                self.assertTrue(all(result["exit_code"] == 0 for result in payload["results"][1:]))
                log = Path(payload["results"][0]["log_path"]).read_text(encoding="utf-8")
                self.assertIn(str(failure) if isinstance(failure, OSError) else "group build failure evidence", log)

    def test_default_non_release_configuration_builds_serially_without_solution_filter(self):
        projects = MODULE.parse_project_entries([])
        roots = MODULE.get_unique_build_projects(projects)
        with tempfile.TemporaryDirectory() as tmp:
            args = self.main_args(tmp, project=[])
            args.configuration = "CustomConfiguration"
            with patch.object(MODULE, "parse_args", return_value=args):
                with patch.object(MODULE, "write_build_solution_filter") as write_filter:
                    with patch.object(MODULE.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run:
                        self.assertEqual(MODULE.main(), 0)
            write_filter.assert_not_called()
            commands = [call.args[0] for call in run.call_args_list]
            self.assertEqual([command[2] for command in commands[:len(roots)]], [root.path for root in roots])
            self.assertTrue(all(command[1] == "build" for command in commands[:len(roots)]))
            self.assertTrue(all(command[1] == "test" for command in commands[len(roots):]))
            self.assertEqual(len(commands), len(roots) + len(projects))
            self.assertTrue(all(command[command.index("-c") + 1] == args.configuration for command in commands))

    def test_invalid_release_mapping_retains_failure_after_serial_diagnostics_and_never_tests(self):
        roots = MODULE.get_unique_build_projects(MODULE.parse_project_entries([]))
        with tempfile.TemporaryDirectory() as tmp:
            args = self.main_args(tmp, project=[])
            with patch.object(MODULE, "parse_args", return_value=args):
                with patch.object(MODULE, "write_build_solution_filter", side_effect=ValueError("invalid Release mapping")):
                    with patch.object(MODULE.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)) as run:
                        with patch.object(MODULE, "run_tests") as tests, redirect_stderr(io.StringIO()):
                            self.assertEqual(MODULE.main(), 1)
            tests.assert_not_called()
            self.assertEqual([call.args[0][2] for call in run.call_args_list], [root.path for root in roots])
            payload = json.loads(Path(args.json_output).read_text(encoding="utf-8"))
            self.assertEqual(payload["total"], len(roots) + 1)
            self.assertEqual(payload["failed"], 1)
            self.assertEqual(payload["results"][0]["exit_code"], 127)
            self.assertIn("invalid Release mapping", Path(payload["results"][0]["log_path"]).read_text(encoding="utf-8"))

    def test_default_group_and_diagnostic_launch_failures_attempt_all_builds_and_never_tests(self):
        roots = MODULE.get_unique_build_projects(MODULE.parse_project_entries([]))
        with tempfile.TemporaryDirectory() as tmp:
            args = self.main_args(tmp, project=[])
            with patch.object(MODULE, "parse_args", return_value=args):
                with patch.object(MODULE.subprocess, "run", side_effect=OSError("dotnet missing")) as run:
                    with patch.object(MODULE, "run_tests") as tests, redirect_stderr(io.StringIO()):
                        self.assertEqual(MODULE.main(), 1)
            tests.assert_not_called()
            self.assertEqual(run.call_count, len(roots) + 1)
            payload = json.loads(Path(args.json_output).read_text(encoding="utf-8"))
            self.assertEqual(payload["failed"], len(roots) + 1)
            self.assertTrue(all(result["exit_code"] == 127 for result in payload["results"]))

    def test_default_build_filter_error_produces_failed_result_and_evidence_without_launch(self):
        with tempfile.TemporaryDirectory() as tmp:
            with patch.object(MODULE.subprocess, "run") as run:
                result = MODULE.run_default_build(
                    [MODULE.TestProject("missing", "tests/Missing.csproj")],
                    repo_root=SCRIPT_PATH.parents[3], configuration="Release", results_dir=Path(tmp), dry_run=False,
                )
            run.assert_not_called()
            self.assertEqual(result.exit_code, 127)
            self.assertIn("missing from Meridian.sln", Path(result.log_path).read_text(encoding="utf-8"))

    def test_noisy_group_failure_keeps_full_log_and_prints_only_bounded_tail(self):
        def noisy_failure(command, **kwargs):
            self.assertEqual(kwargs["stderr"], subprocess.STDOUT)
            self.assertNotIn("capture_output", kwargs)
            self.assertNotIn("cwd", kwargs)
            kwargs["stdout"].write("x" * 100_000 + "\nlast build error\n")
            return subprocess.CompletedProcess(command, 1)

        with tempfile.TemporaryDirectory() as tmp, patch.object(MODULE.subprocess, "run", side_effect=noisy_failure):
            output = io.StringIO()
            with redirect_stdout(output):
                result = MODULE.run_default_build(
                    MODULE.parse_project_entries([]), repo_root=SCRIPT_PATH.parents[3],
                    configuration="Release", results_dir=Path(tmp), dry_run=False,
                )
            self.assertGreater(Path(result.log_path).stat().st_size, 100_000)
            self.assertLess(len(output.getvalue()), 18_000)
            self.assertIn("last build error", output.getvalue())

    def test_default_dry_run_writes_filter_log_and_all_test_evidence_without_processes(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = self.main_args(tmp, project=[])
            args.dry_run = True
            with patch.object(MODULE, "parse_args", return_value=args):
                with patch.object(MODULE.subprocess, "run") as run:
                    self.assertEqual(MODULE.main(), 0)
            run.assert_not_called()
            payload = json.loads(Path(args.json_output).read_text(encoding="utf-8"))
            self.assertEqual(payload["total"], len(MODULE.DEFAULT_TEST_PROJECTS))
            build = payload["build_results"][0]
            self.assertTrue(Path(build["path"]).is_file())
            self.assertTrue(Path(build["log_path"]).is_file())
            self.assertTrue(all(Path(result["log_path"]).is_file() for result in payload["results"]))

    def main_args(self, tmp, *, project, max_parallel=2):
        return MODULE.argparse.Namespace(
            project=project, configuration="Release", filter="Category!=Integration&Category!=Performance",
            results_dir=str(Path(tmp) / "results"), summary_output=str(Path(tmp) / "summary.md"),
            json_output=str(Path(tmp) / "summary.json"), dry_run=False, max_parallel=max_parallel,
        )

    def test_build_failure_or_launch_error_attempts_remaining_builds_and_prevents_tests(self):
        for first_result in (1, OSError("dotnet missing")):
            with self.subTest(first_result=first_result), tempfile.TemporaryDirectory() as tmp:
                args = self.main_args(tmp, project=["first=tests/first.csproj", "second=tests/second.csproj"])
                def complete(command, **kwargs):
                    if command[2] == "tests/first.csproj":
                        if isinstance(first_result, OSError):
                            raise first_result
                        return subprocess.CompletedProcess(command, first_result)
                    return subprocess.CompletedProcess(command, 0)
                with patch.object(MODULE, "parse_args", return_value=args), patch.object(MODULE.subprocess, "run", side_effect=complete) as run:
                    with patch.object(MODULE, "run_tests") as tests, redirect_stderr(io.StringIO()):
                        self.assertEqual(MODULE.main(), 1)
                    tests.assert_not_called()
                self.assertEqual(run.call_count, 2)
                payload = json.loads(Path(args.json_output).read_text(encoding="utf-8"))
                self.assertEqual(payload["failed"], 1)
                self.assertEqual(payload["total"], 2)

    def test_unwired_project_stops_default_run_before_builds_or_tests(self):
        with tempfile.TemporaryDirectory() as tmp:
            args = self.main_args(tmp, project=[])
            with patch.object(MODULE, "parse_args", return_value=args):
                with patch.object(MODULE, "verify_test_project_coverage", return_value=["tests/Orphan.csproj"]):
                    with patch.object(MODULE, "write_build_solution_filter") as write_filter:
                        with patch.object(MODULE.subprocess, "run") as run, redirect_stderr(io.StringIO()):
                            self.assertEqual(MODULE.main(), 2)
                    write_filter.assert_not_called()
                    run.assert_not_called()

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
