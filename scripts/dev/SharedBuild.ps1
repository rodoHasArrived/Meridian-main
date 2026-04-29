Set-StrictMode -Version Latest

$script:MeridianSharedBuildScriptRoot = $PSScriptRoot
$script:MeridianBuildArtifactRetentionApplied = $false
$script:MeridianWorkflowArtifactRetentionRoots = @{}

function ConvertTo-MeridianBuildSlug {
    param([Parameter(Mandatory = $true)][string]$Value)

    $slug = $Value.ToLowerInvariant() -replace '[^a-z0-9]+', '-'
    $slug = $slug.Trim('-')
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return 'build'
    }

    return $slug
}

function Get-MeridianSharedRepoRoot {
    if ([string]::IsNullOrWhiteSpace($script:MeridianSharedBuildScriptRoot)) {
        return $null
    }

    return [System.IO.Path]::GetFullPath((Join-Path $script:MeridianSharedBuildScriptRoot '../..'))
}

function Format-MeridianBuildBytes {
    param([Parameter(Mandatory = $true)][long]$Bytes)

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

function Get-MeridianBuildArtifactMaxRootSizeMB {
    $raw = $env:MERIDIAN_BUILD_ARTIFACT_MAX_ROOT_SIZE_MB
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return 4096
    }

    try {
        return [int]$raw
    }
    catch {
        return 4096
    }
}

function Get-MeridianDirectorySizeBytes {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sum = 0L
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue) {
        $sum += $file.Length
    }

    return [int64]$sum
}

function Invoke-MeridianBuildArtifactRetention {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [int]$MaxAgeDays = 14,
        [int]$RetainLatest = 10,
        [int]$MaxRootSizeMB = (Get-MeridianBuildArtifactMaxRootSizeMB)
    )

    if ($script:MeridianBuildArtifactRetentionApplied -or ($MaxAgeDays -le 0 -and $RetainLatest -le 0 -and $MaxRootSizeMB -le 0)) {
        return
    }

    $script:MeridianBuildArtifactRetentionApplied = $true
    $cutoffUtc = (Get-Date).ToUniversalTime().AddDays(-$MaxAgeDays)
    $maxRootBytes = if ($MaxRootSizeMB -gt 0) { [int64]$MaxRootSizeMB * 1024 * 1024 } else { 0L }
    $artifactRoots = @(
        (Join-Path $RepoRoot 'artifacts/bin')
        (Join-Path $RepoRoot 'artifacts/obj')
    )

    $deletedCount = 0
    $freedBytes = 0L

    foreach ($artifactRoot in $artifactRoots) {
        if (-not (Test-Path -LiteralPath $artifactRoot -PathType Container)) {
            continue
        }

        $resolvedRoot = [System.IO.Path]::GetFullPath($artifactRoot)
        $resolvedRootWithSeparator = $resolvedRoot
        if (-not $resolvedRootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
            $resolvedRootWithSeparator += [System.IO.Path]::DirectorySeparatorChar
        }

        $artifactDirectories = @(
            Get-ChildItem -LiteralPath $resolvedRoot -Directory -Force -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTimeUtc -Descending
        )
        if ($artifactDirectories.Count -eq 0) {
            continue
        }

        $candidateEntries = New-Object System.Collections.Generic.List[object]
        foreach ($directory in $artifactDirectories) {
            $candidatePath = [System.IO.Path]::GetFullPath($directory.FullName)
            if (-not $candidatePath.StartsWith($resolvedRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Warning "Skipping build artifact retention candidate outside expected root: $candidatePath"
                continue
            }

            $candidateEntries.Add([PSCustomObject]@{
                    Path             = $candidatePath
                    Bytes            = Get-MeridianDirectorySizeBytes -Path $candidatePath
                    LastWriteTimeUtc = $directory.LastWriteTimeUtc
                })
        }

        if ($candidateEntries.Count -eq 0) {
            continue
        }

        $retainedDirectories = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        if ($RetainLatest -gt 0) {
            foreach ($entry in ($candidateEntries | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First $RetainLatest)) {
                [void]$retainedDirectories.Add($entry.Path)
            }
        }

        $deletePaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $candidateEntries) {
            $ageExpired = $MaxAgeDays -gt 0 -and $entry.LastWriteTimeUtc -lt $cutoffUtc
            $countExceeded = $RetainLatest -gt 0 -and -not $retainedDirectories.Contains($entry.Path)
            if ($ageExpired -or $countExceeded) {
                [void]$deletePaths.Add($entry.Path)
            }
        }

        if ($maxRootBytes -gt 0) {
            $projectedRootBytes = 0L
            foreach ($entry in $candidateEntries) {
                $projectedRootBytes += [int64]$entry.Bytes
            }

            foreach ($entry in $candidateEntries) {
                if ($deletePaths.Contains($entry.Path)) {
                    $projectedRootBytes -= [int64]$entry.Bytes
                }
            }

            foreach ($entry in ($candidateEntries | Sort-Object LastWriteTimeUtc)) {
                if ($projectedRootBytes -le $maxRootBytes) {
                    break
                }

                if ($deletePaths.Contains($entry.Path)) {
                    continue
                }

                [void]$deletePaths.Add($entry.Path)
                $projectedRootBytes -= [int64]$entry.Bytes
            }
        }

        foreach ($entry in $candidateEntries) {
            if (-not $deletePaths.Contains($entry.Path)) {
                continue
            }

            try {
                Remove-Item -LiteralPath $entry.Path -Recurse -Force -ErrorAction Stop
                $deletedCount++
                $freedBytes += [int64]$entry.Bytes
            }
            catch {
                Write-Warning "Failed to prune stale build artifact directory '$($entry.Path)': $($_.Exception.Message)"
            }
        }
    }

    if ($deletedCount -gt 0) {
        $policies = New-Object System.Collections.Generic.List[string]
        if ($MaxAgeDays -gt 0) {
            $policies.Add("older than $MaxAgeDays days")
        }

        if ($RetainLatest -gt 0) {
            $policies.Add("beyond latest $RetainLatest per root")
        }

        if ($MaxRootSizeMB -gt 0) {
            $policies.Add("above $MaxRootSizeMB MB per root")
        }

        Write-Host ("[INFO] Pruned {0} isolated build artifact director{1} using age/count/size retention ({2}) from artifacts/bin and artifacts/obj ({3} recovered)." -f `
                $deletedCount, `
                $(if ($deletedCount -eq 1) { 'y' } else { 'ies' }), `
                ([string]::Join(' or ', $policies)), `
                (Format-MeridianBuildBytes -Bytes $freedBytes))
    }
}

