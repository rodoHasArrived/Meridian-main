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

function Resolve-FullPath([string]$Value) {
    return [IO.Path]::GetFullPath($Value)
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

function Invoke-Backup([string]$SourceConnection, [string]$SourceDataRoot, [byte[]]$Key) {
    $sourceRoot = Resolve-FullPath $SourceDataRoot
    $backupRootFull = Resolve-FullPath $BackupRoot
    if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) { throw "Data root does not exist: $sourceRoot" }
    if ($backupRootFull.StartsWith($sourceRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "BackupRoot must be outside DataRoot to prevent recursive backup capture."
    }
    [IO.Directory]::CreateDirectory($backupRootFull) | Out-Null
    $stamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssZ")
    $final = Join-Path $backupRootFull "backup-$stamp"
    $stage = Join-Path $backupRootFull ".backup-$stamp-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $started = [DateTimeOffset]::UtcNow
    try {
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
        $completed = [DateTimeOffset]::UtcNow
        $manifest = [ordered]@{
            schemaVersion = 1
            backupId = "backup-$stamp"
            createdAtUtc = $completed
            sourceCommit = $env:GITHUB_SHA
            database = [ordered]@{ archive = "database.dump.enc"; plaintextSha256 = $databaseHash; encryptedSha256 = Get-Sha256 "$databaseDump.enc" }
            dataRoot = [ordered]@{ archive = "data-root.zip.enc"; plaintextSha256 = $dataHash; encryptedSha256 = Get-Sha256 "$dataArchive.enc" }
            encryption = "AES-256-CBC-HMAC-SHA256; independent per-file keys"
            durationSeconds = [Math]::Round(($completed - $started).TotalSeconds, 3)
            maximumRpoSeconds = $MaximumRpoSeconds
        }
        $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stage "manifest.json") -Encoding utf8NoBOM
        Move-Item -LiteralPath $stage -Destination $final
        $cutoff = [DateTimeOffset]::UtcNow.AddDays(-$RetentionDays)
        Get-ChildItem -LiteralPath $backupRootFull -Directory -Filter "backup-*" |
            Where-Object { $_.LastWriteTimeUtc -lt $cutoff.UtcDateTime -and $_.FullName -ne $final } |
            Remove-Item -Recurse -Force
        return $final
    }
    catch {
        if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        throw
    }
}

function Invoke-Restore([string]$SelectedBackup, [string]$TargetConnection, [string]$TargetDataRoot, [byte[]]$Key) {
    if (-not $AllowDatabaseOverwrite) { throw "Restore requires -AllowDatabaseOverwrite and a dedicated recovery target database." }
    $selected = Resolve-FullPath $SelectedBackup
    $manifestPath = Join-Path $selected "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Backup manifest is missing: $manifestPath" }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    foreach ($archive in @($manifest.database.archive, $manifest.dataRoot.archive)) {
        if (-not (Test-Path -LiteralPath (Join-Path $selected $archive))) { throw "Backup archive is missing: $archive" }
    }
    if ((Get-Sha256 (Join-Path $selected $manifest.database.archive)) -ne $manifest.database.encryptedSha256 -or
        (Get-Sha256 (Join-Path $selected $manifest.dataRoot.archive)) -ne $manifest.dataRoot.encryptedSha256) {
        throw "Encrypted backup checksum verification failed."
    }
    $restoreStage = Join-Path ([IO.Path]::GetTempPath()) "meridian-recovery-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($restoreStage) | Out-Null
    try {
        $databaseDump = Join-Path $restoreStage "database.dump"
        $dataArchive = Join-Path $restoreStage "data-root.zip"
        Unprotect-RecoveryFile (Join-Path $selected $manifest.database.archive) $databaseDump $Key
        Unprotect-RecoveryFile (Join-Path $selected $manifest.dataRoot.archive) $dataArchive $Key
        if ((Get-Sha256 $databaseDump) -ne $manifest.database.plaintextSha256 -or (Get-Sha256 $dataArchive) -ne $manifest.dataRoot.plaintextSha256) {
            throw "Decrypted backup checksum verification failed."
        }
        $targetRoot = Resolve-FullPath $TargetDataRoot
        if (Test-Path -LiteralPath $targetRoot) {
            $existing = Get-ChildItem -LiteralPath $targetRoot -Force
            if ($existing.Count -gt 0) {
                if (-not $AllowDataOverwrite) { throw "Restore data root is not empty: $targetRoot" }
                $quarantine = "$targetRoot.pre-restore-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
                Move-Item -LiteralPath $targetRoot -Destination $quarantine
            }
        }
        [IO.Directory]::CreateDirectory($targetRoot) | Out-Null
        [IO.Compression.ZipFile]::ExtractToDirectory($dataArchive, $targetRoot, $false)
        $parts = Get-ConnectionParts $TargetConnection
        $restoreArgs = (Get-PgArguments $parts) + @("--clean", "--if-exists", "--no-owner", "--no-privileges", $databaseDump)
        [void](Invoke-PostgresTool $PgRestorePath $restoreArgs $parts.Password)
        [void](Invoke-PostgresTool $PsqlPath ((Get-PgArguments $parts) + @("--tuples-only", "--command", "SELECT 1")) $parts.Password)
        return $manifest
    }
    finally {
        if (Test-Path -LiteralPath $restoreStage) { Remove-Item -LiteralPath $restoreStage -Recurse -Force }
    }
}

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

