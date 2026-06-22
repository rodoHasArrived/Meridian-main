#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Workflow,
    [string]$Profile,
    [string]$ProfileRoot = 'scripts/dev/workflow-profiles',
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
. (Join-Path $PSScriptRoot 'SharedCheckpoint.ps1')
. (Join-Path $PSScriptRoot 'SharedPreflight.ps1')
. (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')
. (Join-Path $PSScriptRoot 'shared/retry.ps1')

# Script-level constants for all hardcoded automation identifiers, process/window names,
# and timing values. Centralising these here means a single-line change adapts the entire
# script when the binary name or named-pipe identifier changes.
$script:DesktopProcessName = 'Meridian.Desktop'
$script:DesktopWindowTitle = 'Meridian'
$script:SingleInstancePipeName = 'Meridian.Desktop.SingleInstance.Pipe'
$script:MinCaptureWidth = 200
$script:MinCaptureHeight = 200
$script:WindowActivationDelayMs = 400
$script:KeySendDelayMs = 250
$script:DefaultCaptureRetryAttempts = 6

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

    foreach ($candidateProcess in @(Get-Process -Name $script:DesktopProcessName -ErrorAction SilentlyContinue)) {
        $processWindow = Get-MeridianWindowFromProcess -Process $candidateProcess
        if ($null -ne $processWindow) {
            return $processWindow
        }
    }

    try {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $script:DesktopWindowTitle)

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

        [void]$wshShell.AppActivate($script:DesktopWindowTitle)
        Start-Sleep -Milliseconds $script:WindowActivationDelayMs
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

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
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

function Get-StartupContinueWithoutCredentialsButton {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window
    )

    $button = Find-AutomationElementById -Window $Window -AutomationId 'StartupContinueWithoutCredentialsButton'
    if ($null -ne $button) {
        return $button
    }

    return Find-ButtonByName -Window $Window -Names @('Continue without credentials')
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

function Backup-FileState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory
    )

    New-Item -ItemType Directory -Force -Path $BackupDirectory | Out-Null
    $backupPath = Join-Path $BackupDirectory ([System.IO.Path]::GetFileName($Path) + '.bak')
    if (Test-Path -LiteralPath $Path) {
        Copy-Item -LiteralPath $Path -Destination $backupPath -Force
        return [pscustomobject]@{ Path = $Path; BackupPath = $backupPath; Existed = $true }
    }

    return [pscustomobject]@{ Path = $Path; BackupPath = $backupPath; Existed = $false }
}

function Restore-FileState {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$States
    )

    foreach ($state in $States) {
        try {
            if ($state.Existed -and (Test-Path -LiteralPath $state.BackupPath)) {
                Copy-Item -LiteralPath $state.BackupPath -Destination $state.Path -Force
            }
            elseif (Test-Path -LiteralPath $state.Path) {
                Remove-Item -LiteralPath $state.Path -Force
            }
        }
        catch {
            Write-Warn "Failed to restore fixture state '$($state.Path)': $($_.Exception.Message)"
        }
    }
}

