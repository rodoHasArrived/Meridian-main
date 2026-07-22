namespace Meridian.Ledger;

/// <summary>
/// ASC 820 / IFRS 13 fair-value hierarchy level describing the observability of the inputs used to
/// price a position. Recorded on each daily mark so valuation evidence states how defensible the
/// price is, not merely what it was.
/// </summary>
public enum FairValueLevel
{
    /// <summary>Level not yet classified. The backward-compatible default for legacy marks.</summary>
    Unclassified,

    /// <summary>Level 1 — quoted (unadjusted) prices in active markets for identical assets (e.g. an exchange close).</summary>
    Level1,

    /// <summary>Level 2 — inputs other than quoted prices that are observable, directly or indirectly (e.g. matrix pricing, vendor composites).</summary>
    Level2,

    /// <summary>Level 3 — unobservable inputs, typically a model or manual mark for illiquid or private holdings.</summary>
    Level3,
}
