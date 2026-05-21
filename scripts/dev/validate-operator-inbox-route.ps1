#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0-windows10.0.19041.0",
    [string]$OutputRoot = "artifacts/wpf-validation/operator-inbox-route",
    [string]$Filter = "FullyQualifiedName~MainPageUiWorkflowTests|FullyQualifiedName~TradingWorkspaceShellPageTests|FullyQualifiedName~WorkspaceShellContextStripControlTests"
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
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot
$summaryDir = Join-Path $resolvedOutputRoot $runStamp
$testProject = "tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj"
$buildIsolationKey = New-MeridianBuildIsolationKey -Prefix "operator-inbox-route"
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
$retryEvents = New-Object System.Collections.Generic.List[object]

$buildStep = Invoke-MeridianStepWithTestHostRetry -Name "Build isolated WPF operator inbox route slice" -Command $buildCommand -LogName "build.log" -SummaryDir $summaryDir -RepoRoot $repoRoot -Steps $steps -RetryEvents $retryEvents

if ($buildStep.exitCode -eq 0) {
    Invoke-MeridianStepWithTestHostRetry -Name "Run operator inbox route test slice" -Command $testCommand -LogName "test.log" -SummaryDir $summaryDir -RepoRoot $repoRoot -Steps $steps -RetryEvents $retryEvents | Out-Null
}

$failedSteps = @($steps | Where-Object { $_.exitCode -ne 0 })
$retryReason = if ($retryEvents.Count -gt 0) { $retryEvents[0].reason } else { $null }
$stoppedTestHostPids = if ($retryEvents.Count -gt 0) { @($retryEvents[0].stoppedTestHostPids) } else { @() }
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

$summaryJsonPath = Join-Path $summaryDir "operator-inbox-route-validation.json"
$summaryMarkdownPath = Join-Path $summaryDir "operator-inbox-route-validation.md"

$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryJsonPath

$markdown = @(
    "# Operator Inbox Route Validation",
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
    throw "Operator inbox route validation failed: $failedStepNames"
}
