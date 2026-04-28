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
    [int]$LaunchTimeoutSec = 90,
    [string]$CheckpointPath = '',
    [string[]]$ForceCheckpointStep = @(),
    [switch]$AllowCheckpointInputMismatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'SharedBuild.ps1')
. (Join-Path $PSScriptRoot 'SharedPreflight.ps1')

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Write-Ok([string]$Message) { Write-Host "[ OK ] $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }

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

function Get-MeridianWindowFromProcess {
    param(
        [System.Diagnostics.Process]$Process
    )

    if ($null -eq $Process -or $Process.HasExited) {
        return $null
    }

    try {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [System.IntPtr]::Zero) {
            return [System.Windows.Automation.AutomationElement]::FromHandle($Process.MainWindowHandle)
        }
    }
    catch {
        # Ignore transient process/window state while WPF is navigating or starting.
    }

    return $null
}

function Find-MeridianWindow {
    param(
        [System.Diagnostics.Process]$Process
    )

    $processWindow = Get-MeridianWindowFromProcess -Process $Process
    if ($null -ne $processWindow) {
        return $processWindow
    }

    foreach ($candidateProcess in @(Get-Process -Name 'Meridian.Desktop' -ErrorAction SilentlyContinue)) {
        $processWindow = Get-MeridianWindowFromProcess -Process $candidateProcess
        if ($null -ne $processWindow) {
            return $processWindow
        }
    }

    try {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            'Meridian')

        return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $nameCondition)
    }
    catch {
        return $null
    }
}

function Activate-MeridianWindow {
    try {
        $wshShell = Get-Variable -Scope Script -Name WshShell -ValueOnly -ErrorAction SilentlyContinue
        if (-not $wshShell) {
            $script:WshShell = New-Object -ComObject WScript.Shell
            $wshShell = $script:WshShell
        }

        [void]$wshShell.AppActivate('Meridian')
        Start-Sleep -Milliseconds 400
        return $true
    }
    catch {
        Write-Warn "Failed to activate Meridian window: $($_.Exception.Message)"
        return $false
    }
}

function Wait-ForElement {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Finder,

        [int]$Attempts = 20,
        [int]$DelayMilliseconds = 300
    )

    for ($i = 0; $i -lt $Attempts; $i++) {
        try {
            $element = & $Finder
            if ($element) {
                return $element
            }
        }
        catch {
            # Ignore transient UI Automation failures while the tree is updating.
        }

        Start-Sleep -Milliseconds $DelayMilliseconds
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

        $window = Find-MeridianWindow -Process $Process
        if ($null -ne $window) {
            return $window
        }
    }

    throw "Meridian window did not appear within $TimeoutSec seconds."
}

function Find-DescendantByAutomationId {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window,

        [Parameter(Mandatory = $true)]
        [string]$AutomationId
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId
    )

    try {
        return $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    }
    catch {
        # Transient UI Automation timeouts are expected while WPF pages load; callers poll until deadline.
        return $null
    }
}

function Get-ShellAutomationState {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window
    )

    $shellAutomation = Find-DescendantByAutomationId -Window $Window -AutomationId 'ShellAutomationState'
    $pageTitle = Find-DescendantByAutomationId -Window $Window -AutomationId 'PageTitleText'

    return [pscustomobject]@{
        Ready = ($null -ne $shellAutomation) -or ($null -ne $pageTitle)
        PageTag = if ($shellAutomation) { $shellAutomation.Current.Name } else { $null }
        PageTitle = if ($pageTitle) { $pageTitle.Current.Name } else { $null }
    }
}

function Find-AutomationElementById {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window,

        [Parameter(Mandatory = $true)]
        [string]$AutomationId
    )

    return Find-DescendantByAutomationId -Window $Window -AutomationId $AutomationId
}

function Find-ButtonByName {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window,

        [Parameter(Mandatory = $true)]
        [string[]]$Names
    )

    $buttonType = [System.Windows.Automation.ControlType]::Button
    foreach ($name in $Names) {
        $condition = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                $name
            )),
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                $buttonType
            ))
        )

        $button = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $button) {
            return $button
        }
    }

    return $null
}

function Get-OperatingContextEnterButton {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window
    )

    $button = Find-AutomationElementById -Window $Window -AutomationId 'EnterWorkstationButton'
    if ($null -ne $button) {
        return $button
    }

    return Find-ButtonByName -Window $Window -Names @('Enter Workstation', 'Enter Fund')
}

function Get-SeedSampleContextsButton {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window
    )

    return Find-ButtonByName -Window $Window -Names @('Seed Sample Contexts', 'Seed Sample Profiles')
}

