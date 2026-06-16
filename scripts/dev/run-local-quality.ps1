[CmdletBinding()]
param(
    [string]$DotnetTestProject = "tests/Meridian.Tests/Meridian.Tests.csproj",

    [string]$DotnetTestFilter = "",

    [switch]$SkipDotnetFormat,

    [switch]$SkipWarningSuppressionCheck,

    [switch]$SkipDashboardTests,

    [switch]$SkipDashboardBuild,

    [switch]$IncludePlaywrightSmoke,

    [switch]$IncludeDocs,

    [switch]$SkipSecretScan
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
Set-Location $RepoRoot

$StartedAt = Get-Date
$Results = New-Object System.Collections.Generic.List[object]

function Write-Step {
    param([string]$Message)
    Write-Host "[local-quality] $Message"
}

function Invoke-QualityStep {
    param(
        [string]$Name,
        [string]$Command,
        [string[]]$Arguments
    )

    Write-Step "Starting $Name"
    $stepStart = Get-Date

    & $Command @Arguments
    $exitCode = if ($LASTEXITCODE -is [int]) { $LASTEXITCODE } else { 0 }
    $duration = [Math]::Round(((Get-Date) - $stepStart).TotalSeconds, 2)

    $Results.Add([pscustomobject]@{
            Name            = $Name
            Command         = "$Command $($Arguments -join ' ')"
            ExitCode        = $exitCode
            DurationSeconds = $duration
        }) | Out-Null

    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode"
    }

    Write-Step "Passed $Name in ${duration}s"
}

function Test-CommandAvailable {
    param([string]$Name)
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

if (-not $SkipDotnetFormat) {
    Invoke-QualityStep `
        -Name "dotnet format verify" `
        -Command "dotnet" `
        -Arguments @("format", "Meridian.sln", "--verify-no-changes", "--verbosity", "minimal", "--no-restore")
}

if (-not $SkipWarningSuppressionCheck) {
    Invoke-QualityStep `
        -Name "warning suppression inventory" `
        -Command "python" `
        -Arguments @("build/scripts/ci/check-warning-suppressions.py")
}

if (-not [string]::IsNullOrWhiteSpace($DotnetTestFilter)) {
    Invoke-QualityStep `
        -Name "focused dotnet test" `
        -Command "dotnet" `
        -Arguments @(
            "test",
            $DotnetTestProject,
            "-c",
            "Release",
            "--no-restore",
            "--filter",
            $DotnetTestFilter,
            "--logger",
            "console;verbosity=normal",
            "/p:EnableWindowsTargeting=true",
            "/p:NodeReuse=false"
        )
}

if (-not $SkipDashboardTests) {
    Invoke-QualityStep `
        -Name "dashboard tests" `
        -Command "npm" `
        -Arguments @("--prefix", "src/Meridian.Ui/dashboard", "run", "test")
}

if (-not $SkipDashboardBuild) {
    Invoke-QualityStep `
        -Name "dashboard build" `
        -Command "npm" `
        -Arguments @("--prefix", "src/Meridian.Ui/dashboard", "run", "build")
}

if ($IncludePlaywrightSmoke) {
    Invoke-QualityStep `
        -Name "browser workstation smoke" `
        -Command "npm" `
        -Arguments @("--prefix", "src/Meridian.Ui/dashboard", "run", "smoke:workstation")
}

if ($IncludeDocs) {
    Invoke-QualityStep `
        -Name "roadmap registry validation" `
        -Command "python" `
        -Arguments @("build/scripts/docs/validate-roadmap-registry.py", "--summary")

    Invoke-QualityStep `
        -Name "source README validation" `
        -Command "python" `
        -Arguments @("build/scripts/docs/validate-source-readmes.py", "--summary")
}

if (-not $SkipSecretScan) {
    if (Test-CommandAvailable "gitleaks") {
        Invoke-QualityStep `
            -Name "secret scan" `
            -Command "gitleaks" `
            -Arguments @("detect", "--source", ".", "--config", ".gitleaks.toml", "--no-banner")
    }
    else {
        Write-Warning "gitleaks is not installed locally; CI still runs gitleaks/gitleaks-action."
    }
}

$durationSeconds = [Math]::Round(((Get-Date) - $StartedAt).TotalSeconds, 2)
Write-Step "Completed local quality lane in ${durationSeconds}s"
$Results | Format-Table -AutoSize
