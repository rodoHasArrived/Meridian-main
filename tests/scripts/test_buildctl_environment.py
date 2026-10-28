"""Behavioral checks for profile-specific environment readiness and restore failures."""

import argparse
import contextlib
import importlib.util
import io
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock


MODULE = Path(__file__).resolve().parents[2] / "build/python/cli/dev_environment.py"
spec = importlib.util.spec_from_file_location("dev_environment", MODULE)
environment = importlib.util.module_from_spec(spec)
spec.loader.exec_module(environment)


class EnvironmentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for relative in environment._inputs(self.root, "backend-connected"):
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("fixture input\n", encoding="utf-8")
        for parent in (self.root, self.root / environment.DASHBOARD):
            (parent / "package.json").write_text('{"name":"test"}', encoding="utf-8")
            (parent / "package-lock.json").write_text('{"lockfileVersion":3}', encoding="utf-8")
            (parent / "node_modules").mkdir()
        (self.root / "global.json").write_text('{"sdk":{"version":"10.0.100"}}', encoding="utf-8")
        self.versions = {"node": "v24.1.0", "npm": "11.1.0", "dotnet": "10.0.100"}
        self.failure = None
        self.which = mock.patch.object(environment.shutil, "which", side_effect=lambda name: name)
        self.which.start()
        self.addCleanup(self.which.stop)
        self.process = mock.patch.object(environment.subprocess, "run", side_effect=self.run_command)
        self.run_mock = self.process.start()
        self.addCleanup(self.process.stop)

    def run_command(self, command, **kwargs):
        if command[-1] == "--version":
            return subprocess.CompletedProcess(command, 0, self.versions[command[0]] + "\n", "")
        if self.failure and self.failure(command):
            return subprocess.CompletedProcess(command, 1, "", "https://user:secret@example.invalid/feed")
        for parent in (self.root, self.root / environment.DASHBOARD):
            (parent / "node_modules").mkdir(exist_ok=True)
        return subprocess.CompletedProcess(command, 0, "installed", "")

    def inspect(self, profile="fixture-only", restore=False):
        return environment.inspect_environment(self.root, profile, restore)

    def test_fixture_check_does_not_probe_dotnet_or_require_backend_files(self):
        (self.root / "global.json").unlink()
        report = self.inspect()
        self.assertEqual(report["status"], "ready")
        self.assertEqual(report["dependency_evidence"], "availability-only")
        self.assertNotIn("dotnet", report["tool_versions"])
        self.assertTrue(all(call.args[0][0] != "dotnet" for call in self.run_mock.call_args_list))

    def test_node_minimum_includes_22_12_and_newer_major_versions(self):
        for version, status in (("v18.0.0", "blocked"), ("v22.11.9", "blocked"),
                                ("v22.12.0", "ready"), ("v23.0.0", "ready")):
            with self.subTest(version=version):
                self.versions["node"] = version
                self.assertEqual(self.inspect()["status"], status)

    def test_missing_npm_blocks_without_restore_attempt(self):
        with mock.patch.object(environment.shutil, "which", side_effect=lambda name: None if name == "npm" else name):
            report = self.inspect(restore=True)
        self.assertEqual(report["restore_status"], "blocked")
        self.assertTrue(any(item["name"] == "npm" and item["status"] == "fail" for item in report["checks"]))
        self.assertFalse(any(item["name"].startswith("restore:") for item in report["commands"]))

    def test_missing_lock_blocks_restore(self):
        (self.root / environment.DASHBOARD / "package-lock.json").unlink()
        report = self.inspect(restore=True)
        self.assertEqual(report["restore_status"], "blocked")
        self.assertEqual(report["status"], "blocked")

    def test_invalid_lock_blocks_restore(self):
        (self.root / "package-lock.json").write_text("{}", encoding="utf-8")
        report = self.inspect(restore=True)
        self.assertEqual(report["restore_status"], "blocked")

    def test_missing_dependencies_can_be_restored(self):
        (self.root / "node_modules").rmdir()
        (self.root / environment.DASHBOARD / "node_modules").rmdir()
        self.assertEqual(self.inspect()["status"], "blocked")
        report = self.inspect(restore=True)
        self.assertEqual(report["status"], "ready")
        self.assertEqual(report["dependency_evidence"], "lockfile-restored")
        restores = [item["command"] for item in report["commands"] if item["name"].startswith("restore:")]
        self.assertEqual(restores, [["npm", "ci", "--include=optional"],
            ["npm", "--prefix", environment.DASHBOARD, "ci", "--include=optional"]])

    def test_failed_dashboard_restore_never_reports_ready_and_excludes_secret_output(self):
        self.failure = lambda command: "--prefix" in command
        report = self.inspect(restore=True)
        self.assertEqual(report["status"], "blocked")
        self.assertEqual(report["restore_status"], "failed")
        self.assertNotIn("secret", json.dumps(report))
        self.assertNotEqual(report["dependency_evidence"], "lockfile-restored")

    def test_root_install_failure_stops_later_restores(self):
        self.failure = lambda command: True
        report = self.inspect("backend-connected", restore=True)
        restores = [item for item in report["commands"] if item["name"].startswith("restore:")]
        self.assertEqual([item["name"] for item in restores], ["restore:root-npm"])

    def test_backend_restores_selected_host_only_and_no_database_probe(self):
        report = self.inspect("backend-connected", restore=True)
        self.assertEqual(report["status"], "ready")
        self.assertEqual(report["commands"][-1]["command"], ["dotnet", "restore", "src/Meridian/Meridian.csproj", "/p:EnableWindowsTargeting=true"])
        self.assertEqual(report["tool_versions"]["dotnet"], "10.0.100")
        self.assertTrue(all(call.kwargs["cwd"] == self.root for call in self.run_mock.call_args_list))

    def test_backend_sdk_resolution_failure_blocks_installs(self):
        def fail_sdk(command, **kwargs):
            if command[0] == "dotnet":
                return subprocess.CompletedProcess(command, 145, "", "no compatible sdk")
            return self.run_command(command, **kwargs)
        self.run_mock.side_effect = fail_sdk
        report = self.inspect("backend-connected", restore=True)
        self.assertEqual(report["restore_status"], "blocked")

    def test_fingerprint_is_stable_and_changes_with_lock_or_fixture(self):
        baseline = self.inspect()["fingerprint"]
        self.assertEqual(baseline, self.inspect()["fingerprint"])
        (self.root / "package-lock.json").write_text('{"lockfileVersion":3,"changed":true}', encoding="utf-8")
        locked = self.inspect()["fingerprint"]
        self.assertNotEqual(baseline, locked)
        (self.root / environment.DASHBOARD / "src/lib/dev-fixtures.ts").write_text("new fixtures", encoding="utf-8")
        self.assertNotEqual(locked, self.inspect()["fingerprint"])

    def test_fingerprint_includes_nested_fixture_files(self):
        fixture = self.root / environment.DASHBOARD / "src/lib/dev-fixtures/market.ts"
        fixture.parent.mkdir()
        fixture.write_text("first", encoding="utf-8")
        original = self.inspect()["fingerprint"]
        fixture.write_text("second", encoding="utf-8")
        self.assertNotEqual(original, self.inspect()["fingerprint"])

    def test_python_minimum_blocks_restore(self):
        with mock.patch.object(environment.sys, "version_info", (3, 10)):
            report = self.inspect(restore=True)
        self.assertEqual(report["restore_status"], "blocked")

    def test_probe_timeout_and_unstartable_tool_are_sanitized_failures(self):
        for error in (subprocess.TimeoutExpired("tool", 15, output="secret"), OSError("secret")):
            with self.subTest(error=type(error).__name__):
                self.run_mock.side_effect = error
                report = self.inspect()
                self.assertEqual(report["status"], "blocked")
                self.assertNotIn("secret", json.dumps(report))

    def test_windows_npm_shim_uses_node_entrypoint_without_shell(self):
        node_root = self.root / "Node With Spaces"
        entry = node_root / "node_modules/npm/bin/npm-cli.js"
        entry.parent.mkdir(parents=True)
        entry.write_text("// npm", encoding="utf-8")
        command = environment._npm_command(str(node_root / "npm.cmd"), str(node_root / "node.exe"))
        self.assertEqual(command, [str(node_root / "node.exe"), str(entry)])
        self.assertIsNone(environment._npm_command(str(self.root / "missing/npm.cmd"), "node"))
        environment._run(command + ["ci"], self.root, 10)
        self.assertFalse(self.run_mock.call_args.kwargs.get("shell", False))

    def test_parser_and_json_report(self):
        parser = argparse.ArgumentParser()
        environment.add_parser(parser.add_subparsers(dest="command", required=True))
        output = self.root / "evidence/environment.json"
        args = parser.parse_args(["environment", "--json-output", str(output), "--summary"])
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(environment.cmd_environment(args, self.root), 0)
        report = json.loads(output.read_text(encoding="utf-8"))
        self.assertEqual(report["profile"], "fixture-only")
        self.assertEqual(report["restore_status"], "not-requested")
        self.assertTrue(all(item["elapsed_ms"] >= 0 for item in report["commands"]))

    def test_relative_report_path_resolves_against_repository_root(self):
        args = argparse.Namespace(profile="fixture-only", restore=False,
                                  json_output=Path("evidence/environment.json"), summary=True)
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(environment.cmd_environment(args, self.root), 0)
        self.assertTrue((self.root / args.json_output).is_file())

    def test_input_output_paths_are_rejected_before_restore(self):
        for relative in ("package-lock.json", "global.json", f"{environment.DASHBOARD}/src/lib/dev-fixtures.ts"):
            with self.subTest(relative=relative):
                original = (self.root / relative).read_bytes()
                self.run_mock.reset_mock()
                args = argparse.Namespace(profile="fixture-only", restore=True,
                                          json_output=Path(relative), summary=True)
                with contextlib.redirect_stdout(io.StringIO()):
                    self.assertEqual(environment.cmd_environment(args, self.root), 1)
                self.run_mock.assert_not_called()
                self.assertEqual((self.root / relative).read_bytes(), original)

    def test_output_symlink_to_lockfile_is_rejected(self):
        alias = self.root / "report.json"
        try:
            alias.symlink_to(self.root / "package-lock.json")
        except OSError:
            self.skipTest("Symlink creation unavailable on this platform")
        args = argparse.Namespace(profile="fixture-only", restore=True, json_output=alias, summary=True)
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(environment.cmd_environment(args, self.root), 1)
        self.run_mock.assert_not_called()


if __name__ == "__main__":
    unittest.main()
