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

namespace Meridian.Infrastructure.Adapters.Polygon;

/// <summary>
/// Options chain provider using the Polygon.io REST API (v3).
/// Provides option chain snapshots, greeks, and contract discovery for US equity options.
/// Free tier supports delayed quotes; a paid subscription unlocks real-time data.
/// </summary>
/// <remarks>
/// API Documentation:
///   Chain snapshots:   GET /v3/snapshot/options/{underlyingAsset}
///   Single contract:   GET /v3/snapshot/options/{underlyingAsset}/{optionsTicker}
///   Expirations:       GET /v3/reference/options/contracts?underlying_ticker={symbol}
/// </remarks>
[DataSource("polygon-options", "Polygon.io Options", DataSourceType.Realtime, DataSourceCategory.Aggregator,
    Priority = 15, Description = "US equity option chains with greeks, IV, and open interest via Polygon.io REST API")]
[ImplementsAdr("ADR-001", "Options chain provider implementation following provider abstraction contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[ImplementsAdr("ADR-010", "HttpClient sourced via IHttpClientFactory; never instantiated directly")]
[RequiresCredential("POLYGON_API_KEY",
    EnvironmentVariables = new[] { "POLYGON_API_KEY", "POLYGON__APIKEY" },
    DisplayName = "API Key",
    Description = "Polygon.io API key from https://polygon.io/dashboard/api-keys")]
public sealed class PolygonOptionsChainProvider : IOptionsChainProvider
{
    private const string BaseUrl = "https://api.polygon.io";
    private const int MinApiKeyLength = 8;
    private const int DefaultPageLimit = 250;

    private readonly string? _apiKey;
    private readonly HttpClient _http;
    private readonly ILogger _log;

    // --------------------------------------------------------------------- //
    //  IProviderMetadata                                                      //
    // --------------------------------------------------------------------- //

    public string ProviderId => "polygon-options";
    public string ProviderDisplayName => "Polygon.io Options";
    public string ProviderDescription => "US equity option chains with greeks and IV via Polygon.io REST API.";
    public int ProviderPriority => 15;
    public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.OptionsChain();

    /// <inheritdoc/>
    public OptionsChainCapabilities Capabilities { get; } = new()
    {
        SupportsGreeks = true,
        SupportsOpenInterest = true,
        SupportsImpliedVolatility = true,
        SupportsIndexOptions = true,
        SupportsHistorical = false,
        SupportsStreaming = false,
        SupportedInstrumentTypes = new[]
        {
            InstrumentType.EquityOption,
            InstrumentType.IndexOption
        }
    };

    /// <summary>
    /// Creates a new Polygon options chain provider.
    /// </summary>
    /// <param name="apiKey">Polygon API key (falls back to POLYGON_API_KEY env var).</param>
    /// <param name="httpClientFactory">
    /// Optional <see cref="IHttpClientFactory"/> to create the underlying HTTP client (ADR-010).
    /// When provided, a named client "<c>polygon-options</c>" is created so shared resilience
    /// policies and handler lifetimes are centrally managed.
    /// </param>
    /// <param name="httpClient">Optional pre-built HTTP client (test-only override).</param>
    /// <param name="log">Optional logger.</param>
    public PolygonOptionsChainProvider(
        string? apiKey = null,
        IHttpClientFactory? httpClientFactory = null,
        HttpClient? httpClient = null,
        ILogger? log = null)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("POLYGON_API_KEY");
        _http = httpClient
            ?? httpClientFactory?.CreateClient(HttpClientNames.PolygonOptions)
            ?? new HttpClient();
        _log = log ?? Log.ForContext<PolygonOptionsChainProvider>();

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
            _log.Warning("Polygon API key not configured; cannot fetch expirations for {Symbol}", underlyingSymbol);
            return Array.Empty<DateOnly>();
        }

        // Use the reference contracts endpoint to discover available expirations
        // GET /v3/reference/options/contracts?underlying_ticker={symbol}&limit=1000&apiKey={key}
        var url = $"{BaseUrl}/v3/reference/options/contracts" +
                  $"?underlying_ticker={Uri.EscapeDataString(underlyingSymbol)}" +
                  $"&limit={DefaultPageLimit}" +
                  $"&apiKey={_apiKey}";

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Polygon expirations request failed: {StatusCode} for {Symbol}",
                    response.StatusCode, underlyingSymbol);
                return Array.Empty<DateOnly>();
            }

            var json = await response.Content.ReadFromJsonAsync(
                PolygonOptionsJsonContext.Default.PolygonReferenceContractsResponse,
                ct).ConfigureAwait(false);

            if (json?.Results is not { } results)
                return Array.Empty<DateOnly>();

            var expirations = results
                .Where(r => r.ExpirationDate is not null)
                .Select(r => DateOnly.Parse(r.ExpirationDate!))
                .Distinct()
                .OrderBy(d => d)
                .ToArray();

            _log.Debug("Polygon fetched {Count} expirations for {Symbol}", expirations.Length, underlyingSymbol);
            return expirations;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Polygon expirations for {Symbol}", underlyingSymbol);
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
            _log.Warning("Polygon API key not configured; cannot fetch strikes for {Symbol}", underlyingSymbol);
            return Array.Empty<decimal>();
        }

        var expirationStr = expiration.ToString("yyyy-MM-dd");

        // GET /v3/reference/options/contracts?underlying_ticker={symbol}&expiration_date={date}&limit=1000&apiKey={key}
        var url = $"{BaseUrl}/v3/reference/options/contracts" +
                  $"?underlying_ticker={Uri.EscapeDataString(underlyingSymbol)}" +
                  $"&expiration_date={expirationStr}" +
                  $"&limit={DefaultPageLimit}" +
                  $"&apiKey={_apiKey}";

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Polygon strikes request failed: {StatusCode} for {Symbol} {Expiration}",
                    response.StatusCode, underlyingSymbol, expirationStr);
                return Array.Empty<decimal>();
            }

            var json = await response.Content.ReadFromJsonAsync(
                PolygonOptionsJsonContext.Default.PolygonReferenceContractsResponse,
                ct).ConfigureAwait(false);

            if (json?.Results is not { } results)
                return Array.Empty<decimal>();

            var strikes = results
                .Where(r => r.StrikePrice is not null)
                .Select(r => r.StrikePrice!.Value)
                .Distinct()
                .OrderBy(s => s)
                .ToArray();

            return strikes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Polygon strikes for {Symbol} {Expiration}", underlyingSymbol, expirationStr);
            return Array.Empty<decimal>();
        }
    }

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
            _log.Warning("Polygon API key not configured; cannot fetch chain for {Symbol}", underlyingSymbol);
            return null;
        }

        var expirationStr = expiration.ToString("yyyy-MM-dd");

        // GET /v3/snapshot/options/{underlyingAsset}?expiration_date={date}&limit=250&apiKey={key}
        var url = $"{BaseUrl}/v3/snapshot/options/{Uri.EscapeDataString(underlyingSymbol)}" +
                  $"?expiration_date={expirationStr}" +
                  $"&limit={DefaultPageLimit}" +
                  $"&apiKey={_apiKey}";

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Warning("Polygon chain snapshot failed: {StatusCode} for {Symbol} {Expiration}",
                    response.StatusCode, underlyingSymbol, expirationStr);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync(
                PolygonOptionsJsonContext.Default.PolygonSnapshotResponse,
                ct).ConfigureAwait(false);

            if (json?.Results is not { } results || results.Count == 0)
            {
                _log.Information("Polygon returned empty chain for {Symbol} {Expiration}", underlyingSymbol, expirationStr);
                return null;
            }

            return MapToChainSnapshot(underlyingSymbol, expiration, results, strikeRange);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Polygon chain for {Symbol} {Expiration}", underlyingSymbol, expirationStr);
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
            _log.Warning("Polygon API key not configured; cannot fetch quote for {Symbol}", contract.UnderlyingSymbol);
            return null;
        }

        var occSymbol = contract.ToOccSymbol();

        // GET /v3/snapshot/options/{underlyingAsset}/{optionsTicker}?apiKey={key}
        var url = $"{BaseUrl}/v3/snapshot/options/{Uri.EscapeDataString(contract.UnderlyingSymbol)}" +
                  $"/{Uri.EscapeDataString(occSymbol)}" +
                  $"?apiKey={_apiKey}";

        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.Debug("Polygon quote not found: {StatusCode} for {OccSymbol}", response.StatusCode, occSymbol);
                return null;
            }

            var json = await response.Content.ReadFromJsonAsync(
                PolygonOptionsJsonContext.Default.PolygonSingleSnapshotResponse,
                ct).ConfigureAwait(false);

            if (json?.Results is null)
                return null;

            return MapToOptionQuote(contract, json.Results, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error(ex, "Error fetching Polygon quote for {OccSymbol}", occSymbol);
            return null;
        }
    }

    // --------------------------------------------------------------------- //
    //  Mapping helpers                                                        //
    // --------------------------------------------------------------------- //

    /// <summary>
    /// Returns <see langword="true"/> when Polygon API credentials are available and this provider can make live requests.
    /// Exposed internally so the DI registration can select the first configured provider.
    /// </summary>
    public bool IsCredentialsConfigured =>
        !string.IsNullOrWhiteSpace(_apiKey) && _apiKey.Length >= MinApiKeyLength;

    private bool IsConfigured => IsCredentialsConfigured;

    private static OptionChainSnapshot? MapToChainSnapshot(
        string underlyingSymbol,
        DateOnly expiration,
        IReadOnlyList<PolygonOptionSnapshotResult> results,
        int? strikeRange)
    {
        var now = DateTimeOffset.UtcNow;

        // Polygon returns the underlying price in the greeks/day sections
        decimal underlyingPrice = results
            .Select(r => r.UnderlyingAsset?.Price)
            .FirstOrDefault(p => p.HasValue) ?? 0m;

        var calls = new List<OptionQuote>();
        var puts = new List<OptionQuote>();
        var strikeSet = new SortedSet<decimal>();

        foreach (var result in results)
        {
            if (result.Details is null) continue;

            var right = result.Details.ContractType?.ToUpperInvariant() == "PUT"
                ? OptionRight.Put
                : OptionRight.Call;

            var contract = new OptionContractSpec(
                UnderlyingSymbol: underlyingSymbol,
                Strike: result.Details.StrikePrice ?? 0m,
                Expiration: expiration,
                Right: right,
                Style: result.Details.ExerciseStyle?.ToUpperInvariant() == "EUROPEAN"
                    ? OptionStyle.European
                    : OptionStyle.American,
                Multiplier: (ushort)(result.Details.SharesPerContract ?? 100),
                Exchange: "POLYGON",
                Currency: "USD",
                OccSymbol: result.Ticker,
                InstrumentType: InstrumentType.EquityOption);

            var quote = MapToOptionQuote(contract, result, now);
            if (quote is null) continue;

            strikeSet.Add(contract.Strike);

            if (right == OptionRight.Call)
                calls.Add(quote);
            else
                puts.Add(quote);
        }

        if (calls.Count == 0 && puts.Count == 0)
            return null;

        // Apply strike range filter if requested
        IReadOnlyList<decimal> strikes = strikeSet.ToArray();
        if (strikeRange.HasValue && underlyingPrice > 0m)
        {
            // Keep only strikes within strikeRange increments of ATM
            var sortedStrikes = strikes.OrderBy(s => Math.Abs(s - underlyingPrice)).Take(strikeRange.Value * 2 + 1).ToHashSet();
            calls = calls.Where(c => sortedStrikes.Contains(c.Contract.Strike)).ToList();
            puts = puts.Where(p => sortedStrikes.Contains(p.Contract.Strike)).ToList();
            strikes = sortedStrikes.OrderBy(s => s).ToArray();
        }

        return new OptionChainSnapshot(
            Timestamp: now,
            UnderlyingSymbol: underlyingSymbol,
            UnderlyingPrice: underlyingPrice > 0m ? underlyingPrice : 1m,
            Expiration: expiration,
            Strikes: strikes,
            Calls: calls,
            Puts: puts,
            InstrumentType: InstrumentType.EquityOption,
            SequenceNumber: 0,
            Source: "polygon");
    }

    private static OptionQuote? MapToOptionQuote(
        OptionContractSpec contract,
        PolygonOptionSnapshotResult result,
        DateTimeOffset now)
    {
        if (result.Day is null && result.LastQuote is null)
            return null;

        decimal bid = result.LastQuote?.Bid ?? 0m;
        decimal ask = result.LastQuote?.Ask ?? 0m;
        decimal last = result.Day?.Close ?? 0m;
        decimal underlyingPrice = result.UnderlyingAsset?.Price ?? 1m;
        decimal? iv = result.ImpliedVolatility.HasValue ? (decimal)result.ImpliedVolatility.Value : null;
        decimal? delta = result.Greeks?.Delta.HasValue == true ? (decimal)result.Greeks.Delta!.Value : null;
        decimal? gamma = result.Greeks?.Gamma.HasValue == true ? (decimal)result.Greeks.Gamma!.Value : null;
        decimal? theta = result.Greeks?.Theta.HasValue == true ? (decimal)result.Greeks.Theta!.Value : null;
        decimal? vega = result.Greeks?.Vega.HasValue == true ? (decimal)result.Greeks.Vega!.Value : null;
        long? openInterest = result.OpenInterest;
        long? volume = (long?)result.Day?.Volume;

        if (bid <= 0m && ask <= 0m && last <= 0m)
            return null;

        return new OptionQuote(
            Timestamp: now,
            Symbol: result.Ticker ?? contract.ToOccSymbol(),
            Contract: contract,
            BidPrice: bid,
            BidSize: result.LastQuote?.BidSize ?? 0,
            AskPrice: ask,
            AskSize: result.LastQuote?.AskSize ?? 0,
            UnderlyingPrice: underlyingPrice > 0m ? underlyingPrice : 1m,
            LastPrice: last > 0m ? last : null,
            ImpliedVolatility: iv,
            Delta: delta,
            Gamma: gamma,
            Theta: theta,
            Vega: vega,
            OpenInterest: openInterest,
            Volume: volume,
            SequenceNumber: 0,
            Source: "polygon");
    }
}

