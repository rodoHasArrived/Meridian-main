#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Certifies sign, install, launch, update, repair, rollback, and uninstall for Meridian Desktop.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CurrentPackage,

    [Parameter(Mandatory)]
    [string]$PriorPackage,

    [Parameter(Mandatory)]
    [string]$SigningCertificatePfx,

    [Parameter(Mandatory)]
    [string]$SigningCertificatePassword,

    [Parameter(Mandatory)]
    [ValidateSet("x64", "ARM64")]
    [string]$Architecture,

    [string]$ReceiptPath = "artifacts/install-certification/desktop-install-lifecycle.json",

    [ValidateRange(5, 120)]
    [int]$LaunchObservationSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PackageName = "Meridian.Desktop"
$steps = [Collections.Generic.List[object]]::new()
$certificate = $null
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

function Test-InstalledLaunch([string]$StepName) {
    $installed = Get-AppxPackage -Name $PackageName -ErrorAction Stop | Select-Object -First 1
    $candidate = Get-ChildItem -LiteralPath $installed.InstallLocation -Filter "Meridian.Wpf.exe" -File -Recurse |
        Select-Object -First 1
    if ($null -eq $candidate) { throw "Installed Meridian.Wpf.exe was not found under $($installed.InstallLocation)." }
    $script:launchedProcess = Start-Process -FilePath $candidate.FullName -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds $LaunchObservationSeconds
    if ($script:launchedProcess.HasExited) {
        throw "Installed desktop executable exited during the $LaunchObservationSeconds-second launch observation window with code $($script:launchedProcess.ExitCode)."
    }
    Add-Step $StepName "passed" "Launched PID $($script:launchedProcess.Id) from installed package."
    Stop-Process -Id $script:launchedProcess.Id -Force
    $script:launchedProcess.WaitForExit()
    $script:launchedProcess = $null
}

$current = Get-OnlyPackage $CurrentPackage
$prior = Get-OnlyPackage $PriorPackage
$receiptFullPath = [IO.Path]::GetFullPath($ReceiptPath)
[IO.Directory]::CreateDirectory((Split-Path -Parent $receiptFullPath)) | Out-Null
$receipt = [ordered]@{
    schemaVersion = 1
    startedAtUtc = [DateTimeOffset]::UtcNow
    sourceCommit = $env:GITHUB_SHA
    architecture = $Architecture
    currentPackage = [ordered]@{ name = $current.Name; sha256 = (Get-FileHash -LiteralPath $current.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    priorPackage = [ordered]@{ name = $prior.Name; sha256 = (Get-FileHash -LiteralPath $prior.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    status = "failed"
    steps = $steps
}

try {
    $securePassword = ConvertTo-SecureString $SigningCertificatePassword -AsPlainText -Force
    $certificate = Import-PfxCertificate -FilePath $SigningCertificatePfx -Password $securePassword -CertStoreLocation Cert:\CurrentUser\TrustedPeople -Exportable:$false
    if ($null -eq $certificate) { throw "Signing certificate import returned no certificate." }
    Add-Step "trust-publisher" "passed" "$($certificate.Subject) / $($certificate.Thumbprint)"

    [void](Assert-SignedPackage $prior)
    [void](Assert-SignedPackage $current)
    Remove-InstalledPackage

    $priorInstalled = Install-Package $prior "install-prior"
    Test-InstalledLaunch "launch-prior"

    Add-AppxPackage -Path $current.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion -ErrorAction Stop
    $currentInstalled = Get-AppxPackage -Name $PackageName -ErrorAction Stop | Select-Object -First 1
    if ([version]$currentInstalled.Version -le [version]$priorInstalled.Version) {
        throw "Update did not advance package version ($($priorInstalled.Version) -> $($currentInstalled.Version))."
    }
    Add-Step "update-current" "passed" "$($priorInstalled.Version) -> $($currentInstalled.Version)"
    Test-InstalledLaunch "launch-current"

    Add-AppxPackage -Register (Join-Path $currentInstalled.InstallLocation "AppxManifest.xml") -DisableDevelopmentMode -ForceApplicationShutdown -ErrorAction Stop
    Add-Step "repair-current" "passed" "Re-registered current package manifest."
    Test-InstalledLaunch "launch-after-repair"

    Remove-InstalledPackage
    [void](Install-Package $prior "rollback-prior")
    Test-InstalledLaunch "launch-after-rollback"

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
        Remove-Item -LiteralPath "Cert:\CurrentUser\TrustedPeople\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
    }
    $receipt.completedAtUtc = [DateTimeOffset]::UtcNow
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptFullPath -Encoding utf8NoBOM
}

Write-Host "[install-certification] Lifecycle passed. Receipt: $receiptFullPath"
