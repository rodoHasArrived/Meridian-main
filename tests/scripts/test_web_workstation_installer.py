from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "install" / "install-web-workstation.ps1"


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
            self.assertIn('"DataRoot": "data"', (app_data_root / "appsettings.json").read_text())
            launcher = (install_root / "Launch-MeridianWebWorkstation.ps1").read_text()
            self.assertIn("--mode", launcher)
            self.assertIn("desktop", launcher)
            self.assertIn("http://localhost:$Port/workstation/", launcher)

    def test_script_contains_host_asset_shortcut_and_launcher_contracts(self) -> None:
        script = SCRIPT_PATH.read_text(encoding="utf-8")

        self.assertIn("src\\Meridian.Ui\\wwwroot\\workstation", script)
        self.assertIn("wwwroot\\workstation", script)
        self.assertIn("--mode", script)
        self.assertIn("desktop", script)
        self.assertIn("--config", script)
        self.assertIn("Meridian Web Workstation.lnk", script)
        self.assertIn("Launch-MeridianWebWorkstation.ps1", script)
        self.assertNotIn("MDC_CONFIG_PATH", script)

    @staticmethod
    def _write_file(path: Path, content: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
