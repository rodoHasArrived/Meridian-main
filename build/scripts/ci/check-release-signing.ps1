param(
    [string]$OutputPath = 'artifacts/release-readiness/signing.json',
    [string]$ManifestPath = 'src/Meridian.Wpf/Package.appxmanifest'
)

$ErrorActionPreference = 'Stop'
$certificate = $null
$receipt = [ordered]@{
    schemaVersion = 1
    commitSha = $env:GITHUB_SHA
    runId = $env:GITHUB_RUN_ID
    runAttempt = $env:GITHUB_RUN_ATTEMPT
    observedAt = [DateTime]::UtcNow.ToString('o')
    status = 'failed'
    problem = 'signing-secrets-unavailable'
    hasPrivateKey = $false
    publisherMatches = $false
    notBefore = $null
    notAfter = $null
}

try {
    if ([string]::IsNullOrWhiteSpace($env:SIGNING_PFX) -or [string]::IsNullOrWhiteSpace($env:SIGNING_PASSWORD)) {
        throw 'Signing secrets are unavailable.'
    }
    $receipt.problem = 'certificate-unreadable'
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        [Convert]::FromBase64String($env:SIGNING_PFX), $env:SIGNING_PASSWORD,
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    $receipt.notBefore = $certificate.NotBefore.ToUniversalTime().ToString('o')
    $receipt.notAfter = $certificate.NotAfter.ToUniversalTime().ToString('o')
    $receipt.hasPrivateKey = $certificate.HasPrivateKey
    $receipt.problem = 'manifest-unreadable'
    [xml]$manifest = Get-Content $ManifestPath
    $receipt.publisherMatches = $certificate.Subject -eq $manifest.Package.Identity.Publisher
    $receipt.problem = 'certificate-invalid'
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow -or $certificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow) {
        throw 'Signing certificate has no private key or is outside its validity window.'
    }
    $receipt.problem = 'publisher-mismatch'
    if (-not $receipt.publisherMatches) { throw 'Signing publisher does not match the MSIX identity.' }
    $receipt.status = 'passed'
    $receipt.problem = $null
} catch {
    # Never serialize certificate material, passwords or provider exception details.
    Write-Host "::error::Release signing prerequisite failed ($($receipt.problem)); inspect the protected desktop-release-signing environment."
} finally {
    if ($null -ne $certificate) { $certificate.Dispose() }
    $parent = Split-Path -Parent $OutputPath
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $receipt | ConvertTo-Json | Set-Content -Path $OutputPath -Encoding utf8
}
if ($receipt.status -ne 'passed') { exit 1 }
Write-Host 'Signing certificate, private key, publisher and validity prerequisites passed.'
