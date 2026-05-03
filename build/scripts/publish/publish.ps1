# Meridian - Cross-platform Single Executable Build Script (PowerShell)
# Usage: .\publish.ps1 [-Platform <platform>] [-Project <project>]
# Examples:
#   .\publish.ps1                              # Build all platforms, all projects
#   .\publish.ps1 -Platform linux-x64          # Build only Linux x64
#   .\publish.ps1 -Platform win-x64 -Project collector  # Build only Windows x64, collector only

[CmdletBinding()]
param(
    [ValidateSet("all", "win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$Platform = "all",

    [ValidateSet("all", "collector", "desktop")]
    [string]$Project = "all",

    [string]$Version = "1.0.0",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputDir = "./dist",

    [ValidateRange(0, [int]::MaxValue)]
    [int]$OutputRetentionDays = 14,

    [ValidateRange(0, [int]::MaxValue)]
    [int]$OutputRetainLatest = 5,

    [switch]$Help
)

$ErrorActionPreference = "Stop"

# Script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..\..\..")).Path
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) "lib"
$ConfigDir = Join-Path $RepoRoot "config"
Set-Location $RepoRoot
$ResolvedOutputDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    [System.IO.Path]::GetFullPath($OutputDir)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputDir))
}

# Configuration
$AllPlatforms = @("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")
$WindowsPlatforms = @("win-x64", "win-arm64")
$CollectorProject = Join-Path $RepoRoot "src/Meridian/Meridian.csproj"
$DesktopProject = Join-Path $RepoRoot "src/Meridian.Wpf/Meridian.Wpf.csproj"
$ArtifactRetentionModule = Join-Path $LibDir "ArtifactRetention.psm1"
if (Test-Path $ArtifactRetentionModule) {
    Import-Module $ArtifactRetentionModule -Force
}
else {
    throw "Artifact retention module not found: $ArtifactRetentionModule"
}

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] " -ForegroundColor Blue -NoNewline
    Write-Host $Message
}

function Write-Success {
    param([string]$Message)
    Write-Host "[SUCCESS] " -ForegroundColor Green -NoNewline
    Write-Host $Message
}

function Write-Warning {
    param([string]$Message)
    Write-Host "[WARNING] " -ForegroundColor Yellow -NoNewline
    Write-Host $Message
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] " -ForegroundColor Red -NoNewline
    Write-Host $Message
}

function Get-PublishOutputProcesses {
    param([string]$PublishRoot)

    $fullPublishRoot = [System.IO.Path]::GetFullPath($PublishRoot)

    return @(Get-Process -Name 'Meridian', 'Meridian.Desktop' -ErrorAction SilentlyContinue | Where-Object {
            try {
                $processPath = $_.Path
                if ([string]::IsNullOrWhiteSpace($processPath)) {
                    return $false
                }

                $fullProcessPath = [System.IO.Path]::GetFullPath($processPath)
                return $fullProcessPath.StartsWith($fullPublishRoot, [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                return $false
            }
        })
}

function Stop-PublishOutputProcesses {
    param([string]$PublishRoot)

    $runningProcesses = @(Get-PublishOutputProcesses -PublishRoot $PublishRoot)
    if ($runningProcesses.Count -eq 0) {
        return
    }

    Write-Warning "Stopping $($runningProcesses.Count) running publish output process(es) so '$PublishRoot' can be cleaned..."

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
                Write-Success "Stopped process $($process.ProcessName) ($($process.Id))"
                continue
            }

            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
            Write-Success "Stopped process $($process.ProcessName) ($($process.Id))"
        }
        catch {
            throw "Failed to stop running publish output process $($process.ProcessName) ($($process.Id)): $($_.Exception.Message)"
        }
    }
}

function Show-Help {
    Write-Host @"
Meridian - Single Executable Build Script (PowerShell)

Usage: .\publish.ps1 [-Platform <platform>] [-Project <project>]

Parameters:
  -Platform     Target platform (default: all)
                  all         Build for all platforms
                  win-x64     Windows x64
                  win-arm64   Windows ARM64
                  linux-x64   Linux x64
                  linux-arm64 Linux ARM64
                  osx-x64     macOS x64 (Intel)
                  osx-arm64   macOS ARM64 (Apple Silicon)

  -Project      Target project (default: all)
                  all        Build all projects
                  collector  Build only Meridian (CLI)
                  desktop    Build only Meridian.Wpf / Meridian.Desktop (Windows Desktop App)

  -Version      Version number (default: 1.0.0)
  -Configuration Build configuration (default: Release)
  -OutputDir    Output directory (default: ./dist)
  -OutputRetentionDays Days to keep generated publish output when OutputDir is under artifacts/publish (default: 14; 0 disables age pruning)
  -OutputRetainLatest Latest generated publish output directories to keep under artifacts/publish (default: 5; 0 disables count pruning)
  -Help         Show this help message

Examples:
  .\publish.ps1                                    # Build all platforms, all projects
  .\publish.ps1 -Platform linux-x64                # Build Linux x64 only
  .\publish.ps1 -Platform win-x64 -Project collector  # Build Windows collector only
  .\publish.ps1 -Version 2.0.0                     # Build with custom version
"@
}

