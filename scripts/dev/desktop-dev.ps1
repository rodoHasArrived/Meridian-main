#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$Framework = 'net10.0-windows10.0.19041.0',
    [string]$Profile = 'debug-startup',
    [string]$ProfileRoot = 'scripts/dev/workflow-profiles',
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipTestBuild,
    [switch]$SkipLaunchSmoke,
    [switch]$NoIsolation,
    [switch]$EmitJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location $repoRoot
. (Join-Path $PSScriptRoot 'SharedBuild.ps1')
. (Join-Path $PSScriptRoot 'SharedWorkflowProfiles.ps1')

$wpfProject = 'src/Meridian.Wpf/Meridian.Wpf.csproj'
$wpfTestsProject = 'tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj'
$uiServicesProject = 'src/Meridian.Ui.Services/Meridian.Ui.Services.csproj'
$onWindows = $IsWindows -or ($env:OS -eq 'Windows_NT')
$buildIsolationKey = if ($NoIsolation) { '' } else { New-MeridianBuildIsolationKey -Prefix 'desktop-dev' }

$validationErrors = New-Object System.Collections.Generic.List[string]
$validationWarnings = New-Object System.Collections.Generic.List[string]
$steps = New-Object System.Collections.Generic.List[object]

function Write-Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Write-Ok([string]$Message) { Write-Host "[ OK ] $Message" -ForegroundColor Green }
function Write-Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Write-Fail([string]$Message) { Write-Host "[FAIL] $Message" -ForegroundColor Red }
function Write-Step([string]$Message) { Write-Host "`n[STEP] $Message" -ForegroundColor Cyan }

function Format-DesktopCommand {
    param([Parameter(Mandatory = $true)][string[]]$Command)

    return ($Command | ForEach-Object {
            if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ }
        }) -join ' '
}

function Add-ValidationWarning {
    param([Parameter(Mandatory = $true)][string]$Message)

    $validationWarnings.Add($Message) | Out-Null
    Write-Warn $Message
}

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)

    $validationErrors.Add($Message) | Out-Null
    Write-Fail $Message
}

function Invoke-DesktopCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Command,
        [string]$FixHint = ''
    )

    Write-Info (Format-DesktopCommand -Command $Command)
    $output = @()
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        $commandArgs = if ($Command.Count -gt 1) { @($Command[1..($Command.Count - 1)]) } else { @() }
        & $Command[0] @commandArgs 2>&1 | ForEach-Object { $output += $_ }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        $stopwatch.Stop()
    }

    $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
    $tail = ($output | Select-Object -Last 20) -join [Environment]::NewLine
    $steps.Add([ordered]@{
            name = $Name
            command = Format-DesktopCommand -Command $Command
            exitCode = $exitCode
            durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
            tail = $tail
        }) | Out-Null

    if ($exitCode -ne 0) {
        Add-ValidationError "$Name failed with exit code $exitCode."
        if (-not [string]::IsNullOrWhiteSpace($tail)) {
            Write-Host $tail.TrimEnd()
        }

        if (-not [string]::IsNullOrWhiteSpace($FixHint)) {
            Write-Warn $FixHint
        }
    }
    else {
        Write-Ok ("{0} succeeded ({1:0.0}s)" -f $Name, $stopwatch.Elapsed.TotalSeconds)
    }
}

function Test-WindowsSdkInstalled {
    if (-not $onWindows) {
        return $false
    }

    $windowsKitRoots = Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue
    return $null -ne $windowsKitRoots
}

function Test-VisualStudioBuildToolsInstalled {
    if (-not $onWindows) {
        return $false
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        return $false
    }

    $instances = & $vswhere -products * -requires Microsoft.Component.MSBuild -property installationPath 2>&1
    return -not [string]::IsNullOrWhiteSpace(($instances | Out-String))
}

Write-Host '== Meridian: Desktop Dev Bootstrap ==' -ForegroundColor Cyan
Write-Info "Repository    : $repoRoot"
Write-Info "Configuration : $Configuration"
Write-Info "Framework     : $Framework"
Write-Info "Profile       : $Profile"
Write-Info "Isolation key : $(if ([string]::IsNullOrWhiteSpace($buildIsolationKey)) { 'disabled' } else { $buildIsolationKey })"

