using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Alpaca;

/// <summary>
/// Options chain provider for US equity options on Alpaca Markets. Contract discovery
/// (expirations and strikes) uses the Trading API; chain snapshots, quotes and greeks use the
/// Market Data API v1beta1.
/// </summary>
/// <remarks>
/// <para>
/// Endpoints:
///   Contract discovery: GET {trading}/v2/options/contracts?underlying_symbols={symbol}
///   Chain snapshot:     GET {data}/v1beta1/options/snapshots/{underlying_symbol}
/// </para>
/// <para>
/// Contract discovery follows the credentials' trading environment (paper or live host), reads
/// <c>strike_price</c> as the JSON string the API returns, states the expiration window
/// explicitly because the API otherwise stops at the upcoming weekend, and follows
/// <c>next_page_token</c>. A failed or unbounded page walk yields an empty result rather than
/// a truncated chain.
/// </para>
/// Reference: https://docs.alpaca.markets/reference/get-options-contracts
/// </remarks>
[DataSource("alpaca-options", "Alpaca Options", DataSourceType.Realtime, DataSourceCategory.Broker,
    Priority = 8, Description = "US equity option chains with greeks and IV via Alpaca Markets Data API")]
[ImplementsAdr("ADR-001", "Options chain provider implementation following provider abstraction contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "HttpClient sourced via IHttpClientFactory; never instantiated directly")]
[RequiresCredential("ALPACA_KEY_ID",
    EnvironmentVariables = new[] { "ALPACA_KEY_ID", "ALPACA__KEYID" },
    DisplayName = "API Key ID",
    Description = "Alpaca API key ID from https://app.alpaca.markets/brokerage/papers")]
[RequiresCredential("ALPACA_SECRET_KEY",
    EnvironmentVariables = new[] { "ALPACA_SECRET_KEY", "ALPACA__SECRETKEY" },
    DisplayName = "API Secret Key",
    Description = "Alpaca API secret key from https://app.alpaca.markets/brokerage/papers")]
public sealed class AlpacaOptionsChainProvider : IOptionsChainProvider
{
    private const string DataBaseUrl = "https://data.alpaca.markets";

    /// <summary>Largest page the contracts endpoint serves.</summary>
    private const int ContractPageSize = 10000;

    /// <summary>
    /// Upper bound on contract pages per request. Far above any listed chain (the largest
    /// index-ETF chains are a few pages); it only stops a token loop the API never ends.
    /// </summary>
    private const int MaxContractPages = 20;

    /// <summary>
    /// How far ahead expirations are requested. Listed equity LEAPS run under three years; the
    /// margin keeps the horizon from clipping the far end of a chain.
    /// </summary>
    private const int ExpirationHorizonYears = 5;

    private readonly string _tradingApiBaseUrl;
    private readonly string? _keyId;
    private readonly string? _secretKey;
    private readonly HttpClient _http;
    private readonly ILogger _log;

    // --------------------------------------------------------------------- //
    //  IProviderMetadata                                                      //
    // --------------------------------------------------------------------- //

    public string ProviderId => "alpaca";
    public string ProviderDisplayName => "Alpaca Options";
    public string ProviderDescription => "US equity option chains with greeks and IV via Alpaca Markets Data API.";
    public int ProviderPriority => 8;
    public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.OptionsChain();

    /// <inheritdoc/>
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

    /// <summary>
    /// Creates a new Alpaca options chain provider.
    /// </summary>
    /// <param name="httpClientFactory">
    /// Required <see cref="IHttpClientFactory"/> to create the underlying HTTP client (ADR-010).
    /// A named client "<c>alpaca-options</c>" is created so shared resilience
    /// policies and handler lifetimes are centrally managed.
    /// </param>
    /// <param name="keyId">Alpaca API key ID (falls back to ALPACA_KEY_ID env var).</param>
    /// <param name="secretKey">Alpaca API secret key (falls back to ALPACA_SECRET_KEY env var).</param>
    /// <param name="httpClient">Optional pre-built HTTP client (test-only override).</param>
    /// <param name="log">Optional logger.</param>
    /// <param name="useSandbox">
    /// <see langword="true"/> to discover contracts on the paper Trading API,
    /// <see langword="false"/> for live. When omitted, the environment selected by
    /// <c>ALPACA_TRADING_ENVIRONMENT</c> is used (paper unless it says live).
    /// </param>
    public AlpacaOptionsChainProvider(
        IHttpClientFactory httpClientFactory,
        string? keyId = null,
        string? secretKey = null,
        HttpClient? httpClient = null,
        ILogger? log = null,
        bool? useSandbox = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _tradingApiBaseUrl = AlpacaTradingApi.ResolveBaseUrl(useSandbox);
        _keyId = keyId ?? Environment.GetEnvironmentVariable("ALPACA_KEY_ID");
        _secretKey = secretKey ?? Environment.GetEnvironmentVariable("ALPACA_SECRET_KEY");
        _http = httpClient
            ?? httpClientFactory.CreateClient(HttpClientNames.AlpacaOptions);
        _log = log ?? Log.ForContext<AlpacaOptionsChainProvider>();

        if (IsConfigured)
        {
            _http.DefaultRequestHeaders.Add("APCA-API-KEY-ID", _keyId!);
            _http.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", _secretKey!);
        }

        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("User-Agent", "Meridian/1.0");
    }

    // --------------------------------------------------------------------- //
    //  IOptionsChainProvider                                                  //
    // --------------------------------------------------------------------- //

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(
        string underlyingSymbol,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        if (!IsConfigured)
        {
            _log.Warning("Alpaca credentials not configured; cannot fetch expirations for {Symbol}", underlyingSymbol);
            return Array.Empty<DateOnly>();
        }

        try
        {
            var contracts = await FetchContractsAsync(underlyingSymbol, expiration: null, ct).ConfigureAwait(false);
            if (contracts is null)
                return Array.Empty<DateOnly>();

            var expirations = contracts
                .Select(static c => TryParseContractDate(c.ExpirationDate, out var date) ? date : (DateOnly?)null)
                .Where(static date => date.HasValue)
                .Select(static date => date!.Value)
                .Distinct()
                .OrderBy(d => d)
                .ToArray();

            _log.Debug("Alpaca fetched {Count} expirations for {Symbol}", expirations.Length, underlyingSymbol);
            return expirations;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Alpaca expirations for {Symbol}", underlyingSymbol);
            return Array.Empty<DateOnly>();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<decimal>> GetStrikesAsync(
        string underlyingSymbol,
        DateOnly expiration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        if (!IsConfigured)
        {
            _log.Warning("Alpaca credentials not configured; cannot fetch strikes for {Symbol}", underlyingSymbol);
            return Array.Empty<decimal>();
        }

        var expirationStr = FormatContractDate(expiration);

        try
        {
            var contracts = await FetchContractsAsync(underlyingSymbol, expiration, ct).ConfigureAwait(false);
            if (contracts is null)
                return Array.Empty<decimal>();

            var strikes = contracts
                .Where(c => c.StrikePrice is not null)
                .Select(c => c.StrikePrice!.Value)
                .Distinct()
                .OrderBy(s => s)
                .ToArray();

            return strikes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Alpaca strikes for {Symbol} {Expiration}", underlyingSymbol, expirationStr);
            return Array.Empty<decimal>();
        }
    }

    /// <summary>
    /// Reads every active contract for one underlying from the Trading API, either for a single
    /// expiration or for the whole forward horizon. Returns <see langword="null"/> when any page
    /// fails or the page walk does not terminate, so a caller never presents part of a chain as
    /// the chain.
    /// </summary>
    private async Task<IReadOnlyList<AlpacaContractInfo>?> FetchContractsAsync(
        string underlyingSymbol,
        DateOnly? expiration,
        CancellationToken ct)
    {
        // Without an expiration bound the endpoint returns only contracts expiring by the
        // upcoming weekend, so the horizon is always stated.
        var window = expiration is { } single
            ? $"&expiration_date={FormatContractDate(single)}"
            : $"&expiration_date_lte={FormatContractDate(DateOnly.FromDateTime(DateTime.UtcNow).AddYears(ExpirationHorizonYears))}";
        var baseUrl = $"{_tradingApiBaseUrl}/v2/options/contracts" +
                      $"?underlying_symbols={Uri.EscapeDataString(underlyingSymbol)}" +
                      "&status=active" +
                      window +
                      $"&limit={ContractPageSize}";

        var contracts = new List<AlpacaContractInfo>();
        var seenPageTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;
        for (var page = 0; page < MaxContractPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var url = pageToken is null
                ? baseUrl
                : $"{baseUrl}&page_token={Uri.EscapeDataString(pageToken)}";
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Alpaca option contracts request failed: {StatusCode} for {Symbol}",
                    response.StatusCode, underlyingSymbol);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync(
                AlpacaOptionsJsonContext.Default.AlpacaContractsResponse,
                ct).ConfigureAwait(false);
            if (json?.OptionContracts is { } pageContracts)
                contracts.AddRange(pageContracts);

            pageToken = json?.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
                return contracts;

            if (!seenPageTokens.Add(pageToken))
            {
                _log.Warning("Alpaca option contracts paging repeated a page token for {Symbol}; discarding the partial chain",
                    underlyingSymbol);
                return null;
            }
        }

        _log.Warning("Alpaca option contracts paging exceeded {MaxPages} pages for {Symbol}; discarding the partial chain",
            MaxContractPages, underlyingSymbol);
        return null;
    }

    private static string FormatContractDate(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseContractDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    /// <inheritdoc/>
    public async Task<OptionChainSnapshot?> GetChainSnapshotAsync(
        string underlyingSymbol,
        DateOnly expiration,
        int? strikeRange = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        if (!IsConfigured)
        {
            _log.Warning("Alpaca credentials not configured; cannot fetch chain for {Symbol}", underlyingSymbol);
            return null;
        }

        var expirationStr = FormatContractDate(expiration);

        // GET /v1beta1/options/snapshots/{underlying_symbol}?expiration_date={date}&feed=indicative
        var url = $"{DataBaseUrl}/v1beta1/options/snapshots/{Uri.EscapeDataString(underlyingSymbol)}" +
                  $"?expiration_date={expirationStr}" +
                  "&feed=indicative" +
                  "&limit=1000";

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Alpaca chain snapshot failed: {StatusCode} for {Symbol} {Expiration}",
                    response.StatusCode, underlyingSymbol, expirationStr);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync(
                AlpacaOptionsJsonContext.Default.AlpacaSnapshotsResponse,
                ct).ConfigureAwait(false);

            if (json?.Snapshots is not { } snapshots || snapshots.Count == 0)
            {
                _log.Information("Alpaca returned empty chain for {Symbol} {Expiration}", underlyingSymbol, expirationStr);
                return null;
            }

            return MapToChainSnapshot(underlyingSymbol, expiration, snapshots, strikeRange);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Alpaca chain for {Symbol} {Expiration}", underlyingSymbol, expirationStr);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<OptionQuote?> GetOptionQuoteAsync(
        OptionContractSpec contract,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!IsConfigured)
        {
            _log.Warning("Alpaca credentials not configured; cannot fetch quote for {Symbol}", contract.UnderlyingSymbol);
            return null;
        }

        // Alpaca uses OCC format for option ticker symbols
        var occSymbol = contract.ToOccSymbol().Replace(" ", "");

        // GET /v1beta1/options/snapshots/{underlying_symbol}?symbols={occ_symbol}&feed=indicative
        var url = $"{DataBaseUrl}/v1beta1/options/snapshots/{Uri.EscapeDataString(contract.UnderlyingSymbol)}" +
                  $"?symbols={Uri.EscapeDataString(occSymbol)}" +
                  "&feed=indicative";

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Debug("Alpaca quote not found: {StatusCode} for {OccSymbol}", response.StatusCode, occSymbol);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync(
                AlpacaOptionsJsonContext.Default.AlpacaSnapshotsResponse,
                ct).ConfigureAwait(false);

            if (json?.Snapshots is not { } snapshots || !snapshots.TryGetValue(occSymbol, out var snap))
                return null;

            return MapToOptionQuote(contract, occSymbol, snap, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Alpaca quote for {OccSymbol}", occSymbol);
            return null;
        }
    }

    // --------------------------------------------------------------------- //
    //  Mapping helpers                                                        //
    // --------------------------------------------------------------------- //

    /// <summary>
    /// Returns <see langword="true"/> when Alpaca API credentials are available and this provider can make live requests.
    /// Exposed internally so the DI registration can select the first configured provider.
    /// </summary>
    public bool IsCredentialsConfigured =>
        !string.IsNullOrWhiteSpace(_keyId) && !string.IsNullOrWhiteSpace(_secretKey);

    private bool IsConfigured => IsCredentialsConfigured;

    private static OptionChainSnapshot? MapToChainSnapshot(
        string underlyingSymbol,
        DateOnly expiration,
        IReadOnlyDictionary<string, AlpacaOptionSnapshot> snapshots,
        int? strikeRange)
    {
        var now = DateTimeOffset.UtcNow;
        var calls = new List<OptionQuote>();
        var puts = new List<OptionQuote>();
        var strikeSet = new SortedSet<decimal>();

        // Alpaca's snapshot endpoint does not provide a separate underlying spot price field.
        // UnderlyingPrice is left as 0 to signal "not available" rather than using an option
        // contract quote (AskPrice of an option is not the underlying price).
        const decimal underlyingPrice = 0m;

        foreach (var (ticker, snap) in snapshots)
        {
            if (!TryParseOccRightAndStrike(ticker, out var right, out var strike))
                continue;

            var contract = new OptionContractSpec(
                UnderlyingSymbol: underlyingSymbol,
                Strike: strike,
                Expiration: expiration,
                Right: right,
                Style: OptionStyle.American,
                Multiplier: 100,
                Exchange: "ALPACA",
                Currency: "USD",
                OccSymbol: ticker,
                InstrumentType: InstrumentType.EquityOption);

            var quote = MapToOptionQuote(contract, ticker, snap, now);
            if (quote is null)
                continue;

            strikeSet.Add(strike);

            if (right == OptionRight.Call)
                calls.Add(quote);
            else
                puts.Add(quote);
        }

        if (calls.Count == 0 && puts.Count == 0)
            return null;

        IReadOnlyList<decimal> strikes = strikeSet.ToArray();

        return new OptionChainSnapshot(
            Timestamp: now,
            UnderlyingSymbol: underlyingSymbol,
            UnderlyingPrice: underlyingPrice,
            Expiration: expiration,
            Strikes: strikes,
            Calls: calls,
            Puts: puts,
            InstrumentType: InstrumentType.EquityOption,
            SequenceNumber: 0,
            Source: "alpaca");
    }

    /// <summary>
    /// Reads the right and strike from a compact OCC symbol (root, yyMMdd, C or P, then the
    /// strike in thousandths as eight digits). The right is the single character before the
    /// strike digits; searching the whole symbol misreads every put whose root contains a C.
    /// </summary>
    internal static bool TryParseOccRightAndStrike(string ticker, out OptionRight right, out decimal strike)
    {
        right = default;
        strike = 0m;
        if (string.IsNullOrWhiteSpace(ticker) || ticker.Length < 16)
            return false;

        switch (ticker[^9])
        {
            case 'C':
                right = OptionRight.Call;
                break;
            case 'P':
                right = OptionRight.Put;
                break;
            default:
                return false;
        }

        if (!long.TryParse(ticker.AsSpan(ticker.Length - 8), NumberStyles.None, CultureInfo.InvariantCulture, out var strikeMillis))
            return false;

        strike = strikeMillis / 1000m;
        return strike > 0m;
    }

    private static OptionQuote? MapToOptionQuote(
        OptionContractSpec contract,
        string ticker,
        AlpacaOptionSnapshot snap,
        DateTimeOffset now)
    {
        var bid = snap.LatestQuote?.BidPrice ?? 0m;
        var ask = snap.LatestQuote?.AskPrice ?? 0m;
        var last = snap.LatestTrade?.Price ?? 0m;

        if (bid <= 0m && ask <= 0m && last <= 0m)
            return null;

        // Alpaca's option snapshot endpoint does not expose the underlying spot price directly.
        // UnderlyingPrice is set to 0 to signal "not available" to callers rather than using
        // a meaningless placeholder that could mislead downstream moneyness checks.
        const decimal underlyingPrice = 0m;

        return new OptionQuote(
            Timestamp: now,
            Symbol: ticker,
            Contract: contract,
            BidPrice: bid,
            BidSize: snap.LatestQuote?.BidSize ?? 0,
            AskPrice: ask,
            AskSize: snap.LatestQuote?.AskSize ?? 0,
            UnderlyingPrice: underlyingPrice,
            LastPrice: last > 0m ? last : null,
            ImpliedVolatility: snap.ImpliedVolatility.HasValue ? (decimal)snap.ImpliedVolatility.Value : null,
            Delta: snap.Greeks?.Delta.HasValue == true ? (decimal)snap.Greeks.Delta!.Value : null,
            Gamma: snap.Greeks?.Gamma.HasValue == true ? (decimal)snap.Greeks.Gamma!.Value : null,
            Theta: snap.Greeks?.Theta.HasValue == true ? (decimal)snap.Greeks.Theta!.Value : null,
            Vega: snap.Greeks?.Vega.HasValue == true ? (decimal)snap.Greeks.Vega!.Value : null,
            OpenInterest: snap.OpenInterest,
            Volume: (long?)snap.LatestTrade?.Size,
            SequenceNumber: 0,
            Source: "alpaca");
    }
}

// --------------------------------------------------------------------- //
//  Alpaca REST API response models (internal)                            //
// --------------------------------------------------------------------- //

internal sealed class AlpacaContractsResponse
{
    [JsonPropertyName("option_contracts")]
    public List<AlpacaContractInfo>? OptionContracts { get; set; }

    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }
}

internal sealed class AlpacaContractInfo
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_symbol")]
    public string? UnderlyingSymbol { get; set; }

    [JsonPropertyName("expiration_date")]
    public string? ExpirationDate { get; set; }

    /// <summary>The Trading API returns the strike as a JSON string (for example "270").</summary>
    [JsonPropertyName("strike_price")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? StrikePrice { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("style")]
    public string? Style { get; set; }
}

internal sealed class AlpacaSnapshotsResponse
{
    [JsonPropertyName("snapshots")]
    public Dictionary<string, AlpacaOptionSnapshot>? Snapshots { get; set; }

    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }
}

