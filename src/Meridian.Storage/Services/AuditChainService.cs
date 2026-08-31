using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Logging;
using Meridian.Storage.Archival;
using Serilog;
using Meridian.Contracts.Integrity;

namespace Meridian.Storage.Services;

/// <summary>
/// Result of audit chain verification.
/// </summary>
public sealed class AuditChainVerifyResult
{
    /// <summary>
    /// True if the chain is valid (no tampering detected).
    /// </summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    /// <summary>
    /// Number of entries checked in the chain.
    /// </summary>
    [JsonPropertyName("entriesChecked")]
    public int EntriesChecked { get; set; }

    /// <summary>
    /// Path of the first file detected with tampering, if any.
    /// </summary>
    [JsonPropertyName("firstTamperPath")]
    public string? FirstTamperPath { get; set; }

    /// <summary>
    /// Timestamp when tampering was detected.
    /// </summary>
    [JsonPropertyName("tamperedAt")]
    public DateTimeOffset? TamperedAt { get; set; }
}

/// <summary>
/// Interface for audit chain service.
/// </summary>
public interface IAuditChainService
{
    /// <summary>
    /// Append a file entry to the audit chain with SHA256 hash-chaining.
    /// </summary>
    Task AppendEntryAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Verify the integrity of the audit chain.
    /// </summary>
    Task<AuditChainVerifyResult> VerifyChainAsync(string chainLogPath, CancellationToken ct = default);
}

/// <summary>
/// SHA-256 hash-chaining audit service for compliance-grade tamper detection.
/// Stores one JSON line per entry with forward hash-chaining: each entry contains
/// the SHA256 hash of the previous entry's content, creating an immutable chain.
/// </summary>
public sealed class AuditChainService : IAuditChainService
{
    private readonly ILogger _log;

    // The append sequence (read tail hash → compute chained hash → append line) must be atomic.
    // Without this, two concurrent appends read the same predecessor hash and chain off it,
    // silently forking the tamper-evident chain so VerifyChainAsync later reports tampering.
    // ImmutableAuditLogService guards the same race with a lock; this async path needs a
    // SemaphoreSlim because the sequence spans awaits (file hashing and chain I/O).
    //
    private readonly SemaphoreSlim _appendLock = new(1, 1);
    private static readonly TimeSpan CrossProcessLockTimeout = TimeSpan.FromSeconds(30);

    public AuditChainService(ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<AuditChainService>();
    }

