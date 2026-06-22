from __future__ import annotations

import argparse
import importlib.util
import tempfile
import unittest
from pathlib import Path


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
