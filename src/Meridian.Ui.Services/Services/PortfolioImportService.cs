using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for importing portfolios from CSV and JSON files.
/// Supports bulk symbol import and portfolio management.
/// </summary>
public sealed class PortfolioImportService
{
    private static readonly Lazy<PortfolioImportService> _instance = new(() => new PortfolioImportService());
    private readonly ApiClientService _apiClient;

    public static PortfolioImportService Instance => _instance.Value;

    private PortfolioImportService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Parses a CSV file containing portfolio symbols.
    /// Supports various CSV formats with automatic header detection.
    /// </summary>
    public async Task<PortfolioParseResult> ParseCsvAsync(string filePath, CancellationToken ct = default)
    {
        var result = new PortfolioParseResult();

        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, ct);
            if (lines.Length == 0)
            {
                result.Error = "File is empty";
                return result;
            }

            // Detect header and symbol column
            var headers = lines[0].Split(',').Select(h => h.Trim().ToLowerInvariant()).ToArray();
            var symbolColumnIndex = FindSymbolColumn(headers);
            var quantityColumnIndex = FindColumn(headers, "quantity", "qty", "shares", "amount");
            var hasHeader = symbolColumnIndex >= 0;

            var startIndex = hasHeader ? 1 : 0;
            if (!hasHeader) symbolColumnIndex = 0;

            for (var i = startIndex; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var parts = lines[i].Split(',');
                if (parts.Length <= symbolColumnIndex) continue;

                var symbol = parts[symbolColumnIndex].Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) continue;

                // Remove any quotes
                symbol = symbol.Trim('"', '\'');

                var entry = new PortfolioEntry
                {
                    Symbol = symbol,
                    Quantity = quantityColumnIndex >= 0 && parts.Length > quantityColumnIndex
                        ? ParseDecimal(parts[quantityColumnIndex])
                        : null
                };