function Invoke-AutomationButton {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Button,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    try {
        $invoke = $Button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
        if ($null -eq $invoke) {
            Write-Warn "$Description button does not expose InvokePattern."
            return $false
        }

        $invoke.Invoke()
        return $true
    }
    catch {
        Write-Warn "Failed to invoke $Description button: $($_.Exception.Message)"
        return $false
    }
}

function Test-AutomationElementEnabled {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    try {
        return $Element.Current.IsEnabled
    }
    catch {
        return $false
    }
}

function Test-ShellAutomationReady {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window
    )

    $state = Get-ShellAutomationState -Window $Window
    return $state.Ready -and -not [string]::IsNullOrWhiteSpace($state.PageTag)
}

function Ensure-EnteredOperatingContext {
    param(
        [System.Diagnostics.Process]$Process
    )

    $shell = Wait-ForElement -Attempts 4 -DelayMilliseconds 250 -Finder {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before operating context could be confirmed."
        }

        $currentWindow = Find-MeridianWindow -Process $Process
        if ($null -eq $currentWindow) {
            return $null
        }

        if (Test-ShellAutomationReady -Window $currentWindow) {
            return $currentWindow
        }

        return $null
    }

    if ($shell) {
        return $true
    }

    $enterButton = Wait-ForElement -Attempts 10 -DelayMilliseconds 300 -Finder {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before operating context could be selected."
        }

        $currentWindow = Find-MeridianWindow -Process $Process
        if ($null -eq $currentWindow) {
            return $null
        }

        return Get-OperatingContextEnterButton -Window $currentWindow
    }

    if ($enterButton -and -not (Test-AutomationElementEnabled -Element $enterButton)) {
        $seedButton = Wait-ForElement -Attempts 5 -DelayMilliseconds 250 -Finder {
            $currentWindow = Find-MeridianWindow -Process $Process
            if ($null -eq $currentWindow) {
                return $null
            }

            return Get-SeedSampleContextsButton -Window $currentWindow
        }

        if ($seedButton) {
            if (Invoke-AutomationButton -Button $seedButton -Description 'seed sample contexts') {
                Start-Sleep -Seconds 2
            }
        }

        $enterButton = Wait-ForElement -Attempts 10 -DelayMilliseconds 300 -Finder {
            $currentWindow = Find-MeridianWindow -Process $Process
            if ($null -eq $currentWindow) {
                return $null
            }

            return Get-OperatingContextEnterButton -Window $currentWindow
        }
    }

    if (-not $enterButton) {
        Write-Warn 'Operating context selector did not expose an Enter Workstation button.'
        return $false
    }

    if (-not (Test-AutomationElementEnabled -Element $enterButton)) {
        Write-Warn 'Operating context selector did not enable Enter Workstation after seeding sample contexts.'
        return $false
    }

    Activate-MeridianWindow | Out-Null
    if (-not (Invoke-AutomationButton -Button $enterButton -Description 'enter workstation')) {
        return $false
    }

    $shell = Wait-ForElement -Attempts 24 -DelayMilliseconds 500 -Finder {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before the workstation shell appeared."
        }

        $currentWindow = Find-MeridianWindow -Process $Process
        if ($null -eq $currentWindow) {
            return $null
        }

        if (Test-ShellAutomationReady -Window $currentWindow) {
            return $currentWindow
        }

        return $null
    }

    if (-not $shell) {
        Write-Warn 'Workstation shell did not expose automation markers after entering the operating context.'
        return $false
    }

    Start-Sleep -Seconds 1
    return $true
}

