using System.IO.Compression;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for querying historical market data from storage.
/// Implements QW-15: Query Endpoint for Historical Data.
/// </summary>
public sealed class HistoricalDataQueryService
{
    private readonly ILogger _log = LoggingSetup.ForContext<HistoricalDataQueryService>();
    private readonly string _dataRoot;
    private readonly JsonSerializerOptions _jsonOptions;

    public HistoricalDataQueryService(string dataRoot)
    {
        _dataRoot = dataRoot;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Queries historical data for a symbol within a date range.
    /// </summary>
    public async Task<HistoricalDataQueryResult> QueryAsync(
        HistoricalDataQuery query,
        CancellationToken ct = default)
    {
        var result = new HistoricalDataQueryResult
        {
            Symbol = query.Symbol,
            From = query.From,
            To = query.To,
            DataType = query.DataType
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var files = FindDataFiles(query);

        if (files.Count == 0)
        {
            result.Message = $"No data files found for {query.Symbol}";
            return result;
        }

        var records = new List<HistoricalDataRecord>();
        var filesProcessed = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var fileRecords = await ReadDataFileAsync(file, query, ct);
                records.AddRange(fileRecords);
                filesProcessed++;

                if (query.Limit.HasValue && records.Count >= query.Limit.Value)
                {
                    records = records.Take(query.Limit.Value).ToList();
                    break;
                }
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to read file {File}", file);
            }
        }

        stopwatch.Stop();

        result.Records = records;
        result.TotalRecords = records.Count;
        result.FilesProcessed = filesProcessed;
        result.TotalFiles = files.Count;
        result.QueryTimeMs = stopwatch.ElapsedMilliseconds;
        result.Success = true;
        result.Message = $"Found {records.Count} records in {filesProcessed} files";

        return result;
    }

