#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Workflow,
    [string]$DefinitionPath = 'scripts/dev/desktop-workflows.json',
    [string]$ProjectPath,
    [string]$Configuration,
    [string]$Framework,
    [string]$ExeName,
    [string]$OutputRoot,
    [string]$ScreenshotDirectory,
    [switch]$SkipBuild,
    [switch]$KeepAppOpen,
    [switch]$ReuseExistingApp,
    [switch]$NoFixture,
    [switch]$PassThru,
    [int]$LaunchTimeoutSec = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repoRoot

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Write-Ok([string]$Message) { Write-Host "[ OK ] $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Resolve-RepoPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $repoRoot
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-ConfigValue {
    param(
        [System.Collections.IDictionary]$Table,
        [string]$Key,
        $Fallback = $null
    )

    if ($null -ne $Table -and $Table.Contains($Key) -and $null -ne $Table[$Key] -and "$($Table[$Key])" -ne '') {
        return $Table[$Key]
    }

    return $Fallback
}

function Get-ConfigBool {
    param(
        [System.Collections.IDictionary]$Table,
        [string]$Key,
        [bool]$Fallback
    )

    if ($null -ne $Table -and $Table.Contains($Key)) {
        return [bool]$Table[$Key]
    }

    return $Fallback
}

function Find-MeridianWindow {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $all = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition
    )

    foreach ($window in $all) {
        try {
            if ($window.Current.Name -match 'Meridian') {
                return $window
            }
        }
        catch {
            # Ignore transient UIA failures while windows are initializing.
        }
    }

    return $null
}

function Wait-MeridianWindow {
    param(
        [int]$TimeoutSec,
        [System.Diagnostics.Process]$Process
    )

    for ($attempt = 0; $attempt -lt ($TimeoutSec * 2); $attempt++) {
        Start-Sleep -Milliseconds 500

        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before a window was detected (exit code $($Process.ExitCode))."
        }

        $window = Find-MeridianWindow
        if ($null -ne $window) {
            return $window
        }
    }

    throw "Meridian window did not appear within $TimeoutSec seconds."
}

