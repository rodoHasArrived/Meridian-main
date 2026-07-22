import os
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGER_PATH = REPO_ROOT / "build" / "scripts" / "install" / "package-desktop-msix.ps1"
INSTALLER_PATH = REPO_ROOT / "build" / "scripts" / "install" / "install.ps1"
MANIFEST_PATH = REPO_ROOT / "src" / "Meridian.Wpf" / "Package.appxmanifest"
FOUNDATION_NAMESPACE = "http://schemas.microsoft.com/appx/manifest/foundation/windows10"


class DesktopMsixPackagingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.packager = PACKAGER_PATH.read_text(encoding="utf-8")
        cls.installer = INSTALLER_PATH.read_text(encoding="utf-8")
        cls.pwsh = shutil.which("pwsh")

    def test_installer_publishes_unpackaged_layout_then_calls_packager(self) -> None:
        self.assertIn('"--output", $publishOutputPath', self.installer)
        self.assertIn('"-p:WindowsPackageType=None"', self.installer)
        self.assertIn('Join-Path $ScriptDir "package-desktop-msix.ps1"', self.installer)
        self.assertIn('"Meridian.Desktop-$runtimeId.msix"', self.installer)
        self.assertNotIn("GenerateTemporaryStoreCertificate", self.installer)
        self.assertNotIn("AppxPackageDir", self.installer)

    def test_packager_uses_certificate_subject_and_never_invents_a_certificate(self) -> None:
        self.assertIn("Get-PfxData", self.packager)
        self.assertIn('$identity.SetAttribute("Publisher", $signingCertificate.Subject)', self.packager)
        self.assertIn('Resolve-WindowsSdkTool -ToolName "signtool.exe"', self.packager)
        self.assertNotIn("New-SelfSignedCertificate", self.packager)
        self.assertIn("created unsigned MSIX", self.packager)

    def test_all_external_failures_and_missing_outputs_are_terminating(self) -> None:
        self.assertIn("MakeAppx failed with exit code", self.packager)
        self.assertIn("MakeAppx reported success but did not create", self.packager)
        self.assertIn("SignTool failed with exit code", self.packager)
        self.assertIn("has no AppxSignature.p7x entry", self.packager)
        self.assertIn("Desktop publish failed with exit code", self.installer)
        self.assertIn("MSIX package not found in expected output directory", self.installer)
        self.assertIn('if (-not $NoTrustCert) {', self.installer)
        self.assertIn("The MSIX signing certificate could not be trusted", self.installer)
        self.assertIn("MSIX package installation failed", self.installer)
        self.assertNotIn('-not $NoTrustCert -and [string]::IsNullOrWhiteSpace($certPfxPath)', self.installer)
        self.assertNotIn("MDC_APPINSTALLER_URI      URI", self.installer)
        self.assertNotIn("Expected MSIX/MSIXBundle", self.installer)

    @unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"), "requires PowerShell on Windows")
    def test_unsigned_packaging_materializes_manifest_and_assets(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            publish = root / "publish"
            (publish / "Assets" / "Brand").mkdir(parents=True)
            (publish / "Meridian.Desktop.exe").write_bytes(b"desktop")
            (publish / "Assets" / "app.ico").write_bytes(b"icon")
            (publish / "Assets" / "Brand" / "meridian-tile-256.png").write_bytes(b"png")

            fake_makeappx = root / "makeappx.cmd"
            fake_makeappx.write_text(
                """@echo off
setlocal
set "stage="
set "package="
:parse
if "%~1"=="" goto validate
if /I "%~1"=="/d" (
  set "stage=%~2"
  shift
  shift
  goto parse
)
if /I "%~1"=="/p" (
  set "package=%~2"
  shift
  shift
  goto parse
)
shift
goto parse
:validate
if not exist "%stage%\\Meridian.Desktop.exe" exit /b 4
if not exist "%stage%\\Assets\\app.ico" exit /b 5
if not exist "%stage%\\Assets\\Brand\\meridian-tile-256.png" exit /b 6
copy /Y "%stage%\\AppxManifest.xml" "%MDC_TEST_CAPTURE_MANIFEST%" >nul
type nul > "%package%"
exit /b 0
""",
                encoding="utf-8",
            )

            captured_manifest = root / "captured-AppxManifest.xml"
            package = root / "out" / "Meridian.Desktop-win-arm64.msix"
            environment = os.environ.copy()
            environment["MDC_TEST_CAPTURE_MANIFEST"] = str(captured_manifest)
            result = subprocess.run(
                [
                    self.pwsh,
                    "-NoProfile",
                    "-File",
                    str(PACKAGER_PATH),
                    "-PublishDirectory",
                    str(publish),
                    "-ManifestPath",
                    str(MANIFEST_PATH),
                    "-OutputPath",
                    str(package),
                    "-Architecture",
                    "arm64",
                    "-MakeAppxPath",
                    str(fake_makeappx),
                ],
                cwd=REPO_ROOT,
                env=environment,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )

            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertTrue(package.is_file())
            self.assertTrue(captured_manifest.is_file())
            self.assertIn("created unsigned MSIX", result.stdout + result.stderr)

            manifest = ET.parse(captured_manifest)
            identity = manifest.find(f"{{{FOUNDATION_NAMESPACE}}}Identity")
            application = manifest.find(
                f"{{{FOUNDATION_NAMESPACE}}}Applications/{{{FOUNDATION_NAMESPACE}}}Application"
            )
            self.assertIsNotNone(identity)
            self.assertIsNotNone(application)
            self.assertEqual("arm64", identity.attrib["ProcessorArchitecture"])
            self.assertEqual("Meridian.Desktop.exe", application.attrib["Executable"])
            self.assertNotIn("$targetnametoken$", captured_manifest.read_text(encoding="utf-8"))

    @unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"), "requires PowerShell on Windows")
    def test_makeappx_success_without_package_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            publish = root / "publish"
            (publish / "Assets" / "Brand").mkdir(parents=True)
            (publish / "Meridian.Desktop.exe").write_bytes(b"desktop")
            (publish / "Assets" / "app.ico").write_bytes(b"icon")
            (publish / "Assets" / "Brand" / "meridian-tile-256.png").write_bytes(b"png")
            fake_makeappx = root / "makeappx.cmd"
            fake_makeappx.write_text("@echo off\nexit /b 0\n", encoding="utf-8")
            package = root / "out" / "Meridian.Desktop-win-x64.msix"

            result = subprocess.run(
                [
                    self.pwsh,
                    "-NoProfile",
                    "-File",
                    str(PACKAGER_PATH),
                    "-PublishDirectory",
                    str(publish),
                    "-ManifestPath",
                    str(MANIFEST_PATH),
                    "-OutputPath",
                    str(package),
                    "-Architecture",
                    "x64",
                    "-MakeAppxPath",
                    str(fake_makeappx),
                ],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )

            self.assertNotEqual(0, result.returncode)
            self.assertIn(
                "MakeAppx reported success but did not create",
                result.stdout + result.stderr,
            )


if __name__ == "__main__":
    unittest.main()
