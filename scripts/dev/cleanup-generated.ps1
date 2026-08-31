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

function Add-GeneratedDirectory {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Candidates,

        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [string]$Reason
    )

    $fullPath = Join-Path $RepoRoot $RelativePath
    if (Test-Path -LiteralPath $fullPath -PathType Container) {
        $Candidates.Add((New-Candidate -Path $fullPath -Reason $Reason))
    }
}

function Add-GeneratedFiles {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Candidates,

        [Parameter(Mandatory)]
        [string]$RootPath,

        [Parameter(Mandatory)]
        [string[]]$Patterns,

        [Parameter(Mandatory)]
        [string]$Reason,

        [switch]$Recurse
    )

    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) {
        return
    }

    foreach ($pattern in $Patterns) {
        Get-ChildItem -LiteralPath $RootPath -Filter $pattern -File -Force -Recurse:$Recurse -ErrorAction SilentlyContinue |
            ForEach-Object {
                $Candidates.Add((New-Candidate -Path $_.FullName -Reason $Reason))
            }
    }
}

function Add-GeneratedTempChildren {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$DirectoryCandidates,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$FileCandidates,

        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $tmpRoot = Join-Path $RepoRoot '.tmp'
    if (-not (Test-Path -LiteralPath $tmpRoot -PathType Container)) {
        return
    }

    $directoryPatterns = @(
        'codex-*',
        'dashboard-dev',
        'gh-run-*',
        'gitleaks-*',
        'localappdata',
        'logs',
        'MSBuildTemp*',
        'tools'
    )

    foreach ($directory in Get-ChildItem -LiteralPath $tmpRoot -Directory -Force -ErrorAction SilentlyContinue) {
        foreach ($pattern in $directoryPatterns) {
            if ($directory.Name -like $pattern) {
                $DirectoryCandidates.Add((New-Candidate -Path $directory.FullName -Reason "Temporary workspace output"))
                break
            }
        }
    }

    Add-GeneratedFiles `
        -Candidates $FileCandidates `
        -RootPath $tmpRoot `
        -Patterns @('*.log', '*.tmp', '*.pid', '*.exitcode') `
        -Reason "Temporary workspace output"
}

function Add-GeneratedArtifactChildren {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Candidates,

        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$RelativePath,

        [Parameter(Mandatory)]
        [string]$Reason
    )

    $artifactRoot = Join-Path $RepoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
        return
    }

    Get-ChildItem -LiteralPath $artifactRoot -Directory -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            $Candidates.Add((New-Candidate -Path $_.FullName -Reason $Reason))
        }
}

function Test-GeneratedArtifactContainer {
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [string]$FullPath
    )

    $relativePath = (Get-RelativeRepoPath -RepoRoot $RepoRoot -FullPath $FullPath) -replace '\\', '/'
    return $relativePath -in @('artifacts/bin', 'artifacts/obj', 'artifacts/publish')
}

function Test-SkipGeneratedOutputTraversal {
    param(
        [Parameter(Mandatory)]
        [string]$RepoRoot,

        [Parameter(Mandatory)]
        [System.IO.DirectoryInfo]$Directory
    )

    if ($Directory.Name -in @('.git', '.vs', '.idea', 'node_modules')) {
        return $true
    }

    $relativePath = (Get-RelativeRepoPath -RepoRoot $RepoRoot -FullPath $Directory.FullName) -replace '\\', '/'
    $firstSegment = ($relativePath -split '/')[0]
    if ($firstSegment -in @('.agents', '.claude', '.codex', '.tmp', 'archive', 'artifacts', 'data', 'docs', 'Meridian Design System', 'output', 'plugins', 'wwwroot')) {
        return $true
    }

    return Test-GeneratedArtifactContainer -RepoRoot $RepoRoot -FullPath $Directory.FullName
}

function Add-GeneratedBuildOutputDirectories {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Candidates,

        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $generatedDirectoryNames = @('bin', 'obj', 'TestResults', 'BenchmarkDotNet.Artifacts')
    $pendingDirectories = New-Object 'System.Collections.Generic.Stack[string]'
    $pendingDirectories.Push($RepoRoot)

    while ($pendingDirectories.Count -gt 0) {
        $currentDirectory = $pendingDirectories.Pop()
        foreach ($directory in Get-ChildItem -LiteralPath $currentDirectory -Directory -Force -ErrorAction SilentlyContinue) {
            if (Test-SkipGeneratedOutputTraversal -RepoRoot $RepoRoot -Directory $directory) {
                continue
            }

            if ($directory.Name -in $generatedDirectoryNames) {
                $Candidates.Add((New-Candidate -Path $directory.FullName -Reason "Generated .NET build/test output"))
                continue
            }

            $pendingDirectories.Push($directory.FullName)
        }
    }
}

