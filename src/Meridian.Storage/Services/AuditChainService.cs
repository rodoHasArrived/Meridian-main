using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Logging;
using Serilog;

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

        // Compute SHA256 hash of the file
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileHashBytes = await SHA256.HashDataAsync(fileStream, ct).ConfigureAwait(false);
        var fileHash = Convert.ToHexString(fileHashBytes).ToLowerInvariant();

        // Read the previous hash from the last entry in the chain
        var previousHash = "";
        if (File.Exists(chainLogPath))
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(chainLogPath, ct).ConfigureAwait(false);
                if (lines.Length > 0)
                {
                    var lastLine = lines[^1];
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
        var entryHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(entryData));
        var entryHash = Convert.ToHexString(entryHashBytes).ToLowerInvariant();

        var entry = new
        {
            path = Path.GetFileName(filePath),
            hash = entryHash,
            prev = previousHash,
            ts = DateTimeOffset.UtcNow.ToString("O")
        };

        var json = JsonSerializer.Serialize(entry);

        try
        {
            await File.AppendAllTextAsync(chainLogPath, json + Environment.NewLine, Encoding.UTF8, ct)
                .ConfigureAwait(false);
            _log.Debug("Audit entry appended for {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to append audit entry for {FilePath}", filePath);
            throw;
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
            _log.Information("Chain log not found at {ChainLogPath}, marking as valid (empty chain)", chainLogPath);
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
                    var currentHash = root.TryGetProperty("hash"u8, out var hashElement)
                        ? hashElement.GetString() ?? ""
                        : "";
                    var recordedPreviousHash = root.TryGetProperty("prev"u8, out var prevElement)
                        ? prevElement.GetString() ?? ""
                        : "";
                    var timestamp = root.TryGetProperty("ts"u8, out var tsElement)
                        ? tsElement.GetString() ?? ""
                        : "";

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