Write-Step 'Validate required tools'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Add-ValidationError '.NET SDK not found in PATH. Install the .NET 10 SDK.'
}
else {
    $dotnetVersion = (& dotnet --version).Trim()
    $dotnetSdks = @(& dotnet --list-sdks)
    Write-Info "Selected .NET SDK: $dotnetVersion"

    if ($dotnetVersion.StartsWith('10.')) {
        Write-Ok ".NET 10 SDK selected: $dotnetVersion"
    }
    elseif ($dotnetSdks -match '^10\.') {
        Add-ValidationWarning ".NET 10 is installed, but the selected SDK is $dotnetVersion. Check global.json or PATH if restore/build behaves unexpectedly."
    }
    else {
        Add-ValidationError ".NET 10 SDK not detected. Install .NET 10.0.100 or a compatible latestMinor SDK."
    }
}

if (-not $onWindows) {
    Add-ValidationWarning 'Non-Windows environment detected. WPF desktop builds and UI automation require Windows; script-level/profile checks will still run.'
}
else {
    Write-Ok 'Windows platform detected'

    if (Test-WindowsSdkInstalled) {
        Write-Ok 'Windows SDK detected'
    }
    else {
        Add-ValidationWarning 'Windows SDK not detected. Install it from Visual Studio Installer or the Windows SDK download page before full WPF builds.'
    }

    if (Test-VisualStudioBuildToolsInstalled) {
        Write-Ok 'Visual Studio Build Tools detected'
    }
    else {
        Add-ValidationWarning 'Visual Studio Build Tools with MSBuild were not detected. Install the .NET desktop development workload if WPF builds fail.'
    }
}

Write-Step 'Validate desktop workflow profile'
try {
    $profileEnvelope = Get-MeridianWorkflowProfile -RepoRoot $repoRoot -ProfileName $Profile -ProfileRoot $ProfileRoot
    $profileValidation = Test-MeridianWorkflowProfile -ProfileData $profileEnvelope.data
    foreach ($warning in $profileValidation.warnings) {
        Add-ValidationWarning "Profile '$Profile': $warning"
    }

    foreach ($error in $profileValidation.errors) {
        Add-ValidationError "Profile '$Profile': $error"
    }

    if ($profileValidation.isValid) {
        Write-Ok "Workflow profile '$Profile' is valid"
    }
}
catch {
    Add-ValidationError "Workflow profile '$Profile' could not be loaded: $($_.Exception.Message)"
}

foreach ($project in @($uiServicesProject, $wpfProject, $wpfTestsProject)) {
    if (Test-Path -LiteralPath $project) {
        Write-Ok "Project exists: $project"
    }
    else {
        Add-ValidationError "Project not found: $project"
    }
}

if ($validationErrors.Count -eq 0 -and -not $SkipRestore) {
    Write-Step 'Restore desktop projects'
    $sharedRestoreArgs = Get-MeridianBuildArguments -IsolationKey $buildIsolationKey
    $desktopRestoreArgs = Get-MeridianBuildArguments -IsolationKey $buildIsolationKey -EnableFullWpfBuild

    Invoke-DesktopCommand `
        -Name 'Restore UI services' `
        -Command (@('dotnet', 'restore', $uiServicesProject, '--verbosity', 'quiet') + $sharedRestoreArgs) `
        -FixHint "Run: dotnet restore $uiServicesProject /p:EnableWindowsTargeting=true"

    Invoke-DesktopCommand `
        -Name 'Restore WPF desktop shell' `
        -Command (@('dotnet', 'restore', $wpfProject, '--verbosity', 'quiet') + $desktopRestoreArgs) `
        -FixHint "Run: dotnet restore $wpfProject /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true"

    Invoke-DesktopCommand `
        -Name 'Restore WPF desktop tests' `
        -Command (@('dotnet', 'restore', $wpfTestsProject, '--verbosity', 'quiet') + $desktopRestoreArgs) `
        -FixHint "Run: dotnet restore $wpfTestsProject /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true"
}
elseif ($SkipRestore) {
    Write-Step 'Restore desktop projects'
    Write-Warn 'Skipping restore because -SkipRestore was supplied.'
}

if ($validationErrors.Count -eq 0 -and -not $SkipBuild) {
    Write-Step 'Build desktop shell'
    $desktopBuildArgs = Get-MeridianBuildArguments `
        -IsolationKey $buildIsolationKey `
        -TargetFramework $Framework `
        -EnableFullWpfBuild

    Invoke-DesktopCommand `
        -Name 'Build WPF desktop shell' `
        -Command (@('dotnet', 'build', $wpfProject, '-c', $Configuration, '--no-restore', '--verbosity', 'quiet') + $desktopBuildArgs) `
        -FixHint "Run: dotnet build $wpfProject -c $Configuration /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true"
}
elseif ($SkipBuild) {
    Write-Step 'Build desktop shell'
    Write-Warn 'Skipping WPF desktop shell build because -SkipBuild was supplied.'
}

if ($validationErrors.Count -eq 0 -and -not $SkipTestBuild) {
    Write-Step 'Build desktop test project'
    $desktopTestBuildArgs = Get-MeridianBuildArguments `
        -IsolationKey $buildIsolationKey `
        -TargetFramework $Framework `
        -EnableFullWpfBuild

    Invoke-DesktopCommand `
        -Name 'Build WPF desktop tests' `
        -Command (@('dotnet', 'build', $wpfTestsProject, '-c', $Configuration, '--no-restore', '--verbosity', 'quiet') + $desktopTestBuildArgs) `
        -FixHint "Run: dotnet build $wpfTestsProject -c $Configuration /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true"
}
elseif ($SkipTestBuild) {
    Write-Step 'Build desktop test project'
    Write-Warn 'Skipping WPF desktop test build because -SkipTestBuild was supplied.'
}

if ($validationErrors.Count -eq 0 -and -not $SkipLaunchSmoke) {
    Write-Step 'Run fixture startup smoke'

    if ($SkipRestore -or $SkipBuild) {
        Add-ValidationWarning 'Skipping fixture startup smoke because -SkipRestore or -SkipBuild was supplied. Run desktop-dev.ps1 without those switches, or run pwsh ./scripts/dev/run-desktop.ps1 -Fixture -StartupSmoke separately.'
    }
    else {
        Invoke-DesktopCommand `
            -Name 'Launch fixture desktop startup smoke' `
            -Command @(
                'pwsh',
                '-NoProfile',
                '-ExecutionPolicy', 'Bypass',
                '-File', (Join-Path $PSScriptRoot 'run-desktop.ps1'),
                '-Profile', $Profile,
                '-ProfileRoot', $ProfileRoot,
                '-Fixture',
                '-StartupSmoke'
            ) `
            -FixHint 'Run: pwsh ./scripts/dev/run-desktop.ps1 -Fixture -StartupSmoke'
    }
}
elseif ($SkipLaunchSmoke) {
    Write-Step 'Run fixture startup smoke'
    Write-Warn 'Skipping fixture startup smoke because -SkipLaunchSmoke was supplied.'
}

