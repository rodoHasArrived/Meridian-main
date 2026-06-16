<#
.SYNOPSIS
Run a focused duplication audit for source files.

.DESCRIPTION
Scans the repository for duplicate leaf file names and duplicate file contents.
Generates a compact report for common cleanup targets.
#>
[CmdletBinding()]
param(
    [switch]$IncludeHashes = $false,
    [string[]]$SkipDirectories = @(".git", ".vs", ".idea", "artifacts/bin", "artifacts/obj", "artifacts/publish", "node_modules")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $root = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "This script must be run inside a Git checkout."
    }

    return [string](Resolve-Path $root).Path
}

function Get-RelativePath {
    param([Parameter(Mandatory)] [string]$RepoRoot, [Parameter(Mandatory)] [string]$Path)

    $baseUri = [System.Uri](Ensure-TrailingSeparator $RepoRoot)
    $targetUri = [System.Uri]([string](Resolve-Path -Path $Path).Path)
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString())
}

function Ensure-TrailingSeparator {
    param([Parameter(Mandatory)] [string]$Path)
    if ($Path.EndsWith([IO.Path]::DirectorySeparatorChar)) {
        return $Path
    }
    return "$Path$([IO.Path]::DirectorySeparatorChar)"
}

function IsPathSkipped {
    param([string]$Candidate)
    $normalizedCandidate = (NormalizePath $Candidate)

    foreach ($skip in $SkipDirectories) {
        $normalizedSkip = NormalizePath $skip.TrimEnd([IO.Path]::DirectorySeparatorChar)
        if ($normalizedCandidate -like "*$([IO.Path]::DirectorySeparatorChar)$normalizedSkip$([IO.Path]::DirectorySeparatorChar)*" -or
            $normalizedCandidate -like "*$([IO.Path]::DirectorySeparatorChar)$normalizedSkip") {
            return $true
        }
    }

    return $false
}

function NormalizePath {
    param([Parameter(Mandatory)] [string]$Path)

    return $Path -replace "/", "\" -replace "[\\]{2,}", "\" -replace "\\$"
}

function Get-TrackedFiles {
    param([Parameter(Mandatory)] [string]$RepoRoot)

    $files = Get-ChildItem -Path $RepoRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { -not (IsPathSkipped -Candidate $_.FullName) }

    $tracked = git -C $RepoRoot ls-files
    $trackedSet = @{}
    foreach ($entry in $tracked) {
        $trackedSet[$entry] = $true
    }

    return $files | Where-Object { $trackedSet.ContainsKey((Get-RelativePath -RepoRoot $RepoRoot -Path $_.FullName).Replace("/", "\")) }
}

$repoRoot = Get-RepoRoot
$trackedFiles = Get-TrackedFiles -RepoRoot $repoRoot

Write-Host "Scanning $($trackedFiles.Count) tracked files under $repoRoot"

$fileNames = $trackedFiles | Group-Object -Property Name | Where-Object Count -gt 1
Write-Host ""
Write-Host "Duplicate file names"
if ($fileNames) {
    foreach ($group in $fileNames) {
        Write-Host (" - {0} ({1})" -f $group.Name, $group.Count)
        foreach ($item in ($group.Group | Select-Object -ExpandProperty FullName)) {
            Write-Host ("   {0}" -f (Get-RelativePath -RepoRoot $repoRoot -Path $item))
        }
    }
}
else {
    Write-Host "No duplicate leaf names detected."
}

if (-not $IncludeHashes) {
    return
}

$hashGroups = @{}
foreach ($file in $trackedFiles) {
    $hash = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash
    if (-not $hashGroups.ContainsKey($hash)) {
        $hashGroups[$hash] = @()
    }
    $hashGroups[$hash] += $file
}

Write-Host ""
Write-Host "Duplicate file contents (SHA-256)"
$dupes = $hashGroups.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 }
if ($dupes) {
    foreach ($entry in $dupes) {
        Write-Host (" - SHA: {0}" -f $entry.Key)
        foreach ($file in $entry.Value) {
            Write-Host ("   {0}" -f (Get-RelativePath -RepoRoot $repoRoot -Path $file.FullName))
        }
    }
}
else {
    Write-Host "No exact duplicate content detected."
}
