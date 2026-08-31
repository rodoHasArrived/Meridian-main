<#
.SYNOPSIS
    Packages an unpackaged Meridian Desktop publish directory as an MSIX.

.DESCRIPTION
    Meridian Desktop is a WPF application, so the WinUI-only single-project MSIX
    targets do not package it. This script uses the Windows SDK MakeAppx tool
    directly, signs with SignTool when a certificate is supplied, and fails
    unless the requested package is produced.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [ValidateSet("x64", "arm64")]
    [string]$Architecture,

    # MSIX update detection is purely version-based, so a package that always ships the
    # manifest's literal 1.0.0.0 can never be installed over its predecessor. Release builds pass
    # the tag version here.
    [string]$PackageVersion = "",

    [string]$PackageCertificateKeyFile = "",

    [string]$PackageCertificatePassword = "",

    [string]$MakeAppxPath = "",

    [string]$SignToolPath = ""
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "windows-sdk-tools.ps1")

$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)

if ([System.IO.Path]::GetExtension($resolvedOutputPath) -ne ".msix") {
    throw "Desktop package output must use the .msix extension: $resolvedOutputPath"
}

$desktopExecutable = Join-Path $resolvedPublishDirectory "Meridian.Desktop.exe"
if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf)) {
    throw "Published desktop executable not found: $desktopExecutable"
}

$outputDirectory = Split-Path -Parent $resolvedOutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$stagingDirectory = Join-Path $outputDirectory ".package-$Architecture"
try {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    if (Test-Path -LiteralPath $resolvedOutputPath) {
        Remove-Item -LiteralPath $resolvedOutputPath -Force
    }

    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $resolvedPublishDirectory -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $stagingDirectory -Recurse -Force
    }

    [xml]$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw
    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
    $namespaceManager.AddNamespace("foundation", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")

    $identity = $manifest.SelectSingleNode("/foundation:Package/foundation:Identity", $namespaceManager)
    $application = $manifest.SelectSingleNode("/foundation:Package/foundation:Applications/foundation:Application", $namespaceManager)
    if ($null -eq $identity -or $null -eq $application) {
        throw "Package manifest must contain Identity and Application elements: $resolvedManifestPath"
    }

    $application.SetAttribute("Executable", "Meridian.Desktop.exe")
    $identity.SetAttribute("ProcessorArchitecture", $Architecture)
    if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
        if ($PackageVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
            throw "Package version must be Major.Minor.Build.Revision: $PackageVersion"
        }
        $identity.SetAttribute("Version", $PackageVersion)
    }
    $manifestPublisher = $identity.GetAttribute("Publisher")
    if ([string]::IsNullOrWhiteSpace($manifestPublisher)) {
        throw "Package manifest Identity Publisher is required: $resolvedManifestPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($PackageCertificateKeyFile)) {
        if (-not (Test-Path -LiteralPath $PackageCertificateKeyFile -PathType Leaf)) {
            throw "Package signing certificate not found: $PackageCertificateKeyFile"
        }

        $certificatePassword = if ([string]::IsNullOrEmpty($PackageCertificatePassword)) {
            [System.Security.SecureString]::new()
        }
        else {
            ConvertTo-SecureString -String $PackageCertificatePassword -AsPlainText -Force
        }
        try {
            $pfxData = Get-PfxData -FilePath $PackageCertificateKeyFile -Password $certificatePassword
        }
        catch {
            throw "Unable to read package signing certificate '$PackageCertificateKeyFile': $($_.Exception.Message)"
        }

        $signingCertificate = $pfxData.EndEntityCertificates | Select-Object -First 1
        if ($null -eq $signingCertificate -or [string]::IsNullOrWhiteSpace($signingCertificate.Subject)) {
            throw "Package signing certificate '$PackageCertificateKeyFile' has no end-entity subject."
        }

        # Windows requires the package Publisher to match the signing certificate subject.
        $identity.SetAttribute("Publisher", $signingCertificate.Subject)
    }

    $assetAttributes = $manifest.SelectNodes("//@Logo | //@Square150x150Logo | //@Square44x44Logo | //@Icon")
    foreach ($assetAttribute in $assetAttributes) {
        $relativeAssetPath = $assetAttribute.Value.Replace('\', [System.IO.Path]::DirectorySeparatorChar)
        $stagedAssetPath = Join-Path $stagingDirectory $relativeAssetPath
        if (-not (Test-Path -LiteralPath $stagedAssetPath -PathType Leaf)) {
            throw "Package manifest asset not found in publish layout: $relativeAssetPath"
        }
    }

    $stagedManifestPath = Join-Path $stagingDirectory "AppxManifest.xml"
    $xmlSettings = [System.Xml.XmlWriterSettings]::new()
    $xmlSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $xmlSettings.Indent = $true
    $xmlWriter = [System.Xml.XmlWriter]::Create($stagedManifestPath, $xmlSettings)
    try {
        $manifest.Save($xmlWriter)
    }
    finally {
        $xmlWriter.Dispose()
    }

    $resolvedMakeAppxPath = Resolve-WindowsSdkTool -ToolName "makeappx.exe" -ExplicitPath $MakeAppxPath
    $makeAppxOutput = & $resolvedMakeAppxPath pack /o /d $stagingDirectory /p $resolvedOutputPath 2>&1
    $makeAppxExitCode = $LASTEXITCODE
    $makeAppxOutput | ForEach-Object { Write-Host $_ }
    if ($makeAppxExitCode -ne 0) {
        throw "MakeAppx failed with exit code $makeAppxExitCode while creating $resolvedOutputPath"
    }
    if (-not (Test-Path -LiteralPath $resolvedOutputPath -PathType Leaf)) {
        throw "MakeAppx reported success but did not create $resolvedOutputPath"
    }

    if ([string]::IsNullOrWhiteSpace($PackageCertificateKeyFile)) {
        Write-Warning "No package signing certificate was supplied; created unsigned MSIX: $resolvedOutputPath"
    }
    else {
        $resolvedSignToolPath = Resolve-WindowsSdkTool -ToolName "signtool.exe" -ExplicitPath $SignToolPath
        $signToolArguments = @("sign", "/fd", "SHA256", "/a", "/f", $PackageCertificateKeyFile)
        if (-not [string]::IsNullOrEmpty($PackageCertificatePassword)) {
            $signToolArguments += @("/p", $PackageCertificatePassword)
        }
        $signToolArguments += $resolvedOutputPath

        $signToolOutput = & $resolvedSignToolPath $signToolArguments 2>&1
        $signToolExitCode = $LASTEXITCODE
        $signToolOutput | ForEach-Object { Write-Host $_ }
        if ($signToolExitCode -ne 0) {
            throw "SignTool failed with exit code $signToolExitCode while signing $resolvedOutputPath"
        }

        $packageArchive = [System.IO.Compression.ZipFile]::OpenRead($resolvedOutputPath)
        try {
            $signatureEntry = $packageArchive.Entries |
                Where-Object { $_.FullName -eq "AppxSignature.p7x" } |
                Select-Object -First 1
            if ($null -eq $signatureEntry -or $signatureEntry.Length -eq 0) {
                throw "SignTool reported success but $resolvedOutputPath has no AppxSignature.p7x entry."
            }
        }
        finally {
            $packageArchive.Dispose()
        }
    }

    Write-Output $resolvedOutputPath
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