$summaryResult = if ($validationErrors.Count -eq 0) { 'passed' } else { 'failed' }
$summary = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    repoRoot = $repoRoot
    configuration = $Configuration
    framework = $Framework
    profile = $Profile
    buildIsolationKey = $buildIsolationKey
    skipLaunchSmoke = [bool]$SkipLaunchSmoke
    errors = @($validationErrors.ToArray())
    warnings = @($validationWarnings.ToArray())
    result = $summaryResult
    steps = @($steps.ToArray())
}

Write-Host ''
Write-Host '============================================================================' -ForegroundColor Cyan
Write-Host 'Desktop Bootstrap Summary' -ForegroundColor Cyan
Write-Host '============================================================================' -ForegroundColor Cyan

if ($summary.result -eq 'passed') {
    Write-Ok 'Desktop environment validation complete.'
    Write-Host ''
    Write-Host 'Next steps for desktop development:' -ForegroundColor Cyan
    Write-Host '  1. Launch full fixture desktop: pwsh ./scripts/dev/run-desktop.ps1 -Fixture' -ForegroundColor Gray
    Write-Host '  2. Build desktop app:          make desktop-build' -ForegroundColor Gray
    Write-Host '  3. Run desktop tests:          make desktop-test' -ForegroundColor Gray
    Write-Host '  4. Focused route validation:   make desktop-test-position-blotter-route' -ForegroundColor Gray
    Write-Host '  5. Workflow automation:        pwsh ./scripts/dev/run-desktop-workflow.ps1 -Workflow debug-startup' -ForegroundColor Gray
    Write-Host '  6. Blueprint checklist:        python ./scripts/dev/desktop_screen_blueprint_checklist.py --summary' -ForegroundColor Gray
    Write-Host '  7. Open in Visual Studio:      Start-Process src/Meridian.Wpf/Meridian.Wpf.csproj' -ForegroundColor Gray
}
else {
    Write-Fail "Desktop environment validation failed with $($validationErrors.Count) error(s)."
    Write-Host ''
    Write-Host 'Address the errors above, then rerun this script. Use -EmitJson for machine-readable step output.' -ForegroundColor Yellow
}

if ($validationWarnings.Count -gt 0) {
    Write-Host ''
    Write-Host "Warnings ($($validationWarnings.Count)):" -ForegroundColor Yellow
    foreach ($warning in $validationWarnings) {
        Write-Host "  - $warning" -ForegroundColor Yellow
    }
}

if ($EmitJson) {
    Write-Host ''
    $summary | ConvertTo-Json -Depth 8
}

if ($summary.result -ne 'passed') {
    exit 1
}
