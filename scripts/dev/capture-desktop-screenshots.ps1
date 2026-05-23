[CmdletBinding()]
param(
  [string]$Profile = 'screenshot-catalog',
  [string]$ProfileRoot = 'scripts/dev/workflow-profiles',
  [string]$ProjectPath,
  [string]$Configuration,
  [string]$Framework,
  [string]$ExeName,
  [string]$OutputDir,
  [switch]$SkipBuild,
  [switch]$KeepAppOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $PSScriptRoot 'SharedBuild.ps1')
. (Join-Path $PSScriptRoot 'shared/retry.ps1')
$null = & (Join-Path $PSScriptRoot 'validate-workflow-profile.ps1') -Profile $Profile -ProfileRoot $ProfileRoot -EmitJson
. (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')
$profileEnvelope = Get-MeridianWorkflowProfile -RepoRoot $repoRoot -ProfileName $Profile -ProfileRoot $ProfileRoot
$buildProfile = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'build' -Fallback @{}
$fixtureProfile = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'fixture' -Fallback @{}
$screenshotsProfile = Get-MeridianWorkflowProfileValue -Table $profileEnvelope.data -Key 'screenshots' -Fallback @{}
$retentionProfile = Get-MeridianWorkflowProfileValue -Table $screenshotsProfile -Key 'retention' -Fallback @{}

$ProjectPath = if ($PSBoundParameters.ContainsKey('ProjectPath')) { $ProjectPath } else { [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'projectPath' -Fallback 'src/Meridian.Wpf/Meridian.Wpf.csproj') }
$Configuration = if ($PSBoundParameters.ContainsKey('Configuration')) { $Configuration } else { [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'configuration' -Fallback 'Release') }
$Framework = if ($PSBoundParameters.ContainsKey('Framework')) { $Framework } else { [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'framework' -Fallback 'net10.0-windows10.0.19041.0') }
$ExeName = if ($PSBoundParameters.ContainsKey('ExeName')) { $ExeName } else { [string](Get-MeridianWorkflowProfileValue -Table $buildProfile -Key 'exeName' -Fallback 'Meridian.Desktop.exe') }
$OutputDir = if ($PSBoundParameters.ContainsKey('OutputDir')) { $OutputDir } else { [string](Get-MeridianWorkflowProfileValue -Table $screenshotsProfile -Key 'outputRoot' -Fallback 'docs/screenshots/desktop') }
$fixtureRequired = [bool](Get-MeridianWorkflowProfileValue -Table $fixtureProfile -Key 'required' -Fallback $true)
$retentionDays = [int](Get-MeridianWorkflowProfileValue -Table $retentionProfile -Key 'maxAgeDays' -Fallback 14)
$retentionLatest = [int](Get-MeridianWorkflowProfileValue -Table $retentionProfile -Key 'retainLatest' -Fallback 10)
$script:MeridianProcessId = 0
$script:MeridianProcess = $null
$resolvedProjectPath = if ([System.IO.Path]::IsPathRooted($ProjectPath)) {
  [System.IO.Path]::GetFullPath($ProjectPath)
}
else {
  [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
}
$buildIsolationKey = New-MeridianBuildIsolationKey -Prefix 'desktop-screenshots'
Invoke-MeridianWorkflowArtifactRetention -OutputRoot ([System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))) -MaxAgeDays $retentionDays -RetainLatest $retentionLatest

function Assert-Command {
  param([string]$Name)
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "Required command '$Name' was not found in PATH."
  }
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
  } catch {
    # Ignore transient process/window state while WPF is navigating.
  }

  return $null
}