function Wait-ForShellPage {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ExpectedPageTag,
        [string[]]$AcceptedPageTags = @(),
        [int]$TimeoutSec = 12
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $lastState = $null
    $acceptableTags = @()

    if (-not [string]::IsNullOrWhiteSpace($ExpectedPageTag)) {
        $acceptableTags += $ExpectedPageTag
    }

    if ($AcceptedPageTags) {
        $acceptableTags += @($AcceptedPageTags | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    $acceptableTags = @($acceptableTags | Select-Object -Unique)

    while ((Get-Date) -lt $deadline) {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before shell readiness could be confirmed (exit code $($Process.ExitCode))."
        }

        Activate-MeridianWindow | Out-Null
        $window = Wait-MeridianWindow -TimeoutSec 5 -Process $Process
        $lastState = Get-ShellAutomationState -Window $window

        $pageMatches = [string]::IsNullOrWhiteSpace($ExpectedPageTag) -or
            ($acceptableTags -contains $lastState.PageTag)

        if ($lastState.Ready -and $pageMatches) {
            return [pscustomobject]@{
                Window = $window
                State = $lastState
            }
        }

        Start-Sleep -Milliseconds 350
    }

    $observedPageTag = if ($lastState) { $lastState.PageTag } else { $null }
    $observedPageTitle = if ($lastState) { $lastState.PageTitle } else { $null }

    if ([string]::IsNullOrWhiteSpace($ExpectedPageTag)) {
        throw "Shell readiness markers were not confirmed before capture. Last observed page tag: '$observedPageTag'. Last observed title: '$observedPageTitle'."
    }

    $acceptedSummary = if ($acceptableTags.Count -gt 0) { $acceptableTags -join "', '" } else { '' }
    throw "Requested page '$ExpectedPageTag' was not confirmed before capture. Accepted page tags: '$acceptedSummary'. Last observed page tag: '$observedPageTag'. Last observed title: '$observedPageTitle'."
}

function Wait-ForStableShellPage {
    param(
        [System.Diagnostics.Process]$Process,
        [int]$TimeoutSec = 30,
        [int]$StableMs = 1200
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $lastSignature = $null
    $stableSince = $null
    $lastState = $null
    $lastWindow = $null

    while ((Get-Date) -lt $deadline) {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before shell readiness could be confirmed."
        }

        Activate-MeridianWindow | Out-Null
        $lastWindow = Wait-MeridianWindow -TimeoutSec 5 -Process $Process
        $lastState = Get-ShellAutomationState -Window $lastWindow

        if ($lastState.Ready -and -not [string]::IsNullOrWhiteSpace($lastState.PageTag)) {
            $signature = "$($lastState.PageTag)|$($lastState.PageTitle)"
            if ($signature -ne $lastSignature) {
                $lastSignature = $signature
                $stableSince = Get-Date
            }
            elseif ($null -ne $stableSince -and ((Get-Date) - $stableSince).TotalMilliseconds -ge $StableMs) {
                return [pscustomobject]@{
                    Window = $lastWindow
                    State = $lastState
                }
            }
        }

        Start-Sleep -Milliseconds 250
    }

    $observedPageTag = if ($lastState) { $lastState.PageTag } else { $null }
    $observedPageTitle = if ($lastState) { $lastState.PageTitle } else { $null }
    throw "Shell page tag did not stabilize before workflow navigation. Last observed page tag: '$observedPageTag'. Last observed title: '$observedPageTitle'."
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

    if (Send-ForwardedLaunchArgs -Arguments $Arguments -TimeoutMs 10000) {
        Write-Info "Forwarded desktop args through single-instance pipe: $($Arguments -join ' ')"
        return
    }

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

function Send-ForwardedLaunchArgs {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [int]$TimeoutMs = 10000
    )

    if ($Arguments.Count -eq 0) {
        return $true
    }

    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    $payload = [string]::Join("`n", $Arguments)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)

    while ((Get-Date) -lt $deadline) {
        try {
            $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
                '.',
                'Meridian.Desktop.SingleInstance.Pipe',
                [System.IO.Pipes.PipeDirection]::Out,
                [System.IO.Pipes.PipeOptions]::None)
            try {
                $pipe.Connect(500)
                $pipe.Write($bytes, 0, $bytes.Length)
                $pipe.Flush()
                return $true
            }
            finally {
                $pipe.Dispose()
            }
        }
        catch {
            Start-Sleep -Milliseconds 250
        }
    }

    return $false
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

function New-StageState {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path,
        [hashtable]$Inputs = @{},
        [int]$RetryCount = 0
    )

    return [ordered]@{
        stage = $Name
        contractVersion = 1
        inputs = $Inputs
        outputs = @{}
        timestamps = [ordered]@{
            startedAt = (Get-Date).ToString('o')
            finishedAt = $null
        }
        status = 'running'
        retryCount = $RetryCount
        errors = @()
        path = $Path
    }
}

function Save-StageState {
    param([Parameter(Mandatory = $true)][hashtable]$Stage)

    $output = [ordered]@{
        stage = $Stage.stage
        contractVersion = $Stage.contractVersion
        inputs = $Stage.inputs
        outputs = $Stage.outputs
        timestamps = $Stage.timestamps
        status = $Stage.status
        retryCount = $Stage.retryCount
        errors = @($Stage.errors)
    }

    $output | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Stage.path -Encoding utf8
}

function Get-StageState {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -AsHashtable
}

function Test-StageOutputsValid {
    param([hashtable]$StageData)
    if ($null -eq $StageData -or -not $StageData.ContainsKey('outputs')) {
        return $false
    }

    $outputs = $StageData.outputs
    if ($null -eq $outputs) { return $false }

    if ($outputs.ContainsKey('requiredFiles')) {
        foreach ($path in @($outputs.requiredFiles)) {
            if ([string]::IsNullOrWhiteSpace([string]$path)) { continue }
            if (-not (Test-Path -LiteralPath [string]$path)) { return $false }
        }
    }

    return $true
}

