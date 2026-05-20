<#
.SYNOPSIS
    Installs the Meridian browser workstation as a local Windows application.

.DESCRIPTION
    Builds the Vite workstation bundle, publishes the Meridian desktop-local host,
    installs both into a per-user app directory, creates runtime directories and
    configuration, then creates Desktop and Start Menu shortcuts that launch the
    local host and open /workstation/.

.EXAMPLE
    .\build\scripts\install\install-web-workstation.ps1

.EXAMPLE
    .\build\scripts\install\install-web-workstation.ps1 -SkipDashboardBuild -LaunchAfterInstall
#>

[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "Programs\Meridian Web Workstation"),

    [string]$AppDataRoot = (Join-Path $env:LOCALAPPDATA "Meridian"),

    [ValidateRange(1, 65535)]
    [int]$Port = 8080,

    [ValidateSet("win-x64", "win-arm64")]
    [string]$RuntimeIdentifier = "win-x64",

    [string]$Configuration = "Release",

    [switch]$SkipDashboardBuild,

    [switch]$SkipNpmInstall,

    [switch]$SkipHostPublish,

    [switch]$EnableTrimmedPublish,

    [switch]$NoDesktopShortcut,

    [switch]$NoStartMenuShortcut,

    [switch]$SkipLegacyInstallCleanup,

    [string]$DesktopShortcutRoot = "",

    [string]$StartMenuShortcutDirectory = "",

    [switch]$LaunchAfterInstall,

    [switch]$PlanOnly
)

$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Get-FullPathForNewItem {
    param([Parameter(Mandatory)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-DistinctFullPaths {
    param([string[]]$Paths)

    $results = New-Object System.Collections.Generic.List[string]
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $Paths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        try {
            $fullPath = [System.IO.Path]::GetFullPath($path)
        }
        catch {
            continue
        }

        if ($seen.Add($fullPath)) {
            $results.Add($fullPath) | Out-Null
        }
    }

    return $results.ToArray()
}

function Get-DesktopShortcutRoots {
    param([string]$PreferredRoot)

    if (-not [string]::IsNullOrWhiteSpace($PreferredRoot)) {
        return @(Get-FullPathForNewItem -Path $PreferredRoot)
    }

    $candidates = @(
        [Environment]::GetFolderPath("DesktopDirectory")
    )

    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $candidates += Join-Path $env:USERPROFILE "Desktop"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:OneDrive)) {
        $candidates += Join-Path $env:OneDrive "Desktop"
    }

    return Get-DistinctFullPaths -Paths $candidates
}

function Get-PreferredExistingPath {
    param([string[]]$CandidatePaths)

    foreach ($candidatePath in $CandidatePaths) {
        if (Test-Path -LiteralPath $candidatePath) {
            return [System.IO.Path]::GetFullPath($candidatePath)
        }
    }

    if ($CandidatePaths.Count -gt 0) {
        return [System.IO.Path]::GetFullPath($CandidatePaths[0])
    }

    return $null
}

function Get-RepoRoot {
    $root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    return (Resolve-Path -LiteralPath $root).Path
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    Write-Info $Name
    & $Action
    Write-Success $Name
}

function Assert-Windows {
    if (-not $IsWindows -and $env:OS -ne "Windows_NT") {
        throw "The web workstation installer currently creates Windows shortcuts and must run on Windows."
    }
}

function Assert-RequiredPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description was not found: $Path"
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    Assert-RequiredPath -Path $Source -Description "Source directory"
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Get-InstallTimestamp {
    return Get-Date -Format "yyyyMMdd-HHmmss"
}

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warn "Ignoring unreadable JSON file at ${Path}: $($_.Exception.Message)"
        return $null
    }
}

function Get-DirectoryFileIndex {
    param(
        [Parameter(Mandatory)][string]$Root
    )

    if (-not (Test-Path -LiteralPath $Root)) {
        return @()
    }

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $prefixLength = $resolvedRoot.Length

    return Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{
            RelativePath = $_.FullName.Substring($prefixLength).TrimStart('\')
            Length = [int64]$_.Length
        }
    }
}

