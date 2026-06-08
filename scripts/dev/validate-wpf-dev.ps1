#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$Framework = "net10.0-windows10.0.19041.0",
    [string]$OutputRoot = "artifacts/wpf-validation/dev-loop",
    [string]$Filter = "FullyQualifiedName~DesktopWorkflowScriptTests",
    [switch]$BuildOnly,
    [switch]$Restore,
    [switch]$NoIsolation,
    [switch]$AllowConcurrentDotnet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

. (Join-Path $PSScriptRoot "SharedBuild.ps1")

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$env:MERIDIAN_REPO_ROOT = $repoRoot
Set-Location $repoRoot

$runStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot
$summaryDir = Join-Path $resolvedOutputRoot $runStamp
$resultsDirectory = Join-Path $summaryDir "TestResults"
New-Item -ItemType Directory -Force -Path $summaryDir, $resultsDirectory | Out-Null

$wpfProject = "src/Meridian.Wpf/Meridian.Wpf.csproj"
$wpfTestsProject = "tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj"
$buildIsolationKey = if ($NoIsolation -or -not $Restore) { "" } else { New-MeridianBuildIsolationKey -Prefix "wpf-dev-test" }

$serializedBuildArgs = @(
    "/m:1",
    "/nr:false",
    "/p:BuildInParallel=false",
    "/p:UseSharedCompilation=false",
    "/p:EnableWindowsTargeting=true",
    "/p:EnableFullWpfBuild=true",
    "/p:WindowsPackageType=None"
)

if (-not [string]::IsNullOrWhiteSpace($buildIsolationKey)) {
    $serializedBuildArgs += "/p:MeridianBuildIsolationKey=$buildIsolationKey"
}

$serializedRestoreArgs = @($serializedBuildArgs)

if (-not [string]::IsNullOrWhiteSpace($Framework)) {
    $serializedBuildArgs += "/p:TargetFramework=$Framework"
}

$steps = New-Object System.Collections.Generic.List[object]
$retryEvents = New-Object System.Collections.Generic.List[object]

