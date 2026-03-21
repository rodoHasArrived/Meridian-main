namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Processing tier for market events.
/// </summary>
public enum MarketEventTier : byte
{
    /// <summary>
    /// Raw event from data provider.
    /// </summary>
    Raw = 0,

    /// <summary>
    /// Derived event from processing pipeline.
    /// </summary>
    Derived = 1,

    /// <summary>
    /// Enriched event after canonicalization (symbol resolution, condition code mapping, venue normalization).
    /// </summary>
    Enriched = 2,

    /// <summary>
    /// Fully processed event after all pipeline stages.
    /// </summary>
    Processed = 3
}
