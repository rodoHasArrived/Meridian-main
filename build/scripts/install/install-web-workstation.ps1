<#
.SYNOPSIS
    Installs the Meridian browser workstation as a local Windows application.

.DESCRIPTION
    Builds the Vite workstation bundle, publishes the Meridian desktop-local host,
    installs both into a per-user app directory, creates runtime directories and
    configuration, then creates Desktop and Start Menu shortcuts that launch the
    local host and open /workstation/.

.EXAMPLE
    .\build\scripts\install\install-web-workstation.ps1

.EXAMPLE
    .\build\scripts\install\install-web-workstation.ps1 -SkipDashboardBuild -LaunchAfterInstall
#>

[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\Meridian Web Workstation"),

    [string]$AppDataRoot = (Join-Path $env:LOCALAPPDATA "Meridian"),

    [ValidateRange(1, 65535)]
    [int]$Port = 8080,

    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$Configuration = "Release",

    [switch]$SkipDashboardBuild,

    [switch]$SkipNpmInstall,

    [switch]$SkipHostPublish,

    [switch]$EnableTrimmedPublish,

    [switch]$NoDesktopShortcut,

    [switch]$NoStartMenuShortcut,

    [switch]$LaunchAfterInstall,

    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Get-FullPathForNewItem {
    param([Parameter(Mandatory)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-RepoRoot {
    $root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    return (Resolve-Path -LiteralPath $root).Path
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Info $Name
    & $Action
    Write-Success $Name
}

function Assert-Windows {
    if (-not $IsWindows -and $env:OS -ne "Windows_NT") {
        throw "The web workstation installer currently creates Windows shortcuts and must run on Windows."
    }
}

function Assert-RequiredPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description was not found: $Path"
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    Assert-RequiredPath -Path $Source -Description "Source directory"
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function New-DefaultConfig {
    param(
        [Parameter(Mandatory)][string]$ConfigPath
    )

    if (Test-Path -LiteralPath $ConfigPath) {
        Write-Info "Keeping existing config: $ConfigPath"
        return
    }

    $config = @"
{
  "DataRoot": "data",
  "DataSource": "NoOp",
  "Symbols": [],
  "Storage": {
    "NamingConvention": "BySymbol",
    "CompressionProfile": "Standard"
  },
  "Backfill": {
    "Enabled": false,
    "Provider": "stooq",
    "EnableFallback": true,
    "EnableSymbolResolution": true
  },
  "Logging": {
    "Level": "Information"
  },
  "UI": {
    "Theme": "Light",
    "RefreshIntervalMs": 1000
  }
}
"@

    New-Item -ItemType Directory -Path (Split-Path -Parent $ConfigPath) -Force | Out-Null
    Set-Content -LiteralPath $ConfigPath -Value $config -Encoding UTF8
}

function New-LauncherScript {
    param(
        [Parameter(Mandatory)][string]$LauncherPath,
        [Parameter(Mandatory)][string]$ConfigPath,
        [Parameter(Mandatory)][int]$DefaultPort
    )

    $escapedConfigPath = $ConfigPath.Replace('"', '`"')
    $launcher = @"
param(
    [ValidateRange(1, 65535)]
    [int]`$Port = $DefaultPort
)

`$ErrorActionPreference = "Stop"
`$installRoot = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$exePath = Join-Path `$installRoot "Meridian.exe"
`$configPath = "$escapedConfigPath"
`$healthUrl = "http://localhost:`$Port/healthz"
`$workstationUrl = "http://localhost:`$Port/workstation/"
`$loginUrl = "http://localhost:`$Port/login?returnUrl=%2Fworkstation%2F"

if ([string]::IsNullOrWhiteSpace(`$env:MDC_AUTH_MODE)) {
    `$env:MDC_AUTH_MODE = "required"
}

function Test-MeridianEndpoint {
    param([Parameter(Mandatory)][string]`$Uri)

    try {
        `$response = Invoke-WebRequest -Uri `$Uri -UseBasicParsing -TimeoutSec 2
        return `$response.StatusCode -ge 200 -and `$response.StatusCode -lt 500
    }
    catch {
        return `$false
    }
}

if (-not (Test-Path -LiteralPath `$exePath)) {
    throw "Meridian.exe was not found at `$exePath. Re-run the workstation installer."
}

if (-not (Test-MeridianEndpoint -Uri `$healthUrl)) {
    `$arguments = "--mode desktop --http-port `$Port --config ```"`$configPath```""
    Start-Process -FilePath `$exePath -ArgumentList `$arguments -WorkingDirectory `$installRoot -WindowStyle Hidden | Out-Null

    `$ready = `$false
    for (`$attempt = 0; `$attempt -lt 45; `$attempt++) {
        Start-Sleep -Seconds 1
        if (Test-MeridianEndpoint -Uri `$healthUrl) {
            `$ready = `$true
            break
        }
    }

    if (-not `$ready) {
        Write-Warning "Meridian host did not answer `$healthUrl within 45 seconds. Opening the workstation route anyway."
    }
}

Start-Process `$loginUrl
"@

    Set-Content -LiteralPath $LauncherPath -Value $launcher -Encoding UTF8
}

function Get-PowerShellShortcutTarget {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    $powershell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($powershell) {
        return $powershell.Source
    }

    throw "Neither pwsh nor Windows PowerShell was found on PATH."
}

function New-AppShortcut {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$LauncherPath,
        [Parameter(Mandatory)][string]$InstallRootPath,
        [Parameter(Mandatory)][string]$IconPath
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = Get-PowerShellShortcutTarget
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$LauncherPath`""
    $shortcut.WorkingDirectory = $InstallRootPath
    if (Test-Path -LiteralPath $IconPath) {
        $shortcut.IconLocation = $IconPath
    }
    $shortcut.Description = "Launch Meridian Web Workstation"
    $shortcut.Save()
}

Assert-Windows

$repoRoot = Get-RepoRoot
$installRootPath = Get-FullPathForNewItem -Path $InstallRoot
$appDataRootPath = Get-FullPathForNewItem -Path $AppDataRoot
$dataRootPath = Join-Path $appDataRootPath "data"
$configPath = Join-Path $appDataRootPath "appsettings.json"
$hostProject = Join-Path $repoRoot "src\Meridian\Meridian.csproj"
$dashboardRoot = Join-Path $repoRoot "src\Meridian.Ui\dashboard"
$dashboardPackageJson = Join-Path $dashboardRoot "package.json"
$dashboardNodeModules = Join-Path $dashboardRoot "node_modules"
$dashboardBundle = Join-Path $repoRoot "src\Meridian.Ui\wwwroot\workstation"
$publishRoot = Join-Path $repoRoot "artifacts\publish\web-workstation-installer\$RuntimeIdentifier"
$launcherPath = Join-Path $installRootPath "Launch-MeridianWebWorkstation.ps1"
$iconSource = Join-Path $repoRoot "src\Meridian\app.ico"
$iconPath = Join-Path $installRootPath "app.ico"
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "Meridian Web Workstation.lnk"
$startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Meridian"
$startMenuShortcutPath = Join-Path $startMenuDir "Meridian Web Workstation.lnk"

Assert-RequiredPath -Path $hostProject -Description "Meridian host project"
Assert-RequiredPath -Path $dashboardPackageJson -Description "Dashboard package.json"

Write-Host ""
Write-Host "Meridian Web Workstation install plan" -ForegroundColor White
Write-Host "  Repo root:       $repoRoot"
Write-Host "  Install root:    $installRootPath"
Write-Host "  App data root:   $appDataRootPath"
Write-Host "  Config path:     $configPath"
Write-Host "  Data root:       $dataRootPath"
Write-Host "  Port:            $Port"
Write-Host "  Runtime:         $RuntimeIdentifier"
Write-Host "  Workstation URL: http://localhost:$Port/workstation/"
Write-Host ""

if ($PlanOnly) {
    Write-Info "PlanOnly was specified; no files were built, copied, or installed."
    exit 0
}

Invoke-Step -Name "Create application directories" -Action {
    $directories = @(
        $installRootPath,
        $appDataRootPath,
        $dataRootPath,
        (Join-Path $dataRootPath "workstation"),
        (Join-Path $dataRootPath "workstation\evidence"),
        (Join-Path $dataRootPath "workstation\workflows"),
        (Join-Path $installRootPath "data"),
        (Join-Path $installRootPath "data\execution\sessions"),
        (Join-Path $installRootPath "data\strategies\designer"),
        (Join-Path $installRootPath "data\promotions"),
        (Join-Path $installRootPath "artifacts\reconciliation"),
        (Join-Path $installRootPath "wwwroot\workstation")
    )

    foreach ($directory in $directories) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

Invoke-Step -Name "Create default app configuration if needed" -Action {
    New-DefaultConfig -ConfigPath $configPath
}

if (-not $SkipNpmInstall -and -not (Test-Path -LiteralPath $dashboardNodeModules)) {
    Invoke-Step -Name "Install dashboard dependencies" -Action {
        Push-Location $dashboardRoot
        try {
            & npm install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }
}

if (-not $SkipDashboardBuild) {
    Invoke-Step -Name "Build browser workstation bundle" -Action {
        Push-Location $dashboardRoot
        try {
            & npm run build
            if ($LASTEXITCODE -ne 0) {
                throw "npm run build failed with exit code $LASTEXITCODE. If this reports ENOTEMPTY or EPERM under wwwroot/workstation/assets, stop stale Vite preview processes and retry."
            }
        }
        finally {
            Pop-Location
        }
    }
}

Assert-RequiredPath -Path (Join-Path $dashboardBundle "index.html") -Description "Built workstation index.html"

if (-not $SkipHostPublish) {
    Invoke-Step -Name "Publish Meridian local host" -Action {
        New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
        $publishTrimmed = $EnableTrimmedPublish.IsPresent.ToString().ToLowerInvariant()

        $publishArgs = @(
            "publish",
            $hostProject,
            "-c", $Configuration,
            "-r", $RuntimeIdentifier,
            "-o", $publishRoot,
            "--self-contained", "true",
            "-p:PublishSingleFile=true",
            "-p:PublishReadyToRun=false",
            "-p:PublishTrimmed=$publishTrimmed"
        )

        & dotnet $publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }
}

Assert-RequiredPath -Path (Join-Path $publishRoot "Meridian.exe") -Description "Published Meridian.exe"

Invoke-Step -Name "Copy host files into install root" -Action {
    Copy-DirectoryContents -Source $publishRoot -Destination $installRootPath
}

Invoke-Step -Name "Copy workstation assets into install root" -Action {
    $targetBundle = Join-Path $installRootPath "wwwroot\workstation"
    Copy-DirectoryContents -Source $dashboardBundle -Destination $targetBundle
}

if (Test-Path -LiteralPath $iconSource) {
    Invoke-Step -Name "Copy application icon" -Action {
        Copy-Item -LiteralPath $iconSource -Destination $iconPath -Force
    }
}
else {
    Write-Warn "Icon source was not found: $iconSource"
}

Invoke-Step -Name "Create launcher script" -Action {
    New-LauncherScript -LauncherPath $launcherPath -ConfigPath $configPath -DefaultPort $Port
}

if (-not $NoDesktopShortcut) {
    Invoke-Step -Name "Create Desktop shortcut" -Action {
        New-AppShortcut -ShortcutPath $desktopShortcutPath -LauncherPath $launcherPath -InstallRootPath $installRootPath -IconPath $iconPath
    }
}

if (-not $NoStartMenuShortcut) {
    Invoke-Step -Name "Create Start Menu shortcut" -Action {
        New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
        New-AppShortcut -ShortcutPath $startMenuShortcutPath -LauncherPath $launcherPath -InstallRootPath $installRootPath -IconPath $iconPath
    }
}

Write-Host ""
Write-Success "Meridian Web Workstation installed."
Write-Host "  Launch script:   $launcherPath"
Write-Host "  Workstation URL: http://localhost:$Port/workstation/"
if (-not $NoDesktopShortcut) {
    Write-Host "  Desktop link:    $desktopShortcutPath"
}
if (-not $NoStartMenuShortcut) {
    Write-Host "  Start Menu link: $startMenuShortcutPath"
}
Write-Host ""

if ($LaunchAfterInstall) {
    Write-Info "Launching Meridian Web Workstation..."
    & (Get-PowerShellShortcutTarget) -NoProfile -ExecutionPolicy Bypass -File $launcherPath -Port $Port
}
