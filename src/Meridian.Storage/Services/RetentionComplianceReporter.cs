using Microsoft.Extensions.Logging;

namespace Meridian.Storage.Services;

/// <summary>
/// Generates retention compliance reports by scanning stored data against
/// configured retention policies. Outputs machine-readable and human-readable
/// reports suitable for regulatory audits.
/// </summary>
public sealed class RetentionComplianceReporter
{
    private readonly StorageOptions _options;
    private readonly ILogger<RetentionComplianceReporter> _logger;

    private static readonly string[] DataExtensions = { ".jsonl", ".jsonl.gz", ".jsonl.zst", ".jsonl.lz4", ".parquet" };

    public RetentionComplianceReporter(
        StorageOptions options,
        ILogger<RetentionComplianceReporter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a retention compliance report.
    /// </summary>
    public Task<RetentionComplianceReport> GenerateReportAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var retentionDays = _options.RetentionDays;
        var entries = new List<RetentionEntry>();
        var violations = new List<RetentionViolation>();
        long totalFiles = 0;
        long totalBytes = 0;

        if (!Directory.Exists(_options.RootPath))
        {
            _logger.LogInformation("Storage root path does not exist: {RootPath}", _options.RootPath);
            return Task.FromResult(new RetentionComplianceReport(
                GeneratedAt: DateTimeOffset.UtcNow,
                RootPath: _options.RootPath,
                RetentionPolicyDays: retentionDays,
                IsCompliant: true,
                TotalFiles: 0,
                TotalSizeBytes: 0,
                Entries: entries,
                Violations: violations,
                Summary: "No data directory found."));
        }

        var files = Directory.EnumerateFiles(_options.RootPath, "*", SearchOption.AllDirectories)
            .Where(f => DataExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                    continue;

                totalFiles++;
                totalBytes += fileInfo.Length;

                var fileAge = now - fileInfo.LastWriteTimeUtc;
                var relativePath = Path.GetRelativePath(_options.RootPath, filePath);
                var symbol = ExtractSymbolFromPath(relativePath);
                var tier = InferTier(relativePath);

                var status = retentionDays.HasValue && fileAge.TotalDays > retentionDays.Value
                    ? RetentionStatus.ExceedsPolicy
                    : RetentionStatus.Compliant;

                var entry = new RetentionEntry(
                    FilePath: relativePath,
                    Symbol: symbol,
                    Tier: tier,
                    SizeBytes: fileInfo.Length,
                    LastModifiedUtc: fileInfo.LastWriteTimeUtc,
                    AgeDays: (int)fileAge.TotalDays,
                    Status: status);

                entries.Add(entry);

                if (status == RetentionStatus.ExceedsPolicy)
                {
                    violations.Add(new RetentionViolation(
                        FilePath: relativePath,
                        Symbol: symbol,
                        AgeDays: (int)fileAge.TotalDays,
                        RetentionLimitDays: retentionDays!.Value,
                        ExcessDays: (int)fileAge.TotalDays - retentionDays.Value,
                        SizeBytes: fileInfo.Length));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error scanning file {FilePath} for retention compliance", filePath);
            }
        }

        var isCompliant = violations.Count == 0;
        var symbolsAffected = violations.Select(v => v.Symbol).Distinct().ToList();

        var summary = retentionDays.HasValue
            ? isCompliant
                ? $"All {totalFiles} files comply with {retentionDays}-day retention policy."
                : $"{violations.Count} of {totalFiles} files exceed {retentionDays}-day retention policy across {symbolsAffected.Count} symbol(s)."
            : $"No retention policy configured. {totalFiles} files found ({FormatBytes(totalBytes)}).";

        _logger.LogInformation(
            "Retention compliance report: {TotalFiles} files, {ViolationCount} violations, compliant: {IsCompliant}",
            totalFiles, violations.Count, isCompliant);

        return Task.FromResult(new RetentionComplianceReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            RootPath: _options.RootPath,
            RetentionPolicyDays: retentionDays,
            IsCompliant: isCompliant,
            TotalFiles: totalFiles,
            TotalSizeBytes: totalBytes,
            Entries: entries,
            Violations: violations,
            Summary: summary));
    }

    private static string ExtractSymbolFromPath(string relativePath)
    {
        // Attempt to extract symbol from common naming patterns:
        // BySymbol: AAPL/trades/2024-01-01.jsonl
        // ByDate: 2024-01-01/AAPL/trades.jsonl
        // Flat: AAPL_trades_2024-01-01.jsonl
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (parts.Length >= 2)
        {
            // Try first directory segment if it looks like a symbol (all uppercase, 1-10 chars)
            var candidate = parts[0];
            if (candidate.Length is >= 1 and <= 10 && candidate.All(c => char.IsLetterOrDigit(c) || c == '.'))
                return candidate;

            // Try second segment for ByDate layout
            if (parts.Length >= 3)
            {
                candidate = parts[1];
                if (candidate.Length is >= 1 and <= 10 && candidate.All(c => char.IsLetterOrDigit(c) || c == '.'))
                    return candidate;
            }
        }

        // Try extracting from filename
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if (fileName.Contains('_'))
            return fileName.Split('_')[0];

        return "unknown";
    }

    private static string InferTier(string relativePath)
    {
        var lower = relativePath.ToLowerInvariant();
        if (lower.Contains("archive") || lower.Contains("cold"))
            return "cold";
        if (lower.Contains("historical") || lower.Contains("warm"))
            return "warm";
        return "hot";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}

/// <summary>
/// Retention compliance report.
/// </summary>
public sealed record RetentionComplianceReport(
    DateTimeOffset GeneratedAt,
    string RootPath,
    int? RetentionPolicyDays,
    bool IsCompliant,
    long TotalFiles,
    long TotalSizeBytes,
    IReadOnlyList<RetentionEntry> Entries,
    IReadOnlyList<RetentionViolation> Violations,
    string Summary);

/// <summary>
/// A single file's retention status.
/// </summary>
public sealed record RetentionEntry(
    string FilePath,
    string Symbol,
    string Tier,
    long SizeBytes,
    DateTime LastModifiedUtc,
    int AgeDays,
    RetentionStatus Status);

/// <summary>
/// A retention policy violation.
/// </summary>
public sealed record RetentionViolation(
    string FilePath,
    string Symbol,
    int AgeDays,
    int RetentionLimitDays,
    int ExcessDays,
    long SizeBytes);

/// <summary>
/// Retention status for a file.
/// </summary>
public enum RetentionStatus : byte
{
    Compliant,
    ExceedsPolicy,
    Archived,
    PendingDeletion
}