function Add-NodeModuleDirectories {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Candidates,

        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $pendingDirectories = New-Object 'System.Collections.Generic.Stack[string]'
    $pendingDirectories.Push($RepoRoot)

    while ($pendingDirectories.Count -gt 0) {
        $currentDirectory = $pendingDirectories.Pop()
        foreach ($directory in Get-ChildItem -LiteralPath $currentDirectory -Directory -Force -ErrorAction SilentlyContinue) {
            if ($directory.Name -in @('.git', '.vs', '.idea')) {
                continue
            }

            $relativePath = (Get-RelativeRepoPath -RepoRoot $RepoRoot -FullPath $directory.FullName) -replace '\\', '/'
            $firstSegment = ($relativePath -split '/')[0]
            if ($firstSegment -in @('.agents', '.claude', '.codex', '.tmp', 'archive', 'artifacts', 'data', 'docs', 'Meridian Design System', 'output', 'plugins', 'wwwroot')) {
                continue
            }

            if ($directory.Name -eq 'node_modules') {
                $Candidates.Add((New-Candidate -Path $directory.FullName -Reason "Restorable Node.js dependencies"))
                continue
            }

            if (Test-GeneratedArtifactContainer -RepoRoot $RepoRoot -FullPath $directory.FullName) {
                continue
            }

            $pendingDirectories.Push($directory.FullName)
        }
    }
}

function Add-WpfTempProjectFiles {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[object]]$Candidates,

        [Parameter(Mandatory)]
        [string]$RepoRoot
    )

    $wpfDirectory = Join-Path $RepoRoot 'src/Meridian.Wpf'
    if (-not (Test-Path -LiteralPath $wpfDirectory -PathType Container)) {
        return
    }

    Get-ChildItem -LiteralPath $wpfDirectory -Filter '*_wpftmp.csproj' -File -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            $Candidates.Add((New-Candidate -Path $_.FullName -Reason "Stale WPF temporary project file"))
        }
}

$repoRoot = Get-RepoRoot
Set-Location -LiteralPath $repoRoot

$candidateDirectories = New-Object System.Collections.Generic.List[object]
$candidateFiles = New-Object System.Collections.Generic.List[object]

Add-GeneratedBuildOutputDirectories -Candidates $candidateDirectories -RepoRoot $repoRoot
Add-WpfTempProjectFiles -Candidates $candidateFiles -RepoRoot $repoRoot

Add-GeneratedArtifactChildren `
    -Candidates $candidateDirectories `
    -RepoRoot $repoRoot `
    -RelativePath 'artifacts/bin' `
    -Reason "Isolated MSBuild output"

Add-GeneratedArtifactChildren `
    -Candidates $candidateDirectories `
    -RepoRoot $repoRoot `
    -RelativePath 'artifacts/obj' `
    -Reason "Isolated MSBuild intermediate output"

Add-GeneratedArtifactChildren `
    -Candidates $candidateDirectories `
    -RepoRoot $repoRoot `
    -RelativePath 'artifacts/publish' `
    -Reason "Generated publish output"

if ($IncludeTemp) {
    Add-GeneratedDirectory -Candidates $candidateDirectories -RepoRoot $repoRoot -RelativePath '.buildtmp' -Reason "Temporary workspace output"
    Add-GeneratedDirectory -Candidates $candidateDirectories -RepoRoot $repoRoot -RelativePath 'temp' -Reason "Temporary workspace output"
    Add-GeneratedDirectory -Candidates $candidateDirectories -RepoRoot $repoRoot -RelativePath 'output' -Reason "Temporary workspace output"
    Add-GeneratedDirectory -Candidates $candidateDirectories -RepoRoot $repoRoot -RelativePath 'src/Meridian.Ui/dashboard/.tmp' -Reason "Dashboard temporary output"
    Add-GeneratedTempChildren -DirectoryCandidates $candidateDirectories -FileCandidates $candidateFiles -RepoRoot $repoRoot
}

