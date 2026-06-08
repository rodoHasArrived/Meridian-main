#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [ValidateSet('Development', 'Production')]
    [string]$LaunchMode = 'Development',
    [string]$Profile = '',
    [string]$ProfileRoot = 'scripts/dev/workflow-profiles',
    [switch]$NoBuild,
    [switch]$BuildOnly,
    [switch]$Fixture,
    [switch]$StartupSmoke,
    [int]$StartupSmokeTimeoutSec = 45,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$DesktopArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'SharedBuild.ps1')
. (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')

$Profile = if ([string]::IsNullOrWhiteSpace($Profile)) {
    if ($LaunchMode -eq 'Production') { 'desktop-production' } else { 'desktop-development' }
}
else {
    $Profile
}

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
$desktopFramework = [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'framework' -Fallback 'net10.0-windows10.0.19041.0')
$desktopExeName = [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'exeName' -Fallback 'Meridian.Desktop.exe')
$hostConfiguration = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'configuration' -Fallback $desktopConfiguration)
$hostBaseUrl = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'baseUrl' -Fallback 'http://localhost:8080')
$hostHealthPath = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'healthPath' -Fallback '/healthz')
$hostStartupTimeoutSec = [int](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'startupTimeoutSec' -Fallback 30)
$hostMode = [string](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'mode' -Fallback 'desktop')
$hostPort = [int](Get-MeridianWorkflowProfileValue -Table $hostProfile -Key 'port' -Fallback 8080)
$fixtureRequired = [bool](Get-MeridianWorkflowProfileValue -Table $fixtureProfile -Key 'required' -Fallback $false)
$buildIsolationKey = if ($NoBuild) { '' } else { New-MeridianBuildIsolationKey -Prefix 'desktop-run' }
$hostExe = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath $hostProject -Configuration $hostConfiguration -Framework 'net10.0' -BinaryName 'Meridian.exe' -IsolationKey $buildIsolationKey
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
$hostShutdownToken = [System.Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$originalLaunchEnv = @{
    MDC_DATASOURCE = $env:MDC_DATASOURCE
    MDC_SYNTHETIC_MODE = $env:MDC_SYNTHETIC_MODE
    MDC_FIXTURE_MODE = $env:MDC_FIXTURE_MODE
    MDC_SHUTDOWN_TOKEN = $env:MDC_SHUTDOWN_TOKEN
    MERIDIAN_USE_INMEMORY_GOVERNANCE = $env:MERIDIAN_USE_INMEMORY_GOVERNANCE
    DOTNET_ENVIRONMENT = $env:DOTNET_ENVIRONMENT
    ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
}

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Write-Ok([string]$Message) { Write-Host "[OK]   $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Step([string]$Message) { Write-Host "`n[STEP] === $Message ===" -ForegroundColor Cyan }
function Write-Fail([string]$Message) { Write-Host "[FAIL] $Message" -ForegroundColor Red }

function Set-LauncherEnvironmentVariable {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()]
        [string]$Value
    )

    if ($null -eq $Value) {
        Remove-Item "Env:$Name" -ErrorAction SilentlyContinue
    }
    else {
        Set-Item "Env:$Name" -Value $Value
    }
}

function Apply-DesktopLaunchMode {
    if ($LaunchMode -eq 'Development') {
        Write-Info 'Development launch mode enabled; using Development environment and local in-memory governance services.'
        Set-LauncherEnvironmentVariable -Name 'DOTNET_ENVIRONMENT' -Value 'Development'
        Set-LauncherEnvironmentVariable -Name 'ASPNETCORE_ENVIRONMENT' -Value 'Development'
        Set-LauncherEnvironmentVariable -Name 'MERIDIAN_USE_INMEMORY_GOVERNANCE' -Value 'true'
        return
    }

    Write-Info 'Production launch mode enabled; requiring persistence-backed governance services.'
    Set-LauncherEnvironmentVariable -Name 'DOTNET_ENVIRONMENT' -Value 'Production'
    Set-LauncherEnvironmentVariable -Name 'ASPNETCORE_ENVIRONMENT' -Value 'Production'
    Set-LauncherEnvironmentVariable -Name 'MERIDIAN_USE_INMEMORY_GOVERNANCE' -Value $null
}

function Assert-ProductionGovernanceConfiguration {
    $requiredVariables = @(
        'MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING',
        'MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING'
    )

    $missing = @($requiredVariables | Where-Object {
            [string]::IsNullOrWhiteSpace([System.Environment]::GetEnvironmentVariable($_))
        })

    if ($missing.Count -gt 0) {
        $joined = $missing -join ', '
        throw "Production desktop launch requires persistence-backed governance services. Configure $joined, or rerun with -LaunchMode Development for the explicit local/dev in-memory profile."
    }

    Write-Ok 'Production governance persistence configuration is present.'
}

function Wait-ForDesktopWindow {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]
        [int]$TimeoutSec
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) {
            throw "Meridian desktop exited before a window was detected (exit code $($Process.ExitCode))."
        }

        try {
            $Process.Refresh()
            if ($Process.MainWindowHandle -ne [System.IntPtr]::Zero) {
                return
            }
        }
        catch {
            # Ignore transient startup state while WPF is initializing the shell window.
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Meridian desktop window did not appear within $TimeoutSec seconds."
}

function Stop-DesktopProcessAfterSmoke {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    if ($Process.HasExited) {
        return
    }

    try {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [System.IntPtr]::Zero -and $Process.CloseMainWindow()) {
            if ($Process.WaitForExit(10000)) {
                Write-Ok 'Meridian desktop startup smoke closed the shell cleanly'
                return
            }
        }

        Stop-Process -Id $Process.Id -Force
        $Process.WaitForExit()
        Write-Warn 'Meridian desktop startup smoke had to force-close the shell after launch confirmation.'
    }
    catch {
        Write-Warn "Failed to stop the smoke-test desktop shell cleanly: $($_.Exception.Message)"
    }
}

function Stop-OwnedDesktopProcessSafely {
    if ($null -eq $desktopProcess) {
        return
    }

    try {
        if ($desktopProcess.HasExited) {
            return
        }

        $desktopProcess.Refresh()
        if ($desktopProcess.MainWindowHandle -ne [System.IntPtr]::Zero -and $desktopProcess.CloseMainWindow()) {
            if ($desktopProcess.WaitForExit(10000)) {
                Write-Ok "Owned Meridian desktop process $($desktopProcess.Id) exited cleanly"
                return
            }
        }

        Write-Warn "Owned Meridian desktop process $($desktopProcess.Id) did not exit after close request; terminating it."
        Stop-Process -Id $desktopProcess.Id -Force
        $desktopProcess.WaitForExit()
    }
    catch {
        Write-Warn "Failed to stop the owned Meridian desktop process cleanly: $($_.Exception.Message)"
    }
}

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
            $shutdownUri = "http://localhost:$hostPort/api/system/shutdown"
            $gracefulRequested = $false
            try {
                Invoke-WebRequest `
                    -Uri $shutdownUri `
                    -Method Post `
                    -UseBasicParsing `
                    -TimeoutSec 5 `
                    -Headers @{ "X-Meridian-Shutdown-Token" = $hostShutdownToken } | Out-Null
                $gracefulRequested = $true
            }
            catch {
                Write-Warn "Local Meridian host graceful shutdown request failed: $($_.Exception.Message)"
            }

            if ($gracefulRequested -and $hostProcess.WaitForExit(15000)) {
                Write-Ok "Local Meridian host stopped gracefully"
                return
            }

            Write-Warn "Local Meridian host did not exit after graceful shutdown; terminating owned process $($hostProcess.Id)."
            Stop-Process -Id $hostProcess.Id -Force
            $hostProcess.WaitForExit()
            Write-Ok "Local Meridian host terminated"
        }
    }
    catch {
        Write-Warn "Failed to stop the local Meridian host cleanly: $($_.Exception.Message)"
    }
}

