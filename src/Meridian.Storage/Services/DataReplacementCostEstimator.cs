using Meridian.Contracts.Catalog;

namespace Meridian.Storage.Services;

/// <summary>
/// Estimates what the locally collected market-data store would cost to repurchase from
/// commercial metered vendors, priced against a configurable rate card. Estimates are
/// deliberately conservative: two independent bases are computed (per symbol-day and per
/// gigabyte) and the lower one is reported as the headline figure. This is a pure
/// post-processing pass over the <see cref="StorageCatalog"/>; it never touches the data files.
/// </summary>
public static class DataReplacementCostEstimator
{
    /// <summary>
    /// Computes a replacement-cost estimate for <paramref name="catalog"/> using
    /// <paramref name="options"/>. Returns a zero-valued estimate when the catalog is empty
    /// or the meter is disabled.
    /// </summary>
    public static DataReplacementCostEstimate Estimate(StorageCatalog catalog, DataReplacementCostOptions options)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled || catalog.Symbols.Count == 0)
        {
            return DataReplacementCostEstimate.Empty(options);
        }

        // ── Symbol-day basis ───────────────────────────────────────────────
        // Each (symbol, event type) pair is priced per covered trading day, matching how
        // metered vendors price historical downloads. Trading days are preferred; calendar
        // days are the fallback when the catalog has not computed them.
        var byEventType = new Dictionary<string, (long SymbolDays, decimal Usd)>(StringComparer.OrdinalIgnoreCase);
        long totalSymbolDays = 0;
        decimal symbolDayBasisUsd = 0m;

        foreach (var entry in catalog.Symbols.Values)
        {
            var days = ResolveCoveredDays(entry);
            if (days <= 0)
                continue;

            var eventTypes = entry.EventTypes is { Length: > 0 }
                ? entry.EventTypes
                : UnclassifiedEventTypes;

            foreach (var eventType in eventTypes)
            {
                var rate = ResolveRate(options, eventType);
                var cost = days * rate;

                symbolDayBasisUsd += cost;
                totalSymbolDays += days;

                byEventType.TryGetValue(eventType, out var bucket);
                byEventType[eventType] = (bucket.SymbolDays + days, bucket.Usd + cost);
            }
        }

        // ── Volume basis ───────────────────────────────────────────────────
        // Uncompressed size is what a vendor would meter; fall back to on-disk size when the
        // raw figure is unavailable so the estimate stays conservative.
        var meteredBytes = catalog.Statistics.TotalBytesRaw > 0
            ? catalog.Statistics.TotalBytesRaw
            : catalog.Statistics.TotalBytesCompressed;
        var totalGigabytes = meteredBytes / 1_000_000_000m;
        var volumeBasisUsd = totalGigabytes * options.RatePerGigabyteUsd;

        // ── Conservative headline ──────────────────────────────────────────
        var positiveBases = new List<decimal>(2);
        if (symbolDayBasisUsd > 0m)
            positiveBases.Add(symbolDayBasisUsd);
        if (volumeBasisUsd > 0m)
            positiveBases.Add(volumeBasisUsd);
        var conservative = positiveBases.Count > 0 ? positiveBases.Min() : 0m;

        var breakdown = byEventType
            .Select(static kvp => new DataReplacementCostByEventType(
                kvp.Key,
                kvp.Value.SymbolDays,
                Math.Round(kvp.Value.Usd, 2)))
            .OrderByDescending(static row => row.EstimatedUsd)
            .ToArray();

        return new DataReplacementCostEstimate(
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Currency: options.Currency,
            ConservativeEstimateUsd: Math.Round(conservative, 2),
            SymbolDayBasisUsd: Math.Round(symbolDayBasisUsd, 2),
            VolumeBasisUsd: Math.Round(volumeBasisUsd, 2),
            TotalSymbols: catalog.Symbols.Count,
            TotalSymbolDays: totalSymbolDays,
            TotalGigabytes: Math.Round(totalGigabytes, 2),
            EventTypeBreakdown: breakdown,
            RateCardSource: options.RateCardSource,
            RateCardLastVerified: options.RateCardLastVerified,
            MethodologyNote: MethodologyNote);
    }

    private const string MethodologyNote =
        "Lower bound of two independent bases: per-symbol-day rates applied to covered trading days "
        + "per event type, and a per-gigabyte rate applied to uncompressed stored volume. Rates are "
        + "conservative low-end list prices from the configured rate card, not a vendor quote.";

    private static readonly string[] UnclassifiedEventTypes = ["Unclassified"];

    private static int ResolveCoveredDays(SymbolCatalogEntry entry)
    {
        if (entry.DateRange is { } range)
        {
            if (range.TradingDays > 0)
                return range.TradingDays;
            if (range.CalendarDays > 0)
                return range.CalendarDays;
        }

        // A symbol with recorded events but no computed range still covers at least one day.
        return entry.EventCount > 0 ? 1 : 0;
    }

    private static decimal ResolveRate(DataReplacementCostOptions options, string eventType)
    {
        if (options.HistoricalRatePerSymbolDayUsd is { } rates
            && rates.TryGetValue(eventType, out var rate)
            && rate >= 0m)
        {
            return rate;
        }

        return options.DefaultRatePerSymbolDayUsd;
    }
}