$catalogPath = Resolve-RepoPath $DefinitionPath
$initialOutputRoot = if ($PSBoundParameters.ContainsKey('OutputRoot')) { Resolve-RepoPath $OutputRoot } else { Resolve-RepoPath 'artifacts/desktop-workflows' }
$catalogPreflight = Invoke-MeridianPreflight `
    -Scenario 'desktop-workflow-catalog' `
    -RequiredCommands @('dotnet') `
    -RequiredPaths @($catalogPath) `
    -WritableDirectories @($initialOutputRoot) `
    -RequireWindows `
    -EmitJson `
    -AllowWarnings

if ($catalogPreflight.status -eq 'blocked') {
    throw "Preflight failed before workflow load. $(($catalogPreflight.blockingChecks | ConvertTo-Json -Depth 6 -Compress))"
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
Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot
$buildIsolationKey = New-MeridianBuildIsolationKey -Prefix ("desktop-workflow-" + $Workflow)
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

if ([string]::IsNullOrWhiteSpace($CheckpointPath)) {
    $CheckpointPath = Join-Path $resolvedOutputRoot "checkpoints/$Workflow.checkpoint.json"
}

$checkpoint = Initialize-MeridianCheckpoint `
    -Workflow "run-desktop-workflow:$Workflow" `
    -CheckpointPath $CheckpointPath `
    -InputObject ([ordered]@{
        workflow = $Workflow
        definitionPath = $catalogPath
        projectPath = $resolvedProjectPath
        configuration = $resolvedConfiguration
        framework = $resolvedFramework
        exeName = $resolvedExeName
        outputRoot = $resolvedOutputRoot
        screenshotDirectory = $resolvedScreenshotDirectory
        skipBuild = [bool]$SkipBuild
        fixtureMode = $useFixture
    }) `
    -ForceStep $ForceCheckpointStep `
    -AllowInputMismatch:$AllowCheckpointInputMismatch

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$existingRunDirectory = if ($checkpoint.Data.metadata.ContainsKey('runDirectory')) { [string]$checkpoint.Data.metadata.runDirectory } else { '' }
$runDirectory = if (-not [string]::IsNullOrWhiteSpace($existingRunDirectory)) { $existingRunDirectory } else { Join-Path $resolvedOutputRoot "$timestamp-$Workflow" }
$logDirectory = Join-Path $runDirectory 'logs'
$screenshotDirectory = if ($null -ne $resolvedScreenshotDirectory) { $resolvedScreenshotDirectory } else { Join-Path $runDirectory 'screenshots' }
$manifestPath = Join-Path $runDirectory 'manifest.json'
$checkpoint.Data.metadata.runDirectory = $runDirectory
$checkpoint.Data.metadata.manifestPath = $manifestPath
$checkpoint.Data.metadata.screenshotDirectory = $screenshotDirectory
Save-MeridianCheckpointContext -Context $checkpoint

New-Item -ItemType Directory -Force -Path $runDirectory, $logDirectory, $screenshotDirectory | Out-Null

$stdoutPath = Join-Path $logDirectory 'stdout.log'
$stderrPath = Join-Path $logDirectory 'stderr.log'
$exePath = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath $resolvedProjectPath -Configuration $resolvedConfiguration -Framework $resolvedFramework -BinaryName $resolvedExeName -IsolationKey $buildIsolationKey
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
$stageOrder = @('preflight', 'build', 'launch', 'capture', 'post-process', 'publish')
$stageStatuses = [ordered]@{}
$stageContracts = [ordered]@{}
$runSummaryPath = Join-Path $runDirectory 'run-summary.json'
$lastSuccessfulStage = $null
foreach ($stageName in $stageOrder) {
    $stagePath = Join-Path $runDirectory "$stageName.stage.json"
    $existing = Get-StageState -Path $stagePath
    if ($null -ne $existing -and [string]$existing.status -eq 'succeeded' -and (Test-StageOutputsValid -StageData $existing)) {
        $stageStatuses[$stageName] = 'skipped-valid'
        $stageContracts[$stageName] = $existing
        $lastSuccessfulStage = $stageName
    }
    else {
        $stageStatuses[$stageName] = 'pending'
    }
}

$resumeFromIndex = 0
for ($i = 0; $i -lt $stageOrder.Count; $i++) {
    if ($stageStatuses[$stageOrder[$i]] -ne 'skipped-valid') {
        $resumeFromIndex = $i
        break
    }
    $resumeFromIndex = $i + 1
}

