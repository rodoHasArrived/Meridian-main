from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import time
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "install" / "install-web-workstation.ps1"
ROOT_INSTALL_SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "install" / "install.ps1"
SMOKE_SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "install" / "smoke-web-workstation-install.ps1"


@unittest.skipIf(os.name != "nt", "PowerShell installer behavior is validated on Windows")
class WebWorkstationInstallerScriptTests(unittest.TestCase):
    def test_script_parses_and_plan_only_reports_install_contract(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        parse = subprocess.run(
            [
                powershell,
                "-NoProfile",
                "-Command",
                (
                    "$tokens=$null; $errors=$null; "
                    f"[System.Management.Automation.Language.Parser]::ParseFile('{SCRIPT_PATH}', [ref]$tokens, [ref]$errors) | Out-Null; "
                    "if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Error $_.Message }; exit 1 }"
                ),
            ],
            capture_output=True,
            text=True,
        )

        self.assertEqual(parse.returncode, 0, parse.stderr)

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            script_copy = repo_root / "build" / "scripts" / "install" / "install-web-workstation.ps1"
            script_copy.parent.mkdir(parents=True)
            shutil.copy2(SCRIPT_PATH, script_copy)

            self._write_file(repo_root / "src" / "Meridian" / "Meridian.csproj", "<Project />")
            self._write_file(repo_root / "src" / "Meridian.Ui" / "dashboard" / "package.json", "{}")
            desktop_root = repo_root / "desktop"
            start_menu_root = repo_root / "start-menu"

            command = [powershell, "-NoProfile"]
            if Path(powershell).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-File",
                    str(script_copy),
                    "-PlanOnly",
                    "-InstallRoot",
                    str(repo_root / "installed-app"),
                    "-AppDataRoot",
                    str(repo_root / "appdata"),
                    "-DesktopShortcutRoot",
                    str(desktop_root),
                    "-StartMenuShortcutDirectory",
                    str(start_menu_root),
                    "-Port",
                    "8099",
                ]
            )

            result = subprocess.run(command, cwd=repo_root, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("Meridian Web Workstation install plan", result.stdout)
            self.assertIn("http://localhost:8099/workstation/", result.stdout)
            self.assertIn("Backup root:", result.stdout)
            self.assertIn("Install report:", result.stdout)
            self.assertIn("PlanOnly was specified", result.stdout)
            self.assertFalse((repo_root / "installed-app").exists())

    def test_skip_build_install_creates_local_app_layout_without_shortcuts(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            script_copy = repo_root / "build" / "scripts" / "install" / "install-web-workstation.ps1"
            script_copy.parent.mkdir(parents=True)
            shutil.copy2(SCRIPT_PATH, script_copy)

            self._write_file(repo_root / "src" / "Meridian" / "Meridian.csproj", "<Project />")
            self._write_file(repo_root / "src" / "Meridian.Ui" / "dashboard" / "package.json", "{}")
            self._write_file(
                repo_root / "src" / "Meridian.Ui" / "wwwroot" / "workstation" / "index.html",
                "<!doctype html><title>Meridian</title>",
            )
            self._write_file(
                repo_root
                / "artifacts"
                / "publish"
                / "web-workstation"
                / "win-x64"
                / "Meridian.exe",
                "fake executable",
            )

            install_root = repo_root / "installed-app"
            app_data_root = repo_root / "appdata"
            desktop_root = repo_root / "desktop"
            start_menu_root = repo_root / "start-menu"
            command = [powershell, "-NoProfile"]
            if Path(powershell).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-File",
                    str(script_copy),
                    "-SkipNpmInstall",
                    "-SkipDashboardBuild",
                    "-SkipHostPublish",
                    "-NoDesktopShortcut",
                    "-NoStartMenuShortcut",
                    "-InstallRoot",
                    str(install_root),
                    "-AppDataRoot",
                    str(app_data_root),
                    "-DesktopShortcutRoot",
                    str(desktop_root),
                    "-StartMenuShortcutDirectory",
                    str(start_menu_root),
                    "-Port",
                    "8099",
                ]
            )

            result = subprocess.run(command, cwd=repo_root, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue((install_root / "Meridian.exe").exists())
            self.assertTrue((install_root / "wwwroot" / "workstation" / "index.html").exists())
            self.assertTrue((install_root / "Launch-MeridianWebWorkstation.ps1").exists())
            self.assertTrue((app_data_root / "data" / "workstation" / "evidence").is_dir())
            self.assertTrue((app_data_root / "backups").is_dir())
            self.assertTrue((app_data_root / "service").is_dir())
            self.assertFalse((install_root / "data").exists())
            self.assertFalse((install_root / "artifacts").exists())
            appsettings = (app_data_root / "appsettings.json").read_text()
            self.assertIn('"DataRoot": "data"', appsettings)
            self.assertIn('"DataSource": "Synthetic"', appsettings)
            self.assertIn('"Synthetic"', appsettings)
            install_manifest = (app_data_root / "service" / "web-workstation-install-manifest.json").read_text()
            self.assertIn('"hostCopySkipped": false', install_manifest)
            self.assertIn('"bundleCopySkipped": false', install_manifest)
            launcher = (install_root / "Launch-MeridianWebWorkstation.ps1").read_text()
            self.assertIn("--mode", launcher)
            self.assertIn("workstation", launcher)
            self.assertIn("http://localhost:$Port/workstation/", launcher)
            self.assertIn("web-workstation-runtime.json", launcher)
            self.assertIn("web-workstation-install-manifest.json", result.stdout)
            self.assertIn("X-Meridian-Shutdown-Token", launcher)
            self.assertIn("[System.Diagnostics.ProcessStartInfo]::new()", launcher)
            self.assertIn("ArgumentList.Add($argument)", launcher)
            self.assertIn("[switch]$Stop", launcher)
            self.assertIn('"--config", $configPath', launcher)

    def test_skip_build_install_quotes_special_config_path_in_launcher_arguments(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            script_copy = repo_root / "build" / "scripts" / "install" / "install-web-workstation.ps1"
            script_copy.parent.mkdir(parents=True)
            shutil.copy2(SCRIPT_PATH, script_copy)

            self._write_file(repo_root / "src" / "Meridian" / "Meridian.csproj", "<Project />")
            self._write_file(repo_root / "src" / "Meridian.Ui" / "dashboard" / "package.json", "{}")
            self._write_file(
                repo_root / "src" / "Meridian.Ui" / "wwwroot" / "workstation" / "index.html",
                "<!doctype html><title>Meridian</title>",
            )
            self._write_file(
                repo_root
                / "artifacts"
                / "publish"
                / "web-workstation"
                / "win-x64"
                / "Meridian.exe",
                "fake executable",
            )

            install_root = repo_root / "installed app & shell"
            app_data_root = repo_root / "app data's $weird & [case]"
            command = [powershell, "-NoProfile"]
            if Path(powershell).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-File",
                    str(script_copy),
                    "-SkipNpmInstall",
                    "-SkipDashboardBuild",
                    "-SkipHostPublish",
                    "-NoDesktopShortcut",
                    "-NoStartMenuShortcut",
                    "-InstallRoot",
                    str(install_root),
                    "-AppDataRoot",
                    str(app_data_root),
                    "-Port",
                    "8097",
                ]
            )

            result = subprocess.run(command, cwd=repo_root, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            launcher_path = install_root / "Launch-MeridianWebWorkstation.ps1"
            launcher = launcher_path.read_text(encoding="utf-8")
            config_path = app_data_root / "appsettings.json"
            expected_literal = "'" + str(config_path).replace("'", "''") + "'"
            self.assertIn(f"$configPath = {expected_literal}", launcher)
            self.assertIn(
                'foreach ($argument in @("--mode", "workstation", "--http-port", [string]$Port, "--config", $configPath))',
                launcher,
            )
            self.assertIn("[void]$startInfo.ArgumentList.Add($argument)", launcher)
            self.assertNotIn('$arguments = "--mode workstation', launcher)
            self.assertNotIn('--config `"$configPath`"', launcher)

            install_manifest = json.loads(
                (app_data_root / "service" / "web-workstation-install-manifest.json").read_text(encoding="utf-8")
            )
            self.assertEqual(install_manifest["configPath"], str(config_path))

            parse_launcher = subprocess.run(
                [
                    powershell,
                    "-NoProfile",
                    "-Command",
                    (
                        "$tokens=$null; $errors=$null; "
                        "[System.Management.Automation.Language.Parser]::ParseFile("
                        f"'{self._ps_literal(str(launcher_path))}', [ref]$tokens, [ref]$errors) | Out-Null; "
                        "if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Error $_.Message }; exit 1 }"
                    ),
                ],
                capture_output=True,
                text=True,
            )
            self.assertEqual(parse_launcher.returncode, 0, parse_launcher.stderr)

    def test_skip_build_install_archives_legacy_install_and_matching_app_data(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            script_copy = repo_root / "build" / "scripts" / "install" / "install-web-workstation.ps1"
            script_copy.parent.mkdir(parents=True)
            shutil.copy2(SCRIPT_PATH, script_copy)

            self._write_file(repo_root / "src" / "Meridian" / "Meridian.csproj", "<Project />")
            self._write_file(repo_root / "src" / "Meridian.Ui" / "dashboard" / "package.json", "{}")
            self._write_file(
                repo_root / "src" / "Meridian.Ui" / "wwwroot" / "workstation" / "index.html",
                "<!doctype html><title>Meridian</title>",
            )
            self._write_file(
                repo_root
                / "artifacts"
                / "publish"
                / "web-workstation"
                / "win-x64"
                / "Meridian.exe",
                "fake executable",
            )

            install_root = repo_root / "Meridian Web Workstation"
            legacy_install_root = repo_root / "Meridian Web Workstation Test"
            app_data_root = repo_root / "Meridian"
            legacy_app_data_root = repo_root / "Meridian-Test"
            desktop_root = repo_root / "desktop"
            start_menu_root = repo_root / "start-menu"

            self._write_file(legacy_install_root / "Meridian.exe", "legacy executable")
            self._write_file(
                legacy_install_root / "Launch-MeridianWebWorkstation.ps1",
                "Write-Host 'legacy launcher'",
            )
            self._write_file(
                app_data_root / "service" / "web-workstation-runtime.json",
                json.dumps(
                    {
                        "processId": 999999,
                        "port": 8080,
                        "exePath": str(install_root / "Meridian.exe"),
                        "installRoot": str(install_root),
                        "configPath": str(app_data_root / "appsettings.json"),
                    }
                ),
            )
            self._write_file(
                app_data_root / "appsettings.json",
                json.dumps(
                    {
                        "DataRoot": "data",
                        "DataSource": "IB",
                        "ib": None,
                        "dataSources": {"sources": None},
                    }
                ),
            )
            self._write_file(legacy_app_data_root / "appsettings.json", "{\"DataRoot\":\"data\"}")
            self._write_file(legacy_app_data_root / "data" / "seed.txt", "legacy data")
            self._write_file(
                legacy_app_data_root / "service" / "web-workstation-install-manifest.json",
                json.dumps(
                    {
                        "installRoot": str(legacy_install_root),
                        "appDataRoot": str(legacy_app_data_root),
                    }
                ),
            )

            self._create_shortcut(
                powershell,
                desktop_root / "Meridian Web Workstation Test.lnk",
                legacy_install_root / "Launch-MeridianWebWorkstation.ps1",
                legacy_install_root,
            )
            self._create_shortcut(
                powershell,
                start_menu_root / "Meridian Web Workstation Test.lnk",
                legacy_install_root / "Launch-MeridianWebWorkstation.ps1",
                legacy_install_root,
            )
            self._create_shortcut(
                powershell,
                start_menu_root / "Stop Meridian Web Workstation Test.lnk",
                legacy_install_root / "Launch-MeridianWebWorkstation.ps1",
                legacy_install_root,
                stop=True,
            )

            command = [powershell, "-NoProfile"]
            if Path(powershell).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-File",
                    str(script_copy),
                    "-SkipNpmInstall",
                    "-SkipDashboardBuild",
                    "-SkipHostPublish",
                    "-InstallRoot",
                    str(install_root),
                    "-AppDataRoot",
                    str(app_data_root),
                    "-DesktopShortcutRoot",
                    str(desktop_root),
                    "-StartMenuShortcutDirectory",
                    str(start_menu_root),
                    "-Port",
                    "8099",
                ]
            )

            result = subprocess.run(command, cwd=repo_root, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue((install_root / "Meridian.exe").exists())
            self.assertFalse((app_data_root / "service" / "web-workstation-runtime.json").exists())
            self.assertFalse(legacy_install_root.exists())
            self.assertFalse(legacy_app_data_root.exists())
            self.assertTrue(self._wait_for_path_state(desktop_root / "Meridian Web Workstation Test.lnk", exists=False))
            self.assertTrue(self._wait_for_path_state(start_menu_root / "Meridian Web Workstation Test.lnk", exists=False))
            self.assertTrue(self._wait_for_path_state(start_menu_root / "Stop Meridian Web Workstation Test.lnk", exists=False))
            self.assertTrue(self._wait_for_path_state(desktop_root / "Meridian Web Workstation.lnk", exists=True))
            self.assertTrue(self._wait_for_path_state(start_menu_root / "Meridian Web Workstation.lnk", exists=True))
            self.assertTrue(self._wait_for_path_state(start_menu_root / "Stop Meridian Web Workstation.lnk", exists=True))

            install_manifest = json.loads(
                (app_data_root / "service" / "web-workstation-install-manifest.json").read_text(encoding="utf-8")
            )
            archived_installs = install_manifest["actions"]["archivedLegacyInstallRoots"]
            archived_app_data = install_manifest["actions"]["archivedLegacyAppDataRoots"]
            removed_shortcuts = install_manifest["actions"]["legacyShortcutsRemoved"]
            config_repair = install_manifest["actions"]["configRepair"]
            runtime_cleanup = install_manifest["actions"]["runtimeStateCleanup"]
            self.assertEqual(len(archived_installs), 1)
            self.assertEqual(len(archived_app_data), 1)
            self.assertEqual(len(removed_shortcuts), 3)
            self.assertTrue(config_repair["changed"])
            self.assertEqual("IB", config_repair["originalDataSource"])
            self.assertEqual("Synthetic", config_repair["repairedDataSource"])
            self.assertTrue(runtime_cleanup["removed"])
            self.assertTrue(Path(archived_installs[0]["destinationPath"]).exists())
            self.assertTrue(Path(archived_app_data[0]["destinationPath"]).exists())
            repaired_config = json.loads((app_data_root / "appsettings.json").read_text(encoding="utf-8"))
            repaired_data_source = repaired_config.get("dataSource") or repaired_config.get("DataSource")
            synthetic_config = repaired_config.get("synthetic") or repaired_config.get("Synthetic")
            self.assertEqual("Synthetic", repaired_data_source)
            self.assertTrue(synthetic_config["enabled"])
            self.assertEqual(
                install_manifest["discovery"]["legacyAppDataCandidates"][0]["classification"],
                "old-app-data",
            )
            self.assertIn("Archived installs: 1", result.stdout)
            self.assertIn("Repair invalid workstation configuration if needed", result.stdout)
            self.assertIn("Clear stale Meridian runtime state", result.stdout)

    def test_script_contains_host_asset_shortcut_and_launcher_contracts(self) -> None:
        script = SCRIPT_PATH.read_text(encoding="utf-8")

        self.assertIn("src\\Meridian.Ui\\wwwroot\\workstation", script)
        self.assertIn("wwwroot\\workstation", script)
        self.assertIn("--mode", script)
        self.assertIn("workstation", script)
        self.assertIn("--config", script)
        self.assertIn("Meridian Web Workstation.lnk", script)
        self.assertIn("Stop Meridian Web Workstation.lnk", script)
        self.assertIn("Launch-MeridianWebWorkstation.ps1", script)
        self.assertIn("web-workstation-runtime.json", script)
        self.assertIn("web-workstation-install-manifest.json", script)
        self.assertIn("archive\\old-installs", script)
        self.assertIn("Back up Meridian app data", script)
        self.assertIn("Repair-InvalidWorkstationConfig", script)
        self.assertIn("configRepair", script)
        self.assertIn("Clear stale Meridian runtime state", script)
        self.assertIn("runtimeStateCleanup", script)
        self.assertIn("SkipLegacyInstallCleanup", script)
        self.assertIn("X-Meridian-Shutdown-Token", script)
        self.assertIn("ProcessStartInfo", script)
        self.assertIn("ArgumentList.Add", script)
        self.assertNotIn("MDC_CONFIG_PATH", script)

    def test_root_install_script_exposes_web_workstation_mode(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        root_script = ROOT_INSTALL_SCRIPT_PATH.read_text(encoding="utf-8")
        self.assertIn('"WebWorkstation"', root_script)
        self.assertIn("Install-WebWorkstation", root_script)
        self.assertIn("install-web-workstation.ps1", root_script)
        self.assertIn("SkipLegacyInstallCleanup", root_script)
        self.assertIn("$global:LASTEXITCODE = 0", root_script)
        self.assertIn("$webSucceeded = $?", root_script)
        self.assertIn("$webExitCode -ne 0", root_script)

        with tempfile.TemporaryDirectory() as temp_dir:
            repo_root = Path(temp_dir)
            install_dir = repo_root / "build" / "scripts" / "install"
            install_dir.mkdir(parents=True)
            shutil.copy2(ROOT_INSTALL_SCRIPT_PATH, install_dir / "install.ps1")
            shutil.copy2(SCRIPT_PATH, install_dir / "install-web-workstation.ps1")

            self._write_file(repo_root / "src" / "Meridian" / "Meridian.csproj", "<Project />")
            self._write_file(repo_root / "src" / "Meridian.Ui" / "dashboard" / "package.json", "{}")

            command = [powershell, "-NoProfile"]
            if Path(powershell).name.lower().startswith("powershell"):
                command.extend(["-ExecutionPolicy", "Bypass"])
            command.extend(
                [
                    "-File",
                    str(install_dir / "install.ps1"),
                    "-Mode",
                    "WebWorkstation",
                    "-PlanOnly",
                    "-WebInstallRoot",
                    str(repo_root / "installed-app"),
                    "-WebAppDataRoot",
                    str(repo_root / "appdata"),
                    "-WebPort",
                    "8098",
                ]
            )

            result = subprocess.run(command, cwd=repo_root, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("Meridian Web Workstation install plan", result.stdout)
            self.assertIn("http://localhost:8098/workstation/", result.stdout)
            self.assertFalse((repo_root / "installed-app").exists())

    def test_web_workstation_install_smoke_script_parses_and_probes_installed_copy(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        parse = subprocess.run(
            [
                powershell,
                "-NoProfile",
                "-Command",
                (
                    "$tokens=$null; $errors=$null; "
                    f"[System.Management.Automation.Language.Parser]::ParseFile('{SMOKE_SCRIPT_PATH}', [ref]$tokens, [ref]$errors) | Out-Null; "
                    "if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Error $_.Message }; exit 1 }"
                ),
            ],
            capture_output=True,
            text=True,
        )

        self.assertEqual(parse.returncode, 0, parse.stderr)

        smoke_script = SMOKE_SCRIPT_PATH.read_text(encoding="utf-8")
        self.assertIn("install.ps1", smoke_script)
        self.assertIn('"WebWorkstation"', smoke_script)
        self.assertIn("-WebInstallRoot", smoke_script)
        self.assertIn("-WebAppDataRoot", smoke_script)
        self.assertIn("-NoDesktopShortcut", smoke_script)
        self.assertIn("-NoStartMenuShortcut", smoke_script)
        self.assertIn("Meridian.exe", smoke_script)
        self.assertIn("--mode workstation --http-port", smoke_script)
        self.assertIn("MDC_AUTH_MODE = \"optional\"", smoke_script)
        self.assertIn("MDC_USERS = $null", smoke_script)
        self.assertIn("-MaximumRedirection 0", smoke_script)
        self.assertIn("/healthz", smoke_script)
        self.assertIn("/api/system/shutdown", smoke_script)
        self.assertIn("X-Meridian-Shutdown-Token", smoke_script)
        self.assertIn("/workstation/", smoke_script)
        self.assertIn("first workstation asset", smoke_script)
        self.assertIn("Invoke-WebRequest", smoke_script)
        self.assertIn("host.stdout.log", smoke_script)
        self.assertIn("host.stderr.log", smoke_script)

    @staticmethod
    def _write_file(path: Path, content: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    @staticmethod
    def _create_shortcut(
        powershell: str,
        shortcut_path: Path,
        launcher_path: Path,
        working_directory: Path,
        *,
        stop: bool = False,
    ) -> None:
        shortcut_path.parent.mkdir(parents=True, exist_ok=True)
        arguments = f'-NoProfile -ExecutionPolicy Bypass -File "{launcher_path}"'
        if stop:
            arguments += " -Stop"

        command = [powershell, "-NoProfile"]
        if Path(powershell).name.lower().startswith("powershell"):
            command.extend(["-ExecutionPolicy", "Bypass"])
        command.extend(
            [
                "-Command",
                (
                    "$shell=New-Object -ComObject WScript.Shell; "
                    f"$shortcut=$shell.CreateShortcut('{WebWorkstationInstallerScriptTests._ps_literal(str(shortcut_path))}'); "
                    f"$shortcut.TargetPath='{WebWorkstationInstallerScriptTests._ps_literal(powershell)}'; "
                    f"$shortcut.Arguments='{WebWorkstationInstallerScriptTests._ps_literal(arguments)}'; "
                    f"$shortcut.WorkingDirectory='{WebWorkstationInstallerScriptTests._ps_literal(str(working_directory))}'; "
                    "$shortcut.Save()"
                ),
            ]
        )

        result = subprocess.run(command, capture_output=True, text=True)
        if result.returncode != 0:
            raise AssertionError(result.stderr or result.stdout)

    @staticmethod
    def _wait_for_path_state(path: Path, *, exists: bool, timeout_seconds: float = 5.0) -> bool:
        deadline = time.time() + timeout_seconds
        while time.time() < deadline:
            if path.exists() == exists:
                return True
            time.sleep(0.1)

        return path.exists() == exists

    @staticmethod
    def _ps_literal(value: str) -> str:
        return value.replace("'", "''")


if __name__ == "__main__":
    unittest.main()
