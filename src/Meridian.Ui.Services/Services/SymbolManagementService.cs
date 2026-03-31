using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Comprehensive service for symbol management including monitoring status,
/// archive status, dynamic add/remove, and detailed status information.
/// Provides CLI-equivalent functionality through the desktop UI.
/// </summary>
public sealed class SymbolManagementService
{
    private static readonly Lazy<SymbolManagementService> _instance = new(() => new SymbolManagementService());
    private readonly ApiClientService _apiClient;
    private readonly ConfigService _configService;

    public static SymbolManagementService Instance => _instance.Value;

    private SymbolManagementService()
    {
        _apiClient = ApiClientService.Instance;
        _configService = new ConfigService();
    }

    /// <summary>
    /// Gets all configured symbols (equivalent to --symbols CLI command).
    /// </summary>
    public async Task<SymbolListResult> GetAllSymbolsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<SymbolListResponse>(
            UiApiRoutes.Symbols,
            ct);

        if (response.Success && response.Data != null)
        {
            return new SymbolListResult
            {
                Success = true,
                Symbols = response.Data.Symbols?.ToList() ?? new List<SymbolInfo>(),
                TotalCount = response.Data.TotalCount
            };
        }

        // Fallback to local config
        var config = await _configService.LoadConfigAsync();
        var symbols = config?.Symbols?.Select(s => new SymbolInfo
        {
            Symbol = s.Symbol,
            SubscribeTrades = s.SubscribeTrades,
            SubscribeDepth = s.SubscribeDepth,
            DepthLevels = s.DepthLevels,
            Exchange = s.Exchange ?? "SMART",
            SecurityType = s.SecurityType ?? "STK",
            Currency = s.Currency ?? "USD",
            LocalSymbol = s.LocalSymbol,
            IsMonitored = false,
            HasArchivedData = false
        }).ToList() ?? new List<SymbolInfo>();