    /// <summary>
    /// Gets available symbols in the data store.
    /// </summary>
    public IReadOnlyList<string> GetAvailableSymbols()
    {
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_dataRoot))
            return Array.Empty<string>();

        // Search for symbol directories (BySymbol convention)
        foreach (var dir in Directory.GetDirectories(_dataRoot))
        {
            var dirName = Path.GetFileName(dir);
            if (!dirName.StartsWith("_") && !dirName.StartsWith("."))
            {
                symbols.Add(dirName);
            }
        }

        // Also search for files with symbol in name (Flat convention)
        foreach (var file in Directory.GetFiles(_dataRoot, "*.jsonl*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.EndsWith(".jsonl"))
                fileName = Path.GetFileNameWithoutExtension(fileName);

            var parts = fileName.Split('_');
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                symbols.Add(parts[0]);
            }
        }

        return symbols.OrderBy(s => s).ToList();
    }

    /// <summary>
    /// Gets date range for available data for a symbol.
    /// </summary>
    public HistoricalDataDateRange GetDateRange(string symbol)
    {
        var files = FindDataFilesForSymbol(symbol);
        if (files.Count == 0)
        {
            return new HistoricalDataDateRange
            {
                Symbol = symbol,
                HasData = false
            };
        }

        DateOnly? earliest = null;
        DateOnly? latest = null;

        foreach (var file in files)
        {
            var date = ExtractDateFromPath(file);
            if (date.HasValue)
            {
                if (!earliest.HasValue || date < earliest)
                    earliest = date;
                if (!latest.HasValue || date > latest)
                    latest = date;
            }
        }

        return new HistoricalDataDateRange
        {
            Symbol = symbol,
            HasData = earliest.HasValue,
            EarliestDate = earliest,
            LatestDate = latest,
            FileCount = files.Count
        };
    }

    private List<string> FindDataFiles(HistoricalDataQuery query)
    {
        var files = FindDataFilesForSymbol(query.Symbol);

        // Filter by data type if specified
        if (!string.IsNullOrWhiteSpace(query.DataType))
        {
            files = files.Where(f =>
                f.Contains($"_{query.DataType}_", StringComparison.OrdinalIgnoreCase) ||
                f.Contains($"/{query.DataType}/", StringComparison.OrdinalIgnoreCase) ||
                f.Contains($"\\{query.DataType}\\", StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        // Filter by date range
        if (query.From.HasValue || query.To.HasValue)
        {
            files = files.Where(f =>
            {
                var date = ExtractDateFromPath(f);
                if (!date.HasValue)
                    return true; // Include if can't determine date

                if (query.From.HasValue && date < query.From)
                    return false;
                if (query.To.HasValue && date > query.To)
                    return false;
                return true;
            }).ToList();
        }

        return files.OrderBy(f => f).ToList();
    }

    private List<string> FindDataFilesForSymbol(string symbol)
    {
        var files = new List<string>();

        if (!Directory.Exists(_dataRoot))
            return files;

        // BySymbol convention: {root}/{symbol}/**/*.jsonl[.gz]
        var symbolDir = Path.Combine(_dataRoot, symbol);
        if (Directory.Exists(symbolDir))
        {
            files.AddRange(Directory.GetFiles(symbolDir, "*.jsonl", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(symbolDir, "*.jsonl.gz", SearchOption.AllDirectories));
        }

        // Flat convention: {root}/*{symbol}*.jsonl[.gz]
        files.AddRange(Directory.GetFiles(_dataRoot, $"*{symbol}*.jsonl", SearchOption.TopDirectoryOnly));
        files.AddRange(Directory.GetFiles(_dataRoot, $"*{symbol}*.jsonl.gz", SearchOption.TopDirectoryOnly));

        // Historical directory
        var historicalDir = Path.Combine(_dataRoot, "historical");
        if (Directory.Exists(historicalDir))
        {
            files.AddRange(Directory.GetFiles(historicalDir, $"*{symbol}*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".jsonl") || f.EndsWith(".jsonl.gz")));
        }

        return files.Distinct().ToList();
    }

    private async Task<List<HistoricalDataRecord>> ReadDataFileAsync(
        string filePath,
        HistoricalDataQuery query,
        CancellationToken ct)
    {
        var records = new List<HistoricalDataRecord>();
        Stream? stream = null;

        try
        {
            stream = File.OpenRead(filePath);
            if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            {
                stream = new GZipStream(stream, CompressionMode.Decompress);
            }

            using var reader = new StreamReader(stream);
            string? line;
            var lineNumber = 0;
            var skipCount = query.Skip ?? 0;
            var maxRecords = query.Limit ?? int.MaxValue;

            while ((line = await reader.ReadLineAsync(ct)) != null && records.Count < maxRecords)
            {
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (skipCount > 0)
                {
                    skipCount--;
                    continue;
                }

                try
                {
                    var record = ParseRecord(line, filePath, lineNumber);
                    if (record != null && MatchesQuery(record, query))
                    {
                        records.Add(record);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        finally
        {
            stream?.Dispose();
        }

        return records;
    }

    private HistoricalDataRecord? ParseRecord(string line, string filePath, int lineNumber)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        return new HistoricalDataRecord
        {
            SourceFile = Path.GetFileName(filePath),
            LineNumber = lineNumber,
            Timestamp = root.TryGetProperty("timestamp", out var ts) ? ts.GetDateTimeOffset() : default,
            Symbol = root.TryGetProperty("symbol", out var sym) ? sym.GetString() : null,
            EventType = root.TryGetProperty("type", out var type) ? type.GetString() : null,
            RawJson = line
        };
    }

    private static bool MatchesQuery(HistoricalDataRecord record, HistoricalDataQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Symbol) &&
            !string.Equals(record.Symbol, query.Symbol, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.From.HasValue)
        {
            var recordDate = DateOnly.FromDateTime(record.Timestamp.DateTime);
            if (recordDate < query.From)
                return false;
        }

        if (query.To.HasValue)
        {
            var recordDate = DateOnly.FromDateTime(record.Timestamp.DateTime);
            if (recordDate > query.To)
                return false;
        }

        return true;
    }

    private static DateOnly? ExtractDateFromPath(string path)
    {
        // Try to find date patterns in path: YYYY-MM-DD, YYYYMMDD, etc.
        var patterns = new[]
        {
            @"(\d{4})-(\d{2})-(\d{2})",  // 2024-01-15
            @"(\d{4})(\d{2})(\d{2})",     // 20240115
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(path, pattern);
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out var year) &&
                    int.TryParse(match.Groups[2].Value, out var month) &&
                    int.TryParse(match.Groups[3].Value, out var day))
                {
                    try
                    {
                        return new DateOnly(year, month, day);
                    }
                    catch
                    {
                        // Invalid date
                    }
                }
            }
        }

        return null;
    }
}

/// <summary>
/// Query parameters for historical data.
/// </summary>
public sealed record HistoricalDataQuery(
    string Symbol,
    DateOnly? From = null,
    DateOnly? To = null,
    string? DataType = null,
    int? Skip = null,
    int? Limit = null
);

/// <summary>
/// Result of a historical data query.
/// </summary>
public sealed class HistoricalDataQueryResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Symbol { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? DataType { get; set; }
    public int TotalRecords { get; set; }
    public int FilesProcessed { get; set; }
    public int TotalFiles { get; set; }
    public long QueryTimeMs { get; set; }
    public List<HistoricalDataRecord> Records { get; set; } = new();
}

/// <summary>
/// A single record from historical data.
/// </summary>
public sealed class HistoricalDataRecord
{
    public string? SourceFile { get; set; }
    public int LineNumber { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string? Symbol { get; set; }
    public string? EventType { get; set; }
    public string? RawJson { get; set; }
}

/// <summary>
/// Date range information for available data.
/// </summary>
public sealed class HistoricalDataDateRange
{
    public string? Symbol { get; set; }
    public bool HasData { get; set; }
    public DateOnly? EarliestDate { get; set; }
    public DateOnly? LatestDate { get; set; }
    public int FileCount { get; set; }
}
