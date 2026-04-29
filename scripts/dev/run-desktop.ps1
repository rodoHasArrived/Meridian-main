#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$Profile = 'screenshot-catalog',
    [string]$ProfileRoot = 'scripts/dev/workflow-profiles',
    [switch]$NoBuild,
    [switch]$Fixture,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DesktopArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'SharedBuild.ps1')
. (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')

$profileEnvelope = Get-MeridianWorkflowProfile -RepoRoot $repoRoot -ProfileName $Profile -ProfileRoot $ProfileRoot
$profileValidation = Test-MeridianWorkflowProfile -ProfileData $profileEnvelope.data
if (-not $profileValidation.isValid) {
    throw "Profile '$Profile' failed validation: $($profileValidation.errors -join '; ')"
}

$buildProfile = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'build' -Fallback @{}
$hostProfile = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'host' -Fallback @{}
$fixtureProfile = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'fixture' -Fallback @{}

$hostProject = 'src/Meridian/Meridian.csproj'
$desktopProject = [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'projectPath' -Fallback 'src/Meridian.Wpf/Meridian.Wpf.csproj')
$desktopConfiguration = [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'configuration' -Fallback 'Debug')
$desktopFramework = [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'framework' -Fallback 'net9.0-windows10.0.19041.0')
$desktopExeName = [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'exeName' -Fallback 'Meridian.Desktop.exe')
$hostBaseUrl = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'baseUrl' -Fallback 'http://localhost:8080')
$hostHealthPath = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'healthPath' -Fallback '/healthz')
$hostStartupTimeoutSec = [int](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'startupTimeoutSec' -Fallback 30)
$hostMode = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'mode' -Fallback 'desktop')
$hostPort = [int](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'port' -Fallback 8080)
$fixtureRequired = [bool](Get-MeridianWorkflowProfileValue -Table $fixtureProfile -Key 'required' -Fallback $false)
$buildIsolationKey = New-MeridianBuildIsolationKey -Prefix 'desktop-run'
$hostExe = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath $hostProject -Configuration 'Debug' -Framework 'net9.0' -BinaryName 'Meridian.exe' -IsolationKey $buildIsolationKey
$desktopExe = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath $desktopProject -Configuration $desktopConfiguration -Framework $desktopFramework -BinaryName $desktopExeName -IsolationKey $buildIsolationKey
$artifactsDir = Join-Path $repoRoot 'artifacts'
$hostStdout = Join-Path $artifactsDir 'desktop-launcher-host.stdout.log'
$hostStderr = Join-Path $artifactsDir 'desktop-launcher-host.stderr.log'
$desktopStdout = Join-Path $artifactsDir 'desktop-launcher.stdout.log'
$desktopStderr = Join-Path $artifactsDir 'desktop-launcher.stderr.log'
$hostProcess = $null
$hostOwned = $false
$desktopProcess = $null
$desktopAlreadyRunning = $false
$originalFixtureEnv = @{
    MDC_DATASOURCE = $env:MDC_DATASOURCE
    MDC_SYNTHETIC_MODE = $env:MDC_SYNTHETIC_MODE
    MDC_FIXTURE_MODE = $env:MDC_FIXTURE_MODE
}

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Write-Ok([string]$Message) { Write-Host "[OK]   $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }

function Get-WorkspaceDesktopProcesses {
    $expectedPath = [System.IO.Path]::GetFullPath($desktopExe)

    return @(Get-Process -Name 'Meridian.Desktop' -ErrorAction SilentlyContinue | Where-Object {
            try {
                $processPath = $_.Path
                if ([string]::IsNullOrWhiteSpace($processPath)) {
                    return $false
                }

                return [string]::Equals(
                    [System.IO.Path]::GetFullPath($processPath),
                    $expectedPath,
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                return $false
            }
        })
}

function Stop-WorkspaceDesktopProcesses {
    $runningProcesses = @(Get-WorkspaceDesktopProcesses)
    if ($runningProcesses.Count -eq 0) {
        return
    }

    Write-Info "Stopping $($runningProcesses.Count) running Meridian desktop instance(s) from this workspace so the build can update locked binaries..."

    foreach ($process in $runningProcesses) {
        try {
            if ($process.HasExited) {
                continue
            }

            $closed = $false
            if ($process.MainWindowHandle -ne 0) {
                $closed = $process.CloseMainWindow()
            }

            if ($closed -and $process.WaitForExit(5000)) {
                Write-Ok "Stopped Meridian desktop process $($process.Id)"
                continue
            }

            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
            Write-Ok "Stopped Meridian desktop process $($process.Id)"
        }
        catch {
            throw "Failed to stop running Meridian desktop process $($process.Id): $($_.Exception.Message)"
        }
    }
}

function Test-HealthyHost {
    try {
        $healthUri = ($hostBaseUrl.TrimEnd('/')) + $hostHealthPath
        $response = Invoke-WebRequest -Uri $healthUri -UseBasicParsing -TimeoutSec 2
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

    if ($fixtureRequired -and -not $Fixture) {
        $Fixture = $true
        Write-Info "Profile '$Profile' requires fixture mode; enabling -Fixture."
    }

    if ($Fixture) {
        Write-Info 'Fixture mode enabled; forcing synthetic backend overrides for deterministic local startup.'
        $env:MDC_DATASOURCE = 'Synthetic'
        $env:MDC_SYNTHETIC_MODE = '1'
        $env:MDC_FIXTURE_MODE = '1'
    }

    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    Remove-Item $hostStdout, $hostStderr, $desktopStdout, $desktopStderr -ErrorAction SilentlyContinue

    $desktopAlreadyRunning = @(Get-WorkspaceDesktopProcesses).Count -gt 0

    if (-not $NoBuild) {
        if ($desktopAlreadyRunning) {
            Stop-WorkspaceDesktopProcesses
        }

        Write-Info 'Building Meridian host...'
        & dotnet restore $hostProject -v minimal @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey
        )
        if ($LASTEXITCODE -ne 0) {
            throw 'Meridian host restore failed.'
        }

        & dotnet build $hostProject -c Debug -v minimal -nologo --no-restore @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey
        )
        if ($LASTEXITCODE -ne 0) {
            throw 'Meridian host build failed.'
        }

        Write-Info 'Building Meridian desktop shell...'
        & dotnet restore $desktopProject -v minimal @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -EnableFullWpfBuild
        )
        if ($LASTEXITCODE -ne 0) {
            throw 'Meridian desktop restore failed.'
        }

        & dotnet build $desktopProject -c $desktopConfiguration -v minimal -nologo --no-restore @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -TargetFramework $desktopFramework -EnableFullWpfBuild
        )
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

    if (Test-HealthyHost) {
        Write-Ok "Reusing existing local Meridian host on $hostBaseUrl"
    }
    else {
        Write-Info "Starting local Meridian host on $hostBaseUrl..."
        $hostProcess = Start-Process -FilePath $hostExe `
            -ArgumentList @('--mode', $hostMode, '--http-port', "$hostPort") `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $hostStdout `
            -RedirectStandardError $hostStderr `
            -PassThru
        $hostOwned = $true

        $healthy = $false
        for ($attempt = 0; $attempt -lt $hostStartupTimeoutSec; $attempt++) {
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
            throw "Local Meridian host failed to become healthy on $hostBaseUrl."
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
}
finally {
    Stop-OwnedHost

    foreach ($entry in $originalFixtureEnv.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            Remove-Item "Env:$($entry.Key)" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item "Env:$($entry.Key)" -Value $entry.Value
        }
    }
}