function Test-FilesMatch {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source) -or -not (Test-Path -LiteralPath $Destination)) {
        return $false
    }

    $sourceItem = Get-Item -LiteralPath $Source
    $destinationItem = Get-Item -LiteralPath $Destination
    if ($sourceItem.Length -ne $destinationItem.Length) {
        return $false
    }

    return (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -eq
        (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
}

function Test-DirectoryContentsMatch {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source) -or -not (Test-Path -LiteralPath $Destination)) {
        return $false
    }

    $sourceFiles = Get-DirectoryFileIndex -Root $Source
    if ($sourceFiles.Count -eq 0) {
        return $true
    }

    $destinationIndex = @{}
    foreach ($entry in (Get-DirectoryFileIndex -Root $Destination)) {
        $destinationIndex[$entry.RelativePath] = $entry.Length
    }

    foreach ($entry in $sourceFiles) {
        if (-not $destinationIndex.ContainsKey($entry.RelativePath)) {
            return $false
        }

        if ([int64]$destinationIndex[$entry.RelativePath] -ne [int64]$entry.Length) {
            return $false
        }
    }

    return $true
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$InputObject
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $InputObject | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Get-FileSizeMegabytes {
    param(
        [Parameter(Mandatory)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.PSIsContainer) {
        $bytes = (Get-ChildItem -LiteralPath $item.FullName -Recurse -File -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
        return [Math]::Round(($bytes / 1MB), 2)
    }

    return [Math]::Round(($item.Length / 1MB), 2)
}

function Get-ShortcutRecord {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath
    )

    $record = [ordered]@{
        shortcutPath = $ShortcutPath
        exists = Test-Path -LiteralPath $ShortcutPath
        targetPath = $null
        arguments = $null
        workingDirectory = $null
        workingDirectoryExists = $false
        classification = "missing"
    }

    if (-not $record.exists) {
        return [pscustomobject]$record
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $record.targetPath = $shortcut.TargetPath
    $record.arguments = $shortcut.Arguments
    $record.workingDirectory = $shortcut.WorkingDirectory
    $record.workingDirectoryExists = -not [string]::IsNullOrWhiteSpace($shortcut.WorkingDirectory) -and
        (Test-Path -LiteralPath $shortcut.WorkingDirectory)
    $record.classification = if ($record.workingDirectoryExists) { "active-link" } else { "stale-link" }

    return [pscustomobject]$record
}

function Get-MeridianShortcutRecords {
    param(
        [Parameter(Mandatory)][string[]]$Roots,
        [string[]]$KnownShortcutPaths = @()
    )

    $records = New-Object System.Collections.Generic.List[object]
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($knownPath in $KnownShortcutPaths) {
        if (-not [string]::IsNullOrWhiteSpace($knownPath)) {
            $seen.Add([System.IO.Path]::GetFullPath($knownPath)) | Out-Null
        }
    }

    foreach ($root in $Roots) {
        if (-not (Test-Path -LiteralPath $root)) {
            continue
        }

        Get-ChildItem -LiteralPath $root -Filter "*.lnk" -File -ErrorAction SilentlyContinue | Where-Object {
            $_.Name -like "Meridian Web Workstation*.lnk" -or $_.Name -like "Stop Meridian Web Workstation*.lnk"
        } | ForEach-Object {
            $fullPath = [System.IO.Path]::GetFullPath($_.FullName)
            if ($seen.Add($fullPath)) {
                $records.Add((Get-ShortcutRecord -ShortcutPath $fullPath)) | Out-Null
            }
        }
    }

    return $records.ToArray()
}

function Test-ShortcutTargetsInstallRoot {
    param(
        [Parameter(Mandatory)]$ShortcutRecord,
        [Parameter(Mandatory)][string]$InstallRootPath
    )

    $fullInstallRoot = [System.IO.Path]::GetFullPath($InstallRootPath)

    if (-not [string]::IsNullOrWhiteSpace([string]$ShortcutRecord.workingDirectory)) {
        try {
            $fullWorkingDirectory = [System.IO.Path]::GetFullPath([string]$ShortcutRecord.workingDirectory)
            if ($fullWorkingDirectory.StartsWith($fullInstallRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
        catch {
        }
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$ShortcutRecord.arguments) -and
        ([string]$ShortcutRecord.arguments).IndexOf($fullInstallRoot, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return $true
    }

    return $false
}

function Backup-AppDataRoot {
    param(
        [Parameter(Mandatory)][string]$AppDataRootPath,
        [Parameter(Mandatory)][string]$BackupRootPath
    )

    $backupPath = Join-Path $BackupRootPath ("install-" + (Get-InstallTimestamp))
    $copiedItems = New-Object System.Collections.Generic.List[string]
    New-Item -ItemType Directory -Path $backupPath -Force | Out-Null

    if (Test-Path -LiteralPath $AppDataRootPath) {
        Get-ChildItem -LiteralPath $AppDataRootPath -Force | Where-Object {
            $_.Name -notin @("backups", "archive")
        } | ForEach-Object {
            $destination = Join-Path $backupPath $_.Name
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Recurse -Force
            $copiedItems.Add($_.FullName) | Out-Null
        }
    }

    return [pscustomobject]@{
        path = $backupPath
        copiedItems = $copiedItems.ToArray()
    }
}

function Get-LegacyAppDataCandidates {
    param(
        [Parameter(Mandatory)][string]$ActiveAppDataRootPath,
        [string[]]$LegacyInstallRoots = @()
    )

    $appDataParentPath = Split-Path -Parent $ActiveAppDataRootPath
    if (-not (Test-Path -LiteralPath $appDataParentPath)) {
        return @()
    }

    $legacyInstallLookup = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($legacyInstallRoot in $LegacyInstallRoots) {
        if (-not [string]::IsNullOrWhiteSpace($legacyInstallRoot)) {
            $legacyInstallLookup.Add([System.IO.Path]::GetFullPath($legacyInstallRoot)) | Out-Null
        }
    }

    $candidates = New-Object System.Collections.Generic.List[object]
    Get-ChildItem -LiteralPath $appDataParentPath -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -like "Meridian*" -and
        -not [string]::Equals($_.FullName, $ActiveAppDataRootPath, [System.StringComparison]::OrdinalIgnoreCase)
    } | ForEach-Object {
        $manifestPath = Join-Path $_.FullName "service\web-workstation-install-manifest.json"
        $runtimePath = Join-Path $_.FullName "service\web-workstation-runtime.json"
        $hasKnownLayout = (Test-Path -LiteralPath $manifestPath) -or
            (Test-Path -LiteralPath (Join-Path $_.FullName "appsettings.json")) -or
            (Test-Path -LiteralPath (Join-Path $_.FullName "data")) -or
            (Test-Path -LiteralPath (Join-Path $_.FullName "service")) -or
            (Test-Path -LiteralPath (Join-Path $_.FullName "backups")) -or
            (Test-Path -LiteralPath (Join-Path $_.FullName "archive"))

        if (-not $hasKnownLayout) {
            return
        }

        $installManifest = Read-JsonFile -Path $manifestPath
        $linkedInstallRoot = $null
        if ($installManifest -and -not [string]::IsNullOrWhiteSpace([string]$installManifest.installRoot)) {
            try {
                $linkedInstallRoot = [System.IO.Path]::GetFullPath([string]$installManifest.installRoot)
            }
            catch {
                $linkedInstallRoot = $null
            }
        }

        $classification = if ($linkedInstallRoot -and $legacyInstallLookup.Contains($linkedInstallRoot)) {
            "old-app-data"
        }
        else {
            "needs-review"
        }

        $candidates.Add([pscustomobject]@{
                path = $_.FullName
                classification = $classification
                linkedInstallRoot = $linkedInstallRoot
                manifestPath = if (Test-Path -LiteralPath $manifestPath) { $manifestPath } else { $null }
                runtimePath = if (Test-Path -LiteralPath $runtimePath) { $runtimePath } else { $null }
                sizeMb = Get-FileSizeMegabytes -Path $_.FullName
                lastWriteTime = $_.LastWriteTime
            }) | Out-Null
    }

    return $candidates.ToArray()
}

function Read-RuntimeState {
    param(
        [Parameter(Mandatory)][string]$RuntimePath
    )

    if (-not (Test-Path -LiteralPath $RuntimePath)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $RuntimePath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warn "Ignoring unreadable Meridian runtime state at ${RuntimePath}: $($_.Exception.Message)"
        return $null
    }
}

function Remove-RuntimeStateFile {
    param(
        [Parameter(Mandatory)][string]$RuntimePath
    )

    if (Test-Path -LiteralPath $RuntimePath) {
        Remove-Item -LiteralPath $RuntimePath -Force
    }
}

function Get-TrackedRuntimeProcess {
    param($Runtime)

    if ($null -eq $Runtime -or $null -eq $Runtime.processId) {
        return $null
    }

    try {
        return Get-Process -Id ([int]$Runtime.processId) -ErrorAction Stop
    }
    catch {
        return $null
    }
}

function Clear-StaleRuntimeState {
    param(
        [Parameter(Mandatory)][string]$RuntimePath
    )

    $runtime = Read-RuntimeState -RuntimePath $RuntimePath
    if ($null -eq $runtime) {
        return [pscustomobject]@{
            removed = $false
            reason = "runtime-state-not-found"
        }
    }

    $trackedProcess = Get-TrackedRuntimeProcess -Runtime $runtime
    if ($null -ne $trackedProcess) {
        return [pscustomobject]@{
            removed = $false
            reason = "tracked-process-active"
            processId = $runtime.processId
        }
    }

    Remove-RuntimeStateFile -RuntimePath $RuntimePath
    return [pscustomobject]@{
        removed = $true
        reason = "tracked-process-not-running"
        processId = $runtime.processId
    }
}

function Test-TrackedRuntimeProcessMatches {
    param(
        $Runtime,
        [string]$ExpectedInstallRoot
    )

    $process = Get-TrackedRuntimeProcess -Runtime $Runtime
    if ($null -eq $process) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Runtime.exePath) -and $process.Path) {
        if (-not [string]::Equals($process.Path, [string]$Runtime.exePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedInstallRoot) -and $process.Path) {
        $fullInstallRoot = [System.IO.Path]::GetFullPath($ExpectedInstallRoot)
        if (-not $process.Path.StartsWith($fullInstallRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    return $process.ProcessName -like "Meridian*"
}

function Stop-TrackedMeridianHost {
    param(
        [Parameter(Mandatory)][string]$RuntimePath,
        [Parameter(Mandatory)][string]$InstallRootPath
    )

    $runtime = Read-RuntimeState -RuntimePath $RuntimePath
    if ($null -eq $runtime) {
        return [pscustomobject]@{
            attempted = $false
            stopped = $false
            reason = "runtime-state-not-found"
        }
    }

    $trackedProcess = Get-TrackedRuntimeProcess -Runtime $runtime
    if ($null -eq $trackedProcess) {
        Remove-RuntimeStateFile -RuntimePath $RuntimePath
        return [pscustomobject]@{
            attempted = $false
            stopped = $false
            processId = $runtime.processId
            reason = "tracked-process-not-running"
            removedStaleRuntimeState = $true
        }
    }

    if (-not (Test-TrackedRuntimeProcessMatches -Runtime $runtime -ExpectedInstallRoot $InstallRootPath)) {
        return [pscustomobject]@{
            attempted = $false
            stopped = $false
            processId = $runtime.processId
            reason = "tracked-process-did-not-match-install-root"
        }
    }

    $headers = @{}
    if (-not [string]::IsNullOrWhiteSpace([string]$runtime.shutdownToken)) {
        $headers["X-Meridian-Shutdown-Token"] = [string]$runtime.shutdownToken
    }

    $graceful = $false
    if ($runtime.port -and $headers.Count -gt 0) {
        try {
            $shutdownUrl = "http://localhost:{0}/api/system/shutdown" -f [int]$runtime.port
            Invoke-WebRequest -Uri $shutdownUrl -Method Post -UseBasicParsing -TimeoutSec 5 -Headers $headers | Out-Null
            for ($attempt = 0; $attempt -lt 40; $attempt++) {
                Start-Sleep -Milliseconds 250
                if ($null -eq (Get-TrackedRuntimeProcess -Runtime $runtime)) {
                    $graceful = $true
                    break
                }
            }
        }
        catch {
            Write-Warn "Graceful Meridian shutdown request failed: $($_.Exception.Message)"
        }
    }

    if (-not $graceful -and (Test-TrackedRuntimeProcessMatches -Runtime $runtime -ExpectedInstallRoot $InstallRootPath)) {
        Stop-Process -Id ([int]$runtime.processId) -Force
    }

    Remove-RuntimeStateFile -RuntimePath $RuntimePath

    return [pscustomobject]@{
        attempted = $true
        stopped = $true
        graceful = $graceful
        processId = $runtime.processId
        port = $runtime.port
    }
}

function Get-MeridianProcessesForInstallRoot {
    param(
        [Parameter(Mandatory)][string]$InstallRootPath
    )

    $fullInstallRoot = [System.IO.Path]::GetFullPath($InstallRootPath)
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $_.ProcessName -like "Meridian*" -and
            $_.Path -and
            $_.Path.StartsWith($fullInstallRoot, [System.StringComparison]::OrdinalIgnoreCase)
        })
}

function Stop-MeridianProcessesForInstallRoot {
    param(
        [Parameter(Mandatory)][string]$InstallRootPath,
        [string[]]$RuntimePaths = @()
    )

    $results = New-Object System.Collections.Generic.List[object]

    foreach ($runtimePath in (Get-DistinctFullPaths -Paths $RuntimePaths)) {
        if (-not (Test-Path -LiteralPath $runtimePath)) {
            continue
        }

        $stopResult = Stop-TrackedMeridianHost -RuntimePath $runtimePath -InstallRootPath $InstallRootPath
        if ($stopResult.attempted) {
            $results.Add([pscustomobject]@{
                    method = "runtime-state"
                    processId = $stopResult.processId
                    graceful = $stopResult.graceful
                    port = $stopResult.port
                    stopped = $stopResult.stopped
                }) | Out-Null
        }
    }

    foreach ($process in (Get-MeridianProcessesForInstallRoot -InstallRootPath $InstallRootPath)) {
        Stop-Process -Id $process.Id -Force
        $results.Add([pscustomobject]@{
                method = "path-match-force-stop"
                processId = $process.Id
                graceful = $false
                port = $null
                stopped = $true
            }) | Out-Null
    }

    return $results.ToArray()
}

function Remove-StaleShortcut {
    param(
        [Parameter(Mandatory)]$ShortcutRecord
    )

    if ($ShortcutRecord.exists -and $ShortcutRecord.classification -eq "stale-link") {
        Remove-Item -LiteralPath $ShortcutRecord.shortcutPath -Force
        return $true
    }

    return $false
}

function Get-ArchiveDestinationPath {
    param(
        [Parameter(Mandatory)][string]$ArchiveRunRoot,
        [Parameter(Mandatory)][string]$Bucket,
        [Parameter(Mandatory)][string]$LeafName
    )

    $bucketRoot = Join-Path $ArchiveRunRoot $Bucket
    New-Item -ItemType Directory -Path $bucketRoot -Force | Out-Null

    $destinationPath = Join-Path $bucketRoot $LeafName
    if (-not (Test-Path -LiteralPath $destinationPath)) {
        return $destinationPath
    }

    $suffix = 2
    do {
        $destinationPath = Join-Path $bucketRoot ("{0}-{1}" -f $LeafName, $suffix)
        $suffix++
    } while (Test-Path -LiteralPath $destinationPath)

    return $destinationPath
}

function Move-PathToArchive {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$ArchiveRunRoot,
        [Parameter(Mandatory)][string]$Bucket
    )

    if (-not (Test-Path -LiteralPath $SourcePath)) {
        return $null
    }

    $destinationPath = Get-ArchiveDestinationPath -ArchiveRunRoot $ArchiveRunRoot -Bucket $Bucket -LeafName (Split-Path -Leaf $SourcePath)
    Move-Item -LiteralPath $SourcePath -Destination $destinationPath

    return [pscustomobject]@{
        sourcePath = $SourcePath
        destinationPath = $destinationPath
        bucket = $Bucket
    }
}

function Remove-EmptyDirectoryTree {
    param(
        [Parameter(Mandatory)][string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $hasFiles = Get-ChildItem -LiteralPath $Path -Recurse -File -Force -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $hasFiles) {
        return $false
    }

    Remove-Item -LiteralPath $Path -Recurse -Force
    return $true
}

function Migrate-InstallRootData {
    param(
        [Parameter(Mandatory)][string]$InstallDataRootPath,
        [Parameter(Mandatory)][string]$PersistentDataRootPath,
        [Parameter(Mandatory)][string]$BackupSnapshotPath
    )

    if (-not (Test-Path -LiteralPath $InstallDataRootPath)) {
        return [pscustomobject]@{
            foundFiles = $false
            migratedRelativePaths = @()
            skippedRelativePaths = @()
            conflictRelativePaths = @()
            backupPath = $null
            removedInstallRootData = $false
        }
    }

    $files = @(Get-ChildItem -LiteralPath $InstallDataRootPath -Recurse -File -ErrorAction SilentlyContinue)
    if ($files.Count -eq 0) {
        return [pscustomobject]@{
            foundFiles = $false
            migratedRelativePaths = @()
            skippedRelativePaths = @()
            conflictRelativePaths = @()
            backupPath = $null
            removedInstallRootData = $false
        }
    }

    $backupPath = Join-Path $BackupSnapshotPath "install-root-data"
    Copy-DirectoryContents -Source $InstallDataRootPath -Destination $backupPath

    $resolvedInstallDataRoot = (Resolve-Path -LiteralPath $InstallDataRootPath).Path
    $prefixLength = $resolvedInstallDataRoot.Length
    $migrated = New-Object System.Collections.Generic.List[string]
    $skipped = New-Object System.Collections.Generic.List[string]
    $conflicts = New-Object System.Collections.Generic.List[string]

    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($prefixLength).TrimStart('\')
        $destinationPath = Join-Path $PersistentDataRootPath $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        if (Test-Path -LiteralPath $destinationPath) {
            $destinationItem = Get-Item -LiteralPath $destinationPath
            if ($destinationItem.Length -eq $file.Length) {
                $skipped.Add($relativePath) | Out-Null
                continue
            }

            $conflicts.Add($relativePath) | Out-Null
            continue
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
        $migrated.Add($relativePath) | Out-Null
    }

    $removedInstallRootData = $false
    if ($conflicts.Count -eq 0) {
        Remove-Item -LiteralPath $InstallDataRootPath -Recurse -Force
        $removedInstallRootData = $true
    }

    return [pscustomobject]@{
        foundFiles = $true
        migratedRelativePaths = $migrated.ToArray()
        skippedRelativePaths = $skipped.ToArray()
        conflictRelativePaths = $conflicts.ToArray()
        backupPath = $backupPath
        removedInstallRootData = $removedInstallRootData
    }
}

function New-DefaultConfig {
    param(
        [Parameter(Mandatory)][string]$ConfigPath
    )

    if (Test-Path -LiteralPath $ConfigPath) {
        Write-Info "Keeping existing config: $ConfigPath"
        return
    }

    $config = @"
{
  "DataRoot": "data",
  "DataSource": "Synthetic",
  "Synthetic": {
    "Enabled": true
  },
  "Symbols": [],
  "Storage": {
    "NamingConvention": "BySymbol",
    "CompressionProfile": "Standard"
  },
  "Backfill": {
    "Enabled": false,
    "Provider": "stooq",
    "EnableFallback": true,
    "EnableSymbolResolution": true
  },
  "Logging": {
    "Level": "Information"
  },
  "UI": {
    "Theme": "Light",
    "RefreshIntervalMs": 1000
  }
}
"@

    New-Item -ItemType Directory -Path (Split-Path -Parent $ConfigPath) -Force | Out-Null
    Set-Content -LiteralPath $ConfigPath -Value $config -Encoding UTF8
}

function New-LauncherScript {
    param(
        [Parameter(Mandatory)][string]$LauncherPath,
        [Parameter(Mandatory)][string]$ConfigPath,
        [Parameter(Mandatory)][int]$DefaultPort
    )

    $escapedConfigPath = $ConfigPath.Replace('"', '`"')
    $launcher = @"
param(
    [ValidateRange(1, 65535)]
    [int]`$Port = $DefaultPort,

    [switch]`$Stop,

    [switch]`$Status
)

`$ErrorActionPreference = "Stop"
`$installRoot = Split-Path -Parent `$MyInvocation.MyCommand.Path
`$exePath = Join-Path `$installRoot "Meridian.exe"
`$configPath = "$escapedConfigPath"
`$stateRoot = Join-Path (Split-Path -Parent `$configPath) "service"
`$runtimePath = Join-Path `$stateRoot "web-workstation-runtime.json"
`$healthUrl = "http://localhost:`$Port/healthz"
`$lifecycleUrl = "http://localhost:`$Port/api/system/lifecycle"
`$shutdownUrl = "http://localhost:`$Port/api/system/shutdown"
`$workstationUrl = "http://localhost:`$Port/workstation/"
`$loginUrl = "http://localhost:`$Port/login?returnUrl=%2Fworkstation%2F"

if ([string]::IsNullOrWhiteSpace(`$env:MDC_AUTH_MODE)) {
    `$env:MDC_AUTH_MODE = "required"
}

function Test-MeridianEndpoint {
    param(
        [Parameter(Mandatory)][string]`$Uri,
        [hashtable]`$Headers = @{}
    )

    try {
        `$response = Invoke-WebRequest -Uri `$Uri -UseBasicParsing -TimeoutSec 2 -Headers `$Headers
        return `$response.StatusCode -ge 200 -and `$response.StatusCode -lt 500
    }
    catch {
        return `$false
    }
}

function New-ShutdownToken {
    return (([guid]::NewGuid().ToString("N")) + ([guid]::NewGuid().ToString("N")))
}

function Read-RuntimeState {
    if (-not (Test-Path -LiteralPath `$runtimePath)) {
        return `$null
    }

    try {
        return Get-Content -LiteralPath `$runtimePath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Ignoring unreadable Meridian runtime state at `${runtimePath}: `$(`$_.Exception.Message)"
        return `$null
    }
}

function Write-RuntimeState {
    param(
        [Parameter(Mandatory)]`$Process,
        [Parameter(Mandatory)][string]`$ShutdownToken
    )

    New-Item -ItemType Directory -Path `$stateRoot -Force | Out-Null
    [ordered]@{
        processId = [int]`$Process.Id
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        port = [int]`$Port
        exePath = `$exePath
        installRoot = `$installRoot
        configPath = `$configPath
        shutdownToken = `$ShutdownToken
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath `$runtimePath -Encoding UTF8
}

function Remove-RuntimeState {
    if (Test-Path -LiteralPath `$runtimePath) {
        Remove-Item -LiteralPath `$runtimePath -Force
    }
}

function Get-TrackedProcess {
    param(`$Runtime)

    if (`$null -eq `$Runtime -or `$null -eq `$Runtime.processId) {
        return `$null
    }

    try {
        return Get-Process -Id ([int]`$Runtime.processId) -ErrorAction Stop
    }
    catch {
        return `$null
    }
}

function Test-TrackedProcessMatches {
    param(`$Runtime)

    `$process = Get-TrackedProcess -Runtime `$Runtime
    if (`$null -eq `$process) {
        return `$false
    }

    if (`$Runtime.exePath -and `$process.Path) {
        if (-not [string]::Equals(`$process.Path, [string]`$Runtime.exePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return `$false
        }
    }

    if (`$Runtime.startedAtUtc) {
        try {
            `$runtimeStart = [DateTimeOffset]::Parse([string]`$Runtime.startedAtUtc).UtcDateTime
            if (`$process.StartTime.ToUniversalTime() -lt `$runtimeStart.AddSeconds(-5)) {
                return `$false
            }
        }
        catch {
            return `$false
        }
    }

    return `$process.ProcessName -like "Meridian*"
}

function Get-LifecycleHeaders {
    param(`$Runtime)

    if (`$null -ne `$Runtime -and -not [string]::IsNullOrWhiteSpace([string]`$Runtime.shutdownToken)) {
        return @{ "X-Meridian-Shutdown-Token" = [string]`$Runtime.shutdownToken }
    }

    return @{}
}

function Stop-MeridianHost {
    `$runtime = Read-RuntimeState
    if (`$null -eq `$runtime) {
        Write-Warning "No Meridian runtime state was found at `$runtimePath."
        return
    }

    `$headers = Get-LifecycleHeaders -Runtime `$runtime
    if (`$headers.Count -gt 0) {
        try {
            Invoke-WebRequest -Uri `$shutdownUrl -Method Post -UseBasicParsing -TimeoutSec 5 -Headers `$headers | Out-Null
            for (`$attempt = 0; `$attempt -lt 40; `$attempt++) {
                Start-Sleep -Milliseconds 250
                if (`$null -eq (Get-TrackedProcess -Runtime `$runtime)) {
                    Remove-RuntimeState
                    Write-Host "Meridian host stopped gracefully."
                    return
                }
            }
        }
        catch {
            Write-Warning "Graceful Meridian shutdown request failed: `$(`$_.Exception.Message)"
        }
    }

    if (Test-TrackedProcessMatches -Runtime `$runtime) {
        Write-Warning "Meridian did not stop gracefully; terminating the tracked owned process `$(`$runtime.processId)."
        Stop-Process -Id ([int]`$runtime.processId) -Force
        Remove-RuntimeState
        return
    }

    throw "Refusing to stop process `$(`$runtime.processId) because it no longer matches the stored Meridian runtime metadata."
}

function Show-MeridianStatus {
    `$runtime = Read-RuntimeState
    if (`$null -eq `$runtime) {
        Write-Host "No Meridian runtime state is currently tracked."
        return
    }

    `$process = Get-TrackedProcess -Runtime `$runtime
    `$state = if (`$null -eq `$process) { "stopped" } else { "running" }
    Write-Host "Meridian host state: `$state"
    Write-Host "  PID: `$(`$runtime.processId)"
    Write-Host "  Port: `$(`$runtime.port)"
    Write-Host "  Config: `$(`$runtime.configPath)"
}

if (-not (Test-Path -LiteralPath `$exePath)) {
    throw "Meridian.exe was not found at `$exePath. Re-run the workstation installer."
}

if (`$Stop) {
    Stop-MeridianHost
    return
}

if (`$Status) {
    Show-MeridianStatus
    return
}

if (-not (Test-MeridianEndpoint -Uri `$healthUrl)) {
    `$shutdownToken = New-ShutdownToken
    `$env:MDC_SHUTDOWN_TOKEN = `$shutdownToken
    `$arguments = "--mode workstation --http-port `$Port --config ```"`$configPath```""
    `$process = Start-Process -FilePath `$exePath -ArgumentList `$arguments -WorkingDirectory `$installRoot -WindowStyle Hidden -PassThru
    Write-RuntimeState -Process `$process -ShutdownToken `$shutdownToken

    `$ready = `$false
    for (`$attempt = 0; `$attempt -lt 45; `$attempt++) {
        Start-Sleep -Seconds 1
        if (Test-MeridianEndpoint -Uri `$healthUrl) {
            `$ready = `$true
            break
        }
    }

    if (-not `$ready) {
        Write-Warning "Meridian host did not answer `$healthUrl within 45 seconds. Opening the workstation route anyway."
    }
}

Start-Process `$loginUrl
"@

    Set-Content -LiteralPath $LauncherPath -Value $launcher -Encoding UTF8
}

function Get-PowerShellShortcutTarget {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) {
        return $pwsh.Source
    }

    $powershell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($powershell) {
        return $powershell.Source
    }

    throw "Neither pwsh nor Windows PowerShell was found on PATH."
}

function New-AppShortcut {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$LauncherPath,
        [Parameter(Mandatory)][string]$InstallRootPath,
        [Parameter(Mandatory)][string]$IconPath,
        [string]$LauncherArguments = "",
        [string]$Description = "Launch Meridian Web Workstation"
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = Get-PowerShellShortcutTarget
    $launcherSuffix = if ([string]::IsNullOrWhiteSpace($LauncherArguments)) { "" } else { " $LauncherArguments" }
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$LauncherPath`"$launcherSuffix"
    $shortcut.WorkingDirectory = $InstallRootPath
    if (Test-Path -LiteralPath $IconPath) {
        $shortcut.IconLocation = $IconPath
    }
    $shortcut.Description = $Description
    $shortcut.Save()
}

Assert-Windows

$repoRoot = Get-RepoRoot
$installRootPath = Get-FullPathForNewItem -Path $InstallRoot
$appDataRootPath = Get-FullPathForNewItem -Path $AppDataRoot
$dataRootPath = Join-Path $appDataRootPath "data"
$backupRootPath = Join-Path $appDataRootPath "backups"
$archiveRootPath = Join-Path $appDataRootPath "archive\old-installs"
$serviceRootPath = Join-Path $appDataRootPath "service"
$configPath = Join-Path $appDataRootPath "appsettings.json"
$runtimePath = Join-Path $serviceRootPath "web-workstation-runtime.json"
$installManifestPath = Join-Path $serviceRootPath "web-workstation-install-manifest.json"
$hostProject = Join-Path $repoRoot "src\Meridian\Meridian.csproj"
$dashboardRoot = Join-Path $repoRoot "src\Meridian.Ui\dashboard"
$dashboardPackageJson = Join-Path $dashboardRoot "package.json"
$dashboardNodeModules = Join-Path $dashboardRoot "node_modules"
$dashboardBundle = Join-Path $repoRoot "src\Meridian.Ui\wwwroot\workstation"
$publishRoot = Join-Path $repoRoot "artifacts\publish\web-workstation\$RuntimeIdentifier"
$publishExePath = Join-Path $publishRoot "Meridian.exe"
$launcherPath = Join-Path $installRootPath "Launch-MeridianWebWorkstation.ps1"
$installedExePath = Join-Path $installRootPath "Meridian.exe"
$iconSource = Join-Path $repoRoot "src\Meridian\app.ico"
$iconPath = Join-Path $installRootPath "app.ico"
$targetBundle = Join-Path $installRootPath "wwwroot\workstation"
$desktopShortcutRoots = Get-DesktopShortcutRoots -PreferredRoot $DesktopShortcutRoot
$desktopShortcutRoot = Get-PreferredExistingPath -CandidatePaths $desktopShortcutRoots
$desktopShortcutPath = if ($desktopShortcutRoot) { Join-Path $desktopShortcutRoot "Meridian Web Workstation.lnk" } else { $null }
$startMenuDir = if (-not [string]::IsNullOrWhiteSpace($StartMenuShortcutDirectory)) {
    Get-FullPathForNewItem -Path $StartMenuShortcutDirectory
}
else {
    Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Meridian"
}
$startMenuShortcutPath = Join-Path $startMenuDir "Meridian Web Workstation.lnk"
$startMenuStopShortcutPath = Join-Path $startMenuDir "Stop Meridian Web Workstation.lnk"

$installParentPath = Split-Path -Parent $installRootPath
$legacyInstallCandidates = @()
if (Test-Path -LiteralPath $installParentPath) {
    $legacyInstallCandidates = Get-ChildItem -LiteralPath $installParentPath -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -like "Meridian Web Workstation*" -and
            -not [string]::Equals($_.FullName, $installRootPath, [System.StringComparison]::OrdinalIgnoreCase)
        } | ForEach-Object {
            [pscustomobject]@{
                path = $_.FullName
                classification = "old-install"
                sizeMb = Get-FileSizeMegabytes -Path $_.FullName
                lastWriteTime = $_.LastWriteTime
            }
        }
}

$legacyInstallRootPaths = @($legacyInstallCandidates | ForEach-Object { $_.path })
$legacyAppDataCandidates = if ($legacyInstallRootPaths.Count -gt 0) {
    Get-LegacyAppDataCandidates -ActiveAppDataRootPath $appDataRootPath -LegacyInstallRoots $legacyInstallRootPaths
}
else {
    @()
}
$desktopShortcutRecord = if ($desktopShortcutPath) { Get-ShortcutRecord -ShortcutPath $desktopShortcutPath } else { $null }
$startMenuShortcutRecord = Get-ShortcutRecord -ShortcutPath $startMenuShortcutPath
$startMenuStopShortcutRecord = Get-ShortcutRecord -ShortcutPath $startMenuStopShortcutPath
$shortcutSearchRoots = @()
$shortcutSearchRoots += $desktopShortcutRoots
$shortcutSearchRoots += $startMenuDir
$additionalShortcutRecords = Get-MeridianShortcutRecords -Roots $shortcutSearchRoots -KnownShortcutPaths @(
    $desktopShortcutPath,
    $startMenuShortcutPath,
    $startMenuStopShortcutPath
)
$shortcutRecords = @($desktopShortcutRecord, $startMenuShortcutRecord, $startMenuStopShortcutRecord) |
    Where-Object { $null -ne $_ }
$shortcutRecords = $shortcutRecords + $additionalShortcutRecords
$legacyShortcutRecords = @(
    $shortcutRecords | Where-Object {
        foreach ($legacyInstallRootPath in $legacyInstallRootPaths) {
            if (Test-ShortcutTargetsInstallRoot -ShortcutRecord $_ -InstallRootPath $legacyInstallRootPath) {
                return $true
            }
        }

        return $false
    }
)
$existingRuntime = Read-RuntimeState -RuntimePath $runtimePath
$trackedProcess = Get-TrackedRuntimeProcess -Runtime $existingRuntime
$runningInstallPath = if ($trackedProcess -and $trackedProcess.Path) { Split-Path -Parent $trackedProcess.Path } else { $null }
$discoverySnapshot = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    targetInstallRoot = $installRootPath
    appDataRoot = $appDataRootPath
    desktopShortcutRoot = $desktopShortcutRoot
    desktopShortcutRoots = $desktopShortcutRoots
    startMenuShortcutDirectory = $startMenuDir
    runtimeIdentifier = $RuntimeIdentifier
    installCandidates = @(
        if (Test-Path -LiteralPath $installRootPath) {
            [pscustomobject]@{
                path = $installRootPath
                classification = if ($trackedProcess -and $runningInstallPath -and [string]::Equals($runningInstallPath, $installRootPath, [System.StringComparison]::OrdinalIgnoreCase)) { "active-install" } else { "target-install" }
                sizeMb = Get-FileSizeMegabytes -Path $installRootPath
                lastWriteTime = (Get-Item -LiteralPath $installRootPath).LastWriteTime
            }
        }
    ) + $legacyInstallCandidates
    legacyAppDataCandidates = $legacyAppDataCandidates
    shortcuts = $shortcutRecords
    legacyShortcuts = $legacyShortcutRecords
    runtime = if ($existingRuntime) {
        [pscustomobject]@{
            processId = $existingRuntime.processId
            port = $existingRuntime.port
            exePath = $existingRuntime.exePath
            trackedProcessMatches = Test-TrackedRuntimeProcessMatches -Runtime $existingRuntime -ExpectedInstallRoot $installRootPath
        }
    }
    else {
        $null
    }
}

Assert-RequiredPath -Path $hostProject -Description "Meridian host project"
Assert-RequiredPath -Path $dashboardPackageJson -Description "Dashboard package.json"

Write-Host ""
Write-Host "Meridian Web Workstation install plan" -ForegroundColor White
Write-Host "  Repo root:       $repoRoot"
Write-Host "  Install root:    $installRootPath"
Write-Host "  App data root:   $appDataRootPath"
Write-Host "  Config path:     $configPath"
Write-Host "  Data root:       $dataRootPath"
Write-Host "  Backup root:     $backupRootPath"
Write-Host "  Archive root:    $archiveRootPath"
Write-Host "  Install report:  $installManifestPath"
Write-Host "  Port:            $Port"
Write-Host "  Runtime:         $RuntimeIdentifier"
Write-Host "  Workstation URL: http://localhost:$Port/workstation/"
if ($legacyInstallCandidates.Count -gt 0) {
    Write-Host "  Legacy installs: $($legacyInstallCandidates.Count)"
}
if ($legacyAppDataCandidates.Count -gt 0) {
    Write-Host "  Legacy app data: $($legacyAppDataCandidates.Count)"
}
if ($legacyShortcutRecords.Count -gt 0) {
    Write-Host "  Legacy shortcuts: $($legacyShortcutRecords.Count)"
}
if (($shortcutRecords | Where-Object { $_.classification -eq 'stale-link' }).Count -gt 0) {
    Write-Host "  Stale shortcuts: $(($shortcutRecords | Where-Object { $_.classification -eq 'stale-link' }).Count)"
}
Write-Host ""

if ($PlanOnly) {
    Write-Info "PlanOnly was specified; no files were built, copied, or installed."
    exit 0
}

Invoke-Step -Name "Create application directories" -Action {
    $directories = @(
        $installRootPath,
        $appDataRootPath,
        $dataRootPath,
        $backupRootPath,
        $archiveRootPath,
        $serviceRootPath,
        (Join-Path $dataRootPath "workstation"),
        (Join-Path $dataRootPath "workstation\evidence"),
        (Join-Path $dataRootPath "workstation\workflows"),
        $targetBundle
    )

    foreach ($directory in $directories) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
}

$script:backupResult = $null
Invoke-Step -Name "Back up Meridian app data" -Action {
    $script:backupResult = Backup-AppDataRoot -AppDataRootPath $appDataRootPath -BackupRootPath $backupRootPath
}

Invoke-Step -Name "Create default app configuration if needed" -Action {
    New-DefaultConfig -ConfigPath $configPath
}

if (-not $SkipNpmInstall -and -not (Test-Path -LiteralPath $dashboardNodeModules)) {
    Invoke-Step -Name "Install dashboard dependencies" -Action {
        Push-Location $dashboardRoot
        try {
            & npm install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }
}

if (-not $SkipDashboardBuild) {
    Invoke-Step -Name "Build browser workstation bundle" -Action {
        Push-Location $dashboardRoot
        try {
            & npm run build
            if ($LASTEXITCODE -ne 0) {
                throw "npm run build failed with exit code $LASTEXITCODE. If this reports ENOTEMPTY or EPERM under wwwroot/workstation/assets, stop stale Vite preview processes and retry."
            }
        }
        finally {
            Pop-Location
        }
    }
}

$workstationAssetSource = $dashboardBundle
if (-not (Test-Path -LiteralPath (Join-Path $workstationAssetSource "index.html")) -and
    $SkipDashboardBuild -and
    (Test-Path -LiteralPath (Join-Path $targetBundle "index.html"))) {
    Write-Warn "Built workstation bundle was not found under the repo; reusing the installed workstation assets."
    $workstationAssetSource = $targetBundle
}

Assert-RequiredPath -Path (Join-Path $workstationAssetSource "index.html") -Description "Built workstation index.html"

if (-not $SkipHostPublish) {
    Invoke-Step -Name "Publish Meridian local host" -Action {
        New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
        $publishTrimmed = $EnableTrimmedPublish.IsPresent.ToString().ToLowerInvariant()

        $publishArgs = @(
            "publish",
            $hostProject,
            "-c", $Configuration,
            "-r", $RuntimeIdentifier,
            "-o", $publishRoot,
            "--self-contained", "true",
            "-p:PublishSingleFile=true",
            "-p:PublishReadyToRun=false",
            "-p:PublishTrimmed=$publishTrimmed"
        )

        & dotnet $publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }
}

$hostSourceRoot = $publishRoot
if (-not (Test-Path -LiteralPath $publishExePath) -and
    $SkipHostPublish -and
    (Test-Path -LiteralPath $installedExePath)) {
    Write-Warn "Published host output was not found under the repo; reusing the installed Meridian host files."
    $hostSourceRoot = $installRootPath
    $publishExePath = $installedExePath
}

Assert-RequiredPath -Path $publishExePath -Description "Published Meridian.exe"

$hostCopySkipped = Test-DirectoryContentsMatch -Source $hostSourceRoot -Destination $installRootPath
$bundleCopySkipped = Test-DirectoryContentsMatch -Source $workstationAssetSource -Destination $targetBundle
$staleShortcutRemovals = New-Object System.Collections.Generic.List[string]
$legacyShortcutRemovals = New-Object System.Collections.Generic.List[string]
$archivedLegacyInstallRoots = New-Object System.Collections.Generic.List[object]
$archivedLegacyAppDataRoots = New-Object System.Collections.Generic.List[object]
$legacyProcessStops = New-Object System.Collections.Generic.List[object]
$legacyCleanupFailures = New-Object System.Collections.Generic.List[object]
$legacyArchiveRunRoot = $null
$installCleanupRemovals = New-Object System.Collections.Generic.List[string]
$script:runtimeStateCleanup = $null
$script:installRootDataMigration = $null
$script:stopResult = $null

Invoke-Step -Name "Clear stale Meridian runtime state" -Action {
    $script:runtimeStateCleanup = Clear-StaleRuntimeState -RuntimePath $runtimePath
    if ($script:runtimeStateCleanup.removed) {
        Write-Info "Removed stale Meridian runtime state for PID $($script:runtimeStateCleanup.processId)."
    }
    else {
        Write-Info "No stale runtime state cleanup was required ($($script:runtimeStateCleanup.reason))."
    }
}

if (-not $hostCopySkipped -or -not $bundleCopySkipped) {
    Invoke-Step -Name "Stop tracked Meridian host before file replacement" -Action {
        $script:stopResult = Stop-TrackedMeridianHost -RuntimePath $runtimePath -InstallRootPath $installRootPath
        if ($script:stopResult.attempted) {
            Write-Info "Stopped tracked Meridian host PID $($script:stopResult.processId)."
        }
        else {
            Write-Info "No tracked Meridian host required shutdown ($($script:stopResult.reason))."
        }
    }
}
else {
    Write-Info "Installed host files already match the published host output; skipping file replacement shutdown."
}

if ($hostCopySkipped) {
    Write-Info "Installed host already matches $hostSourceRoot; skipping host file copy."
}
else {
    Invoke-Step -Name "Copy host files into install root" -Action {
        Copy-DirectoryContents -Source $hostSourceRoot -Destination $installRootPath
    }
}

if ($bundleCopySkipped) {
    Write-Info "Installed workstation assets already match $workstationAssetSource; skipping asset copy."
}
else {
    Invoke-Step -Name "Copy workstation assets into install root" -Action {
        Copy-DirectoryContents -Source $workstationAssetSource -Destination $targetBundle
    }
}

if (Test-Path -LiteralPath $iconSource) {
    Invoke-Step -Name "Copy application icon" -Action {
        Copy-Item -LiteralPath $iconSource -Destination $iconPath -Force
    }
}
else {
    Write-Warn "Icon source was not found: $iconSource"
}

Invoke-Step -Name "Create launcher script" -Action {
    New-LauncherScript -LauncherPath $launcherPath -ConfigPath $configPath -DefaultPort $Port
}

Invoke-Step -Name "Remove stale Meridian shortcuts" -Action {
    foreach ($shortcutRecord in $shortcutRecords) {
        if (Remove-StaleShortcut -ShortcutRecord $shortcutRecord) {
            $staleShortcutRemovals.Add($shortcutRecord.shortcutPath) | Out-Null
        }
    }
}

if (-not $SkipLegacyInstallCleanup -and $legacyInstallCandidates.Count -gt 0) {
    Invoke-Step -Name "Archive superseded Meridian installs" -Action {
        foreach ($legacyInstallCandidate in $legacyInstallCandidates) {
            try {
                $matchedAppDataCandidates = @(
                    $legacyAppDataCandidates | Where-Object {
                        $_.classification -eq "old-app-data" -and
                        $_.linkedInstallRoot -and
                        [string]::Equals([string]$_.linkedInstallRoot, [string]$legacyInstallCandidate.path, [System.StringComparison]::OrdinalIgnoreCase)
                    }
                )

                $runtimePaths = @($matchedAppDataCandidates | ForEach-Object { $_.runtimePath })
                foreach ($stopResult in (Stop-MeridianProcessesForInstallRoot -InstallRootPath $legacyInstallCandidate.path -RuntimePaths $runtimePaths)) {
                    $legacyProcessStops.Add($stopResult) | Out-Null
                }

                if (-not $legacyArchiveRunRoot) {
                    $legacyArchiveRunRoot = Join-Path $archiveRootPath ("install-" + (Get-InstallTimestamp))
                    New-Item -ItemType Directory -Path $legacyArchiveRunRoot -Force | Out-Null
                }

                $archivedInstallRoot = Move-PathToArchive -SourcePath $legacyInstallCandidate.path -ArchiveRunRoot $legacyArchiveRunRoot -Bucket "install-roots"
                if ($archivedInstallRoot) {
                    $archivedLegacyInstallRoots.Add($archivedInstallRoot) | Out-Null
                }

                foreach ($matchedAppDataCandidate in $matchedAppDataCandidates) {
                    $archivedAppDataRoot = Move-PathToArchive -SourcePath $matchedAppDataCandidate.path -ArchiveRunRoot $legacyArchiveRunRoot -Bucket "app-data"
                    if ($archivedAppDataRoot) {
                        $archivedLegacyAppDataRoots.Add($archivedAppDataRoot) | Out-Null
                    }
                }

                foreach ($legacyShortcutRecord in @(
                        $shortcutRecords | Where-Object {
                            Test-ShortcutTargetsInstallRoot -ShortcutRecord $_ -InstallRootPath $legacyInstallCandidate.path
                        }
                    )) {
                    if (Test-Path -LiteralPath $legacyShortcutRecord.shortcutPath) {
                        Remove-Item -LiteralPath $legacyShortcutRecord.shortcutPath -Force
                        $legacyShortcutRemovals.Add($legacyShortcutRecord.shortcutPath) | Out-Null
                    }
                }
            }
            catch {
                Write-Warn "Failed to archive superseded install $($legacyInstallCandidate.path): $($_.Exception.Message)"
                $legacyCleanupFailures.Add([pscustomobject]@{
                        path = $legacyInstallCandidate.path
                        error = $_.Exception.Message
                    }) | Out-Null
            }
        }
    }
}

if (-not $NoDesktopShortcut) {
    if ($desktopShortcutPath) {
        Invoke-Step -Name "Create Desktop shortcut" -Action {
            New-Item -ItemType Directory -Path $desktopShortcutRoot -Force | Out-Null
            New-AppShortcut -ShortcutPath $desktopShortcutPath -LauncherPath $launcherPath -InstallRootPath $installRootPath -IconPath $iconPath
        }
    }
    else {
        Write-Warn "No usable Desktop shortcut root was found; skipping Desktop shortcut creation."
    }
}

if (-not $NoStartMenuShortcut) {
    Invoke-Step -Name "Create Start Menu shortcut" -Action {
        New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
        New-AppShortcut -ShortcutPath $startMenuShortcutPath -LauncherPath $launcherPath -InstallRootPath $installRootPath -IconPath $iconPath
        New-AppShortcut -ShortcutPath $startMenuStopShortcutPath -LauncherPath $launcherPath -InstallRootPath $installRootPath -IconPath $iconPath -LauncherArguments "-Stop" -Description "Stop Meridian Web Workstation"
    }
}

Invoke-Step -Name "Migrate legacy install-root data into app data" -Action {
    $script:installRootDataMigration = Migrate-InstallRootData `
        -InstallDataRootPath (Join-Path $installRootPath "data") `
        -PersistentDataRootPath $dataRootPath `
        -BackupSnapshotPath $script:backupResult.path

    if ($script:installRootDataMigration.foundFiles) {
        Write-Info "Migrated legacy install-root data backup to $($script:installRootDataMigration.backupPath)."
    }
}

Invoke-Step -Name "Clean empty install-root data placeholders" -Action {
    foreach ($candidate in @(
            (Join-Path $installRootPath "data"),
            (Join-Path $installRootPath "artifacts")
        )) {
        if (Remove-EmptyDirectoryTree -Path $candidate) {
            $installCleanupRemovals.Add($candidate) | Out-Null
        }
    }
}

$installVersion = if (Test-Path -LiteralPath $installedExePath) { [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedExePath) } else { $null }
$installManifest = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    installRoot = $installRootPath
    appDataRoot = $appDataRootPath
    configPath = $configPath
    dataRoot = $dataRootPath
    backup = $script:backupResult
    source = [ordered]@{
        repoRoot = $repoRoot
        publishRoot = $publishRoot
        hostSourceRoot = $hostSourceRoot
        dashboardBundle = $dashboardBundle
        workstationAssetSource = $workstationAssetSource
        runtimeIdentifier = $RuntimeIdentifier
    }
    discovery = $discoverySnapshot
    actions = [ordered]@{
        hostCopySkipped = $hostCopySkipped
        bundleCopySkipped = $bundleCopySkipped
        runtimeStateCleanup = $script:runtimeStateCleanup
        stoppedTrackedHost = $script:stopResult
        legacyArchiveRoot = $legacyArchiveRunRoot
        legacyProcessStops = $legacyProcessStops.ToArray()
        archivedLegacyInstallRoots = $archivedLegacyInstallRoots.ToArray()
        archivedLegacyAppDataRoots = $archivedLegacyAppDataRoots.ToArray()
        legacyShortcutsRemoved = $legacyShortcutRemovals.ToArray()
        legacyCleanupFailures = $legacyCleanupFailures.ToArray()
        installRootDataMigration = $script:installRootDataMigration
        staleShortcutsRemoved = $staleShortcutRemovals.ToArray()
        emptyInstallDirectoriesRemoved = $installCleanupRemovals.ToArray()
    }
    installedVersion = if ($installVersion) {
        [ordered]@{
            productVersion = $installVersion.ProductVersion
            fileVersion = $installVersion.FileVersion
            exePath = $installedExePath
            sizeMb = Get-FileSizeMegabytes -Path $installedExePath
        }
    }
    validation = [ordered]@{
        exeExists = Test-Path -LiteralPath $installedExePath
        launcherExists = Test-Path -LiteralPath $launcherPath
        workstationIndexExists = Test-Path -LiteralPath (Join-Path $targetBundle "index.html")
        configExists = Test-Path -LiteralPath $configPath
    }
}

Invoke-Step -Name "Write install manifest" -Action {
    Write-JsonFile -Path $installManifestPath -InputObject $installManifest
}

Write-Host ""
Write-Success "Meridian Web Workstation installed."
Write-Host "  Launch script:   $launcherPath"
Write-Host "  Workstation URL: http://localhost:$Port/workstation/"
Write-Host "  Backup snapshot: $($script:backupResult.path)"
Write-Host "  Install report:  $installManifestPath"
if ($archivedLegacyInstallRoots.Count -gt 0) {
    Write-Host "  Archived installs: $($archivedLegacyInstallRoots.Count)"
}
if ($archivedLegacyAppDataRoots.Count -gt 0) {
    Write-Host "  Archived app data: $($archivedLegacyAppDataRoots.Count)"
}
if (-not $NoDesktopShortcut) {
    Write-Host "  Desktop link:    $(if ($desktopShortcutPath) { $desktopShortcutPath } else { '[skipped]' })"
}
if (-not $NoStartMenuShortcut) {
    Write-Host "  Start Menu link: $startMenuShortcutPath"
}
Write-Host ""

if ($LaunchAfterInstall) {
    Write-Info "Launching Meridian Web Workstation..."
    & (Get-PowerShellShortcutTarget) -NoProfile -ExecutionPolicy Bypass -File $launcherPath -Port $Port
}