    /// <summary>
    /// Append a file entry to the audit chain. The file is hashed and a new entry is added
    /// with the previous entry's hash for chain integrity.
    /// </summary>
    public async Task AppendEntryAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var chainLogPath = GetChainLogPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(chainLogPath)!);

        _log.Debug("Appending audit entry for {FilePath}", filePath);

        // Compute SHA256 hash of the file. This is independent of chain state, so it is done
        // before taking the append lock to keep the serialized critical section short.
        string fileHash;
        using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var fileHashBytes = await Sha256Digest.ComputeBytesAsync(fileStream, ct).ConfigureAwait(false);
            fileHash = Convert.ToHexString(fileHashBytes).ToLowerInvariant();
        }

        // Serialize the read-tail-hash → compute-chained-hash → append sequence. Two concurrent
        // callers would otherwise read the same predecessor hash and both chain off it, silently
        // forking the tamper-evident chain.
        await _appendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var crossProcessLock = await AcquireCrossProcessLockAsync(chainLogPath, ct).ConfigureAwait(false);
            // Read the previous hash from the last entry in the chain
            var previousHash = "";
            if (File.Exists(chainLogPath))
            {
                try
                {
                    // Only the last entry's hash is needed to chain the next one; stream the file
                    // and keep just the final non-empty line so memory stays bounded as the log grows.
                    string? lastLine = null;
                    using (var reader = new StreamReader(chainLogPath))
                    {
                        string? line;
                        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                lastLine = line;
                            }
                        }
                    }

                    if (lastLine is not null)
                    {
                        using var doc = JsonDocument.Parse(lastLine);
                        if (doc.RootElement.TryGetProperty("hash"u8, out var hashElement))
                        {
                            previousHash = hashElement.GetString() ?? "";
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to read previous hash from chain log at {ChainLogPath}", chainLogPath);
                }
            }

            // Create the chain entry: hash(filePath || fileHash || previousHash)
            var entryData = $"{filePath}{fileHash}{previousHash}";
            var entryHash = Sha256Digest.ComputeUtf8(entryData);

            var entry = new
            {
                path = filePath,
                fileHash,
                hash = entryHash,
                prev = previousHash,
                ts = DateTimeOffset.UtcNow.ToString("O")
            };

            var json = JsonSerializer.Serialize(entry);

            try
            {
                // Copy-on-write append (write temp → fsync → atomic rename → dir fsync) so a crash
                // mid-write can never leave a torn line that VerifyChainAsync misreads as tampering.
                await AtomicFileWriter.AppendLinesAsync(chainLogPath, [json], ct).ConfigureAwait(false);
                _log.Debug("Audit entry appended for {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to append audit entry for {FilePath}", filePath);
                throw;
            }
        }
        finally
        {
            _appendLock.Release();
        }
    }

    private static async Task<FileStream> AcquireCrossProcessLockAsync(
        string chainLogPath,
        CancellationToken ct)
    {
        var lockPath = $"{chainLogPath}.lock";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CrossProcessLockTimeout);
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Verify the entire audit chain for tampering.
    /// </summary>
    public async Task<AuditChainVerifyResult> VerifyChainAsync(string chainLogPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainLogPath, nameof(chainLogPath));

        var result = new AuditChainVerifyResult
        {
            IsValid = true,
            EntriesChecked = 0,
            FirstTamperPath = null,
            TamperedAt = null
        };

        if (!File.Exists(chainLogPath))
        {
            _log.Warning("Chain log not found at {ChainLogPath}; unable to verify integrity", chainLogPath);
            result.IsValid = false;
            result.FirstTamperPath = chainLogPath;
            return result;
        }

        _log.Information("Starting audit chain verification for {ChainLogPath}", chainLogPath);

        string? previousHash = "";
        int lineNumber = 0;

        try
        {
            var lines = await File.ReadAllLinesAsync(chainLogPath, ct).ConfigureAwait(false);

            foreach (var line in lines)
            {
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                result.EntriesChecked++;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    var path = root.TryGetProperty("path"u8, out var pathElement)
                        ? pathElement.GetString() ?? ""
                        : "";
                    var fileHash = root.TryGetProperty("fileHash"u8, out var fileHashElement)
                        ? fileHashElement.GetString() ?? ""
                        : "";
                    var currentHash = root.TryGetProperty("hash"u8, out var hashElement)
                        ? hashElement.GetString() ?? ""
                        : "";
                    var recordedPreviousHash = root.TryGetProperty("prev"u8, out var prevElement)
                        ? prevElement.GetString() ?? ""
                        : "";
                    var timestamp = root.TryGetProperty("ts"u8, out var tsElement)
                        ? tsElement.GetString() ?? ""
                        : "";

                    if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(fileHash))
                    {
                        _log.Error("Audit chain entry at line {LineNumber} is missing required fields", lineNumber);
                        result.IsValid = false;
                        result.FirstTamperPath = path;
                        if (DateTimeOffset.TryParse(timestamp, out var ts))
                        {
                            result.TamperedAt = ts;
                        }

                        break;
                    }

                    if (!File.Exists(path))
                    {
                        _log.Error("Audit chain file not found during verification: {Path}", path);
                        result.IsValid = false;
                        result.FirstTamperPath = path;
                        if (DateTimeOffset.TryParse(timestamp, out var ts))
                        {
                            result.TamperedAt = ts;
                        }

                        break;
                    }

                    await using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var currentFileHashBytes = await Sha256Digest.ComputeBytesAsync(fileStream, ct).ConfigureAwait(false);
                    var currentFileHash = Convert.ToHexString(currentFileHashBytes).ToLowerInvariant();

                    if (!string.Equals(currentFileHash, fileHash, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Error("Audit chain file hash mismatch at line {LineNumber} for {Path}", lineNumber, path);
                        result.IsValid = false;
                        result.FirstTamperPath = path;
                        if (DateTimeOffset.TryParse(timestamp, out var ts))
                        {
                            result.TamperedAt = ts;
                        }

                        break;
                    }

                    var expectedEntryData = $"{path}{fileHash}{recordedPreviousHash}";
                    var expectedEntryHash = Sha256Digest.ComputeUtf8(expectedEntryData);

                    if (!string.Equals(expectedEntryHash, currentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        _log.Error("Audit chain entry hash mismatch at line {LineNumber} for {Path}", lineNumber, path);
                        result.IsValid = false;
                        result.FirstTamperPath = path;
                        if (DateTimeOffset.TryParse(timestamp, out var ts))
                        {
                            result.TamperedAt = ts;
                        }

                        break;
                    }

                    // Verify chain linkage: recorded previous hash should match actual previous hash
                    if (recordedPreviousHash != (previousHash ?? ""))
                    {
                        _log.Error("Chain tampering detected at line {LineNumber} for {Path}. " +
                                   "Expected previous hash {Expected}, got {Actual}",
                            lineNumber, path, previousHash, recordedPreviousHash);

                        result.IsValid = false;
                        result.FirstTamperPath = path;
                        if (DateTimeOffset.TryParse(timestamp, out var ts))
                        {
                            result.TamperedAt = ts;
                        }

                        break;
                    }

                    previousHash = currentHash;
                }
                catch (JsonException ex)
                {
                    _log.Error(ex, "Failed to parse audit chain entry at line {LineNumber}", lineNumber);
                    result.IsValid = false;
                    result.FirstTamperPath = $"<line {lineNumber}>";
                    break;
                }
            }

            if (result.IsValid)
            {
                _log.Information("Audit chain verification successful. {EntriesChecked} entries checked", result.EntriesChecked);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during audit chain verification");
            result.IsValid = false;
        }

        return result;
    }

    /// <summary>
    /// Determine the chain log path for a given file path.
    /// </summary>
    private static string GetChainLogPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? AppContext.BaseDirectory;
        return Path.Combine(directory, "chain.log");
    }
}
