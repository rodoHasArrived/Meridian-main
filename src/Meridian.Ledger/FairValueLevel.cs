using Meridian.Contracts.Operations;

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

/// <summary>
/// Resolves the ASC 820 classification a daily mark may carry, given what the price source
/// asserted, what the fund's valuation policy defaults to, and where the figure actually came
/// from.
/// </summary>
/// <remarks>
/// The observability hierarchy describes inputs observed in a market. A simulated, seeded, or
/// sample price is not an observation of anything: it is a model output whose inputs are, by
/// construction, unobservable. So a non-real mark is classified <see cref="FairValueLevel.Level3"/>
/// and neither the price source nor the fund policy may raise it. Without this clamp a fabricated
/// price inherits <see cref="DailyPortfolioPricingPolicy.DefaultFairValueLevel"/> — or an
/// optimistic source assertion — and enters valuation evidence as an observable market input.
/// </remarks>
public static class FairValueLevelPolicy
{
    /// <summary>
    /// Applies the fund's policy default when the price source left the mark unclassified, then
    /// clamps the result to the ceiling implied by the mark's origin.
    /// </summary>
    public static FairValueLevel Resolve(
        FairValueLevel quoted,
        FairValueLevel policyDefault,
        DataProvenance provenance)
    {
        if (provenance.IsNonReal())
        {
            return FairValueLevel.Level3;
        }

        return quoted == FairValueLevel.Unclassified ? policyDefault : quoted;
    }
}
