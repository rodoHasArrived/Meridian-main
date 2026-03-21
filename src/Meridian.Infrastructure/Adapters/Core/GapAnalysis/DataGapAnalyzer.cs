using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Analyzes existing archived data to detect gaps and avoid duplicate requests.
/// Scans storage directories and identifies which date ranges need backfilling.
/// </summary>
public sealed class DataGapAnalyzer
{
    private readonly string _dataRoot;
    private readonly ILogger _log;

    // Cache of existing data inventory
    private readonly ConcurrentDictionary<string, SymbolDataInventory> _inventoryCache = new();
    private DateTimeOffset _lastInventoryScan = DateTimeOffset.MinValue;
    private readonly TimeSpan _inventoryCacheDuration = TimeSpan.FromMinutes(5);

    public DataGapAnalyzer(string dataRoot, ILogger? log = null)
    {
        _dataRoot = dataRoot;
        _log = log ?? LoggingSetup.ForContext<DataGapAnalyzer>();
    }

    /// <summary>
    /// Analyze gaps for a list of symbols over a date range.
    /// </summary>
    public async Task<GapAnalysisResult> AnalyzeAsync(
        IEnumerable<string> symbols,
        DateOnly from,
        DateOnly to,
        DataGranularity granularity = DataGranularity.Daily,
        CancellationToken ct = default)
    {
        var result = new GapAnalysisResult
        {
            FromDate = from,
            ToDate = to,
            Granularity = granularity,
            AnalyzedAt = DateTimeOffset.UtcNow
        };

        // Refresh inventory if stale
        if (DateTimeOffset.UtcNow - _lastInventoryScan > _inventoryCacheDuration)
        {
            await RefreshInventoryCacheAsync(ct).ConfigureAwait(false);
        }

        var symbolList = symbols.ToList();
        result.TotalSymbols = symbolList.Count;

        foreach (var symbol in symbolList)
        {
            ct.ThrowIfCancellationRequested();

            var symbolGaps = await AnalyzeSymbolGapsAsync(symbol, from, to, granularity, ct).ConfigureAwait(false);
            result.SymbolGaps[symbol] = symbolGaps;

            if (symbolGaps.HasGaps)
            {
                result.SymbolsWithGaps++;
                result.TotalGapDays += symbolGaps.GapDates.Count;
            }
            else
            {
                result.SymbolsComplete++;
            }
        }

        _log.Information("Gap analysis complete: {SymbolsWithGaps}/{TotalSymbols} symbols have gaps, {TotalGapDays} total gap days",
            result.SymbolsWithGaps, result.TotalSymbols, result.TotalGapDays);

        return result;
    }

    /// <summary>
    /// Analyze gaps for a single symbol.
    /// </summary>
    public async Task<SymbolGapInfo> AnalyzeSymbolGapsAsync(
        string symbol,
        DateOnly from,
        DateOnly to,
        DataGranularity granularity = DataGranularity.Daily,
        CancellationToken ct = default)
    {
        var info = new SymbolGapInfo
        {
            Symbol = symbol,
            FromDate = from,
            ToDate = to,
            Granularity = granularity
        };

        // Get inventory for this symbol
        var inventory = await GetSymbolInventoryAsync(symbol, ct).ConfigureAwait(false);

        // Generate expected trading days (simplified - excludes weekends)
        var expectedDates = GenerateTradingDays(from, to);
        info.ExpectedDays = expectedDates.Count;

        // Find gaps
        foreach (var date in expectedDates)
        {
            var hasData = granularity == DataGranularity.Daily
                ? inventory.DailyBarDates.Contains(date)
                : inventory.IntradayBarDates.TryGetValue(granularity, out var dates) && dates.Contains(date);

            if (hasData)
            {
                info.ExistingDates.Add(date);
            }
            else
            {
                info.GapDates.Add(date);
            }
        }

        info.CoveredDays = info.ExistingDates.Count;
        info.HasGaps = info.GapDates.Count > 0;

        return info;
    }

    /// <summary>
    /// Get the data inventory for a specific symbol.
    /// </summary>
    public async Task<SymbolDataInventory> GetSymbolInventoryAsync(string symbol, CancellationToken ct = default)
    {
        var normalizedSymbol = symbol.ToUpperInvariant();

        if (_inventoryCache.TryGetValue(normalizedSymbol, out var cached))
        {
            return cached;
        }

        var inventory = await ScanSymbolDataAsync(normalizedSymbol, ct).ConfigureAwait(false);
        _inventoryCache[normalizedSymbol] = inventory;
        return inventory;
    }

