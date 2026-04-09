<#
.SYNOPSIS
    Meridian - Installation Script for Windows

.DESCRIPTION
    This script automates the installation and setup of Meridian on Windows.
    Features enhanced debugging output, progress tracking, and Windows toast notifications.

.PARAMETER Mode
    Installation mode: Docker, Native, Desktop, Check, Uninstall, UninstallDesktop, or Help

.PARAMETER DetailedOutput
    Enable verbose logging output

.PARAMETER NoNotify
    Disable Windows toast notifications

.PARAMETER LogPath
    Custom path for the installation log file

.PARAMETER AutoInstallPrereqs
    Automatically install missing prerequisites using winget

.PARAMETER Architecture
    Target architecture for Desktop mode: x64 or ARM64 (default: x64)

.PARAMETER SkipInstall
    Build only, do not install the MSIX package (Desktop mode)

.PARAMETER NoTrustCert
    Skip automatic certificate trust prompt (Desktop mode)

.PARAMETER DisableReadyToRun
    Disable ReadyToRun compilation for Desktop mode to reduce local publish disk usage

.PARAMETER EnableReadyToRun
    Enable ReadyToRun compilation for Desktop mode for release-oriented packaging

.EXAMPLE
    .\install.ps1
    Interactive installation

.EXAMPLE
    .\install.ps1 -Mode Docker
    Docker-based installation

.EXAMPLE
    .\install.ps1 -Mode Desktop -DetailedOutput
    Windows Desktop installation with verbose output

.EXAMPLE
    .\install.ps1 -Mode Desktop -Architecture ARM64
    Build Windows Desktop App for ARM64 architecture

.EXAMPLE
    .\install.ps1 -Mode Desktop -AutoInstallPrereqs
    Install Desktop App with automatic prerequisite installation

.EXAMPLE
    .\install.ps1 -Mode Desktop -DisableReadyToRun
    Build Desktop App without ReadyToRun to reduce local disk usage

.EXAMPLE
    .\install.ps1 -Mode Desktop -EnableReadyToRun
    Build Desktop App with ReadyToRun enabled for release packaging

.EXAMPLE
    .\install.ps1 -Mode Native -NoNotify
    Native .NET installation without notifications

.EXAMPLE
    .\install.ps1 -Mode UninstallDesktop
    Uninstall the Windows Desktop App
#>