function Save-WindowCapture {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $rect = $Window.Current.BoundingRectangle
    if ($rect.Width -lt 200 -or $rect.Height -lt 200) {
        throw "Window bounds are too small to capture ($($rect.Width)x$($rect.Height))."
    }

    $bitmap = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.CopyFromScreen(
            [int]$rect.X,
            [int]$rect.Y,
            0,
            0,
            [System.Drawing.Size]::new([int]$rect.Width, [int]$rect.Height)
        )
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        return $Path
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Invoke-ForwardedLaunch {
    param(
        [string]$ExePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory
    )

    $process = Start-Process -FilePath $ExePath -ArgumentList $Arguments -WorkingDirectory $WorkingDirectory -PassThru -WindowStyle Hidden
    $null = $process.WaitForExit(10000)

    if (-not $process.HasExited) {
        Write-Warn "Secondary desktop launcher did not exit within 10 seconds for args: $($Arguments -join ' ')"
        return
    }

    if ($process.ExitCode -ne 0) {
        Write-Warn "Secondary desktop launcher returned exit code $($process.ExitCode) for args: $($Arguments -join ' ')"
    }
}

function Send-WindowKeys {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window,

        [Parameter(Mandatory = $true)]
        [string]$Keys
    )

    $Window.SetFocus()
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

Assert-Command -Name 'dotnet'

if (-not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
    throw 'Desktop workflow automation requires Windows because Meridian.Wpf is a Windows-only application.'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationClient')
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationTypes')

$catalogPath = Resolve-RepoPath $DefinitionPath
if (-not (Test-Path -LiteralPath $catalogPath)) {
    throw "Workflow catalog was not found at '$catalogPath'."
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
$defaults = Get-ConfigValue -Table $catalog -Key 'defaults' -Fallback @{}
$workflowDefinition = @($catalog.workflows | Where-Object { $_.name -eq $Workflow }) | Select-Object -First 1

if ($null -eq $workflowDefinition) {
    throw "Workflow '$Workflow' was not found in '$catalogPath'."
}

$projectPathInput = if ($PSBoundParameters.ContainsKey('ProjectPath')) { $ProjectPath } else { Get-ConfigValue -Table $defaults -Key 'projectPath' -Fallback 'src/Meridian.Wpf/Meridian.Wpf.csproj' }
$resolvedProjectPath = Resolve-RepoPath $projectPathInput
$resolvedConfiguration = if ($PSBoundParameters.ContainsKey('Configuration')) { $Configuration } else { [string](Get-ConfigValue -Table $defaults -Key 'configuration' -Fallback 'Release') }
$resolvedFramework = if ($PSBoundParameters.ContainsKey('Framework')) { $Framework } else { [string](Get-ConfigValue -Table $defaults -Key 'framework' -Fallback 'net9.0-windows10.0.19041.0') }
$resolvedExeName = if ($PSBoundParameters.ContainsKey('ExeName')) { $ExeName } else { [string](Get-ConfigValue -Table $defaults -Key 'exeName' -Fallback 'Meridian.Desktop.exe') }
$outputRootInput = if ($PSBoundParameters.ContainsKey('OutputRoot')) { $OutputRoot } else { [string](Get-ConfigValue -Table $defaults -Key 'outputRoot' -Fallback 'artifacts/desktop-workflows') }
$resolvedOutputRoot = Resolve-RepoPath $outputRootInput
$resolvedScreenshotDirectory = if ($PSBoundParameters.ContainsKey('ScreenshotDirectory')) {
    Resolve-RepoPath $ScreenshotDirectory
}
else {
    $null
}

$settleMs = [int](Get-ConfigValue -Table $workflowDefinition -Key 'settleMs' -Fallback (Get-ConfigValue -Table $defaults -Key 'settleMs' -Fallback 1800))
$useFixture = if ($NoFixture) {
    $false
}
else {
    Get-ConfigBool -Table $workflowDefinition -Key 'fixtureMode' -Fallback (Get-ConfigBool -Table $defaults -Key 'fixtureMode' -Fallback $true)
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDirectory = Join-Path $resolvedOutputRoot "$timestamp-$Workflow"
$logDirectory = Join-Path $runDirectory 'logs'
$screenshotDirectory = if ($null -ne $resolvedScreenshotDirectory) { $resolvedScreenshotDirectory } else { Join-Path $runDirectory 'screenshots' }
$manifestPath = Join-Path $runDirectory 'manifest.json'

New-Item -ItemType Directory -Force -Path $runDirectory, $logDirectory, $screenshotDirectory | Out-Null

$stdoutPath = Join-Path $logDirectory 'stdout.log'
$stderrPath = Join-Path $logDirectory 'stderr.log'
$projectDirectory = Split-Path -Parent $resolvedProjectPath
$exePath = Join-Path $projectDirectory "bin/$resolvedConfiguration/$resolvedFramework/$resolvedExeName"
$manifest = [ordered]@{
    workflow = [ordered]@{
        name = $workflowDefinition.name
        title = $workflowDefinition.title
        description = $workflowDefinition.description
        purpose = $workflowDefinition.purpose
    }
    run = [ordered]@{
        catalogPath = $catalogPath
        projectPath = $resolvedProjectPath
        configuration = $resolvedConfiguration
        framework = $resolvedFramework
        exePath = Resolve-RepoPath $exePath
        fixtureMode = $useFixture
        runDirectory = $runDirectory
        screenshotDirectory = $screenshotDirectory
        stdoutLog = $stdoutPath
        stderrLog = $stderrPath
        startedAt = (Get-Date).ToString('o')
    }
    steps = @()
}

$ownedProcess = $null
$window = $null
$originalFixtureEnv = [Environment]::GetEnvironmentVariable('MDC_FIXTURE_MODE', 'Process')

try {
    if (-not (Test-Path -LiteralPath 'config/appsettings.json') -and (Test-Path -LiteralPath 'config/appsettings.sample.json')) {
        Copy-Item -LiteralPath 'config/appsettings.sample.json' -Destination 'config/appsettings.json' -Force
        Write-Info 'Created config/appsettings.json from sample.'
    }

    if (-not $SkipBuild) {
        Write-Info "Restoring $resolvedProjectPath ..."
        & dotnet restore $resolvedProjectPath -p:TargetFramework=$resolvedFramework --verbosity minimal /p:EnableFullWpfBuild=true
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed for '$resolvedProjectPath'."
        }

        Write-Info "Building $resolvedProjectPath ($resolvedConfiguration, $resolvedFramework) ..."
        & dotnet build $resolvedProjectPath -c $resolvedConfiguration --no-restore -p:TargetFramework=$resolvedFramework --verbosity minimal /p:EnableFullWpfBuild=true
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for '$resolvedProjectPath'."
        }
    }

    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Desktop executable was not found at '$exePath'."
    }

    $existingProcesses = @(Get-Process -Name 'Meridian.Desktop' -ErrorAction SilentlyContinue)
    if ($existingProcesses.Count -gt 0) {
        if (-not $ReuseExistingApp) {
            throw "Meridian.Desktop is already running. Close it or rerun with -ReuseExistingApp."
        }

        Write-Warn "Reusing existing Meridian.Desktop process $($existingProcesses[0].Id)."
        $manifest.run.reusedExistingApp = $true
        $window = Wait-MeridianWindow -TimeoutSec 15 -Process $null
    }
    else {
        if ($useFixture) {
            [Environment]::SetEnvironmentVariable('MDC_FIXTURE_MODE', '1', 'Process')
        }
        else {
            [Environment]::SetEnvironmentVariable('MDC_FIXTURE_MODE', $null, 'Process')
        }

        $launchArguments = @()
        if ($useFixture) {
            $launchArguments += '--fixture'
        }

        Write-Info "Launching Meridian desktop: $exePath"
        $ownedProcess = Start-Process -FilePath $exePath `
            -ArgumentList $launchArguments `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath `
            -PassThru `
            -WindowStyle Normal

        $manifest.run.launchArguments = $launchArguments
        $manifest.run.processId = $ownedProcess.Id
        $window = Wait-MeridianWindow -TimeoutSec $LaunchTimeoutSec -Process $ownedProcess
        Write-Ok 'Meridian window detected.'
    }

    Start-Sleep -Milliseconds ($settleMs + 700)

    $stepIndex = 0
    foreach ($step in $workflowDefinition.steps) {
        $stepIndex += 1
        $title = [string](Get-ConfigValue -Table $step -Key 'title' -Fallback "Step $stepIndex")
        $pageTag = [string](Get-ConfigValue -Table $step -Key 'pageTag' -Fallback '')
        $notes = [string](Get-ConfigValue -Table $step -Key 'notes' -Fallback '')
        $keys = [string](Get-ConfigValue -Table $step -Key 'keys' -Fallback '')
        $launchArgs = @()

        if ($step.Contains('launchArgs')) {
            $launchArgs = @($step.launchArgs)
        }

        if (-not [string]::IsNullOrWhiteSpace($pageTag) -and $launchArgs.Count -eq 0) {
            $launchArgs = @("--page=$pageTag")
        }

        $stepWaitMs = [int](Get-ConfigValue -Table $step -Key 'waitMs' -Fallback $settleMs)
        $shouldCapture = Get-ConfigBool -Table $step -Key 'capture' -Fallback $true
        $captureName = [string](Get-ConfigValue -Table $step -Key 'captureName' -Fallback ('{0:D2}-{1}' -f $stepIndex, ($title -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()))
        $capturePath = if ($shouldCapture) { Join-Path $screenshotDirectory "$captureName.png" } else { $null }

        $stepResult = [ordered]@{
            index = $stepIndex
            title = $title
            pageTag = $pageTag
            notes = $notes
            keys = $keys
            launchArgs = $launchArgs
            capturePath = $capturePath
            status = 'pending'
            startedAt = (Get-Date).ToString('o')
        }

        try {
            Write-Info ("Running step {0}: {1}" -f $stepIndex, $title)

            if ($null -ne $ownedProcess -and $ownedProcess.HasExited) {
                throw "Meridian desktop exited unexpectedly with code $($ownedProcess.ExitCode)."
            }

            if ($launchArgs.Count -gt 0) {
                Invoke-ForwardedLaunch -ExePath $exePath -Arguments $launchArgs -WorkingDirectory $repoRoot
            }

            $window = Wait-MeridianWindow -TimeoutSec 10 -Process $ownedProcess

            if (-not [string]::IsNullOrWhiteSpace($keys)) {
                Send-WindowKeys -Window $window -Keys $keys
            }

            Start-Sleep -Milliseconds $stepWaitMs
            $window = Wait-MeridianWindow -TimeoutSec 10 -Process $ownedProcess

            if ($shouldCapture -and $null -ne $capturePath) {
                $savedPath = Save-WindowCapture -Window $window -Path $capturePath
                $stepResult.capturePath = $savedPath
                Write-Ok "Saved $savedPath"
            }

            $stepResult.status = 'ok'
            $stepResult.completedAt = (Get-Date).ToString('o')
        }
        catch {
            $stepResult.status = 'failed'
            $stepResult.error = $_.Exception.Message
            $stepResult.completedAt = (Get-Date).ToString('o')
            $manifest.steps += [pscustomobject]$stepResult
            throw
        }

        $manifest.steps += [pscustomobject]$stepResult
    }

    $manifest.run.status = 'ok'
}
catch {
    $manifest.run.status = 'failed'
    $manifest.run.error = $_.Exception.Message
    throw
}
finally {
    $manifest.run.finishedAt = (Get-Date).ToString('o')
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8

    if (-not $KeepAppOpen -and $null -ne $ownedProcess) {
        try {
            if (-not $ownedProcess.HasExited) {
                Write-Info 'Stopping Meridian desktop...'
                Stop-Process -Id $ownedProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
            Write-Warn "Failed to stop Meridian desktop cleanly: $($_.Exception.Message)"
        }
    }

    [Environment]::SetEnvironmentVariable('MDC_FIXTURE_MODE', $originalFixtureEnv, 'Process')
}

$savedCaptures = @($manifest.steps | Where-Object { $_.status -eq 'ok' -and $_.capturePath })
Write-Ok "Workflow '$Workflow' completed. Manifest: $manifestPath"
Write-Host ''
$savedCaptures |
    Select-Object @{ Name = 'Step'; Expression = { $_.index } }, Title, CapturePath |
    Format-Table -AutoSize |
    Out-Host

if ($PassThru) {
    [pscustomobject]@{
        workflow = $workflowDefinition.name
        title = $workflowDefinition.title
        manifestPath = $manifestPath
        runDirectory = $runDirectory
        screenshotDirectory = $screenshotDirectory
    }
}
