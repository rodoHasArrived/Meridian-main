using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Domain.Events;
using Serilog;

namespace Meridian.Tools;

/// <summary>
/// Validates JSONL data files for integrity and completeness.
/// Can be run as a standalone tool to check stored data quality.
/// Supports both PascalCase and camelCase JSON property names.
/// </summary>
public sealed class DataValidator
{
    private readonly ILogger _log = LoggingSetup.ForContext<DataValidator>();

    /// <summary>
    /// Tries to get a JSON property by name, checking both PascalCase and camelCase variants.
    /// The storage system serializes with camelCase (e.g. "type", "symbol", "timestamp")
    /// but external or manually created files may use PascalCase.
    /// </summary>
    private static bool TryGetPropertyCaseInsensitive(
        JsonElement root, string pascalName, string camelName, out JsonElement value)
    {
        return root.TryGetProperty(pascalName, out value) || root.TryGetProperty(camelName, out value);
    }

    public record ValidationResult(
        string FilePath,
        bool IsValid,
        int TotalLines,
        int ValidEvents,
        int InvalidEvents,
        int ParseErrors,
        List<string> Errors,
        List<GapInfo> Gaps,
        DateTimeOffset? FirstTimestamp,
        DateTimeOffset? LastTimestamp
    );

    public record GapInfo(
        string Symbol,
        DateTimeOffset Before,
        DateTimeOffset After,
        TimeSpan Duration
    );

    /// <summary>
    /// Validates a single JSONL file.
    /// </summary>
    public async Task<ValidationResult> ValidateFileAsync(string filePath, CancellationToken ct = default)
    {
        // Honour cancellation immediately — even for small files that would
        // otherwise read synchronously from the internal StreamReader buffer.
        ct.ThrowIfCancellationRequested();

        var errors = new List<string>();
        var gaps = new List<GapInfo>();
        var totalLines = 0;
        var validEvents = 0;
        var invalidEvents = 0;
        var parseErrors = 0;
        DateTimeOffset? firstTs = null;
        DateTimeOffset? lastTs = null;

        // Track last timestamp per symbol for gap detection
        var lastTimestampBySymbol = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var gapThreshold = TimeSpan.FromMinutes(5);

        try
        {
            if (!File.Exists(filePath))
            {
                errors.Add($"File not found: {filePath}");
                return new ValidationResult(filePath, false, 0, 0, 0, 0, errors, gaps, null, null);
            }

            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Stream stream = fs;

            // Handle gzip compression
            if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                stream = new GZipStream(fs, CompressionMode.Decompress);
            }

            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                totalLines++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    // Validate required fields (support both PascalCase and camelCase)
                    if (!TryGetPropertyCaseInsensitive(root, "Type", "type", out var typeEl))
                    {
                        invalidEvents++;
                        errors.Add($"Line {totalLines}: Missing 'Type' field");
                        continue;
                    }

                    if (!TryGetPropertyCaseInsensitive(root, "Symbol", "symbol", out var symbolEl))
                    {
                        invalidEvents++;
                        errors.Add($"Line {totalLines}: Missing 'Symbol' field");
                        continue;
                    }

                    if (!TryGetPropertyCaseInsensitive(root, "Timestamp", "timestamp", out var tsEl))
                    {
                        invalidEvents++;
                        errors.Add($"Line {totalLines}: Missing 'Timestamp' field");
                        continue;
                    }

                    // Parse timestamp
                    var tsStr = tsEl.GetString();
                    if (!DateTimeOffset.TryParse(tsStr, out var timestamp))
                    {
                        invalidEvents++;
                        errors.Add($"Line {totalLines}: Invalid timestamp format: {tsStr}");
                        continue;
                    }

                    var symbol = symbolEl.GetString() ?? "";

                    // Track timestamps
                    firstTs ??= timestamp;
                    lastTs = timestamp;

                    // Check for gaps
                    if (lastTimestampBySymbol.TryGetValue(symbol, out var lastSymbolTs))
                    {
                        var gap = timestamp - lastSymbolTs;
                        if (gap > gapThreshold)
                        {
                            gaps.Add(new GapInfo(symbol, lastSymbolTs, timestamp, gap));
                        }
                    }
                    lastTimestampBySymbol[symbol] = timestamp;

                    // Validate event type (can be either string or number)
                    MarketEventType eventType;
                    if (typeEl.ValueKind == JsonValueKind.Number)
                    {
                        // MarketEventType has byte backing; cast to byte before Enum.IsDefined to avoid
                        // ArgumentException from the non-generic overload when passed an int value.
                        if (!typeEl.TryGetInt32(out var typeInt) || typeInt < 0 || typeInt > byte.MaxValue
                            || !Enum.IsDefined(typeof(MarketEventType), (byte)typeInt))
                        {
                            invalidEvents++;
                            errors.Add($"Line {totalLines}: Unknown event type: {typeEl.GetRawText()}");
                            continue;
                        }
                        eventType = (MarketEventType)typeInt;
                    }
                    else if (typeEl.ValueKind == JsonValueKind.String)
                    {
                        var typeStr = typeEl.GetString();
                        if (!Enum.TryParse<MarketEventType>(typeStr, true, out eventType))
                        {
                            invalidEvents++;
                            errors.Add($"Line {totalLines}: Unknown event type: {typeStr}");
                            continue;
                        }
                    }
                    else
                    {
                        invalidEvents++;
                        errors.Add($"Line {totalLines}: Event type must be a string or number");
                        continue;
                    }

                    validEvents++;
                }
                catch (JsonException ex)
                {
                    parseErrors++;
                    errors.Add($"Line {totalLines}: JSON parse error - {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error reading file: {ex.Message}");
            return new ValidationResult(filePath, false, totalLines, validEvents, invalidEvents, parseErrors, errors, gaps, firstTs, lastTs);
        }

