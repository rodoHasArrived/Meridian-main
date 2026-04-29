Set-StrictMode -Version Latest

function Format-MeridianArtifactBytes {
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

function Get-MeridianArtifactDirectorySizeBytes {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sum = 0L
    foreach ($file in Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue) {
        $sum += $file.Length
    }

    return [int64]$sum
}

function Test-MeridianPathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [Parameter(Mandatory = $true)][string]$RootPath
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($RootPath)
    $resolvedCandidate = [System.IO.Path]::GetFullPath($CandidatePath)
    $resolvedRootWithSeparator = $resolvedRoot
    if (-not $resolvedRootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedRootWithSeparator += [System.IO.Path]::DirectorySeparatorChar
    }

    return $resolvedCandidate.StartsWith($resolvedRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-MeridianArtifactIsActivePath {
    param(
        [Parameter(Mandatory = $true)][string]$CandidatePath,
        [string]$ActivePath
    )

    if ([string]::IsNullOrWhiteSpace($ActivePath)) {
        return $false
    }

    $resolvedCandidate = [System.IO.Path]::GetFullPath($CandidatePath)
    $resolvedActive = [System.IO.Path]::GetFullPath($ActivePath)
    if ($resolvedActive.Equals($resolvedCandidate, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $candidateWithSeparator = $resolvedCandidate
    if (-not $candidateWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $candidateWithSeparator += [System.IO.Path]::DirectorySeparatorChar
    }

    return $resolvedActive.StartsWith($candidateWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)
}

function Invoke-MeridianArtifactDirectoryRetention {
    param(
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [string]$ActivePath,
        [int]$MaxAgeDays = 14,
        [int]$RetainLatest = 10,
        [string]$Label = 'artifact'
    )

    if ($MaxAgeDays -lt 0) {
        throw 'MaxAgeDays must be greater than or equal to 0.'
    }

    if ($RetainLatest -lt 0) {
        throw 'RetainLatest must be greater than or equal to 0.'
    }

    if ($MaxAgeDays -le 0 -and $RetainLatest -le 0) {
        return
    }

    if (-not (Test-Path -LiteralPath $OutputRoot -PathType Container)) {
        return
    }

    $resolvedRoot = [System.IO.Path]::GetFullPath($OutputRoot)
    $artifactDirectories = @(
        Get-ChildItem -LiteralPath $resolvedRoot -Directory -Force -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending
    )

    if ($artifactDirectories.Count -eq 0) {
        return
    }

    $retainedDirectories = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    if ($RetainLatest -gt 0) {
        foreach ($directory in ($artifactDirectories | Select-Object -First $RetainLatest)) {
            [void]$retainedDirectories.Add([System.IO.Path]::GetFullPath($directory.FullName))
        }
    }

    $cutoffUtc = if ($MaxAgeDays -gt 0) { (Get-Date).ToUniversalTime().AddDays(-$MaxAgeDays) } else { $null }
    $deletedCount = 0
    $freedBytes = 0L

    foreach ($directory in $artifactDirectories) {
        $candidatePath = [System.IO.Path]::GetFullPath($directory.FullName)
        if (-not (Test-MeridianPathWithinRoot -CandidatePath $candidatePath -RootPath $resolvedRoot)) {
            Write-Warning "Skipping $Label retention candidate outside expected root: $candidatePath"
            continue
        }

        if (Test-MeridianArtifactIsActivePath -CandidatePath $candidatePath -ActivePath $ActivePath) {
            continue
        }

        $ageExpired = $MaxAgeDays -gt 0 -and $directory.LastWriteTimeUtc -lt $cutoffUtc
        $countExceeded = $RetainLatest -gt 0 -and -not $retainedDirectories.Contains($candidatePath)
        if (-not $ageExpired -and -not $countExceeded) {
            continue
        }

        try {
            $candidateBytes = Get-MeridianArtifactDirectorySizeBytes -Path $candidatePath
            Remove-Item -LiteralPath $candidatePath -Recurse -Force -ErrorAction Stop
            $deletedCount++
            $freedBytes += $candidateBytes
        }
        catch {
            Write-Warning "Failed to prune stale $Label directory '$candidatePath': $($_.Exception.Message)"
        }
    }

    if ($deletedCount -gt 0) {
        $policies = New-Object System.Collections.Generic.List[string]
        if ($MaxAgeDays -gt 0) {
            $policies.Add("older than $MaxAgeDays days")
        }

        if ($RetainLatest -gt 0) {
            $policies.Add("beyond latest $RetainLatest")
        }

        Write-Host ("[INFO] Pruned {0} {1} director{2} using {3} retention from {4} ({5} recovered)." -f `
                $deletedCount, `
                $Label, `
                $(if ($deletedCount -eq 1) { 'y' } else { 'ies' }), `
                ([string]::Join(' or ', $policies)), `
                $resolvedRoot, `
                (Format-MeridianArtifactBytes -Bytes $freedBytes))
    }
}

Export-ModuleMember -Function Invoke-MeridianArtifactDirectoryRetention