for ($i = $resumeFromIndex + 1; $i -lt $stageOrder.Count; $i++) {
    if ($stageStatuses[$stageOrder[$i]] -eq 'skipped-valid') {
        $stageStatuses[$stageOrder[$i]] = 'pending'
    }
}

$ownedProcess = $null
$window = $null
$originalFixtureEnv = [Environment]::GetEnvironmentVariable('MDC_FIXTURE_MODE', 'Process')
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationClient')
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationTypes')

try {
    if ($stageStatuses['preflight'] -ne 'skipped-valid') {
        $preflightStagePath = Join-Path $runDirectory 'preflight.stage.json'
        $preflightStage = New-StageState -Name 'preflight' -Path $preflightStagePath -Inputs @{
            workflow = $Workflow
            catalogPath = $catalogPath
            projectPath = $resolvedProjectPath
        }

        try {
            $preflight = Invoke-MeridianPreflight `
                -Scenario 'desktop-workflow' `
                -RequiredCommands @('dotnet') `
                -RequiredPaths @($catalogPath, $resolvedProjectPath) `
                -WritableDirectories @($resolvedOutputRoot, $screenshotDirectory, $runDirectory, $logDirectory) `
                -RequireWindows `
                -FeatureFlagExpectations $(if (-not $useFixture) { @{ 'MDC_FIXTURE_MODE' = '0' } } else { @{} }) `
                -EmitJson `
                -AllowWarnings
            $preflightPath = Join-Path $runDirectory 'preflight.json'
            $preflight | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $preflightPath -Encoding utf8
            if ($preflight.status -eq 'blocked') {
                throw "Preflight failed. See '$preflightPath' for diagnostics."
            }

            $preflightStage.outputs = @{
                preflightPath = $preflightPath
                requiredFiles = @($preflightPath)
            }
            $preflightStage.status = 'succeeded'
            $stageStatuses['preflight'] = 'succeeded'
            $lastSuccessfulStage = 'preflight'
        }
        catch {
            $preflightStage.status = 'failed'
            $preflightStage.errors = @($_.Exception.Message)
            $stageStatuses['preflight'] = 'failed'
            throw
        }
        finally {
            $preflightStage.timestamps.finishedAt = (Get-Date).ToString('o')
            Save-StageState -Stage $preflightStage
            $stageContracts['preflight'] = (Get-StageState -Path $preflightStagePath)
        }
    }
    else {
        Write-Info 'Skipping preflight stage (valid existing outputs).'
    }

    if (-not $SkipBuild -and $stageStatuses['build'] -ne 'skipped-valid') {
        $buildStagePath = Join-Path $runDirectory 'build.stage.json'
        $existingBuildState = Get-StageState -Path $buildStagePath
        $buildStage = New-StageState -Name 'build' -Path $buildStagePath -RetryCount $(if ($existingBuildState) { [int]$existingBuildState.retryCount + 1 } else { 0 }) -Inputs @{
            projectPath = $resolvedProjectPath
            configuration = $resolvedConfiguration
            framework = $resolvedFramework
            skipBuild = [bool]$SkipBuild
        }
        try {
            if (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId 'build-desktop') {
                Start-MeridianCheckpointStep -Context $checkpoint -StepId 'build-desktop' -Description 'Restore and build Meridian desktop shell.'
                $desktopRestoreArgs = @(
                    Get-MeridianBuildArguments `
                        -IsolationKey $buildIsolationKey `
                        -AdditionalProperties @("Configuration=$resolvedConfiguration") `
                        -EnableFullWpfBuild
                )
                $desktopBuildArgs = @(
                    Get-MeridianBuildArguments `
                        -IsolationKey $buildIsolationKey `
                        -TargetFramework $resolvedFramework `
                        -AdditionalProperties @("Configuration=$resolvedConfiguration") `
                        -EnableFullWpfBuild
                )
                Write-Info "Restoring $resolvedProjectPath ..."
                & dotnet restore $resolvedProjectPath --verbosity minimal @desktopRestoreArgs
                if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for '$resolvedProjectPath'." }

                Write-Info "Building $resolvedProjectPath ($resolvedConfiguration, $resolvedFramework) ..."
                & dotnet build $resolvedProjectPath -c $resolvedConfiguration --no-restore --verbosity minimal @desktopBuildArgs
                if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for '$resolvedProjectPath'." }
                Complete-MeridianCheckpointStep -Context $checkpoint -StepId 'build-desktop' -ArtifactPointers @($exePath)
            }
            $buildStage.outputs = @{ exePath = $exePath; requiredFiles = @($exePath) }
            $buildStage.status = 'succeeded'
            $stageStatuses['build'] = 'succeeded'
            $lastSuccessfulStage = 'build'
        }
        catch {
            $buildStage.status = 'failed'
            $buildStage.errors = @($_.Exception.Message)
            $stageStatuses['build'] = 'failed'
            throw
        }
        finally {
            $buildStage.timestamps.finishedAt = (Get-Date).ToString('o')
            Save-StageState -Stage $buildStage
            $stageContracts['build'] = (Get-StageState -Path $buildStagePath)
        }
    }
    elseif (-not $SkipBuild) {
        Write-Info 'Skipping build stage (valid existing outputs).'
    }

    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Desktop executable was not found at '$exePath'."
    }

    if ($stageStatuses['launch'] -ne 'skipped-valid') {
        $launchStagePath = Join-Path $runDirectory 'launch.stage.json'
        $existingLaunchState = Get-StageState -Path $launchStagePath
        $launchStage = New-StageState -Name 'launch' -Path $launchStagePath -RetryCount $(if ($existingLaunchState) { [int]$existingLaunchState.retryCount + 1 } else { 0 }) -Inputs @{
            reuseExistingApp = [bool]$ReuseExistingApp
            fixtureMode = $useFixture
        }
        try {
            $existingProcesses = @(Get-Process -Name 'Meridian.Desktop' -ErrorAction SilentlyContinue)
            if ($existingProcesses.Count -gt 0) {
                if (-not $ReuseExistingApp) {
                    throw "Meridian.Desktop is already running. Close it or rerun with -ReuseExistingApp."
                }

                Write-Warn "Reusing existing Meridian.Desktop process $($existingProcesses[0].Id)."
                $manifest.run.reusedExistingApp = $true
                $window = Wait-MeridianWindow -TimeoutSec 15 -Process $null
                $launchStage.outputs = @{
                    reusedExistingProcess = $true
                    processId = $existingProcesses[0].Id
                    requiredFiles = @()
                }
            }
            else {
                if ($useFixture) {
                    [Environment]::SetEnvironmentVariable('MDC_FIXTURE_MODE', '1', 'Process')
                }
                else {
                    [Environment]::SetEnvironmentVariable('MDC_FIXTURE_MODE', $null, 'Process')
                }

                $launchArguments = @()
                if ($useFixture) { $launchArguments += '--fixture' }

                if (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId 'launch-desktop') {
                    Start-MeridianCheckpointStep -Context $checkpoint -StepId 'launch-desktop' -Description 'Launch Meridian desktop app.'
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
                Complete-MeridianCheckpointStep -Context $checkpoint -StepId 'launch-desktop' -ArtifactPointers @($stdoutPath, $stderrPath)
                Write-Ok 'Meridian window detected.'
                $launchStage.outputs = @{
                    processId = $ownedProcess.Id
                    stdoutLog = $stdoutPath
                    stderrLog = $stderrPath
                    requiredFiles = @($stdoutPath, $stderrPath)
                }
            }
            $launchStage.status = 'succeeded'
            $stageStatuses['launch'] = 'succeeded'
            $lastSuccessfulStage = 'launch'
        }
        catch {
            $launchStage.status = 'failed'
            $launchStage.errors = @($_.Exception.Message)
            $stageStatuses['launch'] = 'failed'
            throw
        }
        finally {
            $launchStage.timestamps.finishedAt = (Get-Date).ToString('o')
            Save-StageState -Stage $launchStage
            $stageContracts['launch'] = (Get-StageState -Path $launchStagePath)
        }
    }
    else {
        Write-Info 'Skipping launch stage (valid existing outputs).'
    }

    $operatingContextConfirmed = Ensure-EnteredOperatingContext -Process $ownedProcess
    $manifest.run.operatingContextConfirmed = $operatingContextConfirmed
    if (-not $operatingContextConfirmed) {
        throw 'Operating context was not confirmed; screenshot workflow cannot continue before shell readiness. Check EnterWorkstationButton and Seed Sample Contexts automation.'
    }

    Write-Ok 'Operating context confirmed.'

    $startupReadiness = Wait-ForStableShellPage -Process $ownedProcess -TimeoutSec ([Math]::Max(30, $LaunchTimeoutSec)) -StableMs 1200
    $window = $startupReadiness.Window
    $manifest.run.initialPageTag = $startupReadiness.State.PageTag
    $manifest.run.initialPageTitle = $startupReadiness.State.PageTitle
    Write-Ok "Shell ready on $($startupReadiness.State.PageTag) ($($startupReadiness.State.PageTitle))."

    $captureFailedSteps = @()
    if ($stageStatuses['capture'] -ne 'skipped-valid') {
        $captureStagePath = Join-Path $runDirectory 'capture.stage.json'
        $existingCaptureState = Get-StageState -Path $captureStagePath
        $captureStage = New-StageState -Name 'capture' -Path $captureStagePath -RetryCount $(if ($existingCaptureState) { [int]$existingCaptureState.retryCount + 1 } else { 0 }) -Inputs @{
            screenshotDirectory = $screenshotDirectory
            stepCount = @($workflowDefinition.steps).Count
        }
        $stepIndex = 0
        foreach ($step in $workflowDefinition.steps) {
        $stepIndex += 1
        $title = [string](Get-ConfigValue -Table $step -Key 'title' -Fallback "Step $stepIndex")
        $pageTag = [string](Get-ConfigValue -Table $step -Key 'pageTag' -Fallback '')
        $acceptedPageTags = @()
        if ($step.Contains('acceptedPageTags')) {
            $acceptedPageTags = @($step.acceptedPageTags)
        }

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
            acceptedPageTags = $acceptedPageTags
            notes = $notes
            keys = $keys
            launchArgs = $launchArgs
            capturePath = $capturePath
            status = 'pending'
            startedAt = (Get-Date).ToString('o')
        }

        try {
            Write-Info ("Running step {0}: {1}" -f $stepIndex, $title)
            $checkpointStepId = ('workflow-step-{0:D2}' -f $stepIndex)
            if (-not (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId $checkpointStepId)) {
                Write-Info ("Skipping step {0}: {1} (checkpoint resume)." -f $stepIndex, $title)
                $stepResult.status = 'ok'
                $stepResult.completedAt = (Get-Date).ToString('o')
                $manifest.steps += [pscustomobject]$stepResult
                continue
            }
            Start-MeridianCheckpointStep -Context $checkpoint -StepId $checkpointStepId -Description $title

            if ($null -ne $ownedProcess -and $ownedProcess.HasExited) {
                throw "Meridian desktop exited unexpectedly with code $($ownedProcess.ExitCode)."
            }

            if ($launchArgs.Count -gt 0) {
                Invoke-ForwardedLaunch -ExePath $exePath -Arguments $launchArgs -WorkingDirectory $repoRoot
            }

            $window = Wait-MeridianWindow -TimeoutSec 10 -Process $ownedProcess
            Activate-MeridianWindow | Out-Null

            if (-not [string]::IsNullOrWhiteSpace($keys)) {
                Send-WindowKeys -Window $window -Keys $keys
            }

            Start-Sleep -Milliseconds $stepWaitMs
            $pageReadiness = Wait-ForShellPage `
                -Process $ownedProcess `
                -ExpectedPageTag $pageTag `
                -AcceptedPageTags $acceptedPageTags `
                -TimeoutSec ([Math]::Max(8, [int][Math]::Ceiling(($stepWaitMs / 1000.0) + 4)))
            $window = $pageReadiness.Window
            $stepResult.observedPageTag = $pageReadiness.State.PageTag
            $stepResult.observedPageTitle = $pageReadiness.State.PageTitle

            if ($shouldCapture -and $null -ne $capturePath) {
                Activate-MeridianWindow | Out-Null
                $window = Wait-MeridianWindow -TimeoutSec 10 -Process $ownedProcess
                $savedPath = Save-WindowCapture -Window $window -Path $capturePath
                $stepResult.capturePath = $savedPath
                Write-Ok "Saved $savedPath"
            }

            $stepResult.status = 'ok'
            $stepResult.completedAt = (Get-Date).ToString('o')
            Complete-MeridianCheckpointStep -Context $checkpoint -StepId $checkpointStepId -ArtifactPointers @($capturePath)
        }
        catch {
            $stepResult.status = 'failed'
            $stepResult.error = $_.Exception.Message
            $stepResult.completedAt = (Get-Date).ToString('o')
            Fail-MeridianCheckpointStep -Context $checkpoint -StepId $checkpointStepId -Message $_.Exception.Message
            $captureFailedSteps += $stepResult
            Write-Warn ("Step {0} failed: {1}" -f $stepIndex, $_.Exception.Message)
        }

            $manifest.steps += [pscustomobject]$stepResult
        }
        $captureSucceeded = @($manifest.steps | Where-Object { $_.status -eq 'ok' -and $_.capturePath }).Count
        $captureFailed = @($manifest.steps | Where-Object { $_.status -eq 'failed' }).Count
        $captureStage.outputs = @{
            manifestPath = $manifestPath
            screenshotDirectory = $screenshotDirectory
            capturesSucceeded = $captureSucceeded
            capturesFailed = $captureFailed
            requiredFiles = @($manifestPath, $screenshotDirectory)
        }
        if ($captureSucceeded -eq 0 -and @($workflowDefinition.steps).Count -gt 0) {
            $captureStage.status = 'failed'
            $captureStage.errors = @('No screenshots were captured successfully.')
            $stageStatuses['capture'] = 'failed'
            throw 'Capture stage failed: no screenshots were captured successfully.'
        }
        elseif ($captureFailed -gt 0) {
            $captureStage.status = 'partial'
            $captureStage.errors = @("Failed steps: $captureFailed")
            $stageStatuses['capture'] = 'partial'
        }
        else {
            $captureStage.status = 'succeeded'
            $stageStatuses['capture'] = 'succeeded'
            $lastSuccessfulStage = 'capture'
        }
        $captureStage.timestamps.finishedAt = (Get-Date).ToString('o')
        Save-StageState -Stage $captureStage
        $stageContracts['capture'] = (Get-StageState -Path $captureStagePath)
    }
    else {
        Write-Info 'Skipping capture stage (valid existing outputs).'
    }

    if ($stageStatuses['post-process'] -ne 'skipped-valid') {
        $postProcessPath = Join-Path $runDirectory 'post-process.stage.json'
        $postProcessStage = New-StageState -Name 'post-process' -Path $postProcessPath -Inputs @{ actions = @('none') }
        $postProcessStage.outputs = @{ requiredFiles = @($manifestPath) }
        $postProcessStage.status = 'succeeded'
        $postProcessStage.timestamps.finishedAt = (Get-Date).ToString('o')
        Save-StageState -Stage $postProcessStage
        $stageStatuses['post-process'] = 'succeeded'
        $stageContracts['post-process'] = (Get-StageState -Path $postProcessPath)
        $lastSuccessfulStage = 'post-process'
    }
    else {
        Write-Info 'Skipping post-process stage (valid existing outputs).'
    }

    if ($stageStatuses['publish'] -ne 'skipped-valid') {
        $publishPath = Join-Path $runDirectory 'publish.stage.json'
        $publishStage = New-StageState -Name 'publish' -Path $publishPath -Inputs @{ publishEnabled = $workflowDefinition.Contains('publish') }
        $publishedFiles = @()
        if ($workflowDefinition.Contains('publish')) {
            $publishDirectory = Resolve-RepoPath ([string](Get-ConfigValue -Table $workflowDefinition.publish -Key 'directory' -Fallback ''))
            if (-not [string]::IsNullOrWhiteSpace($publishDirectory)) {
                New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
                foreach ($step in @($manifest.steps | Where-Object { $_.status -eq 'ok' -and $_.capturePath })) {
                    $destination = Join-Path $publishDirectory ([System.IO.Path]::GetFileName([string]$step.capturePath))
                    Copy-Item -LiteralPath $step.capturePath -Destination $destination -Force
                    $publishedFiles += $destination
                }
            }
        }
        $publishStage.outputs = @{ publishedFiles = $publishedFiles; requiredFiles = @($publishedFiles) }
        $publishStage.status = 'succeeded'
        $publishStage.timestamps.finishedAt = (Get-Date).ToString('o')
        Save-StageState -Stage $publishStage
        $stageStatuses['publish'] = 'succeeded'
        $stageContracts['publish'] = (Get-StageState -Path $publishPath)
        $lastSuccessfulStage = 'publish'
    }
    else {
        Write-Info 'Skipping publish stage (valid existing outputs).'
    }

    $failedCount = @($manifest.steps | Where-Object { $_.status -eq 'failed' }).Count
    $okCount = @($manifest.steps | Where-Object { $_.status -eq 'ok' }).Count
    $manifest.run.status = if ($failedCount -gt 0) { 'partial' } else { 'ok' }
    $manifest.run.partialFailures = $failedCount
    $manifest.run.completedSteps = $okCount
}
catch {
    $manifest.run.status = 'failed'
    $manifest.run.error = $_.Exception.Message
    throw
}
finally {
    $manifest.run.finishedAt = (Get-Date).ToString('o')
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $runSummary = [ordered]@{
        workflow = $Workflow
        runDirectory = $runDirectory
        manifestPath = $manifestPath
        generatedAt = (Get-Date).ToString('o')
        lastSuccessfulStage = $lastSuccessfulStage
        stages = $stageStatuses
        totals = [ordered]@{
            steps = @($manifest.steps).Count
            succeeded = @($manifest.steps | Where-Object { $_.status -eq 'ok' }).Count
            failed = @($manifest.steps | Where-Object { $_.status -eq 'failed' }).Count
        }
        status = if (@($manifest.steps | Where-Object { $_.status -eq 'failed' }).Count -gt 0) { 'partial' } else { [string]$manifest.run.status }
    }
    $runSummary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $runSummaryPath -Encoding utf8

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
Write-Ok "Workflow '$Workflow' completed. Manifest: $manifestPath. Run summary: $runSummaryPath"
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
