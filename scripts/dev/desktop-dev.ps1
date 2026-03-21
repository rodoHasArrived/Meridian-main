#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "== Meridian: Desktop Dev Bootstrap ==" -ForegroundColor Cyan
Write-Host ""

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../..")
Set-Location $repoRoot

function Test-Command([string]$Name) {
    return $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

function Info([string]$Message) { Write-Host "[INFO] $Message" -ForegroundColor Gray }
function Ok([string]$Message) { Write-Host "[OK]   $Message" -ForegroundColor Green }
function Warn([string]$Message) { Write-Host "[WARN] $Message" -ForegroundColor Yellow }
function Error([string]$Message) { Write-Host "[ERROR] $Message" -ForegroundColor Red }

$validationErrors = @()
$validationWarnings = @()

# ============================================================================
# Step 1: Validate .NET SDK
# ============================================================================
Write-Host "Step 1: Validating .NET SDK..." -ForegroundColor Cyan

if (-not (Test-Command "dotnet")) {
    Error ".NET SDK not found"
    $validationErrors += "dotnet command not found in PATH"
    Write-Host ""
    Write-Host "Fix: Install .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
    exit 1
}

$dotnetVersion = (& dotnet --version).Trim()
Info "Detected .NET SDK: $dotnetVersion"

# Check if .NET 9 is installed
if (-not $dotnetVersion.StartsWith("9.")) {
    Warn ".NET 9 SDK not detected (found: $dotnetVersion)"
    $validationWarnings += ".NET 9 SDK recommended but not installed"
    Write-Host ""
    Write-Host "Fix: Install .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
} else {
    Ok ".NET 9 SDK detected: $dotnetVersion"
}

# Check for required workloads
Write-Host ""
Info "Checking for required .NET workloads..."
$workloads = & dotnet workload list 2>&1 | Out-String
if ($workloads -match "No workloads installed") {
    Warn "No .NET workloads detected"
    $validationWarnings += "No .NET workloads installed"
} else {
    Ok "Workloads installed"
}

# ============================================================================
# Step 2: Check Windows Platform
# ============================================================================
Write-Host ""
Write-Host "Step 2: Checking Windows platform requirements..." -ForegroundColor Cyan

$onWindows = $IsWindows -or ($env:OS -eq 'Windows_NT')
if (-not $onWindows) {
    Warn "Non-Windows environment detected"
    $validationWarnings += "Desktop apps require Windows for building and testing"
    Write-Host ""
    Write-Host "Info: WPF and UWP builds are Windows-only. This script will only perform basic validation." -ForegroundColor Gray
    Write-Host "      For full desktop development, run this script on Windows." -ForegroundColor Gray
} else {
    Ok "Windows platform detected"
    
    # Check for Windows SDK on Windows
    Info "Checking for Windows SDK..."
    $windowsSdks = Get-ChildItem "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -ErrorAction SilentlyContinue
    if ($windowsSdks) {
        Ok "Windows SDK installed"
    } else {
        Warn "Windows SDK not detected"
        $validationWarnings += "Windows SDK not found - required for UWP builds"
        Write-Host ""
        Write-Host "Fix: Install Windows SDK from Visual Studio Installer or https://developer.microsoft.com/windows/downloads/windows-sdk/" -ForegroundColor Yellow
    }
    
    # Check for Visual Studio Build Tools
    Info "Checking for Visual Studio Build Tools..."
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsInstances = & $vswhere -products * -requires Microsoft.Component.MSBuild -property installationPath 2>&1
        if ($vsInstances) {
            Ok "Visual Studio Build Tools detected"
        } else {
            Warn "Visual Studio Build Tools not detected"
            $validationWarnings += "Visual Studio Build Tools not found"
        }
    } else {
        Warn "vswhere.exe not found - cannot verify Visual Studio installation"
        $validationWarnings += "Cannot verify Visual Studio Build Tools"
    }
    
    # Check for XAML Designer support
    Info "Checking for XAML tooling..."
    $xamlPackages = & dotnet list package --include-transitive 2>&1 | Select-String -Pattern "Microsoft.UI.Xaml|Windows.UI.Xaml"
    if ($xamlPackages) {
        Ok "XAML packages detected in project"
    } else {
        Info "XAML packages will be restored during build"
    }
}

# ============================================================================
# Step 3: Restore Desktop Projects
# ============================================================================
Write-Host ""
Write-Host "Step 3: Restoring desktop projects..." -ForegroundColor Cyan

$wpfProject = "src/Meridian.Wpf/Meridian.Wpf.csproj"
$uwpProject = "src/Meridian.Uwp/Meridian.Uwp.csproj"
$uiServicesProject = "src/Meridian.Ui.Services/Meridian.Ui.Services.csproj"