function Initialize-FixtureOperatingContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BackupDirectory
    )

    $meridianLocalAppData = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Meridian'
    New-Item -ItemType Directory -Force -Path $meridianLocalAppData | Out-Null

    $fundProfilesPath = Join-Path $meridianLocalAppData 'fund-profiles.json'
    $operatingContextPath = Join-Path $meridianLocalAppData 'workstation-operating-context.json'
    $states = @(
        (Backup-FileState -Path $fundProfilesPath -BackupDirectory $BackupDirectory),
        (Backup-FileState -Path $operatingContextPath -BackupDirectory $BackupDirectory)
    )

    [ordered]@{
        LastSelectedFundProfileId = 'alpha-credit'
        Profiles = @(
            [ordered]@{
                FundProfileId = 'alpha-credit'
                DisplayName = 'Alpha Credit'
                LegalEntityName = 'Alpha Credit Master Fund LP'
                BaseCurrency = 'USD'
                DefaultWorkspaceId = 'accounting'
                DefaultLandingPageTag = 'AccountingShell'
                DefaultLedgerScope = 0
                EntityIds = @()
                SleeveIds = @()
                VehicleIds = @()
                IsDefault = $true
            }
        )
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fundProfilesPath -Encoding utf8

    [ordered]@{
        LastSelectedOperatingContextKey = 'Fund:alpha-credit'
        WindowMode = 1
        CurrentLayoutPresetId = 'accounting-review'
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $operatingContextPath -Encoding utf8

    return $states
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

function Resolve-WorkflowPageTag {
    param(
        [string]$PageTag
    )

    if ([string]::IsNullOrWhiteSpace($PageTag)) {
        return $PageTag
    }

    switch ($PageTag.Trim()) {
        'ResearchShell' { return 'StrategyShell' }
        'ResearchWorkspace' { return 'StrategyShell' }
        'DataOperationsShell' { return 'DataShell' }
        'DataOperationsWorkspace' { return 'DataShell' }
        'GovernanceShell' { return 'AccountingShell' }
        'GovernanceWorkspace' { return 'AccountingShell' }
        default { return $PageTag.Trim() }
    }
}

function Ensure-EnteredOperatingContext {
    param(
        [System.Diagnostics.Process]$Process
    )

    # Delay between invoking Switch Context and probing Enter Workstation on FundProfileSelection.
    $contextSwitchTransitionDelayMs = 800
    # The desktop window can appear before the WPF frame has finished rendering the selector.
    $contextSelectorProbeAttempts = 40
    # Settle time after Seed Sample Contexts completes before re-probing Enter Workstation.
    $contextSeedSettleSeconds = 2
    # Settle time after entering the workstation before the capture loop starts.
    $contextEnterSettleSeconds = 1

    $shell = Wait-ForElement -Attempts 4 -DelayMilliseconds 250 -Finder {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before operating context could be confirmed."
        }

        $currentWindow = Find-MeridianWindow -Process $Process
        if ($null -eq $currentWindow) {
            return $null
        }

        if (Test-ShellAutomationReady -Window $currentWindow) {
            $contextSelectionHint = Find-AutomationElementById -Window $currentWindow -AutomationId 'ContextSelectionHint'
            if ($null -ne $contextSelectionHint) {
                # Returning $null forces the workflow into the context-selection automation branch below.
                # Wait-ForElement bounds retries so this recovery path fails fast instead of hanging indefinitely.
                return $null
            }

            return $currentWindow
        }

        return $null
    }

    if ($shell) {
        return $true
    }

    $startupContinueButton = Wait-ForElement -Attempts 8 -DelayMilliseconds 300 -Finder {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before startup authentication could be completed."
        }

        $currentWindow = Find-MeridianWindow -Process $Process
        if ($null -eq $currentWindow) {
            return $null
        }

        return Get-StartupContinueWithoutCredentialsButton -Window $currentWindow
    }

    if ($startupContinueButton -and (Test-AutomationElementEnabled -Element $startupContinueButton)) {
        Activate-MeridianWindow | Out-Null
        if (Invoke-AutomationButton -Button $startupContinueButton -Description 'continue without credentials') {
            Start-Sleep -Seconds $contextEnterSettleSeconds
            $shell = Wait-ForElement -Attempts 24 -DelayMilliseconds 500 -Finder {
                if ($null -ne $Process -and $Process.HasExited) {
                    throw "Meridian desktop exited before the workstation shell appeared after startup authentication."
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
        }
    }

    $enterButton = Wait-ForElement -Attempts $contextSelectorProbeAttempts -DelayMilliseconds 300 -Finder {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before operating context could be selected."
        }

        $currentWindow = Find-MeridianWindow -Process $Process
        if ($null -eq $currentWindow) {
            return $null
        }

        return Get-OperatingContextEnterButton -Window $currentWindow
    }

    if (-not $enterButton) {
        $switchContextButton = Wait-ForElement -Attempts 6 -DelayMilliseconds 250 -Finder {
            if ($null -ne $Process -and $Process.HasExited) {
                throw "Meridian desktop exited before context switch could be requested."
            }

            $currentWindow = Find-MeridianWindow -Process $Process
            if ($null -eq $currentWindow) {
                return $null
            }

            $contextSelectionHintButton = Find-AutomationElementById -Window $currentWindow -AutomationId 'ContextSelectionHintButton'
            if ($null -ne $contextSelectionHintButton) {
                # Preferred shell hint when no operating context is active.
                return $contextSelectionHintButton
            }

            $fallbackSwitchButton = Find-AutomationElementById -Window $currentWindow -AutomationId 'SwitchContextButton'
            if ($null -ne $fallbackSwitchButton) {
                # Fallback command-strip action for shells where the hint card is not rendered.
                return $fallbackSwitchButton
            }

            return $null
        }

        if ($switchContextButton -and (Test-AutomationElementEnabled -Element $switchContextButton)) {
            Activate-MeridianWindow | Out-Null
            if (Invoke-AutomationButton -Button $switchContextButton -Description 'switch context') {
                # Allow the FundProfileSelection page transition to complete before probing for Enter Workstation.
                Start-Sleep -Milliseconds $contextSwitchTransitionDelayMs
                $enterButton = Wait-ForElement -Attempts $contextSelectorProbeAttempts -DelayMilliseconds 300 -Finder {
                    $currentWindow = Find-MeridianWindow -Process $Process
                    if ($null -eq $currentWindow) {
                        return $null
                    }

                    return Get-OperatingContextEnterButton -Window $currentWindow
                }
            }
        }
    }

    if (-not $enterButton -or -not (Test-AutomationElementEnabled -Element $enterButton)) {
        $seedButton = Wait-ForElement -Attempts 5 -DelayMilliseconds 250 -Finder {
            $currentWindow = Find-MeridianWindow -Process $Process
            if ($null -eq $currentWindow) {
                return $null
            }

            return Get-SeedSampleContextsButton -Window $currentWindow
        }

        if ($seedButton) {
            if (Invoke-AutomationButton -Button $seedButton -Description 'seed sample contexts') {
                Start-Sleep -Seconds $contextSeedSettleSeconds
            }
        }

        $enterButton = Wait-ForElement -Attempts $contextSelectorProbeAttempts -DelayMilliseconds 300 -Finder {
            $currentWindow = Find-MeridianWindow -Process $Process
            if ($null -eq $currentWindow) {
                return $null
            }

            return Get-OperatingContextEnterButton -Window $currentWindow
        }
    }

    if (-not $enterButton) {
        $readyShell = Wait-ForElement -Attempts 6 -DelayMilliseconds 500 -Finder {
            if ($null -ne $Process -and $Process.HasExited) {
                throw "Meridian desktop exited before operating context readiness could be confirmed."
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

        if ($readyShell) {
            Write-Warn 'Operating context selector did not expose an Enter Workstation button; shell automation is already ready.'
            Start-Sleep -Seconds $contextEnterSettleSeconds
            return $true
        }

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

    Start-Sleep -Seconds $contextEnterSettleSeconds
    return $true
}

function Wait-ForShellPage {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$ExpectedPageTag,
        [int]$TimeoutSec = 12
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $lastState = $null
    $expectedCanonicalPageTag = Resolve-WorkflowPageTag -PageTag $ExpectedPageTag

    while ((Get-Date) -lt $deadline) {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "Meridian desktop exited before shell readiness could be confirmed (exit code $($Process.ExitCode))."
        }

        Activate-MeridianWindow | Out-Null
        $window = Wait-MeridianWindow -TimeoutSec 5 -Process $Process
        $lastState = Get-ShellAutomationState -Window $window

        if ($lastState.Ready -and (
                [string]::IsNullOrWhiteSpace($expectedCanonicalPageTag) -or
                [string]::Equals($lastState.PageTag, $expectedCanonicalPageTag, [System.StringComparison]::Ordinal))) {
            return [pscustomobject]@{
                Window = $window
                State = $lastState
            }
        }

        Start-Sleep -Milliseconds 350
    }

    $observedPageTag = if ($lastState) { $lastState.PageTag } else { $null }
    $observedPageTitle = if ($lastState) { $lastState.PageTitle } else { $null }
    throw "Requested page '$ExpectedPageTag' (canonical '$expectedCanonicalPageTag') was not confirmed before capture. Last observed page tag: '$observedPageTag'. Last observed title: '$observedPageTitle'."
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

function Test-BitmapHasVisualContent {
    param(
        [Parameter(Mandatory = $true)]
        [System.Drawing.Bitmap]$Bitmap
    )

    $sampledColors = New-Object 'System.Collections.Generic.HashSet[int]'
    $sampleColumns = 8
    $sampleRows = 8
    for ($xIndex = 0; $xIndex -lt $sampleColumns; $xIndex += 1) {
        $x = [Math]::Min($Bitmap.Width - 1, [Math]::Max(0, [int](($Bitmap.Width - 1) * $xIndex / [Math]::Max(1, $sampleColumns - 1))))
        for ($yIndex = 0; $yIndex -lt $sampleRows; $yIndex += 1) {
            $y = [Math]::Min($Bitmap.Height - 1, [Math]::Max(0, [int](($Bitmap.Height - 1) * $yIndex / [Math]::Max(1, $sampleRows - 1))))
            [void]$sampledColors.Add($Bitmap.GetPixel($x, $y).ToArgb())
            if ($sampledColors.Count -ge 4) {
                return $true
            }
        }
    }

    return $false
}

function Test-ImageFileHasVisualContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }

    $stream = $null
    $bitmap = $null
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $bitmap = [System.Drawing.Bitmap]::new($stream)
        return Test-BitmapHasVisualContent -Bitmap $bitmap
    }
    finally {
        if ($null -ne $bitmap) { $bitmap.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

function Save-InProcessWindowCapture {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [int]$TimeoutMs = 15000
    )

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($resolvedPath)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
        Remove-Item -LiteralPath $resolvedPath -Force
    }

    if (-not (Send-ForwardedLaunchArgs -Arguments @("--screenshot=$resolvedPath") -TimeoutMs 10000)) {
        throw 'Unable to forward in-process screenshot request to the running Meridian desktop instance.'
    }

    $deadline = (Get-Date).AddMilliseconds($TimeoutMs)
    $lastReason = 'screenshot file was not created'
    while ((Get-Date) -lt $deadline) {
        if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
            try {
                $file = Get-Item -LiteralPath $resolvedPath
                if ($file.Length -gt 0 -and (Test-ImageFileHasVisualContent -Path $resolvedPath)) {
                    return $resolvedPath
                }

                $lastReason = "screenshot file exists but failed visual-content validation ($($file.Length) bytes)"
            }
            catch {
                $lastReason = $_.Exception.Message
            }
        }

        Start-Sleep -Milliseconds 250
    }

    throw "In-process desktop screenshot capture did not produce a valid image before timeout: $lastReason."
}

function Save-WindowCapture {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Window,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $rect = $Window.Current.BoundingRectangle
    if ($rect.Width -lt $script:MinCaptureWidth -or $rect.Height -lt $script:MinCaptureHeight) {
        throw "Window bounds are too small to capture ($($rect.Width)x$($rect.Height))."
    }

    if (-not ('MeridianDesktopCaptureNative' -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class MeridianDesktopCaptureNative
{
    public const uint PW_RENDERFULLCONTENT = 0x00000002;
    public const int SRCCOPY = 0x00CC0020;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);
}
"@
    }

    $nativeHandleValue = [int64]$Window.Current.NativeWindowHandle
    if ($nativeHandleValue -le 0) {
        throw 'Window did not expose a native handle for PrintWindow capture.'
    }

    $nativeHandle = [System.IntPtr]::new($nativeHandleValue)
    $bitmap = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = [System.IntPtr]::Zero

    try {
        $hdc = $graphics.GetHdc()
        $printSucceeded = [MeridianDesktopCaptureNative]::PrintWindow(
            $nativeHandle,
            $hdc,
            [uint32][MeridianDesktopCaptureNative]::PW_RENDERFULLCONTENT
        )

        if (-not $printSucceeded) {
            $lastError = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
            throw "PrintWindow failed while capturing desktop screenshot (Win32: $lastError)."
        }

        if (-not (Test-BitmapHasVisualContent -Bitmap $bitmap)) {
            $screenDc = [MeridianDesktopCaptureNative]::GetDC([System.IntPtr]::Zero)
            if ($screenDc -eq [System.IntPtr]::Zero) {
                $lastError = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
                throw "PrintWindow returned a blank capture and GetDC failed while preparing screen fallback (Win32: $lastError)."
            }

            try {
                $blitSucceeded = [MeridianDesktopCaptureNative]::BitBlt(
                    $hdc,
                    0,
                    0,
                    [int]$rect.Width,
                    [int]$rect.Height,
                    $screenDc,
                    [int]$rect.Left,
                    [int]$rect.Top,
                    [MeridianDesktopCaptureNative]::SRCCOPY
                )

                if (-not $blitSucceeded) {
                    $lastError = [System.Runtime.InteropServices.Marshal]::GetLastWin32Error()
                    throw "PrintWindow returned a blank capture and screen fallback failed (Win32: $lastError)."
                }
            }
            finally {
                [MeridianDesktopCaptureNative]::ReleaseDC([System.IntPtr]::Zero, $screenDc) | Out-Null
            }
        }

        if (-not (Test-BitmapHasVisualContent -Bitmap $bitmap)) {
            throw 'Desktop screenshot capture remained blank after PrintWindow and screen fallback.'
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        return $Path
    }
    finally {
        if ($hdc -ne [System.IntPtr]::Zero) {
            $graphics.ReleaseHdc($hdc)
        }
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
                $script:SingleInstancePipeName,
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
    Start-Sleep -Milliseconds $script:KeySendDelayMs
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

function Add-StageStatus {
    param(
        [Parameter(Mandatory = $true)]
        [object]$StageStatus,

        [Parameter(Mandatory = $true)]
        [string]$Stage,

        [Parameter(Mandatory = $true)]
        [string]$Status,

        [string]$Message = '',
        [object]$Metadata = $null
    )

    if ($null -eq $StageStatus) {
        throw "StageStatus collection is null."
    }

    if ($StageStatus -isnot [System.Collections.Generic.List[object]]) {
        throw "StageStatus must be a List[object], got: $($StageStatus.GetType().FullName)"
    }

    $StageStatus.Add([ordered]@{
            stage = $Stage
            status = $Status
            message = $Message
            metadata = $Metadata
            timestampUtc = (Get-Date).ToUniversalTime().ToString('O')
        }) | Out-Null
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
    if ($null -eq $StageData -or -not (Test-MeridianDictionaryContainsKey -Dictionary $StageData -Key 'outputs')) {
        return $false
    }

    $outputs = $StageData.outputs
    if ($null -eq $outputs) { return $false }

    if (Test-MeridianDictionaryContainsKey -Dictionary $outputs -Key 'requiredFiles') {
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
    -RequiredCommands ([string[]]@('dotnet')) `
    -RequiredPaths ([string[]]@([string]$catalogPath)) `
    -WritableDirectories ([string[]]@([string]$initialOutputRoot)) `
    -RequireWindows:$true `
    -EmitJson:$true `
    -AllowWarnings:$true

if ($catalogPreflight.status -eq 'blocked') {
    throw "Preflight failed before workflow load. $(($catalogPreflight.blockingChecks | ConvertTo-Json -Depth 6 -Compress))"
}

$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json -AsHashtable
$defaults = Get-ConfigValue -Table $catalog -Key 'defaults' -Fallback @{}
$workflowDefinition = @($catalog.workflows | Where-Object { $_.name -eq $Workflow }) | Select-Object -First 1

if ($null -eq $workflowDefinition) {
    throw "Workflow '$Workflow' was not found in '$catalogPath'."
}

$resolvedProfileName = if ($PSBoundParameters.ContainsKey('Profile') -and -not [string]::IsNullOrWhiteSpace($Profile)) { $Profile } else { $Workflow }
$null = & (Join-Path $PSScriptRoot 'validate-workflow-profile.ps1') -Profile $resolvedProfileName -ProfileRoot $ProfileRoot -NoFixture:$NoFixture -ReuseExistingApp:$ReuseExistingApp -EmitJson
$profileEnvelope = Get-MeridianWorkflowProfile -RepoRoot $repoRoot -ProfileName $resolvedProfileName -ProfileRoot $ProfileRoot
$profileBuild = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'build' -Fallback @{}
$profileFixture = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'fixture' -Fallback @{}
$profileScreenshots = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'screenshots' -Fallback @{}
$profileRetention = Get-MeridianWorkflowProfileValue -Table $profileScreenshots -Key 'retention' -Fallback @{}

$projectPathInput = if ($PSBoundParameters.ContainsKey('ProjectPath')) { $ProjectPath } else { Get-MeridianWorkflowProfileValue -Table $profileBuild -Key 'projectPath' -Fallback (Get-ConfigValue -Table $defaults -Key 'projectPath' -Fallback 'src/Meridian.Wpf/Meridian.Wpf.csproj') }
$resolvedProjectPath = Resolve-RepoPath $projectPathInput
$resolvedConfiguration = if ($PSBoundParameters.ContainsKey('Configuration')) { $Configuration } else { [string](Get-MeridianWorkflowProfileValue -Table $profileBuild -Key 'configuration' -Fallback (Get-ConfigValue -Table $defaults -Key 'configuration' -Fallback 'Release')) }
$resolvedFramework = if ($PSBoundParameters.ContainsKey('Framework')) { $Framework } else { [string](Get-MeridianWorkflowProfileValue -Table $profileBuild -Key 'framework' -Fallback (Get-ConfigValue -Table $defaults -Key 'framework' -Fallback 'net10.0-windows10.0.19041.0')) }
$resolvedExeName = if ($PSBoundParameters.ContainsKey('ExeName')) { $ExeName } else { [string](Get-MeridianWorkflowProfileValue -Table $profileBuild -Key 'exeName' -Fallback (Get-ConfigValue -Table $defaults -Key 'exeName' -Fallback 'Meridian.Desktop.exe')) }
$outputRootInput = if ($PSBoundParameters.ContainsKey('OutputRoot')) { $OutputRoot } else { [string](Get-MeridianWorkflowProfileValue -Table $profileScreenshots -Key 'outputRoot' -Fallback (Get-ConfigValue -Table $defaults -Key 'outputRoot' -Fallback 'artifacts/desktop-workflows')) }
$resolvedOutputRoot = Resolve-RepoPath $outputRootInput
$retentionDays = [int](Get-MeridianWorkflowProfileValue -Table $profileRetention -Key 'maxAgeDays' -Fallback 14)
$retentionLatest = [int](Get-MeridianWorkflowProfileValue -Table $profileRetention -Key 'retainLatest' -Fallback 10)
Invoke-MeridianWorkflowArtifactRetention -OutputRoot $resolvedOutputRoot -MaxAgeDays $retentionDays -RetainLatest $retentionLatest
$buildIsolationKey = if ($SkipBuild) { '' } else { New-MeridianBuildIsolationKey -Prefix ("desktop-workflow-" + $Workflow) }
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
    $profileFixtureRequired = [bool](Get-MeridianWorkflowProfileValue -Table $profileFixture -Key 'required' -Fallback $true)
    if ($profileFixtureRequired) {
        $true
    }
    else {
        Get-ConfigBool -Table $workflowDefinition -Key 'fixtureMode' -Fallback (Get-ConfigBool -Table $defaults -Key 'fixtureMode' -Fallback $true)
    }
}

if ([string]::IsNullOrWhiteSpace($CheckpointPath)) {
    $CheckpointPath = Join-Path $resolvedOutputRoot "checkpoints/$Workflow.checkpoint.json"
}

$checkpoint = Initialize-MeridianCheckpoint `
    -Workflow "run-desktop-workflow:$Workflow" `
    -CheckpointPath $CheckpointPath `
    -InputObject ([ordered]@{
        workflow = $Workflow
        profile = $resolvedProfileName
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
$existingRunDirectory = if (Test-MeridianDictionaryContainsKey -Dictionary $checkpoint.Data.metadata -Key 'runDirectory') { [string]$checkpoint.Data.metadata.runDirectory } else { '' }
$runDirectory = Join-Path $resolvedOutputRoot "$timestamp-$Workflow"
if (-not [string]::IsNullOrWhiteSpace($existingRunDirectory)) {
    $runDirectory = $existingRunDirectory
}
$logDirectory = Join-Path $runDirectory 'logs'
$bundleDirectory = Join-Path $runDirectory 'bundle'
$bundleScreenshotDirectory = Join-Path $bundleDirectory 'screenshots'
$bundleWorkflowLogPath = Join-Path $bundleDirectory 'workflow-log.txt'
$bundleStageStatusPath = Join-Path $bundleDirectory 'stage-status.json'
$bundleEnvironmentPath = Join-Path $bundleDirectory 'environment.json'
$bundleLastSuccessfulStepPath = Join-Path $bundleDirectory 'last-successful-step.json'
$screenshotDirectory = if ($null -ne $resolvedScreenshotDirectory) { $resolvedScreenshotDirectory } else { Join-Path $runDirectory 'screenshots' }
$manifestPath = Join-Path $runDirectory 'manifest.json'
$checkpoint.Data.metadata.runDirectory = $runDirectory
$checkpoint.Data.metadata.manifestPath = $manifestPath
$checkpoint.Data.metadata.screenshotDirectory = $screenshotDirectory
$checkpoint.Data.metadata.bundleDirectory = $bundleDirectory
Save-MeridianCheckpointContext -Context $checkpoint

New-Item -ItemType Directory -Force -Path $runDirectory, $logDirectory, $screenshotDirectory, $bundleDirectory, $bundleScreenshotDirectory | Out-Null

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
        profile = $resolvedProfileName
        profilePath = $profileEnvelope.path
        catalogPath = $catalogPath
        projectPath = $resolvedProjectPath
        configuration = $resolvedConfiguration
        framework = $resolvedFramework
        exePath = Resolve-RepoPath $exePath
        fixtureMode = $useFixture
        runDirectory = $runDirectory
        screenshotDirectory = $screenshotDirectory
        bundleDirectory = $bundleDirectory
        stdoutLog = $stdoutPath
        stderrLog = $stderrPath
        startedAt = (Get-Date).ToString('o')
    }
    steps = @()
    retryTelemetry = @()
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
$workflowTranscriptActive = $false
$stageStatusEntries = [System.Collections.Generic.List[object]]::new()
$lastSuccessfulStep = $null
$originalWorkflowEnv = @{
    MDC_FIXTURE_MODE = [Environment]::GetEnvironmentVariable('MDC_FIXTURE_MODE', 'Process')
    DOTNET_ENVIRONMENT = [Environment]::GetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Process')
    ASPNETCORE_ENVIRONMENT = [Environment]::GetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Process')
    MERIDIAN_USE_INMEMORY_GOVERNANCE = [Environment]::GetEnvironmentVariable('MERIDIAN_USE_INMEMORY_GOVERNANCE', 'Process')
    MDC_WPF_SOFTWARE_RENDERING = [Environment]::GetEnvironmentVariable('MDC_WPF_SOFTWARE_RENDERING', 'Process')
}
$fixtureFileStates = @()

$environmentSnapshot = [ordered]@{
    workflow = $Workflow
    runDirectory = $runDirectory
    bundleDirectory = $bundleDirectory
    hostName = [System.Environment]::MachineName
    userName = [System.Environment]::UserName
    osVersion = [System.Environment]::OSVersion.VersionString
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    dotnetRoot = [System.Environment]::GetEnvironmentVariable('DOTNET_ROOT')
    fixtureMode = $useFixture
    reuseExistingApp = [bool]$ReuseExistingApp
    skipBuild = [bool]$SkipBuild
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
}
$environmentSnapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $bundleEnvironmentPath -Encoding utf8
Start-Transcript -LiteralPath $bundleWorkflowLogPath -Force | Out-Null
$workflowTranscriptActive = $true

$preflight = Invoke-MeridianPreflight `
    -Scenario 'desktop-workflow' `
    -RequiredCommands @('dotnet') `
    -RequiredPaths @($catalogPath, $resolvedProjectPath) `
    -WritableDirectories @($resolvedOutputRoot, $screenshotDirectory, $runDirectory, $logDirectory) `
    -RequireWindows `
    -FeatureFlagExpectations $(if (-not $useFixture) { @{ 'MDC_FIXTURE_MODE' = '0' } } else { @{} }) `
    -EmitJson `
    -AllowWarnings

if ($preflight.status -eq 'blocked') {
    $preflightPath = Join-Path $runDirectory 'preflight.json'
    $preflight | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $preflightPath -Encoding utf8
    throw "Preflight failed. See '$preflightPath' for diagnostics."
}
Add-StageStatus -StageStatus $stageStatusEntries -Stage 'preflight' -Status 'ok' -Message 'Desktop workflow preflight checks completed.'

$retryTelemetry = New-Object System.Collections.ArrayList
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
foreach ($uiaAssembly in @('UIAutomationClient', 'UIAutomationTypes')) {
    try {
        Add-Type -AssemblyName $uiaAssembly -ErrorAction Stop
    }
    catch {
        # Fallback for environments where the assembly must be loaded by partial name.
        try {
            [void][System.Reflection.Assembly]::LoadWithPartialName($uiaAssembly)
        }
        catch {
            throw "Required UI Automation assembly '$uiaAssembly' could not be loaded. Desktop workflow automation requires Windows UIAutomation assemblies. Error: $($_.Exception.Message)"
        }
    }
}

try {
    if (-not (Test-Path -LiteralPath 'config/appsettings.json') -and (Test-Path -LiteralPath 'config/appsettings.sample.json')) {
        Copy-Item -LiteralPath 'config/appsettings.sample.json' -Destination 'config/appsettings.json' -Force
        Write-Info 'Created config/appsettings.json from sample.'
    }

    if ($useFixture) {
        $fixtureFileStates = @(Initialize-FixtureOperatingContext -BackupDirectory (Join-Path $bundleDirectory 'fixture-state-backup'))
        Write-Info 'Prepared fixture operating context for desktop workflow startup.'
    }

    if ($stageStatuses['preflight'] -ne 'skipped-valid') {
        $preflightStagePath = Join-Path $runDirectory 'preflight.stage.json'
        $preflightStage = New-StageState -Name 'preflight' -Path $preflightStagePath -Inputs @{
            workflow = $Workflow
            catalogPath = $catalogPath
            projectPath = $resolvedProjectPath
        }

        try {
            $preflightArgs = @{
                Scenario = 'desktop-workflow'
                RequiredCommands = [string[]]@('dotnet')
                RequiredPaths = [string[]]@($catalogPath, $resolvedProjectPath)
                WritableDirectories = [string[]]@($resolvedOutputRoot, $screenshotDirectory, $runDirectory, $logDirectory)
                RequireWindows = $true
                FeatureFlagExpectations = $(if (-not $useFixture) { @{ 'MDC_FIXTURE_MODE' = '0' } } else { @{} })
                EmitJson = $true
                AllowWarnings = $true
            }
            $preflight = Invoke-MeridianPreflight @preflightArgs
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
                        -AdditionalProperties @("Configuration=$resolvedConfiguration", 'UseSharedCompilation=false') `
                        -EnableFullWpfBuild `
                        -MaxCpuCount 1
                )
                $desktopBuildArgs = @(
                    Get-MeridianBuildArguments `
                        -IsolationKey $buildIsolationKey `
                        -TargetFramework $resolvedFramework `
                        -AdditionalProperties @("Configuration=$resolvedConfiguration", 'UseSharedCompilation=false') `
                        -EnableFullWpfBuild `
                        -MaxCpuCount 1
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
        Complete-MeridianCheckpointStep -Context $checkpoint -StepId 'build-desktop' -ArtifactPointers @($exePath)
        Add-StageStatus -StageStatus $stageStatusEntries -Stage 'build-desktop' -Status 'ok' -Message 'Desktop build completed.' -Metadata @{ exePath = $exePath }
    }
    elseif (-not $SkipBuild) {
        Write-Info 'Skipping desktop build (checkpoint resume).'
        Add-StageStatus -StageStatus $stageStatusEntries -Stage 'build-desktop' -Status 'skipped' -Message 'Skipped via checkpoint resume.'
    }
    else {
        Add-StageStatus -StageStatus $stageStatusEntries -Stage 'build-desktop' -Status 'skipped' -Message 'Skipped because -SkipBuild was supplied.'
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
            $existingProcesses = @(Get-Process -Name $script:DesktopProcessName -ErrorAction SilentlyContinue)
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
                    [Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Development', 'Process')
                    [Environment]::SetEnvironmentVariable('ASPNETCORE_ENVIRONMENT', 'Development', 'Process')
                    [Environment]::SetEnvironmentVariable('MERIDIAN_USE_INMEMORY_GOVERNANCE', 'true', 'Process')
                    [Environment]::SetEnvironmentVariable('MDC_WPF_SOFTWARE_RENDERING', '1', 'Process')
                }
                else {
                    [Environment]::SetEnvironmentVariable('MDC_FIXTURE_MODE', $null, 'Process')
                    [Environment]::SetEnvironmentVariable('MDC_WPF_SOFTWARE_RENDERING', $null, 'Process')
                }

                $launchArguments = @()
                if ($useFixture) { $launchArguments += '--fixture' }

                if (Test-MeridianCheckpointStepShouldRun -Context $checkpoint -StepId 'launch-desktop') {
                    Add-StageStatus -StageStatus $stageStatusEntries -Stage 'launch-desktop' -Status 'running' -Message 'Launching Meridian desktop process.'
                    Start-MeridianCheckpointStep -Context $checkpoint -StepId 'launch-desktop' -Description 'Launch Meridian desktop app.'
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
                    Add-StageStatus -StageStatus $stageStatusEntries -Stage 'launch-desktop' -Status 'ok' -Message 'Desktop process launched.' -Metadata @{ processId = $ownedProcess.Id }
                    Write-Ok 'Meridian window detected.'
                    $launchStage.outputs = @{
                        processId = $ownedProcess.Id
                        stdoutLog = $stdoutPath
                        stderrLog = $stderrPath
                        requiredFiles = @($stdoutPath, $stderrPath)
                    }
                }
                else {
                    Write-Info 'Skipping desktop launch step marker (checkpoint resume).'
                    Add-StageStatus -StageStatus $stageStatusEntries -Stage 'launch-desktop' -Status 'skipped' -Message 'Skipped via checkpoint resume.'
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
    Add-StageStatus -StageStatus $stageStatusEntries -Stage 'ensure-operating-context' -Status 'ok' -Message 'Operating context confirmed.'

    Write-Ok 'Operating context confirmed.'

    $startupReadiness = Wait-ForStableShellPage -Process $ownedProcess -TimeoutSec ([Math]::Max(30, $LaunchTimeoutSec)) -StableMs 1200
    $window = $startupReadiness.Window
    $manifest.run.initialPageTag = $startupReadiness.State.PageTag
    $manifest.run.initialPageTitle = $startupReadiness.State.PageTitle
    Add-StageStatus -StageStatus $stageStatusEntries -Stage 'startup-readiness' -Status 'ok' -Message 'Shell readiness confirmed.' -Metadata @{ pageTag = $startupReadiness.State.PageTag; pageTitle = $startupReadiness.State.PageTitle }
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
        $expectedPageTag = Resolve-WorkflowPageTag -PageTag $pageTag
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
        $stepMaxRetryAttempts = [int](Get-ConfigValue -Table $step -Key 'retryAttempts' -Fallback $script:DefaultCaptureRetryAttempts)
        $captureName = [string](Get-ConfigValue -Table $step -Key 'captureName' -Fallback ('{0:D2}-{1}' -f $stepIndex, ($title -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()))
        $capturePath = if ($shouldCapture) { Join-Path $screenshotDirectory "$captureName.png" } else { $null }
        # Collect step-level additional accepted page tags and resolve each alias.
        $stepAdditionalPageTags = @()
        if ($step.Contains('additionalPageTags')) {
            $stepAdditionalPageTags = @(
                $step.additionalPageTags |
                    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
                    ForEach-Object { Resolve-WorkflowPageTag -PageTag ([string]$_) }
            )
        }
        if ($shouldCapture -and [string]::IsNullOrWhiteSpace($pageTag)) {
            Write-Warn ("Step {0} '{1}': capture is enabled but no pageTag is set — screenshot will use whatever page is currently active." -f $stepIndex, $title)
        }

        $stepResult = [ordered]@{
            index = $stepIndex
            title = $title
            pageTag = $pageTag
            expectedPageTag = $expectedPageTag
            notes = $notes
            keys = $keys
            launchArgs = $launchArgs
            capturePath = $capturePath
            bundleCapturePath = $null
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
            Add-StageStatus -StageStatus $stageStatusEntries -Stage $checkpointStepId -Status 'running' -Message $title

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
            $pageReadiness = Wait-ForShellPage -Process $ownedProcess -ExpectedPageTag $pageTag -TimeoutSec ([Math]::Max(8, [int][Math]::Ceiling(($stepWaitMs / 1000.0) + 4)))
            $window = $pageReadiness.Window
            $stepResult.observedPageTag = $pageReadiness.State.PageTag
            $stepResult.observedPageTitle = $pageReadiness.State.PageTitle

            $captureRetry = Invoke-MeridianRetry `
                -Name ("{0}/step-{1:D2}/{2}" -f $Workflow, $stepIndex, $captureName) `
                -MaxAttempts $stepMaxRetryAttempts `
                -BaseDelayMs ([Math]::Max(250, [int]($stepWaitMs / 3))) `
                -MaxDelayMs ([Math]::Max(1200, $stepWaitMs * 2)) `
                -JitterMs 180 `
                -TelemetrySink $retryTelemetry `
                -Predicate {
                    if ($null -ne $ownedProcess -and $ownedProcess.HasExited) {
                        return [pscustomobject]@{
                            ready = $false
                            failure = (New-MeridianRetryFailure -Code 'desktop.process.exited' -Reason "Meridian desktop exited unexpectedly with code $($ownedProcess.ExitCode)." -Data @{ exitCode = $ownedProcess.ExitCode })
                        }
                    }

                    $candidateWindow = Wait-MeridianWindow -TimeoutSec 5 -Process $ownedProcess
                    if ($null -eq $candidateWindow) {
                        return [pscustomobject]@{
                            ready = $false
                            failure = (New-MeridianRetryFailure -Code 'desktop.window.not_visible' -Reason 'Meridian window was not visible.' -Data @{})
                        }
                    }

                    $rect = $candidateWindow.Current.BoundingRectangle
                    if ($rect.Width -lt 200 -or $rect.Height -lt 200) {
                        return [pscustomobject]@{
                            ready = $false
                            failure = (New-MeridianRetryFailure -Code 'desktop.window.too_small' -Reason "Window bounds are too small for capture ($($rect.Width)x$($rect.Height))." -Data @{ width = $rect.Width; height = $rect.Height })
                        }
                    }

                    $shellState = Get-ShellAutomationState -Window $candidateWindow
                    if (-not $shellState.Ready -or [string]::IsNullOrWhiteSpace($shellState.PageTag)) {
                        return [pscustomobject]@{
                            ready = $false
                            failure = (New-MeridianRetryFailure -Code 'desktop.automation.marker_missing' -Reason 'Shell automation readiness marker was not present.' -Data @{ pageTag = $shellState.PageTag; pageTitle = $shellState.PageTitle })
                        }
                    }

                    $expectedTags = @()
                    if (-not [string]::IsNullOrWhiteSpace($expectedPageTag)) { $expectedTags += $expectedPageTag }
                    if ($stepAdditionalPageTags.Count -gt 0) { $expectedTags += $stepAdditionalPageTags }
                    $expectedTags = @($expectedTags | Select-Object -Unique)

                    if ($expectedTags.Count -gt 0 -and -not ($expectedTags -contains $shellState.PageTag)) {
                        return [pscustomobject]@{
                            ready = $false
                            failure = (New-MeridianRetryFailure -Code 'desktop.route.not_active' -Reason "Expected page tag '$pageTag' was not active." -Data @{ expectedTags = $expectedTags; observedPageTag = $shellState.PageTag; observedPageTitle = $shellState.PageTitle })
                        }
                    }

                    return [pscustomobject]@{
                        ready = $true
                        window = $candidateWindow
                        state = $shellState
                    }
                } `
                -Action {
                    param($readiness)

                    Activate-MeridianWindow | Out-Null
                    if ($shouldCapture -and $null -ne $capturePath) {
                        try {
                            $savedPath = Save-InProcessWindowCapture -Path $capturePath
                        }
                        catch {
                            Write-Warn "In-process desktop screenshot capture failed; falling back to native window capture: $($_.Exception.Message)"
                            $savedPath = Save-WindowCapture -Window $readiness.window -Path $capturePath
                        }

                        return [pscustomobject]@{
                            capturePath = $savedPath
                            state = $readiness.state
                            window = $readiness.window
                        }
                    }

                    return [pscustomobject]@{
                        capturePath = $null
                        state = $readiness.state
                        window = $readiness.window
                    }
                }

            if (-not $captureRetry.Success) {
                $failure = $captureRetry.Failure
                $failureCode = if ($failure -and $failure.PSObject.Properties.Name -contains 'code') { $failure.code } else { 'retry.unknown_failure' }
                $failureReason = if ($failure -and $failure.PSObject.Properties.Name -contains 'reason') { $failure.reason } else { 'Capture readiness did not succeed before retries were exhausted.' }
                throw "[${failureCode}] $failureReason"
            }

            $window = $captureRetry.Value.window
            $stepResult.retryAttempts = $captureRetry.Attempts
            $stepResult.retryName = $captureRetry.Telemetry.name
            $stepResult.observedPageTag = $captureRetry.Value.state.PageTag
            $stepResult.observedPageTitle = $captureRetry.Value.state.PageTitle

            if ($shouldCapture -and $null -ne $capturePath) {
                $savedPath = $captureRetry.Value.capturePath
                $stepResult.capturePath = $savedPath
                $bundleCapturePath = Join-Path $bundleScreenshotDirectory ([System.IO.Path]::GetFileName($savedPath))
                Copy-Item -LiteralPath $savedPath -Destination $bundleCapturePath -Force
                $stepResult.bundleCapturePath = $bundleCapturePath
                Write-Ok "Saved $savedPath"
            }

            $stepResult.status = 'ok'
            $stepResult.completedAt = (Get-Date).ToString('o')
            $lastSuccessfulStep = [ordered]@{
                index = $stepResult.index
                title = $stepResult.title
                pageTag = $stepResult.pageTag
                observedPageTag = $stepResult.observedPageTag
                observedPageTitle = $stepResult.observedPageTitle
                capturePath = $stepResult.capturePath
                bundleCapturePath = $stepResult.bundleCapturePath
                completedAt = $stepResult.completedAt
            }
            Complete-MeridianCheckpointStep -Context $checkpoint -StepId $checkpointStepId -ArtifactPointers @($capturePath)
            Add-StageStatus -StageStatus $stageStatusEntries -Stage $checkpointStepId -Status 'ok' -Message $title -Metadata @{ capturePath = $capturePath }
        }
        catch {
            $stepResult.status = 'failed'
            $stepResult.error = $_.Exception.Message
            $stepResult.completedAt = (Get-Date).ToString('o')
            Add-StageStatus -StageStatus $stageStatusEntries -Stage $checkpointStepId -Status 'failed' -Message $_.Exception.Message -Metadata @{ title = $title; index = $stepIndex }
            try {
                if ($null -ne $window) {
                    $failedCapturePath = Join-Path $bundleScreenshotDirectory ('{0:D2}-{1}-failed-attempt.png' -f $stepIndex, (($title -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()))
                    $stepResult.failedAttemptCapturePath = Save-WindowCapture -Window $window -Path $failedCapturePath
                }
            }
            catch {
                Write-Warn "Failed to capture failed-attempt screenshot for step '$title': $($_.Exception.Message)"
            }
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
            $captureStage.status = 'failed'
            $captureStage.errors = @("Failed steps: $captureFailed")
            $stageStatuses['capture'] = 'failed'
            throw "Capture stage failed: $captureFailed step(s) failed."
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
                    $resolvedSource = [System.IO.Path]::GetFullPath([string]$step.capturePath)
                    $resolvedDest = [System.IO.Path]::GetFullPath($destination)
                    if ($resolvedSource -ne $resolvedDest) {
                        Copy-Item -LiteralPath $step.capturePath -Destination $destination -Force
                    }
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
    Add-StageStatus -StageStatus $stageStatusEntries -Stage 'workflow' -Status 'failed' -Message $_.Exception.Message
    throw
}
finally {
    $manifest.run.finishedAt = (Get-Date).ToString('o')
    $manifest.retryTelemetry = @($retryTelemetry)
    $manifest.retrySummary = [ordered]@{
        total = $retryTelemetry.Count
        failures = @(
            $retryTelemetry |
                Where-Object { $_.status -eq 'failed' -and $null -ne $_.failure } |
                Group-Object { $_.failure.code } |
                Sort-Object Count -Descending |
                ForEach-Object {
                    [ordered]@{
                        code = $_.Name
                        count = $_.Count
                    }
                }
        )
    }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $stageStatusEntries | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $bundleStageStatusPath -Encoding utf8
    if ($null -ne $lastSuccessfulStep) {
        $lastSuccessfulStep | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bundleLastSuccessfulStepPath -Encoding utf8
    }
    else {
        [ordered]@{
            message = 'No successful workflow step was recorded.'
            generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $bundleLastSuccessfulStepPath -Encoding utf8
    }

    try {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'summarize-desktop-workflow-bundle.ps1') `
            -BundlePath $bundleDirectory `
            -ManifestPath $manifestPath `
            -WorkflowName $workflowDefinition.name `
            -UseFixture:$useFixture `
            -SkipBuild:$SkipBuild `
            -ReuseExistingApp:$ReuseExistingApp | Out-Null
    }
    catch {
        Write-Warn "Failed to summarize desktop workflow bundle: $($_.Exception.Message)"
    }
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

    if ($fixtureFileStates.Count -gt 0 -and (-not $KeepAppOpen -or $null -eq $ownedProcess -or $ownedProcess.HasExited)) {
        Restore-FileState -States $fixtureFileStates
    }

    foreach ($entry in $originalWorkflowEnv.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
    }
    if ($workflowTranscriptActive) {
        try {
            Stop-Transcript | Out-Null
        }
        catch {
            Write-Warn "Failed to stop workflow transcript: $($_.Exception.Message)"
        }
    }
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
