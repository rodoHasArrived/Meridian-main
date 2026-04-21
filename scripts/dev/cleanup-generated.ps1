param(
    [switch]$Execute,
    [switch]$IncludeNodeModules,
    [switch]$IncludeVisualStudio,
    [switch]$IncludeTemp,
    [switch]$IncludeLogs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    $root = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "This script must be run from inside a Git working tree."
    }

    return $root.Trim()
}

function Test-TrackedContent {
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$FullPath
    )

    $relativePath = Get-RelativeRepoPath -RepoRoot $RepoRoot -FullPath $FullPath
    $tracked = (& git -C $RepoRoot ls-files -- $relativePath 2>$null)
    return -not [string]::IsNullOrWhiteSpace(($tracked | Out-String))
}

function Get-RelativeRepoPath {
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$FullPath
    )

    $repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
    $fullTargetPath = [System.IO.Path]::GetFullPath($FullPath)

    if (-not $repoRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $repoRootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $repoUri = New-Object System.Uri($repoRootPath)
    $targetUri = New-Object System.Uri($fullTargetPath)
    $relativeUri = $repoUri.MakeRelativeUri($targetUri)
    $relativePath = [System.Uri]::UnescapeDataString($relativeUri.ToString())

    return $relativePath -replace '/', '\'
}

function Get-DirectorySizeBytes {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return 0L
    }

    $sum = 0L
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue) {
        $sum += $file.Length
    }

    return [int64]$sum
}

function Format-Bytes {
    param(
        [Parameter(Mandatory)]
        [long]$Bytes
    )

    if ($Bytes -ge 1GB) {
        return '{0:N2} GB' -f ($Bytes / 1GB)
    }

    if ($Bytes -ge 1MB) {
        return '{0:N2} MB' -f ($Bytes / 1MB)
    }

    if ($Bytes -ge 1KB) {
        return '{0:N2} KB' -f ($Bytes / 1KB)
    }

    return "$Bytes B"
}

function New-Candidate {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Reason
    )

    [PSCustomObject]@{
        Path   = $Path
        Reason = $Reason
    }
}

$repoRoot = Get-RepoRoot
Set-Location -LiteralPath $repoRoot

$candidateDirectories = New-Object System.Collections.Generic.List[object]

Get-ChildItem -LiteralPath $repoRoot -Recurse -Directory -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @('bin', 'obj', 'TestResults', 'BenchmarkDotNet.Artifacts') } |
    ForEach-Object {
        $candidateDirectories.Add((New-Candidate -Path $_.FullName -Reason "Generated .NET build/test output"))
    }

if ($IncludeTemp) {
    foreach ($name in @('temp')) {
        $fullPath = Join-Path $repoRoot $name
        if (Test-Path -LiteralPath $fullPath -PathType Container) {
            $candidateDirectories.Add((New-Candidate -Path $fullPath -Reason "Temporary workspace output"))
        }
    }
}

if ($IncludeLogs) {
    foreach ($name in @('logs', 'diagnostic-logs')) {
        $fullPath = Join-Path $repoRoot $name
        if (Test-Path -LiteralPath $fullPath -PathType Container) {
            $candidateDirectories.Add((New-Candidate -Path $fullPath -Reason "Generated log files"))
        }
    }
}

if ($IncludeVisualStudio) {
    $vsPath = Join-Path $repoRoot '.vs'
    if (Test-Path -LiteralPath $vsPath -PathType Container) {
        $candidateDirectories.Add((New-Candidate -Path $vsPath -Reason "Visual Studio local cache"))
    }
}

if ($IncludeNodeModules) {
    $nodeModulesPath = Join-Path $repoRoot 'node_modules'
    if (Test-Path -LiteralPath $nodeModulesPath -PathType Container) {
        $candidateDirectories.Add((New-Candidate -Path $nodeModulesPath -Reason "Restorable Node.js dependencies"))
    }
}

$seen = @{}
$removable = New-Object System.Collections.Generic.List[object]
$skipped = New-Object System.Collections.Generic.List[object]

foreach ($candidate in $candidateDirectories | Sort-Object Path -Unique) {
    if ($seen.ContainsKey($candidate.Path)) {
        continue
    }

    $seen[$candidate.Path] = $true

    if (-not (Test-Path -LiteralPath $candidate.Path -PathType Container)) {
        continue
    }

    if (Test-TrackedContent -RepoRoot $repoRoot -FullPath $candidate.Path) {
        $skipped.Add([PSCustomObject]@{
                Path   = $candidate.Path
                Reason = "Contains tracked content at the directory root"
            })
        continue
    }

    $relativeCandidatePath = (Get-RelativeRepoPath -RepoRoot $repoRoot -FullPath $candidate.Path) -replace '\\', '/'
    $trackedChildren = (& git -C $repoRoot ls-files -- ("{0}/" -f $relativeCandidatePath) 2>$null)
    if (-not [string]::IsNullOrWhiteSpace(($trackedChildren | Out-String))) {
        $skipped.Add([PSCustomObject]@{
                Path   = $candidate.Path
                Reason = "Contains tracked files"
            })
        continue
    }

    $sizeBytes = Get-DirectorySizeBytes -Path $candidate.Path
    $removable.Add([PSCustomObject]@{
            Path      = $candidate.Path
            Reason    = $candidate.Reason
            SizeBytes = $sizeBytes
            Size      = Format-Bytes -Bytes $sizeBytes
        })
}

$totalBytes = ($removable | Measure-Object -Property SizeBytes -Sum).Sum
if ($null -eq $totalBytes) {
    $totalBytes = 0L
}

Write-Host ""
Write-Host "Cleanup mode: $([string]::Join('', @($(if ($Execute) { 'EXECUTE' } else { 'PREVIEW' }))))"
Write-Host "Repository: $repoRoot"
Write-Host ""

if ($removable.Count -eq 0) {
    Write-Host "No removable generated directories were found."
}
else {
    $removable |
        Sort-Object SizeBytes -Descending |
        Select-Object Size, Reason, Path |
        Format-Table -Wrap -AutoSize

    Write-Host ""
    Write-Host ("Estimated space to recover: {0}" -f (Format-Bytes -Bytes $totalBytes))
}

if ($skipped.Count -gt 0) {
    Write-Host ""
    Write-Host "Skipped to protect tracked content:"
    $skipped | Select-Object Reason, Path | Format-Table -Wrap -AutoSize
}

if (-not $Execute) {
    Write-Host ""
    Write-Host "Preview only. Re-run with -Execute to delete the directories listed above."
    Write-Host "Optional switches: -IncludeTemp -IncludeLogs -IncludeVisualStudio -IncludeNodeModules"
    exit 0
}

if ($removable.Count -eq 0) {
    Write-Host ""
    Write-Host "Nothing to delete."
    exit 0
}

Write-Host ""
Write-Host "Deleting generated directories..."

foreach ($entry in $removable) {
    Remove-Item -LiteralPath $entry.Path -Recurse -Force
    Write-Host ("Deleted {0}" -f $entry.Path)
}

Write-Host ""
Write-Host ("Recovered approximately {0}" -f (Format-Bytes -Bytes $totalBytes))
