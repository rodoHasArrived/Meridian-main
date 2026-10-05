#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates, restores, or drills an encrypted Meridian production backup.

.DESCRIPTION
    The supported local-workstation topology has two durable roots: its dedicated
    PostgreSQL database and the configured Meridian data root. This script treats
    them as one recovery unit, encrypts both before publishing a backup, verifies
    plaintext and ciphertext integrity, and emits a machine-readable receipt.

    Restore is fail-closed. A non-empty data root is never overwritten unless
    -AllowDataOverwrite is supplied, in which case it is moved to a timestamped
    sibling quarantine directory. A database is never restored unless
    -AllowDatabaseOverwrite is supplied.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("Backup", "Restore", "Drill")]
    [string]$Mode,

    [Parameter(Mandatory)]
    [string]$ConnectionString,

    [Parameter(Mandatory)]
    [string]$DataRoot,

    [Parameter(Mandatory)]
    [string]$BackupRoot,

    [string]$BackupPath,

    [string]$LastVerifiedRecoverablePointAtUtc,

    [string]$RecoverablePointEvidence,

    [string]$SourceCommit = $env:GITHUB_SHA,

    [string]$RestoreConnectionString,

    [string]$RestoreDataRoot,

    [string]$EncryptionKeyBase64 = $env:MDC_RECOVERY_ENCRYPTION_KEY_BASE64,

    [ValidateRange(1, 3650)]
    [int]$RetentionDays = 35,

    [ValidateRange(1, 86400)]
    [int]$MaximumRpoSeconds = 3600,

    [ValidateRange(1, 86400)]
    [int]$MaximumRtoSeconds = 7200,

    [switch]$AllowDatabaseOverwrite,

    [switch]$AllowDataOverwrite,

    [string]$PgDumpPath = "pg_dump",

    [string]$PgRestorePath = "pg_restore",

    [string]$PsqlPath = "psql",

    [string]$ReceiptPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "recovery-evidence.ps1")

function Resolve-FullPath([string]$Value) {
    return [IO.Path]::GetFullPath($Value)
}

