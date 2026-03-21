using System.Globalization;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Utilities;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Stooq;

/// <summary>
/// Pulls free end-of-day historical bars from Stooq (https://stooq.pl).
/// Extends BaseHistoricalDataProvider for common functionality.
/// </summary>
[DataSource("stooq", "Stooq (free EOD)", DataSourceType.Historical, DataSourceCategory.Free,
    Priority = 15, Description = "Free end-of-day OHLCV data from Stooq")]
[ImplementsAdr("ADR-001", "Stooq historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class StooqHistoricalDataProvider : BaseHistoricalDataProvider
{
    private const string BaseUrl = "https://stooq.pl/q/d/l";

    public override string Name => "stooq";
    public override string DisplayName => "Stooq (free EOD)";
    public override string Description => "Free daily OHLCV from stooq.pl (equities/ETFs, US suffix).";
    protected override string HttpClientName => HttpClientNames.StooqHistorical;

    // Stooq has no documented rate limits, use conservative defaults
    public override int Priority => 15;

    public StooqHistoricalDataProvider(HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log)
    {
    }

    /// <summary>
    /// Stooq uses lowercase symbols with dots replaced by dashes.
    /// </summary>
    protected override string NormalizeSymbol(string symbol)
    {
        return SymbolNormalization.NormalizeForStooq(symbol);
    }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ValidateSymbol(symbol);

        await WaitForRateLimitSlotAsync(ct).ConfigureAwait(false);

        var normalizedSymbol = NormalizeSymbol(symbol);
        var url = $"{BaseUrl}/?s={normalizedSymbol}.us&i=d";

        Log.Information("Requesting Stooq history for {Symbol} ({Url})", symbol, url);

        using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var httpResult = await ResponseHandler.HandleResponseAsync(response, symbol, "daily bars", ct: ct).ConfigureAwait(false);
            var errorMsg = httpResult.IsNotFound
                ? $"Symbol {symbol} not found (404)"
                : $"HTTP error {httpResult.StatusCode}: {httpResult.ReasonPhrase}";

            Log.Warning("Stooq HTTP error for {Symbol}: {Error}", symbol, errorMsg);
            throw new InvalidOperationException($"Failed to fetch Stooq data for {symbol}: {errorMsg}");
        }

        var csv = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var bars = ParseCsvResponse(csv, symbol, from, to);

        Log.Information("Fetched {Count} bars for {Symbol} from Stooq", bars.Count, symbol);
        return bars.OrderBy(b => b.SessionDate).ToArray();
    }

    private List<HistoricalBar> ParseCsvResponse(string csv, string symbol, DateOnly? from, DateOnly? to)
    {
        var bars = new List<HistoricalBar>();
        using var reader = new StringReader(csv);

        // Skip header row
        reader.ReadLine();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 6)
                continue;

            if (!DateOnly.TryParse(parts[0], out var date))
                continue;

            // Apply date filter
            if (from is not null && date < from.Value)
                continue;
            if (to is not null && date > to.Value)
                continue;

            if (!decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var open))
                continue;
            if (!decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var high))
                continue;
            if (!decimal.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var low))
                continue;
            if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                continue;
            if (!long.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var volume))
                continue;

            // Validate OHLC
            if (!IsValidOhlc(open, high, low, close))
                continue;

            bars.Add(new HistoricalBar(
                Symbol: symbol.ToUpperInvariant(),
                SessionDate: date,
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Source: Name,
                SequenceNumber: date.DayNumber));
        }

        return bars;
    }
}
