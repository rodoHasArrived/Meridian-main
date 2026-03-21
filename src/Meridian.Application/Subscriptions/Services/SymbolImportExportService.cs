using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.UI;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for bulk import and export of symbol subscriptions via CSV, text files, and other formats.
/// </summary>
public sealed class SymbolImportExportService
{
    private readonly ConfigStore _configStore;

    // Common comment prefixes to strip
    private static readonly char[] CommentChars = { '#', ';', '/' };

    public SymbolImportExportService(ConfigStore configStore)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    }

    /// <summary>
    /// Import symbols from plain text content (one symbol per line).
    /// Supports comments (#, ;, //) and inline comments.
    /// </summary>
    public async Task<BulkImportResult> ImportFromTextAsync(
        string textContent,
        BulkImportOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new BulkImportOptions(HasHeader: false);
        var stopwatch = Stopwatch.StartNew();

        var errors = new List<ImportError>();
        var imported = new List<string>();
        var skipped = 0;

        var lines = textContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return new BulkImportResult(0, 0, 0, Array.Empty<ImportError>(), Array.Empty<string>(), 0);
        }

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        var symbolsToAdd = new List<SymbolConfig>();
        var defaults = options.Defaults ?? new ImportDefaults();

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();

            // Skip empty lines and full-line comments
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith('#') || line.StartsWith(';') || line.StartsWith("//"))
                continue;

            // Strip inline comments
            var commentIndex = line.IndexOf('#');
            if (commentIndex > 0)
                line = line[..commentIndex].Trim();
            commentIndex = line.IndexOf(';');
            if (commentIndex > 0)
                line = line[..commentIndex].Trim();
            commentIndex = line.IndexOf("//");
            if (commentIndex > 0)
                line = line[..commentIndex].Trim();

            // Handle comma-separated or whitespace-separated symbols on same line
            var symbols = Regex.Split(line, @"[\s,]+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim().ToUpperInvariant())
                .ToArray();

            foreach (var symbol in symbols)
            {
                if (string.IsNullOrWhiteSpace(symbol))
                    continue;

                if (options.ValidateSymbols && !IsValidSymbol(symbol))
                {
                    errors.Add(new ImportError(lineNumber, symbol, "Invalid symbol format"));
                    continue;
                }

                if (existingSymbols.ContainsKey(symbol))
                {
                    if (options.SkipExisting && !options.UpdateExisting)
                    {
                        skipped++;
                        continue;
                    }
                    if (options.UpdateExisting)
                    {
                        var symbolConfig = new SymbolConfig(
                            Symbol: symbol,
                            SubscribeTrades: defaults.SubscribeTrades,
                            SubscribeDepth: defaults.SubscribeDepth,
                            DepthLevels: defaults.DepthLevels,
                            SecurityType: defaults.SecurityType,
                            Exchange: defaults.Exchange,
                            Currency: defaults.Currency
                        );
                        existingSymbols[symbol] = symbolConfig;
                        imported.Add(symbol);
                    }
                }
                else
                {
                    var symbolConfig = new SymbolConfig(
                        Symbol: symbol,
                        SubscribeTrades: defaults.SubscribeTrades,
                        SubscribeDepth: defaults.SubscribeDepth,
                        DepthLevels: defaults.DepthLevels,
                        SecurityType: defaults.SecurityType,
                        Exchange: defaults.Exchange,
                        Currency: defaults.Currency
                    );
                    symbolsToAdd.Add(symbolConfig);
                    existingSymbols[symbol] = symbolConfig; // Prevent duplicates in same file
                    imported.Add(symbol);
                }
            }
        }

        // Save changes
        if (symbolsToAdd.Count > 0 || options.UpdateExisting)
        {
            var allSymbols = existingSymbols.Values.ToList();
            var next = cfg with { Symbols = allSymbols.ToArray() };
            await _configStore.SaveAsync(next);
        }

        stopwatch.Stop();

        return new BulkImportResult(
            SuccessCount: imported.Count,
            FailureCount: errors.Count,
            SkippedCount: skipped,
            Errors: errors.ToArray(),
            ImportedSymbols: imported.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Detect import format and route to appropriate handler.
    /// </summary>
    public async Task<BulkImportResult> ImportAutoDetectAsync(
        string content,
        BulkImportOptions? options = null,
        CancellationToken ct = default)
    {
        // Detect CSV by checking for commas in first non-empty, non-comment line
        var firstDataLine = content.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => !string.IsNullOrEmpty(l) && !l.StartsWith('#') && !l.StartsWith(';'));

        if (firstDataLine != null && firstDataLine.Contains(',') && firstDataLine.Contains("Symbol"))
        {
            // Likely CSV with header
            return await ImportFromCsvAsync(content, options, ct);
        }

        // Default to plain text import
        return await ImportFromTextAsync(content, options ?? new BulkImportOptions(HasHeader: false), ct);
    }

    /// <summary>
    /// Import symbols from CSV content.
    /// </summary>
    public async Task<BulkImportResult> ImportFromCsvAsync(
        string csvContent,
        BulkImportOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new BulkImportOptions();
        var stopwatch = Stopwatch.StartNew();

        var errors = new List<ImportError>();
        var imported = new List<string>();
        var skipped = 0;

        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return new BulkImportResult(0, 0, 0, Array.Empty<ImportError>(), Array.Empty<string>(), 0);
        }

        var startIndex = 0;
        Dictionary<string, int>? columnMap = null;

        if (options.HasHeader && lines.Length > 0)
        {
            columnMap = ParseHeader(lines[0]);
            startIndex = 1;
        }

        var cfg = _configStore.Load();
        var existingSymbols = (cfg.Symbols ?? Array.Empty<SymbolConfig>())
            .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

        var symbolsToAdd = new List<SymbolConfig>();

        for (var i = startIndex; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                var symbol = ParseCsvLine(line, columnMap, options.Defaults);
                if (symbol is null)
                {
                    errors.Add(new ImportError(lineNumber, null, "Failed to parse line"));
                    continue;
                }

                if (options.ValidateSymbols && !IsValidSymbol(symbol.Symbol))
                {
                    errors.Add(new ImportError(lineNumber, symbol.Symbol, "Invalid symbol format"));
                    continue;
                }

                if (existingSymbols.ContainsKey(symbol.Symbol))
                {
                    if (options.SkipExisting && !options.UpdateExisting)
                    {
                        skipped++;
                        continue;
                    }
                    if (options.UpdateExisting)
                    {
                        existingSymbols[symbol.Symbol] = symbol;
                        imported.Add(symbol.Symbol);
                    }
                }
                else
                {
                    symbolsToAdd.Add(symbol);
                    imported.Add(symbol.Symbol);
                }
            }
            catch (Exception ex)
            {
                errors.Add(new ImportError(lineNumber, null, ex.Message));
            }
        }

        // Save changes
        if (symbolsToAdd.Count > 0 || options.UpdateExisting)
        {
            var allSymbols = existingSymbols.Values.ToList();
            allSymbols.AddRange(symbolsToAdd);
            var next = cfg with { Symbols = allSymbols.ToArray() };
            await _configStore.SaveAsync(next);
        }

        stopwatch.Stop();

        return new BulkImportResult(
            SuccessCount: imported.Count,
            FailureCount: errors.Count,
            SkippedCount: skipped,
            Errors: errors.ToArray(),
            ImportedSymbols: imported.ToArray(),
            ProcessingTimeMs: stopwatch.ElapsedMilliseconds
        );
    }

    /// <summary>
    /// Export current symbol subscriptions to CSV.
    /// </summary>
    public string ExportToCsv(BulkExportOptions? options = null)
    {
        options ??= new BulkExportOptions();

        var cfg = _configStore.Load();
        var symbols = cfg.Symbols ?? Array.Empty<SymbolConfig>();

        if (options.FilterSymbols is { Length: > 0 })
        {
            var filter = new HashSet<string>(options.FilterSymbols, StringComparer.OrdinalIgnoreCase);
            symbols = symbols.Where(s => filter.Contains(s.Symbol)).ToArray();
        }

        var columns = options.Columns ?? CsvColumns.Standard;
        var sb = new StringBuilder();

        if (options.IncludeHeader)
        {
            sb.AppendLine(string.Join(",", columns));
        }

        foreach (var symbol in symbols)
        {
            var values = columns.Select(col => GetColumnValue(symbol, col, options.IncludeMetadata));
            sb.AppendLine(string.Join(",", values));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Get export as bytes for file download.
    /// </summary>
    public byte[] ExportToCsvBytes(BulkExportOptions? options = null)
    {
        var csv = ExportToCsv(options);
        return Encoding.UTF8.GetBytes(csv);
    }

    private static Dictionary<string, int> ParseHeader(string headerLine)
    {
        var columns = ParseCsvValues(headerLine);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Length; i++)
        {
            map[columns[i].Trim()] = i;
        }
        return map;
    }

    private static SymbolConfig? ParseCsvLine(
        string line,
        Dictionary<string, int>? columnMap,
        ImportDefaults? defaults)
    {
        var values = ParseCsvValues(line);
        if (values.Length == 0)
            return null;

        defaults ??= new ImportDefaults();

        string GetValue(string column, string defaultValue = "")
        {
            if (columnMap is null)
            {
                // Assume standard column order
                return column switch
                {
                    CsvColumns.Symbol => values.Length > 0 ? values[0].Trim() : defaultValue,
                    CsvColumns.SubscribeTrades => values.Length > 1 ? values[1].Trim() : defaultValue,
                    CsvColumns.SubscribeDepth => values.Length > 2 ? values[2].Trim() : defaultValue,
                    CsvColumns.DepthLevels => values.Length > 3 ? values[3].Trim() : defaultValue,
                    CsvColumns.SecurityType => values.Length > 4 ? values[4].Trim() : defaultValue,
                    CsvColumns.Exchange => values.Length > 5 ? values[5].Trim() : defaultValue,
                    CsvColumns.Currency => values.Length > 6 ? values[6].Trim() : defaultValue,
                    CsvColumns.PrimaryExchange => values.Length > 7 ? values[7].Trim() : defaultValue,
                    _ => defaultValue
                };
            }

            if (columnMap.TryGetValue(column, out var index) && index < values.Length)
            {
                return values[index].Trim();
            }
            return defaultValue;
        }

        var symbol = GetValue(CsvColumns.Symbol);
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        return new SymbolConfig(
            Symbol: symbol,
            SubscribeTrades: ParseBool(GetValue(CsvColumns.SubscribeTrades), defaults.SubscribeTrades),
            SubscribeDepth: ParseBool(GetValue(CsvColumns.SubscribeDepth), defaults.SubscribeDepth),
            DepthLevels: ParseInt(GetValue(CsvColumns.DepthLevels), defaults.DepthLevels),
            SecurityType: GetValueOrDefault(GetValue(CsvColumns.SecurityType), defaults.SecurityType),
            Exchange: GetValueOrDefault(GetValue(CsvColumns.Exchange), defaults.Exchange),
            Currency: GetValueOrDefault(GetValue(CsvColumns.Currency), defaults.Currency),
            PrimaryExchange: GetNullableValue(GetValue(CsvColumns.PrimaryExchange)),
            LocalSymbol: GetNullableValue(GetValue(CsvColumns.LocalSymbol)),
            TradingClass: GetNullableValue(GetValue(CsvColumns.TradingClass)),
            ConId: ParseNullableInt(GetValue(CsvColumns.ConId))
        );
    }

    private static string[] ParseCsvValues(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        values.Add(current.ToString());

        return values.ToArray();
    }

    private static string GetColumnValue(SymbolConfig symbol, string column, bool includeMetadata)
    {
        var value = column switch
        {
            CsvColumns.Symbol => symbol.Symbol,
            CsvColumns.SubscribeTrades => symbol.SubscribeTrades.ToString().ToLower(),
            CsvColumns.SubscribeDepth => symbol.SubscribeDepth.ToString().ToLower(),
            CsvColumns.DepthLevels => symbol.DepthLevels.ToString(),
            CsvColumns.SecurityType => symbol.SecurityType,
            CsvColumns.Exchange => symbol.Exchange,
            CsvColumns.Currency => symbol.Currency,
            CsvColumns.PrimaryExchange => symbol.PrimaryExchange ?? "",
            CsvColumns.LocalSymbol => symbol.LocalSymbol ?? "",
            CsvColumns.TradingClass => symbol.TradingClass ?? "",
            CsvColumns.ConId => symbol.ConId?.ToString() ?? "",
            _ => ""
        };

        // Escape CSV value if needed
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            value = $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
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

    private static bool ParseBool(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        if (bool.TryParse(value, out var result))
            return result;
        if (value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;
        return defaultValue;
    }

    private static int ParseInt(string value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return int.TryParse(value, out var result) ? result : defaultValue;
    }

    private static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return int.TryParse(value, out var result) ? result : null;
    }

    private static string GetValueOrDefault(string value, string defaultValue)
    {
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
    }

    private static string? GetNullableValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
