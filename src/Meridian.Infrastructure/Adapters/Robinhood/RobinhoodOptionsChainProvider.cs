// ✅ ADR-001: IOptionsChainProvider contract
// ✅ ADR-004: CancellationToken on all async methods
// ✅ ADR-005: Attribute-based provider discovery via [DataSource]
// ✅ ADR-010: HTTP client via IHttpClientFactory
// ✅ ADR-014: JSON serialization via source generators
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace Meridian.Infrastructure.Adapters.Robinhood;

/// <summary>
/// Robinhood options chain provider that retrieves option chain data via the unofficial Robinhood API.
///
/// <para>
/// <b>Important:</b> Robinhood does not publish an official public API.
/// This provider targets the same endpoints used by the Robinhood mobile application.
/// Set <c>ROBINHOOD_ACCESS_TOKEN</c> to your personal access token.
/// </para>
///
/// <para>
/// API flow:
/// <list type="number">
///   <item>Resolve the equity instrument ID from <c>/instruments/?symbol=…</c></item>
///   <item>Fetch option chain metadata from <c>/options/chains/?equity_instrument_ids=…</c></item>
///   <item>Enumerate expiration dates and contracts from <c>/options/instruments/?chain_id=…&amp;expiration_date=…</c></item>
///   <item>Fetch market data from <c>/marketdata/options/?instruments=…</c></item>
/// </list>
/// </para>
/// </summary>
[DataSource("robinhood-options", "Robinhood Options Chain", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 35, Description = "Robinhood options chain data via unofficial API (requires personal access token)")]
[ImplementsAdr("ADR-001", "Robinhood options chain provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "Uses IHttpClientFactory for HTTP connections")]
[ImplementsAdr("ADR-014", "JSON serialization uses source generators")]
public sealed class RobinhoodOptionsChainProvider : IOptionsChainProvider
{
    private const string BaseUrl = "https://api.robinhood.com";
    private const string EnvAccessToken = "ROBINHOOD_ACCESS_TOKEN";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RobinhoodOptionsChainProvider> _logger;
    private readonly string? _accessToken;

    /// <summary>
    /// Initializes a new instance of <see cref="RobinhoodOptionsChainProvider"/>.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory (ADR-010).</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="accessToken">
    /// Optional access token. If null, reads from <c>ROBINHOOD_ACCESS_TOKEN</c> environment variable.
    /// </param>
    public RobinhoodOptionsChainProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<RobinhoodOptionsChainProvider> logger,
        string? accessToken = null)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _accessToken = accessToken ?? Environment.GetEnvironmentVariable(EnvAccessToken);

        if (string.IsNullOrWhiteSpace(_accessToken))
            _logger.LogWarning(
                "ROBINHOOD_ACCESS_TOKEN is not set; Robinhood options chain provider will be unavailable.");
    }

    // ── IOptionsChainProvider ──────────────────────────────────────────────

    /// <inheritdoc />
    public string ProviderId => "robinhood";

    /// <inheritdoc />
    public string ProviderDisplayName => "Robinhood Options (unofficial)";

    /// <inheritdoc />
    public string ProviderDescription =>
        "Options chain data via the unofficial Robinhood API (requires personal access token)";

    /// <inheritdoc />
    public int ProviderPriority => 35;

    /// <inheritdoc />
    public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.OptionsChain(
        greeks: true,
        openInterest: true,
        streaming: false);

    /// <inheritdoc />
    public OptionsChainCapabilities Capabilities { get; } = new()
    {
        SupportsGreeks = true,
        SupportsOpenInterest = true,
        SupportsImpliedVolatility = true,
        SupportsIndexOptions = false,
        SupportsHistorical = false,
        SupportsStreaming = false,
        SupportedInstrumentTypes = new[] { InstrumentType.EquityOption }
    };

    /// <inheritdoc />
    public ProviderCredentialField[] ProviderCredentialFields =>
    [
        new ProviderCredentialField(
            Name: "AccessToken",
            EnvironmentVariable: "ROBINHOOD_ACCESS_TOKEN",
            DisplayName: "Access Token",
            Required: true)
    ];

    /// <inheritdoc />
    public string[] ProviderWarnings => new[]
    {
        "Uses the unofficial Robinhood API — not endorsed by Robinhood Markets, Inc.",
        "Robinhood only supports American-style equity options. Index options are not available.",
        "Rate limiting may apply. Avoid high-frequency polling."
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(
        string underlyingSymbol,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);

        _logger.LogDebug("Robinhood: fetching expirations for {Symbol}", underlyingSymbol);

        var chainId = await GetChainIdAsync(underlyingSymbol, ct).ConfigureAwait(false);
        if (chainId is null)
        {
            _logger.LogWarning("Robinhood: no option chain found for {Symbol}", underlyingSymbol);
            return Array.Empty<DateOnly>();
        }

        using var client = CreateHttpClient();
        var response = await client.GetAsync(
            $"{BaseUrl}/options/chains/{chainId}/", ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Robinhood: /options/chains/{ChainId}/ returned {Status}",
                chainId, response.StatusCode);
            return Array.Empty<DateOnly>();
        }

        var chain = await response.Content.ReadFromJsonAsync(
            RobinhoodOptionsSerializerContext.Default.RobinhoodOptionChain, ct).ConfigureAwait(false);

        if (chain?.ExpirationDates is null or { Length: 0 })
            return Array.Empty<DateOnly>();

        var dates = new List<DateOnly>(chain.ExpirationDates.Length);
        foreach (var d in chain.ExpirationDates)
        {
            if (DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
                dates.Add(parsed);
        }

        dates.Sort();
        return dates.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<decimal>> GetStrikesAsync(
        string underlyingSymbol,
        DateOnly expiration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);

        _logger.LogDebug("Robinhood: fetching strikes for {Symbol} exp {Expiration}",
            underlyingSymbol, expiration);

        var instruments = await FetchContractsAsync(underlyingSymbol, expiration, ct).ConfigureAwait(false);
        if (instruments.Count == 0)
            return Array.Empty<decimal>();

        var strikes = instruments
            .Select(i => ParseDecimal(i.StrikePrice))
            .Where(s => s > 0)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        return strikes.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<OptionChainSnapshot?> GetChainSnapshotAsync(
        string underlyingSymbol,
        DateOnly expiration,
        int? strikeRange = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);

        _logger.LogDebug("Robinhood: fetching chain snapshot for {Symbol} exp {Expiration}",
            underlyingSymbol, expiration);

        var instruments = await FetchContractsAsync(underlyingSymbol, expiration, ct).ConfigureAwait(false);
        if (instruments.Count == 0)
            return null;

        // Fetch underlying price to allow ATM filtering.
        var underlyingPrice = await GetUnderlyingPriceAsync(underlyingSymbol, ct).ConfigureAwait(false);
        if (underlyingPrice <= 0)
        {
            _logger.LogWarning(
                "Robinhood: could not determine underlying price for {Symbol}; snapshot unavailable",
                underlyingSymbol);
            return null;
        }

        // Apply strike range filter if requested.
        IEnumerable<RobinhoodOptionInstrument> filtered = instruments;
        if (strikeRange.HasValue)
        {
            var atm = instruments
                .Select(i => ParseDecimal(i.StrikePrice))
                .Where(s => s > 0)
                .OrderBy(s => Math.Abs(s - underlyingPrice))
                .FirstOrDefault();

            if (atm > 0)
            {
                var allStrikes = instruments
                    .Select(i => ParseDecimal(i.StrikePrice))
                    .Where(s => s > 0)
                    .Distinct()
                    .OrderBy(s => s)
                    .ToList();

                var atmIndex = allStrikes.IndexOf(atm);
                var lo = Math.Max(0, atmIndex - strikeRange.Value);
                var hi = Math.Min(allStrikes.Count - 1, atmIndex + strikeRange.Value);
                var allowedStrikes = allStrikes.GetRange(lo, hi - lo + 1).ToHashSet();
                filtered = instruments.Where(i => allowedStrikes.Contains(ParseDecimal(i.StrikePrice)));
            }
        }

        var filteredList = filtered.ToList();
        if (filteredList.Count == 0)
            return null;

        // Batch-fetch market data for all contracts.
        var marketDataMap = await FetchMarketDataAsync(filteredList, ct).ConfigureAwait(false);

        var calls = new List<OptionQuote>();
        var puts = new List<OptionQuote>();
        var strikes = new SortedSet<decimal>();

        foreach (var instrument in filteredList)
        {
            var strike = ParseDecimal(instrument.StrikePrice);
            if (strike <= 0)
                continue;

            strikes.Add(strike);

            var isCall = string.Equals(instrument.Type, "call", StringComparison.OrdinalIgnoreCase);
            marketDataMap.TryGetValue(instrument.Url ?? string.Empty, out var md);

            var bid = ParseDecimal(md?.BidPrice);
            var ask = ParseDecimal(md?.AskPrice);

            // Skip if no market data (likely expired or halted contract).
            if (bid == 0 && ask == 0 && md is null)
                continue;

            var contract = BuildContractSpec(underlyingSymbol, instrument);
            if (contract is null)
                continue;

            var quote = new OptionQuote(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: instrument.Url ?? contract.ToOccSymbol(),
                Contract: contract,
                BidPrice: bid,
                BidSize: ParseLong(md?.BidSize),
                AskPrice: ask,
                AskSize: ParseLong(md?.AskSize),
                UnderlyingPrice: underlyingPrice,
                LastPrice: md?.LastTradePrice is null ? null : ParseDecimal(md.LastTradePrice),
                ImpliedVolatility: md?.ImpliedVolatility is null ? null : ParseDecimal(md.ImpliedVolatility),
                Delta: md?.Delta is null ? null : ParseDecimal(md.Delta),
                Gamma: md?.Gamma is null ? null : ParseDecimal(md.Gamma),
                Theta: md?.Theta is null ? null : ParseDecimal(md.Theta),
                Vega: md?.Vega is null ? null : ParseDecimal(md.Vega),
                OpenInterest: md?.OpenInterest is null ? null : (long?)ParseLong(md.OpenInterest),
                Volume: md?.Volume is null ? null : (long?)ParseLong(md.Volume),
                Source: "robinhood");

            if (isCall)
                calls.Add(quote);
            else
                puts.Add(quote);
        }

        if (calls.Count == 0 && puts.Count == 0)
            return null;

        return new OptionChainSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            UnderlyingSymbol: underlyingSymbol.ToUpperInvariant(),
            UnderlyingPrice: underlyingPrice,
            Expiration: expiration,
            Strikes: strikes.ToList().AsReadOnly(),
            Calls: calls.OrderBy(c => c.Contract.Strike).ToList().AsReadOnly(),
            Puts: puts.OrderBy(p => p.Contract.Strike).ToList().AsReadOnly(),
            Source: "robinhood");
    }

    /// <inheritdoc />
    public async Task<OptionQuote?> GetOptionQuoteAsync(
        OptionContractSpec contract,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contract);

        _logger.LogDebug("Robinhood: fetching option quote for {Contract}", contract);

        // Resolve instrument URL using chain lookup.
        var instruments = await FetchContractsAsync(
            contract.UnderlyingSymbol, contract.Expiration, ct).ConfigureAwait(false);

        var matched = instruments.FirstOrDefault(i =>
            ParseDecimal(i.StrikePrice) == contract.Strike &&
            string.Equals(i.Type, contract.Right == OptionRight.Call ? "call" : "put",
                StringComparison.OrdinalIgnoreCase));

        if (matched?.Url is null)
        {
            _logger.LogDebug(
                "Robinhood: no matching instrument found for {Contract}", contract);
            return null;
        }

        var underlyingPrice = await GetUnderlyingPriceAsync(
            contract.UnderlyingSymbol, ct).ConfigureAwait(false);
        if (underlyingPrice <= 0)
            return null;

        using var client = CreateHttpClient();
        var encodedUrl = Uri.EscapeDataString(matched.Url);
        var mdResponse = await client.GetAsync(
            $"{BaseUrl}/marketdata/options/?instruments={encodedUrl}", ct).ConfigureAwait(false);

        if (!mdResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning("Robinhood: /marketdata/options/ returned {Status}", mdResponse.StatusCode);
            return null;
        }

        var mdList = await mdResponse.Content.ReadFromJsonAsync(
            RobinhoodOptionsSerializerContext.Default.RobinhoodOptionMarketDataListResponse,
            ct).ConfigureAwait(false);

        var md = mdList?.Results?.FirstOrDefault();
        if (md is null)
            return null;

        var bid = ParseDecimal(md.BidPrice);
        var ask = ParseDecimal(md.AskPrice);

        return new OptionQuote(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: matched.Url ?? contract.ToOccSymbol(),
            Contract: contract,
            BidPrice: bid,
            BidSize: ParseLong(md.BidSize),
            AskPrice: ask,
            AskSize: ParseLong(md.AskSize),
            UnderlyingPrice: underlyingPrice,
            LastPrice: md.LastTradePrice is null ? null : ParseDecimal(md.LastTradePrice),
            ImpliedVolatility: md.ImpliedVolatility is null ? null : ParseDecimal(md.ImpliedVolatility),
            Delta: md.Delta is null ? null : ParseDecimal(md.Delta),
            Gamma: md.Gamma is null ? null : ParseDecimal(md.Gamma),
            Theta: md.Theta is null ? null : ParseDecimal(md.Theta),
            Vega: md.Vega is null ? null : ParseDecimal(md.Vega),
            OpenInterest: md.OpenInterest is null ? null : (long?)ParseLong(md.OpenInterest),
            Volume: md.Volume is null ? null : (long?)ParseLong(md.Volume),
            Source: "robinhood");
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private HttpClient CreateHttpClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientNames.RobinhoodBrokerage);
        if (!string.IsNullOrWhiteSpace(_accessToken))
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _accessToken);
        return client;
    }

    /// <summary>
    /// Resolves the option chain ID for the given underlying symbol.
    /// </summary>
    private async Task<string?> GetChainIdAsync(string underlyingSymbol, CancellationToken ct)
    {
        using var client = CreateHttpClient();

        // Step 1: resolve the instrument ID for the underlying.
        var instrResponse = await client.GetAsync(
            $"{BaseUrl}/instruments/?symbol={Uri.EscapeDataString(underlyingSymbol.ToUpperInvariant())}",
            ct).ConfigureAwait(false);

        if (!instrResponse.IsSuccessStatusCode)
            return null;

        var instrList = await instrResponse.Content.ReadFromJsonAsync(
            RobinhoodOptionsSerializerContext.Default.RobinhoodEquityInstrumentListResponse,
            ct).ConfigureAwait(false);

        var instrumentId = instrList?.Results?.FirstOrDefault()?.Id;
        if (string.IsNullOrWhiteSpace(instrumentId))
            return null;

        // Step 2: get the chain associated with this instrument.
        var chainResponse = await client.GetAsync(
            $"{BaseUrl}/options/chains/?equity_instrument_ids={Uri.EscapeDataString(instrumentId)}",
            ct).ConfigureAwait(false);

        if (!chainResponse.IsSuccessStatusCode)
            return null;

        var chainList = await chainResponse.Content.ReadFromJsonAsync(
            RobinhoodOptionsSerializerContext.Default.RobinhoodOptionChainListResponse,
            ct).ConfigureAwait(false);

        return chainList?.Results?.FirstOrDefault()?.Id;
    }

    /// <summary>
    /// Fetches all option contracts for a specific underlying and expiration.
    /// </summary>
    private async Task<IReadOnlyList<RobinhoodOptionInstrument>> FetchContractsAsync(
        string underlyingSymbol, DateOnly expiration, CancellationToken ct)
    {
        var chainId = await GetChainIdAsync(underlyingSymbol, ct).ConfigureAwait(false);
        if (chainId is null)
            return Array.Empty<RobinhoodOptionInstrument>();

        using var client = CreateHttpClient();
        var dateStr = expiration.ToString("yyyy-MM-dd");
        var results = new List<RobinhoodOptionInstrument>();
        string? nextUrl = $"{BaseUrl}/options/instruments/?chain_id={Uri.EscapeDataString(chainId)}&expiration_date={dateStr}&state=active&tradability=tradable";

        while (!string.IsNullOrEmpty(nextUrl))
        {
            ct.ThrowIfCancellationRequested();
            var response = await client.GetAsync(nextUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                break;

            var page = await response.Content.ReadFromJsonAsync(
                RobinhoodOptionsSerializerContext.Default.RobinhoodOptionInstrumentListResponse,
                ct).ConfigureAwait(false);

            if (page?.Results is { Length: > 0 })
                results.AddRange(page.Results);

            nextUrl = page?.Next;
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Batch-fetches market data for a list of option contracts.
    /// Returns a map from instrument URL to market data.
    /// </summary>
    private async Task<Dictionary<string, RobinhoodOptionMarketData>> FetchMarketDataAsync(
        IReadOnlyList<RobinhoodOptionInstrument> instruments, CancellationToken ct)
    {
        const int batchSize = 50; // Robinhood limits to ~50 instruments per request
        var result = new Dictionary<string, RobinhoodOptionMarketData>(StringComparer.OrdinalIgnoreCase);

        using var client = CreateHttpClient();

        for (var i = 0; i < instruments.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = instruments.Skip(i).Take(batchSize).ToList();
            var urls = string.Join(",", batch.Select(b => b.Url ?? string.Empty).Where(u => !string.IsNullOrEmpty(u)));
            if (string.IsNullOrEmpty(urls))
                continue;

            var encoded = Uri.EscapeDataString(urls);
            var response = await client.GetAsync(
                $"{BaseUrl}/marketdata/options/?instruments={encoded}", ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                continue;

            var mdList = await response.Content.ReadFromJsonAsync(
                RobinhoodOptionsSerializerContext.Default.RobinhoodOptionMarketDataListResponse,
                ct).ConfigureAwait(false);

            if (mdList?.Results is null)
                continue;

            foreach (var md in mdList.Results)
            {
                if (!string.IsNullOrEmpty(md.Instrument))
                    result[md.Instrument] = md;
            }
        }

        return result;
    }

    /// <summary>
    /// Fetches the current market price of the underlying equity.
    /// Uses <c>/quotes/{symbol}/</c>.
    /// </summary>
    private async Task<decimal> GetUnderlyingPriceAsync(string symbol, CancellationToken ct)
    {
        try
        {
            using var client = CreateHttpClient();
            var response = await client.GetAsync(
                $"{BaseUrl}/quotes/{Uri.EscapeDataString(symbol.ToUpperInvariant())}/",
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return 0m;

            var quote = await response.Content.ReadFromJsonAsync(
                RobinhoodOptionsSerializerContext.Default.RobinhoodEquityQuoteResponse,
                ct).ConfigureAwait(false);

            return ParseDecimal(quote?.LastTradePrice ?? quote?.LastExtendedHoursTradePrice);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Robinhood: failed to fetch underlying price for {Symbol}", symbol);
            return 0m;
        }
    }

    private static OptionContractSpec? BuildContractSpec(
        string underlyingSymbol, RobinhoodOptionInstrument instrument)
    {
        var strike = ParseDecimal(instrument.StrikePrice);
        if (strike <= 0)
            return null;

        if (!DateOnly.TryParseExact(instrument.ExpirationDate, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiration))
            return null;

        var right = string.Equals(instrument.Type, "call", StringComparison.OrdinalIgnoreCase)
            ? OptionRight.Call
            : OptionRight.Put;

        return new OptionContractSpec(
            UnderlyingSymbol: underlyingSymbol.ToUpperInvariant(),
            Strike: strike,
            Expiration: expiration,
            Right: right,
            Style: OptionStyle.American,
            Multiplier: 100,
            Exchange: "robinhood",
            Currency: "USD");
    }

    private static decimal ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : 0L;

    // ── JSON DTOs (ADR-014: source generators) ─────────────────────────────

    // Equity instrument (underlying lookup)
    internal sealed class RobinhoodEquityInstrument
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("symbol")] public string? Symbol { get; init; }
    }

    internal sealed class RobinhoodEquityInstrumentListResponse
    {
        [JsonPropertyName("results")] public RobinhoodEquityInstrument[]? Results { get; init; }
    }

    // Option chain
    internal sealed class RobinhoodOptionChain
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("symbol")] public string? Symbol { get; init; }
        [JsonPropertyName("expiration_dates")] public string[]? ExpirationDates { get; init; }
        [JsonPropertyName("trade_value_multiplier")] public string? TradeValueMultiplier { get; init; }
        [JsonPropertyName("can_open_position")] public bool? CanOpenPosition { get; init; }
        [JsonPropertyName("cash_component")] public string? CashComponent { get; init; }
        [JsonPropertyName("min_ticks")] public RobinhoodOptionMinTicks? MinTicks { get; init; }
    }

    internal sealed class RobinhoodOptionMinTicks
    {
        [JsonPropertyName("above_tick")] public string? AboveTick { get; init; }
        [JsonPropertyName("below_tick")] public string? BelowTick { get; init; }
        [JsonPropertyName("cutoff_price")] public string? CutoffPrice { get; init; }
    }

    internal sealed class RobinhoodOptionChainListResponse
    {
        [JsonPropertyName("results")] public RobinhoodOptionChain[]? Results { get; init; }
    }

    // Option instruments (contract specs)
    internal sealed class RobinhoodOptionInstrument
    {
        [JsonPropertyName("url")] public string? Url { get; init; }
        [JsonPropertyName("chain_id")] public string? ChainId { get; init; }
        [JsonPropertyName("chain_symbol")] public string? ChainSymbol { get; init; }
        [JsonPropertyName("expiration_date")] public string? ExpirationDate { get; init; }
        [JsonPropertyName("strike_price")] public string? StrikePrice { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }  // "call" or "put"
        [JsonPropertyName("state")] public string? State { get; init; }
        [JsonPropertyName("tradability")] public string? Tradability { get; init; }
    }

    internal sealed class RobinhoodOptionInstrumentListResponse
    {
        [JsonPropertyName("results")] public RobinhoodOptionInstrument[]? Results { get; init; }
        [JsonPropertyName("next")] public string? Next { get; init; }
    }

    // Option market data
    internal sealed class RobinhoodOptionMarketData
    {
        [JsonPropertyName("instrument")] public string? Instrument { get; init; }
        [JsonPropertyName("ask_price")] public string? AskPrice { get; init; }
        [JsonPropertyName("ask_size")] public string? AskSize { get; init; }
        [JsonPropertyName("bid_price")] public string? BidPrice { get; init; }
        [JsonPropertyName("bid_size")] public string? BidSize { get; init; }
        [JsonPropertyName("last_trade_price")] public string? LastTradePrice { get; init; }
        [JsonPropertyName("last_trade_size")] public string? LastTradeSize { get; init; }
        [JsonPropertyName("implied_volatility")] public string? ImpliedVolatility { get; init; }
        [JsonPropertyName("delta")] public string? Delta { get; init; }
        [JsonPropertyName("gamma")] public string? Gamma { get; init; }
        [JsonPropertyName("theta")] public string? Theta { get; init; }
        [JsonPropertyName("vega")] public string? Vega { get; init; }
        [JsonPropertyName("rho")] public string? Rho { get; init; }
        [JsonPropertyName("open_interest")] public string? OpenInterest { get; init; }
        [JsonPropertyName("volume")] public string? Volume { get; init; }
        [JsonPropertyName("adjusted_mark_price")] public string? AdjustedMarkPrice { get; init; }
    }

    internal sealed class RobinhoodOptionMarketDataListResponse
    {
        [JsonPropertyName("results")] public RobinhoodOptionMarketData[]? Results { get; init; }
    }

    // Equity quote (for underlying price)
    internal sealed class RobinhoodEquityQuoteResponse
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; init; }
        [JsonPropertyName("last_trade_price")] public string? LastTradePrice { get; init; }
        [JsonPropertyName("last_extended_hours_trade_price")] public string? LastExtendedHoursTradePrice { get; init; }
        [JsonPropertyName("bid_price")] public string? BidPrice { get; init; }
        [JsonPropertyName("ask_price")] public string? AskPrice { get; init; }
    }
}

/// <summary>
/// ADR-014 source-generated JSON serializer context for Robinhood options chain DTOs.
/// </summary>
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodEquityInstrument))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodEquityInstrumentListResponse))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodOptionChain))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodOptionChainListResponse))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodOptionInstrument))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodOptionInstrumentListResponse))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodOptionMarketData))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodOptionMarketDataListResponse))]
[JsonSerializable(typeof(RobinhoodOptionsChainProvider.RobinhoodEquityQuoteResponse))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class RobinhoodOptionsSerializerContext : JsonSerializerContext;
