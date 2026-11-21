import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
RUNNER = REPO_ROOT / "scripts" / "dev" / "run-desktop-workflow.ps1"
TEMPLATE = REPO_ROOT / "scripts" / "dev" / "desktop-workflow-fixture-config.json"
POWERSHELL = shutil.which("pwsh")


@unittest.skipUnless(POWERSHELL, "PowerShell 7 is not available")
class DesktopWorkflowFixtureTests(unittest.TestCase):
    def run_fixture(self, scenario: str) -> dict:
        with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as directory:
            test_root = Path(directory)
            harness = test_root / "fixture-test.ps1"
            harness.write_text(
                r"""
param([string]$Runner, [string]$Template, [string]$TestRoot, [string]$Scenario)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($Runner, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
$functions = @('Backup-FileState', 'Restore-FileState', 'Initialize-FixtureOperatingContext', 'Assert-FixtureProcessOwnership')
$definitions = @($ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -in $functions
}, $true))
if ($definitions.Count -ne $functions.Count) { throw 'Fixture functions were not found' }
foreach ($definition in $definitions) { Invoke-Expression $definition.Extent.Text }
function Write-Warn([string]$Message) { throw $Message }
function Write-Info([string]$Message) { }
$script:DesktopProcessName = 'Meridian.Desktop'
function Get-Process {
    [CmdletBinding()]
    param([string]$Name)
    if ($Scenario -eq 'running') { [pscustomobject]@{ Id = 123 } }
}
if ($Scenario -eq 'non-fixture') {
    Assert-FixtureProcessOwnership -UseFixture:$false -KeepAppOpen -ReuseExistingApp
    '{"allowed":true}'
    exit 0
}
if ($Scenario -eq 'missing-backup') {
    $operatorPath = Join-Path $TestRoot 'operator.json'
    [System.IO.File]::WriteAllText($operatorPath, 'operator bytes')
    $state = [pscustomobject]@{ Path = $operatorPath; BackupPath = "$operatorPath.missing"; Existed = $true; TemporaryBackup = $true }
    $failed = $false
    try { Restore-FileState -States @($state) } catch { $failed = $true }
    [ordered]@{ failed = $failed; retained = [System.IO.File]::ReadAllText($operatorPath) -eq 'operator bytes' } | ConvertTo-Json -Compress
    exit 0
}
if ($Scenario -eq 'post-write-failure') {
    function Set-Content {
        param([string]$LiteralPath, [string]$Encoding, [Parameter(ValueFromPipeline = $true)]$Value)
        process {
            if ($LiteralPath.EndsWith('workstation-operating-context.json')) { throw 'Injected failure after config and fund writes' }
            Microsoft.PowerShell.Management\Set-Content -LiteralPath $LiteralPath -Encoding $Encoding -Value $Value
        }
    }
}

$localData = Join-Path $TestRoot 'local-data'
$applicationData = Join-Path $localData 'Meridian'
$bundle = Join-Path $TestRoot 'bundle'
$privateBackup = Join-Path $TestRoot 'private-backup'
$fixtureData = Join-Path $TestRoot 'run-fixture-data'
New-Item -ItemType Directory -Force -Path $applicationData | Out-Null
$names = @('fund-profiles.json', 'workstation-operating-context.json', 'appsettings.json', 'appsettings.json.last-known-good')
$originals = @{}
if ($Scenario -ne 'missing') {
    foreach ($name in $names) {
        $path = Join-Path $applicationData $name
        [System.IO.File]::WriteAllText($path, "original operator bytes: $name`r`n")
        $originals[$name] = [System.IO.File]::ReadAllBytes($path)
    }
}
if ($Scenario -eq 'invalid-template') { $Template = Join-Path $TestRoot 'missing-template.json' }
$failed = $false
try {
    Assert-FixtureProcessOwnership -UseFixture:$true -KeepAppOpen:($Scenario -eq 'keep-open') -ReuseExistingApp:($Scenario -eq 'reuse')
    $states = @(Initialize-FixtureOperatingContext -BackupDirectory $bundle `
        -LocalApplicationDataRoot $localData -ConfigurationTemplate $Template `
        -ConfigurationBackupDirectory $privateBackup -FixtureDataRoot $fixtureData)
}
catch {
    if ($Scenario -notin @('invalid-template', 'post-write-failure', 'keep-open', 'reuse', 'running')) { throw }
    $failed = $true
}

$seed = $null
$cleanup = $null
if (-not $failed) {
    $seed = Get-Content -LiteralPath (Join-Path $applicationData 'appsettings.json') -Raw | ConvertFrom-Json
    # FirstRunService refreshes this recovery copy while the fixture app is running.
    Copy-Item -LiteralPath (Join-Path $applicationData 'appsettings.json') `
        -Destination (Join-Path $applicationData 'appsettings.json.last-known-good') -Force
    if ($Scenario -in @('report-failure', 'report-restore-failure')) {
        $workflowTry = @($ast.EndBlock.Statements | Where-Object {
            $_ -is [System.Management.Automation.Language.TryStatementAst] -and $null -ne $_.Finally
        })
        if ($workflowTry.Count -ne 1) { throw 'The actual workflow finally block was not found' }
        $script:cleanupEvents = [System.Collections.Generic.List[string]]::new()
        function Stop-Process {
            [CmdletBinding()]
            param([int]$Id, [switch]$Force)
            $script:cleanupEvents.Add('process-stop')
        }
        function Stop-Transcript { $script:cleanupEvents.Add('transcript-stop') }
        function Set-Content {
            param([string]$LiteralPath, [string]$Encoding, [Parameter(ValueFromPipeline = $true)]$Value)
            process { throw 'Injected report write failure' }
        }
        $manifest = [ordered]@{ run = [ordered]@{ status = 'ok' }; steps = @() }
        $retryTelemetry = [System.Collections.ArrayList]::new()
        $manifestPath = Join-Path $TestRoot 'manifest.json'
        $ownedProcess = [pscustomobject]@{ HasExited = $false; Id = 123 }
        $KeepAppOpen = $false
        $fixtureFileStates = $states
        $workflowTranscriptActive = $true
        $originalWorkflowEnv = @{ MDC_FIXTURE_LIFECYCLE_TEST = 'operator-env' }
        [Environment]::SetEnvironmentVariable('MDC_FIXTURE_LIFECYCLE_TEST', 'fixture-env', 'Process')
        if ($Scenario -eq 'report-restore-failure') {
            Remove-Item -LiteralPath $states[2].BackupPath -Force
        }
        $cleanupError = $null
        try { Invoke-Expression ('& ' + $workflowTry[0].Finally.Extent.Text) } catch { $cleanupError = $_.Exception.Message }
        $cleanup = [ordered]@{
            error = $cleanupError
            events = @($script:cleanupEvents)
            restoredEnvironment = [Environment]::GetEnvironmentVariable('MDC_FIXTURE_LIFECYCLE_TEST', 'Process')
        }
    }
    else {
        Restore-FileState -States $states
    }
}
$restored = @{}
foreach ($name in $names) {
    $path = Join-Path $applicationData $name
    $restored[$name] = if ($Scenario -eq 'missing') {
        -not (Test-Path -LiteralPath $path)
    } else {
        [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($path)) -eq [Convert]::ToBase64String($originals[$name])
    }
}
[ordered]@{
    failed = $failed
    seed = $seed
    fixtureDataRoot = $fixtureData
    restored = $restored
    bundleBackups = @(if (Test-Path -LiteralPath $bundle) { Get-ChildItem -LiteralPath $bundle -File | Select-Object -ExpandProperty Name })
    privateBackups = @(if (Test-Path -LiteralPath $privateBackup) { Get-ChildItem -LiteralPath $privateBackup -File | Select-Object -ExpandProperty Name })
    cleanup = $cleanup
} | ConvertTo-Json -Depth 8 -Compress
""",
                encoding="utf-8",
            )
            result = subprocess.run(
                [POWERSHELL, "-NoProfile", "-NonInteractive", "-File", str(harness),
                 "-Runner", str(RUNNER), "-Template", str(TEMPLATE),
                 "-TestRoot", str(test_root), "-Scenario", scenario],
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            return json.loads(result.stdout)

    def assert_offline_seed(self, result: dict) -> None:
        seed = result["seed"]
        self.assertEqual(seed["DataSource"], "Synthetic")
        self.assertEqual(seed["Symbols"], [])
        self.assertEqual(seed["Synthetic"]["UniverseSymbols"], [])
        self.assertTrue(seed["Synthetic"]["Enabled"])
        self.assertFalse(seed["Backfill"]["Enabled"])
        self.assertEqual(seed["Backfill"]["Provider"], "synthetic")
        self.assertEqual(seed["Backfill"]["Symbols"], [])
        self.assertEqual(Path(seed["DataRoot"]), Path(result["fixtureDataRoot"]))
        self.assertTrue(all(result["restored"].values()), result["restored"])
        self.assertEqual(result["privateBackups"], [])

    def test_fixture_config_and_recovery_copy_restore_exact_operator_bytes(self) -> None:
        result = self.run_fixture("existing")
        self.assertFalse(result["failed"])
        self.assert_offline_seed(result)
        self.assertEqual(sorted(result["bundleBackups"]), ["fund-profiles.json.bak", "workstation-operating-context.json.bak"])

    def test_fixture_created_files_are_removed_after_capture(self) -> None:
        result = self.run_fixture("missing")
        self.assertFalse(result["failed"])
        self.assert_offline_seed(result)
        self.assertEqual(result["bundleBackups"], [])

    def test_fixture_seed_failure_restores_operator_state_and_is_not_hidden(self) -> None:
        for scenario in ("invalid-template", "post-write-failure"):
            with self.subTest(scenario=scenario):
                result = self.run_fixture(scenario)
                self.assertTrue(result["failed"])
                self.assertTrue(all(result["restored"].values()), result["restored"])
                self.assertEqual(result["privateBackups"], [])

    def test_fixture_guards_reject_retained_or_existing_apps_before_file_mutation(self) -> None:
        for scenario in ("keep-open", "reuse", "running"):
            with self.subTest(scenario=scenario):
                result = self.run_fixture(scenario)
                self.assertTrue(result["failed"])
                self.assertTrue(all(result["restored"].values()), result["restored"])
                self.assertEqual(result["bundleBackups"], [])
                self.assertEqual(result["privateBackups"], [])

    def test_non_fixture_runs_can_keep_or_reuse_an_app(self) -> None:
        self.assertTrue(self.run_fixture("non-fixture")["allowed"])

    def test_missing_backup_fails_without_deleting_current_operator_file(self) -> None:
        result = self.run_fixture("missing-backup")
        self.assertTrue(result["failed"])
        self.assertTrue(result["retained"])

    def test_report_write_failure_still_stops_process_and_restores_fixture_environment_and_transcript(self) -> None:
        result = self.run_fixture("report-failure")
        self.assertEqual(result["cleanup"]["error"], "Injected report write failure")
        self.assertEqual(result["cleanup"]["events"], ["process-stop", "transcript-stop"])
        self.assertEqual(result["cleanup"]["restoredEnvironment"], "operator-env")
        self.assert_offline_seed(result)

    def test_restoration_failure_remains_visible_after_report_failure_and_environment_cleanup(self) -> None:
        result = self.run_fixture("report-restore-failure")
        self.assertIn("Expected fixture backup", result["cleanup"]["error"])
        self.assertEqual(result["cleanup"]["events"], ["process-stop", "transcript-stop"])
        self.assertEqual(result["cleanup"]["restoredEnvironment"], "operator-env")
        self.assertFalse(result["restored"]["appsettings.json"])
        self.assertTrue(all(restored for name, restored in result["restored"].items() if name != "appsettings.json"))


if __name__ == "__main__":
    unittest.main()