function Invoke-MeridianWorkflowArtifactRetention {
    param(
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [int]$MaxAgeDays = 14,
        [int]$RetainLatest = 10
    )

    if ($MaxAgeDays -le 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $OutputRoot -PathType Container)) {
        return
    }

    $resolvedRoot = [System.IO.Path]::GetFullPath($OutputRoot)
    if ($script:MeridianWorkflowArtifactRetentionRoots.ContainsKey($resolvedRoot)) {
        return
    }

    $script:MeridianWorkflowArtifactRetentionRoots[$resolvedRoot] = $true

    $resolvedRootWithSeparator = $resolvedRoot
    if (-not $resolvedRootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedRootWithSeparator += [System.IO.Path]::DirectorySeparatorChar
    }

    $runDirectories = @(
        Get-ChildItem -LiteralPath $resolvedRoot -Directory -Force -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending
    )

    if ($runDirectories.Count -eq 0) {
        return
    }

    $retainedDirectories = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    if ($RetainLatest -gt 0) {
        foreach ($directory in ($runDirectories | Select-Object -First $RetainLatest)) {
            [void]$retainedDirectories.Add([System.IO.Path]::GetFullPath($directory.FullName))
        }
    }

    $cutoffUtc = (Get-Date).ToUniversalTime().AddDays(-$MaxAgeDays)
    $deletedCount = 0
    $freedBytes = 0L

    foreach ($directory in $runDirectories) {
        if ($directory.LastWriteTimeUtc -ge $cutoffUtc) {
            continue
        }

        $candidatePath = [System.IO.Path]::GetFullPath($directory.FullName)
        if ($retainedDirectories.Contains($candidatePath)) {
            continue
        }

        if (-not $candidatePath.StartsWith($resolvedRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Warning "Skipping workflow artifact retention candidate outside expected root: $candidatePath"
            continue
        }

        try {
            $candidateBytes = Get-MeridianDirectorySizeBytes -Path $candidatePath
            Remove-Item -LiteralPath $candidatePath -Recurse -Force -ErrorAction Stop
            $deletedCount++
            $freedBytes += $candidateBytes
        }
        catch {
            Write-Warning "Failed to prune stale workflow artifact directory '$candidatePath': $($_.Exception.Message)"
        }
    }

    if ($deletedCount -gt 0) {
        Write-Host ("[INFO] Pruned {0} stale workflow artifact director{1} older than {2} days from {3}; retained latest {4} ({5} recovered)." -f `
                $deletedCount, `
                $(if ($deletedCount -eq 1) { 'y' } else { 'ies' }), `
                $MaxAgeDays, `
                $resolvedRoot, `
                $RetainLatest, `
                (Format-MeridianBuildBytes -Bytes $freedBytes))
    }
}