function Find-MeridianWindow {
  param(
    [System.Diagnostics.Process]$Process = $script:MeridianProcess,
    [int]$ProcessId = 0
  )

  $processWindow = Get-MeridianWindowFromProcess -Process $Process
  if ($null -ne $processWindow) {
    return $processWindow
  }

  $targetProcessId = if ($ProcessId -gt 0) {
    $ProcessId
  }
  elseif ($script:MeridianProcessId -gt 0) {
    $script:MeridianProcessId
  }
  else {
    0
  }

  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $all = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.Condition]::TrueCondition
  )

  foreach ($w in $all) {
    try {
      if ($targetProcessId -gt 0 -and $w.Current.ProcessId -eq $targetProcessId) {
        return $w
      }

      if ($w.Current.Name -match 'Meridian') {
        return $w
      }
    } catch {
      # Ignore transient UIA read failures.
    }
  }

  return $null
}

function Activate-MeridianWindow {
  try {
    $wshShell = Get-Variable -Scope Script -Name WshShell -ValueOnly -ErrorAction SilentlyContinue
    if (-not $wshShell) {
      $script:WshShell = New-Object -ComObject WScript.Shell
      $wshShell = $script:WshShell
    }

    $activationTarget = if ($script:MeridianProcessId -gt 0) {
      $script:MeridianProcessId
    }
    else {
      'Meridian'
    }

    [void]$wshShell.AppActivate($activationTarget)
    Start-Sleep -Milliseconds 400
    return $true
  } catch {
    Write-Warning "Failed to activate Meridian window: $_"
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
    } catch {
      # Ignore transient UI Automation timeouts while the tree is updating.
    }

    Start-Sleep -Milliseconds $DelayMilliseconds
  }

  return $null
}

function Wait-MeridianWindow {
  param(
    [System.Diagnostics.Process]$Process = $script:MeridianProcess,
    [int]$TimeoutSec = 8
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  while ((Get-Date) -lt $deadline) {
    if ($null -ne $Process -and $Process.HasExited) {
      return $null
    }

    $window = Find-MeridianWindow -Process $Process
    if ($null -ne $window) {
      return $window
    }

    Start-Sleep -Milliseconds 250
  }

  return $null
}

function Ensure-EnteredFundProfile {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window
  )

  $shellAutomationCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'ShellAutomationState'
  )
  $pageTitleCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'PageTitleText'
  )

  function Find-ShellMarker {
    $currentWindow = Find-MeridianWindow
    if (-not $currentWindow) { return $null }

    $shellAutomation = $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $shellAutomationCond)
    if ($shellAutomation) {
      return $shellAutomation
    }

    return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $pageTitleCond)
  }

  $shell = Wait-ForElement -Attempts 4 -DelayMilliseconds 250 -Finder {
    return Find-ShellMarker
  }

  if ($shell) {
    return $true
  }

  $buttonType = [System.Windows.Automation.ControlType]::Button
  $enterWorkstationIdCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'EnterWorkstationButton'
  )
  $enterWorkstationNameCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::NameProperty,
      'Enter Workstation'
    )),
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      $buttonType
    ))
  )
  $enterFundCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::NameProperty,
      'Enter Fund'
    )),
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      $buttonType
    ))
  )
  $seedContextsCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::NameProperty,
      'Seed Sample Contexts'
    )),
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      $buttonType
    ))
  )
  $seedProfilesCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::NameProperty,
      'Seed Sample Profiles'
    )),
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      $buttonType
    ))
  )

  function Find-EnterWorkstationButton {
    $currentWindow = Find-MeridianWindow
    if (-not $currentWindow) { return $null }

    $button = $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $enterWorkstationIdCond)
    if ($button) { return $button }

    $button = $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $enterWorkstationNameCond)
    if ($button) { return $button }

    return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $enterFundCond)
  }

  function Find-SeedSampleContextsButton {
    $currentWindow = Find-MeridianWindow
    if (-not $currentWindow) { return $null }

    $button = $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $seedContextsCond)
    if ($button) { return $button }

    return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $seedProfilesCond)
  }

  $enterFund = Wait-ForElement -Attempts 10 -DelayMilliseconds 300 -Finder {
    return Find-EnterWorkstationButton
  }

  if ($enterFund -and -not $enterFund.Current.IsEnabled) {
    $seedProfiles = Wait-ForElement -Attempts 5 -DelayMilliseconds 250 -Finder {
      return Find-SeedSampleContextsButton
    }

    if ($seedProfiles) {
      try {
        $seedInvoke = $seedProfiles.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
        $seedInvoke.Invoke()
        Start-Sleep -Seconds 2
      } catch {
        Write-Warning "Failed to seed sample fund profiles: $_"
      }
    }

    $enterFund = Wait-ForElement -Attempts 10 -DelayMilliseconds 300 -Finder {
      return Find-EnterWorkstationButton
    }
  }

  if (-not $enterFund) {
    Write-Warning 'Fund profile selection view did not expose an Enter Workstation button.'
    return $false
  }

  try {
    Activate-MeridianWindow | Out-Null
    $enterInvoke = $enterFund.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
    $enterInvoke.Invoke()
  } catch {
    Write-Warning "Failed to enter the selected fund profile: $_"
    return $false
  }

  $shell = Wait-ForElement -Attempts 20 -DelayMilliseconds 500 -Finder {
    return Find-ShellMarker
  }

  if (-not $shell) {
    Write-Warning 'Main workstation shell did not appear after entering the fund profile.'
    return $false
  }

  Start-Sleep -Seconds 1
  return $true
}

