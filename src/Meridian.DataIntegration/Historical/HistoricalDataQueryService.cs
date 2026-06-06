using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.DataIntegration.Historical;

/// <summary>
/// Service for querying historical market data from storage.
/// Implements QW-15: Query Endpoint for Historical Data.
/// </summary>
public sealed class HistoricalDataQueryService
{
    private readonly ILogger<HistoricalDataQueryService> _log;
    private readonly string _dataRoot;
    private readonly JsonSerializerOptions _jsonOptions;

    public HistoricalDataQueryService(
        string dataRoot,
        ILogger<HistoricalDataQueryService>? log = null)
    {
        _dataRoot = dataRoot;
        _log = log ?? NullLogger<HistoricalDataQueryService>.Instance;
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
                _log.LogWarning(ex, "Failed to read file {File}", file);
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
            if (IsLikelySymbolName(dirName) &&
                Directory.GetFiles(dir, "*.jsonl*", SearchOption.AllDirectories).Length > 0)
            {
                symbols.Add(dirName.ToUpperInvariant());
            }
        }

        // Also search for files with symbol in name (Flat convention)
        foreach (var file in Directory.GetFiles(_dataRoot, "*.jsonl*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.EndsWith(".jsonl"))
                fileName = Path.GetFileNameWithoutExtension(fileName);

            var parts = fileName.Split('_');
            if (parts.Length > 0 && IsLikelySymbolName(parts[0]))
            {
                symbols.Add(parts[0].ToUpperInvariant());
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

    /// <summary>
    /// Aggregates stored trade events into OHLCV bars at the requested interval.
    /// Reads historical jsonl files for the symbol, extracts trade payloads, and buckets them by
    /// the requested minute window aligned to UTC.
    /// </summary>
    public async Task<HistoricalBarsResult> GetBarsAsync(
        HistoricalBarsQuery query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Symbol))
        {
            throw new ArgumentException("Symbol is required", nameof(query));
        }

        if (query.IntervalMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.IntervalMinutes, "Interval minutes must be positive");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var fileQuery = new HistoricalDataQuery(
            Symbol: query.Symbol,
            From: query.From,
            To: query.To,
            DataType: query.DataType);

        var files = FindDataFiles(fileQuery);
        var bars = new SortedDictionary<DateTimeOffset, BarAccumulator>();
        var filesProcessed = 0;
        var maxBars = query.MaxBars ?? 5000;
        var bucketTicks = TimeSpan.FromMinutes(query.IntervalMinutes).Ticks;
        var fromBoundary = query.From.HasValue
            ? new DateTimeOffset(query.From.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : (DateTimeOffset?)null;
        var toBoundary = query.To.HasValue
            ? new DateTimeOffset(query.To.Value.ToDateTime(TimeOnly.MinValue).AddDays(1), TimeSpan.Zero)
            : (DateTimeOffset?)null;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await AggregateTradesFromFileAsync(
                    file,
                    bars,
                    bucketTicks,
                    fromBoundary,
                    toBoundary,
                    query.Symbol,
                    ct).ConfigureAwait(false);
                filesProcessed++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to aggregate bars from file {File}", file);
            }
        }

        stopwatch.Stop();

        var orderedBars = bars
            .Take(maxBars)
            .Select(kvp => kvp.Value.ToPoint())
            .ToList();

        return new HistoricalBarsResult
        {
            Success = true,
            Symbol = query.Symbol,
            IntervalMinutes = query.IntervalMinutes,
            From = query.From,
            To = query.To,
            Bars = orderedBars,
            TotalBars = orderedBars.Count,
            FilesProcessed = filesProcessed,
            TotalFiles = files.Count,
            QueryTimeMs = stopwatch.ElapsedMilliseconds,
            Message = $"Aggregated {orderedBars.Count} bars from {filesProcessed} file(s)"
        };
    }

    private static async Task AggregateTradesFromFileAsync(
        string filePath,
        SortedDictionary<DateTimeOffset, BarAccumulator> bars,
        long bucketTicks,
        DateTimeOffset? fromBoundary,
        DateTimeOffset? toBoundary,
        string symbol,
        CancellationToken ct)
    {
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
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryExtractTrade(line, symbol, out var ts, out var price, out var size))
                {
                    continue;
                }

                if (fromBoundary is { } fromTs && ts < fromTs) continue;
                if (toBoundary is { } toTs && ts >= toTs) continue;

                var bucketStartTicks = ts.UtcTicks - (ts.UtcTicks % bucketTicks);
                var bucketStart = new DateTimeOffset(bucketStartTicks, TimeSpan.Zero);

                if (!bars.TryGetValue(bucketStart, out var accumulator))
                {
                    accumulator = new BarAccumulator(bucketStart);
                    bars[bucketStart] = accumulator;
                }

