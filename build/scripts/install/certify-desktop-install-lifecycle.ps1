#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Certifies sign, install, launch, update, repair, rollback, and uninstall for Meridian Desktop.

.DESCRIPTION
    Drives the installed lifecycle of the packaged desktop workstation on a clean machine and
    writes a truthful receipt of every leg that ran.

    Two modes exist:

    * N-1 update certification (default). Requires a prior release package and certifies the
      full install -> launch -> update -> repair -> rollback -> uninstall chain.
    * First-release certification (-FirstRelease). The repository has no published N-1 package,
      so the update and rollback legs have nothing to exercise. They are recorded as
      'not-applicable' with an explicit reason instead of being silently omitted, and the legs
      that a first release can genuinely prove - signature, install, launch, repair, relaunch,
      uninstall - still run and still fail closed.

.PARAMETER TrustSelfSignedRoot
    Validation-only. Also trusts the supplied certificate as a root so a self-signed package can
    be installed and Authenticode-validated on an ephemeral CI runner. Never use this for a
    published release: it makes the runner trust the certificate rather than proving the
    certificate is already trusted.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CurrentPackage,

    [string]$PriorPackage = "",

    [Parameter(Mandatory)]
    [string]$SigningCertificatePfx,

    [Parameter(Mandatory)]
    [string]$SigningCertificatePassword,

    [Parameter(Mandatory)]
    [ValidateSet("x64", "ARM64")]
    [string]$Architecture,

    [switch]$FirstRelease,

    [switch]$TrustSelfSignedRoot,

    [string]$ReceiptPath = "artifacts/install-certification/desktop-install-lifecycle.json",

    [ValidateRange(5, 120)]
    [int]$LaunchObservationSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PackageName = "Meridian.Desktop"
$DefaultExecutableName = "Meridian.Desktop.exe"
$steps = [Collections.Generic.List[object]]::new()
$certificate = $null
$rootCertificate = $null
$launchedProcess = $null

function Add-Step([string]$Name, [string]$Status, [string]$Detail) {
    $steps.Add([ordered]@{ name = $Name; status = $Status; detail = $Detail; atUtc = [DateTimeOffset]::UtcNow }) | Out-Null
}

function Get-OnlyPackage([string]$PathValue) {
    $resolved = Resolve-Path -LiteralPath $PathValue -ErrorAction Stop
    $item = Get-Item -LiteralPath $resolved.Path
    if ($item.Extension -notin @(".msix", ".msixbundle")) {
        throw "Desktop certification requires an MSIX or MSIX bundle: $($item.FullName)"
    }
    return $item
}

function Assert-SignedPackage([IO.FileInfo]$Package) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Package.FullName
    if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
        throw "Package signature is not valid for $($Package.FullName): $($signature.Status) $($signature.StatusMessage)"
    }
    Add-Step "verify-signature" "passed" "$($Package.Name) signed by $($signature.SignerCertificate.Subject)"
    return $signature
}

function Remove-InstalledPackage {
    $installed = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
    if ($installed) {
        $installed | ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -ErrorAction Stop }
    }
}

function Install-Package([IO.FileInfo]$Package, [string]$StepName) {
    Add-AppxPackage -Path $Package.FullName -ForceApplicationShutdown -ErrorAction Stop
    $installed = Get-AppxPackage -Name $PackageName -ErrorAction Stop | Select-Object -First 1
    if ($null -eq $installed) { throw "Package installation did not register $PackageName." }
    $processor = [string]$installed.Architecture
    if ($Architecture -eq "ARM64" -and $processor -notmatch "Arm64") {
        throw "Installed package architecture '$processor' does not match ARM64 runner certification."
    }
    if ($Architecture -eq "x64" -and $processor -notmatch "X64") {
        throw "Installed package architecture '$processor' does not match x64 runner certification."
    }
    Add-Step $StepName "passed" "$($installed.PackageFullName) at $($installed.InstallLocation)"
    return $installed
}

# The packaged entry point is whatever package-desktop-msix.ps1 stamped into the manifest's
# Application@Executable. Reading it back from the installed manifest keeps this gate correct if
# the desktop assembly name ever changes again, instead of hard-coding a name that silently
# stops matching the package.
function Get-InstalledExecutable([string]$InstallLocation) {
    $manifestPath = Join-Path $InstallLocation "AppxManifest.xml"
    $executableName = $DefaultExecutableName
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
        $namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
        $namespaceManager.AddNamespace("foundation", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
        $application = $manifest.SelectSingleNode("/foundation:Package/foundation:Applications/foundation:Application", $namespaceManager)
        if ($null -ne $application) {
            $declared = $application.GetAttribute("Executable")
            if (-not [string]::IsNullOrWhiteSpace($declared)) {
                $executableName = Split-Path -Leaf $declared
            }
        }
    }

    $candidate = Get-ChildItem -LiteralPath $InstallLocation -Filter $executableName -File -Recurse |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "Installed $executableName was not found under $InstallLocation."
    }
    return $candidate
}

function Test-InstalledLaunch([string]$StepName) {
    $installed = Get-AppxPackage -Name $PackageName -ErrorAction Stop | Select-Object -First 1
    $candidate = Get-InstalledExecutable $installed.InstallLocation
    $script:launchedProcess = Start-Process -FilePath $candidate.FullName -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds $LaunchObservationSeconds
    if ($script:launchedProcess.HasExited) {
        throw "Installed desktop executable exited during the $LaunchObservationSeconds-second launch observation window with code $($script:launchedProcess.ExitCode)."
    }
    Add-Step $StepName "passed" "Launched PID $($script:launchedProcess.Id) from installed package ($($candidate.Name))."
    Stop-Process -Id $script:launchedProcess.Id -Force
    $script:launchedProcess.WaitForExit()
    $script:launchedProcess = $null
}