function Maximize-MeridianWindow {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window
  )

  try {
    $pattern = $Window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern) -as [System.Windows.Automation.WindowPattern]
    if ($pattern -and $pattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized) {
      $pattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
      Start-Sleep -Milliseconds 700
    }
  } catch {
    Write-Warning "Failed to maximize Meridian window: $_"
  }
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
    Write-Warning "Skipping capture for '$Path' because window bounds are too small ($($rect.Width)x$($rect.Height))."
    return
  }

  $bmp = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
  $graphics = [System.Drawing.Graphics]::FromImage($bmp)
  try {
    $graphics.CopyFromScreen(
      [int]$rect.X,
      [int]$rect.Y,
      0,
      0,
      [System.Drawing.Size]::new([int]$rect.Width, [int]$rect.Height)
    )
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Host "Saved: $Path"
  } finally {
    $graphics.Dispose()
    $bmp.Dispose()
  }
}

function Find-NavigationElementByName {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window,

    [Parameter(Mandatory = $true)]
    [string]$Name
  )

  $nameCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    $Name
  )
  $listItemCond = New-Object System.Windows.Automation.AndCondition(
    $nameCond,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::ListItem
    ))
  )
  $buttonCond = New-Object System.Windows.Automation.AndCondition(
    $nameCond,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
      [System.Windows.Automation.ControlType]::Button
    ))
  )

  $element = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $listItemCond)
  if ($element) { return $element }

  $element = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCond)
  if ($element) { return $element }

  return $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
}