function Test-SufficientDiskSpaceForBuild {
    $warnThresholdGb = 2.0
    $blockThresholdGb = 0.5

    $pathsToCheck = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:TEMP)) { $pathsToCheck.Add($env:TEMP) }
    $nugetCache = Join-Path ([System.Environment]::GetFolderPath('UserProfile')) '.nuget'
    if (Test-Path $nugetCache) { $pathsToCheck.Add($nugetCache) }
    $pathsToCheck.Add($repoRoot)

    foreach ($checkPath in $pathsToCheck) {
        $root = $null
        try { $root = [System.IO.Path]::GetPathRoot($checkPath) } catch { continue }
        if ([string]::IsNullOrWhiteSpace($root)) { continue }

        $drive = $null
        try { $drive = [System.IO.DriveInfo]::new($root) } catch { continue }
        if ($null -eq $drive -or -not $drive.IsReady) { continue }

        $freeGb = $drive.AvailableFreeSpace / 1GB
        if ($freeGb -lt $blockThresholdGb) {
            Write-Fail ("Disk space critically low on drive {0}: {1:0.00} GB free." -f $drive.Name, $freeGb)
            Write-Warn 'Free up disk space before retrying. To clear the NuGet package cache, run:'
            Write-Warn '  dotnet nuget locals all --clear'
            throw ("Insufficient disk space on drive {0} ({1:0.00} GB free). See suggestions above." -f $drive.Name, $freeGb)
        }
        elseif ($freeGb -lt $warnThresholdGb) {
            Write-Warn ("Low disk space on drive {0}: {1:0.00} GB free — NuGet restore may fail." -f $drive.Name, $freeGb)
            Write-Warn 'Consider clearing the NuGet cache to free space: dotnet nuget locals all --clear'
        }
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

    if ($LaunchMode -eq 'Production' -and $Fixture) {
        throw 'Production launch mode cannot run with -Fixture. Use -LaunchMode Development for deterministic fixture runs.'
    }

    Apply-DesktopLaunchMode

    if ($Fixture) {
        Write-Info 'Fixture mode enabled; forcing synthetic backend overrides for deterministic local startup.'
        $env:MDC_DATASOURCE = 'Synthetic'
        $env:MDC_SYNTHETIC_MODE = '1'
        $env:MDC_FIXTURE_MODE = '1'
    }

    Write-Step 'Meridian desktop launcher'
    Write-Info "Launch mode   : $LaunchMode"
    Write-Info "Profile       : $Profile"
    Write-Info "Fixture mode  : $Fixture"
    Write-Info "Build         : $(-not $NoBuild)"
    Write-Info "Build only    : $BuildOnly"
    Write-Info "Startup smoke : $StartupSmoke"
    Write-Info "Host URL      : $hostBaseUrl"
    Write-Info "Host          : Meridian.exe ($hostConfiguration / net10.0)"
    Write-Info "Desktop       : $desktopExeName ($desktopConfiguration / $desktopFramework)"
    Write-Info "Artifacts     : $artifactsDir"

    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
    Remove-Item $hostStdout, $hostStderr, $desktopStdout, $desktopStderr -ErrorAction SilentlyContinue

    $desktopAlreadyRunning = @(Get-WorkspaceDesktopProcesses).Count -gt 0

    if (-not $NoBuild) {
        if ($desktopAlreadyRunning) {
            Stop-WorkspaceDesktopProcesses
        }

        Write-Step 'Build'
        $activeRepoBuildProcesses = @(Get-MeridianRepoOwnedBuildProcesses -RepoRoot $repoRoot)
        if ($activeRepoBuildProcesses.Count -gt 0) {
            foreach ($process in $activeRepoBuildProcesses) {
                Write-Warn ("PID {0} {1}: {2}" -f $process.ProcessId, $process.Name, $process.CommandLine)
            }

            throw 'Active repo-owned build/test/restore/MSBuild or compiler processes were detected. Stop them before launching a desktop build to avoid WPF temporary-project contention.'
        }

        Invoke-MeridianWpfTempProjectCleanup -RepoRoot $repoRoot -WpfProjectPath $desktopProject | Out-Null
        Test-SufficientDiskSpaceForBuild

        Write-Info 'Restoring Meridian host packages...'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & dotnet restore $hostProject -v minimal @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey
        )
        $sw.Stop()
        if ($LASTEXITCODE -ne 0) {
            Write-Fail 'Meridian host restore failed.'
            Write-Warn 'If the error mentions disk space or a corrupt archive, run: dotnet nuget locals all --clear'
            throw 'Meridian host restore failed.'
        }
        Write-Ok ("Host packages restored ({0:0.0}s)" -f $sw.Elapsed.TotalSeconds)

        Write-Info 'Building Meridian host...'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & dotnet build $hostProject -c $hostConfiguration -v minimal -nologo --no-restore @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey
        )
        $sw.Stop()
        if ($LASTEXITCODE -ne 0) {
            Write-Fail 'Meridian host build failed.'
            throw 'Meridian host build failed.'
        }
        Write-Ok ("Host built ({0:0.0}s)" -f $sw.Elapsed.TotalSeconds)

        Write-Info 'Restoring Meridian desktop shell packages...'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & dotnet restore $desktopProject -v minimal @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -EnableFullWpfBuild
        )
        $sw.Stop()
        if ($LASTEXITCODE -ne 0) {
            Write-Fail 'Meridian desktop restore failed.'
            Write-Warn 'If the error mentions disk space or a corrupt archive, run: dotnet nuget locals all --clear'
            throw 'Meridian desktop restore failed.'
        }
        Write-Ok ("Desktop packages restored ({0:0.0}s)" -f $sw.Elapsed.TotalSeconds)

        Write-Info 'Building Meridian desktop shell...'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        & dotnet build $desktopProject -c $desktopConfiguration -v minimal -nologo --no-restore @(
            Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -TargetFramework $desktopFramework -EnableFullWpfBuild
        )
        $sw.Stop()
        if ($LASTEXITCODE -ne 0) {
            Write-Fail 'Meridian desktop build failed.'
            throw 'Meridian desktop build failed.'
        }
        Write-Ok ("Desktop shell built ({0:0.0}s)" -f $sw.Elapsed.TotalSeconds)
    }
    else {
        Write-Info 'Skipping build step (-NoBuild).'
    }

    if (-not (Test-Path $hostExe)) {
        throw "Host executable not found at '$hostExe'."
    }

    if (-not (Test-Path $desktopExe)) {
        throw "Desktop executable not found at '$desktopExe'."
    }

    if ($BuildOnly) {
        Write-Ok 'Desktop build completed; skipping host and shell launch because -BuildOnly was supplied.'
        return
    }

    Write-Step 'Start host'
    if ($LaunchMode -eq 'Production') {
        Assert-ProductionGovernanceConfiguration
    }

    if (Test-HealthyHost) {
        Write-Ok "Reusing existing local Meridian host on $hostBaseUrl"
    }
    else {
        Write-Info "Starting local Meridian host on $hostBaseUrl..."
        $env:MDC_SHUTDOWN_TOKEN = $hostShutdownToken
        $hostProcess = Start-Process -FilePath $hostExe `
            -ArgumentList @('--mode', $hostMode, '--http-port', "$hostPort") `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $hostStdout `
            -RedirectStandardError $hostStderr `
            -WindowStyle Hidden `
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

            $pct = [int](($attempt / $hostStartupTimeoutSec) * 100)
            Write-Progress -Activity 'Waiting for Meridian host to become healthy' `
                -Status "$attempt / $hostStartupTimeoutSec s elapsed" `
                -PercentComplete $pct
            Start-Sleep -Seconds 1
        }
        Write-Progress -Activity 'Waiting for Meridian host to become healthy' -Completed

        if (-not $healthy) {
            Write-Fail "Meridian host did not become healthy within ${hostStartupTimeoutSec}s."
            Write-Warn "Host stdout log : $hostStdout"
            Write-Warn "Host stderr log : $hostStderr"
            Show-HostLogs
            throw "Local Meridian host failed to become healthy on $hostBaseUrl."
        }

        Write-Ok 'Local Meridian host is healthy'
    }

    Write-Step 'Launch shell'
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

    if ($StartupSmoke) {
        Write-Step 'Startup smoke'

        try {
            Wait-ForDesktopWindow -Process $desktopProcess -TimeoutSec $StartupSmokeTimeoutSec
            Write-Ok 'Meridian desktop window became available'
        }
        catch {
            if (Test-Path $desktopStderr) {
                $hasStderr = $false
                Get-Content -Path $desktopStderr | ForEach-Object {
                    $hasStderr = $true
                    Write-Host $_
                }
                if (-not $hasStderr) {
                    Write-Verbose "Desktop stderr log was empty: $desktopStderr"
                }
            }

            Write-Fail $_.Exception.Message
            Write-Warn "Desktop stdout log : $desktopStdout"
            Write-Warn "Desktop stderr log : $desktopStderr"
            throw
        }

        Stop-DesktopProcessAfterSmoke -Process $desktopProcess
        Write-Ok 'Meridian desktop startup smoke completed successfully'
        return
    }

    $desktopProcess.WaitForExit()

    if ($desktopProcess.ExitCode -ne 0) {
        if (Test-Path $desktopStderr) {
            $hasStderr = $false
            Get-Content -Path $desktopStderr | ForEach-Object {
                $hasStderr = $true
                Write-Host $_
            }
            if (-not $hasStderr) {
                Write-Verbose "Desktop stderr log was empty: $desktopStderr"
            }
        }

        Write-Fail "Meridian desktop exited with code $($desktopProcess.ExitCode)."
        Write-Warn "Desktop stdout log : $desktopStdout"
        Write-Warn "Desktop stderr log : $desktopStderr"
        throw "Meridian desktop exited with code $($desktopProcess.ExitCode)."
    }

    Write-Ok 'Meridian desktop exited cleanly'
}
finally {
    Stop-OwnedDesktopProcessSafely
    Stop-OwnedHost

    foreach ($entry in $originalLaunchEnv.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            Remove-Item "Env:$($entry.Key)" -ErrorAction SilentlyContinue
        }
        else {
            Set-Item "Env:$($entry.Key)" -Value $entry.Value
        }
    }
}