/// <summary>
/// Rate card and toggles for the data replacement-cost meter, bound from the
/// <c>DataReplacementCost</c> configuration section. Defaults are conservative low-end
/// list prices as of the recorded verification date; operators should refresh them from
/// current vendor rate cards rather than treating them as quotes.
/// </summary>
public sealed class DataReplacementCostOptions
{
    public const string SectionName = "DataReplacementCost";

    public bool Enabled { get; set; } = true;

    public string Currency { get; set; } = "USD";

    /// <summary>Historical per-symbol-day rates keyed by event type (e.g. Trade, Quote, Depth).</summary>
    public Dictionary<string, decimal>? HistoricalRatePerSymbolDayUsd { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Trade"] = 0.10m,
            ["Quote"] = 0.10m,
            ["Depth"] = 0.25m,
            ["OrderBook"] = 0.25m,
        };

    /// <summary>Fallback rate applied to event types not present in the rate card.</summary>
    public decimal DefaultRatePerSymbolDayUsd { get; set; } = 0.10m;

    /// <summary>Metered per-gigabyte rate applied to uncompressed stored volume.</summary>
    public decimal RatePerGigabyteUsd { get; set; } = 5.00m;

    /// <summary>Provenance note for the configured rates.</summary>
    public string RateCardSource { get; set; } =
        "Conservative low-end of published metered vendor list prices (per-symbol-day historical and per-GB tick data).";

    /// <summary>Date the rate card values were last checked against vendor pricing pages.</summary>
    public string RateCardLastVerified { get; set; } = "2026-07-01";
}

/// <summary>Replacement-cost estimate for the local market-data store.</summary>
public sealed record DataReplacementCostEstimate(
    DateTimeOffset GeneratedAtUtc,
    string Currency,
    decimal ConservativeEstimateUsd,
    decimal SymbolDayBasisUsd,
    decimal VolumeBasisUsd,
    int TotalSymbols,
    long TotalSymbolDays,
    decimal TotalGigabytes,
    IReadOnlyList<DataReplacementCostByEventType> EventTypeBreakdown,
    string RateCardSource,
    string RateCardLastVerified,
    string MethodologyNote)
{
    public static DataReplacementCostEstimate Empty(DataReplacementCostOptions options) => new(
        DateTimeOffset.UtcNow,
        options.Currency,
        0m,
        0m,
        0m,
        0,
        0,
        0m,
        Array.Empty<DataReplacementCostByEventType>(),
        options.RateCardSource,
        options.RateCardLastVerified,
        "No catalogued data or the meter is disabled.");
}

/// <summary>Per-event-type slice of the symbol-day cost basis.</summary>
public sealed record DataReplacementCostByEventType(
    string EventType,
    long SymbolDays,
    decimal EstimatedUsd);
