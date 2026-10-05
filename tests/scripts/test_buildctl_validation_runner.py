from __future__ import annotations

import argparse
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


BUILDCTL_PATH = Path(__file__).resolve().parents[2] / "build" / "python" / "cli" / "buildctl.py"


def load_buildctl():
    spec = importlib.util.spec_from_file_location("buildctl_validation_under_test", BUILDCTL_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Unable to load buildctl.py from {BUILDCTL_PATH}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ValidationRunnerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.buildctl = load_buildctl()

    def test_auto_isolation_key_uses_run_id(self) -> None:
        self.assertEqual(
            self.buildctl._resolve_isolation_key("auto", run_id="test-run", disabled=False),
            "test-run",
        )

    def test_no_isolation_disables_isolation_key(self) -> None:
        self.assertIsNone(
            self.buildctl._resolve_isolation_key("custom", run_id="test-run", disabled=True)
        )

    def test_validation_lock_blocks_second_runner(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            first = self.buildctl._acquire_validation_lock(
                repo_root,
                run_id="first",
                command="first command",
                queue=False,
                timeout_seconds=0,
            )
            second = self.buildctl._acquire_validation_lock(
                repo_root,
                run_id="second",
                command="second command",
                queue=False,
                timeout_seconds=0,
            )

            self.assertTrue(first)
            self.assertFalse(second)
            self.buildctl._release_validation_lock(repo_root, "first")
            self.assertIsNone(self.buildctl._read_validation_lock(repo_root))

    def test_test_command_rejects_no_build_with_auto_isolation(self) -> None:
        args = self._args(no_build=True, isolation_key="auto")

        self.assertEqual(self.buildctl.cmd_test(args), 2)

    def test_test_command_builds_before_test_with_same_isolation_key(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            original_root = self.buildctl.REPO_ROOT
            original_active = self.buildctl._get_active_repo_build_processes
            original_prune = self.buildctl._prune_for_isolation
            original_run = self.buildctl._run_passthrough
            commands: list[list[str]] = []
            try:
                self.buildctl.REPO_ROOT = Path(temp_dir)
                self.buildctl._get_active_repo_build_processes = lambda repo_root=None: []
                self.buildctl._prune_for_isolation = lambda args, isolation_key: None
                self.buildctl._run_passthrough = lambda command: commands.append(command) or 0

                exit_code = self.buildctl.cmd_test(
                    self._args(run_id="run-one", project="tests/Example.Tests/Example.Tests.csproj")
                )
            finally:
                self.buildctl.REPO_ROOT = original_root
                self.buildctl._get_active_repo_build_processes = original_active
                self.buildctl._prune_for_isolation = original_prune
                self.buildctl._run_passthrough = original_run

            self.assertEqual(exit_code, 0)
            self.assertEqual([command[1] for command in commands], ["restore", "build", "test"])
            self.assertTrue(
                all(
                    "/p:MeridianBuildIsolationKey=run-one" in command
                    for command in commands
                )
            )
            self.assertIn("--no-build", commands[-1])

    def test_generated_run_ids_are_unique_within_one_process(self) -> None:
        self.assertEqual(len({self.buildctl._new_run_id("test") for _ in range(1000)}), 1000)

    def test_reports_use_unique_subdirectories_and_reject_repeated_ids(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_run_passthrough", return_value=0), \
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]):
            for run_id in ("first", "second"):
                self.assertEqual(self.buildctl.cmd_test(self._args(run_id=run_id, results_directory="reports")), 0)
                record = json.loads((Path(temp_dir) / f".ai/validation-runs/{run_id}.json").read_text())
                self.assertEqual(Path(record["resultsDirectory"]), Path(temp_dir) / "reports" / run_id)
            first = Path(temp_dir) / ".ai/validation-runs/first.json"
            evidence = first.read_bytes()
            self.assertEqual(self.buildctl.cmd_test(self._args(run_id="first")), 2)
            self.assertEqual(first.read_bytes(), evidence)

    def test_no_build_is_forwarded_without_restoring_or_mutating_build_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_run_passthrough", return_value=0) as run, \
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]):
            self.assertEqual(self.buildctl.cmd_test(self._args(no_build=True, isolation_key="existing", configuration="Debug")), 0)
            commands = [call.args[0] for call in run.call_args_list]
            self.assertEqual([command[1] for command in commands], ["test"])
            self.assertIn("--no-build", commands[-1])

    def test_restore_uses_same_configuration_and_normalized_properties_as_build(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_run_passthrough", return_value=0) as run, \
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]):
            self.assertEqual(self.buildctl.cmd_test(self._args(configuration="Debug", property=["UseAppHost=true", "/P:USEAPPHOST=false"])), 0)
            commands = [call.args[0] for call in run.call_args_list]
            self.assertIn("/p:Configuration=Debug", commands[0])
            for command in commands:
                self.assertEqual([arg for arg in command if "useapphost=" in arg.lower()], ["/p:useapphost=false"])

    def test_build_and_test_cannot_bypass_shared_output_lock(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_run_passthrough") as run, \
                patch.object(self.buildctl, "_prune_for_isolation") as prune:
            self.assertTrue(self.buildctl._acquire_validation_lock(Path(temp_dir), run_id="owner", command="owner", queue=False, timeout_seconds=0))
            self.assertEqual(self.buildctl.cmd_test(self._args(allow_concurrent=True)), 3)
            self.assertEqual(self.buildctl.cmd_build(self._args()), 3)
            run.assert_not_called()
            prune.assert_not_called()
            self.assertEqual(self.buildctl._read_validation_lock()["runId"], "owner")

    def test_build_holds_lock_through_prune_restore_build_and_releases_on_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]):
            phases = []
            def checked(phase):
                self.assertIsNotNone(self.buildctl._read_validation_lock())
                phases.append(phase)
            def run(command):
                checked(command[1])
                return 1 if command[1] == "build" else 0
            with patch.object(self.buildctl, "_prune_for_isolation", side_effect=lambda *args: checked("prune")), \
                    patch.object(self.buildctl, "_run_passthrough", side_effect=run):
                self.assertEqual(self.buildctl.cmd_build(self._args()), 1)
            self.assertEqual(phases, ["prune", "restore", "build"])
            self.assertIsNone(self.buildctl._read_validation_lock())

    def test_invalid_and_conflicting_output_options_fail_before_commands(self) -> None:
        with patch.object(self.buildctl, "_run_passthrough") as run:
            for updates in ({"run_id": "../escape"}, {"isolation_key": "../escape"},
                            {"isolation_key": "profile-reserved"}, {"profile": "worktree", "no_isolation": True},
                            {"profile": "worktree", "isolation_key": "other"}, {"profile": "worktree", "fresh": True},
                            {"fresh": True, "property": ["MeridianBuildIsolationKey=profile-existing"]},
                            {"fresh": True, "property": ["BaseOutputPath=/tmp/shared"]},
                            {"fresh": True, "property": ["Foo=1;OutDir=/tmp/shared"]},
                            {"fresh": True, "property": ["VSTestResultsDirectory=/tmp/shared"]},
                            {"fresh": True, "property": ["ImportDirectoryBuildProps=false"]},
                            {"fresh": True, "property": ["DirectoryBuildPropsPath=/tmp/shared.props"]},
                            {"fresh": True, "framework": "net10.0;BaseOutputPath=/tmp/shared"},
                            {"fresh": True, "configuration": "Release,BaseOutputPath=/tmp/shared"},
                            {"logger": ["trx;LogFileName=../../shared.trx"]},
                            {"logger": ["trx;LogFilePrefix=C:\\shared"]},
                            {"fresh": True, "run_id": "profile-existing"},
                            {"fresh": True, "skip_restore": True}):
                with self.subTest(updates=updates):
                    self.assertEqual(self.buildctl.cmd_test(self._args(**updates)), 2)
            run.assert_not_called()

    def test_fresh_run_cannot_reuse_preexisting_output_without_run_metadata(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_run_passthrough") as run, \
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]):
            (Path(temp_dir) / "artifacts/bin/existing").mkdir(parents=True)
            self.assertEqual(self.buildctl.cmd_test(self._args(fresh=True, run_id="existing")), 2)
            run.assert_not_called()
            self.assertIsNone(self.buildctl._read_validation_lock())

    def test_profile_reuses_builds_but_fresh_validation_uses_new_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_run", return_value=subprocess.CompletedProcess([], 0, "10.0.100\n")), \
                patch.object(self.buildctl, "_run_passthrough", return_value=0) as run, \
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]):
            project = Path(temp_dir) / "Example.csproj"
            project.write_text("<Project />")
            for run_id in ("one", "two"):
                self.assertEqual(self.buildctl.cmd_test(self._args(project="Example.csproj", profile="worktree", run_id=run_id)), 0)
            commands = [call.args[0] for call in run.call_args_list]
            keys = [next(arg for arg in command if arg.startswith("/p:MeridianBuildIsolationKey=")) for command in commands]
            self.assertEqual(len(set(keys)), 1)
            self.assertIn("profile-", keys[0])
            self.assertEqual([command[1] for command in commands], ["restore", "build", "test"] * 2)
            self.assertEqual(self.buildctl.cmd_test(self._args(project="Example.csproj", fresh=True, run_id="fresh")), 0)
            self.assertIn("/p:MeridianBuildIsolationKey=fresh", run.call_args.args[0])
            record = json.loads((Path(temp_dir) / ".ai/validation-runs/fresh.json").read_text())
            self.assertIsNone(record["profile"])

    def test_profile_compatibility_failure_never_launches_dotnet_build_or_test(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir, patch.object(self.buildctl, "REPO_ROOT", Path(temp_dir)), \
                patch.object(self.buildctl, "_run", return_value=subprocess.CompletedProcess([], 0, "10.0.100\n")), \
                patch.object(self.buildctl, "_run_passthrough", return_value=0) as run, \
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]):
            (Path(temp_dir) / "Example.csproj").write_text("<Project />")
            self.assertEqual(self.buildctl.cmd_test(self._args(project="Example.csproj", profile="worktree")), 0)
            run.reset_mock()
            self.assertEqual(self.buildctl.cmd_test(self._args(project="Example.csproj", profile="worktree", configuration="Debug")), 2)
            run.assert_not_called()
            self.assertIsNone(self.buildctl._read_validation_lock())

    def test_output_lock_queues_between_processes(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            self.assertTrue(self.buildctl._acquire_validation_lock(root, run_id="parent", command="parent", queue=False, timeout_seconds=0))
            script = "import sys; from pathlib import Path; sys.path.insert(0, sys.argv[1]); import buildctl; root=Path(sys.argv[2]); print('ready', flush=True); ok=buildctl._acquire_validation_lock(root, run_id='child', command='child', queue=True, timeout_seconds=5); buildctl._release_validation_lock(root, 'child') if ok else None; sys.exit(0 if ok else 1)"
            child = subprocess.Popen([sys.executable, "-c", script, str(BUILDCTL_PATH.parent), temp_dir], stdout=subprocess.PIPE, text=True)
            try:
                self.assertEqual(child.stdout.readline().strip(), "ready")
                self.assertIsNone(child.poll())
                self.buildctl._release_validation_lock(root, "parent")
                self.assertEqual(child.wait(timeout=10), 0)
                self.assertIsNone(self.buildctl._read_validation_lock(root))
            finally:
                if child.poll() is None:
                    child.kill()
                    child.wait()
                child.stdout.close()

    @staticmethod
    def _args(**overrides):
        defaults = {
            "lane": "dotnet",
            "project": "tests/Meridian.Tests/Meridian.Tests.csproj",
            "configuration": "Release",
            "framework": None,
            "runtime": None,
            "filter": "Category!=Integration",
            "verbosity": "quiet",
            "skip_restore": False,
            "no_build": False,
            "no_isolation": False,
            "full_wpf_build": False,
            "shutdown_build_servers": False,
            "allow_concurrent": False,
            "queue": False,
            "queue_timeout_seconds": 0,
            "run_id": None,
            "isolation_key": "auto",
            "results_directory": None,
            "settings": None,
            "logger": None,
            "collect": [],
            "isolation_retention_days": 0,
            "isolation_retain_latest": 0,
            "isolation_max_root_size_mb": 0,
            "property": [],
        }
        defaults.update(overrides)
        return argparse.Namespace(**defaults)


if __name__ == "__main__":
    unittest.main()
