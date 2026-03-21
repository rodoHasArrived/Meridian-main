namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Classification of expected trading activity for a symbol.
/// Determines monitoring thresholds for gap detection, completeness scoring,
/// SLA freshness, and anomaly detection.
/// </summary>
public enum LiquidityProfile : byte
{
    /// <summary>
    /// High liquidity - large cap equities, major ETFs, FX majors.
    /// Continuous quoting, hundreds of trades per minute.
    /// Gap threshold ~60s, expected ~1000+ events/hour.
    /// </summary>
    High = 0,

    /// <summary>
    /// Normal liquidity - mid-cap equities, sector ETFs, active options.
    /// Regular but not constant activity.
    /// Gap threshold ~120s, expected ~200 events/hour.
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Low liquidity - small-cap stocks, less active options, single-stock futures.
    /// Trades may be minutes apart during quiet periods.
    /// Gap threshold ~600s, expected ~20 events/hour.
    /// </summary>
    Low = 2,

    /// <summary>
    /// Very low liquidity - OTC stocks, thinly-traded options, illiquid bonds.
    /// Trades may be 10+ minutes apart; wide spreads are normal.
    /// Gap threshold ~1800s, expected ~5 events/hour.
    /// </summary>
    VeryLow = 3,

    /// <summary>
    /// Minimal activity - deep OTC, far out-of-money options, exotic instruments.
    /// May have fewer than a handful of trades per day.
    /// Gap threshold ~3600s, expected ~1 event/hour.
    /// </summary>
    Minimal = 4
}