function Invoke-ElementMouseClick {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Element
  )

  try {
    $point = $Element.GetClickablePoint()
    [NativeDesktopInput]::SetCursorPos([int]$point.X, [int]$point.Y) | Out-Null
    Start-Sleep -Milliseconds 75
    [NativeDesktopInput]::mouse_event([NativeDesktopInput]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 25
    [NativeDesktopInput]::mouse_event([NativeDesktopInput]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [System.UIntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    return $true
  } catch {
    return $false
  }
}

function Invoke-NavigateWithKeyboard {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window,

    [System.Diagnostics.Process]$Process = $script:MeridianProcess,

    [Parameter(Mandatory = $true)]
    [string]$SearchTerm
  )

  try {
    Activate-MeridianWindow | Out-Null

    [System.Windows.Forms.SendKeys]::SendWait('^k')
    Start-Sleep -Milliseconds 300

    $cond = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
      'CommandPaletteInput'
    )

    $inputEl = Wait-ForElement -Attempts 15 -DelayMilliseconds 250 -Finder {
      $currentWindow = Wait-MeridianWindow -Process $Process -TimeoutSec 2
      if (-not $currentWindow) {
        $currentWindow = $Window
      }

      if (-not $currentWindow) { return $null }
      return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    }

    if (-not $inputEl) {
      $commandPaletteButtonCond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
          [System.Windows.Automation.AutomationElement]::NameProperty,
          'Open Command Palette'
        )),
        (New-Object System.Windows.Automation.PropertyCondition(
          [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
          [System.Windows.Automation.ControlType]::Button
        ))
      )
      $commandPaletteButton = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $commandPaletteButtonCond)
      if ($commandPaletteButton) {
        try {
          $paletteInvoke = $commandPaletteButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
          if ($paletteInvoke) {
            $paletteInvoke.Invoke()
          }
          else {
            Invoke-ElementMouseClick -Element $commandPaletteButton | Out-Null
          }
        } catch {
          Invoke-ElementMouseClick -Element $commandPaletteButton | Out-Null
        }

        $inputEl = Wait-ForElement -Attempts 12 -DelayMilliseconds 250 -Finder {
          $currentWindow = Wait-MeridianWindow -Process $Process -TimeoutSec 2
          if (-not $currentWindow) {
            $currentWindow = $Window
          }

          if (-not $currentWindow) { return $null }
          return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        }
      }
    }

    if (-not $inputEl) {
      Write-Warning "Command palette input not found for '$SearchTerm'."
      [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
      return $false
    }

    $valuePattern = $inputEl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) -as [System.Windows.Automation.ValuePattern]
    if ($valuePattern) {
      $valuePattern.SetValue('')
      $valuePattern.SetValue($SearchTerm)
    }
    else {
      try { $inputEl.SetFocus() } catch {}
      [System.Windows.Forms.SendKeys]::SendWait('^a')
      [System.Windows.Forms.SendKeys]::SendWait('{BACKSPACE}')
      [System.Windows.Forms.SendKeys]::SendWait($SearchTerm)
    }

    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 900
    return $true
  } catch {
    Write-Warning "Keyboard navigation failed for '$SearchTerm': $_"
    return $false
  }
}

function Invoke-NavigateWithMouse {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window,

    [System.Diagnostics.Process]$Process = $script:MeridianProcess,

    [Parameter(Mandatory = $true)]
    [string]$SearchTerm
  )

  try {
    Activate-MeridianWindow | Out-Null
    $currentWindow = Wait-MeridianWindow -Process $Process -TimeoutSec 4
    if (-not $currentWindow) {
      $currentWindow = $Window
    }

    if (-not $currentWindow) {
      return $false
    }

    $targetElement = Find-NavigationElementByName -Window $currentWindow -Name $SearchTerm
    if (-not $targetElement) {
      return $false
    }

    $selectionPattern = $targetElement.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern) -as [System.Windows.Automation.SelectionItemPattern]
    if ($selectionPattern) {
      $selectionPattern.Select()
      Start-Sleep -Milliseconds 400
      return $true
    }

    $invokePattern = $targetElement.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
    if ($invokePattern) {
      $invokePattern.Invoke()
      Start-Sleep -Milliseconds 400
      return $true
    }

    return (Invoke-ElementMouseClick -Element $targetElement)
  } catch {
    Write-Warning "Mouse navigation failed for '$SearchTerm': $_"
    return $false
  }
}