function Publish-Project {
    param(
        [string]$ProjectPath,
        [string]$RuntimeId,
        [string]$ProjectName,
        [string]$OutputSubDir
    )

    $outputPath = Join-Path (Join-Path $ResolvedOutputDir $RuntimeId) $OutputSubDir

    Write-Info "Publishing $ProjectName for $RuntimeId..."

    dotnet publish $ProjectPath `
        -c $Configuration `
        -r $RuntimeId `
        -o $outputPath `
        -p:Version=$Version `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish $ProjectName for $RuntimeId"
    }

    # Copy configuration file
    $configFile = Join-Path $ConfigDir "appsettings.json"
    if (Test-Path $configFile) {
        Copy-Item $configFile -Destination $outputPath
    }

    Write-Success "Published $ProjectName for $RuntimeId -> $outputPath"
}

function Publish-DesktopApp {
    param([string]$RuntimeId)

    $outputPath = Join-Path (Join-Path $ResolvedOutputDir $RuntimeId) "desktop"
    $platform = if ($RuntimeId -eq "win-arm64") { "ARM64" } else { "x64" }

    Write-Info "Publishing Meridian Desktop (WPF) for $RuntimeId..."

    # Meridian Desktop is a WPF app with a separate assembly name.
    dotnet publish $DesktopProject `
        -c $Configuration `
        -r $RuntimeId `
        -o $outputPath `
        -p:Version=$Version `
        -p:EnableFullWpfBuild=true `
        -p:Platform=$platform `
        --self-contained true `
        -p:WindowsPackageType=None `
        -p:PublishReadyToRun=false

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to publish Meridian Desktop for $RuntimeId"
    }

    # Copy configuration files
    $configFile = Join-Path $ConfigDir "appsettings.json"
    $sampleConfigFile = Join-Path $ConfigDir "appsettings.sample.json"

    if (Test-Path $configFile) {
        Copy-Item $configFile -Destination $outputPath
    }
    if (Test-Path $sampleConfigFile) {
        Copy-Item $sampleConfigFile -Destination $outputPath
    }

    # Create data directory for first run
    $dataDir = Join-Path $outputPath "data"
    New-Item -ItemType Directory -Path $dataDir -Force | Out-Null

    Write-Success "Published Meridian Desktop (WPF) for $RuntimeId -> $outputPath"
}

function New-Package {
    param([string]$RuntimeId)

    $outputPath = Join-Path $ResolvedOutputDir $RuntimeId
    $packageName = "Meridian-$Version-$RuntimeId"

    if (Test-Path $outputPath) {
        Write-Info "Creating package for $RuntimeId..."

        $archivePath = Join-Path $ResolvedOutputDir "$packageName.zip"

        if (Test-Path $archivePath) {
            Remove-Item $archivePath -Force
        }

        Compress-Archive -Path $outputPath -DestinationPath $archivePath
        Write-Success "Created $archivePath"
    }
}

function Invoke-PublishOutputRetention {
    $artifactPublishRoot = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "artifacts/publish"))
    $artifactPublishRootWithSeparator = $artifactPublishRoot
    if (-not $artifactPublishRootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $artifactPublishRootWithSeparator += [System.IO.Path]::DirectorySeparatorChar
    }

    if (-not $ResolvedOutputDir.StartsWith($artifactPublishRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    Invoke-MeridianArtifactDirectoryRetention `
        -OutputRoot $artifactPublishRoot `
        -ActivePath $ResolvedOutputDir `
        -MaxAgeDays $OutputRetentionDays `
        -RetainLatest $OutputRetainLatest `
        -Label "publish output"
}

# Main script
if ($Help) {
    Show-Help
    exit 0
}

Invoke-PublishOutputRetention

# Determine target platforms
$TargetPlatforms = if ($Platform -eq "all") { $AllPlatforms } else { @($Platform) }

# Clean output directory
Write-Info "Cleaning output directory: $ResolvedOutputDir"
if (Test-Path $ResolvedOutputDir) {
    Stop-PublishOutputProcesses -PublishRoot $ResolvedOutputDir
    Remove-Item $ResolvedOutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ResolvedOutputDir -Force | Out-Null

# Build projects
Write-Info "Building Meridian v$Version ($Configuration)"
Write-Info "Target platforms: $($TargetPlatforms -join ', ')"
Write-Info "Target projects: $Project"
Write-Host ""

foreach ($rid in $TargetPlatforms) {
    Write-Info "=== Building for $rid ==="

    if ($Project -eq "all" -or $Project -eq "collector") {
        Publish-Project -ProjectPath $CollectorProject -RuntimeId $rid -ProjectName "Meridian" -OutputSubDir "collector"
    }

    # Build the WPF desktop app only for Windows platforms
    if (($Project -eq "all" -or $Project -eq "desktop") -and ($rid -in $WindowsPlatforms)) {
        Write-Info "Publishing Meridian Desktop (WPF) for $rid..."
        Publish-DesktopApp -RuntimeId $rid
    }

    # Create package
    New-Package -RuntimeId $rid

    Write-Host ""
}

# Summary
Write-Success "=== Build Complete ==="
Write-Host ""
Write-Info "Output directory: $ResolvedOutputDir"
Write-Host ""

# List outputs
Get-ChildItem $ResolvedOutputDir -Recurse -Filter "Meridian*" |
    Where-Object { $_.Extension -in @(".exe", "", ".zip", ".tar.gz") } |
    Select-Object -First 20 |
    ForEach-Object { Write-Host "  $($_.FullName)" }

Write-Host ""
Write-Info "To run the collector:"
Write-Host "  $ResolvedOutputDir\win-x64\collector\Meridian.exe"
Write-Info "To run the desktop app:"
Write-Host "  $ResolvedOutputDir\win-x64\desktop\Meridian.Desktop.exe"
