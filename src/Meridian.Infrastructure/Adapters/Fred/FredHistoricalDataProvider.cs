using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Fred;

/// <summary>
/// Historical provider for FRED economic time series.
/// FRED observations are normalized into synthetic daily bars where OHLC are all equal to the observation value.
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
public sealed class FredHistoricalDataProvider : BaseHistoricalDataProvider
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
                throw new InvalidOperationException($"Failed to fetch FRED data for {normalizedSeriesId}: {httpResult.ErrorMessage}");

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
        if (!DateOnly.TryParseExact(observation.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var sessionDate))
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
    }
}