function New-ScreenNavigationRegistry {
  return @(
    [pscustomobject]@{ slug = 'dashboard'; searchTerm = 'System Overview'; skipNavigation = $true; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'providers'; searchTerm = 'Providers'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'provider-health'; searchTerm = 'Provider Health'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'backfill'; searchTerm = 'Backfill'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'symbols'; searchTerm = 'Symbols'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'live-data'; searchTerm = 'Live Data'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'storage'; searchTerm = 'Storage'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'data-quality'; searchTerm = 'Data Quality'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'data-browser'; searchTerm = 'Data Browser'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'strategy-runs'; searchTerm = 'Strategy Runs'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'backtest'; searchTerm = 'Backtest'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'quant-script'; searchTerm = 'Quant Script'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'security-master'; searchTerm = 'Security Master'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'diagnostics'; searchTerm = 'Diagnostics'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
    [pscustomobject]@{ slug = 'settings'; searchTerm = 'Settings'; skipNavigation = $false; navigationTimeoutSec = 8; readinessRetryAttempts = 6; captureRetryAttempts = 6 }
  )
}

function Select-ScreenNavigationMethod {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window,

    [System.Diagnostics.Process]$Process = $script:MeridianProcess,

    [Parameter(Mandatory = $true)]
    [string]$SearchTerm,

    [scriptblock]$KeyboardNavigationAction = {
      param($window, $process, $searchTerm)
      return (Invoke-NavigateWithKeyboard -Window $window -Process $process -SearchTerm $searchTerm)
    },

    [scriptblock]$MouseNavigationAction = {
      param($window, $process, $searchTerm)
      return (Invoke-NavigateWithMouse -Window $window -Process $process -SearchTerm $searchTerm)
    }
  )

  if (& $KeyboardNavigationAction $Window $Process $SearchTerm) {
    return [pscustomobject]@{
      success = $true
      method = 'keyboard'
      category = 'navigation_success'
    }
  }

  Write-Warning "Retrying '$SearchTerm' navigation with mouse automation fallback."
  if (& $MouseNavigationAction $Window $Process $SearchTerm) {
    return [pscustomobject]@{
      success = $true
      method = 'mouse'
      category = 'navigation_success'
    }
  }

  return [pscustomobject]@{
    success = $false
    method = 'none'
    category = 'navigation_failure'
    failure = (New-MeridianRetryFailure -Code 'desktop.navigation.failed' -Reason "Unable to navigate to '$SearchTerm' with keyboard or mouse strategies." -Data @{ page = $SearchTerm })
  }
}

function Invoke-ScreenNavigation {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window,

    [Parameter(Mandatory = $true)]
    [pscustomobject]$Screen,

    [System.Diagnostics.Process]$Process = $script:MeridianProcess
  )

  if ($Screen.skipNavigation) {
    Write-Host "Capturing '$($Screen.searchTerm)' from the detected shell window."
    return [pscustomobject]@{
      success = $true
      method = 'none'
      category = 'navigation_skipped'
    }
  }

  Write-Host "Navigating to '$($Screen.searchTerm)' ..."
  return (Select-ScreenNavigationMethod -Window $Window -Process $Process -SearchTerm $Screen.searchTerm)
}

function Test-ScreenReadiness {
  param(
    [System.Diagnostics.Process]$Process = $script:MeridianProcess,

    [Parameter(Mandatory = $true)]
    [pscustomobject]$Screen
  )

  $candidateWindow = Find-MeridianWindow -Process $Process
  if ($null -eq $candidateWindow) {
    return [pscustomobject]@{
      ready = $false
      failure = (New-MeridianRetryFailure -Code 'desktop.window.not_visible' -Reason "Window was not visible while capturing '$($Screen.searchTerm)'." -Data @{ page = $Screen.searchTerm; slug = $Screen.slug })
    }
  }

  $shellMarker = $candidateWindow.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
      'ShellAutomationState'
    ))
  )

  if ($null -eq $shellMarker) {
    return [pscustomobject]@{
      ready = $false
      failure = (New-MeridianRetryFailure -Code 'desktop.automation.marker_missing' -Reason "Shell automation marker was not found while capturing '$($Screen.searchTerm)'." -Data @{ page = $Screen.searchTerm; slug = $Screen.slug })
    }
  }

  return [pscustomobject]@{
    ready = $true
    window = $candidateWindow
  }
}