if ($IncludeLogs) {
    foreach ($name in @('logs', 'diagnostic-logs')) {
        $fullPath = Join-Path $repoRoot $name
        if (Test-Path -LiteralPath $fullPath -PathType Container) {
            $candidateDirectories.Add((New-Candidate -Path $fullPath -Reason "Generated log files"))
        }
    }

    Add-GeneratedFiles `
        -Candidates $candidateFiles `
        -RootPath (Join-Path $repoRoot 'artifacts') `
        -Patterns @('*.log', '*.tmp', '*.pid', '*.exitcode', '*_stdout.txt', '*_stderr.txt') `
        -Reason "Generated artifact log or process sidecar"
}

if ($IncludeVisualStudio) {
    $vsPath = Join-Path $repoRoot '.vs'
    if (Test-Path -LiteralPath $vsPath -PathType Container) {
        $candidateDirectories.Add((New-Candidate -Path $vsPath -Reason "Visual Studio local cache"))
    }
}

if ($IncludeNodeModules) {
    Add-NodeModuleDirectories -Candidates $candidateDirectories -RepoRoot $repoRoot
}

$seen = @{}
$removable = New-Object System.Collections.Generic.List[object]
$removableFiles = New-Object System.Collections.Generic.List[object]
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

foreach ($candidate in $candidateFiles | Sort-Object Path -Unique) {
    if ($seen.ContainsKey($candidate.Path)) {
        continue
    }

    $seen[$candidate.Path] = $true

    if (-not (Test-Path -LiteralPath $candidate.Path -PathType Leaf)) {
        continue
    }

    if (Test-TrackedContent -RepoRoot $repoRoot -FullPath $candidate.Path) {
        $skipped.Add([PSCustomObject]@{
                Path   = $candidate.Path
                Reason = "File is tracked content"
            })
        continue
    }

    $file = Get-Item -LiteralPath $candidate.Path -Force
    $removableFiles.Add([PSCustomObject]@{
            Path      = $candidate.Path
            Reason    = $candidate.Reason
            SizeBytes = [int64]$file.Length
            Size      = Format-Bytes -Bytes ([int64]$file.Length)
        })
}

$totalBytes = 0L
foreach ($entry in $removable) {
    $totalBytes += [int64]$entry.SizeBytes
}
foreach ($entry in $removableFiles) {
    $totalBytes += [int64]$entry.SizeBytes
}

Write-Host ""
Write-Host "Cleanup mode: $([string]::Join('', @($(if ($Execute) { 'EXECUTE' } else { 'PREVIEW' }))))"
Write-Host "Repository: $repoRoot"
Write-Host ""

if ($removable.Count -eq 0 -and $removableFiles.Count -eq 0) {
    Write-Host "No removable generated directories or files were found."
}
else {
    if ($removable.Count -gt 0) {
        Write-Host "Generated directories:"
        $removable |
            Sort-Object SizeBytes -Descending |
            Select-Object Size, Reason, Path |
            Format-Table -Wrap -AutoSize
    }

    if ($removableFiles.Count -gt 0) {
        Write-Host "Generated files:"
        $removableFiles |
            Sort-Object SizeBytes -Descending |
            Select-Object Size, Reason, Path |
            Format-Table -Wrap -AutoSize
    }

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

if ($removable.Count -eq 0 -and $removableFiles.Count -eq 0) {
    Write-Host ""
    Write-Host "Nothing to delete."
    exit 0
}

Write-Host ""
Write-Host "Deleting generated artifacts..."

foreach ($entry in $removable) {
    if (-not (Test-Path -LiteralPath $entry.Path -PathType Container)) {
        Write-Host ("Skipped missing {0}" -f $entry.Path)
        continue
    }

    Remove-Item -LiteralPath $entry.Path -Recurse -Force
    Write-Host ("Deleted {0}" -f $entry.Path)
}

foreach ($entry in $removableFiles) {
    if (-not (Test-Path -LiteralPath $entry.Path -PathType Leaf)) {
        Write-Host ("Skipped missing {0}" -f $entry.Path)
        continue
    }

    Remove-Item -LiteralPath $entry.Path -Force
    Write-Host ("Deleted {0}" -f $entry.Path)
}

Write-Host ""
Write-Host ("Recovered approximately {0}" -f (Format-Bytes -Bytes $totalBytes))