        return new SymbolListResult
        {
            Success = true,
            Symbols = symbols,
            TotalCount = symbols.Count,
            FromLocalConfig = true
        };
    }

    /// <summary>
    /// Gets currently monitored (active) symbols (equivalent to --symbols-monitored).
    /// </summary>
    public async Task<SymbolListResult> GetMonitoredSymbolsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<SymbolListResponse>(
            UiApiRoutes.SymbolsMonitored,
            ct);

        if (response.Success && response.Data != null)
        {
            return new SymbolListResult
            {
                Success = true,
                Symbols = response.Data.Symbols?.ToList() ?? new List<SymbolInfo>(),
                TotalCount = response.Data.TotalCount
            };
        }

        return new SymbolListResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get monitored symbols"
        };
    }

    /// <summary>
    /// Gets symbols with archived data (equivalent to --symbols-archived).
    /// </summary>
    public async Task<SymbolListResult> GetArchivedSymbolsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<SymbolListResponse>(
            UiApiRoutes.SymbolsArchived,
            ct);

        if (response.Success && response.Data != null)
        {
            return new SymbolListResult
            {
                Success = true,
                Symbols = response.Data.Symbols?.ToList() ?? new List<SymbolInfo>(),
                TotalCount = response.Data.TotalCount
            };
        }

        return new SymbolListResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get archived symbols"
        };
    }

    /// <summary>
    /// Gets detailed status for a specific symbol (equivalent to --symbol-status).
    /// </summary>
    public async Task<SymbolDetailedStatus> GetSymbolStatusAsync(string symbol, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<SymbolDetailedStatus>(
            UiApiRoutes.WithParam(UiApiRoutes.SymbolStatus, "symbol", symbol),
            ct);

        if (response.Success && response.Data != null)
        {
            return response.Data;
        }

        return new SymbolDetailedStatus
        {
            Symbol = symbol,
            Error = response.ErrorMessage ?? "Failed to get symbol status"
        };
    }

    /// <summary>
    /// Dynamically adds a symbol at runtime (equivalent to --symbols-add).
    /// </summary>
    public async Task<SymbolOperationResult> AddSymbolAsync(
        string symbol,
        bool subscribeTrades = true,
        bool subscribeDepth = false,
        int depthLevels = 10,
        string? exchange = null,
        CancellationToken ct = default)
    {
        var request = new AddSymbolRequest
        {
            Symbol = symbol.ToUpperInvariant(),
            SubscribeTrades = subscribeTrades,
            SubscribeDepth = subscribeDepth,
            DepthLevels = depthLevels,
            Exchange = exchange ?? "SMART",
            SecurityType = "STK",
            Currency = "USD"
        };

        var response = await _apiClient.PostWithResponseAsync<SymbolOperationResponse>(
            UiApiRoutes.SymbolsAdd,
            request,
            ct);

        if (response.Success && response.Data != null)
        {
            // Also update local config
            await _configService.AddOrUpdateSymbolAsync(new SymbolConfig
            {
                Symbol = request.Symbol,
                SubscribeTrades = request.SubscribeTrades,
                SubscribeDepth = request.SubscribeDepth,
                DepthLevels = request.DepthLevels,
                Exchange = request.Exchange,
                SecurityType = request.SecurityType,
                Currency = request.Currency
            });

            return new SymbolOperationResult
            {
                Success = true,
                Symbol = request.Symbol,
                Message = response.Data.Message ?? $"Symbol {request.Symbol} added successfully"
            };
        }

        return new SymbolOperationResult
        {
            Success = false,
            Symbol = symbol,
            Error = response.ErrorMessage ?? "Failed to add symbol"
        };
    }

    /// <summary>
    /// Dynamically removes a symbol at runtime (equivalent to --symbols-remove).
    /// </summary>
    public async Task<SymbolOperationResult> RemoveSymbolAsync(string symbol, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SymbolOperationResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.SymbolRemove, "symbol", symbol),
            null,
            ct);

        if (response.Success && response.Data != null)
        {
            // Also update local config
            await _configService.DeleteSymbolAsync(symbol);

            return new SymbolOperationResult
            {
                Success = true,
                Symbol = symbol,
                Message = response.Data.Message ?? $"Symbol {symbol} removed successfully"
            };
        }

        return new SymbolOperationResult
        {
            Success = false,
            Symbol = symbol,
            Error = response.ErrorMessage ?? "Failed to remove symbol"
        };
    }

    /// <summary>
    /// Enables or disables trades subscription for a symbol.
    /// </summary>
    public async Task<SymbolOperationResult> ToggleTradesAsync(string symbol, bool enabled, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SymbolOperationResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.SymbolTrades, "symbol", symbol),
            new { enabled },
            ct);

        if (response.Success)
        {
            return new SymbolOperationResult
            {
                Success = true,
                Symbol = symbol,
                Message = $"Trades {(enabled ? "enabled" : "disabled")} for {symbol}"
            };
        }

        return new SymbolOperationResult
        {
            Success = false,
            Symbol = symbol,
            Error = response.ErrorMessage ?? "Failed to toggle trades subscription"
        };
    }

    /// <summary>
    /// Enables or disables depth subscription for a symbol.
    /// </summary>
    public async Task<SymbolOperationResult> ToggleDepthAsync(string symbol, bool enabled, int depthLevels = 10, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SymbolOperationResponse>(
            UiApiRoutes.WithParam(UiApiRoutes.SymbolDepth, "symbol", symbol),
            new { enabled, depthLevels },
            ct);

        if (response.Success)
        {
            return new SymbolOperationResult
            {
                Success = true,
                Symbol = symbol,
                Message = $"Depth {(enabled ? "enabled" : "disabled")} for {symbol}"
            };
        }

        return new SymbolOperationResult
        {
            Success = false,
            Symbol = symbol,
            Error = response.ErrorMessage ?? "Failed to toggle depth subscription"
        };
    }

    /// <summary>
    /// Gets symbol statistics summary.
    /// </summary>
    public async Task<SymbolStatistics> GetSymbolStatisticsAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<SymbolStatistics>(
            UiApiRoutes.SymbolsStatistics,
            ct);

        return response.Data ?? new SymbolStatistics();
    }

    /// <summary>
    /// Validates a symbol format and availability.
    /// </summary>
    public async Task<SymbolValidationResult> ValidateSymbolAsync(string symbol, string? exchange = null, CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SymbolValidationResult>(
            UiApiRoutes.SymbolsValidate,
            new { symbol, exchange },
            ct);

        return response.Data ?? new SymbolValidationResult
        {
            Symbol = symbol,
            IsValid = false,
            Error = response.ErrorMessage ?? "Validation failed"
        };
    }

    /// <summary>
    /// Gets archive information for a symbol.
    /// </summary>
    public async Task<SymbolArchiveInfo> GetSymbolArchiveInfoAsync(string symbol, CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<SymbolArchiveInfo>(
            UiApiRoutes.WithParam(UiApiRoutes.SymbolArchive, "symbol", symbol),
            ct);

        return response.Data ?? new SymbolArchiveInfo { Symbol = symbol };
    }

    /// <summary>
    /// Bulk adds multiple symbols at once.
    /// </summary>
    public async Task<BulkSymbolOperationResult> BulkAddSymbolsAsync(
        IEnumerable<string> symbols,
        bool subscribeTrades = true,
        bool subscribeDepth = false,
        int depthLevels = 10,
        CancellationToken ct = default)
    {
        var symbolList = symbols.Select(s => s.ToUpperInvariant()).ToList();

        var response = await _apiClient.PostWithResponseAsync<BulkSymbolOperationResponse>(
            UiApiRoutes.SymbolsBulkAdd,
            new
            {
                symbols = symbolList,
                subscribeTrades,
                subscribeDepth,
                depthLevels
            },
            ct);

        if (response.Success && response.Data != null)
        {
            // Update local config for successful adds
            foreach (var symbol in response.Data.SuccessfulSymbols ?? Array.Empty<string>())
            {
                await _configService.AddOrUpdateSymbolAsync(new SymbolConfig
                {
                    Symbol = symbol,
                    SubscribeTrades = subscribeTrades,
                    SubscribeDepth = subscribeDepth,
                    DepthLevels = depthLevels,
                    Exchange = "SMART",
                    SecurityType = "STK",
                    Currency = "USD"
                });
            }

            return new BulkSymbolOperationResult
            {
                Success = true,
                SuccessfulSymbols = response.Data.SuccessfulSymbols?.ToList() ?? new List<string>(),
                FailedSymbols = response.Data.FailedSymbols?.ToList() ?? new List<string>(),
                Message = $"Added {response.Data.SuccessCount} symbols, {response.Data.FailCount} failed"
            };
        }

        return new BulkSymbolOperationResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Bulk add operation failed"
        };
    }

    /// <summary>
    /// Bulk removes multiple symbols at once.
    /// </summary>
    public async Task<BulkSymbolOperationResult> BulkRemoveSymbolsAsync(
        IEnumerable<string> symbols,
        CancellationToken ct = default)
    {
        var symbolList = symbols.Select(s => s.ToUpperInvariant()).ToList();

        var response = await _apiClient.PostWithResponseAsync<BulkSymbolOperationResponse>(
            UiApiRoutes.SymbolsBulkRemove,
            new { symbols = symbolList },
            ct);

        if (response.Success && response.Data != null)
        {
            // Update local config for successful removals
            foreach (var symbol in response.Data.SuccessfulSymbols ?? Array.Empty<string>())
            {
                await _configService.DeleteSymbolAsync(symbol);
            }

            return new BulkSymbolOperationResult
            {
                Success = true,
                SuccessfulSymbols = response.Data.SuccessfulSymbols?.ToList() ?? new List<string>(),
                FailedSymbols = response.Data.FailedSymbols?.ToList() ?? new List<string>(),
                Message = $"Removed {response.Data.SuccessCount} symbols, {response.Data.FailCount} failed"
            };
        }

        return new BulkSymbolOperationResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Bulk remove operation failed"
        };
    }

    /// <summary>
    /// Searches for symbols matching the query using the backend symbol search service.
    /// </summary>
    /// <param name="query">Search query (partial symbol or company name).</param>
    /// <param name="limit">Maximum number of results (default 10).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of matching symbols with metadata.</returns>
    public async Task<SymbolSearchResponse> SearchSymbolsAsync(
        string query,
        int limit = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SymbolSearchResponse
            {
                Success = true,
                Results = new List<SymbolSearchResultItem>(),
                TotalCount = 0,
                Query = query ?? string.Empty
            };
        }

        var encodedQuery = Uri.EscapeDataString(query);
        var response = await _apiClient.GetWithResponseAsync<SymbolSearchApiResponse>(
            UiApiRoutes.WithQuery(UiApiRoutes.SymbolsSearch, $"q={encodedQuery}&limit={limit}&includeFigi=false"),
            ct);

        if (response.Success && response.Data != null)
        {
            return new SymbolSearchResponse
            {
                Success = true,
                Results = response.Data.Results?.Select(r => new SymbolSearchResultItem
                {
                    Symbol = r.Symbol,
                    Name = r.Name,
                    Exchange = r.Exchange,
                    AssetType = r.AssetType,
                    MatchScore = r.MatchScore
                }).ToList() ?? new List<SymbolSearchResultItem>(),
                TotalCount = response.Data.TotalCount,
                Query = response.Data.Query ?? query,
                Sources = response.Data.Sources?.ToList() ?? new List<string>(),
                ElapsedMs = response.Data.ElapsedMs
            };
        }

        return new SymbolSearchResponse
        {
            Success = false,
            Results = new List<SymbolSearchResultItem>(),
            TotalCount = 0,
            Query = query,
            Error = response.ErrorMessage ?? "Symbol search failed"
        };
    }
}


