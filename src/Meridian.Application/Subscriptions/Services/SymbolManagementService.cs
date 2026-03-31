using System.Text;
using System.Text.RegularExpressions;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.UI;
using Meridian.Storage;
using Serilog;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Unified service for symbol management operations including:
/// - Adding/removing symbols from configuration
/// - Listing monitored symbols
/// - Scanning archived data for symbols
/// </summary>
public sealed class SymbolManagementService
{
    private readonly ConfigStore _configStore;
    private readonly string _dataRoot;
    private readonly ILogger _log;

    /// <summary>
    /// Known data types that indicate market data files.
    /// </summary>
    private static readonly string[] KnownDataTypes =
    {
        "Trade", "Trades", "Quote", "Quotes", "BboQuote",
        "L2Snapshot", "LOBSnapshot", "Depth", "Bar", "Bars",
        "OHLCV", "Tick", "Ticks"
    };

    /// <summary>
    /// File extensions that indicate market data files.
    /// </summary>
    private static readonly string[] DataExtensions = { ".jsonl", ".jsonl.gz", ".parquet", ".csv" };

    public SymbolManagementService(ConfigStore configStore, string dataRoot, ILogger? log = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _dataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
        _log = log ?? LoggingSetup.ForContext<SymbolManagementService>();
    }

    /// <summary>
    /// Add one or more symbols to the configuration for monitoring.
    /// </summary>
    public async Task<SymbolOperationResult> AddSymbolsAsync(
        IEnumerable<string> symbols,
        SymbolAddOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new SymbolAddOptions();
        var symbolList = symbols.Select(s => s.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (symbolList.Count == 0)
        {
            return new SymbolOperationResult(
                Success: false,
                Message: "No valid symbols provided",
                AffectedSymbols: Array.Empty<string>()
            );
        }

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        var added = new List<string>();
        var skipped = new List<string>();
        var updated = new List<string>();

        foreach (var symbol in symbolList)
        {
            if (!IsValidSymbol(symbol))
            {
                _log.Warning("Skipping invalid symbol format: {Symbol}", symbol);
                skipped.Add(symbol);
                continue;
            }

            if (existingSymbols.ContainsKey(symbol))
            {
                if (options.UpdateExisting)
                {
                    existingSymbols[symbol] = CreateSymbolConfig(symbol, options);
                    updated.Add(symbol);
                    _log.Information("Updated symbol configuration: {Symbol}", symbol);
                }
                else
                {
                    skipped.Add(symbol);
                    _log.Debug("Symbol already exists, skipping: {Symbol}", symbol);
                }
            }
            else
            {
                existingSymbols[symbol] = CreateSymbolConfig(symbol, options);
                added.Add(symbol);
                _log.Information("Added symbol: {Symbol}", symbol);
            }
        }

        if (added.Count > 0 || updated.Count > 0)
        {
            var next = cfg with { Symbols = existingSymbols.Values.ToArray() };
            await _configStore.SaveAsync(next);
        }

        var message = BuildResultMessage(added.Count, updated.Count, skipped.Count);
        return new SymbolOperationResult(
            Success: true,
            Message: message,
            AffectedSymbols: added.Concat(updated).ToArray(),
            AddedCount: added.Count,
            UpdatedCount: updated.Count,
            SkippedCount: skipped.Count
        );
    }

    /// <summary>
    /// Remove symbols from the configuration.
    /// </summary>
    public async Task<SymbolOperationResult> RemoveSymbolsAsync(
        IEnumerable<string> symbols,
        CancellationToken ct = default)
    {
        var symbolsToRemove = symbols
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (symbolsToRemove.Count == 0)
        {
            return new SymbolOperationResult(
                Success: false,
                Message: "No valid symbols provided",
                AffectedSymbols: Array.Empty<string>()
            );
        }

        var cfg = _configStore.Load();
        var existingSymbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();
        var removed = existingSymbols.Where(s => symbolsToRemove.Contains(s.Symbol)).Select(s => s.Symbol).ToList();
        var remainingSymbols = existingSymbols.Where(s => !symbolsToRemove.Contains(s.Symbol)).ToArray();

        if (removed.Count > 0)
        {
            var next = cfg with { Symbols = remainingSymbols };
            await _configStore.SaveAsync(next);
            _log.Information("Removed {Count} symbols: {Symbols}", removed.Count, string.Join(", ", removed));
        }

        return new SymbolOperationResult(
            Success: true,
            Message: removed.Count > 0
                ? $"Removed {removed.Count} symbol(s)"
                : "No matching symbols found to remove",
            AffectedSymbols: removed.ToArray(),
            RemovedCount: removed.Count
        );
    }

    /// <summary>
    /// Get all symbols currently configured for monitoring.
    /// </summary>
    public MonitoredSymbolsResult GetMonitoredSymbols()
    {
        var cfg = _configStore.Load();
        var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();

        var symbolInfos = symbols.Select(s => new MonitoredSymbolInfo(
            Symbol: s.Symbol,
            SubscribeTrades: s.SubscribeTrades,
            SubscribeDepth: s.SubscribeDepth,
            DepthLevels: s.DepthLevels,
            SecurityType: s.SecurityType,
            Exchange: s.Exchange,
            Currency: s.Currency
        )).ToArray();

        return new MonitoredSymbolsResult(
            Symbols: symbolInfos,
            TotalCount: symbolInfos.Length,
            TradeSubscriptions: symbolInfos.Count(s => s.SubscribeTrades),
            DepthSubscriptions: symbolInfos.Count(s => s.SubscribeDepth)
        );
    }

    /// <summary>
    /// Scan the data directory for archived symbols.
    /// </summary>
    public async Task<ArchivedSymbolsResult> GetArchivedSymbolsAsync(
        ArchivedSymbolsOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ArchivedSymbolsOptions();

        if (!Directory.Exists(_dataRoot))
        {
            return new ArchivedSymbolsResult(
                Symbols: Array.Empty<ArchivedSymbolInfo>(),
                TotalCount: 0,
                TotalFiles: 0,
                TotalSizeBytes: 0,
                DataRoot: _dataRoot,
                ScanDurationMs: 0
            );
        }

        var startTime = DateTime.UtcNow;
        var symbolStats = new Dictionary<string, SymbolFileStats>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() => ScanDirectory(_dataRoot, symbolStats, options), ct);

        var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

        var archivedSymbols = symbolStats
            .Select(kvp => new ArchivedSymbolInfo(
                Symbol: kvp.Key,
                FileCount: kvp.Value.FileCount,
                TotalSizeBytes: kvp.Value.TotalSizeBytes,
                OldestData: kvp.Value.OldestDate,
                NewestData: kvp.Value.NewestDate,
                DataTypes: kvp.Value.DataTypes.Distinct().ToArray(),
                Sources: kvp.Value.Sources.Distinct().ToArray()
            ))
            .OrderBy(s => s.Symbol)
            .ToArray();

        return new ArchivedSymbolsResult(
            Symbols: archivedSymbols,
            TotalCount: archivedSymbols.Length,
            TotalFiles: symbolStats.Values.Sum(s => s.FileCount),
            TotalSizeBytes: symbolStats.Values.Sum(s => s.TotalSizeBytes),
            DataRoot: _dataRoot,
            ScanDurationMs: (long)duration
        );
    }