    /// <summary>
    /// Refresh the entire inventory cache by scanning the data directory.
    /// </summary>
    public async Task RefreshInventoryCacheAsync(CancellationToken ct = default)
    {
        _log.Information("Refreshing data inventory cache from {DataRoot}", _dataRoot);

        _inventoryCache.Clear();

        if (!Directory.Exists(_dataRoot))
        {
            _log.Warning("Data root directory does not exist: {DataRoot}", _dataRoot);
            _lastInventoryScan = DateTimeOffset.UtcNow;
            return;
        }

        // Scan for all data files
        var files = Directory.EnumerateFiles(_dataRoot, "*.jsonl*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("_catalog") && !f.Contains("_logs"))
            .ToList();

        _log.Debug("Found {FileCount} data files to analyze", files.Count);

        var tasks = files
            .GroupBy(f => ExtractSymbolFromPath(f))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .Select(async g =>
            {
                var symbol = g.Key!;
                var inventory = new SymbolDataInventory { Symbol = symbol };

                foreach (var file in g)
                {
                    ct.ThrowIfCancellationRequested();
                    await AnalyzeFileAsync(file, inventory, ct).ConfigureAwait(false);
                }

                _inventoryCache[symbol] = inventory;
            });

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _lastInventoryScan = DateTimeOffset.UtcNow;
        _log.Information("Inventory refresh complete: {SymbolCount} symbols indexed", _inventoryCache.Count);
    }

    /// <summary>
    /// Scan data for a specific symbol.
    /// </summary>
    private async Task<SymbolDataInventory> ScanSymbolDataAsync(string symbol, CancellationToken ct)
    {
        var inventory = new SymbolDataInventory { Symbol = symbol };

        if (!Directory.Exists(_dataRoot))
            return inventory;

        // Look for files matching this symbol in various naming conventions
        var patterns = new[]
        {
            $"{symbol}_*.jsonl*",           // Flat: AAPL_Bar_2024-01-01.jsonl
            $"*/{symbol}/**/*.jsonl*",      // BySymbol: AAPL/Bar/2024-01-01.jsonl
            $"*/{symbol}_*.jsonl*",         // Various patterns
        };

        // Simple approach: search recursively and filter
        var allFiles = Directory.EnumerateFiles(_dataRoot, "*.jsonl*", SearchOption.AllDirectories)
            .Where(f => PathContainsSymbol(f, symbol))
            .Where(f => !f.Contains("_catalog") && !f.Contains("_logs"));

        foreach (var file in allFiles)
        {
            ct.ThrowIfCancellationRequested();
            await AnalyzeFileAsync(file, inventory, ct).ConfigureAwait(false);
        }

        return inventory;
    }

    /// <summary>
    /// Analyze a single data file and update inventory.
    /// </summary>
    private async Task AnalyzeFileAsync(string filePath, SymbolDataInventory inventory, CancellationToken ct)
    {
        try
        {
            var fileName = Path.GetFileName(filePath);
            var date = ExtractDateFromFileName(fileName);

            if (!date.HasValue)
            {
                // Try to get date from first record in file
                date = await GetFirstRecordDateAsync(filePath, ct).ConfigureAwait(false);
            }

            if (!date.HasValue)
                return;

            var fileInfo = new FileInfo(filePath);
            var isBar = fileName.Contains("bar", StringComparison.OrdinalIgnoreCase) ||
                        fileName.Contains("ohlc", StringComparison.OrdinalIgnoreCase);

            if (isBar)
            {
                // Determine granularity from file name or content
                var granularity = DetermineGranularity(fileName);

                if (granularity == DataGranularity.Daily)
                {
                    inventory.DailyBarDates.Add(date.Value);
                    inventory.DailyBarFiles[date.Value] = new DataFileInfo
                    {
                        Path = filePath,
                        Date = date.Value,
                        SizeBytes = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTimeUtc
                    };
                }
                else
                {
                    if (!inventory.IntradayBarDates.TryGetValue(granularity, out var dates))
                    {
                        dates = new HashSet<DateOnly>();
                        inventory.IntradayBarDates[granularity] = dates;
                    }
                    dates.Add(date.Value);
                }
            }

            inventory.TotalFiles++;
            inventory.TotalSizeBytes += fileInfo.Length;
            inventory.LastUpdated = DateTimeOffset.UtcNow;

            if (!inventory.OldestDate.HasValue || date.Value < inventory.OldestDate.Value)
                inventory.OldestDate = date.Value;

            if (!inventory.NewestDate.HasValue || date.Value > inventory.NewestDate.Value)
                inventory.NewestDate = date.Value;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "Failed to analyze file: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Get the date from the first record in a file.
    /// </summary>
    private async Task<DateOnly?> GetFirstRecordDateAsync(string filePath, CancellationToken ct)
    {
        try
        {
            await using var stream = OpenFileStream(filePath);
            using var reader = new StreamReader(stream);

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
                return null;

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // Try common timestamp field names
            foreach (var fieldName in new[] { "Timestamp", "t", "timestamp", "date", "Date", "SessionDate" })
            {
                if (root.TryGetProperty(fieldName, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.String)
                    {
                        if (DateTimeOffset.TryParse(prop.GetString(), out var dto))
                            return DateOnly.FromDateTime(dto.UtcDateTime);
                        if (DateOnly.TryParse(prop.GetString(), out var dateOnly))
                            return dateOnly;
                    }
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    private Stream OpenFileStream(string filePath)
    {
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (filePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(fileStream, CompressionMode.Decompress);
        }

        return fileStream;
    }

    private static bool PathContainsSymbol(string path, string symbol)
    {
        var normalizedPath = path.ToUpperInvariant();
        var normalizedSymbol = symbol.ToUpperInvariant();

        // Check various patterns
        return normalizedPath.Contains($"/{normalizedSymbol}/") ||
               normalizedPath.Contains($"\\{normalizedSymbol}\\") ||
               normalizedPath.Contains($"/{normalizedSymbol}_") ||
               normalizedPath.Contains($"\\{normalizedSymbol}_") ||
               normalizedPath.Contains($"_{normalizedSymbol}_") ||
               normalizedPath.Contains($"_{normalizedSymbol}.") ||
               Path.GetFileName(normalizedPath).StartsWith($"{normalizedSymbol}_") ||
               Path.GetFileName(normalizedPath).StartsWith($"{normalizedSymbol}.");
    }

    private static string? ExtractSymbolFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            fileName = Path.GetFileNameWithoutExtension(fileName);

        // Try to extract symbol from various naming patterns
        // Pattern: SYMBOL_type_date.jsonl
        var parts = fileName.Split('_');
        if (parts.Length >= 1 && IsValidSymbol(parts[0]))
        {
            return parts[0].ToUpperInvariant();
        }

        // Pattern: date/SYMBOL/type.jsonl - get from directory
        var directories = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var dir in directories.Reverse())
        {
            if (IsValidSymbol(dir))
                return dir.ToUpperInvariant();
        }

        return null;
    }

    private static bool IsValidSymbol(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 10)
            return false;

        // Common non-symbol directory names
        var excludes = new[] { "data", "bar", "trade", "quote", "depth", "daily", "minute", "hour", "logs", "catalog" };
        if (excludes.Contains(s, StringComparer.OrdinalIgnoreCase))
            return false;

        // Check if it looks like a date
        if (s.Contains('-') && s.Length == 10)
            return false;

        // Must be alphanumeric (allowing . and - for some symbols)
        return s.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-');
    }

    private static DateOnly? ExtractDateFromFileName(string fileName)
    {
        // Try to find a date pattern in the filename
        // Common patterns: 2024-01-01, 20240101, 2024_01_01

        var patterns = new[]
        {
            @"(\d{4}-\d{2}-\d{2})",   // 2024-01-01
            @"(\d{4}_\d{2}_\d{2})",   // 2024_01_01
            @"(\d{8})"                 // 20240101
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(fileName, pattern);
            if (match.Success)
            {
                var dateStr = match.Groups[1].Value.Replace("_", "-");
                if (dateStr.Length == 8)
                {
                    dateStr = $"{dateStr[..4]}-{dateStr[4..6]}-{dateStr[6..8]}";
                }

                if (DateOnly.TryParse(dateStr, out var date))
                    return date;
            }
        }

        return null;
    }

    private static DataGranularity DetermineGranularity(string fileName)
    {
        var lower = fileName.ToLowerInvariant();

        if (lower.Contains("1min") || lower.Contains("minute1"))
            return DataGranularity.Minute1;
        if (lower.Contains("5min") || lower.Contains("minute5"))
            return DataGranularity.Minute5;
        if (lower.Contains("15min") || lower.Contains("minute15"))
            return DataGranularity.Minute15;
        if (lower.Contains("30min") || lower.Contains("minute30"))
            return DataGranularity.Minute30;
        if (lower.Contains("1hour") || lower.Contains("hour1") || lower.Contains("hourly"))
            return DataGranularity.Hour1;
        if (lower.Contains("4hour") || lower.Contains("hour4"))
            return DataGranularity.Hour4;
        if (lower.Contains("week"))
            return DataGranularity.Weekly;
        if (lower.Contains("month"))
            return DataGranularity.Monthly;

        return DataGranularity.Daily;
    }

    /// <summary>
    /// Generate expected trading days (excludes weekends).
    /// </summary>
    private static List<DateOnly> GenerateTradingDays(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        var current = from;

        while (current <= to)
        {
            // Exclude weekends (Saturday = 6, Sunday = 0)
            var dayOfWeek = current.DayOfWeek;
            if (dayOfWeek != DayOfWeek.Saturday && dayOfWeek != DayOfWeek.Sunday)
            {
                days.Add(current);
            }
            current = current.AddDays(1);
        }

        return days;
    }

    /// <summary>
    /// Clear the inventory cache.
    /// </summary>
    public void ClearCache()
    {
        _inventoryCache.Clear();
        _lastInventoryScan = DateTimeOffset.MinValue;
    }
}

/// <summary>
/// Result of a gap analysis operation.
/// </summary>
public sealed class GapAnalysisResult
{
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public DataGranularity Granularity { get; init; }
    public DateTimeOffset AnalyzedAt { get; init; }

    public int TotalSymbols { get; set; }
    public int SymbolsWithGaps { get; set; }
    public int SymbolsComplete { get; set; }
    public int TotalGapDays { get; set; }

    /// <summary>Alias for TotalGapDays for API compatibility.</summary>
    public int TotalGaps => TotalGapDays;

    public Dictionary<string, SymbolGapInfo> SymbolGaps { get; init; } = new();

    public bool HasGaps => SymbolsWithGaps > 0;
    public double CompletenessPercent => TotalSymbols > 0
        ? (SymbolsComplete * 100.0) / TotalSymbols
        : 0;
}

/// <summary>
/// Gap information for a single symbol.
/// </summary>
public sealed class SymbolGapInfo
{
    public string Symbol { get; init; } = string.Empty;
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public DataGranularity Granularity { get; init; }

    public int ExpectedDays { get; set; }
    public int CoveredDays { get; set; }
    public bool HasGaps { get; set; }

    public List<DateOnly> GapDates { get; init; } = new();
    public List<DateOnly> ExistingDates { get; init; } = new();

    public double CoveragePercent => ExpectedDays > 0
        ? (CoveredDays * 100.0) / ExpectedDays
        : 0;

    /// <summary>
    /// Get consolidated date ranges for gaps (to minimize requests).
    /// </summary>
    public List<(DateOnly From, DateOnly To)> GetGapRanges(int maxDaysPerRange = 365)
    {
        if (GapDates.Count == 0)
            return new List<(DateOnly, DateOnly)>();

        var sorted = GapDates.OrderBy(d => d).ToList();
        var ranges = new List<(DateOnly From, DateOnly To)>();

        var rangeStart = sorted[0];
        var rangeEnd = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            var current = sorted[i];
            var daysSinceRangeEnd = current.DayNumber - rangeEnd.DayNumber;
            var rangeDays = rangeEnd.DayNumber - rangeStart.DayNumber + 1;

            // Extend range if consecutive and within max size
            if (daysSinceRangeEnd <= 3 && rangeDays < maxDaysPerRange) // Allow small gaps (weekends)
            {
                rangeEnd = current;
            }
            else
            {
                ranges.Add((rangeStart, rangeEnd));
                rangeStart = current;
                rangeEnd = current;
            }
        }

        ranges.Add((rangeStart, rangeEnd));
        return ranges;
    }
}

/// <summary>
/// Inventory of existing data for a symbol.
/// </summary>
public sealed class SymbolDataInventory
{
    public string Symbol { get; init; } = string.Empty;

    /// <summary>
    /// Dates with daily bar data.
    /// </summary>
    public HashSet<DateOnly> DailyBarDates { get; init; } = new();

    /// <summary>
    /// Dates with intraday bar data by granularity.
    /// </summary>
    public Dictionary<DataGranularity, HashSet<DateOnly>> IntradayBarDates { get; init; } = new();

    /// <summary>
    /// File info for daily bar files.
    /// </summary>
    public Dictionary<DateOnly, DataFileInfo> DailyBarFiles { get; init; } = new();

    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public DateOnly? OldestDate { get; set; }
    public DateOnly? NewestDate { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Information about a data file.
/// </summary>
public sealed class DataFileInfo
{
    public string Path { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastModified { get; init; }
    public int? EventCount { get; init; }
    public string? Checksum { get; init; }
}