function Get-ActiveRepoDotnetProcess {
    if (-not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
        return @()
    }

    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe' OR Name = 'MSBuild.exe' OR Name = 'testhost.exe' OR Name = 'csc.exe' OR Name = 'VBCSCompiler.exe'" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        return @()
    }

    $repoBuildProcessIds = @(
        $processes | Where-Object {
            $_.ProcessId -ne $PID -and
            $_.CommandLine -and
            (
                $_.CommandLine.IndexOf($repoRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                $_.CommandLine.IndexOf("Meridian", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            ) -and
            (
                $_.Name -eq "MSBuild.exe" -or
                $_.Name -eq "testhost.exe" -or
                $_.CommandLine -match '(?i)\bdotnet(\.exe)?"?\s+(build|test|restore|msbuild)\b' -or
                $_.CommandLine.IndexOf("MSBuild.dll", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            )
        } | Select-Object -ExpandProperty ProcessId
    )

    return @(
        $processes | Where-Object {
            $_.ProcessId -ne $PID -and
            (
                $repoBuildProcessIds -contains $_.ProcessId -or
                (($_.Name -eq "csc.exe" -or $_.Name -eq "VBCSCompiler.exe") -and $repoBuildProcessIds -contains $_.ParentProcessId)
            )
        }
    )
}

$activeRepoDotnetProcesses = @(Get-ActiveRepoDotnetProcess)
$blockedByConcurrentDotnet = $activeRepoDotnetProcesses.Count -gt 0 -and -not $AllowConcurrentDotnet

if ($blockedByConcurrentDotnet) {
    $concurrencyLogPath = Join-Path $summaryDir "active-dotnet-processes.log"
    $processLines = @(
        "Active repo-owned build/test/restore/MSBuild or compiler processes were detected before WPF development validation.",
        "Rerun after they exit, or pass -AllowConcurrentDotnet when you intentionally want overlapping builds.",
        ""
    )

    foreach ($process in $activeRepoDotnetProcesses) {
        $processLines += ("PID {0} {1}: {2}" -f $process.ProcessId, $process.Name, $process.CommandLine)
    }

    $processLines | Set-Content -Path $concurrencyLogPath
    Write-Warning "Active repo-owned build/test/restore/MSBuild or compiler processes are already running; skipping WPF validation to avoid shared-output contention."
    foreach ($process in $activeRepoDotnetProcesses) {
        Write-Warning ("PID {0} {1}: {2}" -f $process.ProcessId, $process.Name, $process.CommandLine)
    }

    $steps.Add([ordered]@{
            name = "Check concurrent repo dotnet processes"
            command = "Get-CimInstance Win32_Process"
            exitCode = 1
            durationSeconds = 0
            logPath = $concurrencyLogPath
            tail = ($processLines | Select-Object -Last 25) -join [Environment]::NewLine
        }) | Out-Null
}

if (-not $blockedByConcurrentDotnet) {
    Invoke-MeridianWpfTempProjectCleanup -RepoRoot $repoRoot -WpfProjectPath $wpfProject | Out-Null
}

if (-not $blockedByConcurrentDotnet -and $Restore) {
    $restoreWpfCommand = @("dotnet", "restore", $wpfProject, "--verbosity", "minimal") + $serializedRestoreArgs
    Invoke-MeridianStepWithTestHostRetry `
        -Name "Restore WPF desktop shell" `
        -Command $restoreWpfCommand `
        -LogName "restore-wpf.log" `
        -SummaryDir $summaryDir `
        -RepoRoot $repoRoot `
        -Steps $steps `
        -RetryEvents $retryEvents | Out-Null

    if (-not $BuildOnly) {
        $restoreTestsCommand = @("dotnet", "restore", $wpfTestsProject, "--verbosity", "minimal") + $serializedRestoreArgs
        Invoke-MeridianStepWithTestHostRetry `
            -Name "Restore WPF desktop tests" `
            -Command $restoreTestsCommand `
            -LogName "restore-tests.log" `
            -SummaryDir $summaryDir `
            -RepoRoot $repoRoot `
            -Steps $steps `
            -RetryEvents $retryEvents | Out-Null
    }
}

if (-not $blockedByConcurrentDotnet) {
    $buildWpfCommand = @(
        "dotnet",
        "build",
        $wpfProject,
        "-c", $Configuration,
        "--no-restore",
        "--nologo",
        "--verbosity", "minimal"
    ) + $serializedBuildArgs

    if ([string]::IsNullOrWhiteSpace($buildIsolationKey)) {
        $buildWpfCommand = @(
            "dotnet",
            "build",
            $wpfProject,
            "-c", $Configuration,
            "--no-restore",
            "--no-dependencies",
            "--nologo",
            "--verbosity", "minimal"
        ) + $serializedBuildArgs
    }

    $buildWpfStep = Invoke-MeridianStepWithTestHostRetry `
        -Name "Build WPF desktop shell" `
        -Command $buildWpfCommand `
        -LogName "build-wpf.log" `
        -SummaryDir $summaryDir `
        -RepoRoot $repoRoot `
        -Steps $steps `
        -RetryEvents $retryEvents

    if ($buildWpfStep.exitCode -eq 0 -and -not $BuildOnly) {
        $buildTestsCommand = @(
            "dotnet",
            "build",
            $wpfTestsProject,
            "-c", $Configuration,
            "--no-restore",
            "--nologo",
            "--verbosity", "minimal"
        ) + $serializedBuildArgs

        if ([string]::IsNullOrWhiteSpace($buildIsolationKey)) {
            $buildTestsCommand = @(
                "dotnet",
                "build",
                $wpfTestsProject,
                "-c", $Configuration,
                "--no-restore",
                "--no-dependencies",
                "--nologo",
                "--verbosity", "minimal"
            ) + $serializedBuildArgs
        }

        $buildTestsStep = Invoke-MeridianStepWithTestHostRetry `
            -Name "Build WPF desktop tests" `
            -Command $buildTestsCommand `
            -LogName "build-tests.log" `
            -SummaryDir $summaryDir `
            -RepoRoot $repoRoot `
            -Steps $steps `
            -RetryEvents $retryEvents

        if ($buildTestsStep.exitCode -eq 0) {
            $testCommand = @(
                "dotnet",
                "test",
                $wpfTestsProject,
                "-c", $Configuration,
                "--no-build",
                "--no-restore",
                "--nologo",
                "--verbosity", "minimal",
                "--results-directory", $resultsDirectory
            )

            if (-not [string]::IsNullOrWhiteSpace($Filter)) {
                $testCommand += @("--filter", $Filter)
            }

            $testCommand += $serializedBuildArgs

            Invoke-MeridianStepWithTestHostRetry `
                -Name "Run WPF desktop development tests" `
                -Command $testCommand `
                -LogName "test.log" `
                -SummaryDir $summaryDir `
                -RepoRoot $repoRoot `
                -Steps $steps `
                -RetryEvents $retryEvents | Out-Null
        }
    }
}

$failedSteps = @($steps | Where-Object { $_.exitCode -ne 0 })
$summary = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("O")
    configuration = $Configuration
    framework = $Framework
    repoRoot = $repoRoot
    buildIsolationKey = $buildIsolationKey
    buildOnly = [bool]$BuildOnly
    restore = [bool]$Restore
    allowConcurrentDotnet = [bool]$AllowConcurrentDotnet
    activeRepoDotnetProcesses = @($activeRepoDotnetProcesses | ForEach-Object {
            [ordered]@{
                processId = $_.ProcessId
                name = $_.Name
                commandLine = $_.CommandLine
            }
        })
    filter = $Filter
    outputRoot = $resolvedOutputRoot
    resultsDirectory = $resultsDirectory
    retryEvents = $retryEvents
    result = if ($failedSteps.Count -eq 0) { "passed" } else { "failed" }
    steps = $steps
}

$summaryJsonPath = Join-Path $summaryDir "wpf-dev-test-validation.json"
$summaryMarkdownPath = Join-Path $summaryDir "wpf-dev-test-validation.md"

$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryJsonPath

$markdown = @(
    "# WPF Development Validation",
    "",
    "- Generated: $($summary.generatedAtUtc)",
    "- Configuration: $Configuration",
    "- Framework: $Framework",
    "- Build isolation key: $(if ([string]::IsNullOrWhiteSpace($buildIsolationKey)) { 'disabled' } else { $buildIsolationKey })",
    "- Build only: $([bool]$BuildOnly)",
    "- Restore first: $([bool]$Restore)",
    "- Allow concurrent dotnet: $([bool]$AllowConcurrentDotnet)",
    ('- Test filter: `{0}`' -f $(if ([string]::IsNullOrWhiteSpace($Filter)) { '<none>' } else { $Filter })),
    ('- Results directory: `{0}`' -f $resultsDirectory),
    "- Overall result: $($summary.result)",
    ""
)

if ($retryEvents.Count -gt 0) {
    $markdown += "## Retry Events"
    foreach ($retryEvent in $retryEvents) {
        $markdown += "- $($retryEvent.step): stopped repo-owned testhost PIDs $($retryEvent.stoppedTestHostPids -join ', ')"
    }

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
    throw "WPF development validation failed: $failedStepNames"
}

