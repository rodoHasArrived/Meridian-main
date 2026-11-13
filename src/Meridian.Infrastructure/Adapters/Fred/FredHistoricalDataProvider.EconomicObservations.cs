using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Resilience;

namespace Meridian.Infrastructure.Adapters.Fred;

/// <summary>A reviewed mapping from a FRED series to an exact contractual index and explicit source units.</summary>
public sealed record FredEconomicSeriesDefinition(string SeriesId, string IndexName,
    BondMarketObservationKind Kind, BondMarketValueUnit Unit, string SourceName, string? Currency = null);

/// <summary>Unmodified response evidence is available to the host's evidence retainer; missing observations stay explicit.</summary>
public sealed record FredEconomicObservationsResult(
    IReadOnlyList<BondMarketObservationInput> Observations,
    IReadOnlyList<DateOnly> MissingDates,
    string RawResponseJson,
    string ResponseSha256);

public sealed partial class FredHistoricalDataProvider
{
    /// <summary>
    /// Fetches typed, vintage-bound index observations independently of synthetic OHLC bars.
    /// Series mapping is explicit: benchmark yields are never turned into discount factors.
    /// FRED's vintage date is retained as a date, not invented as a publication-time instant.
    /// </summary>
    public async Task<FredEconomicObservationsResult> GetEconomicObservationsAsync(
        FredEconomicSeriesDefinition definition, DateOnly from, DateOnly to, DateOnly vintageDate, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(definition);
        ct.ThrowIfCancellationRequested();
        if (from == default || to < from || vintageDate == default || to > vintageDate
            || string.IsNullOrWhiteSpace(definition.SeriesId) || string.IsNullOrWhiteSpace(definition.IndexName)
            || string.IsNullOrWhiteSpace(definition.SourceName)
            || definition.Kind is not (BondMarketObservationKind.Fixing or BondMarketObservationKind.InflationIndex)
            || definition.Kind == BondMarketObservationKind.Fixing && definition.Unit is not (BondMarketValueUnit.Percent or BondMarketValueUnit.AnnualFraction)
            || definition.Kind == BondMarketObservationKind.InflationIndex && definition.Unit != BondMarketValueUnit.IndexLevel)
            throw new ArgumentException("A typed economic request needs a declared series, index, source, date window, vintage and rate/index units.");
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("FRED API key is required. Set FRED_API_KEY environment variable.");
        var series = NormalizeSymbol(definition.SeriesId);
        var vintage = vintageDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var url = BuildRequestUrl(series, from, to) + $"&realtime_start={vintage}&realtime_end={vintage}&limit=100000";
        using var response = await ExecuteGetAsync(url, series, "typed economic observations", ct).ConfigureAwait(false);
        if ((int)response.StatusCode == 429)
            throw new RateLimitException("FRED economic observations rate limit exceeded.", provider: Name, symbol: series,
                retryAfter: HttpResiliencePolicy.ExtractRetryAfter(response) ?? TimeSpan.FromSeconds(60));
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"FRED economic observations returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("observations", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("FRED economic response did not contain an observation array.");
        if (document.RootElement.TryGetProperty("count", out var count) && count.TryGetInt32(out var total) && total > rows.GetArrayLength())
            throw new InvalidDataException("FRED economic response was truncated; no partial observation population was accepted.");
        var observations = new List<BondMarketObservationInput>();
        var missing = new List<DateOnly>();
        foreach (var row in rows.EnumerateArray())
        {
            if (!row.TryGetProperty("date", out var dateNode) || !DateOnly.TryParseExact(dateNode.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidDataException("A FRED economic observation has an invalid economic date.");
            if (date < from || date > to)
                continue;
            var rawValue = row.TryGetProperty("value", out var valueNode) ? valueNode.GetString() : null;
            if (string.IsNullOrWhiteSpace(rawValue) || rawValue == ".")
            {
                missing.Add(date);
                continue;
            }
            if (!decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException("A supplied FRED economic value was malformed; it was not converted into zero or silently dropped.");
            var sourceVintage = row.TryGetProperty("realtime_start", out var revisionNode)
                && DateOnly.TryParseExact(revisionNode.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var revision)
                ? revision : vintageDate;
            if (sourceVintage > vintageDate)
                throw new InvalidDataException("FRED returned an observation outside the requested vintage cutoff.");
            // Per-row evidence stays stable when the same value is fetched in a different date window.
            var sourceFacts = JsonSerializer.Serialize(new[] { series, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), rawValue, sourceVintage.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
            observations.Add(BondMarketDataRules.Normalize(new BondMarketObservationInput(
                $"{series}:{date:yyyy-MM-dd}", sourceVintage.DayNumber + 1L, definition.Kind, Name, definition.IndexName,
                date, definition.Unit, value, definition.SourceName, Sha256Digest.ComputeUtf8(sourceFacts),
                $"https://fred.stlouisfed.org/series/{Uri.EscapeDataString(series)}?observation_date={date:yyyy-MM-dd}&vintage_date={sourceVintage:yyyy-MM-dd}",
                Currency: definition.Currency, SourceVintageDate: sourceVintage)));
        }
        if (observations.GroupBy(row => row.ObservationKey, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidDataException("FRED returned competing observations for the same series/date/vintage.");
        return new(observations.OrderBy(row => row.EconomicDate).ToArray(), missing.Distinct().Order().ToArray(), json, Sha256Digest.ComputeUtf8(json));
    }
}