public sealed class SymbolListResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<SymbolInfo> Symbols { get; set; } = new();
    public int TotalCount { get; set; }
    public bool FromLocalConfig { get; set; }
}

public sealed class SymbolInfo
{
    public string Symbol { get; set; } = string.Empty;
    public bool SubscribeTrades { get; set; }
    public bool SubscribeDepth { get; set; }
    public int DepthLevels { get; set; }
    public string Exchange { get; set; } = "SMART";
    public string SecurityType { get; set; } = "STK";
    public string Currency { get; set; } = "USD";
    public string? LocalSymbol { get; set; }
    public string? PrimaryExchange { get; set; }
    public bool IsMonitored { get; set; }
    public bool HasArchivedData { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public long TotalTrades { get; set; }
    public long TotalQuotes { get; set; }
}

public sealed class SymbolDetailedStatus
{
    public string Symbol { get; set; } = string.Empty;
    public string? Error { get; set; }

    // Subscription status
    public bool IsConfigured { get; set; }
    public bool IsMonitored { get; set; }
    public bool TradesSubscribed { get; set; }
    public bool DepthSubscribed { get; set; }
    public int DepthLevels { get; set; }

    // Runtime statistics
    public DateTime? FirstSeenTime { get; set; }
    public DateTime? LastTradeTime { get; set; }
    public DateTime? LastQuoteTime { get; set; }
    public long TotalTradesReceived { get; set; }
    public long TotalQuotesReceived { get; set; }
    public long TotalDepthUpdates { get; set; }
    public double TradesPerSecond { get; set; }
    public double QuotesPerSecond { get; set; }

