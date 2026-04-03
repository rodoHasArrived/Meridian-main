[CmdletBinding()]
param(
  [string]$ProjectPath = 'src/Meridian.Wpf/Meridian.Wpf.csproj',
  [string]$Configuration = 'Release',
  [string]$Framework = 'net9.0-windows',
  [string]$ExeName = 'Meridian.Desktop.exe',
  [string]$OutputDir = 'docs/screenshots/desktop',
  [switch]$SkipBuild,
  [switch]$KeepAppOpen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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
    $Window.SetFocus()
    Start-Sleep -Milliseconds 300

    [System.Windows.Forms.SendKeys]::SendWait('^k')
    Start-Sleep -Milliseconds 600

    $cond = New-Object System.Windows.Automation.PropertyCondition(
      [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
      'CommandPaletteInput'
    )

    $inputEl = $null
    for ($i = 0; $i -lt 12; $i++) {
      $inputEl = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
      if ($inputEl) {
        break
      }
      Start-Sleep -Milliseconds 200
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
  Write-Host "Restoring $ProjectPath ($Framework) ..."
  dotnet restore $ProjectPath -p:TargetFramework=$Framework --verbosity minimal

  Write-Host "Building $ProjectPath ($Configuration, $Framework) ..."
  dotnet build $ProjectPath -c $Configuration --no-restore -p:TargetFramework=$Framework --verbosity minimal
}

$env:MDC_FIXTURE_MODE = '1'
$projectDir = Split-Path -Parent $ProjectPath
$exePath = Join-Path $projectDir "bin/$Configuration/$Framework/$ExeName"
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

  $pages = [ordered]@{
    'dashboard' = 'Dashboard'
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