# Restore shared UI services first
Info "Restoring UI services project..."
& dotnet restore $uiServicesProject --verbosity quiet 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Error "UI services restore failed"
    $validationErrors += "Failed to restore Meridian.Ui.Services"
    Write-Host ""
    Write-Host "Fix: Check that the project file exists and dependencies are valid" -ForegroundColor Yellow
    Write-Host "      Run: dotnet restore $uiServicesProject" -ForegroundColor Yellow
} else {
    Ok "UI services restore succeeded"
}

# Restore WPF project
Info "Restoring WPF project..."
& dotnet restore $wpfProject --verbosity quiet 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Error "WPF restore failed"
    $validationErrors += "Failed to restore WPF project"
    Write-Host ""
    Write-Host "Fix: Run 'dotnet restore $wpfProject' to see detailed error messages" -ForegroundColor Yellow
} else {
    Ok "WPF restore succeeded"
}

# Restore UWP project (Windows only)
if ($onWindows) {
    Info "Restoring UWP project (legacy)..."
    & dotnet restore $uwpProject -r win-x64 --verbosity quiet 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Warn "UWP restore failed"
        $validationWarnings += "Failed to restore UWP project"
        Write-Host ""
        Write-Host "Fix: Verify Windows SDK and WinUI workloads are installed" -ForegroundColor Yellow
        Write-Host "      Run: dotnet restore $uwpProject -r win-x64" -ForegroundColor Yellow
        Write-Host "      Or run: ./scripts/dev/diagnose-uwp-xaml.ps1 for detailed diagnostics" -ForegroundColor Yellow
    } else {
        Ok "UWP restore succeeded"
    }
}

# ============================================================================
# Step 4: Smoke Build WPF
# ============================================================================
Write-Host ""
Write-Host "Step 4: Running WPF smoke build..." -ForegroundColor Cyan

Info "Building WPF project (Debug)..."
& dotnet build $wpfProject -c Debug --no-restore --verbosity quiet 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Error "WPF smoke build failed"
    $validationErrors += "WPF build failed"
    Write-Host ""
    Write-Host "Fix: Run 'dotnet build $wpfProject -c Debug' to see detailed error messages" -ForegroundColor Yellow
    Write-Host "      Common issues:" -ForegroundColor Yellow
    Write-Host "        - Missing Windows targeting pack: Install .NET Desktop Development workload" -ForegroundColor Yellow
    Write-Host "        - XAML compiler errors: Check XAML syntax in Views/" -ForegroundColor Yellow
} else {
    Ok "WPF smoke build succeeded"
}

# ============================================================================
# Step 5: Smoke Build UWP (Optional)
# ============================================================================
if ($onWindows) {
    Write-Host ""
    Write-Host "Step 5: Running UWP smoke build (optional legacy)..." -ForegroundColor Cyan
    
    Info "Building UWP project (Debug)..."
    & dotnet build $uwpProject -c Debug -r win-x64 --no-restore --verbosity quiet 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Warn "UWP smoke build failed"
        $validationWarnings += "UWP build failed (legacy project, not critical)"
        Write-Host ""
        Write-Host "Fix: Run './scripts/dev/diagnose-uwp-xaml.ps1' for targeted UWP diagnostics" -ForegroundColor Yellow
        Write-Host "      Or run: dotnet build $uwpProject -c Debug -r win-x64" -ForegroundColor Yellow
    } else {
        Ok "UWP smoke build succeeded"
    }
}

# ============================================================================
# Summary
# ============================================================================
Write-Host ""
Write-Host "============================================================================" -ForegroundColor Cyan
Write-Host "Desktop Bootstrap Summary" -ForegroundColor Cyan
Write-Host "============================================================================" -ForegroundColor Cyan
Write-Host ""

if ($validationErrors.Count -gt 0) {
    Write-Host "ERRORS FOUND ($($validationErrors.Count)):" -ForegroundColor Red
    foreach ($error in $validationErrors) {
        Write-Host "  ❌ $error" -ForegroundColor Red
    }
    Write-Host ""
}

if ($validationWarnings.Count -gt 0) {
    Write-Host "WARNINGS ($($validationWarnings.Count)):" -ForegroundColor Yellow
    foreach ($warning in $validationWarnings) {
        Write-Host "  ⚠️  $warning" -ForegroundColor Yellow
    }
    Write-Host ""
}

if ($validationErrors.Count -eq 0) {
    Write-Host "✅ Desktop environment validation complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps for desktop development:" -ForegroundColor Cyan
    Write-Host "  1. Build WPF app:              make build-wpf" -ForegroundColor Gray
    Write-Host "  2. Run desktop service tests:  make test-desktop-services" -ForegroundColor Gray
    Write-Host "  3. Open in Visual Studio:      Start-Process src/Meridian.Wpf/Meridian.Wpf.csproj" -ForegroundColor Gray
    if ($onWindows) {
        Write-Host "  4. UWP diagnostics (if needed): make uwp-xaml-diagnose" -ForegroundColor Gray
    }
    Write-Host ""
    exit 0
} else {
    Write-Host "❌ Desktop environment validation failed!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please address the errors above before proceeding with desktop development." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}
