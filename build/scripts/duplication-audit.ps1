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
    [switch]$ShowIntentional = $false,
    [string[]]$SkipDirectories = @(".git", ".vs", ".idea", "artifacts/bin", "artifacts/obj", "artifacts/publish", "node_modules"),
    [string[]]$ScopeRoots = @("src", "tests")
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

function IsInScope {
    param([Parameter(Mandatory)] [string]$RelativePath, [string[]]$ScopeRoots)

    if ($ScopeRoots.Count -eq 0) {
        return $true;
    }

    foreach ($scope in $ScopeRoots) {
        if ([string]::IsNullOrWhiteSpace($scope)) {
            continue;
        }

        $scope = $scope -replace "[\\/]+$", "";
        if ($RelativePath -eq $scope -or $RelativePath.StartsWith("$scope/")) {
            return $true;
        }
    }

    return $false;
}

function Get-TrackedFiles {
    param([Parameter(Mandatory)] [string]$RepoRoot)

    $files = Get-ChildItem -Path $RepoRoot -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { -not (IsPathSkipped -Candidate $_.FullName) }

    $tracked = git -C $RepoRoot ls-files --cached --others --exclude-standard
    $trackedSet = @{}
    foreach ($entry in $tracked) {
        $trackedSet[$entry] = $true
    }

    return $files | Where-Object {
        $relative = Get-RelativePath -RepoRoot $RepoRoot -Path $_.FullName
        $trackedSet.ContainsKey($relative) -and (IsInScope -RelativePath $relative -ScopeRoots $ScopeRoots)
    }
}

function Get-DuplicateNameCategory {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string[]]$RelativePaths
    )

    $pathText = ($RelativePaths -join "|")

    if ($Name -in @("README.md", "GlobalUsings.cs", "DesignModule.cs", "Program.cs", "Interop.fs", "index.ts")) {
        return "intentional-convention"
    }

    if ($Name.EndsWith("Tests.cs", [StringComparison]::OrdinalIgnoreCase)) {
        return "test-parity"
    }

    if ($pathText -match "src/Meridian\.Wpf/" -and
        ($pathText -match "src/Meridian\.Ui\.Services/" -or $pathText -match "src/Meridian\.Ui\.Shared/" -or $pathText -match "src/Meridian\.Ui/dashboard/")) {
        return "surface-adapter"
    }

    if ($pathText -match "src/Meridian\.Application/" -and $pathText -match "src/Meridian\.Contracts/") {
        return "contract-migration"
    }

    if ($pathText -match "src/Meridian\.Domain/" -and $pathText -match "src/Meridian\.Contracts/") {
        return "contract-migration"
    }

    if ($pathText -match "src/Meridian\.Application/" -and $pathText -match "src/Meridian\.Ui\.Shared/") {
        return "shared-wrapper"
    }

    if ($pathText -match "src/Meridian\.Application/" -and $pathText -match "src/Meridian\.Ui\.Services/") {
        return "shared-wrapper"
    }

    return "actionable-review"
}

function Get-DuplicateContentCategory {
    param([Parameter(Mandatory)] [string[]]$RelativePaths)

    $extensions = @($RelativePaths | ForEach-Object { [IO.Path]::GetExtension($_).ToLowerInvariant() } | Sort-Object -Unique)
    if ($extensions.Count -eq 1 -and $extensions[0] -in @(".ico", ".svg", ".png", ".jpg", ".jpeg")) {
        return "exact-asset-copy"
    }

    return "actionable-content"
}

function Write-DuplicateNameGroup {
    param([Parameter(Mandatory)] $Group)

    Write-Host (" - [{0}] {1} ({2})" -f $Group.Category, $Group.Name, $Group.Count)
    foreach ($path in $Group.Paths) {
        Write-Host ("   {0}" -f $path)
    }
}

$repoRoot = Get-RepoRoot
$trackedFiles = Get-TrackedFiles -RepoRoot $repoRoot

Write-Host "Scanning $($trackedFiles.Count) repository files under $repoRoot"

$fileNames = $trackedFiles | Group-Object -Property Name | Where-Object Count -gt 1 | ForEach-Object {
    $paths = @($_.Group | ForEach-Object { Get-RelativePath -RepoRoot $repoRoot -Path $_.FullName } | Sort-Object)
    [pscustomobject]@{
        Name = $_.Name
        Count = $_.Count
        Paths = $paths
        Category = Get-DuplicateNameCategory -Name $_.Name -RelativePaths $paths
    }
}

Write-Host ""
Write-Host "Duplicate file names"
if ($fileNames) {
    $actionable = @($fileNames | Where-Object { $_.Category -notin @("intentional-convention", "test-parity") })
    $intentional = @($fileNames | Where-Object { $_.Category -in @("intentional-convention", "test-parity") })

    if ($actionable.Count -gt 0) {
        foreach ($group in ($actionable | Sort-Object Category, Name)) {
            Write-DuplicateNameGroup -Group $group
        }
    }
    else {
        Write-Host "No actionable duplicate leaf names detected."
    }

    if ($intentional.Count -gt 0) {
        Write-Host ""
        if ($ShowIntentional) {
            Write-Host "Intentional or parity duplicate names"
            foreach ($group in ($intentional | Sort-Object Category, Name)) {
                Write-DuplicateNameGroup -Group $group
            }
        }
        else {
            Write-Host ("Suppressed {0} intentional or parity duplicate name groups. Use -ShowIntentional to list them." -f $intentional.Count)
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
        $paths = @($entry.Value | ForEach-Object { Get-RelativePath -RepoRoot $repoRoot -Path $_.FullName } | Sort-Object)
        $category = Get-DuplicateContentCategory -RelativePaths $paths
        Write-Host (" - [{0}] SHA: {1}" -f $category, $entry.Key)
        foreach ($file in $entry.Value) {
            Write-Host ("   {0}" -f (Get-RelativePath -RepoRoot $repoRoot -Path $file.FullName))
        }
    }
}
else {
    Write-Host "No exact duplicate content detected."
}
