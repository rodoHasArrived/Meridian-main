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

function Test-MeridianPathIsReparsePoint {
    param([Parameter(Mandatory = $true)][string]$Path)

    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $false
    }

    return (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Test-MeridianPathHasReparsePointAncestor {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$StopAt = ''
    )

    $current = [System.IO.Path]::GetFullPath($Path)
    $stopAtFullPath = if ([string]::IsNullOrWhiteSpace($StopAt)) { '' } else { [System.IO.Path]::GetFullPath($StopAt) }

    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (-not [string]::IsNullOrWhiteSpace($stopAtFullPath) -and [string]::Equals($current, $stopAtFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }

        if ((Test-Path -LiteralPath $current) -and (Test-MeridianPathIsReparsePoint -Path $current)) {
            return $true
        }

        $parent = [System.IO.Directory]::GetParent($current)
        if ($null -eq $parent) {
            return $false
        }

        $current = $parent.FullName
    }

    return $false
}

function Test-MeridianWorkflowRunArtifactDirectory {
    param([Parameter(Mandatory = $true)][string]$Name)

    return $Name -match '^\d{8}-\d{6}($|-)'
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

        if (Test-MeridianPathHasReparsePointAncestor -Path $artifactRoot -StopAt $RepoRoot) {
            Write-Warning "Skipping build artifact retention root because it crosses a reparse point: $artifactRoot"
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
            if (Test-MeridianPathHasReparsePointAncestor -Path $candidatePath -StopAt $resolvedRoot) {
                Write-Warning "Skipping build artifact retention candidate because it crosses a reparse point: $candidatePath"
                continue
            }

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
                if (Test-MeridianPathHasReparsePointAncestor -Path $entry.Path -StopAt $resolvedRoot) {
                    Write-Warning "Skipping build artifact retention delete because candidate crosses a reparse point: $($entry.Path)"
                    continue
                }

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

    if (Test-MeridianPathHasReparsePointAncestor -Path $OutputRoot) {
        Write-Warning "Skipping workflow artifact retention root because it crosses a reparse point: $OutputRoot"
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
            Where-Object { Test-MeridianWorkflowRunArtifactDirectory -Name $_.Name } |
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

    $cutoffUtc = if ($MaxAgeDays -gt 0) { (Get-Date).ToUniversalTime().AddDays(-$MaxAgeDays) } else { $null }
    $deletedCount = 0
    $freedBytes = 0L

    foreach ($directory in $runDirectories) {
        $candidatePath = [System.IO.Path]::GetFullPath($directory.FullName)
        if (Test-MeridianPathHasReparsePointAncestor -Path $candidatePath -StopAt $resolvedRoot) {
            Write-Warning "Skipping workflow artifact retention candidate because it crosses a reparse point: $candidatePath"
            continue
        }

        if (-not $candidatePath.StartsWith($resolvedRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Warning "Skipping workflow artifact retention candidate outside expected root: $candidatePath"
            continue
        }

        $ageExpired = $MaxAgeDays -gt 0 -and $directory.LastWriteTimeUtc -lt $cutoffUtc
        $countExceeded = $RetainLatest -gt 0 -and -not $retainedDirectories.Contains($candidatePath)
        if (-not $ageExpired -and -not $countExceeded) {
            continue
        }

        try {
            if (Test-MeridianPathHasReparsePointAncestor -Path $candidatePath -StopAt $resolvedRoot) {
                Write-Warning "Skipping workflow artifact retention delete because candidate crosses a reparse point: $candidatePath"
                continue
            }

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
        $policies = New-Object System.Collections.Generic.List[string]
        if ($MaxAgeDays -gt 0) {
            $policies.Add("older than $MaxAgeDays days")
        }

        if ($RetainLatest -gt 0) {
            $policies.Add("beyond latest $RetainLatest")
        }

        Write-Host ("[INFO] Pruned {0} workflow artifact director{1} using age/count retention ({2}) from {3} ({4} recovered)." -f `
                $deletedCount, `
                $(if ($deletedCount -eq 1) { 'y' } else { 'ies' }), `
                ([string]::Join(' or ', $policies)), `
                $resolvedRoot, `
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
        [switch]$EnableFullWpfBuild,
        [int]$MaxCpuCount = 0
    )

    $args = @(
        '/p:EnableWindowsTargeting=true',
        '/nr:false'
    )

    if ($MaxCpuCount -gt 0) {
        $args += "-maxcpucount:$MaxCpuCount"
    }

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

function Format-MeridianCommandText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string[]]$Command
    )

    return ($Command | ForEach-Object {
            if ($_ -match '\\s') { '"{0}"' -f $_ } else { $_ }
        }) -join ' '
}

function Invoke-MeridianLoggedStep {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Command,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    Write-Host ">>> $Name" -ForegroundColor Cyan
    Write-Host ("    " + (Format-MeridianCommandText -Command $Command))

    $logDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($LogPath))
    if (-not [string]::IsNullOrWhiteSpace($logDirectory)) {
        New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    }

    $output = @()
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"

    try {
        & $Command[0] @($Command[1..($Command.Count - 1)]) 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { $output += $_ }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        $stopwatch.Stop()
    }

    $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
    return [ordered]@{
        name = $Name
        command = Format-MeridianCommandText -Command $Command
        exitCode = $exitCode
        durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2)
        logPath = $LogPath
        tail = ($output | Select-Object -Last 25) -join [Environment]::NewLine
    }
}

function Get-MeridianRepoOwnedTestHostProcesses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if (-not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
        return @()
    }

    $processes = @(Get-CimInstance Win32_Process -Filter "Name = 'testhost.exe'" -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        return @()
    }

    return @(
        $processes | Where-Object {
            ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) -or
            ($_.CommandLine -and $_.CommandLine.IndexOf($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            ($_.CommandLine -and $_.CommandLine.IndexOf("Meridian.Wpf.Tests", [System.StringComparison]::OrdinalIgnoreCase) -ge 0)
        }
    )
}

function Stop-MeridianRepoOwnedTestHostProcesses {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $repoTestHosts = @(Get-MeridianRepoOwnedTestHostProcesses -RepoRoot $RepoRoot)
    if ($repoTestHosts.Count -eq 0) {
        return @()
    }

    foreach ($repoTestHost in $repoTestHosts) {
        Write-Host ("Stopping stale repo-owned testhost PID {0}..." -f $repoTestHost.ProcessId) -ForegroundColor Yellow
        Stop-Process -Id $repoTestHost.ProcessId -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 750
    return $repoTestHosts
}

function Invoke-MeridianStepWithTestHostRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string[]]$Command,
        [Parameter(Mandatory = $true)][string]$LogName,
        [Parameter(Mandatory = $true)][string]$SummaryDir,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)]$Steps,
        [Parameter(Mandatory = $true)]$RetryEvents
    )

    $logPath = Join-Path $SummaryDir $LogName
    $step = Invoke-MeridianLoggedStep -Name $Name -Command $Command -LogPath $logPath
    if ($null -ne $Steps -and $Steps.GetType().GetMethod('Add')) {
        [void]$Steps.Add($step)
    }

    if ($step.exitCode -eq 0) {
        return $step
    }

    $repoTestHosts = @(Get-MeridianRepoOwnedTestHostProcesses -RepoRoot $RepoRoot)
    if ($repoTestHosts.Count -eq 0 -or $null -eq $RetryEvents -or -not $RetryEvents.GetType().GetMethod('Add')) {
        return $step
    }

    $retryLogSuffix = if ($LogName -match '\.log$') { '-retry.log' } else { '-retry.log' }
    $retryLogName = if ($LogName -match '\.log$') {
        $LogName.Substring(0, $LogName.Length - 4) + $retryLogSuffix
    }
    else {
        "$LogName$retryLogSuffix"
    }
    $retryLogPath = Join-Path $SummaryDir $retryLogName
    $stoppedTestHostPids = @($repoTestHosts | Select-Object -ExpandProperty ProcessId)

    Stop-MeridianRepoOwnedTestHostProcesses -RepoRoot $RepoRoot | Out-Null

    $retryReason = "build failed while repo-owned testhost processes were still running"
    [void]$RetryEvents.Add([ordered]@{
            step = $Name
            reason = $retryReason
            stoppedTestHostPids = $stoppedTestHostPids
        })

    $retryStepName = "$Name (retry after testhost cleanup)"
    $retryStep = Invoke-MeridianLoggedStep -Name $retryStepName -Command $Command -LogPath $retryLogPath
    [void]$Steps.Add($retryStep)
    return $retryStep
}
