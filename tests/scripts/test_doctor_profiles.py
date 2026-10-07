from __future__ import annotations

import importlib.util
import io
import json
import os
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
from collections import namedtuple
from contextlib import ExitStack, redirect_stdout
from pathlib import Path
from unittest.mock import patch


REPO_ROOT = Path(__file__).resolve().parents[2]
PYTHON_ROOT = REPO_ROOT / "build/python"
sys.path.insert(0, str(PYTHON_ROOT))

import prerequisites
from diagnostics import doctor


def load_buildctl():
    spec = importlib.util.spec_from_file_location(
        "buildctl_doctor_profiles_under_test", PYTHON_ROOT / "cli/buildctl.py"
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


VersionInfo = namedtuple("VersionInfo", "major minor micro releaselevel serial")


class DoctorProfileTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.buildctl = load_buildctl()

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.write("global.json", json.dumps({
            "sdk": {"version": "10.0.100", "rollForward": "latestMinor", "allowPrerelease": True}
        }))
        self.write(".nvmrc", "24\n")
        self.write("Meridian.sln", "Test solution\n")
        self.write("Directory.Build.props", "<Project />\n")
        self.write("Directory.Packages.props", "<Project />\n")
        self.write("src/Meridian.Wpf/Meridian.Wpf.csproj", "<Project />\n")
        self.write("build/scripts/docs/requirements.txt", "PyYAML==6.0.2\n")
        self.write("build/scripts/ci/requirements.txt", "-r ../docs/requirements.txt\nPillow==12.3.0\n")
        self.write("src/Meridian.Ui/dashboard/package.json", json.dumps({"engines": {"node": ">=24 <25"}}))
        self.write("src/Meridian.Ui/dashboard/package-lock.json", json.dumps({"lockfileVersion": 3}))
        self.available = {"dotnet", "node", "npm", "git", "pwsh", "actionlint", "promtool", "docker-compose"}
        self.versions = {
            "dotnet": (0, "10.0.100"),
            "node": (0, "v24.9.0"),
            "npm": (0, "11.6.0"),
            "git": (0, "git version 2.50.0"),
            "pwsh": (0, "7.5.2"),
            "actionlint": (0, "1.7.9\ninstalled by test fixture"),
            "docker-compose": (0, "Docker Compose version v2.39.4"),
        }
        self.packages = {"PyYAML": "6.0.2", "Pillow": "12.3.0"}
        self.commands: list[list[str]] = []
        self.python_version = VersionInfo(3, 11, 10, "final", 0)

    def write(self, relative: str, content: str) -> None:
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")

    def command_version(self, command, root):
        self.assertEqual(Path(root), self.root)
        self.commands.append(list(command))
        tool = Path(command[0]).name.removesuffix(".exe")
        return self.versions[tool]

    def package_version(self, package):
        if package not in self.packages:
            raise prerequisites.metadata.PackageNotFoundError(package)
        return self.packages[package]

    def environment(self):
        stack = ExitStack()
        stack.enter_context(patch.object(prerequisites.shutil, "which", side_effect=lambda tool: tool if tool in self.available else None))
        stack.enter_context(patch.object(prerequisites, "_command_version", side_effect=self.command_version))
        stack.enter_context(patch.object(prerequisites.sys, "version_info", self.python_version))
        stack.enter_context(patch.object(prerequisites.platform, "system", return_value="Windows"))
        stack.enter_context(patch.object(prerequisites.metadata, "version", side_effect=self.package_version))
        return stack

    def check(self, profile="full-quality-gate"):
        with self.environment():
            return prerequisites.check_prerequisites(self.root, profile)

    def row(self, results, token):
        matching = [row for row in results if token.lower() == row.name.lower()]
        if not matching:
            matching = [row for row in results if token.lower() in row.name.lower()]
        self.assertEqual(len(matching), 1, [(row.name, row.status) for row in results])
        return matching[0]

    def assert_actionable_failure(self, row):
        self.assertEqual(row.status, "fail", row)
        self.assertTrue(row.details, row)
        self.assertTrue(row.expected, row)
        self.assertTrue(row.fix, row)

    def test_supported_profiles_pass_with_required_prerequisites(self) -> None:
        self.assertEqual(set(prerequisites.PROFILE_NAMES), {"unit-test", "browser", "desktop", "full-quality-gate"})
        for profile in prerequisites.PROFILE_NAMES:
            with self.subTest(profile=profile):
                results = self.check(profile)
                self.assertTrue(results)
                self.assertTrue(all(row.status == "pass" for row in results), results)

    def test_full_quality_gate_rejects_python_310_with_install_fix(self) -> None:
        self.python_version = VersionInfo(3, 10, 16, "final", 0)
        result = self.row(self.check(), "Python")
        self.assert_actionable_failure(result)
        self.assertIn("3.11", result.expected)
        self.assertIn("3.11", result.fix)

    def test_browser_requires_supported_node_major(self) -> None:
        for version in ("v18.20.8", "v20.19.0", "v22.15.0", "v25.0.0"):
            with self.subTest(version=version):
                self.versions["node"] = (0, version)
                result = self.row(self.check("browser"), "Node")
                self.assert_actionable_failure(result)
                self.assertIn("24", result.expected)
                self.assertIn("24", result.fix)
        self.versions["node"] = (0, "v24.9.0")
        self.assertEqual(self.row(self.check("browser"), "Node").status, "pass")

    def test_browser_missing_node_or_failed_version_probe_fails(self) -> None:
        self.available.remove("node")
        self.assert_actionable_failure(self.row(self.check("browser"), "Node"))
        self.available.add("node")
        self.versions["node"] = (1, "v24.9.0")
        self.assert_actionable_failure(self.row(self.check("browser"), "Node"))

    def test_browser_reads_supported_node_version_from_nvmrc(self) -> None:
        self.write(".nvmrc", "25\n")
        self.assert_actionable_failure(self.row(self.check("browser"), "Node"))
        self.versions["node"] = (0, "v25.0.0")
        self.assertEqual(self.row(self.check("browser"), "Node").status, "pass")

    def test_browser_missing_or_invalid_node_definition_has_a_fix(self) -> None:
        for content in (None, "unsupported"):
            with self.subTest(content=content):
                if content is None:
                    (self.root / ".nvmrc").unlink()
                else:
                    self.write(".nvmrc", content)
                self.assert_actionable_failure(self.row(self.check("browser"), "Node"))

    def test_required_tool_missing_and_unreadable_versions_have_fixes(self) -> None:
        names = {"dotnet": ".NET", "node": "Node", "npm": "npm", "git": "Git", "pwsh": "PowerShell", "actionlint": "actionlint"}
        for tool, name in names.items():
            with self.subTest(tool=tool, failure="missing"):
                self.available.remove(tool)
                self.assert_actionable_failure(self.row(self.check(), name))
                self.available.add(tool)
            for outcome in ((1, "version command failed"), (0, "unknown"), (0, "")):
                with self.subTest(tool=tool, outcome=outcome):
                    original = self.versions[tool]
                    self.versions[tool] = outcome
                    self.assert_actionable_failure(self.row(self.check(), name))
                    self.versions[tool] = original

    def test_unit_test_requires_only_python_dotnet_and_git(self) -> None:
        self.available = {"dotnet", "git"}
        self.packages.clear()
        (self.root / "build/scripts/ci/requirements.txt").unlink()
        (self.root / ".nvmrc").unlink()
        results = self.check("unit-test")
        self.assertTrue(all(row.status == "pass" for row in results), results)
        names = " ".join(row.name for row in results).lower()
        for unrelated in ("node", "npm", "powershell", "pyyaml", "pillow", "actionlint", "alpaca", "postgres", "docker"):
            self.assertNotIn(unrelated, names)
        self.assertEqual({command[0] for command in self.commands}, {"dotnet", "git"})
        self.assertFalse((self.root / "config/appsettings.json").exists())

    def test_profiles_never_run_restore_build_or_service_probes(self) -> None:
        for profile in prerequisites.PROFILE_NAMES:
            with self.subTest(profile=profile):
                self.commands.clear()
                self.check(profile)
                for command in self.commands:
                    self.assertNotIn("restore", command)
                    self.assertNotIn("build", command)
                    self.assertNotIn("ci", command)
                    self.assertNotIn(command[0], {"docker", "psql"})

    def test_selected_dotnet_sdk_must_satisfy_global_json(self) -> None:
        self.versions["dotnet"] = (0, "11.0.100")
        result = self.row(self.check("unit-test"), ".NET")
        self.assert_actionable_failure(result)
        self.assertIn("10.0.100", result.expected)
        self.assertIn("global.json", result.details + result.expected + result.fix)
        self.assertTrue(self.commands)
        self.assertTrue(all(command == ["dotnet", "--version"] for command in self.commands if command[0] == "dotnet"))

    def test_exact_sdk_policy_rejects_later_installed_version(self) -> None:
        self.write("global.json", '{"sdk":{"version":"10.0.100","rollForward":"disable"}}')
        self.versions["dotnet"] = (0, "10.0.101")
        self.assert_actionable_failure(self.row(self.check("unit-test"), ".NET"))

    def test_failed_sdk_selection_is_not_replaced_with_installed_sdk_list(self) -> None:
        self.versions["dotnet"] = (1, "A compatible .NET SDK was not found. Installed SDK: 10.0.100")
        self.assert_actionable_failure(self.row(self.check("unit-test"), ".NET"))
        self.assertNotIn(["dotnet", "--list-sdks"], self.commands)

    def test_missing_and_malformed_global_json_have_actionable_fixes(self) -> None:
        for content in (None, "{", '{"sdk":{"version":"unknown"}}'):
            with self.subTest(content=content):
                path = self.root / "global.json"
                if content is None:
                    path.unlink()
                else:
                    path.write_text(content, encoding="utf-8")
                failures = [row for row in self.check("unit-test") if row.status == "fail"]
                self.assertTrue(failures)
                for result in failures:
                    self.assert_actionable_failure(result)

    def test_full_quality_gate_requires_pinned_python_packages(self) -> None:
        for package in ("PyYAML", "Pillow"):
            for installed in (None, "0.0.1"):
                with self.subTest(package=package, installed=installed):
                    original = self.packages.pop(package)
                    if installed is not None:
                        self.packages[package] = installed
                    result = self.row(self.check(), package)
                    self.assert_actionable_failure(result)
                    self.assertIn(original, result.expected)
                    self.assertIn("pip", result.fix)
                    self.assertIn("build/scripts/ci/requirements.txt", result.fix)
                    self.packages[package] = original

    def test_python_package_pins_come_from_existing_ci_requirements(self) -> None:
        self.write("build/scripts/docs/requirements.txt", "PyYAML==6.0.3\n")
        self.packages["PyYAML"] = "6.0.3"
        self.assertEqual(self.row(self.check(), "PyYAML").status, "pass")

    def test_browser_reports_missing_package_and_lock_files(self) -> None:
        for filename in ("package.json", "package-lock.json"):
            path = self.root / "src/Meridian.Ui/dashboard" / filename
            original = path.read_text(encoding="utf-8")
            with self.subTest(filename=filename):
                path.unlink()
                failures = [row for row in self.check("browser") if row.status == "fail" and filename in row.name + row.details]
                self.assertTrue(failures)
                for result in failures:
                    self.assert_actionable_failure(result)
            path.write_text(original, encoding="utf-8")

    def test_desktop_requires_windows_and_powershell_7(self) -> None:
        self.versions["pwsh"] = (0, "6.2.7")
        self.assert_actionable_failure(self.row(self.check("desktop"), "PowerShell"))
        self.versions["pwsh"] = (0, "7.5.2")
        with self.environment(), patch.object(prerequisites.platform, "system", return_value="Linux"):
            results = prerequisites.check_prerequisites(self.root, "desktop")
        failures = [row for row in results if row.status == "fail"]
        self.assertTrue(failures)
        self.assertTrue(any("windows" in (row.name + row.expected).lower() for row in failures))
        for result in failures:
            self.assert_actionable_failure(result)

    def test_full_quality_gate_runs_on_linux_without_desktop_host_requirement(self) -> None:
        with self.environment(), patch.object(prerequisites.platform, "system", return_value="Linux"):
            results = prerequisites.check_prerequisites(self.root, "full-quality-gate")
        self.assertTrue(all(row.status == "pass" for row in results), results)

    def test_buildctl_profiles_are_read_only_without_quick(self) -> None:
        for profile in prerequisites.PROFILE_NAMES:
            with self.subTest(profile=profile), self.environment(), ExitStack() as stack:
                stack.enter_context(patch.object(self.buildctl, "REPO_ROOT", self.root))
                for function in ("_check_solution_restore", "_check_env_vars", "_check_docker_daemon", "_check_postgres", "_run_dotnet_writer"):
                    stack.enter_context(patch.object(self.buildctl, function, side_effect=AssertionError(f"Unexpected {function}")))
                args = self.buildctl.build_parser().parse_args(["doctor", "--profile", profile])
                with redirect_stdout(io.StringIO()):
                    self.assertEqual(self.buildctl.cmd_doctor(args), 0)

    def test_buildctl_python_310_failure_cannot_be_ignored_as_warning(self) -> None:
        self.python_version = VersionInfo(3, 10, 16, "final", 0)
        with self.environment(), patch.object(self.buildctl, "REPO_ROOT", self.root):
            args = self.buildctl.build_parser().parse_args(["doctor", "--profile", "full-quality-gate", "--no-fail-on-warn"])
            output = io.StringIO()
            with redirect_stdout(output):
                self.assertEqual(self.buildctl.cmd_doctor(args), 1)
        self.assertIn("3.11", output.getvalue())
        self.assertIn("Python", output.getvalue())

    def test_diagnostics_profiles_skip_credentials_services_network_and_restore(self) -> None:
        for profile in prerequisites.PROFILE_NAMES:
            with self.subTest(profile=profile), self.environment(), ExitStack() as stack:
                for function in ("_check_config", "_check_env_vars", "_check_docker_daemon", "_check_postgres", "_check_network", "_check_ports"):
                    stack.enter_context(patch.object(doctor.Doctor, function, side_effect=AssertionError(f"Unexpected {function}")))
                results = doctor.Doctor(self.root, quick=False, profile=profile).run()
                self.assertTrue(all(row.status == "pass" for row in results), results)

    def test_diagnostics_python_310_failure_cannot_be_ignored_as_warning(self) -> None:
        self.python_version = VersionInfo(3, 10, 16, "final", 0)
        output = io.StringIO()
        with self.environment(), redirect_stdout(output):
            code = doctor.run_doctor(self.root, quick=False, json_output=True, fail_on_warn=False, profile="full-quality-gate")
        self.assertNotEqual(code, 0)
        self.assert_actionable_failure(self.row([prerequisites.CheckResult(**row) for row in json.loads(output.getvalue())], "Python"))

    def test_shell_bootstrap_checks_running_python_version_without_other_prerequisites(self) -> None:
        for minor, expected in ((10, 1), (11, 0), (12, 0)):
            with self.subTest(minor=minor), patch.object(prerequisites.sys, "version_info", VersionInfo(3, minor, 0, "final", 0)), patch.object(prerequisites, "check_prerequisites", side_effect=AssertionError("Only Python should be checked")):
                with redirect_stdout(io.StringIO()):
                    self.assertEqual(prerequisites.main(["--python-version-ok"]), expected)

    def test_windows_npm_version_probe_uses_resolved_command_path(self) -> None:
        npm_path = r"C:\Program Files\nodejs\npm.cmd"
        with patch.object(prerequisites.shutil, "which", return_value=npm_path), patch.object(prerequisites.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "11.6.0\n", "")) as run:
            self.assertEqual(prerequisites._command_version(["npm", "--version"], self.root), (0, "11.6.0"))
        self.assertEqual(run.call_args.args[0], [npm_path, "--version"])
        self.assertEqual(run.call_args.kwargs["cwd"], self.root)

    def test_ci_browser_setup_uses_same_node_version_file_as_doctor(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/meridian-ci.yml").read_text(encoding="utf-8")
        self.assertIn("node-version-file: .nvmrc", workflow)
        self.assertNotIn("node-version:", workflow)
        self.assertEqual((REPO_ROOT / ".nvmrc").read_text(encoding="utf-8").strip(), "24")


class CiPrerequisitePreflightTests(unittest.TestCase):
    """Exercise real CI/setup scripts and preflight with isolated, inert tools."""

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.bin = self.root / "test-bin"
        self.bin.mkdir()
        self.log = self.root / "tool-calls.log"
        self.bash = shutil.which("bash")
        self.assertIsNotNone(self.bash, "The CI entrypoint requires Bash")
        for relative in ("scripts/ci.sh", "scripts/ai/setup.sh", "README.md", "build/python/prerequisites.py", ".nvmrc", "global.json", "Meridian.sln", "Directory.Build.props", "Directory.Packages.props", "build/scripts/ci/requirements.txt", "build/scripts/docs/requirements.txt", "src/Meridian.Ui/dashboard/package.json", "src/Meridian.Ui/dashboard/package-lock.json"):
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(REPO_ROOT / relative, target)
        self.python_shim_source = f'''case "$1" in
  build/python/prerequisites.py|*/build/python/prerequisites.py)
    printf 'preflight %s\\n' "$*" >> "$DOCTOR_TEST_LOG"
    exec {shlex.quote(sys.executable)} {shlex.quote(str(self.root / "run-preflight.py"))} "$@"
    ;;
  --version) printf 'Python 3.11.10\\n' ;;
  *) exit 0 ;;
esac
'''
        self.shim("python3", self.python_shim_source)
        (self.root / "run-preflight.py").write_text('''import importlib.metadata
import os
import runpy
import json
import shutil
import sys
from collections import namedtuple
from pathlib import Path

if os.environ.get("DOCTOR_TEST_OLD_PYTHON"):
    version_info = namedtuple("VersionInfo", "major minor micro releaselevel serial")
    sys.version_info = version_info(3, 10, 16, "final", 0)

original_which = shutil.which
missing_tools = os.environ.get("DOCTOR_TEST_MISSING_TOOLS", "").split(",")
shutil.which = lambda command, *args, **kwargs: None if command in missing_tools else original_which(command, *args, **kwargs)
packages = json.loads(Path(__file__).with_name("package-versions.json").read_text())

def version(package):
    if package == os.environ.get("DOCTOR_TEST_MISSING_PACKAGE"):
        raise importlib.metadata.PackageNotFoundError(package)
    return packages[package]

importlib.metadata.version = version
sys.argv = sys.argv[1:]
runpy.run_path(sys.argv[0], run_name="__main__")
''', encoding="utf-8")
        (self.root / "package-versions.json").write_text(json.dumps(dict(prerequisites._requirements(self.root / "build/scripts/ci/requirements.txt"))), encoding="utf-8")
        self.shim("dotnet", '''printf 'dotnet %s\\n' "$*" >> "$DOCTOR_TEST_LOG"
case "$1" in
  --version) printf '10.0.100\\n' ;;
  --info) exit 0 ;;
  restore) exit "${DOCTOR_TEST_RESTORE_EXIT:-42}" ;;
  *) exit 0 ;;
esac
''')
        self.shim("node", '''printf 'node %s\\n' "$*" >> "$DOCTOR_TEST_LOG"
if [[ "$1" == --version ]]; then printf '%s\\n' "${DOCTOR_TEST_NODE_VERSION:-v24.9.0}"; fi
''')
        self.shim("npm", '''printf 'npm %s\\n' "$*" >> "$DOCTOR_TEST_LOG"
if [[ "$1" == --version ]]; then printf '11.6.0\\n'; elif [[ " $* " == *" ci "* ]]; then exit "${DOCTOR_TEST_NPM_EXIT:-42}"; fi
''')
        self.shim("git", '''if [[ "$1" == rev-parse ]]; then pwd; else printf 'git version 2.50.0\\n'; fi
''')
        self.shim("pwsh", "printf '7.5.2\\n'\n")
        self.shim("actionlint", "printf '1.7.9\\n'\n")
        self.shim("promtool", "exit 0\n")
        self.shim("docker-compose", "printf 'Docker Compose version v2.39.4\\n'\n")

    def shim(self, tool: str, source: str) -> None:
        target = self.bin / tool
        target.write_text(f"#!{self.bash}\n{source}", encoding="utf-8")
        target.chmod(0o755)

    def run_ci(self, lane="quality-gate", **updates):
        environment = {**os.environ, "PATH": str(self.bin) + os.pathsep + os.environ.get("PATH", ""), "DOCTOR_TEST_LOG": str(self.log)}
        environment.update(updates)
        result = subprocess.run([self.bash, "scripts/ci.sh", "--lane", lane], cwd=self.root, env=environment, text=True, capture_output=True, timeout=20)
        calls = self.log.read_text(encoding="utf-8").splitlines() if self.log.exists() else []
        return result, calls

    def run_setup(self, *flags, **updates):
        environment = {**os.environ, "PATH": str(self.bin) + os.pathsep + os.environ.get("PATH", ""), "DOCTOR_TEST_LOG": str(self.log)}
        environment.update(updates)
        args = [self.bash, "scripts/ai/setup.sh", "--strict", "--no-install-dotnet", "--skip-ai-helper", "--skip-context", "--skip-config", *flags]
        result = subprocess.run(args, cwd=self.root, env=environment, text=True, capture_output=True, timeout=20)
        calls = self.log.read_text(encoding="utf-8").splitlines() if self.log.exists() else []
        return result, calls

    def assert_no_restore_or_install(self, calls):
        self.assertFalse(any(line.startswith("dotnet restore") or (line.startswith("npm ") and " ci " in f" {line} ") for line in calls), calls)

    def test_full_gate_missing_python_package_fails_before_restore(self) -> None:
        result, calls = self.run_ci(DOCTOR_TEST_MISSING_PACKAGE="Pillow")
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Pillow", result.stdout + result.stderr)
        self.assertIn("pip", result.stdout + result.stderr)
        self.assertIn("build/scripts/ci/requirements.txt", result.stdout + result.stderr)
        self.assert_no_restore_or_install(calls)
        self.assertTrue(any(line.startswith("preflight ") for line in calls), calls)

    def test_full_gate_unsupported_node_fails_before_restore(self) -> None:
        result, calls = self.run_ci(DOCTOR_TEST_NODE_VERSION="v22.15.0")
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("24", result.stdout + result.stderr)
        self.assert_no_restore_or_install(calls)

    def test_browser_unsupported_node_fails_before_npm_ci(self) -> None:
        result, calls = self.run_ci("verify-browser", DOCTOR_TEST_NODE_VERSION="v20.19.0")
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("24", result.stdout + result.stderr)
        self.assert_no_restore_or_install(calls)

    def test_ci_selects_compatible_python_after_older_python3(self) -> None:
        self.shim("python3", '''if [[ "$*" == *"--python-version-ok"* ]]; then exit 1; fi
printf 'The unsupported Python interpreter was selected.\\n' >&2
exit 99
''')
        self.shim("python", "printf 'compatible python %s\\n' \"$*\" >> \"$DOCTOR_TEST_LOG\"\n" + self.python_shim_source)
        result, calls = self.run_ci("verify-browser")
        self.assertEqual(result.returncode, 42, result.stdout + result.stderr)
        self.assertTrue(any(line.startswith("compatible python ") and "--lane verify-browser" in line for line in calls), calls)

    def test_full_gate_python_310_fails_before_restore(self) -> None:
        self.shim("python", self.python_shim_source)
        result, calls = self.run_ci(DOCTOR_TEST_OLD_PYTHON="1")
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Python", result.stdout + result.stderr)
        self.assertIn("3.11", result.stdout + result.stderr)
        self.assert_no_restore_or_install(calls)

    def test_passing_preflight_precedes_first_restore_or_install(self) -> None:
        for lane, operation in (("quality-gate", "dotnet restore"), ("verify-browser", "npm --prefix")):
            with self.subTest(lane=lane):
                self.log.unlink(missing_ok=True)
                result, calls = self.run_ci(lane)
                self.assertEqual(result.returncode, 42, result.stdout + result.stderr)
                preflight = next(index for index, line in enumerate(calls) if line.startswith("preflight "))
                write = next(index for index, line in enumerate(calls) if line.startswith(operation))
                self.assertLess(preflight, write, calls)

    def test_setup_skip_node_does_not_require_node_or_python_extras(self) -> None:
        result, calls = self.run_setup("--skip-node", DOCTOR_TEST_MISSING_TOOLS="node,npm", DOCTOR_TEST_MISSING_PACKAGE="Pillow", DOCTOR_TEST_RESTORE_EXIT="0")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any("--lane verify-dotnet --skip-python-packages" in line for line in calls), calls)
        self.assertFalse(any(line.startswith("npm ") and " ci " in f" {line} " for line in calls), calls)
        self.assertTrue(any(line.startswith("dotnet restore") for line in calls), calls)

    def test_setup_skip_restore_does_not_require_dotnet(self) -> None:
        self.shim("dotnet", "exit 127\n")
        result, calls = self.run_setup("--skip-restore", DOCTOR_TEST_MISSING_TOOLS="dotnet")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any("--skip-dotnet" in line and "--lane verify-fast" in line for line in calls), calls)
        self.assertFalse(any(line.startswith("dotnet restore") for line in calls), calls)

    def test_setup_failed_node_preflight_precedes_npm_or_restore(self) -> None:
        (self.root / "package.json").write_text("{}", encoding="utf-8")
        (self.root / "package-lock.json").write_text("{}", encoding="utf-8")
        result, calls = self.run_setup(DOCTOR_TEST_NODE_VERSION="v22.15.0")
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("24", result.stdout + result.stderr)
        self.assert_no_restore_or_install(calls)
        self.assertTrue(any("--lane verify-fast --skip-python-packages" in line for line in calls), calls)

    def test_setup_failed_python_preflight_precedes_npm_or_restore(self) -> None:
        self.shim("python", self.python_shim_source)
        result, calls = self.run_setup(DOCTOR_TEST_OLD_PYTHON="1")
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("3.11", result.stdout + result.stderr)
        self.assert_no_restore_or_install(calls)


if __name__ == "__main__":
    unittest.main()