param(
    [Parameter(Position = 0)]
    [ValidateSet("Docker", "Native", "Desktop", "Check", "Uninstall", "UninstallDesktop", "Help")]
    [string]$Mode = "",

    [switch]$DetailedOutput,

    [switch]$NoNotify,

    [string]$LogPath = "",

    [switch]$AutoInstallPrereqs,

    [ValidateSet("x64", "ARM64")]
    [string]$Architecture = "x64",

    [switch]$SkipInstall,

    [switch]$NoTrustCert,

    [switch]$DisableReadyToRun,

    [switch]$EnableReadyToRun
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..\..\..")).Path
$LibDir = Join-Path (Split-Path -Parent $ScriptDir) "lib"
$DockerFile = Join-Path $RepoRoot "deploy\docker\Dockerfile"
$DockerComposeFile = Join-Path $RepoRoot "deploy\docker\docker-compose.yml"

# Import the build notification module
$notificationModule = Join-Path $LibDir "BuildNotification.psm1"
if (Test-Path $notificationModule) {
    Import-Module $notificationModule -Force
    $useNotificationModule = $true
}
else {
    $useNotificationModule = $false
}

# Fallback functions if module not available
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Write-Info($message) { Write-Host "[INFO] $message" -ForegroundColor Blue }
function Write-Success($message) { Write-Host "[SUCCESS] $message" -ForegroundColor Green }
function Write-Warn($message) { Write-Host "[WARNING] $message" -ForegroundColor Yellow }
function Write-Err($message) { Write-Host "[ERROR] $message" -ForegroundColor Red }

# Desktop app package identity
$DesktopAppPackageName = "Meridian.Desktop"
$DesktopAppPublisher = "CN=Meridian"

function Test-WingetAvailable {
    try {
        $null = Get-Command winget -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Install-PrerequisiteWithWinget {
    param(
        [string]$PackageId,
        [string]$DisplayName
    )

    if (-not (Test-WingetAvailable)) {
        Write-Warn "winget is not available. Please install $DisplayName manually."
        return $false
    }

    Write-Info "Installing $DisplayName using winget..."
    try {
        $result = winget install -e --id $PackageId --accept-package-agreements --accept-source-agreements 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "$DisplayName installed successfully"
            return $true
        }
        else {
            Write-Warn "Failed to install $DisplayName. Exit code: $LASTEXITCODE"
            return $false
        }
    }
    catch {
        Write-Warn "Error installing $DisplayName : $($_.Exception.Message)"
        return $false
    }
}

function Test-DesktopPrerequisites {
    param(
        [switch]$AutoInstall
    )

    Write-Info "Checking Desktop App prerequisites..."
    $allMet = $true
    $missing = @()

    # Check .NET SDK 9.0+
    if (Test-Command "dotnet") {
        $dotnetVersion = dotnet --version
        $majorVersion = [int]($dotnetVersion -split '\.')[0]
        if ($majorVersion -ge 9) {
            Write-Success ".NET SDK: $dotnetVersion (9.0+ required)"
        }
        else {
            Write-Warn ".NET SDK: $dotnetVersion (9.0+ required for Desktop App)"
            $missing += @{ Name = ".NET SDK 9.0"; WingetId = "Microsoft.DotNet.SDK.9"; Display = ".NET SDK 9.0" }
            $allMet = $false
        }
    }
    else {
        Write-Warn ".NET SDK: Not installed (9.0+ required)"
        $missing += @{ Name = ".NET SDK 9.0"; WingetId = "Microsoft.DotNet.SDK.9"; Display = ".NET SDK 9.0" }
        $allMet = $false
    }

    # Check Windows SDK (optional but recommended)
    $windowsSdkPath = "C:\Program Files (x86)\Windows Kits\10"
    if (Test-Path $windowsSdkPath) {
        $sdkVersions = Get-ChildItem -Path "$windowsSdkPath\Include" -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1
        if ($sdkVersions) {
            Write-Success "Windows SDK: $($sdkVersions.Name)"
        }
        else {
            Write-Info "Windows SDK: Found but no version detected (Windows App SDK from NuGet will be used)"
        }
    }
    else {
        Write-Info "Windows SDK: Not found (Windows App SDK from NuGet will be used)"
    }

    # Check Visual C++ Redistributable (required for WinUI 3)
    $vcRedistKey = "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64"
    if (Test-Path $vcRedistKey) {
        $vcVersion = (Get-ItemProperty $vcRedistKey -ErrorAction SilentlyContinue).Version
        if ($vcVersion) {
            Write-Success "Visual C++ Redistributable: $vcVersion"
        }
        else {
            Write-Success "Visual C++ Redistributable: Installed"
        }
    }
    else {
        Write-Warn "Visual C++ Redistributable: Not detected (may cause runtime issues)"
        $missing += @{ Name = "VC++ Redistributable"; WingetId = "Microsoft.VCRedist.2015+.x64"; Display = "Visual C++ Redistributable" }
        # Don't fail on this - it might be installed differently
    }

    # Check if running on Windows 10 1809+ or Windows 11
    $osVersion = [System.Environment]::OSVersion.Version
    $buildNumber = $osVersion.Build
    if ($buildNumber -ge 17763) {
        Write-Success "Windows Version: Build $buildNumber (Windows 10 1809+ or Windows 11)"
    }
    else {
        Write-Warn "Windows Version: Build $buildNumber (Windows 10 1809+ recommended)"
    }

    # Auto-install missing prerequisites if requested
    if ($AutoInstall -and $missing.Count -gt 0) {
        Write-Host ""
        Write-Info "Attempting to install missing prerequisites..."

        foreach ($prereq in $missing) {
            $installed = Install-PrerequisiteWithWinget -PackageId $prereq.WingetId -DisplayName $prereq.Display
            if ($installed) {
                $allMet = $true  # Re-check after installation
            }
        }

        # Refresh environment after installations
        if ($missing.Count -gt 0) {
            Write-Info "Refreshing environment variables..."
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
        }
    }

    Write-Host ""
    return @{
        AllMet = $allMet
        Missing = $missing
    }
}

function Install-TrustedCertificate {
    param(
        [string]$MsixPath
    )

    Write-Info "Extracting certificate from MSIX package..."

    try {
        # Get the certificate from the MSIX package
        $signature = Get-AuthenticodeSignature -FilePath $MsixPath -ErrorAction Stop
        if ($null -eq $signature.SignerCertificate) {
            Write-Warn "No certificate found in MSIX package"
            return $false
        }

        $cert = $signature.SignerCertificate

        # Check if certificate is already trusted
        $trustedCerts = Get-ChildItem -Path "Cert:\CurrentUser\TrustedPeople" -ErrorAction SilentlyContinue
        $alreadyTrusted = $trustedCerts | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

        if ($alreadyTrusted) {
            Write-Success "Certificate is already trusted"
            return $true
        }

        Write-Host ""
        Write-Host "  Certificate Details:" -ForegroundColor Yellow
        Write-Host "    Subject:    $($cert.Subject)" -ForegroundColor Gray
        Write-Host "    Issuer:     $($cert.Issuer)" -ForegroundColor Gray
        Write-Host "    Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
        Write-Host "    Valid From: $($cert.NotBefore) to $($cert.NotAfter)" -ForegroundColor Gray
        Write-Host ""

        Write-Host "  To install the Desktop App, the signing certificate must be trusted." -ForegroundColor White
        Write-Host "  This adds the certificate to your 'Trusted People' certificate store." -ForegroundColor Gray
        Write-Host ""

        $confirm = Read-Host "  Trust this certificate? [Y/n]"
        if ($confirm -eq "" -or $confirm -match "^[Yy]") {
            # Export to temp file and import
            $tempCertPath = Join-Path $env:TEMP "mdc_temp_cert.cer"
            [System.IO.File]::WriteAllBytes($tempCertPath, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

            $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "CurrentUser")
            $store.Open("ReadWrite")
            $store.Add($cert)
            $store.Close()

            Remove-Item $tempCertPath -ErrorAction SilentlyContinue
            Write-Success "Certificate trusted successfully"
            return $true
        }
        else {
            Write-Warn "Certificate not trusted. MSIX installation may fail."
            return $false
        }
    }
    catch {
        Write-Warn "Failed to trust certificate: $($_.Exception.Message)"
        return $false
    }
}

function Install-MsixPackage {
    param(
        [string]$MsixPath
    )

    Write-Info "Installing MSIX package..."

    try {
        # Use Add-AppxPackage to install
        Add-AppxPackage -Path $MsixPath -ErrorAction Stop
        Write-Success "MSIX package installed successfully"

        # Get the installed app info
        $installedApp = Get-AppxPackage -Name $DesktopAppPackageName -ErrorAction SilentlyContinue
        if ($installedApp) {
            Write-Host ""
            Write-Host "  Installed App Details:" -ForegroundColor Green
            Write-Host "    Name:    $($installedApp.Name)" -ForegroundColor Gray
            Write-Host "    Version: $($installedApp.Version)" -ForegroundColor Gray
            Write-Host "    Status:  $($installedApp.Status)" -ForegroundColor Gray
            Write-Host ""
            Write-Host "  The app is now available in your Start Menu." -ForegroundColor White
            Write-Host "  Search for 'Meridian' to launch it." -ForegroundColor Gray
        }
        return $true
    }
    catch {
        Write-Err "Failed to install MSIX package: $($_.Exception.Message)"

        if ($_.Exception.Message -match "trust") {
            Write-Host ""
            Write-Host "  The package certificate is not trusted." -ForegroundColor Yellow
            Write-Host "  Run the script again without -NoTrustCert to trust the certificate." -ForegroundColor Gray
        }
        return $false
    }
}

function Uninstall-DesktopApp {
    Write-Info "Uninstalling Windows Desktop App..."

    try {
        $installedApp = Get-AppxPackage -Name $DesktopAppPackageName -ErrorAction SilentlyContinue
        if ($null -eq $installedApp) {
            Write-Warn "Desktop App is not installed"
            return $false
        }

        Write-Host ""
        Write-Host "  Found installed app:" -ForegroundColor Yellow
        Write-Host "    Name:    $($installedApp.Name)" -ForegroundColor Gray
        Write-Host "    Version: $($installedApp.Version)" -ForegroundColor Gray
        Write-Host "    Install: $($installedApp.InstallLocation)" -ForegroundColor Gray
        Write-Host ""

        $confirm = Read-Host "  Uninstall this app? [Y/n]"
        if ($confirm -eq "" -or $confirm -match "^[Yy]") {
            Remove-AppxPackage -Package $installedApp.PackageFullName -ErrorAction Stop
            Write-Success "Desktop App uninstalled successfully"

            # Optionally remove certificate
            Write-Host ""
            $removeCert = Read-Host "  Also remove the trusted certificate? [y/N]"
            if ($removeCert -match "^[Yy]") {
                $certs = Get-ChildItem -Path "Cert:\CurrentUser\TrustedPeople" |
                    Where-Object { $_.Subject -eq $DesktopAppPublisher }
                foreach ($cert in $certs) {
                    Remove-Item -Path $cert.PSPath -ErrorAction SilentlyContinue
                    Write-Success "Removed certificate: $($cert.Thumbprint)"
                }
            }
            return $true
        }
        else {
            Write-Info "Uninstall cancelled"
            return $false
        }
    }
    catch {
        Write-Err "Failed to uninstall: $($_.Exception.Message)"
        return $false
    }
}

function Show-Header {
    if ($useNotificationModule) {
        Show-BuildHeader -Title "Meridian - Installation Script" -Subtitle "Version 1.3.0 - Enhanced Windows Desktop Edition"
    }
    else {
        Write-Host ""
        Write-Host "======================================================================" -ForegroundColor Cyan
        Write-Host "           Meridian - Installation Script                " -ForegroundColor Cyan
        Write-Host "                         Version 1.3.0                                " -ForegroundColor Cyan
        Write-Host "======================================================================" -ForegroundColor Cyan
        Write-Host ""
    }
}

function Show-Help {
    Show-Header
    Write-Host "Usage: .\install.ps1 [-Mode <mode>] [options]"
    Write-Host ""
    Write-Host "Modes:" -ForegroundColor Yellow
    Write-Host "  Docker           Install using Docker (recommended for production)"
    Write-Host "  Native           Install using native .NET SDK (CLI)"
    Write-Host "  Desktop          Build and install Windows Desktop App (WPF)"
    Write-Host "  Check            Check prerequisites only"
    Write-Host "  Uninstall        Remove Docker containers and images"
    Write-Host "  UninstallDesktop Remove Windows Desktop App"
    Write-Host "  Help             Show this help message"
    Write-Host ""
    Write-Host "Options:" -ForegroundColor Yellow
    Write-Host "  -DetailedOutput      Enable verbose logging output"
    Write-Host "  -NoNotify            Disable Windows toast notifications"
    Write-Host "  -AutoInstallPrereqs  Automatically install missing prerequisites via winget"
    Write-Host ""
    Write-Host "Desktop Mode Options:" -ForegroundColor Yellow
    Write-Host "  -Architecture <arch> Target architecture: x64 (default) or ARM64"
    Write-Host "  -SkipInstall         Build only, do not install the MSIX package"
    Write-Host "  -DisableReadyToRun   Reduce local publish disk usage"
    Write-Host "  -EnableReadyToRun    Enable ReadyToRun for release-oriented packaging"
    Write-Host "  -NoTrustCert         Skip automatic certificate trust prompt"
    Write-Host ""
    Write-Host "Examples:" -ForegroundColor Yellow
    Write-Host "  .\install.ps1                                  # Interactive installation"
    Write-Host "  .\install.ps1 -Mode Docker                     # Quick Docker installation"
    Write-Host "  .\install.ps1 -Mode Native                     # Native .NET installation"
    Write-Host "  .\install.ps1 -Mode Desktop                    # Desktop App (x64, with install)"
    Write-Host "  .\install.ps1 -Mode Desktop -Architecture ARM64 # Desktop App for ARM64"
    Write-Host "  .\install.ps1 -Mode Desktop -SkipInstall       # Build only, no install"
    Write-Host "  .\install.ps1 -Mode Desktop -DisableReadyToRun # Lower-disk local publish"
    Write-Host "  .\install.ps1 -Mode Desktop -EnableReadyToRun  # Release-style publish"
    Write-Host "  .\install.ps1 -Mode Desktop -AutoInstallPrereqs # Auto-install .NET SDK etc."
    Write-Host "  .\install.ps1 -Mode UninstallDesktop           # Uninstall Desktop App"
    Write-Host ""
    Write-Host "Environment Variables (Desktop Mode):" -ForegroundColor Yellow
    Write-Host "  MDC_APPINSTALLER_URI      URI for AppInstaller auto-update"
    Write-Host "  MDC_SIGNING_CERT_PFX      Path to signing certificate (PFX)"
    Write-Host "  MDC_SIGNING_CERT_PASSWORD Password for signing certificate"
    Write-Host ""
}

function Test-Command($command) {
    try {
        Get-Command $command -ErrorAction Stop | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Test-Prerequisites {
    Write-Info "Checking prerequisites..."

    $missing = @()

    # Check Docker
    if (Test-Command "docker") {
        $dockerVersion = docker --version
        Write-Success "Docker: $dockerVersion"
    }
    else {
        Write-Warning "Docker: Not installed"
        $missing += "docker"
    }

    # Check Docker Compose
    if (Test-Command "docker") {
        try {
            docker compose version | Out-Null
            Write-Success "Docker Compose: Available"
        }
        catch {
            Write-Warning "Docker Compose: Not available"
            $missing += "docker-compose"
        }
    }

    # Check .NET SDK
    if (Test-Command "dotnet") {
        $dotnetVersion = dotnet --version
        Write-Success ".NET SDK: $dotnetVersion"

        if ([version]$dotnetVersion -lt [version]"8.0") {
            Write-Warning ".NET SDK version 8.0+ recommended (found: $dotnetVersion)"
        }
    }
    else {
        Write-Warning ".NET SDK: Not installed"
        $missing += "dotnet"
    }

    # Check Git
    if (Test-Command "git") {
        $gitVersion = git --version
        Write-Success "Git: $gitVersion"
    }
    else {
        Write-Warning "Git: Not installed"
        $missing += "git"
    }

    Write-Host ""

    if ($missing.Count -eq 0) {
        Write-Success "All prerequisites are installed!"
        return $true
    }
    else {
        Write-Warning "Missing prerequisites: $($missing -join ', ')"
        return $false
    }
}

function Show-Prerequisites-Suggestions {
    param(
        [switch]$ForDesktop
    )

    Write-Host ""
    Write-Info "Installation suggestions for Windows:"
    Write-Host ""

    if ($ForDesktop) {
        Write-Host "For Desktop App (WinUI 3):" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Using winget (recommended):" -ForegroundColor Yellow
        Write-Host "  winget install -e --id Microsoft.DotNet.SDK.9"
        Write-Host "  winget install -e --id Microsoft.VCRedist.2015+.x64"
        Write-Host ""
        Write-Host "Or run with automatic installation:" -ForegroundColor Yellow
        Write-Host "  .\install.ps1 -Mode Desktop -AutoInstallPrereqs"
        Write-Host ""
        Write-Host "Manual downloads:" -ForegroundColor Yellow
        Write-Host "  .NET SDK 9.0:      https://dotnet.microsoft.com/download/dotnet/9.0"
        Write-Host "  VC++ Redistributable: https://aka.ms/vs/17/release/vc_redist.x64.exe"
        Write-Host ""
    }
    else {
        Write-Host "Using winget (Windows Package Manager):" -ForegroundColor Yellow
        Write-Host "  winget install -e --id Docker.DockerDesktop"
        Write-Host "  winget install -e --id Microsoft.DotNet.SDK.9"
        Write-Host "  winget install -e --id Git.Git"
        Write-Host ""
        Write-Host "Using Chocolatey:" -ForegroundColor Yellow
        Write-Host "  choco install docker-desktop dotnet-sdk git -y"
        Write-Host ""
        Write-Host "Manual downloads:" -ForegroundColor Yellow
        Write-Host "  Docker Desktop: https://www.docker.com/products/docker-desktop"
        Write-Host "  .NET SDK 9.0:   https://dotnet.microsoft.com/download/dotnet/9.0"
        Write-Host "  Git:            https://git-scm.com/download/win"
        Write-Host ""
    }
}

function Setup-Config {
    Write-Info "Setting up configuration..."

    $configPath = Join-Path $RepoRoot "config\appsettings.json"
    $samplePath = Join-Path $RepoRoot "config\appsettings.sample.json"

    if (-not (Test-Path $configPath)) {
        if (Test-Path $samplePath) {
            Copy-Item $samplePath $configPath
            Write-Success "Created appsettings.json from template"
            Write-Warning "Remember to edit appsettings.json with your API credentials"
        }
        else {
            Write-Error "appsettings.sample.json not found"
            return $false
        }
    }
    else {
        Write-Info "appsettings.json already exists, skipping..."
    }

    # Create data directory
    $dataDir = Join-Path $RepoRoot "data"
    $logsDir = Join-Path $RepoRoot "logs"

    if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir | Out-Null }
    if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir | Out-Null }

    Write-Success "Created data and logs directories"
    return $true
}

function Install-Docker {
    Write-Info "Installing with Docker..."

    Push-Location $RepoRoot

    try {
        # Build image
        Write-Info "Building Docker image..."
        docker build -f $DockerFile -t meridian:latest $RepoRoot

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build Docker image"
            return
        }

        Write-Success "Docker image built successfully"

        # Setup config
        if (-not (Setup-Config)) { return }

        # Start container
        Write-Info "Starting container..."
        docker compose -f $DockerComposeFile up -d

        if ($LASTEXITCODE -eq 0) {
            Write-Success "Container started successfully"
            Write-Host ""
            Write-Host "======================================================================" -ForegroundColor Green
            Write-Host "                    Installation Complete!                            " -ForegroundColor Green
            Write-Host "======================================================================" -ForegroundColor Green
            Write-Host "  API:         http://localhost:8080" -ForegroundColor White
            Write-Host "  Metrics:     http://localhost:8080/metrics" -ForegroundColor White
            Write-Host "  Status:      http://localhost:8080/status" -ForegroundColor White
            Write-Host "  Health:      http://localhost:8080/health" -ForegroundColor White
            Write-Host "======================================================================" -ForegroundColor Green
            Write-Host "  View logs:   docker compose logs -f" -ForegroundColor Gray
            Write-Host "  Stop:        docker compose down" -ForegroundColor Gray
            Write-Host "  Restart:     docker compose restart" -ForegroundColor Gray
            Write-Host "======================================================================" -ForegroundColor Green
        }
        else {
            Write-Error "Failed to start container"
        }
    }
    finally {
        Pop-Location
    }
}

function Install-Native {
    Write-Info "Installing with native .NET..."

    if (-not (Test-Command "dotnet")) {
        Write-Error ".NET SDK is required for native installation"
        Show-Prerequisites-Suggestions
        return
    }

    Push-Location $RepoRoot

    try {
        $projectPath = Join-Path $RepoRoot "src\Meridian\Meridian.csproj"

        # Restore and build
        Write-Info "Restoring dependencies..."
        dotnet restore $projectPath

        Write-Info "Building project..."
        dotnet build $projectPath -c Release

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Build failed"
            return
        }

        Write-Success "Build completed successfully"

        # Setup config
        if (-not (Setup-Config)) { return }

        # Run tests
        Write-Info "Running self-tests..."
        dotnet run --project $projectPath --configuration Release -- --selftest

        Write-Host ""
        Write-Host "======================================================================" -ForegroundColor Green
        Write-Host "                    Installation Complete!                            " -ForegroundColor Green
        Write-Host "======================================================================" -ForegroundColor Green
        Write-Host "  Start desktop-local backend:" -ForegroundColor White
        Write-Host "    dotnet run --project src\Meridian\Meridian.csproj -- --mode desktop" -ForegroundColor Gray
        Write-Host "" -ForegroundColor White
        Write-Host "  Or use the publish script for a standalone executable:" -ForegroundColor White
        Write-Host "    .\publish.ps1 win-x64" -ForegroundColor Gray
        Write-Host "    .\publish\win-x64\Meridian.exe --mode desktop" -ForegroundColor Gray
        Write-Host "======================================================================" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

function Install-Desktop {
    if ($useNotificationModule) {
        Show-BuildSection -Title "Windows Desktop Application Installation"
    Initialize-BuildNotification -EnableToast (-not $NoNotify) -DetailedOutput $DetailedOutput
    }
    else {
        Write-Info "Installing Windows Desktop Application..."
    }

    # Determine architecture-specific settings
    $arch = $Architecture.ToLower()
    $runtimeId = "win-$arch"
    $platformName = if ($arch -eq "arm64") { "ARM64" } else { "x64" }

    Write-Info "Target: $platformName ($runtimeId)"

    # Check desktop-specific prerequisites
    $prereqResult = Test-DesktopPrerequisites -AutoInstall:$AutoInstallPrereqs
    if (-not $prereqResult.AllMet) {
        if ($prereqResult.Missing.Count -gt 0) {
            if ($useNotificationModule) {
                Show-BuildError -Error "Missing prerequisites for Desktop installation" `
                    -Suggestion "Install missing prerequisites or use -AutoInstallPrereqs flag" `
                    -Details @(
                        "Missing: $($prereqResult.Missing.Display -join ', ')",
                        "The Windows Desktop App requires .NET 9.0 SDK",
                        "Windows App SDK 1.6+ is also required (installed via NuGet)"
                    )
            }
            else {
                Write-Err "Missing prerequisites for Desktop installation"
            }
            Show-Prerequisites-Suggestions -ForDesktop
            return
        }
    }

    Push-Location $RepoRoot
    $buildSuccess = $false

    try {
        $desktopProjectPath = Join-Path $RepoRoot "src\Meridian.Wpf\Meridian.Wpf.csproj"
        $outputPath = Join-Path $RepoRoot "dist\$runtimeId\msix"
        $diagnosticLogDir = Join-Path $RepoRoot "diagnostic-logs"

        # Ensure diagnostic log directory exists
        if (-not (Test-Path $diagnosticLogDir)) {
            New-Item -ItemType Directory -Path $diagnosticLogDir -Force | Out-Null
        }

        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

        # ==================== STEP 1: Environment Check ====================
        if ($useNotificationModule) {
            Start-BuildStep -Name "Environment Verification" -Description "Checking build environment"
        }
        else {
            Write-Info "Verifying build environment..."
        }

        # Check .NET version
        $dotnetVersion = dotnet --version
        $dotnetSdkList = dotnet --list-sdks
        if ($useNotificationModule) {
            Update-BuildProgress -Message ".NET SDK version: $dotnetVersion"
            Update-BuildProgress -Message "Available SDKs: $($dotnetSdkList -join ', ')"
        }
        else {
            Write-Info ".NET SDK version: $dotnetVersion"
        }

        # Check for Windows SDK
        $windowsSdkPath = "C:\Program Files (x86)\Windows Kits\10"
        $hasWindowsSdk = Test-Path $windowsSdkPath
        if ($useNotificationModule) {
            if ($hasWindowsSdk) {
                Update-BuildProgress -Message "Windows SDK: Found at $windowsSdkPath"
            }
            else {
                Update-BuildProgress -Message "Windows SDK: Not found (will use Windows App SDK from NuGet)"
            }
            Complete-BuildStep -Success $true -Message "Environment verified"
        }
        else {
            if ($hasWindowsSdk) {
                Write-Success "Windows SDK found"
            }
        }

        # ==================== STEP 2: Clean Previous Build ====================
        if ($useNotificationModule) {
            Start-BuildStep -Name "Clean Previous Build" -Description "Removing old build artifacts"
        }
        else {
            Write-Info "Cleaning previous build..."
        }

        if (Test-Path $outputPath) {
            Remove-Item -Path $outputPath -Recurse -Force
            if ($useNotificationModule) {
                Update-BuildProgress -Message "Removed previous build at $outputPath"
            }
        }

        # Clean obj/bin directories for the project
        $objPath = Join-Path (Split-Path -Parent $desktopProjectPath) "obj"
        $binPath = Join-Path (Split-Path -Parent $desktopProjectPath) "bin"
        if (Test-Path $objPath) {
            Remove-Item -Path $objPath -Recurse -Force -ErrorAction SilentlyContinue
            if ($useNotificationModule) {
                Update-BuildProgress -Message "Cleaned obj directory"
            }
        }

        if ($useNotificationModule) {
            Complete-BuildStep -Success $true -Message "Clean completed"
        }

        # ==================== STEP 3: Restore Dependencies ====================
        if ($useNotificationModule) {
            Start-BuildStep -Name "Restore Dependencies" -Description "Restoring NuGet packages"
        }
        else {
            Write-Info "Restoring dependencies..."
        }

        $restoreLogFile = Join-Path $diagnosticLogDir "desktop-restore-$timestamp.log"
        $restoreArgs = @(
            "restore"
            $desktopProjectPath
            "-r", $runtimeId
            "-p:EnableFullWpfBuild=true"
            "-v", "normal"
        )

        if ($DetailedOutput) {
            $restoreArgs += @("-v", "detailed")
        }

        if ($useNotificationModule) {
            Update-BuildProgress -Message "Running: dotnet $($restoreArgs -join ' ')"
        }

        $restoreOutput = & dotnet $restoreArgs 2>&1 | Tee-Object -FilePath $restoreLogFile
        $restoreExitCode = $LASTEXITCODE

        if ($restoreExitCode -ne 0) {
            if ($useNotificationModule) {
                Complete-BuildStep -Success $false -Message "Restore failed"
                Show-BuildError -Error "Failed to restore NuGet packages" `
                    -LogFile $restoreLogFile `
                    -Suggestion "Check network connectivity and NuGet source configuration" `
                    -Details @(
                        "Exit code: $restoreExitCode",
                        "Check log file for detailed error messages",
                        "Run 'dotnet nuget list source' to verify NuGet sources"
                    )
            }
            else {
                Write-Err "Restore failed. Check log: $restoreLogFile"
            }
            return
        }

        # Count restored packages
        $packageCount = ($restoreOutput | Select-String -Pattern "Restored" -AllMatches).Count
        if ($useNotificationModule) {
            Update-BuildProgress -Message "Restored $packageCount package reference(s)"
            Complete-BuildStep -Success $true -Message "Dependencies restored"
        }
        else {
            Write-Success "Dependencies restored ($packageCount packages)"
        }

        # ==================== STEP 4: Build Application ====================
        if ($useNotificationModule) {
            Start-BuildStep -Name "Build Application" -Description "Compiling Windows Desktop App"
        }
        else {
            Write-Info "Building Windows Desktop App..."
        }

        $buildLogFile = Join-Path $diagnosticLogDir "desktop-build-$timestamp.log"
        $publishLogFile = Join-Path $diagnosticLogDir "desktop-publish-$timestamp.log"

        # Build first to catch compilation errors
        $buildArgs = @(
            "build"
            $desktopProjectPath
            "-c", "Release"
            "-r", $runtimeId
            "--no-restore"
            "-p:EnableFullWpfBuild=true"
            "-p:Platform=$platformName"
        )

        if ($DetailedOutput) {
            $buildArgs += @("-v", "detailed")
        }
        else {
            $buildArgs += @("-v", "normal")
        }

        if ($useNotificationModule) {
            Update-BuildProgress -Message "Compiling C# code..."
            Update-BuildProgress -Message "Target: win-x64 | Config: Release"
        }

        $buildOutput = & dotnet $buildArgs 2>&1 | Tee-Object -FilePath $buildLogFile
        $buildExitCode = $LASTEXITCODE

        # Extract warnings and errors
        $buildWarnings = $buildOutput | Select-String -Pattern "warning [A-Z]+\d+:" -AllMatches
        $buildErrors = $buildOutput | Select-String -Pattern "error [A-Z]+\d+:" -AllMatches

        if ($buildExitCode -ne 0) {
            if ($useNotificationModule) {
                Complete-BuildStep -Success $false -Message "Build failed"

                $errorDetails = @("Exit code: $buildExitCode")
                if ($buildErrors.Count -gt 0) {
                    $errorDetails += "Errors found: $($buildErrors.Count)"
                    # Show first few errors
                    $buildErrors | Select-Object -First 5 | ForEach-Object {
                        $errorDetails += "  $_"
                    }
                }

                Show-BuildError -Error "Build compilation failed" `
                    -LogFile $buildLogFile `
                    -Suggestion "Review the build errors above and fix the code issues" `
                    -Details $errorDetails
            }
            else {
                Write-Err "Build failed. Check log: $buildLogFile"
                if ($buildErrors.Count -gt 0) {
                    Write-Host "Errors:" -ForegroundColor Red
                    $buildErrors | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
                }
            }
            return
        }

        if ($buildWarnings.Count -gt 0) {
            if ($useNotificationModule) {
                Show-BuildWarning -Message "Build completed with $($buildWarnings.Count) warning(s)" `
                    -Suggestion "Review warnings in: $buildLogFile"
            }
            else {
                Write-Warn "Build completed with $($buildWarnings.Count) warning(s)"
            }
        }

        if ($useNotificationModule) {
            Update-BuildProgress -Message "Build succeeded, proceeding to publish..."
            Complete-BuildStep -Success $true -Message "Compilation successful"
        }
        else {
            Write-Success "Build completed"
        }

        # ==================== STEP 5: Publish Application ====================
        if ($useNotificationModule) {
            Start-BuildStep -Name "Publish Application" -Description "Creating MSIX package"
        }
        else {
            Write-Info "Publishing MSIX package..."
        }

        if ($DisableReadyToRun -and $EnableReadyToRun) {
            throw "Specify only one of -DisableReadyToRun or -EnableReadyToRun."
        }

        $appInstallerUri = $env:MDC_APPINSTALLER_URI
        $certPfxPath = $env:MDC_SIGNING_CERT_PFX
        $certPassword = $env:MDC_SIGNING_CERT_PASSWORD
        $publishReadyToRun = if ($EnableReadyToRun) { "true" } else { "false" }
        $publishArgs = @(
            "publish"
            $desktopProjectPath
            "-c", "Release"
            "-r", $runtimeId
            "--self-contained", "true"
            "-p:EnableFullWpfBuild=true"
            "-p:WindowsPackageType=MSIX"
            "-p:PublishReadyToRun=$publishReadyToRun"
            "-p:Platform=$platformName"
            "-p:AppxPackageDir=$outputPath\\"
        )

        if (-not [string]::IsNullOrWhiteSpace($appInstallerUri)) {
            $publishArgs += @(
                "-p:GenerateAppInstallerFile=true"
                "-p:AppInstallerUri=$appInstallerUri"
                "-p:AppInstallerCheckForUpdateFrequency=OnApplicationRun"
                "-p:AppInstallerUpdateFrequency=1"
            )
        }

        if (-not [string]::IsNullOrWhiteSpace($certPfxPath)) {
            $publishArgs += @(
                "-p:PackageCertificateKeyFile=$certPfxPath"
                "-p:PackageCertificatePassword=$certPassword"
            )
        }
        else {
            $publishArgs += "-p:GenerateTemporaryStoreCertificate=true"
        }

        if ($DetailedOutput) {
            $publishArgs += @("-v", "detailed")
        }
        else {
            $publishArgs += @("-v", "normal")
        }

        if ($useNotificationModule) {
            Update-BuildProgress -Message "Publishing MSIX package..."
            Update-BuildProgress -Message "ReadyToRun: $(if ($EnableReadyToRun) { 'Enabled (faster startup)' } else { 'Disabled (lower disk usage)' })"
        }

        $publishOutput = & dotnet $publishArgs 2>&1 | Tee-Object -FilePath $publishLogFile
        $publishExitCode = $LASTEXITCODE

        if ($publishExitCode -ne 0) {
            if ($useNotificationModule) {
                Complete-BuildStep -Success $false -Message "Publish failed"
                Show-BuildError -Error "Failed to publish application" `
                    -LogFile $publishLogFile `
                    -Suggestion "Check if all required Windows SDK components are installed" `
                    -Details @(
                        "Exit code: $publishExitCode",
                        "The publish step creates the final executable",
                        "Ensure Windows App SDK dependencies are available"
                    )
            }
            else {
                Write-Err "Publish failed. Check log: $publishLogFile"
            }
            return
        }

        if ($useNotificationModule) {
            Complete-BuildStep -Success $true -Message "Application published"
        }
        else {
            Write-Success "Publish completed"
        }

        # ==================== STEP 6: Verify Installation ====================
        if ($useNotificationModule) {
            Start-BuildStep -Name "Verify Installation" -Description "Checking MSIX output"
        }
        else {
            Write-Info "Verifying MSIX output..."
        }

        $msixPackages = Get-ChildItem -Path $outputPath -Filter "*.msix*" -File -ErrorAction SilentlyContinue
        if (-not $msixPackages) {
            if ($useNotificationModule) {
                Complete-BuildStep -Success $false -Message "MSIX package not found"
                Show-BuildError -Error "MSIX package not found at expected location" `
                    -Suggestion "Check build output and ensure the project packaged correctly" `
                    -Details @(
                        "Expected MSIX/MSIXBundle in: $outputPath",
                        "Check publish output directory: $outputPath"
                    )
            }
            else {
                Write-Err "MSIX package not found in: $outputPath"
            }
            return
        }

        $packageCount = $msixPackages.Count
        $totalSize = "{0:N2} MB" -f (($msixPackages | Measure-Object -Property Length -Sum).Sum / 1MB)

        if ($useNotificationModule) {
            Update-BuildProgress -Message "Packages: $packageCount ($totalSize total)"
            Complete-BuildStep -Success $true -Message "Installation verified"
        }
        else {
            Write-Success "MSIX package(s) verified: $packageCount"
        }

        $buildSuccess = $true

        # ==================== STEP 7: Certificate Trust & Installation ====================
        $msixFile = $msixPackages | Where-Object { $_.Extension -eq ".msix" } | Select-Object -First 1
        $installedSuccessfully = $false

        if (-not $SkipInstall -and $msixFile) {
            if ($useNotificationModule) {
                Start-BuildStep -Name "Install Application" -Description "Installing MSIX package"
            }
            else {
                Write-Info "Preparing to install MSIX package..."
            }

            # Trust certificate if not using a production certificate and not skipped
            if (-not $NoTrustCert -and [string]::IsNullOrWhiteSpace($certPfxPath)) {
                $certTrusted = Install-TrustedCertificate -MsixPath $msixFile.FullName
                if (-not $certTrusted) {
                    Write-Warn "Certificate was not trusted. Installation may fail."
                }
            }

            # Install the MSIX package
            $installedSuccessfully = Install-MsixPackage -MsixPath $msixFile.FullName

            if ($useNotificationModule) {
                Complete-BuildStep -Success $installedSuccessfully -Message $(if ($installedSuccessfully) { "Application installed" } else { "Installation skipped or failed" })
            }
        }
        elseif ($SkipInstall) {
            Write-Info "Skipping installation (-SkipInstall specified)"
        }

        # Show final summary
        if ($useNotificationModule) {
            Show-BuildSummary -Success $true -OutputPath $outputPath
        }

        Write-Host ""
        Write-Host "======================================================================" -ForegroundColor Green
        if ($installedSuccessfully) {
            Write-Host "           Windows Desktop App Installed Successfully!               " -ForegroundColor Green
        }
        else {
            Write-Host "           Windows Desktop App Build Complete!                       " -ForegroundColor Green
        }
        Write-Host "======================================================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "  Architecture: " -ForegroundColor White -NoNewline
        Write-Host "$platformName ($runtimeId)" -ForegroundColor Cyan
        Write-Host "  Location:     " -ForegroundColor White -NoNewline
        Write-Host $outputPath -ForegroundColor Cyan
        Write-Host "  Packages:     " -ForegroundColor White -NoNewline
        Write-Host "$packageCount MSIX file(s)" -ForegroundColor Cyan
        Write-Host "  Size:         " -ForegroundColor White -NoNewline
        Write-Host "$totalSize total" -ForegroundColor Gray
        Write-Host ""
        if (-not [string]::IsNullOrWhiteSpace($certPfxPath)) {
            Write-Host "  Signing:      " -ForegroundColor White -NoNewline
            Write-Host "Signed with $certPfxPath" -ForegroundColor Gray
        }
        else {
            Write-Host "  Signing:      " -ForegroundColor White -NoNewline
            Write-Host "Temporary dev certificate used" -ForegroundColor Gray
        }
        Write-Host ""
        if ($installedSuccessfully) {
            Write-Host "  Status:       " -ForegroundColor White -NoNewline
            Write-Host "INSTALLED - Available in Start Menu" -ForegroundColor Green
            Write-Host ""
            Write-Host "  Launch:       Search for 'Meridian' in Start Menu" -ForegroundColor Gray
        }
        else {
            Write-Host "  To install manually:" -ForegroundColor Yellow
            Write-Host "    1. Trust the certificate (if self-signed):" -ForegroundColor Gray
            Write-Host "       Right-click MSIX > Properties > Digital Signatures > Details > View Certificate > Install" -ForegroundColor DarkGray
            Write-Host "    2. Double-click the .msix file to install" -ForegroundColor Gray
            Write-Host ""
            Write-Host "  Or run: Add-AppxPackage -Path `"$($msixFile.FullName)`"" -ForegroundColor Gray
        }
        Write-Host ""
        Write-Host "  Guidance:     " -ForegroundColor White -NoNewline
        Write-Host "docs/guides/msix-packaging.md" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  Build logs:   " -ForegroundColor White -NoNewline
        Write-Host $diagnosticLogDir -ForegroundColor Gray
        Write-Host "======================================================================" -ForegroundColor Green
    }
    catch {
        if ($useNotificationModule) {
            Show-BuildError -Error "Unexpected error during installation" `
                -Details @(
                    "Exception: $($_.Exception.Message)",
                    "Line: $($_.InvocationInfo.ScriptLineNumber)"
                ) `
                -Suggestion "Check the diagnostic logs for more details"
            Show-BuildSummary -Success $false
        }
        else {
            Write-Err "Unexpected error: $($_.Exception.Message)"
        }
        throw
    }
    finally {
        Pop-Location
        if (-not $buildSuccess -and $useNotificationModule) {
            Write-Host ""
            Write-Host "  Troubleshooting Resources:" -ForegroundColor Yellow
            Write-Host "    • Run build diagnostics: python build-system\cli\buildctl.py doctor" -ForegroundColor Gray
            Write-Host "    • Check .NET SDK: dotnet --info" -ForegroundColor Gray
            Write-Host "    • Verify Windows SDK: Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots'" -ForegroundColor Gray
            Write-Host "    • Documentation: docs/guides/troubleshooting.md" -ForegroundColor Gray
            Write-Host ""
        }
    }
}

function Uninstall-Docker {
    Write-Info "Uninstalling Docker containers and images..."

    Push-Location $RepoRoot

    try {
        # Stop containers
        Write-Info "Stopping containers..."
        docker compose -f $DockerComposeFile down 2>$null

        # Remove image
        Write-Info "Removing Docker image..."
        docker rmi meridian:latest 2>$null

        Write-Success "Uninstallation complete"
        Write-Warning "Data directory (.\data) was preserved. Remove manually if needed."
    }
    finally {
        Pop-Location
    }
}

function Show-InteractiveMenu {
    Show-Header

    Test-Prerequisites | Out-Null
    Write-Host ""

    Write-Host "Choose installation method:" -ForegroundColor Yellow
    Write-Host "  1) Docker (recommended for production)"
    Write-Host "  2) Native .NET SDK (CLI application)"
    Write-Host "  3) Windows Desktop App (WinUI 3 - recommended for Windows)"
    Write-Host "  4) Windows Desktop App (ARM64)"
    Write-Host "  5) Check prerequisites only"
    Write-Host "  6) Uninstall Docker containers"
    Write-Host "  7) Uninstall Desktop App"
    Write-Host "  8) Exit"
    Write-Host ""

    $choice = Read-Host "Enter choice [1-8]"

    switch ($choice) {
        "1" { Install-Docker }
        "2" { Install-Native }
        "3" {
            $script:Architecture = "x64"
            Install-Desktop
        }
        "4" {
            $script:Architecture = "ARM64"
            Install-Desktop
        }
        "5" {
            Test-Prerequisites | Out-Null
            Show-Prerequisites-Suggestions
        }
        "6" { Uninstall-Docker }
        "7" { Uninstall-DesktopApp }
        "8" {
            Write-Host "Exiting..."
            exit 0
        }
        default {
            Write-Error "Invalid choice"
            exit 1
        }
    }
}

# Main
switch ($Mode) {
    "Docker" {
        Show-Header
        Test-Prerequisites | Out-Null
        Install-Docker
    }
    "Native" {
        Show-Header
        Test-Prerequisites | Out-Null
        Install-Native
    }
    "Desktop" {
        Show-Header
        Install-Desktop
    }
    "Check" {
        Show-Header
        Test-Prerequisites | Out-Null
        Show-Prerequisites-Suggestions
    }
    "Uninstall" {
        Show-Header
        Uninstall-Docker
    }
    "UninstallDesktop" {
        Show-Header
        Uninstall-DesktopApp
    }
    "Help" {
        Show-Help
    }
    "" {
        Show-InteractiveMenu
    }
}
