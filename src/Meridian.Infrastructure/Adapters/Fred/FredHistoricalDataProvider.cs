using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Core.Exceptions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Utilities;
using Meridian.Contracts.SecurityMaster;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Fred;

/// <summary>
/// Historical provider for FRED economic time series.
/// Legacy chart clients can request explicitly synthetic bars. Fixed-income consumers use
/// GetRateObservationsAsync, which preserves units and vintage evidence without OHLC conversion.
/// </summary>
[DataSource("fred", "FRED Economic Data", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 28, Description = "Federal Reserve Economic Data time series mapped to synthetic daily bars")]
[ImplementsAdr("ADR-001", "FRED historical provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
[RequiresCredential("FRED_API_KEY",
    EnvironmentVariables = new[] { "FRED_API_KEY", "FRED__APIKEY" },
    DisplayName = "API Key",
    Description = "FRED API key from https://fred.stlouisfed.org/docs/api/api_key.html")]
public sealed partial class FredHistoricalDataProvider : BaseHistoricalDataProvider, ISecurityRateObservationProvider
{
    private const string ApiBaseUrl = "https://api.stlouisfed.org/fred";
    private readonly string? _apiKey;

    public override string Name => "fred";
    public override string DisplayName => "FRED Economic Data";
    public override string Description => "Federal Reserve economic time series mapped to synthetic daily bars by series ID.";
    protected override string HttpClientName => HttpClientNames.FredHistorical;

    public override int Priority => 28;
    public override TimeSpan RateLimitDelay => TimeSpan.FromMilliseconds(500);
    public override int MaxRequestsPerWindow => 120;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    public override HistoricalDataCapabilities Capabilities { get; } =
        HistoricalDataCapabilities.BarsOnly.WithMarkets("US");

    public FredHistoricalDataProvider(string? apiKey = null, HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
        _apiKey = apiKey ?? Environment.GetEnvironmentVariable("FRED_API_KEY");
    }

    public override async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return false;

        try
        {
            using var response = await Http.GetAsync(
                $"{ApiBaseUrl}/series/observations?series_id=GDP&api_key={Uri.EscapeDataString(_apiKey)}&file_type=json&limit=1",
                ct).ConfigureAwait(false);

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("FRED API key is required. Set FRED_API_KEY environment variable.");

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSeriesId = NormalizeSymbol(symbol);
        var url = BuildRequestUrl(normalizedSeriesId, from, to);

        Log.Information("Requesting FRED history for {SeriesId}", normalizedSeriesId);

        try
        {
            using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
            var httpResult = await HandleHttpResponseAsync(response, normalizedSeriesId, "economic observations", ct).ConfigureAwait(false);

            if (httpResult.IsNotFound)
                return Array.Empty<HistoricalBar>();

            if (!httpResult.IsSuccess)
            {
                if (httpResult.IsRateLimited)
                {
                    throw new RateLimitException(
                        $"{Name} API rate limit exceeded for {normalizedSeriesId}",
                        provider: Name,
                        symbol: normalizedSeriesId,
                        retryAfter: httpResult.RetryAfter ?? TimeSpan.FromSeconds(60));
                }

                throw new InvalidOperationException($"Failed to fetch FRED data for {normalizedSeriesId}: {httpResult.ErrorMessage}");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var payload = DeserializeResponse<FredObservationsResponse>(json, normalizedSeriesId);

            if (payload?.Observations is null || payload.Observations.Count == 0)
                return Array.Empty<HistoricalBar>();

            return payload.Observations
                .Select(o => TryMapObservation(normalizedSeriesId, o, from, to))
                .Where(static b => b is not null)
                .Select(static b => b!)
                .OrderBy(b => b.SessionDate)
                .ToList();
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "Failed to parse FRED response for {SeriesId}", normalizedSeriesId);
            throw new InvalidOperationException($"Failed to parse FRED data for {normalizedSeriesId}", ex);
        }
    }

    protected override string NormalizeSymbol(string symbol)
        => symbol.Trim().ToUpperInvariant();

    /// <summary>Dedicated rate/fixing ingress. FRED's realtime dates are vintages, never fabricated publication timestamps.</summary>
    public async Task<IReadOnlyList<SecurityRateObservationDto>> GetRateObservationsAsync(
        string seriesId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(seriesId);
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("FRED API key is required. Configure the connection before loading rate observations.");
        var normalized = NormalizeSymbol(seriesId);
        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);
        using var metadataResponse = await Http.GetAsync(
            $"{ApiBaseUrl}/series?series_id={Uri.EscapeDataString(normalized)}&api_key={Uri.EscapeDataString(_apiKey)}&file_type=json", ct).ConfigureAwait(false);
        metadataResponse.EnsureSuccessStatusCode();
        var metadata = JsonSerializer.Deserialize<FredSeriesResponse>(await metadataResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var series = metadata?.Series?.SingleOrDefault() ?? throw new InvalidOperationException("FRED did not return a unique series with units.");
        var unit = series.Units?.Trim() switch
        {
            "Percent" => SecurityRateUnit.Percent,
            "Basis Points" => SecurityRateUnit.BasisPoints,
            { } units when units.StartsWith("Index", StringComparison.OrdinalIgnoreCase) => SecurityRateUnit.IndexLevel,
            _ => throw new InvalidOperationException($"FRED units '{series.Units}' are not a supported rate or index convention. Review the series before ingestion.")
        };
        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);
        // lin retains the original level; transformations are explicit evidence, never heuristics.
        using var response = await Http.GetAsync(BuildRequestUrl(normalized, from, to) + "&units=lin", ct).ConfigureAwait(false);
        var result = await HandleHttpResponseAsync(response, normalized, "typed rate observations", ct).ConfigureAwait(false);
        if (result.IsNotFound)
            return [];
        if (result.IsRateLimited)
            throw new RateLimitException($"FRED rate limit exceeded for {normalized}", provider: Name,
                symbol: normalized, retryAfter: result.RetryAfter ?? TimeSpan.FromSeconds(60));
        if (!result.IsSuccess)
            throw new InvalidOperationException($"Unable to load FRED rate observations: {result.ErrorMessage}");
        var payload = DeserializeResponse<FredObservationsResponse>(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), normalized);
        var retrievedAt = DateTimeOffset.UtcNow;
        var kind = normalized.StartsWith("DGS", StringComparison.Ordinal) || normalized.StartsWith("GS", StringComparison.Ordinal)
            ? SecurityRateObservationKind.TreasuryConstantMaturityYield
            : normalized is "SOFR" or "EFFR" or "DFF" or "OBFR" or "SOFR30DAYAVG" or "SOFR90DAYAVG" or "SOFR180DAYAVG"
                ? SecurityRateObservationKind.ObservedFixing
                : SecurityRateObservationKind.EconomicObservation;
        return (payload?.Observations ?? []).Where(observation =>
                DateOnly.TryParseExact(observation.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                (!from.HasValue || date >= from.Value) && (!to.HasValue || date <= to.Value) &&
                decimal.TryParse(observation.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            .Select(observation => new SecurityRateObservationDto(Name, normalized,
                decimal.Parse(observation.Value!, NumberStyles.Float, CultureInfo.InvariantCulture), observation.Value!, unit,
                series.Units!, DateOnly.ParseExact(observation.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                PublishedAt: null, ParseVintage(observation.RealtimeStart), ParseVintage(observation.RealtimeEnd),
                retrievedAt, "lin (untransformed provider level)", kind,
                Currency: kind is SecurityRateObservationKind.ObservedFixing or SecurityRateObservationKind.TreasuryConstantMaturityYield ? "USD" : null))
            .OrderBy(observation => observation.ObservationDate).ThenBy(observation => observation.VintageDate).ToArray();
    }

    private static DateOnly? ParseVintage(string? value)
        => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private sealed class FredSeriesResponse
    {
        [JsonPropertyName("seriess")]
        public List<FredSeries>? Series { get; set; }
    }

    private sealed class FredSeries
    {
        [JsonPropertyName("units")]
        public string? Units { get; set; }
    }

    private string BuildRequestUrl(string seriesId, DateOnly? from, DateOnly? to)
    {
        var query = new List<string>
        {
            $"series_id={Uri.EscapeDataString(seriesId)}",
            $"api_key={Uri.EscapeDataString(_apiKey!)}",
            "file_type=json",
            "sort_order=asc"
        };

        if (from.HasValue)
            query.Add($"observation_start={from.Value:yyyy-MM-dd}");
        if (to.HasValue)
            query.Add($"observation_end={to.Value:yyyy-MM-dd}");

        return $"{ApiBaseUrl}/series/observations?{string.Join("&", query)}";
    }

    private HistoricalBar? TryMapObservation(string seriesId, FredObservation observation, DateOnly? from, DateOnly? to)
    {
        if (!ProviderDateParsing.TryParseProviderDate(observation.Date, "yyyy-MM-dd", out var sessionDate))
            return null;

        if (from.HasValue && sessionDate < from.Value)
            return null;
        if (to.HasValue && sessionDate > to.Value)
            return null;

        if (string.IsNullOrWhiteSpace(observation.Value) || observation.Value == ".")
            return null;

        if (!decimal.TryParse(observation.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        return new HistoricalBar(
            Symbol: seriesId,
            SessionDate: sessionDate,
            Open: value,
            High: value,
            Low: value,
            Close: value,
            Volume: 0,
            Source: Name,
            SequenceNumber: sessionDate.DayNumber
        );
    }

    private sealed class FredObservationsResponse
    {
        [JsonPropertyName("observations")]
        public List<FredObservation>? Observations { get; set; }
    }

    private sealed class FredObservation
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }

        [JsonPropertyName("realtime_start")]
        public string? RealtimeStart { get; set; }

        [JsonPropertyName("realtime_end")]
        public string? RealtimeEnd { get; set; }
    }
}