function Resolve-RecoveryLocation([string]$Value, [int]$LinkDepth = 0) {
    if ($IsWindows -and $Value -match '^(\\\\[?.]\\|\\\?\?\\)') {
        throw "Recovery receipt path checks require ordinary drive or UNC paths, not device namespace paths: $Value"
    }
    $resolved = [IO.Path]::GetPathRoot($Value)
    # Resolve existing links one component at a time, retaining any not-yet-created
    # suffix. Lexical normalization alone misses receipt parents aliased into a root.
    $parts = $Value.Substring($resolved.Length).Split(
        [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
        [StringSplitOptions]::RemoveEmptyEntries)
    foreach ($part in $parts) {
        if ($part -eq '.') { continue }
        if ($part -eq '..') {
            $parent = [IO.Directory]::GetParent($resolved)
            if ($null -ne $parent) { $resolved = $parent.FullName }
            continue
        }
        $parentPath = $resolved
        $resolved = Join-Path $resolved $part
        $directory = [IO.DirectoryInfo]::new($resolved)
        if ($null -ne $directory.LinkTarget) {
            if ($LinkDepth -ge 40) { throw "Recovery path contains too many symbolic links: $Value" }
            # Walk the raw target: normalizing '..' before following its preceding
            # link would resolve a different location from the filesystem.
            $target = $directory.LinkTarget
            if (-not [IO.Path]::IsPathFullyQualified($target)) {
                if ([IO.Path]::IsPathRooted($target)) { throw "Recovery path contains an unsupported link target: $target" }
                $target = Join-Path $parentPath $target
            }
            $resolved = Resolve-RecoveryLocation $target ($LinkDepth + 1)
        }
    }
    return [IO.Path]::TrimEndingDirectorySeparator($resolved)
}

function Test-RecoveryPathWithin([string]$Path, [string]$Root) {
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $prefix = $Root
    if (-not [IO.Path]::EndsInDirectorySeparator($prefix)) { $prefix += [IO.Path]::DirectorySeparatorChar }
    return $Path.Equals($Root, $comparison) -or $Path.StartsWith($prefix, $comparison)
}

function Assert-RecoveryReceiptLocation([string]$Path) {
    $location = Resolve-RecoveryLocation (Resolve-FullPath $Path)
    foreach ($root in @($DataRoot, $RestoreDataRoot)) {
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $rootLocation = Resolve-RecoveryLocation (Resolve-FullPath $root)
        # A receipt cannot be captured/quarantined with a recovery root, nor can
        # creating it as a file prevent a missing root from becoming a directory.
        if ((Test-RecoveryPathWithin $location $rootLocation) -or (Test-RecoveryPathWithin $rootLocation $location)) {
            throw "ReceiptPath must not overlap DataRoot or RestoreDataRoot: $Path"
        }
    }
}

function Get-Sha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-RecoveryKey {
    if ([string]::IsNullOrWhiteSpace($EncryptionKeyBase64)) {
        throw "MDC_RECOVERY_ENCRYPTION_KEY_BASE64 (or -EncryptionKeyBase64) is required."
    }

    try {
        $key = [Convert]::FromBase64String($EncryptionKeyBase64)
    }
    catch {
        throw "The recovery encryption key is not valid Base64."
    }

    if ($key.Length -ne 32) {
        throw "The recovery encryption key must decode to exactly 32 bytes."
    }

    return ,$key
}

function Derive-Key([byte[]]$RootKey, [byte[]]$Salt, [string]$Purpose) {
    $hmac = [Security.Cryptography.HMACSHA256]::new($RootKey)
    try {
        $purposeBytes = [Text.Encoding]::UTF8.GetBytes("meridian-recovery-v1:$Purpose")
        $input = [byte[]]::new($Salt.Length + $purposeBytes.Length)
        [Array]::Copy($Salt, 0, $input, 0, $Salt.Length)
        [Array]::Copy($purposeBytes, 0, $input, $Salt.Length, $purposeBytes.Length)
        return ,$hmac.ComputeHash($input)
    }
    finally {
        $hmac.Dispose()
    }
}

function Add-HmacInput([Security.Cryptography.HMAC]$Hmac, [byte[]]$Buffer, [int]$Count) {
    [void]$Hmac.TransformBlock($Buffer, 0, $Count, $Buffer, 0)
}

function Test-FixedTimeEqual([byte[]]$Left, [byte[]]$Right) {
    if ($Left.Length -ne $Right.Length) { return $false }
    $difference = 0
    for ($index = 0; $index -lt $Left.Length; $index++) {
        $difference = $difference -bor ($Left[$index] -bxor $Right[$index])
    }
    return $difference -eq 0
}

function Write-AuthenticatedManifest([System.Collections.IDictionary]$Manifest, [string]$Directory, [byte[]]$RootKey) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Manifest | ConvertTo-Json -Depth 8))
    $salt = [byte[]]::new(16)
    [Security.Cryptography.RandomNumberGenerator]::Fill($salt)
    $authenticationKey = Derive-Key $RootKey $salt "manifest-authentication"
    $hmac = [Security.Cryptography.HMACSHA256]::new($authenticationKey)
    try {
        $tag = $hmac.ComputeHash($bytes)
        [IO.File]::WriteAllBytes((Join-Path $Directory "manifest.json"), $bytes)
        [IO.File]::WriteAllBytes((Join-Path $Directory "manifest.hmac"), [byte[]]($salt + $tag))
    }
    finally {
        $hmac.Dispose()
        [Array]::Clear($authenticationKey, 0, $authenticationKey.Length)
    }
}

function Read-AuthenticatedManifest([string]$Directory, [byte[]]$RootKey) {
    $manifestPath = Join-Path $Directory "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Backup manifest is missing: $manifestPath" }
    # Parse the same bytes that are authenticated, never a second filesystem read.
    $bytes = [IO.File]::ReadAllBytes($manifestPath)
    $document = [System.Text.Json.JsonDocument]::Parse([Text.Encoding]::UTF8.GetString($bytes))
    try {
        if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
            throw "Backup manifest must be a JSON object."
        }
        $manifest = ConvertFrom-RecoveryJsonElement $document.RootElement
    }
    finally { $document.Dispose() }
    if (-not $manifest.Contains("schemaVersion") -or -not (Test-RecoveryNumber $manifest.schemaVersion) -or
        $manifest.schemaVersion -notin @(1, 2)) {
        throw "Backup manifest requires a supported schemaVersion."
    }
    $authenticated = $false
    $manifestHash = $null
    if ($manifest.schemaVersion -eq 2) {
        $tagPath = Join-Path $Directory "manifest.hmac"
        if (-not (Test-Path -LiteralPath $tagPath -PathType Leaf)) { throw "Backup manifest authentication is missing." }
        $authentication = [IO.File]::ReadAllBytes($tagPath)
        if ($authentication.Length -ne 48) { throw "Backup manifest authentication is invalid." }
        $salt = [byte[]]$authentication[0..15]
        $expectedTag = [byte[]]$authentication[16..47]
        $authenticationKey = Derive-Key $RootKey $salt "manifest-authentication"
        $hmac = [Security.Cryptography.HMACSHA256]::new($authenticationKey)
        try {
            if (-not (Test-FixedTimeEqual ($hmac.ComputeHash($bytes)) $expectedTag)) {
                throw "Backup manifest authentication failed."
            }
        }
        finally {
            $hmac.Dispose()
            [Array]::Clear($authenticationKey, 0, $authenticationKey.Length)
        }
        if (-not (Test-RecoveryCommit $manifest['sourceCommit'])) {
            throw "Authenticated backup manifest requires its original sourceCommit."
        }
        $authenticated = $true
        $manifestHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    }
    return [ordered]@{ manifest = $manifest; authenticated = $authenticated; sha256 = $manifestHash }
}

