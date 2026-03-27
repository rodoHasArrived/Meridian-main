using System.Security.Cryptography;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Storage.Services;

/// <summary>
/// Centralized checksum computation service to eliminate duplicate implementations
/// across PortableDataPackager, AnalysisExportService, and ManifestService.
/// All were implementing identical SHA256 checksum logic.
/// </summary>
public sealed class StorageChecksumService
{
    private readonly ILogger _log;
    private readonly IAuditChainService? _auditChainService;

    public StorageChecksumService(ILogger? log = null, IAuditChainService? auditChainService = null)
    {
        _log = log ?? LoggingSetup.ForContext<StorageChecksumService>();
        _auditChainService = auditChainService;
    }

    /// <summary>
    /// Compute SHA256 checksum for a file asynchronously.
    /// </summary>
    public async Task<string> ComputeFileChecksumAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        _log.Debug("Computing checksum for {FilePath}", filePath);

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return await ComputeChecksumAsync(stream, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Compute SHA256 checksum for a stream asynchronously.
    /// </summary>
    public async Task<string> ComputeChecksumAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Compute SHA256 checksum for byte array.
    /// </summary>
    public string ComputeChecksum(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data, nameof(data));

        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Compute SHA256 checksum for string content.
    /// </summary>
    public string ComputeChecksum(string content)
    {
        ArgumentNullException.ThrowIfNull(content, nameof(content));

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return ComputeChecksum(bytes);
    }

    /// <summary>
    /// Verify file matches expected checksum.
    /// </summary>
    public async Task<bool> VerifyFileChecksumAsync(string filePath, string expectedChecksum, CancellationToken ct = default)
    {
        var actualChecksum = await ComputeFileChecksumAsync(filePath, ct).ConfigureAwait(false);
        var matches = string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);

        if (!matches)
        {
            _log.Warning("Checksum mismatch for {FilePath}. Expected: {Expected}, Actual: {Actual}",
                filePath, expectedChecksum, actualChecksum);
        }

        return matches;
    }

    /// <summary>
    /// Compute checksums for multiple files in parallel.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> ComputeFileChecksumsAsync(
        IEnumerable<string> filePaths,
        CancellationToken ct = default)
    {
        var results = new Dictionary<string, string>();
        var tasks = filePaths.Select(async path =>
        {
            var checksum = await ComputeFileChecksumAsync(path, ct).ConfigureAwait(false);
            return (path, checksum);
        });

        var completed = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var (path, checksum) in completed)
        {
            results[path] = checksum;
        }

        return results;
    }

    /// <summary>
    /// Verify the audit chain for a storage path.
    /// </summary>
    public async Task<AuditChainVerifyResult> VerifyAuditChainAsync(string storageBasePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageBasePath, nameof(storageBasePath));

        if (_auditChainService == null)
        {
            _log.Warning("Audit chain service not configured, cannot verify chain");
            return new AuditChainVerifyResult { IsValid = false, EntriesChecked = 0 };
        }

        var chainLogPath = Path.Combine(storageBasePath, "chain.log");
        _log.Information("Verifying audit chain at {ChainLogPath}", chainLogPath);

        return await _auditChainService.VerifyChainAsync(chainLogPath, ct).ConfigureAwait(false);
    }
}