                accumulator.Add(price, size);
            }
        }
        finally
        {
            stream?.Dispose();
        }
    }

    private static bool TryExtractTrade(
        string line,
        string symbol,
        out DateTimeOffset timestamp,
        out decimal price,
        out long size)
    {
        timestamp = default;
        price = 0m;
        size = 0L;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!IsTradeEvent(root))
            {
                return false;
            }

            if (!root.TryGetProperty("symbol", out var symbolEl) || symbolEl.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var lineSymbol = symbolEl.GetString();
            if (lineSymbol is null || !string.Equals(lineSymbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!payload.TryGetProperty("price", out var priceEl) || !TryReadDecimal(priceEl, out price) || price <= 0m)
            {
                return false;
            }

            if (!payload.TryGetProperty("size", out var sizeEl) || !TryReadInt64(sizeEl, out size) || size < 0)
            {
                return false;
            }

            // Prefer payload timestamp (event time) if present; fall back to top-level timestamp.
            if (payload.TryGetProperty("timestamp", out var payloadTs) && TryReadTimestamp(payloadTs, out timestamp))
            {
                return true;
            }

            if (root.TryGetProperty("timestamp", out var topTs) && TryReadTimestamp(topTs, out timestamp))
            {
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsTradeEvent(JsonElement root)
    {
        if (root.TryGetProperty("type", out var typeEl))
        {
            switch (typeEl.ValueKind)
            {
                case JsonValueKind.Number:
                    if (typeEl.TryGetInt32(out var typeNum) && typeNum == 3) return true;
                    break;
                case JsonValueKind.String:
                    if (string.Equals(typeEl.GetString(), "Trade", StringComparison.OrdinalIgnoreCase)) return true;
                    break;
            }
        }

        if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String)
            {
                return string.Equals(kindEl.GetString(), "trade", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static bool TryReadDecimal(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDecimal(out value);
            case JsonValueKind.String:
                return decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            default:
                value = 0m;
                return false;
        }
    }

    private static bool TryReadInt64(JsonElement element, out long value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetInt64(out value);
            case JsonValueKind.String:
                return long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            default:
                value = 0L;
                return false;
        }
    }

    private static bool TryReadTimestamp(JsonElement element, out DateTimeOffset value)
    {
        if (element.ValueKind == JsonValueKind.String && element.TryGetDateTimeOffset(out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var unixMs))
        {
            // Heuristic: > ~1e12 looks like ms, otherwise treat as seconds.
            value = unixMs > 1_000_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(unixMs)
                : DateTimeOffset.FromUnixTimeSeconds(unixMs);
            return true;
        }

        value = default;
        return false;
    }

    private sealed class BarAccumulator
    {
        private decimal _open;
        private decimal _high;
        private decimal _low;
        private decimal _close;
        private long _volume;
        private decimal _pxVolume;
        private int _tradeCount;
        private bool _initialized;

        public BarAccumulator(DateTimeOffset start)
        {
            Start = start;
        }

        public DateTimeOffset Start { get; }

        public void Add(decimal price, long size)
        {
            if (!_initialized)
            {
                _open = price;
                _high = price;
                _low = price;
                _initialized = true;
            }
            else
            {
                if (price > _high) _high = price;
                if (price < _low) _low = price;
            }

            _close = price;
            if (size > 0)
            {
                _volume += size;
                _pxVolume += price * size;
            }
            _tradeCount++;
        }

        public HistoricalBarPoint ToPoint()
        {
            var vwap = _volume > 0 ? _pxVolume / _volume : _close;
            return new HistoricalBarPoint(Start, _open, _high, _low, _close, _volume, vwap, _tradeCount);
        }
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
            Timestamp = ReadRecordTimestamp(root),
            Symbol = ReadRecordSymbol(root),
            EventType = ReadEventType(root),
            RawJson = line
        };
    }

    private static DateTimeOffset ReadRecordTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var ts) && TryReadTimestamp(ts, out var timestamp))
        {
            return timestamp;
        }

        if (root.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("timestamp", out var payloadTs) &&
            TryReadTimestamp(payloadTs, out timestamp))
        {
            return timestamp;
        }

        return default;
    }

    private static string? ReadRecordSymbol(JsonElement root)
    {
        if (root.TryGetProperty("symbol", out var sym) && sym.ValueKind == JsonValueKind.String)
        {
            return sym.GetString();
        }

        if (root.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("symbol", out var payloadSym) &&
            payloadSym.ValueKind == JsonValueKind.String)
        {
            return payloadSym.GetString();
        }

        return null;
    }

    private static string? ReadEventType(JsonElement root)
    {
        if (root.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String)
            {
                return type.GetString();
            }

            if (type.ValueKind == JsonValueKind.Number && type.TryGetInt32(out var numericType))
            {
                if (numericType is >= byte.MinValue and <= byte.MaxValue)
                {
                    var eventType = (MarketEventType)numericType;
                    if (Enum.IsDefined(eventType))
                    {
                        return eventType.ToString();
                    }
                }

                return numericType.ToString(CultureInfo.InvariantCulture);
            }
        }

        if (root.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("kind", out var kind) &&
            kind.ValueKind == JsonValueKind.String)
        {
            return kind.GetString();
        }

        return null;
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

    private static bool IsLikelySymbolName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 20)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsUpper(ch) && !char.IsDigit(ch) && ch != '.' && ch != '-')
            {
                return false;
            }
        }

        return true;
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

/// <summary>
/// Query parameters for aggregated OHLCV bars.
/// </summary>
public sealed record HistoricalBarsQuery(
    string Symbol,
    int IntervalMinutes,
    DateOnly? From = null,
    DateOnly? To = null,
    string? DataType = null,
    int? MaxBars = null
);

/// <summary>
/// A single OHLCV bar.
/// </summary>
public sealed record HistoricalBarPoint(
    DateTimeOffset Start,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal Vwap,
    int TradeCount
);

/// <summary>
/// Result of a historical bars aggregation query.
/// </summary>
public sealed class HistoricalBarsResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Symbol { get; set; }
    public int IntervalMinutes { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int TotalBars { get; set; }
    public int FilesProcessed { get; set; }
    public int TotalFiles { get; set; }
    public long QueryTimeMs { get; set; }
    public List<HistoricalBarPoint> Bars { get; set; } = new();
}
