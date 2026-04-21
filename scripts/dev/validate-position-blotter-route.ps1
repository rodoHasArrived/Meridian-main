#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Framework = "net9.0-windows10.0.19041.0",
    [string]$OutputRoot = "artifacts/wpf-validation/position-blotter-route",
    [string]$Filter = "FullyQualifiedName~PositionBlotterViewModelTests|FullyQualifiedName~ShellNavigationCatalogTests|FullyQualifiedName~WorkspaceDeepPageChromeTests|FullyQualifiedName~TradingWorkspaceShellPageTests"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

. (Join-Path $PSScriptRoot "SharedBuild.ps1")

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$env:MERIDIAN_REPO_ROOT = $repoRoot
$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$summaryDir = Join-Path (Join-Path $repoRoot $OutputRoot) $runStamp
$testProject = "tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj"
$buildIsolationKey = New-MeridianBuildIsolationKey -Prefix "position-blotter-route"
$resultsDirectory = Join-Path $summaryDir "TestResults"
$binaryPath = Get-MeridianProjectBinaryPath `
    -RepoRoot $repoRoot `
    -ProjectPath $testProject `
    -Configuration $Configuration `
    -Framework $Framework `
    -BinaryName "Meridian.Wpf.Tests.dll" `
    -IsolationKey $buildIsolationKey

New-Item -ItemType Directory -Force -Path $summaryDir, $resultsDirectory | Out-Null