internal sealed class AlpacaOptionSnapshot
{
    [JsonPropertyName("latestQuote")]
    public AlpacaOptionQuote? LatestQuote { get; set; }

    [JsonPropertyName("latestTrade")]
    public AlpacaOptionTrade? LatestTrade { get; set; }

    [JsonPropertyName("greeks")]
    public AlpacaGreeks? Greeks { get; set; }

    [JsonPropertyName("impliedVolatility")]
    public double? ImpliedVolatility { get; set; }

    [JsonPropertyName("openInterest")]
    public long? OpenInterest { get; set; }
}

internal sealed class AlpacaOptionQuote
{
    [JsonPropertyName("ap")]
    public decimal AskPrice { get; set; }

    [JsonPropertyName("as")]
    public long AskSize { get; set; }

    [JsonPropertyName("bp")]
    public decimal BidPrice { get; set; }

    [JsonPropertyName("bs")]
    public long BidSize { get; set; }

    [JsonPropertyName("t")]
    public DateTimeOffset Timestamp { get; set; }
}

internal sealed class AlpacaOptionTrade
{
    [JsonPropertyName("p")]
    public decimal Price { get; set; }

    [JsonPropertyName("s")]
    public long Size { get; set; }

    [JsonPropertyName("t")]
    public DateTimeOffset Timestamp { get; set; }
}

internal sealed class AlpacaGreeks
{
    [JsonPropertyName("delta")]
    public double? Delta { get; set; }

    [JsonPropertyName("gamma")]
    public double? Gamma { get; set; }

    [JsonPropertyName("theta")]
    public double? Theta { get; set; }

    [JsonPropertyName("vega")]
    public double? Vega { get; set; }

    [JsonPropertyName("rho")]
    public double? Rho { get; set; }
}

/// <summary>
/// JSON serialization context for Alpaca options API responses (ADR-014).
/// </summary>
[JsonSerializable(typeof(AlpacaContractsResponse))]
[JsonSerializable(typeof(AlpacaSnapshotsResponse))]
[JsonSerializable(typeof(AlpacaOptionSnapshot))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class AlpacaOptionsJsonContext : JsonSerializerContext
{
}
