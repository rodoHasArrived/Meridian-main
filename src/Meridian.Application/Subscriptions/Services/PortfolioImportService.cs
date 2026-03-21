using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Application.Subscriptions.Models;
using Meridian.Application.UI;
using Meridian.Infrastructure.Http;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for importing symbols from broker portfolios.
/// Supports Alpaca, Interactive Brokers, and manual entry.
/// </summary>
public sealed class PortfolioImportService
{
    private readonly ConfigStore _configStore;
    private readonly WatchlistService _watchlistService;
    private readonly HttpClient _httpClient;

    public PortfolioImportService(
        ConfigStore configStore,
        WatchlistService watchlistService,
        HttpClient? httpClient = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _watchlistService = watchlistService ?? throw new ArgumentNullException(nameof(watchlistService));
        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _httpClient = httpClient ?? HttpClientFactoryProvider.CreateClient(HttpClientNames.PortfolioImport);
    }

    /// <summary>
    /// Import symbols from a broker portfolio.
    /// </summary>
    public async Task<PortfolioImportResult> ImportFromBrokerAsync(
        PortfolioImportRequest request,
        CancellationToken ct = default)
    {
        return request.Broker.ToLowerInvariant() switch
        {
            "alpaca" => await ImportFromAlpacaAsync(request.Options, ct),
            "ib" or "interactivebrokers" => await ImportFromInteractiveBrokersAsync(request.Options, ct),
            "manual" => new PortfolioImportResult(false, "manual", 0, 0, 0, Array.Empty<string>(),
                new[] { "Use ImportManualAsync for manual entries" }),
            _ => new PortfolioImportResult(false, request.Broker, 0, 0, 0, Array.Empty<string>(),
                new[] { $"Unsupported broker: {request.Broker}" })
        };
    }

    /// <summary>
    /// Import symbols from Alpaca portfolio.
    /// </summary>
    public async Task<PortfolioImportResult> ImportFromAlpacaAsync(
        PortfolioImportOptions options,
        CancellationToken ct = default)
    {
        var config = _configStore.Load();
        var alpacaConfig = config.Alpaca;

        if (alpacaConfig is null)
        {
            return new PortfolioImportResult(false, "alpaca", 0, 0, 0, Array.Empty<string>(),
                new[] { "Alpaca configuration not found. Please configure Alpaca credentials." });
        }

        try
        {
            var portfolio = await FetchAlpacaPortfolioAsync(alpacaConfig, ct);
            return await ProcessPortfolioAsync(portfolio, options, ct);
        }
        catch (Exception ex)
        {
            return new PortfolioImportResult(false, "alpaca", 0, 0, 0, Array.Empty<string>(),
                new[] { $"Failed to fetch Alpaca portfolio: {ex.Message}" });
        }
    }

    /// <summary>
    /// Import symbols from Interactive Brokers portfolio.
    /// </summary>
    public async Task<PortfolioImportResult> ImportFromInteractiveBrokersAsync(
        PortfolioImportOptions options,
        CancellationToken ct = default)
    {
        var config = _configStore.Load();
        var ibConfig = config.IB;

        if (ibConfig is null)
        {
            return new PortfolioImportResult(false, "ib", 0, 0, 0, Array.Empty<string>(),
                new[] { "Interactive Brokers configuration not found. Please configure IB gateway connection." });
        }

        try
        {
            var portfolio = await FetchInteractiveBrokersPortfolioAsync(ibConfig, ct);
            return await ProcessPortfolioAsync(portfolio, options, ct);
        }
        catch (Exception ex)
        {
            return new PortfolioImportResult(false, "ib", 0, 0, 0, Array.Empty<string>(),
                new[] { $"Failed to fetch IB portfolio: {ex.Message}" });
        }
    }

    /// <summary>
    /// Import symbols from manual entries.
    /// </summary>
    public async Task<PortfolioImportResult> ImportManualAsync(
        ManualPortfolioEntry[] entries,
        PortfolioImportOptions options,
        CancellationToken ct = default)
    {
        var positions = entries.Select(e => new PortfolioPosition(
            Symbol: e.Symbol.ToUpperInvariant(),
            Quantity: e.Quantity ?? 0,
            MarketValue: null,
            AverageCost: null,
            UnrealizedPnL: null,
            AssetClass: e.AssetClass ?? "STK",
            Exchange: null,
            Currency: "USD",
            Side: "long"
        )).ToArray();

        var portfolio = new PortfolioSummary(
            Broker: "manual",
            AccountId: "manual",
            TotalValue: null,
            CashBalance: null,
            BuyingPower: null,
            Positions: positions,
            RetrievedAt: DateTimeOffset.UtcNow,
            Currency: "USD"
        );

        return await ProcessPortfolioAsync(portfolio, options, ct);
    }