    /// <summary>
    /// Get a comprehensive symbol status report combining monitored and archived data.
    /// </summary>
    public async Task<SymbolStatusReport> GetSymbolStatusAsync(
        string symbol,
        CancellationToken ct = default)
    {
        symbol = symbol.Trim().ToUpperInvariant();

        var cfg = _configStore.Load();
        var monitoredConfig = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .FirstOrDefault(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        var archivedOptions = new ArchivedSymbolsOptions { FilterSymbols = new[] { symbol } };
        var archivedResult = await GetArchivedSymbolsAsync(archivedOptions, ct);
        var archivedInfo = archivedResult.Symbols.FirstOrDefault();

        return new SymbolStatusReport(
            Symbol: symbol,
            IsMonitored: monitoredConfig != null,
            HasArchivedData: archivedInfo != null,
            MonitoredConfig: monitoredConfig,
            ArchivedInfo: archivedInfo
        );
    }

    /// <summary>
    /// Display formatted symbol management output to console.
    /// </summary>
    public void DisplayMonitoredSymbols(MonitoredSymbolsResult result)
    {
        Console.WriteLine();
        Console.WriteLine("Monitored Symbols");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine();

        if (result.TotalCount == 0)
        {
            Console.WriteLine("  No symbols currently configured for monitoring.");
            Console.WriteLine();
            Console.WriteLine("  To add symbols, use:");
            Console.WriteLine("    Meridian --symbols-add AAPL,MSFT,GOOGL");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  {"Symbol",-12} {"Trades",-8} {"Depth",-8} {"Levels",-8} {"Type",-6} {"Exchange",-10}");
        Console.WriteLine($"  {new string('-', 12)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 6)} {new string('-', 10)}");

        foreach (var symbol in result.Symbols.OrderBy(s => s.Symbol))
        {
            Console.WriteLine($"  {symbol.Symbol,-12} {(symbol.SubscribeTrades ? "Yes" : "No"),-8} {(symbol.SubscribeDepth ? "Yes" : "No"),-8} {symbol.DepthLevels,-8} {symbol.SecurityType,-6} {symbol.Exchange,-10}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Total: {result.TotalCount} symbol(s)");
        Console.WriteLine($"  Trade subscriptions: {result.TradeSubscriptions}");
        Console.WriteLine($"  Depth subscriptions: {result.DepthSubscriptions}");
        Console.WriteLine();
    }

    /// <summary>
    /// Display formatted archived symbols output to console.
    /// </summary>
    public void DisplayArchivedSymbols(ArchivedSymbolsResult result)
    {
        Console.WriteLine();
        Console.WriteLine("Archived Symbols");
        Console.WriteLine(new string('=', 70));
        Console.WriteLine();

        if (result.TotalCount == 0)
        {
            Console.WriteLine($"  No archived data found in: {result.DataRoot}");
            Console.WriteLine();
            Console.WriteLine("  To backfill historical data, use:");
            Console.WriteLine("    Meridian --backfill --backfill-symbols AAPL,MSFT");
            Console.WriteLine();
            return;
        }

        Console.WriteLine($"  {"Symbol",-12} {"Files",-8} {"Size",-12} {"Date Range",-25} {"Types"}");
        Console.WriteLine($"  {new string('-', 12)} {new string('-', 8)} {new string('-', 12)} {new string('-', 25)} {new string('-', 20)}");

        foreach (var symbol in result.Symbols)
        {
            var sizeStr = FormatBytes(symbol.TotalSizeBytes);
            var dateRange = symbol.OldestData.HasValue && symbol.NewestData.HasValue
                ? $"{symbol.OldestData:yyyy-MM-dd} to {symbol.NewestData:yyyy-MM-dd}"
                : "Unknown";
            var types = string.Join(", ", symbol.DataTypes.Take(3));
            if (symbol.DataTypes.Length > 3)
                types += "...";

            Console.WriteLine($"  {symbol.Symbol,-12} {symbol.FileCount,-8} {sizeStr,-12} {dateRange,-25} {types}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Total: {result.TotalCount} symbol(s), {result.TotalFiles} files, {FormatBytes(result.TotalSizeBytes)}");
        Console.WriteLine($"  Data root: {result.DataRoot}");
        Console.WriteLine($"  Scan time: {result.ScanDurationMs}ms");
        Console.WriteLine();
    }

    /// <summary>
    /// Display a combined view of monitored and archived symbols.
    /// </summary>
    public async Task DisplayAllSymbolsAsync(CancellationToken ct = default)
    {
        var monitored = GetMonitoredSymbols();
        var archived = await GetArchivedSymbolsAsync(ct: ct);

        var allSymbols = monitored.Symbols.Select(s => s.Symbol)
            .Union(archived.Symbols.Select(s => s.Symbol), StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("Symbol Overview");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine();

        if (allSymbols.Count == 0)
        {
            Console.WriteLine("  No symbols found (neither monitored nor archived).");
            Console.WriteLine();
            return;
        }

        var monitoredSet = monitored.Symbols.ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);
        var archivedSet = archived.Symbols.ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"  {"Symbol",-12} {"Monitored",-12} {"Archived",-10} {"Files",-8} {"Size",-12} {"Date Range"}");
        Console.WriteLine($"  {new string('-', 12)} {new string('-', 12)} {new string('-', 10)} {new string('-', 8)} {new string('-', 12)} {new string('-', 25)}");

        foreach (var symbol in allSymbols)
        {
            var isMonitored = monitoredSet.ContainsKey(symbol);
            var hasArchived = archivedSet.TryGetValue(symbol, out var archiveInfo);

            var files = hasArchived ? archiveInfo!.FileCount.ToString() : "-";
            var size = hasArchived ? FormatBytes(archiveInfo!.TotalSizeBytes) : "-";
            var dateRange = hasArchived && archiveInfo!.OldestData.HasValue && archiveInfo.NewestData.HasValue
                ? $"{archiveInfo.OldestData:yyyy-MM-dd} to {archiveInfo.NewestData:yyyy-MM-dd}"
                : "-";

            Console.WriteLine($"  {symbol,-12} {(isMonitored ? "Yes" : "No"),-12} {(hasArchived ? "Yes" : "No"),-10} {files,-8} {size,-12} {dateRange}");
        }

        Console.WriteLine();
        Console.WriteLine($"  Total symbols: {allSymbols.Count}");
        Console.WriteLine($"  Monitored: {monitored.TotalCount}");
        Console.WriteLine($"  With archived data: {archived.TotalCount}");
        Console.WriteLine();
    }

    private void ScanDirectory(string path, Dictionary<string, SymbolFileStats> symbolStats, ArchivedSymbolsOptions options)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            {
                // Skip system directories
                if (file.Contains("/_") || file.Contains("\\_"))
                    continue;

                var extension = GetDataExtension(file);
                if (extension == null)
                    continue;

                var fileName = Path.GetFileName(file);
                var dirPath = Path.GetDirectoryName(file) ?? "";
                var relativePath = Path.GetRelativePath(_dataRoot, dirPath);

                // Try to extract symbol from file path
                var symbol = ExtractSymbolFromPath(relativePath, fileName);
                if (string.IsNullOrEmpty(symbol))
                    continue;

                if (options.FilterSymbols?.Length > 0 &&
                    !options.FilterSymbols.Contains(symbol, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!symbolStats.TryGetValue(symbol, out var stats))
                {
                    stats = new SymbolFileStats();
                    symbolStats[symbol] = stats;
                }

                stats.FileCount++;
                try
                {
                    var fileInfo = new FileInfo(file);
                    stats.TotalSizeBytes += fileInfo.Length;

                    // Try to extract date from file name or path
                    var date = ExtractDateFromPath(relativePath, fileName);
                    if (date.HasValue)
                    {
                        if (!stats.OldestDate.HasValue || date < stats.OldestDate)
                            stats.OldestDate = date;
                        if (!stats.NewestDate.HasValue || date > stats.NewestDate)
                            stats.NewestDate = date;
                    }
                }
                catch
                {
                    // Ignore file access errors
                }

                // Extract data type
                var dataType = ExtractDataType(relativePath, fileName);
                if (!string.IsNullOrEmpty(dataType))
                    stats.DataTypes.Add(dataType);

                // Extract source
                var source = ExtractSource(relativePath);
                if (!string.IsNullOrEmpty(source))
                    stats.Sources.Add(source);
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error scanning directory: {Path}", path);
        }
    }

    private static string? ExtractSymbolFromPath(string relativePath, string fileName)
    {
        // Remove extension
        var baseName = fileName;
        foreach (var ext in DataExtensions)
        {
            if (baseName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^ext.Length];
                break;
            }
        }

        // Pattern 1: BySymbol convention - symbol is a directory name
        // data/AAPL/Trade/2024-01-15.jsonl -> AAPL
        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            if (IsLikelySymbol(part) && !IsKnownDataType(part) && !IsDateString(part))
            {
                return part.ToUpperInvariant();
            }
        }

        // Pattern 2: Flat convention - symbol_type_date.jsonl
        // AAPL_Trade_2024-01-15.jsonl -> AAPL
        var underscoreParts = baseName.Split('_');
        if (underscoreParts.Length >= 1 && IsLikelySymbol(underscoreParts[0]))
        {
            return underscoreParts[0].ToUpperInvariant();
        }

        return null;
    }

    private static DateOnly? ExtractDateFromPath(string relativePath, string fileName)
    {
        // Look for date patterns like 2024-01-15, 2024_01_15, 20240115
        var datePatterns = new[]
        {
            @"(\d{4})-(\d{2})-(\d{2})",  // 2024-01-15
            @"(\d{4})_(\d{2})_(\d{2})",  // 2024_01_15
            @"(\d{4})(\d{2})(\d{2})"     // 20240115
        };

        var combinedPath = $"{relativePath}/{fileName}";

        foreach (var pattern in datePatterns)
        {
            var match = Regex.Match(combinedPath, pattern);
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

    private static string? ExtractDataType(string relativePath, string fileName)
    {
        var combinedPath = $"{relativePath}/{fileName}";

        foreach (var dataType in KnownDataTypes)
        {
            if (combinedPath.Contains(dataType, StringComparison.OrdinalIgnoreCase))
            {
                return dataType;
            }
        }

        return null;
    }

    private static string? ExtractSource(string relativePath)
    {
        var knownSources = new[] { "alpaca", "polygon", "yahoo", "stooq", "tiingo", "finnhub", "ib", "live", "historical", "backfill" };

        var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var part in parts)
        {
            var lower = part.ToLowerInvariant();
            if (knownSources.Contains(lower))
            {
                return part;
            }
        }

        return null;
    }

    private static bool IsLikelySymbol(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value.Length > 10)
            return false;
        if (value.Length < 1)
            return false;

        // Symbols are typically uppercase letters, possibly with dots or dashes
        return Regex.IsMatch(value, @"^[A-Za-z][A-Za-z0-9\.\-]{0,9}$");
    }

    private static bool IsKnownDataType(string value)
    {
        return KnownDataTypes.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDateString(string value)
    {
        return Regex.IsMatch(value, @"^\d{4}[-_]?\d{2}[-_]?\d{2}$") ||
               Regex.IsMatch(value, @"^\d{4}$") ||       // Year only
               Regex.IsMatch(value, @"^\d{2}$");        // Month or day only
    }

    private static string? GetDataExtension(string filePath)
    {
        foreach (var ext in DataExtensions)
        {
            if (filePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return ext;
            }
        }
        return null;
    }

    private static bool IsValidSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return false;
        if (symbol.Length > 20)
            return false;
        // Allow alphanumeric, dots, dashes, and spaces (for preferreds)
        return symbol.All(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == ' ');
    }

    private static SymbolConfig CreateSymbolConfig(string symbol, SymbolAddOptions options)
    {
        return new SymbolConfig(
            Symbol: symbol,
            SubscribeTrades: options.SubscribeTrades,
            SubscribeDepth: options.SubscribeDepth,
            DepthLevels: options.DepthLevels,
            SecurityType: options.SecurityType,
            Exchange: options.Exchange,
            Currency: options.Currency
        );
    }

    private static string BuildResultMessage(int added, int updated, int skipped)
    {
        var parts = new List<string>();
        if (added > 0)
            parts.Add($"added {added}");
        if (updated > 0)
            parts.Add($"updated {updated}");
        if (skipped > 0)
            parts.Add($"skipped {skipped}");
        return parts.Count > 0 ? string.Join(", ", parts) : "No changes made";
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private sealed class SymbolFileStats
    {
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }
        public DateOnly? OldestDate { get; set; }
        public DateOnly? NewestDate { get; set; }
        public List<string> DataTypes { get; } = new();
        public List<string> Sources { get; } = new();
    }
}


/// <summary>
/// Result of a symbol add/remove operation.
/// </summary>
public sealed record SymbolOperationResult(
    bool Success,
    string Message,
    string[] AffectedSymbols,
    int AddedCount = 0,
    int UpdatedCount = 0,
    int RemovedCount = 0,
    int SkippedCount = 0
);

/// <summary>
/// Options for adding symbols.
/// </summary>
public sealed record SymbolAddOptions(
    bool SubscribeTrades = true,
    bool SubscribeDepth = true,
    int DepthLevels = 10,
    string SecurityType = "STK",
    string Exchange = "SMART",
    string Currency = "USD",
    bool UpdateExisting = false
);

/// <summary>
/// Information about a monitored symbol.
/// </summary>
public sealed record MonitoredSymbolInfo(
    string Symbol,
    bool SubscribeTrades,
    bool SubscribeDepth,
    int DepthLevels,
    string SecurityType,
    string Exchange,
    string Currency
);

/// <summary>
/// Result of querying monitored symbols.
/// </summary>
public sealed record MonitoredSymbolsResult(
    MonitoredSymbolInfo[] Symbols,
    int TotalCount,
    int TradeSubscriptions,
    int DepthSubscriptions
);

/// <summary>
/// Information about archived data for a symbol.
/// </summary>
public sealed record ArchivedSymbolInfo(
    string Symbol,
    int FileCount,
    long TotalSizeBytes,
    DateOnly? OldestData,
    DateOnly? NewestData,
    string[] DataTypes,
    string[] Sources
);

/// <summary>
/// Result of scanning archived symbols.
/// </summary>
public sealed record ArchivedSymbolsResult(
    ArchivedSymbolInfo[] Symbols,
    int TotalCount,
    int TotalFiles,
    long TotalSizeBytes,
    string DataRoot,
    long ScanDurationMs
);

/// <summary>
/// Options for scanning archived symbols.
/// </summary>
public sealed record ArchivedSymbolsOptions(
    string[]? FilterSymbols = null,
    bool IncludeCompressed = true
);

/// <summary>
/// Combined status report for a single symbol.
/// </summary>
public sealed record SymbolStatusReport(
    string Symbol,
    bool IsMonitored,
    bool HasArchivedData,
    SymbolConfig? MonitoredConfig,
    ArchivedSymbolInfo? ArchivedInfo
);