    // Archive status
    public bool HasArchivedData { get; set; }
    public DateOnly? FirstArchiveDate { get; set; }
    public DateOnly? LastArchiveDate { get; set; }
    public long ArchivedTradeCount { get; set; }
    public long ArchivedQuoteCount { get; set; }
    public long ArchiveSizeBytes { get; set; }
    public int ArchiveDayCount { get; set; }

    // Data quality
    public double DataQualityScore { get; set; }
    public int SequenceGaps { get; set; }
    public int IntegrityIssues { get; set; }

    // Provider info
    public string? ActiveProvider { get; set; }
    public string? MappedProviderSymbol { get; set; }
}

public sealed class SymbolOperationResult
{
    public bool Success { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public sealed class BulkSymbolOperationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public List<string> SuccessfulSymbols { get; set; } = new();
    public List<string> FailedSymbols { get; set; } = new();
}

public sealed class SymbolStatistics
{
    public int TotalConfigured { get; set; }
    public int TotalMonitored { get; set; }
    public int TotalWithArchivedData { get; set; }
    public int TradesEnabled { get; set; }
    public int DepthEnabled { get; set; }
    public long TotalTradesCollected { get; set; }
    public long TotalQuotesCollected { get; set; }
    public double AverageTradesPerSymbol { get; set; }
    public DateTime? LastUpdateTime { get; set; }
}

public sealed class SymbolValidationResult
{
    public string Symbol { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public string? NormalizedSymbol { get; set; }
    public string? SuggestedExchange { get; set; }
    public string? SecurityType { get; set; }
    public bool IsAvailableForTrading { get; set; }
}

public sealed class SymbolArchiveInfo
{
    public string Symbol { get; set; } = string.Empty;
    public bool HasData { get; set; }
    public DateOnly? FirstDate { get; set; }
    public DateOnly? LastDate { get; set; }
    public int TotalDays { get; set; }
    public long TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<ArchiveFileInfo> Files { get; set; } = new();
}

public sealed class ArchiveFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string EventType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public long RecordCount { get; set; }
}



public sealed class AddSymbolRequest
{
    public string Symbol { get; set; } = string.Empty;
    public bool SubscribeTrades { get; set; }
    public bool SubscribeDepth { get; set; }
    public int DepthLevels { get; set; }
    public string Exchange { get; set; } = "SMART";
    public string SecurityType { get; set; } = "STK";
    public string Currency { get; set; } = "USD";
}

public sealed class SymbolListResponse
{
    public SymbolInfo[]? Symbols { get; set; }
    public int TotalCount { get; set; }
}

public sealed class SymbolOperationResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
}

public sealed class BulkSymbolOperationResponse
{
    public bool Success { get; set; }
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public string[]? SuccessfulSymbols { get; set; }
    public string[]? FailedSymbols { get; set; }
}

/// <summary>
/// Response from symbol search operation.
/// </summary>
public sealed class SymbolSearchResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<SymbolSearchResultItem> Results { get; set; } = new();
    public int TotalCount { get; set; }
    public string Query { get; set; } = string.Empty;
    public List<string> Sources { get; set; } = new();
    public long ElapsedMs { get; set; }
}

/// <summary>
/// Individual symbol search result item.
/// </summary>
public sealed class SymbolSearchResultItem
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Exchange { get; set; }
    public string? AssetType { get; set; }
    public int MatchScore { get; set; }

    /// <summary>
    /// Gets a display string combining symbol and name.
    /// </summary>
    public string DisplayText => string.IsNullOrEmpty(Name) || Name == Symbol
        ? Symbol
        : $"{Symbol} - {Name}";
}

/// <summary>
/// API response model for symbol search endpoint.
/// </summary>
public sealed class SymbolSearchApiResponse
{
    public SymbolSearchApiResult[]? Results { get; set; }
    public int TotalCount { get; set; }
    public string[]? Sources { get; set; }
    public long ElapsedMs { get; set; }
    public string? Query { get; set; }
}

/// <summary>
/// Individual result from the API response.
/// </summary>
public sealed class SymbolSearchApiResult
{
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Exchange { get; set; }
    public string? AssetType { get; set; }
    public string? Country { get; set; }
    public string? Currency { get; set; }
    public string? Source { get; set; }
    public int MatchScore { get; set; }
    public string? Figi { get; set; }
    public string? CompositeFigi { get; set; }
}