// --------------------------------------------------------------------- //
//  Polygon REST API response models (internal)                           //
// --------------------------------------------------------------------- //

internal sealed class PolygonReferenceContractsResponse
{
    [JsonPropertyName("results")]
    public List<PolygonContractReference>? Results { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; set; }
}

internal sealed class PolygonContractReference
{
    [JsonPropertyName("ticker")]
    public string? Ticker { get; set; }

    [JsonPropertyName("underlying_ticker")]
    public string? UnderlyingTicker { get; set; }

    [JsonPropertyName("expiration_date")]
    public string? ExpirationDate { get; set; }

    [JsonPropertyName("strike_price")]
    public decimal? StrikePrice { get; set; }

    [JsonPropertyName("contract_type")]
    public string? ContractType { get; set; }

    [JsonPropertyName("exercise_style")]
    public string? ExerciseStyle { get; set; }

    [JsonPropertyName("shares_per_contract")]
    public int? SharesPerContract { get; set; }
}

internal sealed class PolygonSnapshotResponse
{
    [JsonPropertyName("results")]
    public List<PolygonOptionSnapshotResult>? Results { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class PolygonSingleSnapshotResponse
{
    [JsonPropertyName("results")]
    public PolygonOptionSnapshotResult? Results { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class PolygonOptionSnapshotResult
{
    [JsonPropertyName("ticker")]
    public string? Ticker { get; set; }

    [JsonPropertyName("implied_volatility")]
    public double? ImpliedVolatility { get; set; }

    [JsonPropertyName("open_interest")]
    public long? OpenInterest { get; set; }

    [JsonPropertyName("greeks")]
    public PolygonGreeks? Greeks { get; set; }

    [JsonPropertyName("day")]
    public PolygonDayData? Day { get; set; }

    [JsonPropertyName("last_quote")]
    public PolygonLastQuote? LastQuote { get; set; }

    [JsonPropertyName("details")]
    public PolygonContractDetails? Details { get; set; }

    [JsonPropertyName("underlying_asset")]
    public PolygonUnderlyingAsset? UnderlyingAsset { get; set; }
}

internal sealed class PolygonGreeks
{
    [JsonPropertyName("delta")]
    public double? Delta { get; set; }

    [JsonPropertyName("gamma")]
    public double? Gamma { get; set; }

    [JsonPropertyName("theta")]
    public double? Theta { get; set; }

    [JsonPropertyName("vega")]
    public double? Vega { get; set; }
}

internal sealed class PolygonDayData
{
    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    [JsonPropertyName("volume")]
    public double Volume { get; set; }

    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    [JsonPropertyName("high")]
    public decimal High { get; set; }

    [JsonPropertyName("low")]
    public decimal Low { get; set; }
}

internal sealed class PolygonLastQuote
{
    [JsonPropertyName("bid")]
    public decimal Bid { get; set; }

    [JsonPropertyName("bid_size")]
    public long BidSize { get; set; }

    [JsonPropertyName("ask")]
    public decimal Ask { get; set; }

    [JsonPropertyName("ask_size")]
    public long AskSize { get; set; }
}

internal sealed class PolygonContractDetails
{
    [JsonPropertyName("contract_type")]
    public string? ContractType { get; set; }

    [JsonPropertyName("expiration_date")]
    public string? ExpirationDate { get; set; }

    [JsonPropertyName("exercise_style")]
    public string? ExerciseStyle { get; set; }

    [JsonPropertyName("strike_price")]
    public decimal? StrikePrice { get; set; }

    [JsonPropertyName("shares_per_contract")]
    public int? SharesPerContract { get; set; }
}

internal sealed class PolygonUnderlyingAsset
{
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("ticker")]
    public string? Ticker { get; set; }
}

/// <summary>
/// JSON serialization context for Polygon options API responses (ADR-014).
/// </summary>
[JsonSerializable(typeof(PolygonReferenceContractsResponse))]
[JsonSerializable(typeof(PolygonSnapshotResponse))]
[JsonSerializable(typeof(PolygonSingleSnapshotResponse))]
[JsonSerializable(typeof(PolygonOptionSnapshotResult))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
internal sealed partial class PolygonOptionsJsonContext : JsonSerializerContext
{
}