        var isValid = parseErrors == 0 && invalidEvents == 0;

        return new ValidationResult(
            filePath,
            isValid,
            totalLines,
            validEvents,
            invalidEvents,
            parseErrors,
            errors,
            gaps,
            firstTs,
            lastTs
        );
    }

    /// <summary>
    /// Validates all JSONL files in a directory.
    /// </summary>
    public async Task<List<ValidationResult>> ValidateDirectoryAsync(
        string directoryPath,
        bool recursive = true,
        CancellationToken ct = default)
    {
        var results = new List<ValidationResult>();

        if (!Directory.Exists(directoryPath))
        {
            _log.Error("Directory not found: {Path}", directoryPath);
            return results;
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directoryPath, "*.jsonl", searchOption)
            .Concat(Directory.GetFiles(directoryPath, "*.jsonl.gz", searchOption))
            .ToList();

        _log.Information("Validating {Count} files in {Path}", files.Count, directoryPath);

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested)
                break;

            _log.Debug("Validating {File}", file);
            var result = await ValidateFileAsync(file, ct);
            results.Add(result);

            if (!result.IsValid)
            {
                _log.Warning("Validation failed for {File}: {ErrorCount} errors, {ParseErrors} parse errors",
                    file, result.Errors.Count, result.ParseErrors);
            }
        }

        return results;
    }

    /// <summary>
    /// Generates a summary report for validation results.
    /// </summary>
    public static ValidationSummary GenerateSummary(IEnumerable<ValidationResult> results)
    {
        var resultList = results.ToList();

        return new ValidationSummary(
            TotalFiles: resultList.Count,
            ValidFiles: resultList.Count(r => r.IsValid),
            InvalidFiles: resultList.Count(r => !r.IsValid),
            TotalEvents: resultList.Sum(r => r.ValidEvents),
            TotalErrors: resultList.Sum(r => r.Errors.Count),
            TotalGaps: resultList.Sum(r => r.Gaps.Count),
            EarliestTimestamp: resultList.Min(r => r.FirstTimestamp),
            LatestTimestamp: resultList.Max(r => r.LastTimestamp)
        );
    }

    public record ValidationSummary(
        int TotalFiles,
        int ValidFiles,
        int InvalidFiles,
        int TotalEvents,
        int TotalErrors,
        int TotalGaps,
        DateTimeOffset? EarliestTimestamp,
        DateTimeOffset? LatestTimestamp
    );

    /// <summary>
    /// Prints validation results to console.
    /// </summary>
    public static void PrintResults(IEnumerable<ValidationResult> results)
    {
        var resultList = results.ToList();

        Console.WriteLine("\n=== Data Validation Results ===\n");

        foreach (var result in resultList)
        {
            var status = result.IsValid ? "[OK]" : "[FAIL]";
            Console.WriteLine($"{status} {Path.GetFileName(result.FilePath)}");
            Console.WriteLine($"    Events: {result.ValidEvents} valid, {result.InvalidEvents} invalid, {result.ParseErrors} parse errors");

            if (result.FirstTimestamp.HasValue && result.LastTimestamp.HasValue)
            {
                Console.WriteLine($"    Time range: {result.FirstTimestamp:yyyy-MM-dd HH:mm} to {result.LastTimestamp:yyyy-MM-dd HH:mm}");
            }

            if (result.Gaps.Count > 0)
            {
                Console.WriteLine($"    Gaps detected: {result.Gaps.Count}");
                foreach (var gap in result.Gaps.Take(5))
                {
                    Console.WriteLine($"      - {gap.Symbol}: {gap.Duration:hh\\:mm\\:ss} gap at {gap.Before:HH:mm:ss}");
                }
                if (result.Gaps.Count > 5)
                {
                    Console.WriteLine($"      ... and {result.Gaps.Count - 5} more");
                }
            }

            if (!result.IsValid && result.Errors.Count > 0)
            {
                Console.WriteLine($"    Errors:");
                foreach (var error in result.Errors.Take(5))
                {
                    Console.WriteLine($"      - {error}");
                }
                if (result.Errors.Count > 5)
                {
                    Console.WriteLine($"      ... and {result.Errors.Count - 5} more");
                }
            }

            Console.WriteLine();
        }

        var summary = GenerateSummary(resultList);
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"Files: {summary.ValidFiles}/{summary.TotalFiles} valid");
        Console.WriteLine($"Events: {summary.TotalEvents:N0} total");
        Console.WriteLine($"Errors: {summary.TotalErrors:N0}");
        Console.WriteLine($"Gaps: {summary.TotalGaps:N0}");

        if (summary.EarliestTimestamp.HasValue && summary.LatestTimestamp.HasValue)
        {
            Console.WriteLine($"Date range: {summary.EarliestTimestamp:yyyy-MM-dd} to {summary.LatestTimestamp:yyyy-MM-dd}");
        }
    }
}