$sharedArgs = Get-MeridianBuildArguments `
    -IsolationKey $buildIsolationKey `
    -TargetFramework $Framework `
    -EnableFullWpfBuild

function Format-CommandText {
    param([Parameter(Mandatory = $true)][string[]]$Command)

    return ($Command | ForEach-Object {
            if ($_ -match "\s") { '"{0}"' -f $_ } else { $_ }
        }) -join " "
}

function Get-RepoOwnedTestHostProcess {
    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'testhost.exe'" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        return @()
    }

    return @(
        $processes | Where-Object {
            ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) -or
            ($_.CommandLine -and $_.CommandLine.IndexOf($repoRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            ($_.CommandLine -and $_.CommandLine.IndexOf("Meridian.Wpf.Tests", [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
        }
    )
}

function Stop-RepoOwnedTestHostProcess {
    $repoTestHosts = @(Get-RepoOwnedTestHostProcess)
    if ($repoTestHosts.Count -eq 0) {
        return @()
    }

    foreach ($repoTestHost in $repoTestHosts) {
        Write-Host "Stopping stale repo-owned testhost PID $($repoTestHost.ProcessId)..." -ForegroundColor Yellow
        Stop-Process -Id $repoTestHost.ProcessId -Force -ErrorAction Stop
    }

    Start-Sleep -Milliseconds 750
    return $repoTestHosts
}

function Invoke-LoggedStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Command,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    Write-Host ("    " + (Format-CommandText -Command $Command))

    $output = @()
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    try {
        & $Command[0] @($Command[1..($Command.Count - 1)]) 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { $output += $_ }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        $stopwatch.Stop()
    }

    return [ordered]@{
        name = $Name
        command = Format-CommandText -Command $Command
        exitCode = $LASTEXITCODE
        durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        logPath = $LogPath
        tail = ($output | Select-Object -Last 25) -join [Environment]::NewLine
    }
}

$buildCommand = @(
    "dotnet",
    "build",
    $testProject,
    "-c", $Configuration,
    "--nologo",
    "--verbosity", "minimal"
) + $sharedArgs

$testCommand = @(
    "dotnet",
    "test",
    $testProject,
    "-c", $Configuration,
    "--no-build",
    "--nologo",
    "--verbosity", "minimal",
    "--results-directory", $resultsDirectory,
    "--filter", $Filter
) + $sharedArgs

$steps = New-Object System.Collections.Generic.List[object]
$retryReason = $null
$stoppedTestHostPids = @()

$buildLogPath = Join-Path $summaryDir "build.log"
$buildStep = Invoke-LoggedStep -Name "Build isolated WPF route slice" -Command $buildCommand -LogPath $buildLogPath
$steps.Add($buildStep) | Out-Null

if ($buildStep.exitCode -ne 0) {
    $staleRepoTestHosts = @(Get-RepoOwnedTestHostProcess)
    if ($staleRepoTestHosts.Count -gt 0) {
        $retryReason = "build failed while repo-owned testhost processes were still running"
        $stoppedTestHostPids = @($staleRepoTestHosts | Select-Object -ExpandProperty ProcessId)
        Stop-RepoOwnedTestHostProcess | Out-Null

        $retryLogPath = Join-Path $summaryDir "build-retry.log"
        $buildRetryStep = Invoke-LoggedStep -Name "Retry isolated WPF route build after testhost cleanup" -Command $buildCommand -LogPath $retryLogPath
        $steps.Add($buildRetryStep) | Out-Null
        $buildStep = $buildRetryStep
    }
}

if ($buildStep.exitCode -eq 0) {
    $testLogPath = Join-Path $summaryDir "test.log"
    $testStep = Invoke-LoggedStep -Name "Run position blotter route test slice" -Command $testCommand -LogPath $testLogPath
    $steps.Add($testStep) | Out-Null
}

$failedSteps = @($steps | Where-Object { $_.exitCode -ne 0 })
$summary = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    configuration = $Configuration
    framework = $Framework
    repoRoot = $repoRoot
    testProject = $testProject
    filter = $Filter
    buildIsolationKey = $buildIsolationKey
    binaryPath = $binaryPath
    resultsDirectory = $resultsDirectory
    retryReason = $retryReason
    stoppedTestHostPids = $stoppedTestHostPids
    result = if ($failedSteps.Count -eq 0) { "passed" } else { "failed" }
    steps = $steps
}

$summaryJsonPath = Join-Path $summaryDir "position-blotter-route-validation.json"
$summaryMarkdownPath = Join-Path $summaryDir "position-blotter-route-validation.md"

$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryJsonPath

$markdown = @(
    "# Position Blotter Route Validation",
    "",
    "- Generated: $($summary.generatedAtUtc)",
    "- Configuration: $Configuration",
    "- Framework: $Framework",
    "- Build isolation key: $buildIsolationKey",
    ('- Test project: `{0}`' -f $testProject),
    ('- Test filter: `{0}`' -f $Filter),
    ('- Binary path: `{0}`' -f $binaryPath),
    ('- Results directory: `{0}`' -f $resultsDirectory),
    "- Overall result: $($summary.result)",
    ""
)

if (-not [string]::IsNullOrWhiteSpace($retryReason)) {
    $markdown += "- Retry reason: $retryReason"
    $markdown += "- Stopped repo-owned testhost PIDs: $($stoppedTestHostPids -join ', ')"
    $markdown += ""
}

$markdown += @(
    "| Step | Exit Code | Duration (s) | Log |",
    "|---|---:|---:|---|"
)

foreach ($step in $steps) {
    $relativeLogPath = $step.logPath.Substring($repoRoot.Length + 1).Replace('\', '/')
    $markdown += ('| {0} | {1} | {2} | `{3}` |' -f $step.name, $step.exitCode, $step.durationSeconds, $relativeLogPath)
}

$markdown -join [Environment]::NewLine | Set-Content -Path $summaryMarkdownPath

Write-Host ""
Write-Host "Validation artifacts:" -ForegroundColor Green
Write-Host "  $summaryJsonPath"
Write-Host "  $summaryMarkdownPath"

if ($summary.result -ne "passed") {
    $failedStepNames = ($failedSteps | ForEach-Object { $_.name }) -join ", "
    throw "Position blotter route validation failed: $failedStepNames"
}