$hasPriorPackage = -not [string]::IsNullOrWhiteSpace($PriorPackage)
if ($FirstRelease) {
    if ($hasPriorPackage) {
        throw "-FirstRelease certifies a release with no published predecessor; do not also supply -PriorPackage."
    }
}
elseif (-not $hasPriorPackage) {
    throw "Installed lifecycle certification requires -PriorPackage, or -FirstRelease when no N-1 release exists."
}

$current = Get-OnlyPackage $CurrentPackage
$prior = if ($hasPriorPackage) { Get-OnlyPackage $PriorPackage } else { $null }
$receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
[IO.Directory]::CreateDirectory((Split-Path -Parent $receiptFullPath)) | Out-Null
$receipt = [ordered]@{
    schemaVersion = 2
    startedAtUtc = [DateTimeOffset]::UtcNow
    sourceCommit = $env:GITHUB_SHA
    architecture = $Architecture
    mode = if ($FirstRelease) { "first-release" } else { "n-1-update" }
    publisherTrust = if ($TrustSelfSignedRoot) { "runner-trusted-self-signed (validation only)" } else { "pre-trusted-certificate-chain" }
    currentPackage = [ordered]@{ name = $current.Name; sha256 = (Get-FileHash -LiteralPath $current.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    priorPackage = if ($null -eq $prior) { $null } else { [ordered]@{ name = $prior.Name; sha256 = (Get-FileHash -LiteralPath $prior.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } }
    status = "failed"
    steps = $steps
}

try {
    $securePassword = ConvertTo-SecureString $SigningCertificatePassword -AsPlainText -Force
    $certificate = Import-PfxCertificate -FilePath $SigningCertificatePfx -Password $securePassword -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Exportable:$false
    if ($null -eq $certificate) { throw "Signing certificate import returned no certificate." }
    if ($TrustSelfSignedRoot) {
        # Validation-only: an ephemeral runner has no reason to already trust a throwaway
        # certificate, so anchor it for the duration of this run and remove it in `finally`.
        $rootCertificate = Import-PfxCertificate -FilePath $SigningCertificatePfx -Password $securePassword -CertStoreLocation Cert:\LocalMachine\Root -Exportable:$false
    }
    Add-Step "trust-publisher" "passed" "$($certificate.Subject) / $($certificate.Thumbprint)"

    if ($null -ne $prior) {
        [void](Assert-SignedPackage $prior)
    }
    [void](Assert-SignedPackage $current)
    Remove-InstalledPackage

    if ($null -eq $prior) {
        Add-Step "install-prior" "not-applicable" "First release: no published N-1 package exists to install."
        Add-Step "launch-prior" "not-applicable" "First release: no published N-1 package exists to launch."

        [void](Install-Package $current "install-current")
        Test-InstalledLaunch "launch-current"

        Add-Step "update-current" "not-applicable" "First release: no published N-1 package exists to update from."
    }
    else {
        $priorInstalled = Install-Package $prior "install-prior"
        Test-InstalledLaunch "launch-prior"

        Add-AppxPackage -Path $current.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop
        $currentInstalled = Get-AppxPackage -Name $PackageName -ErrorAction Stop | Select-Object -First 1
        if ([version]$currentInstalled.Version -le [version]$priorInstalled.Version) {
            throw "Update did not advance package version ($($priorInstalled.Version) -> $($currentInstalled.Version))."
        }
        Add-Step "update-current" "passed" "$($priorInstalled.Version) -> $($currentInstalled.Version)"
        Test-InstalledLaunch "launch-current"
    }

    $installedCurrent = Get-AppxPackage -Name $PackageName -ErrorAction Stop | Select-Object -First 1
    Add-AppxPackage -Register (Join-Path $installedCurrent.InstallLocation "AppxManifest.xml") -DisableDevelopmentMode -ForceApplicationShutdown -ErrorAction Stop
    Add-Step "repair-current" "passed" "Re-registered current package manifest."
    Test-InstalledLaunch "launch-after-repair"

    if ($null -eq $prior) {
        Add-Step "rollback-prior" "not-applicable" "First release: no published N-1 package exists to roll back to."
        Add-Step "launch-after-rollback" "not-applicable" "First release: no published N-1 package exists to roll back to."
    }
    else {
        Remove-InstalledPackage
        [void](Install-Package $prior "rollback-prior")
        Test-InstalledLaunch "launch-after-rollback"
    }

    Remove-InstalledPackage
    if (Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue) { throw "Package remains installed after uninstall." }
    Add-Step "uninstall" "passed" "No $PackageName package remains registered."
    $receipt.status = "passed"
}
catch {
    $receipt.error = $_.Exception.Message
    throw
}
finally {
    if ($null -ne $launchedProcess -and -not $launchedProcess.HasExited) {
        Stop-Process -Id $launchedProcess.Id -Force -ErrorAction SilentlyContinue
    }
    Remove-InstalledPackage
    if ($null -ne $certificate) {
        Remove-Item -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $rootCertificate) {
        Remove-Item -LiteralPath "Cert:\LocalMachine\Root\$($rootCertificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }
    $receipt.completedAtUtc = [DateTimeOffset]::UtcNow
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptFullPath -Encoding utf8NoBOM
}

Write-Host "[install-certification] Lifecycle passed ($($receipt.mode)). Receipt: $receiptFullPath"
