[CmdletBinding()]
param(
  [string]$ProjectPath = 'src/Meridian.Wpf/Meridian.Wpf.csproj',
  [string]$Configuration = 'Release',
  [string]$Framework = 'net9.0-windows10.0.19041.0',
  [string]$ExeName = 'Meridian.Desktop.exe',
  [string]$OutputDir = 'docs/screenshots/desktop',
  [switch]$SkipBuild,
  [switch]$KeepAppOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $PSScriptRoot 'SharedBuild.ps1')
$resolvedProjectPath = if ([System.IO.Path]::IsPathRooted($ProjectPath)) {
  [System.IO.Path]::GetFullPath($ProjectPath)
}
else {
  [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))
}
$buildIsolationKey = New-MeridianBuildIsolationKey -Prefix 'desktop-screenshots'

function Assert-Command {
  param([string]$Name)
  if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
    throw "Required command '$Name' was not found in PATH."
  }
}

function Find-MeridianWindow {
  $root = [System.Windows.Automation.AutomationElement]::RootElement
  $all = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.Condition]::TrueCondition
  )

  foreach ($w in $all) {
    try {
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

    [void]$wshShell.AppActivate('Meridian')
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

  $enterFund = Wait-ForElement -Attempts 10 -DelayMilliseconds 300 -Finder {
    $currentWindow = Find-MeridianWindow
    if (-not $currentWindow) { return $null }
    return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $enterFundCond)
  }

  if ($enterFund -and -not $enterFund.Current.IsEnabled) {
    $seedProfiles = Wait-ForElement -Attempts 5 -DelayMilliseconds 250 -Finder {
      $currentWindow = Find-MeridianWindow
      if (-not $currentWindow) { return $null }
      return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $seedProfilesCond)
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
      $currentWindow = Find-MeridianWindow
      if (-not $currentWindow) { return $null }
      return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $enterFundCond)
    }
  }

  if (-not $enterFund) {
    Write-Warning 'Fund profile selection view did not expose an Enter Fund button.'
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

function Invoke-Navigate {
  param(
    [Parameter(Mandatory = $true)]
    [System.Windows.Automation.AutomationElement]$Window,

    [Parameter(Mandatory = $true)]
    [string]$SearchTerm
  )

  try {
    Activate-MeridianWindow | Out-Null

    [System.Windows.Forms.SendKeys]::SendWait('^k')

    $cond = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
      'CommandPaletteInput'
    )

    $inputEl = Wait-ForElement -Attempts 15 -DelayMilliseconds 250 -Finder {
      $currentWindow = Find-MeridianWindow
      if (-not $currentWindow) { return $null }
      return $currentWindow.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    }

    if (-not $inputEl) {
      Write-Warning "Command palette input not found for '$SearchTerm'."
      [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
      return $false
    }

    $valuePattern = $inputEl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) -as [System.Windows.Automation.ValuePattern]
    $valuePattern.SetValue($SearchTerm)
    Start-Sleep -Milliseconds 400

    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 1000
    return $true
  } catch {
    Write-Warning "Navigation failed for '$SearchTerm': $_"
    return $false
  }
}

Assert-Command -Name 'dotnet'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationClient')
[void][System.Reflection.Assembly]::LoadWithPartialName('UIAutomationTypes')

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

$env:MDC_FIXTURE_MODE = '1'
$exePath = Get-MeridianProjectBinaryPath -RepoRoot $repoRoot -ProjectPath $resolvedProjectPath -Configuration $Configuration -Framework $Framework -BinaryName $ExeName -IsolationKey $buildIsolationKey
$stdoutPath = 'wpf-startup-stdout.log'
$stderrPath = 'wpf-startup-stderr.log'

if (-not (Test-Path $exePath)) {
  throw "WPF executable was not found at '$exePath'. Build the project or check your parameters."
}

Write-Host "Starting $exePath in fixture mode..."
$proc = Start-Process -FilePath $exePath -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru -WindowStyle Normal

try {
  $window = $null
  Write-Host 'Waiting for Meridian window (up to 90 seconds)...'
  for ($i = 0; $i -lt 45; $i++) {
    Start-Sleep -Seconds 2

    if ($proc.HasExited) {
      Write-Host "Application exited with code $($proc.ExitCode)."
      if (Test-Path $stdoutPath) { Write-Host '--- stdout ---'; Get-Content $stdoutPath | Write-Host }
      if (Test-Path $stderrPath) { Write-Host '--- stderr ---'; Get-Content $stderrPath | Write-Host }
      throw 'WPF process exited before the main window appeared.'
    }

    $window = Find-MeridianWindow
    if ($window) {
      Write-Host 'Meridian window detected.'
      break
    }
  }

  if (-not $window) {
    if (Test-Path $stdoutPath) { Write-Host '--- stdout ---'; Get-Content $stdoutPath | Write-Host }
    if (Test-Path $stderrPath) { Write-Host '--- stderr ---'; Get-Content $stderrPath | Write-Host }
    throw 'Meridian window did not appear within 90 seconds.'
  }

  Start-Sleep -Seconds 4

  if (-not (Ensure-EnteredFundProfile -Window $window)) {
    Write-Warning 'Unable to enter the fund profile selector. Continuing without page navigation.'
  }

  $window = Find-MeridianWindow
  if ($window) {
    Maximize-MeridianWindow -Window $window
  }

  $pages = [ordered]@{
    'dashboard' = 'System Overview'
    'providers' = 'Providers'
    'provider-health' = 'Provider Health'
    'backfill' = 'Backfill'
    'symbols' = 'Symbols'
    'live-data' = 'Live Data'
    'storage' = 'Storage'
    'data-quality' = 'Data Quality'
    'data-browser' = 'Data Browser'
    'strategy-runs' = 'Strategy Runs'
    'backtest' = 'Backtest'
    'quant-script' = 'Quant Script'
    'security-master' = 'Security Master'
    'diagnostics' = 'Diagnostics'
    'settings' = 'Settings'
  }

  foreach ($entry in $pages.GetEnumerator()) {
    $slug = $entry.Key
    $search = $entry.Value

    if (-not $window) {
      Write-Warning "Skipping '$search' because the main window reference was lost."
      continue
    }

    Write-Host "Navigating to '$search' ..."
    $ok = Invoke-Navigate -Window $window -SearchTerm $search
    $window = Find-MeridianWindow

    if ($window -and $ok) {
      Save-WindowCapture -Window $window -Path (Join-Path $OutputDir "wpf-$slug.png")
    } else {
      Write-Warning "Skipping '$search' because navigation failed or window reference was lost."
    }
  }

  Write-Host "Desktop screenshots complete. Output directory: $OutputDir"
  Get-ChildItem $OutputDir -Filter '*.png' |
    Select-Object Name, @{ Name = 'SizeKB'; Expression = { '{0:N0}' -f ($_.Length / 1KB) } } |
    Format-Table -AutoSize
}
finally {
  if (-not $KeepAppOpen) {
    Write-Host 'Stopping WPF process...'
    try { $proc | Stop-Process -Force -ErrorAction SilentlyContinue } catch {}
    Get-Process -Name 'Meridian.Desktop' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
  }
}
