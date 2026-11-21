using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Config;
using Meridian.Core.Subscriptions.Models;
using Meridian.Application.UI;
using Meridian.Infrastructure.Http;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Service for importing symbols from broker portfolios.
/// Supports Alpaca, Interactive Brokers, and manual entry.
/// </summary>
public sealed partial class PortfolioImportService
{
    private const NumberStyles ProviderDecimalStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
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
        ct.ThrowIfCancellationRequested();
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
        ct.ThrowIfCancellationRequested();
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
            RequireSelectedAccount(portfolio, options.BrokerAccountId);
            return await ProcessPortfolioAsync(portfolio, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
        ct.ThrowIfCancellationRequested();
        var config = _configStore.Load();
        var ibConfig = config.IB;
        var clientPortalConfig = config.IBClientPortal;

        if (ibConfig is null)
        {
            return new PortfolioImportResult(false, "ib", 0, 0, 0, Array.Empty<string>(),
                new[] { "Interactive Brokers configuration not found. Please configure IB gateway connection." });
        }

        if (clientPortalConfig is null || !clientPortalConfig.Enabled)
        {
            return new PortfolioImportResult(false, "ib", 0, 0, 0, Array.Empty<string>(),
                new[] { "Interactive Brokers Client Portal is not enabled. Configure IBClientPortal for portfolio import." });
        }

        try
        {
            var portfolio = await FetchInteractiveBrokersPortfolioAsync(clientPortalConfig, ct, options.BrokerAccountId);
            return await ProcessPortfolioAsync(portfolio, options, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
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
    public Task<PortfolioSummary?> GetPortfolioSummaryAsync(string broker, CancellationToken ct = default)
        => GetPortfolioSummaryAsync(broker, null, ct);

    public async Task<PortfolioSummary?> GetPortfolioSummaryAsync(string broker, string? accountId, CancellationToken ct = default)
    {
        var config = _configStore.Load();

        var portfolio = broker.ToLowerInvariant() switch
        {
            "alpaca" when config.Alpaca is not null => await FetchAlpacaPortfolioAsync(config.Alpaca, ct),
            "ib" or "interactivebrokers" when config.IBClientPortal is { Enabled: true } => await FetchInteractiveBrokersPortfolioAsync(config.IBClientPortal, ct, accountId),
            _ => null
        };
        if (portfolio is not null)
            RequireSelectedAccount(portfolio, accountId);
        return portfolio;
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

        if (config.IB is not null && config.IBClientPortal is { Enabled: true })
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

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var positionsJson = await response.Content.ReadFromJsonAsync(BrokerImportJsonContext.Default.AlpacaPositionArray, ct)
            ?? throw new InvalidDataException("Alpaca returned no position population.");

        // Also get account info
        using var accountRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/v2/account");
        accountRequest.Headers.Add("APCA-API-KEY-ID", alpacaConfig.KeyId);
        accountRequest.Headers.Add("APCA-API-SECRET-KEY", alpacaConfig.SecretKey);

        using var accountResponse = await _httpClient.SendAsync(accountRequest, ct);
        accountResponse.EnsureSuccessStatusCode();

        var account = await accountResponse.Content.ReadFromJsonAsync(BrokerImportJsonContext.Default.AlpacaAccount, ct)
            ?? throw new InvalidDataException("Alpaca returned no account evidence.");
        if (string.IsNullOrWhiteSpace(account.AccountNumber))
            throw new InvalidDataException("Alpaca account identity is missing.");

        var positions = (positionsJson ?? Array.Empty<AlpacaPosition>())
            .Select(p => new PortfolioPosition(
                Symbol: RequireSymbol(p.Symbol),
                Quantity: ParseRequiredProviderDecimal(p.Qty, $"quantity for {p.Symbol}"),
                MarketValue: ParseProviderDecimal(p.MarketValue),
                AverageCost: ParseProviderDecimal(p.AvgEntryPrice),
                UnrealizedPnL: ParseProviderDecimal(p.UnrealizedPl),
                AssetClass: p.AssetClass ?? "us_equity",
                Exchange: p.Exchange,
                Currency: "USD",
                Side: p.Side ?? "long"
            ))
            .ToArray();

        return new PortfolioSummary(
            Broker: "alpaca",
            AccountId: account?.AccountNumber ?? "unknown",
            TotalValue: ParseProviderDecimal(account?.PortfolioValue),
            CashBalance: ParseProviderDecimal(account?.Cash),
            BuyingPower: ParseProviderDecimal(account?.BuyingPower),
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
    private async Task<PortfolioSummary> FetchInteractiveBrokersPortfolioAsync(IBClientPortalOptions clientPortalConfig, CancellationToken ct, string? selectedAccount = null)
    {
        var clientPortalUrl = clientPortalConfig.BaseUrl.TrimEnd('/');

        if (!Uri.TryCreate(clientPortalUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("IB Client Portal BaseUrl is invalid. Use an absolute HTTP or HTTPS URL.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "/v1/api/portfolio/accounts"));

            var ibClient = clientPortalConfig.AllowSelfSignedCertificates
                ? HttpClientFactoryProvider.CreateClient(HttpClientNames.IBClientPortal)
                : _httpClient;
            using var ownedClient = clientPortalConfig.AllowSelfSignedCertificates ? ibClient : null;
            using var response = await ibClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"IB Client Portal not available at {clientPortalUrl}. Please ensure IB Gateway or TWS is running with Client Portal enabled.");
            }

            var accounts = await response.Content.ReadFromJsonAsync(BrokerImportJsonContext.Default.IbAccountArray, ct);

            if (accounts is null || accounts.Length == 0)
            {
                throw new InvalidOperationException("No IB accounts found.");
            }

            var accountId = string.IsNullOrWhiteSpace(selectedAccount)
                ? accounts.Length == 1 ? accounts[0].AccountId : throw new InvalidDataException("Select an IB account explicitly when multiple accounts are available.")
                : accounts.SingleOrDefault(a => a.AccountId == selectedAccount)?.AccountId;
            if (string.IsNullOrWhiteSpace(accountId))
                throw new InvalidDataException("The selected IB account is unavailable or has no identity.");
            var escapedAccount = Uri.EscapeDataString(accountId);

            // Get positions
            var ibPositions = new List<IbPosition>();
            var pageHashes = new HashSet<string>(StringComparer.Ordinal);
            for (var page = 0; ; page++)
            {
                if (page >= 10_000)
                    throw new InvalidDataException("IB position pagination did not terminate.");
                using var posRequest = new HttpRequestMessage(HttpMethod.Get,
                    new Uri(baseUri, $"/v1/api/portfolio/{escapedAccount}/positions/{page}"));
                using var posResponse = await ibClient.SendAsync(posRequest, ct);
                posResponse.EnsureSuccessStatusCode();
                var batch = await posResponse.Content.ReadFromJsonAsync(BrokerImportJsonContext.Default.IbPositionArray, ct)
                    ?? throw new InvalidDataException("IB returned no position population.");
                if (batch.Length == 0)
                    break;
                var pageHash = Meridian.Contracts.Integrity.Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(batch, BrokerImportJsonContext.Default.IbPositionArray));
                if (!pageHashes.Add(pageHash))
                    throw new InvalidDataException("IB repeated a position page; account coverage is incomplete.");
                ibPositions.AddRange(batch);
            }
            using var summaryRequest = new HttpRequestMessage(HttpMethod.Get,
                new Uri(baseUri, $"/v1/api/portfolio/{escapedAccount}/summary"));
            using var summaryResponse = await ibClient.SendAsync(summaryRequest, ct);
            summaryResponse.EnsureSuccessStatusCode();
            using var summary = await JsonDocument.ParseAsync(await summaryResponse.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var cash = ReadIbSummaryAmount(summary.RootElement, "cashbalance")
                ?? ReadIbSummaryAmount(summary.RootElement, "totalcashvalue");
            var equity = ReadIbSummaryAmount(summary.RootElement, "netliquidation");

            var positions = ibPositions
                .Select(p => new PortfolioPosition(
                    Symbol: RequireSymbol(p.Ticker),
                    Quantity: p.Position ?? throw new InvalidDataException($"IB quantity for {p.Ticker} is missing."),
                    MarketValue: p.MktValue,
                    AverageCost: p.AvgCost,
                    UnrealizedPnL: p.UnrealizedPnl,
                    AssetClass: p.AssetClass,
                    Exchange: p.ListingExchange,
                    Currency: string.IsNullOrWhiteSpace(p.Currency) ? throw new InvalidDataException($"IB currency for {p.Ticker} is missing.") : p.Currency,
                    Side: p.Position >= 0 ? "long" : "short"
                ))
                .ToArray();

            return new PortfolioSummary(
                Broker: "interactivebrokers",
                AccountId: accountId,
                TotalValue: equity?.Amount,
                CashBalance: cash?.Amount,
                BuyingPower: null,
                Positions: positions,
                RetrievedAt: DateTimeOffset.UtcNow,
                Currency: cash?.Currency ?? equity?.Currency ?? throw new InvalidDataException("IB account currency is missing.")
            );
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException(
                $"Unable to connect to IB Client Portal at {clientPortalUrl}. Please ensure IB Gateway or TWS is running with Client Portal enabled.");
        }
    }

    public static IReadOnlyList<PortfolioPosition> SelectPositions(PortfolioSummary portfolio, PortfolioImportOptions options)
    {
        var excludeSymbols = new HashSet<string>(
            options.ExcludeSymbols ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        // Filter positions based on options
        return portfolio.Positions
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

    }

    public async Task<PortfolioImportResult> ProcessPortfolioAsync(
        PortfolioSummary portfolio,
        PortfolioImportOptions options,
        CancellationToken ct)
    {
        var filteredPositions = SelectPositions(portfolio, options);
        var errors = new List<string>();

        var (imported, skipped) = await _configStore.UpdateAsync(config =>
        {
            var existingSymbols = (config.Symbols ?? Array.Empty<SymbolConfig>())
                .Select(s => s.Symbol)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var imported = new List<string>();
            var skipped = 0;
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
            if (symbolsToImport.Count == 0)
                return (config, (imported, skipped));

            var allSymbols = (config.Symbols ?? Array.Empty<SymbolConfig>())
                .ToDictionary(s => s.Symbol, s => s, StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbolsToImport)
            {
                allSymbols[symbol.Symbol] = symbol;
            }

            return (config with { Symbols = allSymbols.Values.ToArray() }, (imported, skipped));
        }, ct);

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

    private static decimal? ParseProviderDecimal(string? value)
        => decimal.TryParse(value, ProviderDecimalStyles, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static decimal ParseRequiredProviderDecimal(string? value, string field)
        => decimal.TryParse(value, ProviderDecimalStyles, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidDataException($"Provider {field} is missing or malformed.");

    private static string RequireSymbol(string? symbol)
        => string.IsNullOrWhiteSpace(symbol) ? throw new InvalidDataException("Provider position symbol is missing.") : symbol.Trim().ToUpperInvariant();

    private static void RequireSelectedAccount(PortfolioSummary portfolio, string? accountId)
    {
        if (accountId is not null && !string.Equals(accountId, portfolio.AccountId, StringComparison.Ordinal))
            throw new InvalidDataException("The provider response does not belong to the selected account.");
    }

    private static (decimal Amount, string Currency)? ReadIbSummaryAmount(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var row))
            return null;
        if (!row.TryGetProperty("currency", out var currency) || string.IsNullOrWhiteSpace(currency.GetString()) ||
            !row.TryGetProperty("amount", out var amount))
            throw new InvalidDataException($"IB {name} lacks dated currency/amount evidence.");
        return (amount.ValueKind == JsonValueKind.Number ? amount.GetDecimal() : ParseRequiredProviderDecimal(amount.GetString(), name), currency.GetString()!);
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
        [property: JsonPropertyName("position")] decimal? Position,
        [property: JsonPropertyName("mktValue")] decimal? MktValue,
        [property: JsonPropertyName("avgCost")] decimal? AvgCost,
        [property: JsonPropertyName("unrealizedPnl")] decimal? UnrealizedPnl,
        [property: JsonPropertyName("assetClass")] string? AssetClass,
        [property: JsonPropertyName("listingExchange")] string? ListingExchange,
        [property: JsonPropertyName("currency")] string? Currency
    );

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(AlpacaPosition[]))]
    [JsonSerializable(typeof(AlpacaAccount))]
    [JsonSerializable(typeof(IbAccount[]))]
    [JsonSerializable(typeof(IbPosition[]))]
    private partial class BrokerImportJsonContext : JsonSerializerContext;
}
