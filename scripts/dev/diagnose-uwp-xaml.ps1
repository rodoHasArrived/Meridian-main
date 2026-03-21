#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "== UWP XAML Diagnostic Preflight ==" -ForegroundColor Cyan

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../../..")
Set-Location $repoRoot

function Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Ok([string]$Message) { Write-Host "[OK]   $Message" -ForegroundColor Green }
function Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }

$packagesFile = "Directory.Packages.props"
$uwpProject = "src/Meridian.Uwp/Meridian.Uwp.csproj"

if (-not (Test-Path $packagesFile)) { throw "Missing $packagesFile" }
if (-not (Test-Path $uwpProject)) { throw "Missing $uwpProject" }

$packages = Get-Content $packagesFile -Raw
$project = Get-Content $uwpProject -Raw

Info "Checking Microsoft.WindowsAppSDK package version"
$winAppSdkMatch = [regex]::Match($packages, 'PackageVersion\s+Include="Microsoft\.WindowsAppSDK"\s+Version="([^"]+)"')
if ($winAppSdkMatch.Success) {
    $version = $winAppSdkMatch.Groups[1].Value
    Info "Detected WindowsAppSDK version: $version"
    if ($version -match '^1\.7\.25') { Ok "Using stable 1.7.x family version format" }
    else { Warn "Unexpected WindowsAppSDK version format. Ensure stable release (not preview) is pinned." }
} else {
    Warn "Microsoft.WindowsAppSDK package version not found in Directory.Packages.props"
}

Info "Checking explicit target platform properties"
if ($project -match '<TargetFramework>net9\.0-windows10\.0\.19041\.0</TargetFramework>') { Ok "TargetFramework explicitly set" }
else { Warn "TargetFramework not found or unexpected value" }

if ($project -match '<TargetPlatformVersion>10\.0\.19041\.0</TargetPlatformVersion>') { Ok "TargetPlatformVersion explicitly set" }
else { Warn "TargetPlatformVersion missing or unexpected" }

if ($project -match '<AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>') { Ok "AppendRuntimeIdentifierToOutputPath disabled" }
else { Warn "AppendRuntimeIdentifierToOutputPath should be disabled for known XAML path duplication issue" }

if ($project -match '<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>') { Ok "AppendTargetFrameworkToOutputPath disabled" }
else { Warn "AppendTargetFrameworkToOutputPath should be disabled for known XAML path duplication issue" }

Info "Validating UWP XAML files are well-formed XML"
$parseErrors = @()
Get-ChildItem -Path src/Meridian.Uwp -Filter *.xaml -Recurse | ForEach-Object {
    try {
        [xml](Get-Content $_.FullName -Raw) | Out-Null
    }
    catch {
        $parseErrors += "${($_.FullName)} :: $($_.Exception.Message)"
    }
}

if ($parseErrors.Count -eq 0) {
    Ok "All XAML files parsed successfully"
} else {
    Warn "Found $($parseErrors.Count) XAML parsing error(s):"
    $parseErrors | ForEach-Object { Write-Host "  - $_" }
}

Write-Host "" 
Write-Host "Diagnostics complete." -ForegroundColor Green
Write-Host "If compiler failures persist: dotnet build $uwpProject -r win-x64 -v detailed" -ForegroundColor Cyan