function Wait-ScreenReady {
  param(
    [System.Diagnostics.Process]$Process = $script:MeridianProcess,

    [Parameter(Mandatory = $true)]
    [pscustomobject]$Screen,

    [System.Collections.ArrayList]$TelemetrySink = $null
  )

  return (Invoke-MeridianRetry `
    -Name ("capture/page/{0}/ready" -f $Screen.slug) `
    -MaxAttempts $Screen.readinessRetryAttempts `
    -BaseDelayMs 350 `
    -MaxDelayMs 2500 `
    -JitterMs 180 `
    -TelemetrySink $TelemetrySink `
    -Predicate {
      return (Test-ScreenReadiness -Process $Process -Screen $Screen)
    } `
    -Action {
      param($state)
      return $state.window
    })
}

function Invoke-ScreenCapture {
  param(
    [Parameter(Mandatory = $true)]
    [pscustomobject]$Screen,

    [System.Diagnostics.Process]$Process = $script:MeridianProcess,

    [System.Windows.Automation.AutomationElement]$Window = $null,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [System.Collections.ArrayList]$TelemetrySink = $null,

    [scriptblock]$NavigationInvoker = {
      param($window, $process, $screen)
      return (Invoke-ScreenNavigation -Window $window -Process $process -Screen $screen)
    },

    [scriptblock]$ReadinessWaiter = {
      param($process, $screen, $telemetry)
      return (Wait-ScreenReady -Process $process -Screen $screen -TelemetrySink $telemetry)
    },

    [scriptblock]$CaptureInvoker = {
      param($window, $targetPath)
      Save-WindowCapture -Window $window -Path $targetPath
      return $targetPath
    }
  )

  $screenResult = [ordered]@{
    slug = $Screen.slug
    screen = $Screen.searchTerm
    status = 'failure'
    category = 'uninitialized'
    message = ''
    navigationMethod = 'none'
    path = $null
    window = $Window
  }

  try {
    if (-not $Window) {
      $Window = Wait-MeridianWindow -Process $Process -TimeoutSec $Screen.navigationTimeoutSec
    }

    if (-not $Window) {
      $screenResult.status = 'warning'
      $screenResult.category = 'window_missing'
      $screenResult.message = "Skipping '$($Screen.searchTerm)' because the main window reference was lost."
      return [pscustomobject]$screenResult
    }

    $navigation = & $NavigationInvoker $Window $Process $Screen
    $screenResult.navigationMethod = $navigation.method
    if (-not $navigation.success) {
      $screenResult.status = 'warning'
      $screenResult.category = 'navigation_failure'
      $screenResult.message = "Skipping '$($Screen.searchTerm)' because navigation failed."
      $screenResult.window = Wait-MeridianWindow -Process $Process -TimeoutSec $Screen.navigationTimeoutSec
      return [pscustomobject]$screenResult
    }

    $readyResult = & $ReadinessWaiter $Process $Screen $TelemetrySink
    if (-not $readyResult.Success) {
      $failure = $readyResult.Failure
      $screenResult.status = 'warning'
      $screenResult.category = 'timeout'
      $screenResult.message = ("Skipping '{0}' because readiness checks were exhausted [{1}] {2}" -f $Screen.searchTerm, $failure.code, $failure.reason)
      $screenResult.window = Wait-MeridianWindow -Process $Process -TimeoutSec $Screen.navigationTimeoutSec
      return [pscustomobject]$screenResult
    }

    $targetPath = Join-Path $OutputDirectory ("wpf-{0}.png" -f $Screen.slug)
    $captureRetry = Invoke-MeridianRetry `
      -Name ("capture/page/$($Screen.slug)") `
      -MaxAttempts $Screen.captureRetryAttempts `
      -BaseDelayMs 350 `
      -MaxDelayMs 2500 `
      -JitterMs 180 `
      -TelemetrySink $TelemetrySink `
      -Predicate {
        return (Test-ScreenReadiness -Process $Process -Screen $Screen)
      } `
      -Action {
        param($state)
        return (& $CaptureInvoker $state.window $targetPath)
      }

    if ($captureRetry.Success) {
      $screenResult.status = 'success'
      $screenResult.category = 'capture_success'
      $screenResult.message = "Saved: $($captureRetry.Value)"
      $screenResult.path = $captureRetry.Value
      $screenResult.window = Wait-MeridianWindow -Process $Process -TimeoutSec $Screen.navigationTimeoutSec
      return [pscustomobject]$screenResult
    }

    $captureFailure = $captureRetry.Failure
    $screenResult.status = 'warning'
    $screenResult.category = 'capture_failure'
    $screenResult.message = ("Skipping '{0}' due to retry exhaustion [{1}] {2}" -f $Screen.searchTerm, $captureFailure.code, $captureFailure.reason)
    $screenResult.window = Wait-MeridianWindow -Process $Process -TimeoutSec $Screen.navigationTimeoutSec
    return [pscustomobject]$screenResult
  } catch {
    $screenResult.status = 'failure'
    $screenResult.category = 'unexpected_error'
    $screenResult.message = ("Capture failed for '{0}': {1}" -f $Screen.searchTerm, $_.Exception.Message)
    $screenResult.window = Wait-MeridianWindow -Process $Process -TimeoutSec $Screen.navigationTimeoutSec
    return [pscustomobject]$screenResult
  }
}

