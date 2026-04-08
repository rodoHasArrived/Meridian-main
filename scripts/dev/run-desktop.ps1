#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Fixture,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DesktopArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
Set-Location $repoRoot

$hostProject = 'src/Meridian/Meridian.csproj'
$desktopProject = 'src/Meridian.Wpf/Meridian.Wpf.csproj'
$hostExe = Join-Path $repoRoot 'src\Meridian\bin\Debug\net9.0\Meridian.exe'
$desktopExe = Join-Path $repoRoot 'src\Meridian.Wpf\bin\Debug\net9.0-windows10.0.19041.0\Meridian.Desktop.exe'
$artifactsDir = Join-Path $repoRoot 'artifacts'
$hostStdout = Join-Path $artifactsDir 'desktop-launcher-host.stdout.log'
$hostStderr = Join-Path $artifactsDir 'desktop-launcher-host.stderr.log'
$desktopStdout = Join-Path $artifactsDir 'desktop-launcher.stdout.log'
$desktopStderr = Join-Path $artifactsDir 'desktop-launcher.stderr.log'
$hostProcess = $null
$hostOwned = $false
$desktopProcess = $null
$desktopAlreadyRunning = $false

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Write-Ok([string]$Message) { Write-Host "[OK]   $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }

function Test-HealthyHost {
    try {
        $response = Invoke-WebRequest -Uri 'http://localhost:8080/healthz' -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 300
    }
    catch {
        return $false
    }
}

function Show-HostLogs {
    if (Test-Path $hostStderr) {
        $stderr = Get-Content $hostStderr | Out-String
        if (-not [string]::IsNullOrWhiteSpace($stderr)) {
            Write-Host $stderr.TrimEnd()
        }
    }

    if (Test-Path $hostStdout) {
        $stdout = Get-Content $hostStdout | Out-String
        if (-not [string]::IsNullOrWhiteSpace($stdout)) {
            Write-Host $stdout.TrimEnd()
        }
    }
}

function Stop-OwnedHost {
    if (-not $hostOwned -or $null -eq $hostProcess) {
        return
    }

    try {
        if (-not $hostProcess.HasExited) {
            Write-Info "Stopping local Meridian host..."
            Stop-Process -Id $hostProcess.Id -Force
            $hostProcess.WaitForExit()
            Write-Ok "Local Meridian host stopped"
        }
    }
    catch {
        Write-Warn "Failed to stop the local Meridian host cleanly: $($_.Exception.Message)"
    }
}

try {
    if (-not $IsWindows -and $env:OS -ne 'Windows_NT') {
        throw 'The desktop launcher requires Windows because Meridian.Wpf is a Windows-only application.'
    }

    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    Remove-Item $hostStdout, $hostStderr, $desktopStdout, $desktopStderr -ErrorAction SilentlyContinue

    if (-not $NoBuild) {
        Write-Info 'Building Meridian host...'
        & dotnet build $hostProject -c Debug -v minimal -nologo /p:EnableWindowsTargeting=true
        if ($LASTEXITCODE -ne 0) {
            throw 'Meridian host build failed.'
        }

        Write-Info 'Building Meridian desktop shell...'
        & dotnet build $desktopProject -c Debug -v minimal -nologo /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true
        if ($LASTEXITCODE -ne 0) {
            throw 'Meridian desktop build failed.'
        }
    }

    if (-not (Test-Path $hostExe)) {
        throw "Host executable not found at '$hostExe'."
    }

    if (-not (Test-Path $desktopExe)) {
        throw "Desktop executable not found at '$desktopExe'."
    }

    $desktopAlreadyRunning = $null -ne (Get-Process -Name 'Meridian.Desktop' -ErrorAction SilentlyContinue | Select-Object -First 1)

    if (Test-HealthyHost) {
        Write-Ok 'Reusing existing local Meridian host on http://localhost:8080'
    }
    else {
        Write-Info 'Starting local Meridian host on http://localhost:8080...'
        $hostProcess = Start-Process -FilePath $hostExe `
            -ArgumentList @('--mode', 'web', '--http-port', '8080') `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $hostStdout `
            -RedirectStandardError $hostStderr `
            -PassThru
        $hostOwned = $true

        $healthy = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            if ($hostProcess.HasExited) {
                break
            }

            if (Test-HealthyHost) {
                $healthy = $true
                break
            }

            Start-Sleep -Seconds 1
        }

        if (-not $healthy) {
            Show-HostLogs
            throw 'Local Meridian host failed to become healthy on http://localhost:8080.'
        }

        Write-Ok 'Local Meridian host is healthy'
    }

    $desktopLaunchArgs = @()
    if ($Fixture) {
        $desktopLaunchArgs += '--fixture'
    }

    if ($DesktopArgs.Count -gt 0) {
        $desktopLaunchArgs += $DesktopArgs
    }

    Write-Info 'Launching Meridian desktop shell...'
    $desktopProcess = Start-Process -FilePath $desktopExe `
        -ArgumentList $desktopLaunchArgs `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $desktopStdout `
        -RedirectStandardError $desktopStderr `
        -PassThru

    $desktopProcess.WaitForExit()

    if ($desktopProcess.ExitCode -ne 0) {
        if (Test-Path $desktopStderr) {
            Get-Content $desktopStderr
        }

        throw "Meridian desktop exited with code $($desktopProcess.ExitCode)."
    }

    Write-Ok 'Meridian desktop exited cleanly'

    if ($desktopAlreadyRunning -and $hostOwned) {
        Write-Info 'Leaving the local Meridian host running because the desktop app was already open.'
        $hostOwned = $false
    }
}
finally {
    Stop-OwnedHost
}