function Protect-RecoveryFile([string]$InputPath, [string]$OutputPath, [byte[]]$RootKey) {
    $salt = [byte[]]::new(16)
    $iv = [byte[]]::new(16)
    [Security.Cryptography.RandomNumberGenerator]::Fill($salt)
    [Security.Cryptography.RandomNumberGenerator]::Fill($iv)
    $encryptionKey = Derive-Key $RootKey $salt "encryption"
    $authenticationKey = Derive-Key $RootKey $salt "authentication"

    $input = [IO.File]::OpenRead($InputPath)
    $output = [IO.File]::Create($OutputPath)
    $aes = [Security.Cryptography.Aes]::Create()
    $aes.Key = $encryptionKey
    $aes.IV = $iv
    $aes.Mode = [Security.Cryptography.CipherMode]::CBC
    $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
    try {
        $output.Write($salt, 0, $salt.Length)
        $output.Write($iv, 0, $iv.Length)
        $crypto = [Security.Cryptography.CryptoStream]::new(
            $output,
            $aes.CreateEncryptor(),
            [Security.Cryptography.CryptoStreamMode]::Write,
            $true)
        try {
            $input.CopyTo($crypto)
            $crypto.FlushFinalBlock()
        }
        finally {
            $crypto.Dispose()
        }
    }
    finally {
        $input.Dispose()
        $output.Dispose()
        $aes.Dispose()
        [Array]::Clear($encryptionKey, 0, $encryptionKey.Length)
    }

    $hmac = [Security.Cryptography.HMACSHA256]::new($authenticationKey)
    $ciphertext = [IO.File]::OpenRead($OutputPath)
    try {
        $buffer = [byte[]]::new(1MB)
        while (($read = $ciphertext.Read($buffer, 0, $buffer.Length)) -gt 0) {
            Add-HmacInput $hmac $buffer $read
        }
        [void]$hmac.TransformFinalBlock([byte[]]::new(0), 0, 0)
        $tag = $hmac.Hash
    }
    finally {
        $ciphertext.Dispose()
        $hmac.Dispose()
        [Array]::Clear($authenticationKey, 0, $authenticationKey.Length)
    }
    $append = [IO.File]::Open($OutputPath, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $append.Write($tag, 0, $tag.Length) } finally { $append.Dispose() }
}

