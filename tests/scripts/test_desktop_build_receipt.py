from __future__ import annotations

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
POWERSHELL = shutil.which("pwsh")
FRAMEWORK = "net10.0-windows10.0.19041.0"
WPF_PROJECT = "src/Meridian.Wpf/Meridian.Wpf.csproj"


@unittest.skipUnless(POWERSHELL, "PowerShell 7 is not available")
class DesktopBuildReceiptTests(unittest.TestCase):
    def _make_repo(self, root: Path) -> None:
        scripts = root / "scripts/dev"
        scripts.mkdir(parents=True)
        for name in (
            "desktop-dev.ps1",
            "run-desktop.ps1",
            "SharedBuild.ps1",
            "SharedDesktopBuild.ps1",
            "SharedWorkflowProfiles.ps1",
        ):
            shutil.copy2(REPO_ROOT / "scripts/dev" / name, scripts / name)
        shutil.copytree(REPO_ROOT / "scripts/dev/workflow-profiles", scripts / "workflow-profiles")
        for relative in (
            WPF_PROJECT,
            "src/Meridian/Meridian.csproj",
            "src/Meridian.Ui.Services/Meridian.Ui.Services.csproj",
            "tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj",
        ):
            project = root / relative
            project.parent.mkdir(parents=True, exist_ok=True)
            project.write_text("<Project />", encoding="utf-8")

    def _probe(self, root: Path, body: str) -> dict:
        script = root / "probe.ps1"
        script.write_text(
            "$ErrorActionPreference = 'Stop'\n"
            "$global:LASTEXITCODE = 0\n"
            "$repoRoot = $PSScriptRoot\n"
            "Set-Location $repoRoot\n"
            ". (Join-Path $repoRoot 'scripts/dev/SharedBuild.ps1')\n"
            ". (Join-Path $repoRoot 'scripts/dev/SharedDesktopBuild.ps1')\n"
            + body,
            encoding="utf-8",
        )
        result = subprocess.run(
            [str(POWERSHELL), "-NoProfile", "-File", str(script)],
            cwd=root,
            capture_output=True,
            text=True,
            timeout=45,
        )
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        receipts = [line.removeprefix("HARNESS_JSON=") for line in result.stdout.splitlines()
                    if line.startswith("HARNESS_JSON=")]
        self.assertEqual(len(receipts), 1, result.stdout + result.stderr)
        return json.loads(receipts[0])

    def _receipt_probe(self, mutation: str = "", *, isolation_key: str = "receipt-test") -> dict:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._make_repo(root)
            return self._probe(root, f"""
$binaryPath = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath '{WPF_PROJECT}' -Configuration Debug -Framework '{FRAMEWORK}' -BinaryName 'Meridian.Desktop.exe' -IsolationKey '{isolation_key}'
$outputDirectory = Split-Path -Parent $binaryPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
foreach ($name in @('Meridian.Desktop.exe', 'Meridian.Desktop.dll', 'Meridian.Desktop.deps.json', 'Meridian.Desktop.runtimeconfig.json')) {{
    Set-Content -LiteralPath (Join-Path $outputDirectory $name) -Value 'fixture'
}}
$receiptPath = New-MeridianDesktopBuildReceipt -RepoRoot $repoRoot -ProjectPath '{WPF_PROJECT}' -Configuration Debug -Framework '{FRAMEWORK}' -BinaryName 'Meridian.Desktop.exe' -IsolationKey '{isolation_key}'
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable
{mutation}
$accepted = $false
$errorMessage = ''
$validated = $null
try {{
    $validated = Read-MeridianDesktopBuildReceipt -ReceiptPath $receiptPath -RepoRoot $repoRoot -ProjectPath '{WPF_PROJECT}' -Configuration Debug -Framework '{FRAMEWORK}' -BinaryName 'Meridian.Desktop.exe'
    $accepted = $true
}} catch {{ $errorMessage = $_.Exception.Message }}
Write-Output ('HARNESS_JSON=' + (@{{ accepted = $accepted; error = $errorMessage; receipt = $validated; expectedExecutable = $binaryPath }} | ConvertTo-Json -Depth 8 -Compress))
""")

    def test_receipt_round_trip_preserves_exact_output_and_binary_paths(self) -> None:
        for isolation_key in ("receipt-test", ""):
            with self.subTest(isolation_key=isolation_key):
                result = self._receipt_probe(isolation_key=isolation_key)
                self.assertTrue(result["accepted"], result["error"])
                receipt = result["receipt"]
                self.assertEqual(receipt["schemaVersion"], 1)
                self.assertEqual(receipt["configuration"], "Debug")
                self.assertEqual(receipt["framework"], FRAMEWORK)
                self.assertEqual(receipt["buildIsolationKey"], isolation_key)
                self.assertEqual(receipt["executablePath"], result["expectedExecutable"])
                self.assertEqual(str(Path(receipt["executablePath"]).parent), receipt["outputDirectory"])

    def test_receipt_rejects_missing_malformed_and_incompatible_data(self) -> None:
        mutations = {
            "missing receipt": "Remove-Item -LiteralPath $receiptPath",
            "malformed JSON": "Set-Content -LiteralPath $receiptPath -Value '{ broken JSON'",
            "unknown schema": "$receipt.schemaVersion = 2",
            "string schema": "$receipt.schemaVersion = '1'",
            "JSON primitive": "$receipt = 'unexpected'",
            "invalid isolation key": "$receipt.buildIsolationKey = '../escape'",
            "empty configuration": "$receipt.configuration = ''",
            "nonstring configuration": "$receipt.configuration = 10",
            "relative executable": "$receipt.executablePath = 'Meridian.Desktop.exe'",
            "different repository": "$receipt.repoRoot = Join-Path $repoRoot 'other-repo'",
            "different project": "$receipt.projectPath = Join-Path $repoRoot 'src/Meridian/Meridian.csproj'",
            "different configuration": "$receipt.configuration = 'Release'",
            "different framework": "$receipt.framework = 'net9.0-windows10.0.19041.0'",
            "different isolation key": "$receipt.buildIsolationKey = 'other-run'",
            "different output": "$receipt.outputDirectory = $repoRoot",
            "different executable": "$receipt.executablePath = Join-Path $repoRoot 'Meridian.Desktop.exe'",
            "different assembly": "$receipt.assemblyPath = Join-Path $repoRoot 'Meridian.Desktop.dll'",
            "different deps": "$receipt.depsJsonPath = Join-Path $repoRoot 'Meridian.Desktop.deps.json'",
            "different runtime config": "$receipt.runtimeConfigPath = Join-Path $repoRoot 'Meridian.Desktop.runtimeconfig.json'",
        }
        for name, mutation in mutations.items():
            with self.subTest(case=name):
                if name not in ("missing receipt", "malformed JSON"):
                    mutation += "\n$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath"
                result = self._receipt_probe(mutation)
                self.assertFalse(result["accepted"], name)
                self.assertTrue(result["error"], name)

    def test_receipt_rejects_missing_required_fields_and_binary_files(self) -> None:
        for field in (
            "schemaVersion", "repoRoot", "projectPath", "buildIsolationKey", "configuration", "framework",
            "outputDirectory", "executablePath", "assemblyPath", "depsJsonPath", "runtimeConfigPath",
        ):
            with self.subTest(missing_field=field):
                result = self._receipt_probe(
                    f"$receipt.Remove('{field}')\n"
                    "$receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath"
                )
                self.assertFalse(result["accepted"], field)
                self.assertTrue(result["error"], field)
        for field in ("executablePath", "assemblyPath", "depsJsonPath", "runtimeConfigPath"):
            with self.subTest(missing_file=field):
                result = self._receipt_probe(f"Remove-Item -LiteralPath $receipt['{field}']")
                self.assertFalse(result["accepted"], field)
                self.assertTrue(result["error"], field)
            with self.subTest(directory_instead_of_file=field):
                result = self._receipt_probe(
                    f"Remove-Item -LiteralPath $receipt['{field}']\n"
                    f"New-Item -ItemType Directory -Path $receipt['{field}'] | Out-Null"
                )
                self.assertFalse(result["accepted"], field)

    def test_receipt_writer_requires_complete_build_output(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._make_repo(root)
            result = self._probe(root, f"""
$binaryPath = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath '{WPF_PROJECT}' -Configuration Debug -Framework '{FRAMEWORK}' -BinaryName 'Meridian.Desktop.exe' -IsolationKey 'receipt-test'
$outputDirectory = Split-Path -Parent $binaryPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$names = @('Meridian.Desktop.exe', 'Meridian.Desktop.dll', 'Meridian.Desktop.deps.json', 'Meridian.Desktop.runtimeconfig.json')
$results = @()
foreach ($missingName in $names) {{
    foreach ($name in $names) {{ Set-Content -LiteralPath (Join-Path $outputDirectory $name) -Value 'fixture' }}
    Remove-Item -LiteralPath (Join-Path $outputDirectory $missingName)
    $accepted = $false
    try {{
        New-MeridianDesktopBuildReceipt -RepoRoot $repoRoot -ProjectPath '{WPF_PROJECT}' -Configuration Debug -Framework '{FRAMEWORK}' -BinaryName 'Meridian.Desktop.exe' -IsolationKey 'receipt-test' | Out-Null
        $accepted = $true
    }} catch {{ }}
    $results += @{{ missing = $missingName; accepted = $accepted; receiptExists = (Test-Path -LiteralPath (Join-Path $outputDirectory 'desktop-build-receipt.json')) }}
}}
Write-Output ('HARNESS_JSON=' + (@{{ cases = $results }} | ConvertTo-Json -Depth 8 -Compress))
""")
            self.assertEqual(len(result["cases"]), 4)
            for case in result["cases"]:
                self.assertFalse(case["accepted"], case)
                self.assertFalse(case["receiptExists"], case)

    def _launcher_probe(
        self,
        *,
        dev: bool = False,
        healthy: bool = False,
        host_binary: bool = False,
        build_only: bool = False,
        no_build: bool = False,
        configuration: str = "Debug",
        framework: str = FRAMEWORK,
        invalid_receipt: bool = False,
        missing_receipt: bool = False,
        missing_artifact: bool = False,
        host_never_ready: bool = False,
        window_failure: bool = False,
        without_receipt: bool = False,
        health_transition: str = "",
    ) -> dict:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self._make_repo(root)
            options = {
                "dev": dev,
                "healthy": healthy,
                "hostBinary": host_binary,
                "buildOnly": build_only,
                "noBuild": no_build,
                "configuration": configuration,
                "framework": framework,
                "invalidReceipt": invalid_receipt,
                "missingReceipt": missing_receipt,
                "missingArtifact": missing_artifact,
                "hostNeverReady": host_never_ready,
                "windowFailure": window_failure,
                "withoutReceipt": without_receipt,
                "healthTransition": health_transition,
            }
            (root / "options.json").write_text(json.dumps(options), encoding="utf-8")
            return self._probe(root, LAUNCH_HARNESS)

    def test_desktop_dev_compiles_wpf_once_and_runs_fixture_readiness_smoke(self) -> None:
        for configuration, framework in (
            ("Debug", FRAMEWORK),
            ("Release", "net10.0-windows10.0.22621.0"),
        ):
            with self.subTest(configuration=configuration, framework=framework):
                result = self._launcher_probe(dev=True, configuration=configuration, framework=framework)
                self.assertEqual(result["error"], "")
                self.assertEqual(result["wpfBuilds"], 1, result)
                test_builds = [build for build in result["builds"] if build["project"].endswith("Meridian.Wpf.Tests.csproj")]
                self.assertEqual(len(test_builds), 1, result)
                self.assertIn("--no-dependencies", test_builds[0]["arguments"])
                self.assertEqual(result["hostBuilds"], 1, result)
                desktop = self._desktop_launch(result)
                self.assertIn(f"/{configuration}/{framework}/Meridian.Desktop.exe", desktop["path"].replace("\\", "/"))
                self.assertIn("/artifacts/bin/desktop-dev-", desktop["path"].replace("\\", "/"))
                self.assertEqual(desktop["path"], result["receiptExecutable"])
                self._assert_fixture_readiness(result)

    def test_launcher_receipt_launches_exact_binary_and_builds_only_missing_host(self) -> None:
        for host_binary in (False, True):
            with self.subTest(host_binary=host_binary):
                result = self._launcher_probe(host_binary=host_binary)
                self.assertEqual(result["error"], "")
                self.assertEqual(result["wpfBuilds"], 0, result)
                self.assertEqual(result["hostBuilds"], 0 if host_binary else 1, result)
                self.assertEqual(self._desktop_launch(result)["path"], result["receiptExecutable"])
                self.assertEqual(result["launches"][0]["path"], result["expectedHost"])
                self._assert_fixture_readiness(result)

    def test_standalone_launcher_builds_wpf_once_before_missing_host(self) -> None:
        result = self._launcher_probe(without_receipt=True)
        self.assertEqual(result["error"], "")
        self.assertEqual(result["wpfBuilds"], 1, result)
        self.assertEqual(result["hostBuilds"], 1, result)
        self.assertTrue(result["builds"][0]["project"].endswith("Meridian.Wpf.csproj"), result)
        self.assertEqual(self._desktop_launch(result)["path"], result["receiptExecutable"])
        self._assert_fixture_readiness(result)

    def test_standalone_launcher_rechecks_host_health_after_wpf_build(self) -> None:
        for healthy, transition, host_builds in (
            (True, "dies_after_wpf", 1),
            (False, "ready_after_wpf", 0),
        ):
            with self.subTest(transition=transition):
                result = self._launcher_probe(without_receipt=True, healthy=healthy, health_transition=transition)
                self.assertEqual(result["error"], "")
                self.assertEqual(result["wpfBuilds"], 1, result)
                self.assertEqual(result["hostBuilds"], host_builds, result)
                self.assertEqual(len(result["launches"]), 1 + host_builds, result)
                self.assertGreaterEqual(result["healthChecks"], 2, result)
                self._assert_fixture_readiness(result)

    def test_healthy_host_reuse_needs_no_host_artifact_or_build(self) -> None:
        for no_build in (False, True):
            with self.subTest(no_build=no_build):
                result = self._launcher_probe(healthy=True, no_build=no_build)
                self.assertEqual(result["error"], "")
                self.assertEqual(result["builds"], [], result)
                self.assertEqual(len(result["launches"]), 1, result)
                self.assertEqual(self._desktop_launch(result)["path"], result["receiptExecutable"])
                self._assert_fixture_readiness(result)

    def test_build_only_ensures_host_artifact_even_with_healthy_running_host(self) -> None:
        result = self._launcher_probe(healthy=True, build_only=True)
        self.assertEqual(result["error"], "")
        self.assertEqual(result["wpfBuilds"], 0, result)
        self.assertEqual(result["hostBuilds"], 1, result)
        self.assertEqual(result["launches"], [], result)
        self.assertEqual(result["windowChecks"], 0, result)

    def test_no_build_rejects_missing_host_and_accepts_existing_binary(self) -> None:
        missing = self._launcher_probe(no_build=True)
        self.assertTrue(missing["error"], missing)
        self.assertEqual(missing["builds"], [], missing)
        self.assertEqual(missing["launches"], [], missing)
        existing = self._launcher_probe(no_build=True, host_binary=True)
        self.assertEqual(existing["error"], "")
        self.assertEqual(existing["builds"], [], existing)
        self._assert_fixture_readiness(existing)

    def test_missing_or_incompatible_receipt_fails_before_build_or_launch(self) -> None:
        for fault in ("invalid_receipt", "missing_receipt", "missing_artifact"):
            with self.subTest(fault=fault):
                result = self._launcher_probe(**{fault: True})
                self.assertTrue(result["error"], result)
                self.assertEqual(result["builds"], [], result)
                self.assertEqual(result["launches"], [], result)
                self.assertEqual(result["windowChecks"], 0, result)
                self.assertEqual(result["healthChecks"], 0, result)

    def test_host_and_window_readiness_failures_still_fail_smoke(self) -> None:
        host = self._launcher_probe(host_never_ready=True)
        self.assertTrue(host["error"], host)
        self.assertEqual(len(host["launches"]), 1, host)
        self.assertGreaterEqual(host["healthChecks"], 2, host)
        self.assertEqual(host["windowChecks"], 0, host)
        window = self._launcher_probe(window_failure=True)
        self.assertIn("fixture window readiness failed", window["error"])
        self.assertEqual(len(window["launches"]), 2, window)
        self.assertEqual(window["windowChecks"], 1, window)
        self.assertEqual(window["smokeStops"], 0, window)

    def _desktop_launch(self, result: dict) -> dict:
        matches = [launch for launch in result["launches"] if launch["path"].endswith("Meridian.Desktop.exe")]
        self.assertEqual(len(matches), 1, result)
        return matches[0]

    def _assert_fixture_readiness(self, result: dict) -> None:
        desktop = self._desktop_launch(result)
        self.assertIn("--fixture", desktop["arguments"], result)
        for launch in result["launches"]:
            self.assertEqual(launch["dataSource"], "Synthetic", result)
            self.assertEqual(launch["syntheticMode"], "1", result)
            self.assertEqual(launch["fixtureMode"], "1", result)
            self.assertEqual(launch["environment"], "Development", result)
        self.assertGreaterEqual(result["healthChecks"], 1, result)
        self.assertEqual(result["windowChecks"], 1, result)
        self.assertEqual(result["smokeStops"], 1, result)


LAUNCH_HARNESS = r"""
$options = Get-Content (Join-Path $repoRoot 'options.json') -Raw | ConvertFrom-Json -AsHashtable
$global:harness = @{
    builds = [System.Collections.Generic.List[object]]::new()
    launches = [System.Collections.Generic.List[object]]::new()
    healthChecks = 0
    windowChecks = 0
    smokeStops = 0
    hostStarted = $false
    builtDesktop = ''
    externalHealthy = [bool]$options.healthy
    options = $options
}
$env:OS = 'Windows_NT'
${env:ProgramFiles(x86)} = $repoRoot
$env:MDC_DATASOURCE = 'original-source'
$env:MDC_SYNTHETIC_MODE = 'original-synthetic'
$env:MDC_FIXTURE_MODE = 'original-fixture'
function global:Get-CimInstance { [CmdletBinding()] param($ClassName, $Filter) return @() }
function global:Get-Process { [CmdletBinding()] param($Name) return @() }
function global:Start-Sleep { param($Seconds, $Milliseconds) }
function global:Write-Progress { param($Activity, $Status, $PercentComplete, [switch]$Completed) }
function global:Invoke-WebRequest {
    [CmdletBinding()]
    param($Uri, [switch]$UseBasicParsing, $TimeoutSec, $Method, $Headers)
    if ($Uri -like '*/healthz') {
        $global:harness.healthChecks++
        if ($global:harness.externalHealthy -or ($global:harness.hostStarted -and -not $global:harness.options.hostNeverReady)) {
            return @{ StatusCode = 200 }
        }
        throw 'Fixture host is not ready'
    }
    return @{ StatusCode = 200 }
}
function global:Start-Process {
    [CmdletBinding()]
    param($FilePath, $ArgumentList, $WorkingDirectory, $RedirectStandardOutput, $RedirectStandardError, $WindowStyle, [switch]$PassThru)
    $global:harness.launches.Add(@{
        path = [string]$FilePath
        arguments = @($ArgumentList)
        dataSource = $env:MDC_DATASOURCE
        syntheticMode = $env:MDC_SYNTHETIC_MODE
        fixtureMode = $env:MDC_FIXTURE_MODE
        environment = $env:DOTNET_ENVIRONMENT
    })
    if ((Split-Path -Leaf $FilePath) -eq 'Meridian.exe') { $global:harness.hostStarted = $true }
    return [System.Diagnostics.Process]::GetCurrentProcess()
}
function global:TestHarness-WindowReady {
    param([System.Diagnostics.Process]$Process, [int]$TimeoutSec)
    $global:harness.windowChecks++
    if ($global:harness.options.windowFailure) { throw 'fixture window readiness failed' }
}
function global:TestHarness-SmokeStop { param([System.Diagnostics.Process]$Process) $global:harness.smokeStops++ }
function global:TestHarness-OwnedDesktopStop { }
function global:TestHarness-OwnedHostStop { }
Set-Alias -Name Wait-ForDesktopWindow -Value TestHarness-WindowReady -Scope Global
Set-Alias -Name Stop-DesktopProcessAfterSmoke -Value TestHarness-SmokeStop -Scope Global
Set-Alias -Name Stop-OwnedDesktopProcessSafely -Value TestHarness-OwnedDesktopStop -Scope Global
Set-Alias -Name Stop-OwnedHost -Value TestHarness-OwnedHostStop -Scope Global
function global:TestHarness-WriteBinary {
    param([string]$ProjectPath, [string]$Configuration, [string]$Framework, [string]$IsolationKey, [string]$BinaryName)
    $exe = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath $ProjectPath -Configuration $Configuration -Framework $Framework -BinaryName $BinaryName -IsolationKey $IsolationKey
    $directory = Split-Path -Parent $exe
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Set-Content -LiteralPath $exe -Value 'fixture executable'
    if ($BinaryName -eq 'Meridian.Desktop.exe') {
        $global:harness.builtDesktop = $exe
        foreach ($name in @('Meridian.Desktop.dll', 'Meridian.Desktop.deps.json', 'Meridian.Desktop.runtimeconfig.json')) {
            Set-Content -LiteralPath (Join-Path $directory $name) -Value 'fixture companion'
        }
    }
    return $exe
}
function global:dotnet {
    $command = @($args | ForEach-Object { $_ })
    $global:LASTEXITCODE = 0
    if ($command[0] -eq '--version') { return '10.0.100' }
    if ($command[0] -eq '--list-sdks') { return '10.0.100 [fixture-sdk]' }
    if ($command[0] -ne 'build') { return }
    $project = [string]$command[1]
    $configuration = 'Debug'
    $configurationIndex = [Array]::IndexOf($command, '-c')
    if ($configurationIndex -ge 0) { $configuration = [string]$command[$configurationIndex + 1] }
    $framework = 'net10.0'
    $isolationKey = ''
    foreach ($argument in $command) {
        if ($argument -like '/p:MeridianBuildIsolationKey=*') { $isolationKey = $argument.Substring('/p:MeridianBuildIsolationKey='.Length) }
        if ($argument -like '/p:TargetFramework=*') { $framework = $argument.Substring('/p:TargetFramework='.Length) }
    }
    $global:harness.builds.Add(@{ project = $project; arguments = $command })
    if ($project.EndsWith('Meridian.Wpf.csproj')) {
        TestHarness-WriteBinary -ProjectPath $project -Configuration $configuration -Framework $framework -IsolationKey $isolationKey -BinaryName 'Meridian.Desktop.exe' | Out-Null
        if ($global:harness.options.healthTransition -eq 'dies_after_wpf') { $global:harness.externalHealthy = $false }
        if ($global:harness.options.healthTransition -eq 'ready_after_wpf') { $global:harness.externalHealthy = $true }
    } elseif ($project.EndsWith('Meridian.csproj')) {
        TestHarness-WriteBinary -ProjectPath $project -Configuration $configuration -Framework $framework -IsolationKey $isolationKey -BinaryName 'Meridian.exe' | Out-Null
    } elseif ($project.EndsWith('Meridian.Wpf.Tests.csproj') -and $command -notcontains '--no-dependencies') {
        # Model the test project's WPF ProjectReference so a repeated graph build is observable.
        $global:harness.builds.Add(@{ project = 'src/Meridian.Wpf/Meridian.Wpf.csproj'; arguments = @('dependency-build') })
    }
}
function global:pwsh {
    $command = @($args | ForEach-Object { $_ })
    $fileIndex = [Array]::IndexOf($command, '-File')
    if ($fileIndex -lt 0) { throw 'The fixture pwsh accepts script invocations only' }
    $scriptPath = [string]$command[$fileIndex + 1]
    $scriptArguments = @{}
    for ($index = $fileIndex + 2; $index -lt $command.Count; $index++) {
        $name = ([string]$command[$index]).TrimStart('-')
        if ($index + 1 -lt $command.Count -and -not ([string]$command[$index + 1]).StartsWith('-')) {
            $scriptArguments[$name] = $command[++$index]
        } else {
            $scriptArguments[$name] = $true
        }
    }
    try {
        & $scriptPath @scriptArguments
        $global:LASTEXITCODE = 0
    } catch {
        Write-Output $_.Exception.Message
        $global:LASTEXITCODE = 1
    }
}
$receiptPath = ''
$receiptExecutable = ''
$expectedHost = ''
if (-not $options.dev -and -not $options.withoutReceipt) {
    $receiptExecutable = TestHarness-WriteBinary -ProjectPath 'src/Meridian.Wpf/Meridian.Wpf.csproj' -Configuration $options.configuration -Framework $options.framework -IsolationKey 'receipt-test' -BinaryName 'Meridian.Desktop.exe'
    $receiptPath = New-MeridianDesktopBuildReceipt -RepoRoot $repoRoot -ProjectPath 'src/Meridian.Wpf/Meridian.Wpf.csproj' -Configuration $options.configuration -Framework $options.framework -BinaryName 'Meridian.Desktop.exe' -IsolationKey 'receipt-test'
    $expectedHost = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath 'src/Meridian/Meridian.csproj' -Configuration Debug -Framework 'net10.0' -BinaryName 'Meridian.exe' -IsolationKey 'receipt-test'
    if ($options.hostBinary) {
        TestHarness-WriteBinary -ProjectPath 'src/Meridian/Meridian.csproj' -Configuration Debug -Framework 'net10.0' -IsolationKey 'receipt-test' -BinaryName 'Meridian.exe' | Out-Null
    }
    if ($options.invalidReceipt) {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -AsHashtable
        $receipt.configuration = 'Release'
        $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath
    }
    if ($options.missingReceipt) { Remove-Item -LiteralPath $receiptPath }
    if ($options.missingArtifact) { Remove-Item -LiteralPath (Join-Path (Split-Path -Parent $receiptExecutable) 'Meridian.Desktop.dll') }
}
$profilePath = Join-Path $repoRoot 'scripts/dev/workflow-profiles/desktop-development.json'
$profile = Get-Content -LiteralPath $profilePath -Raw | ConvertFrom-Json -AsHashtable
$profile.host.startupTimeoutSec = 1
$profile | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $profilePath
$errorMessage = ''
try {
    if ($options.dev) {
        & (Join-Path $repoRoot 'scripts/dev/desktop-dev.ps1') -Configuration $options.configuration -Framework $options.framework | Out-Null
    } else {
        $launchParameters = @{
            Configuration = $options.configuration
            Framework = $options.framework
            Fixture = $true
            StartupSmoke = $true
            BuildOnly = [bool]$options.buildOnly
            NoBuild = [bool]$options.noBuild
        }
        if (-not $options.withoutReceipt) { $launchParameters.BuildReceiptPath = $receiptPath }
        & (Join-Path $repoRoot 'scripts/dev/run-desktop.ps1') @launchParameters | Out-Null
    }
} catch { $errorMessage = $_.Exception.Message }
$wpfBuilds = @($global:harness.builds | Where-Object { $_.project.EndsWith('Meridian.Wpf.csproj') }).Count
$hostBuilds = @($global:harness.builds | Where-Object { $_.project.EndsWith('/Meridian.csproj') }).Count
Write-Output ('HARNESS_JSON=' + (@{
    error = $errorMessage
    builds = @($global:harness.builds.ToArray())
    launches = @($global:harness.launches.ToArray())
    wpfBuilds = $wpfBuilds
    hostBuilds = $hostBuilds
    healthChecks = $global:harness.healthChecks
    windowChecks = $global:harness.windowChecks
    smokeStops = $global:harness.smokeStops
    receiptExecutable = $global:harness.builtDesktop
    expectedHost = $expectedHost
} | ConvertTo-Json -Depth 8 -Compress))
"""


if __name__ == "__main__":
    unittest.main()