Assert-Command -Name 'dotnet'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationClient')
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationTypes')
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class NativeDesktopInput
{
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
"@

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if (-not (Test-Path 'config/appsettings.json')) {
  Copy-Item 'config/appsettings.sample.json' 'config/appsettings.json' -Force
  Write-Host 'Created config/appsettings.json from sample.'
}

if (-not $SkipBuild) {
  Write-Host "Restoring $ProjectPath ..."
  dotnet restore $resolvedProjectPath --verbosity minimal @(
    Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -EnableFullWpfBuild
  )

  Write-Host "Building $ProjectPath ($Configuration) ..."
  dotnet build $resolvedProjectPath -c $Configuration --no-restore --verbosity minimal @(
    Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -TargetFramework $Framework -EnableFullWpfBuild
  )
}

if ($fixtureRequired) {
  $env:MDC_FIXTURE_MODE = '1'
}
$exePath = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath $resolvedProjectPath -Configuration $Configuration -Framework $Framework -BinaryName $ExeName -IsolationKey $buildIsolationKey
$runStamp = (Get-Date).ToString('yyyyMMddHHmmss')
$stdoutPath = Join-Path $OutputDir "wpf-startup-stdout-$runStamp.log"
$stderrPath = Join-Path $OutputDir "wpf-startup-stderr-$runStamp.log"
$retryTelemetry = New-Object System.Collections.ArrayList
$screenDiagnostics = New-Object System.Collections.ArrayList
$retryTelemetryPath = Join-Path $OutputDir 'retry-telemetry.json'

if (-not (Test-Path $exePath)) {
  throw "WPF executable was not found at '$exePath'. Build the project or check your parameters."
}

Write-Host "Starting $exePath in fixture mode..."
$proc = Start-Process -FilePath $exePath -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru -WindowStyle Normal
$script:MeridianProcessId = $proc.Id
$script:MeridianProcess = $proc

try {
  $window = $null
  Write-Host 'Waiting for Meridian window (up to 90 seconds)...'
  $windowRetry = Invoke-MeridianRetry `
    -Name 'capture/launch/window-visible' `
    -MaxAttempts 45 `
    -BaseDelayMs 500 `
    -MaxDelayMs 2000 `
    -JitterMs 150 `
    -TelemetrySink $retryTelemetry `
    -Predicate {
      if ($proc.HasExited) {
        return [pscustomobject]@{
          ready = $false
          failure = (New-MeridianRetryFailure -Code 'desktop.process.exited' -Reason "WPF process exited before the main window appeared (exit code $($proc.ExitCode))." -Data @{ exitCode = $proc.ExitCode })
        }
      }

      $candidateWindow = Find-MeridianWindow -Process $proc
      if ($null -eq $candidateWindow) {
        return [pscustomobject]@{
          ready = $false
          failure = (New-MeridianRetryFailure -Code 'desktop.window.not_visible' -Reason 'Meridian window was not visible yet.' -Data @{})
        }
      }

      return [pscustomobject]@{
        ready = $true
        window = $candidateWindow
      }
    } `
    -Action {
      param($state)
      return $state.window
    }

  if (-not $windowRetry.Success) {
    if (Test-Path $stdoutPath) { Write-Host '--- stdout ---'; Get-Content $stdoutPath | Write-Host }
    if (Test-Path $stderrPath) { Write-Host '--- stderr ---'; Get-Content $stderrPath | Write-Host }
    $launchFailure = $windowRetry.Failure
    throw "[{0}] {1}" -f $launchFailure.code, $launchFailure.reason
  }
  $window = $windowRetry.Value
  Write-Host 'Meridian window detected.'

  if (-not (Ensure-EnteredFundProfile -Window $window)) {
    Write-Warning 'Unable to enter the fund profile selector. Continuing without page navigation.'
  }

  $window = Find-MeridianWindow -Process $proc
  if ($window) {
    Maximize-MeridianWindow -Window $window
  }

  $screenRegistry = New-ScreenNavigationRegistry
  foreach ($screen in $screenRegistry) {
    $screenResult = Invoke-ScreenCapture -Screen $screen -Process $proc -Window $window -OutputDirectory $OutputDir -TelemetrySink $retryTelemetry
    $window = $screenResult.window
    $null = $screenDiagnostics.Add($screenResult)

    switch ($screenResult.status) {
      'success' { Write-Host $screenResult.message }
      'warning' { Write-Warning $screenResult.message }
      default { Write-Warning ("Failure: {0}" -f $screenResult.message) }
    }
  }

  $successCount = @($screenDiagnostics | Where-Object { $_.status -eq 'success' }).Count
  $warningCount = @($screenDiagnostics | Where-Object { $_.status -eq 'warning' }).Count
  $failureCount = @($screenDiagnostics | Where-Object { $_.status -eq 'failure' }).Count
  Write-Host ("Desktop screenshot summary: success={0}, warnings={1}, failures={2}" -f $successCount, $warningCount, $failureCount)

  if ($warningCount -gt 0 -or $failureCount -gt 0) {
    Write-Host 'Screen diagnostics:'
    $screenDiagnostics |
      Select-Object slug, screen, status, category, navigationMethod, message |
      Format-Table -AutoSize
  }

  Write-Host "Desktop screenshots complete. Output directory: $OutputDir"
  Get-ChildItem $OutputDir -Filter '*.png' |
    Select-Object Name, @{ Name = 'SizeKB'; Expression = { '{0:N0}' -f ($_.Length / 1KB) } } |
    Format-Table -AutoSize
}
finally {
  $script:MeridianProcessId = 0
  $script:MeridianProcess = $null
  $retryReport = [ordered]@{
    generatedAt = (Get-Date).ToString('o')
    outputDirectory = $OutputDir
    telemetry = @($retryTelemetry)
    screenDiagnostics = @($screenDiagnostics | Select-Object slug, screen, status, category, navigationMethod, path, message)
    failureRanking = @(
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
  $retryReport | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $retryTelemetryPath -Encoding utf8

  if (-not $KeepAppOpen) {
    Write-Host 'Stopping WPF process...'
    try { $proc | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
    Get-Process -Name 'Meridian.Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  }
}
