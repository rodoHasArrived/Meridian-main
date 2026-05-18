from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
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
                    "-Port",
                    "8099",
                ]
            )

            result = subprocess.run(command, cwd=repo_root, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("Meridian Web Workstation install plan", result.stdout)
            self.assertIn("http://localhost:8099/workstation/", result.stdout)
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
                / "web-workstation-installer"
                / "win-x64"
                / "Meridian.exe",
                "fake executable",
            )

            install_root = repo_root / "installed-app"
            app_data_root = repo_root / "appdata"
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
                    "8099",
                ]
            )

            result = subprocess.run(command, cwd=repo_root, capture_output=True, text=True)

            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertTrue((install_root / "Meridian.exe").exists())
            self.assertTrue((install_root / "wwwroot" / "workstation" / "index.html").exists())
            self.assertTrue((install_root / "Launch-MeridianWebWorkstation.ps1").exists())
            self.assertTrue((install_root / "data" / "execution" / "sessions").is_dir())
            self.assertTrue((app_data_root / "data" / "workstation" / "evidence").is_dir())
            appsettings = (app_data_root / "appsettings.json").read_text()
            self.assertIn('"DataRoot": "data"', appsettings)
            self.assertIn('"DataSource": "Synthetic"', appsettings)
            self.assertIn('"Synthetic"', appsettings)
            launcher = (install_root / "Launch-MeridianWebWorkstation.ps1").read_text()
            self.assertIn("--mode", launcher)
            self.assertIn("desktop", launcher)
            self.assertIn("http://localhost:$Port/workstation/", launcher)
            self.assertIn("web-workstation-runtime.json", launcher)
            self.assertIn("X-Meridian-Shutdown-Token", launcher)
            self.assertIn("-PassThru", launcher)
            self.assertIn("[switch]$Stop", launcher)
            self.assertIn('--config `"$configPath`"', launcher)

    def test_script_contains_host_asset_shortcut_and_launcher_contracts(self) -> None:
        script = SCRIPT_PATH.read_text(encoding="utf-8")

        self.assertIn("src\\Meridian.Ui\\wwwroot\\workstation", script)
        self.assertIn("wwwroot\\workstation", script)
        self.assertIn("--mode", script)
        self.assertIn("desktop", script)
        self.assertIn("--config", script)
        self.assertIn("Meridian Web Workstation.lnk", script)
        self.assertIn("Stop Meridian Web Workstation.lnk", script)
        self.assertIn("Launch-MeridianWebWorkstation.ps1", script)
        self.assertIn("web-workstation-runtime.json", script)
        self.assertIn("X-Meridian-Shutdown-Token", script)
        self.assertIn("-PassThru", script)
        self.assertNotIn("MDC_CONFIG_PATH", script)

    def test_root_install_script_exposes_web_workstation_mode(self) -> None:
        powershell = shutil.which("pwsh") or shutil.which("powershell")
        if powershell is None:
            self.skipTest("PowerShell is not available")

        root_script = ROOT_INSTALL_SCRIPT_PATH.read_text(encoding="utf-8")
        self.assertIn('"WebWorkstation"', root_script)
        self.assertIn("Install-WebWorkstation", root_script)
        self.assertIn("install-web-workstation.ps1", root_script)
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
        self.assertIn("--mode desktop --http-port", smoke_script)
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


if __name__ == "__main__":
    unittest.main()