    /// <summary>
    /// Get portfolio summary without importing.
    /// </summary>
    public async Task<PortfolioSummary?> GetPortfolioSummaryAsync(string broker, CancellationToken ct = default)
    {
        var config = _configStore.Load();

        return broker.ToLowerInvariant() switch
        {
            "alpaca" when config.Alpaca is not null => await FetchAlpacaPortfolioAsync(config.Alpaca, ct),
            "ib" or "interactivebrokers" when config.IB is not null => await FetchInteractiveBrokersPortfolioAsync(config.IB, ct),
            _ => null
        };
    }

    /// <summary>
    /// Get available brokers based on configuration.
    /// </summary>
    public IReadOnlyList<string> GetAvailableBrokers()
    {
        var config = _configStore.Load();
        var brokers = new List<string> { "manual" };

        if (config.Alpaca is not null)
            brokers.Add("alpaca");

        if (config.IB is not null)
            brokers.Add("interactivebrokers");

        return brokers;
    }

    private async Task<PortfolioSummary> FetchAlpacaPortfolioAsync(AlpacaOptions alpacaConfig, CancellationToken ct)
    {
        var baseUrl = alpacaConfig.UseSandbox
            ? "https://paper-api.alpaca.markets"
            : "https://api.alpaca.markets";

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/positions");
        request.Headers.Add("APCA-API-KEY-ID", alpacaConfig.KeyId);
        request.Headers.Add("APCA-API-SECRET-KEY", alpacaConfig.SecretKey);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var positionsJson = await response.Content.ReadFromJsonAsync<AlpacaPosition[]>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        // Also get account info
        using var accountRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/account");
        accountRequest.Headers.Add("APCA-API-KEY-ID", alpacaConfig.KeyId);
        accountRequest.Headers.Add("APCA-API-SECRET-KEY", alpacaConfig.SecretKey);

        var accountResponse = await _httpClient.SendAsync(accountRequest, ct);
        accountResponse.EnsureSuccessStatusCode();

        var account = await accountResponse.Content.ReadFromJsonAsync<AlpacaAccount>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        var positions = (positionsJson ?? Array.Empty<AlpacaPosition>())
            .Select(p => new PortfolioPosition(
                Symbol: p.Symbol,
                Quantity: decimal.Parse(p.Qty ?? "0"),
                MarketValue: decimal.TryParse(p.MarketValue, out var mv) ? mv : null,
                AverageCost: decimal.TryParse(p.AvgEntryPrice, out var ac) ? ac : null,
                UnrealizedPnL: decimal.TryParse(p.UnrealizedPl, out var pnl) ? pnl : null,
                AssetClass: p.AssetClass ?? "us_equity",
                Exchange: p.Exchange,
                Currency: "USD",
                Side: p.Side ?? "long"
            ))
            .ToArray();

        return new PortfolioSummary(
            Broker: "alpaca",
            AccountId: account?.AccountNumber ?? "unknown",
            TotalValue: decimal.TryParse(account?.PortfolioValue, out var tv) ? tv : null,
            CashBalance: decimal.TryParse(account?.Cash, out var cash) ? cash : null,
            BuyingPower: decimal.TryParse(account?.BuyingPower, out var bp) ? bp : null,
            Positions: positions,
            RetrievedAt: DateTimeOffset.UtcNow,
            Currency: "USD"
        );
    }

    /// <summary>
    /// Fetches portfolio data from Interactive Brokers using the Client Portal API.
    /// </summary>
    /// <remarks>
    /// Current implementation uses the Client Portal API which requires IB Gateway or TWS
    /// with Client Portal enabled. For fully automated integration, consider using IBAutomater
    /// or the official TWS API with the EnhancedIBConnectionManager.
    /// </remarks>
    private async Task<PortfolioSummary> FetchInteractiveBrokersPortfolioAsync(IBOptions ibConfig, CancellationToken ct)
    {
        // IB Client Portal API or TWS API integration
        // For now, return a stub that indicates IB requires TWS connection
        // In production, this would use the IBAutomater or Client Portal API

        // Check if Client Portal is running (typically on port 5000)
        var clientPortalUrl = $"https://localhost:{ibConfig.Port}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{clientPortalUrl}/v1/api/portfolio/accounts");