                result.Entries.Add(entry);
            }

            result.Success = result.Entries.Count > 0;
            result.TotalSymbols = result.Entries.Count;
        }
        catch (Exception ex)
        {
            result.Error = $"Failed to parse CSV: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Parses a JSON file containing portfolio data.
    /// Supports both array format and object format with symbols property.
    /// </summary>
    public async Task<PortfolioParseResult> ParseJsonAsync(string filePath, CancellationToken ct = default)
    {
        var result = new PortfolioParseResult();

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // Array format: ["AAPL", "MSFT"] or [{"symbol": "AAPL"}, ...]
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var entry = ParseJsonElement(element);
                    if (entry != null)
                    {
                        result.Entries.Add(entry);
                    }
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                // Object format: {"symbols": [...], "portfolio": [...], etc.}
                foreach (var prop in new[] { "symbols", "portfolio", "holdings", "positions", "tickers" })
                {
                    if (doc.RootElement.TryGetProperty(prop, out var arrayElement) &&
                        arrayElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in arrayElement.EnumerateArray())
                        {
                            var entry = ParseJsonElement(element);
                            if (entry != null)
                            {
                                result.Entries.Add(entry);
                            }
                        }
                        break;
                    }
                }
            }

            result.Success = result.Entries.Count > 0;
            result.TotalSymbols = result.Entries.Count;
        }
        catch (Exception ex)
        {
            result.Error = $"Failed to parse JSON: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Imports portfolio entries as symbol subscriptions.
    /// </summary>
    public async Task<PortfolioImportResult> ImportAsSubscriptionsAsync(
        IEnumerable<PortfolioEntry> entries,
        bool enableTrades = true,
        bool enableDepth = false,
        int depthLevels = 5,
        CancellationToken ct = default)
    {
        var result = new PortfolioImportResult();
        var symbols = entries.Select(e => e.Symbol).Distinct().ToList();

        try
        {
            var response = await _apiClient.PostWithResponseAsync<BatchOperationResponse>(
                "/api/symbols/batch",
                new
                {
                    operation = "add",
                    symbols = symbols.Select(s => new
                    {
                        symbol = s,
                        trades = enableTrades,
                        depth = enableDepth,
                        depthLevels
                    })
                },
                ct);

            if (response.Success && response.Data != null)
            {
                result.Success = true;
                result.ImportedCount = response.Data.SuccessCount;
                result.SkippedCount = response.Data.SkippedCount;
                result.FailedCount = response.Data.FailedCount;
                result.Errors = response.Data.Errors?.ToList() ?? new List<string>();
            }
            else
            {
                result.Error = response.ErrorMessage ?? "Failed to import symbols";
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Imports portfolio entries to a watchlist.
    /// </summary>
    public async Task<PortfolioImportResult> ImportToWatchlistAsync(
        IEnumerable<PortfolioEntry> entries,
        string watchlistName,
        CancellationToken ct = default)
    {
        var result = new PortfolioImportResult();
        var symbols = entries.Select(e => e.Symbol).Distinct().ToList();

        try
        {
            var watchlistService = WatchlistService.Instance;
            var success = await watchlistService.CreateOrUpdateWatchlistAsync(watchlistName, symbols, ct);
            result.Success = success;
            result.ImportedCount = success ? symbols.Count : 0;
            if (!success)
            {
                result.Error = "Failed to create or update watchlist. The WatchlistService may not be initialized.";
            }
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Gets common index constituents for quick import.
    /// </summary>
    public async Task<IndexConstituentsResult> GetIndexConstituentsAsync(string indexName, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<IndexConstituentsResponse>(
            $"/api/indices/{indexName}/constituents",
            ct);

        if (response.Success && response.Data != null)
        {
            return new IndexConstituentsResult
            {
                Success = true,
                IndexName = response.Data.IndexName,
                Symbols = response.Data.Symbols?.ToList() ?? new List<string>()
            };
        }

        // Fallback to hardcoded common indices
        return GetHardcodedIndex(indexName);
    }

    /// <summary>
    /// Imports a list of symbols as subscriptions.
    /// </summary>
    public Task<PortfolioImportResult> ImportSymbolsAsync(
        IEnumerable<string> symbols,
        bool enableTrades = true,
        bool enableDepth = false,
        int depthLevels = 5,
        CancellationToken ct = default)
    {
        var entries = symbols.Select(s => new PortfolioEntry { Symbol = s });
        return ImportAsSubscriptionsAsync(entries, enableTrades, enableDepth, depthLevels, ct);
    }

    private static IndexConstituentsResult GetHardcodedIndex(string indexName)
    {
        return indexName.ToUpperInvariant() switch
        {
            "SP500" or "SPX" or "S&P500" => new IndexConstituentsResult
            {
                Success = true,
                IndexName = "S&P 500",
                Symbols = new List<string>
                {
                    "AAPL", "MSFT", "AMZN", "NVDA", "GOOGL", "META", "TSLA", "BRK.B", "UNH", "XOM",
                    "JNJ", "JPM", "V", "PG", "MA", "HD", "CVX", "MRK", "ABBV", "LLY",
                    "PEP", "KO", "COST", "AVGO", "WMT", "MCD", "CSCO", "TMO", "ABT", "ACN"
                    // Top 30 by weight - full list would be 500+
                }
            },
            "QQQ" or "NDX" or "NASDAQ100" => new IndexConstituentsResult
            {
                Success = true,
                IndexName = "Nasdaq-100",
                Symbols = new List<string>
                {
                    "AAPL", "MSFT", "AMZN", "NVDA", "META", "GOOGL", "GOOG", "TSLA", "AVGO", "COST",
                    "PEP", "CSCO", "NFLX", "AMD", "ADBE", "INTC", "CMCSA", "TMUS", "TXN", "QCOM"
                }
            },
            "DIA" or "DJIA" or "DOW30" => new IndexConstituentsResult
            {
                Success = true,
                IndexName = "Dow Jones 30",
                Symbols = new List<string>
                {
                    "AAPL", "MSFT", "UNH", "GS", "HD", "MCD", "V", "CAT", "AMGN", "CRM",
                    "BA", "HON", "TRV", "AXP", "JPM", "IBM", "JNJ", "PG", "CVX", "MRK",
                    "DIS", "NKE", "KO", "MMM", "WBA", "VZ", "INTC", "CSCO", "DOW", "WMT"
                }
            },
            "IWM" or "RTY" or "RUSSELL2000" => new IndexConstituentsResult
            {
                Success = true,
                IndexName = "Russell 2000 (Sample)",
                Symbols = new List<string>
                {
                    "AMC", "SFIX", "PLUG", "RKT", "SPCE", "CLOV", "WISH", "SOFI", "HOOD", "RIVN"
                }
            },
            _ => new IndexConstituentsResult
            {
                Success = false,
                Error = $"Unknown index: {indexName}"
            }
        };
    }

    private static int FindSymbolColumn(string[] headers)
    {
        var symbolHeaders = new[] { "symbol", "ticker", "sym", "stock", "security", "name" };
        for (var i = 0; i < headers.Length; i++)
        {
            if (symbolHeaders.Contains(headers[i]))
                return i;
        }
        return -1;
    }

    private static int FindColumn(string[] headers, params string[] names)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            if (names.Contains(headers[i]))
                return i;
        }
        return -1;
    }

    private static decimal? ParseDecimal(string value)
    {
        if (decimal.TryParse(value.Trim().Trim('"'), out var result))
            return result;
        return null;
    }

    private static PortfolioEntry? ParseJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var symbol = element.GetString()?.Trim().ToUpperInvariant();
            return string.IsNullOrEmpty(symbol) ? null : new PortfolioEntry { Symbol = symbol };
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            string? symbol = null;
            decimal? quantity = null;

            foreach (var prop in new[] { "symbol", "ticker", "sym" })
            {
                if (element.TryGetProperty(prop, out var symElement))
                {
                    symbol = symElement.GetString()?.Trim().ToUpperInvariant();
                    break;
                }
            }

            foreach (var prop in new[] { "quantity", "qty", "shares", "amount" })
            {
                if (element.TryGetProperty(prop, out var qtyElement))
                {
                    if (qtyElement.TryGetDecimal(out var qty))
                        quantity = qty;
                    break;
                }
            }

            return string.IsNullOrEmpty(symbol) ? null : new PortfolioEntry { Symbol = symbol, Quantity = quantity };
        }

        return null;
    }
}

/// <summary>
/// Result of parsing a portfolio file.
/// </summary>
public sealed class PortfolioParseResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int TotalSymbols { get; set; }
    public List<PortfolioEntry> Entries { get; set; } = new();
}

/// <summary>
/// A single portfolio entry.
/// </summary>
public sealed class PortfolioEntry
{
    public string Symbol { get; set; } = string.Empty;
    public decimal? Quantity { get; set; }
    public string? Exchange { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// Result of importing portfolio entries.
/// </summary>
public sealed class PortfolioImportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of fetching index constituents.
/// </summary>
public sealed class IndexConstituentsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string IndexName { get; set; } = string.Empty;
    public List<string> Symbols { get; set; } = new();
}

/// <summary>
/// Response from batch operation API.
/// </summary>
public sealed class BatchOperationResponse
{
    public int SuccessCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public string[]? Errors { get; set; }
}

/// <summary>
/// Response from index constituents API.
/// </summary>
public sealed class IndexConstituentsResponse
{
    public string IndexName { get; set; } = string.Empty;
    public string[]? Symbols { get; set; }
}
