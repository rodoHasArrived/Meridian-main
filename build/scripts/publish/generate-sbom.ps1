[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDropPath,

    [string]$BuildComponentPath = ".",

    [string]$PackageName = "Meridian",

    [string]$PackageVersion = "0.0.0-local",

    [string]$PackageSupplier = "Meridian",

    [string]$NamespaceUriBase = "https://github.com/rodoHasArrived/Meridian-main",

    [string]$SbomToolPath = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..\..\..")).Path
Set-Location $RepoRoot

function Resolve-SbomTool {
    param([string]$ConfiguredPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        if (-not (Test-Path $ConfiguredPath)) {
            throw "SBOM tool path does not exist: $ConfiguredPath"
        }

        return (Resolve-Path $ConfiguredPath).Path
    }

    $candidate = Get-Command "sbom-tool" -ErrorAction SilentlyContinue
    if ($candidate) {
        return $candidate.Source
    }

    $dotnetToolCandidate = Get-Command "Microsoft.Sbom.Tool" -ErrorAction SilentlyContinue
    if ($dotnetToolCandidate) {
        return $dotnetToolCandidate.Source
    }

    throw @"
Microsoft SBOM Tool is not available on PATH.

Install the free open-source tool, then rerun this script:
  dotnet tool install --global Microsoft.Sbom.DotNetTool

Or pass -SbomToolPath with the path to a downloaded sbom-tool executable.
"@
}

function Resolve-RepoPath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $PathValue))
}

$ResolvedBuildDropPath = Resolve-RepoPath $BuildDropPath
$ResolvedBuildComponentPath = Resolve-RepoPath $BuildComponentPath

if (-not (Test-Path $ResolvedBuildDropPath)) {
    throw "Build drop path does not exist: $ResolvedBuildDropPath"
}

if (-not (Test-Path $ResolvedBuildComponentPath)) {
    throw "Build component path does not exist: $ResolvedBuildComponentPath"
}

$Tool = Resolve-SbomTool -ConfiguredPath $SbomToolPath

Write-Host "[sbom] Generating SBOM for $ResolvedBuildDropPath"
& $Tool generate `
    -b $ResolvedBuildDropPath `
    -bc $ResolvedBuildComponentPath `
    -pn $PackageName `
    -pv $PackageVersion `
    -ps $PackageSupplier `
    -nsb $NamespaceUriBase

if ($LASTEXITCODE -ne 0) {
    throw "SBOM generation failed with exit code $LASTEXITCODE"
}

Write-Host "[sbom] SBOM generation complete"