            // Use the named IB Client Portal client configured with SSL bypass for self-signed certs
            var ibClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.IBClientPortal);
            var response = await ibClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "IB Client Portal not available. Please ensure IB Gateway or TWS is running with Client Portal enabled.");
            }

            var accounts = await response.Content.ReadFromJsonAsync<IbAccount[]>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (accounts is null || accounts.Length == 0)
            {
                throw new InvalidOperationException("No IB accounts found.");
            }

            var accountId = accounts[0].AccountId ?? "unknown";

            // Get positions
            using var posRequest = new HttpRequestMessage(HttpMethod.Get,
                $"{clientPortalUrl}/v1/api/portfolio/{accountId}/positions/0");

            var posResponse = await ibClient.SendAsync(posRequest, ct);
            var ibPositions = await posResponse.Content.ReadFromJsonAsync<IbPosition[]>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            var positions = (ibPositions ?? Array.Empty<IbPosition>())
                .Where(p => !string.IsNullOrEmpty(p.Ticker))
                .Select(p => new PortfolioPosition(
                    Symbol: p.Ticker!,
                    Quantity: p.Position,
                    MarketValue: p.MktValue,
                    AverageCost: p.AvgCost,
                    UnrealizedPnL: p.UnrealizedPnl,
                    AssetClass: p.AssetClass,
                    Exchange: p.ListingExchange,
                    Currency: p.Currency ?? "USD",
                    Side: p.Position >= 0 ? "long" : "short"
                ))
                .ToArray();

            return new PortfolioSummary(
                Broker: "interactivebrokers",
                AccountId: accountId,
                TotalValue: positions.Sum(p => p.MarketValue ?? 0),
                CashBalance: null,
                BuyingPower: null,
                Positions: positions,
                RetrievedAt: DateTimeOffset.UtcNow,
                Currency: "USD"
            );
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                "Unable to connect to IB Client Portal. Please ensure IB Gateway or TWS is running with API enabled.");
        }
    }

    private async Task<PortfolioImportResult> ProcessPortfolioAsync(
        PortfolioSummary portfolio,
        PortfolioImportOptions options,
        CancellationToken ct)
    {
        var config = _configStore.Load();
        var existingSymbols = (config.Symbols ?? Array.Empty<SymbolConfig>())
            .Select(s => s.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var excludeSymbols = new HashSet<string>(
            options.ExcludeSymbols ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var errors = new List<string>();
        var imported = new List<string>();
        var skipped = 0;

        // Filter positions based on options
        var filteredPositions = portfolio.Positions
            .Where(p =>
            {
                if (excludeSymbols.Contains(p.Symbol))
                    return false;

                if (options.MinPositionValue.HasValue && p.MarketValue < options.MinPositionValue.Value)
                    return false;

                if (options.MinQuantity.HasValue && Math.Abs(p.Quantity) < options.MinQuantity.Value)
                    return false;

                if (options.LongOnly && p.Side.Equals("short", StringComparison.OrdinalIgnoreCase))
                    return false;

                if (options.AssetClasses is { Length: > 0 })
                {
                    var assetClass = p.AssetClass?.ToLowerInvariant() ?? "stock";
                    if (!options.AssetClasses.Any(ac => assetClass.Contains(ac.ToLowerInvariant())))
                        return false;
                }

                return true;
            })
            .ToList();

        var symbolsToImport = new List<SymbolConfig>();

        foreach (var position in filteredPositions)
        {
            if (options.SkipExisting && existingSymbols.Contains(position.Symbol))
            {
                skipped++;
                continue;
            }

            var symbolConfig = new SymbolConfig(
                Symbol: position.Symbol,
                SubscribeTrades: options.SubscribeTrades,
                SubscribeDepth: options.SubscribeDepth,
                DepthLevels: 10,
                SecurityType: MapAssetClass(position.AssetClass),
                Exchange: position.Exchange ?? "SMART",
                Currency: position.Currency
            );

            symbolsToImport.Add(symbolConfig);
            imported.Add(position.Symbol);
        }

        // Save imported symbols
        if (symbolsToImport.Count > 0)
        {
            var allSymbols = (config.Symbols ?? Array.Empty<SymbolConfig>())
                .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbolsToImport)
            {
                allSymbols[symbol.Symbol] = symbol;
            }

            var next = config with { Symbols = allSymbols.Values.ToArray() };
            await _configStore.SaveAsync(next);
        }

        // Create watchlist if requested
        string? watchlistId = null;
        if (options.CreateWatchlist && imported.Count > 0)
        {
            var watchlistName = options.WatchlistName ??
                $"{portfolio.Broker} Portfolio ({DateTimeOffset.UtcNow:yyyy-MM-dd})";

            var watchlist = await _watchlistService.CreateWatchlistAsync(new CreateWatchlistRequest(
                Name: watchlistName,
                Description: $"Imported from {portfolio.Broker} on {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}",
                Symbols: imported.ToArray(),
                Color: GetBrokerColor(portfolio.Broker),
                IsActive: true
            ), ct);

            watchlistId = watchlist.Id;
        }

        return new PortfolioImportResult(
            Success: true,
            Broker: portfolio.Broker,
            ImportedCount: imported.Count,
            SkippedCount: skipped,
            FailedCount: errors.Count,
            ImportedSymbols: imported.ToArray(),
            Errors: errors.ToArray(),
            WatchlistId: watchlistId,
            Portfolio: portfolio
        );
    }

    private static string MapAssetClass(string? assetClass)
    {
        if (string.IsNullOrEmpty(assetClass))
            return "STK";

        return assetClass.ToLowerInvariant() switch
        {
            "us_equity" or "stock" or "stocks" => "STK",
            "etf" or "etfs" => "ETF",
            "option" or "options" or "equity_option" or "equity_options" => "OPT",
            "index_option" or "index_options" => "IND_OPT",
            "future" or "futures" => "FUT",
            "future_option" or "future_options" or "futures_option" or "futures_options" => "FOP",
            "single_stock_future" or "single_stock_futures" => "SSF",
            "forex" or "fx" => "CASH",
            "crypto" or "cryptocurrency" => "CRYPTO",
            "commodity" or "commodities" => "CMDTY",
            "bond" or "bonds" or "fixed_income" => "BOND",
            "cfd" => "CFD",
            "fund" or "funds" or "mutual_fund" or "mutual_funds" => "FUND",
            "warrant" or "warrants" => "WAR",
            "spread" or "bag" => "BAG",
            "margin" => "MARGIN",
            "index" or "indices" => "IND",
            _ => "STK"
        };
    }

    private static string GetBrokerColor(string broker)
    {
        return broker.ToLowerInvariant() switch
        {
            "alpaca" => "#F7D616", // Alpaca yellow
            "interactivebrokers" or "ib" => "#D41F3C", // IB red
            _ => "#6B7280" // Gray
        };
    }

    // DTOs for Alpaca API responses
    private sealed record AlpacaPosition(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("qty")] string? Qty,
        [property: JsonPropertyName("market_value")] string? MarketValue,
        [property: JsonPropertyName("avg_entry_price")] string? AvgEntryPrice,
        [property: JsonPropertyName("unrealized_pl")] string? UnrealizedPl,
        [property: JsonPropertyName("asset_class")] string? AssetClass,
        [property: JsonPropertyName("exchange")] string? Exchange,
        [property: JsonPropertyName("side")] string? Side
    );

    private sealed record AlpacaAccount(
        [property: JsonPropertyName("account_number")] string? AccountNumber,
        [property: JsonPropertyName("portfolio_value")] string? PortfolioValue,
        [property: JsonPropertyName("cash")] string? Cash,
        [property: JsonPropertyName("buying_power")] string? BuyingPower
    );

    // DTOs for IB Client Portal API responses
    private sealed record IbAccount(
        [property: JsonPropertyName("accountId")] string? AccountId
    );

    private sealed record IbPosition(
        [property: JsonPropertyName("ticker")] string? Ticker,
        [property: JsonPropertyName("position")] decimal Position,
        [property: JsonPropertyName("mktValue")] decimal? MktValue,
        [property: JsonPropertyName("avgCost")] decimal? AvgCost,
        [property: JsonPropertyName("unrealizedPnl")] decimal? UnrealizedPnl,
        [property: JsonPropertyName("assetClass")] string? AssetClass,
        [property: JsonPropertyName("listingExchange")] string? ListingExchange,
        [property: JsonPropertyName("currency")] string? Currency
    );
}