function New-MeridianBuildIsolationKey {
    param([string]$Prefix = 'automation')

    $repoRoot = Get-MeridianSharedRepoRoot
    if (-not [string]::IsNullOrWhiteSpace($repoRoot)) {
        Invoke-MeridianBuildArtifactRetention -RepoRoot $repoRoot
    }

    $slug = ConvertTo-MeridianBuildSlug -Value $Prefix
    $timestamp = Get-Date -Format 'yyyyMMddHHmmss'
    return "$slug-$PID-$timestamp"
}

function Get-MeridianProjectOutputRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [string]$IsolationKey
    )

    if ([string]::IsNullOrWhiteSpace($IsolationKey)) {
        return $null
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot "artifacts/bin/$IsolationKey/$projectName"))
}

function Get-MeridianProjectBinaryPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][string]$Framework,
        [Parameter(Mandatory = $true)][string]$BinaryName,
        [string]$IsolationKey
    )

    if ([string]::IsNullOrWhiteSpace($IsolationKey)) {
        $projectDirectory = Split-Path -Parent $ProjectPath
        $projectOutputDirectory = if ([System.IO.Path]::IsPathRooted($projectDirectory)) {
            Join-Path $projectDirectory "bin/$Configuration/$Framework"
        }
        else {
            Join-Path $RepoRoot (Join-Path $projectDirectory "bin/$Configuration/$Framework")
        }

        return [System.IO.Path]::GetFullPath((Join-Path $projectOutputDirectory $BinaryName))
    }

    $outputRoot = Get-MeridianProjectOutputRoot -RepoRoot $RepoRoot -ProjectPath $ProjectPath -IsolationKey $IsolationKey
    return [System.IO.Path]::GetFullPath((Join-Path $outputRoot "$Configuration/$Framework/$BinaryName"))
}

function Get-MeridianBuildArguments {
    param(
        [string]$IsolationKey,
        [string]$TargetFramework,
        [string[]]$AdditionalProperties = @(),
        [switch]$EnableFullWpfBuild
    )

    $args = @(
        '/p:EnableWindowsTargeting=true',
        '-maxcpucount:1',
        '/nr:false'
    )

    if (-not [string]::IsNullOrWhiteSpace($IsolationKey)) {
        $args += "/p:MeridianBuildIsolationKey=$IsolationKey"
    }

    if (-not [string]::IsNullOrWhiteSpace($TargetFramework)) {
        $args += "/p:TargetFramework=$TargetFramework"
    }

    if ($EnableFullWpfBuild) {
        $args += '/p:EnableFullWpfBuild=true'
    }

    foreach ($property in $AdditionalProperties) {
        if ([string]::IsNullOrWhiteSpace($property)) {
            continue
        }

        if ($property.StartsWith('/p:', [System.StringComparison]::OrdinalIgnoreCase) -or
            $property.StartsWith('-p:', [System.StringComparison]::OrdinalIgnoreCase)) {
            $args += $property
            continue
        }

        $args += "/p:$property"
    }

    return $args
}