function Unprotect-RecoveryFile([string]$InputPath, [string]$OutputPath, [byte[]]$RootKey) {
    $length = (Get-Item -LiteralPath $InputPath).Length
    if ($length -lt 65) { throw "Encrypted recovery file is truncated: $InputPath" }
    $stream = [IO.File]::OpenRead($InputPath)
    try {
        $salt = [byte[]]::new(16)
        $iv = [byte[]]::new(16)
        $stream.ReadExactly($salt, 0, $salt.Length)
        $stream.ReadExactly($iv, 0, $iv.Length)
        $authenticatedLength = $length - 32
        $authenticationKey = Derive-Key $RootKey $salt "authentication"
        $hmac = [Security.Cryptography.HMACSHA256]::new($authenticationKey)
        try {
            $stream.Position = 0
            $remaining = $authenticatedLength
            $buffer = [byte[]]::new(1MB)
            while ($remaining -gt 0) {
                $read = $stream.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))
                if ($read -le 0) { throw "Encrypted recovery file ended before its authentication tag." }
                Add-HmacInput $hmac $buffer $read
                $remaining -= $read
            }
            [void]$hmac.TransformFinalBlock([byte[]]::new(0), 0, 0)
            $actualTag = $hmac.Hash
            $expectedTag = [byte[]]::new(32)
            $stream.ReadExactly($expectedTag, 0, $expectedTag.Length)
            if (-not (Test-FixedTimeEqual $actualTag $expectedTag)) {
                throw "Encrypted recovery file authentication failed: $InputPath"
            }
        }
        finally {
            $hmac.Dispose()
            [Array]::Clear($authenticationKey, 0, $authenticationKey.Length)
        }

        $encryptionKey = Derive-Key $RootKey $salt "encryption"
        $aes = [Security.Cryptography.Aes]::Create()
        $aes.Key = $encryptionKey
        $aes.IV = $iv
        $aes.Mode = [Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [Security.Cryptography.PaddingMode]::PKCS7
        $stream.Position = 32
        $bounded = [MeridianRecoveryBoundedStream]::new($stream, $authenticatedLength - 32)
        $crypto = [Security.Cryptography.CryptoStream]::new($bounded, $aes.CreateDecryptor(), [Security.Cryptography.CryptoStreamMode]::Read)
        $output = [IO.File]::Create($OutputPath)
        try { $crypto.CopyTo($output) }
        finally {
            $output.Dispose()
            $crypto.Dispose()
            $bounded.Dispose()
            $aes.Dispose()
            [Array]::Clear($encryptionKey, 0, $encryptionKey.Length)
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-ConnectionParts([string]$Value) {
    $builder = [Data.Common.DbConnectionStringBuilder]::new()
    # PowerShell adapts DbConnectionStringBuilder as a dictionary, so a property-style assignment
    # would store one literal "ConnectionString" entry instead of parsing Host/Database/Username.
    # Invoking the CLR setter keeps the parsed key/value semantics.
    $builder.set_ConnectionString($Value)
    function Value-For([string[]]$Names, [string]$Default = "") {
        foreach ($name in $Names) {
            if ($builder.ContainsKey($name)) { return [string]$builder[$name] }
        }
        return $Default
    }
    return [ordered]@{
        Host = Value-For @("Host", "Server") "localhost"
        Port = Value-For @("Port") "5432"
        Database = Value-For @("Database", "Initial Catalog")
        Username = Value-For @("Username", "User ID", "UserId")
        Password = Value-For @("Password", "Pwd")
    }
}

function Invoke-PostgresTool([string]$Tool, [string[]]$Arguments, [string]$Password) {
    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $Tool
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.Environment["PGPASSWORD"] = $Password
    foreach ($argument in $Arguments) { [void]$psi.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $output = $stdout.GetAwaiter().GetResult()
    $errorOutput = $stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
        throw "$Tool failed with exit code $($process.ExitCode): $errorOutput"
    }
    return $output
}

function Get-PgArguments([System.Collections.IDictionary]$Parts) {
    if ([string]::IsNullOrWhiteSpace($Parts.Database) -or [string]::IsNullOrWhiteSpace($Parts.Username)) {
        throw "PostgreSQL connection string must include Database and Username/User ID."
    }
    return @("--host", $Parts.Host, "--port", $Parts.Port, "--username", $Parts.Username, "--dbname", $Parts.Database)
}

function Copy-BackupEvidence([System.Collections.IDictionary]$VerifiedArchive, [string]$SelectedBackup) {
    $Manifest = $VerifiedArchive.manifest
    $receipt.backupPath = Resolve-FullPath $SelectedBackup
    $receipt.backupId = $Manifest.backupId
    # The execution checkout is drillSourceCommit; it must never relabel retained data.
    $receipt.sourceCommit = if ($Manifest.Contains("sourceCommit")) { $Manifest.sourceCommit } else { $null }
    $receipt.manifestAuthenticated = $VerifiedArchive.manifestAuthenticated
    $receipt.manifestSha256 = $VerifiedArchive.manifestSha256
    if ($VerifiedArchive.manifestAuthenticated) {
        foreach ($field in @("backupStartedAtUtc", "backupCompletedAtUtc", "backupDurationSeconds",
                "lastVerifiedRecoverablePointAtUtc", "recoverablePointVerifiedAtUtc", "recoverablePointEvidence")) {
            if ($Manifest.Contains($field)) { $receipt[$field] = $Manifest[$field] }
        }
    }
    else {
        # Legacy archive timings remain useful, but do not identify a recoverable checkpoint.
        if ($Manifest.Contains("createdAtUtc")) { $receipt.backupCompletedAtUtc = $Manifest.createdAtUtc }
        if ($Manifest.Contains("durationSeconds")) { $receipt.backupDurationSeconds = $Manifest.durationSeconds }
    }
}

function Set-VerifiedRecoverablePoint {
    if (-not [string]::IsNullOrWhiteSpace([string]$receipt.lastVerifiedRecoverablePointAtUtc) -and
        -not [string]::IsNullOrWhiteSpace([string]$receipt.recoverablePointEvidence)) {
        $receipt.recoverablePointVerifiedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
}

function Get-VerifiedRecoveryArchive([string]$SelectedBackup, [byte[]]$Key) {
    $selected = Resolve-FullPath $SelectedBackup
    $verifiedManifest = Read-AuthenticatedManifest $selected $Key
    $manifest = $verifiedManifest.manifest
    if (-not $manifest.Contains("backupId") -or [string]::IsNullOrWhiteSpace([string]$manifest.backupId)) {
        throw "Backup manifest requires a backupId."
    }
    foreach ($kind in @("database", "dataRoot")) {
        if (-not $manifest.Contains($kind) -or $manifest[$kind] -isnot [System.Collections.IDictionary]) {
            throw "Backup manifest is missing its $kind archive description."
        }
        $entry = $manifest[$kind]
        foreach ($field in @("archive", "encryptedSha256", "plaintextSha256")) {
            if (-not $entry.Contains($field) -or [string]::IsNullOrWhiteSpace([string]$entry[$field])) {
                throw "Backup manifest is missing $kind.$field."
            }
        }
        $archive = [string]$entry.archive
        if ([IO.Path]::IsPathRooted($archive) -or $archive -match '[\\/]' -or $archive -in @(".", "..")) {
            throw "Backup archive must be a file inside the selected backup: $archive"
        }
        if ($entry.encryptedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or $entry.plaintextSha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw "Backup manifest contains an invalid $kind checksum."
        }
        $archivePath = Join-Path $selected $archive
        if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { throw "Backup archive is missing: $archive" }
        if ((Get-Sha256 $archivePath) -ne $entry.encryptedSha256) { throw "Encrypted backup checksum verification failed." }
    }
    $validationStage = Join-Path ([IO.Path]::GetTempPath()) "meridian-recovery-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($validationStage) | Out-Null
    try {
        $databaseDump = Join-Path $validationStage "database.dump"
        $dataArchive = Join-Path $validationStage "data-root.zip"
        Unprotect-RecoveryFile (Join-Path $selected $manifest.database.archive) $databaseDump $Key
        Unprotect-RecoveryFile (Join-Path $selected $manifest.dataRoot.archive) $dataArchive $Key
        if ((Get-Sha256 $databaseDump) -ne $manifest.database.plaintextSha256 -or
            (Get-Sha256 $dataArchive) -ne $manifest.dataRoot.plaintextSha256) {
            throw "Decrypted backup checksum verification failed."
        }
        return [ordered]@{
            manifest = $manifest
            manifestAuthenticated = $verifiedManifest.authenticated
            manifestSha256 = $verifiedManifest.sha256
            databaseDump = $databaseDump
            dataArchive = $dataArchive
            stagePath = $validationStage
        }
    }
    catch {
        if (Test-Path -LiteralPath $validationStage) { Remove-Item -LiteralPath $validationStage -Recurse -Force }
        throw
    }
}

function Remove-VerifiedRecoveryArchive([System.Collections.IDictionary]$VerifiedArchive) {
    if ($null -ne $VerifiedArchive -and (Test-Path -LiteralPath $VerifiedArchive.stagePath)) {
        Remove-Item -LiteralPath $VerifiedArchive.stagePath -Recurse -Force
    }
}

function Invoke-Backup([string]$SourceConnection, [string]$SourceDataRoot, [byte[]]$Key) {
    $started = [DateTimeOffset]::UtcNow
    $receipt.backupStartedAtUtc = $started.ToString("o")
    $receipt.lastVerifiedRecoverablePointAtUtc = $LastVerifiedRecoverablePointAtUtc
    $receipt.recoverablePointEvidence = $RecoverablePointEvidence
    $stage = $null
    $verifiedArchive = $null
    try {
        $sourceRoot = Resolve-FullPath $SourceDataRoot
        $backupRootFull = Resolve-FullPath $BackupRoot
        if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) { throw "Data root does not exist: $sourceRoot" }
        if ($backupRootFull -eq $sourceRoot -or
            $backupRootFull.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "BackupRoot must be outside DataRoot to prevent recursive backup capture."
        }
        [IO.Directory]::CreateDirectory($backupRootFull) | Out-Null
        $stamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
        $final = Join-Path $backupRootFull "backup-$stamp"
        $stage = Join-Path $backupRootFull ".backup-$stamp-$([Guid]::NewGuid().ToString('N'))"
        [IO.Directory]::CreateDirectory($stage) | Out-Null
        $databaseDump = Join-Path $stage "database.dump"
        $dataArchive = Join-Path $stage "data-root.zip"
        $parts = Get-ConnectionParts $SourceConnection
        $pgArgs = (Get-PgArguments $parts) + @("--format", "custom", "--no-owner", "--no-privileges", "--file", $databaseDump)
        [void](Invoke-PostgresTool $PgDumpPath $pgArgs $parts.Password)
        [IO.Compression.ZipFile]::CreateFromDirectory($sourceRoot, $dataArchive, [IO.Compression.CompressionLevel]::Optimal, $false)
        $databaseHash = Get-Sha256 $databaseDump
        $dataHash = Get-Sha256 $dataArchive
        Protect-RecoveryFile $databaseDump "$databaseDump.enc" $Key
        Protect-RecoveryFile $dataArchive "$dataArchive.enc" $Key
        Remove-Item -LiteralPath $databaseDump, $dataArchive -Force
        $manifest = [ordered]@{
            schemaVersion = 2
            backupId = "backup-$stamp"
            backupStartedAtUtc = $receipt.backupStartedAtUtc
            backupCompletedAtUtc = $null
            backupDurationSeconds = $null
            lastVerifiedRecoverablePointAtUtc = $LastVerifiedRecoverablePointAtUtc
            recoverablePointVerifiedAtUtc = $null
            recoverablePointEvidence = $RecoverablePointEvidence
            sourceCommit = $SourceCommit
            database = [ordered]@{ archive = "database.dump.enc"; plaintextSha256 = $databaseHash; encryptedSha256 = Get-Sha256 "$databaseDump.enc" }
            dataRoot = [ordered]@{ archive = "data-root.zip.enc"; plaintextSha256 = $dataHash; encryptedSha256 = Get-Sha256 "$dataArchive.enc" }
            encryption = "AES-256-CBC-HMAC-SHA256; independent per-file keys"
        }
        Write-AuthenticatedManifest $manifest $stage $Key
        $verifiedArchive = Get-VerifiedRecoveryArchive $stage $Key
        Remove-VerifiedRecoveryArchive $verifiedArchive
        $verifiedArchive = $null
        # Measure archive creation and integrity validation; publication and retention follow.
        $completed = [DateTimeOffset]::UtcNow
        $receipt.backupCompletedAtUtc = $completed.ToString("o")
        $receipt.backupDurationSeconds = [Math]::Round(($completed - $started).TotalSeconds, 3)
        Set-VerifiedRecoverablePoint
        $manifest.backupCompletedAtUtc = $receipt.backupCompletedAtUtc
        $manifest.backupDurationSeconds = $receipt.backupDurationSeconds
        $manifest.recoverablePointVerifiedAtUtc = $receipt.recoverablePointVerifiedAtUtc
        Write-AuthenticatedManifest $manifest $stage $Key
        # Recheck the final signed bytes before publishing their evidence.
        $verifiedArchive = Get-VerifiedRecoveryArchive $stage $Key
        Move-Item -LiteralPath $stage -Destination $final
        Copy-BackupEvidence $verifiedArchive $final
        $cutoff = [DateTimeOffset]::UtcNow.AddDays(-$RetentionDays)
        Get-ChildItem -LiteralPath $backupRootFull -Directory -Filter "backup-*" |
            Where-Object { $_.LastWriteTimeUtc -lt $cutoff.UtcDateTime -and $_.FullName -ne $final } |
            Remove-Item -Recurse -Force
    }
    catch {
        if ($null -ne $stage -and (Test-Path -LiteralPath $stage)) { Remove-Item -LiteralPath $stage -Recurse -Force }
        throw
    }
    finally {
        Remove-VerifiedRecoveryArchive $verifiedArchive
        if ($null -eq $receipt.backupCompletedAtUtc) {
            $completed = [DateTimeOffset]::UtcNow
            $receipt.backupCompletedAtUtc = $completed.ToString("o")
            $receipt.backupDurationSeconds = [Math]::Round(($completed - $started).TotalSeconds, 3)
        }
    }
    return $final
}

function Invoke-Restore([string]$SelectedBackup, [string]$TargetConnection, [string]$TargetDataRoot, [byte[]]$Key) {
    $started = [DateTimeOffset]::UtcNow
    $receipt.restoreStartedAtUtc = $started.ToString("o")
    $verifiedArchive = $null
    try {
        if (-not $AllowDatabaseOverwrite) { throw "Restore requires -AllowDatabaseOverwrite and a dedicated recovery target database." }
        # Repeat full integrity validation immediately before using the archive at the target.
        $verifiedArchive = Get-VerifiedRecoveryArchive $SelectedBackup $Key
        $manifest = $verifiedArchive.manifest
        if ($Mode -eq "Drill") {
            if ($receipt.backupId -ne $manifest.backupId -or $receipt.manifestSha256 -ne $verifiedArchive.manifestSha256) {
                throw "Backup manifest changed after the drill established its recovery evidence."
            }
        }
        else { Copy-BackupEvidence $verifiedArchive $SelectedBackup }
        $targetRoot = Resolve-FullPath $TargetDataRoot
        if (Test-Path -LiteralPath $targetRoot) {
            # The array subexpression gives an empty directory a real Count under StrictMode.
            $existing = @(Get-ChildItem -LiteralPath $targetRoot -Force)
            if ($existing.Count -gt 0) {
                if (-not $AllowDataOverwrite) { throw "Restore data root is not empty: $targetRoot" }
                $quarantine = "$targetRoot.pre-restore-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
                Move-Item -LiteralPath $targetRoot -Destination $quarantine
            }
        }
        [IO.Directory]::CreateDirectory($targetRoot) | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($verifiedArchive.dataArchive, $targetRoot, $false)
        $parts = Get-ConnectionParts $TargetConnection
        $restoreArgs = (Get-PgArguments $parts) + @("--clean", "--if-exists", "--no-owner", "--no-privileges", $verifiedArchive.databaseDump)
        [void](Invoke-PostgresTool $PgRestorePath $restoreArgs $parts.Password)
        [void](Invoke-PostgresTool $PsqlPath ((Get-PgArguments $parts) + @("--tuples-only", "--command", "SELECT 1")) $parts.Password)
        return $manifest
    }
    finally {
        Remove-VerifiedRecoveryArchive $verifiedArchive
        $completed = [DateTimeOffset]::UtcNow
        $receipt.restoreCompletedAtUtc = $completed.ToString("o")
        $receipt.restoreDurationSeconds = [Math]::Round(($completed - $started).TotalSeconds, 3)
    }
}

# Validate explicit and default receipt locations before creating any files or
# directories. DataRoot also covers Restore's default target when none is supplied.
if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
    $receiptDirectory = Resolve-FullPath $BackupRoot
    $receiptName = "recovery-$($Mode.ToLowerInvariant())-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))-$([Guid]::NewGuid().ToString('N'))-receipt.json"
    $ReceiptPath = Join-Path $receiptDirectory $receiptName
}
else { $ReceiptPath = Resolve-FullPath $ReceiptPath }
Assert-RecoveryReceiptLocation $ReceiptPath
if (Test-Path -LiteralPath $ReceiptPath) { throw "Recovery receipt already exists; choose a new -ReceiptPath: $ReceiptPath" }

# CryptoStream must not read the trailing HMAC tag as ciphertext.
Add-Type -TypeDefinition @"
using System;
using System.IO;
public sealed class MeridianRecoveryBoundedStream : Stream
{
    private readonly Stream inner; private long remaining;
    public MeridianRecoveryBoundedStream(Stream inner, long length) { this.inner = inner; remaining = length; }
    public override int Read(byte[] buffer, int offset, int count) { if (remaining <= 0) return 0; int read = inner.Read(buffer, offset, (int)Math.Min(count, remaining)); remaining -= read; return read; }
    public override int Read(Span<byte> buffer) { if (remaining <= 0) return 0; int read = inner.Read(buffer.Slice(0, (int)Math.Min(buffer.Length, remaining))); remaining -= read; return read; }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { } public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}
"@

# Reserve the receipt before any backup or restore work. CreateNew also rejects a
# concurrent writer and existing symlinks without changing the earlier evidence.
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReceiptPath)) | Out-Null
$receiptStream = [IO.File]::Open($ReceiptPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
$operationStarted = [DateTimeOffset]::UtcNow
$key = $null
$operationError = $null
$receipt = [ordered]@{
    schemaVersion = 2
    mode = $Mode
    sourceCommit = $null
    drillSourceCommit = $SourceCommit
    manifestAuthenticated = $false
    manifestSha256 = $null
    startedAtUtc = $operationStarted.ToString("o")
    completedAtUtc = $null
    status = "failed"
    objectiveStatus = "unproven"
    backupPath = $null
    backupId = $null
    backupStartedAtUtc = $null
    backupCompletedAtUtc = $null
    backupDurationSeconds = $null
    restoreStartedAtUtc = $null
    restoreCompletedAtUtc = $null
    restoreDurationSeconds = $null
    lastVerifiedRecoverablePointAtUtc = $null
    recoverablePointVerifiedAtUtc = $null
    recoverablePointEvidence = $null
    simulatedLossAtUtc = $null
    lossDeclaredAtUtc = $null
    reconciliationCompletedAtUtc = $null
    reconciliationEvidence = $null
    operatorAcceptedAtUtc = $null
    operatorAcceptedBy = $null
    operatorAcceptanceEvidence = $null
    maximumRpoSeconds = $MaximumRpoSeconds
    maximumRtoSeconds = $MaximumRtoSeconds
    measuredRpoSeconds = $null
    measuredRtoSeconds = $null
    objectiveErrors = @()
}
try {
    if (-not $PSBoundParameters.ContainsKey('SourceCommit') -and
        [string]::IsNullOrWhiteSpace($SourceCommit) -and (Get-Command git -ErrorAction SilentlyContinue)) {
        $repositoryRoot = Resolve-FullPath (Join-Path $PSScriptRoot "../../..")
        $gitCommit = & git -C $repositoryRoot rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0) { $SourceCommit = [string]$gitCommit }
    }
    $receipt.drillSourceCommit = $SourceCommit
    if (-not (Test-RecoveryCommit $SourceCommit)) {
        throw "SourceCommit must be a full 40- or 64-character hexadecimal commit identifier."
    }
    # Key validation belongs inside the receipt lifecycle, so rejected keys leave a failure receipt.
    $key = Get-RecoveryKey
    switch ($Mode) {
        "Backup" {
            if (-not [string]::IsNullOrWhiteSpace($BackupPath)) { throw "Backup mode does not accept -BackupPath." }
            [void](Invoke-Backup $ConnectionString $DataRoot $key)
        }
        "Restore" {
            if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw "Restore mode requires -BackupPath." }
            if (-not [string]::IsNullOrWhiteSpace($LastVerifiedRecoverablePointAtUtc) -or
                -not [string]::IsNullOrWhiteSpace($RecoverablePointEvidence)) {
                throw "Recoverable-point assertions must be recorded when the backup is created."
            }
            $receipt.backupPath = Resolve-FullPath $BackupPath
            $targetConnection = if ([string]::IsNullOrWhiteSpace($RestoreConnectionString)) { $ConnectionString } else { $RestoreConnectionString }
            $targetData = if ([string]::IsNullOrWhiteSpace($RestoreDataRoot)) { $DataRoot } else { $RestoreDataRoot }
            [void](Invoke-Restore $BackupPath $targetConnection $targetData $key)
        }
        "Drill" {
            if ([string]::IsNullOrWhiteSpace($RestoreConnectionString) -or [string]::IsNullOrWhiteSpace($RestoreDataRoot)) {
                throw "Drill mode requires -RestoreConnectionString and -RestoreDataRoot."
            }
            if ([string]::IsNullOrWhiteSpace($BackupPath)) {
                $selectedBackup = Invoke-Backup $ConnectionString $DataRoot $key
            }
            else {
                if (-not [string]::IsNullOrWhiteSpace($LastVerifiedRecoverablePointAtUtc) -or
                    -not [string]::IsNullOrWhiteSpace($RecoverablePointEvidence)) {
                    throw "A retained backup's recoverable-point assertion must come from its original manifest."
                }
                $selectedBackup = Resolve-FullPath $BackupPath
                $receipt.backupPath = $selectedBackup
            }
            $verifiedArchive = $null
            try {
                # Confirm both archives and the checkpoint assertion before establishing simulated loss.
                $verifiedArchive = Get-VerifiedRecoveryArchive $selectedBackup $key
                Copy-BackupEvidence $verifiedArchive $selectedBackup
                Set-VerifiedRecoverablePoint
            }
            finally {
                Remove-VerifiedRecoveryArchive $verifiedArchive
            }
            # A controlled drill marks the boundary without deleting the source recovery unit.
            $receipt.simulatedLossAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            $receipt.lossDeclaredAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
            [void](Invoke-Restore $selectedBackup $RestoreConnectionString $RestoreDataRoot $key)
        }
    }
    $receipt.status = "passed"
}
catch {
    $operationError = $_.Exception
    $receipt.error = $operationError.Message
}
finally {
    if ($null -ne $key) { [Array]::Clear($key, 0, $key.Length) }
    try {
        $receipt.completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        $objective = Get-RecoveryObjectiveEvidence -Receipt $receipt
        foreach ($field in @("objectiveStatus", "measuredRpoSeconds", "measuredRtoSeconds", "objectiveErrors",
                "rpoStatus", "rtoStatus", "effectiveMaximumRpoSeconds", "effectiveMaximumRtoSeconds")) {
            $receipt[$field] = $objective[$field]
        }
        $receiptBytes = [Text.Encoding]::UTF8.GetBytes(($receipt | ConvertTo-Json -Depth 8))
        $receiptStream.Write($receiptBytes, 0, $receiptBytes.Length)
        $receiptStream.Flush($true)
    }
    finally { $receiptStream.Dispose() }
}

if ($null -ne $operationError) { throw $operationError }
if (($null -ne $receipt.measuredRpoSeconds -and $receipt.measuredRpoSeconds -gt $receipt.effectiveMaximumRpoSeconds) -or
    ($null -ne $receipt.measuredRtoSeconds -and $receipt.measuredRtoSeconds -gt $receipt.effectiveMaximumRtoSeconds)) {
    throw "Recovery objective budget breached. Recoverable-point age: $($receipt.measuredRpoSeconds) seconds; loss-to-acceptance: $($receipt.measuredRtoSeconds) seconds. Receipt: $ReceiptPath"
}
Write-Host "[recovery] $Mode archive operations passed; recovery objective $($receipt.objectiveStatus). Receipt: $ReceiptPath"
