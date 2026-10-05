from __future__ import annotations

import argparse
import importlib.util
import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
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
        stderr = io.StringIO()
        with patch.object(self.buildctl, "_run_passthrough") as run, redirect_stderr(stderr):
            self.assertEqual(self.buildctl.cmd_test(self._args(no_build=True)), 2)
        run.assert_not_called()
        self.assertIn("existing --isolation-key or --no-isolation", stderr.getvalue())

    def test_test_command_builds_before_test_with_same_isolation_key(self) -> None:
        exit_code, commands, _, _ = self._run_test(
            self._args(run_id="run-one", project="tests/Example.Tests/Example.Tests.csproj")
        )

        self.assertEqual(exit_code, 0)
        self.assertEqual([command[1] for command in commands], ["restore", "build", "test"])
        self.assertTrue(
            all("/p:MeridianBuildIsolationKey=run-one" in command for command in commands)
        )
        self.assertIn("--no-restore", commands[1])
        self.assertIn("--no-build", commands[-1])
        self.assertIn("--no-restore", commands[-1])

    def test_skip_restore_still_builds_once_before_test(self) -> None:
        exit_code, commands, _, _ = self._run_test(self._args(skip_restore=True))

        self.assertEqual(exit_code, 0)
        self.assertEqual([command[1] for command in commands], ["build", "test"])
        self.assertIn("--no-build", commands[-1])
        self.assertIn("--no-restore", commands[-1])

    def test_no_build_reuses_explicit_outputs_without_restore_or_build(self) -> None:
        for selection in ({"isolation_key": "existing-output"}, {"no_isolation": True}):
            for skip_restore in (False, True):
                with self.subTest(selection=selection, skip_restore=skip_restore):
                    exit_code, commands, payload, _ = self._run_test(
                        self._args(no_build=True, skip_restore=skip_restore, **selection)
                    )

                    self.assertEqual(exit_code, 0)
                    self.assertEqual([command[1] for command in commands], ["test"])
                    self.assertIn("--no-build", commands[0])
                    self.assertIn("--no-restore", commands[0])
                    isolation_args = [
                        arg for arg in commands[0] if arg.startswith("/p:MeridianBuildIsolationKey=")
                    ]
                    expected_key = selection.get("isolation_key")
                    self.assertEqual(
                        isolation_args,
                        [f"/p:MeridianBuildIsolationKey={expected_key}"] if expected_key else [],
                    )
                    self.assertEqual(payload["isolationKey"], expected_key)
                    self.assertEqual(payload["status"], "passed")

    def test_output_selectors_and_test_options_are_preserved(self) -> None:
        for no_build in (False, True):
            with self.subTest(no_build=no_build):
                exit_code, commands, _, _ = self._run_test(
                    self._args(
                        no_build=no_build,
                        project="tests/Example.Tests/Example.Tests.csproj",
                        isolation_key="compatible-output",
                        configuration="Debug",
                        framework="net10.0-windows",
                        runtime="win-x64",
                        full_wpf_build=True,
                        property=["ExampleSlice=Small", "-p:UseAppHost=false"],
                        filter="FullyQualifiedName~Example",
                        settings="tests/example.runsettings",
                        logger=["trx", "console;verbosity=quiet"],
                        collect=["XPlat Code Coverage"],
                    )
                )

                self.assertEqual(exit_code, 0)
                for command in commands:
                    self.assertEqual(command[2], "tests/Example.Tests/Example.Tests.csproj")
                    self.assertIn("/p:MeridianBuildIsolationKey=compatible-output", command)
                    self.assertIn("/p:TargetFramework=net10.0-windows", command)
                    self.assertEqual(command[command.index("-r") + 1], "win-x64")
                    self.assertIn("/p:EnableFullWpfBuild=true", command)
                    self.assertIn("/p:ExampleSlice=Small", command)
                    self.assertIn("-p:UseAppHost=false", command)
                    if command[1] != "restore":
                        self.assertEqual(command[command.index("-c") + 1], "Debug")
                test_command = commands[-1]
                self.assertIn("--no-build", test_command)
                self.assertIn("--no-restore", test_command)
                self.assertEqual(test_command[test_command.index("--filter") + 1], "FullyQualifiedName~Example")
                self.assertEqual(test_command[test_command.index("--settings") + 1], "tests/example.runsettings")
                self.assertEqual(test_command.count("--logger"), 2)
                self.assertIn("trx", test_command)
                self.assertIn("console;verbosity=quiet", test_command)
                self.assertEqual(test_command[test_command.index("--collect") + 1], "XPlat Code Coverage")

    def test_failed_reuse_records_failure_without_build_fallback(self) -> None:
        exit_code, commands, payload, stderr = self._run_test(
            self._args(no_build=True, isolation_key="missing-output"), exit_codes={"test": 1}
        )

        self.assertEqual(exit_code, 1)
        self.assertEqual([command[1] for command in commands], ["test"])
        self.assertEqual(payload["status"], "failed")
        self.assertEqual(payload["exitCode"], 1)
        self.assertEqual(payload["steps"][0]["exitCode"], 1)
        self.assertIn("If outputs are missing or incompatible", stderr)
        self.assertIn("rerun without --no-build", stderr)

    def test_build_failure_does_not_run_tests_on_stale_outputs(self) -> None:
        exit_code, commands, payload, _ = self._run_test(
            self._args(), exit_codes={"build": 1}
        )

        self.assertEqual(exit_code, 1)
        self.assertEqual([command[1] for command in commands], ["restore", "build"])
        self.assertEqual(payload["status"], "failed")

    def _run_test(self, args, *, exit_codes=None):
        commands: list[list[str]] = []
        stderr = io.StringIO()
        args.run_id = args.run_id or "test-run"

        def run(command):
            commands.append(command)
            return (exit_codes or {}).get(command[1], 0)

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            with (
                patch.object(self.buildctl, "REPO_ROOT", repo_root),
                patch.object(self.buildctl, "_get_active_repo_build_processes", return_value=[]),
                patch.object(self.buildctl, "_prune_for_isolation"),
                patch.object(self.buildctl, "_run_passthrough", side_effect=run),
                redirect_stdout(io.StringIO()),
                redirect_stderr(stderr),
            ):
                exit_code = self.buildctl.cmd_test(args)
            payload = json.loads(
                (repo_root / ".ai/validation-runs" / f"{args.run_id}.json").read_text(encoding="utf-8")
            )
            self.assertIsNone(self.buildctl._read_validation_lock(repo_root))
        return exit_code, commands, payload, stderr.getvalue()

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