$operationStarted = [DateTimeOffset]::UtcNow
$key = Get-RecoveryKey
$receipt = [ordered]@{ schemaVersion = 1; mode = $Mode; startedAtUtc = $operationStarted; status = "failed"; sourceCommit = $env:GITHUB_SHA }
try {
    switch ($Mode) {
        "Backup" {
            $created = Invoke-Backup $ConnectionString $DataRoot $key
            $receipt.backupPath = $created
        }
        "Restore" {
            if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw "Restore mode requires -BackupPath." }
            $targetConnection = if ([string]::IsNullOrWhiteSpace($RestoreConnectionString)) { $ConnectionString } else { $RestoreConnectionString }
            $targetData = if ([string]::IsNullOrWhiteSpace($RestoreDataRoot)) { $DataRoot } else { $RestoreDataRoot }
            $manifest = Invoke-Restore $BackupPath $targetConnection $targetData $key
            $receipt.backupId = $manifest.backupId
        }
        "Drill" {
            if ([string]::IsNullOrWhiteSpace($RestoreConnectionString) -or [string]::IsNullOrWhiteSpace($RestoreDataRoot)) {
                throw "Drill mode requires -RestoreConnectionString and -RestoreDataRoot."
            }
            $created = Invoke-Backup $ConnectionString $DataRoot $key
            $backupCompleted = [DateTimeOffset]::UtcNow
            $manifest = Invoke-Restore $created $RestoreConnectionString $RestoreDataRoot $key
            $restored = [DateTimeOffset]::UtcNow
            $rpoSeconds = [Math]::Round(($backupCompleted - $operationStarted).TotalSeconds, 3)
            $rtoSeconds = [Math]::Round(($restored - $backupCompleted).TotalSeconds, 3)
            if ($rpoSeconds -gt $MaximumRpoSeconds) { throw "Measured backup window $rpoSeconds seconds exceeds RPO objective $MaximumRpoSeconds seconds." }
            if ($rtoSeconds -gt $MaximumRtoSeconds) { throw "Measured restore time $rtoSeconds seconds exceeds RTO objective $MaximumRtoSeconds seconds." }
            $receipt.backupPath = $created
            $receipt.backupId = $manifest.backupId
            $receipt.measuredRpoSeconds = $rpoSeconds
            $receipt.measuredRtoSeconds = $rtoSeconds
        }
    }
    $receipt.status = "passed"
}
catch {
    $receipt.error = $_.Exception.Message
    throw
}
finally {
    [Array]::Clear($key, 0, $key.Length)
    $receipt.completedAtUtc = [DateTimeOffset]::UtcNow
    if ([string]::IsNullOrWhiteSpace($ReceiptPath)) {
        $receiptDirectory = Resolve-FullPath $BackupRoot
        [IO.Directory]::CreateDirectory($receiptDirectory) | Out-Null
        $ReceiptPath = Join-Path $receiptDirectory "recovery-$($Mode.ToLowerInvariant())-receipt.json"
    }
    $receipt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReceiptPath -Encoding utf8NoBOM
}

Write-Host "[recovery] $Mode passed. Receipt: $ReceiptPath"
