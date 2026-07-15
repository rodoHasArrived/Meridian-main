namespace Meridian.Ledger;

/// <summary>
/// Wash-sale deferral policy applied when relieving a tax lot at a loss (US IRC §1091 and
/// equivalent regimes). When enabled, a realized loss that coincides with the acquisition of a
/// substantially-identical security within <see cref="WindowDays"/> days before or after the sale
/// is disallowed in proportion to the replacement quantity; the disallowed amount is capitalized
/// into the replacement lot's cost basis rather than recognized as a realized loss.
/// </summary>
/// <param name="Enabled">When false (the default) no wash-sale adjustment is applied and relief behaves exactly as before.</param>
/// <param name="WindowDays">Symmetric window, in days, around the sale date within which a replacement acquisition triggers deferral. Defaults to the US 30-day rule.</param>
public sealed record WashSalePolicy(bool Enabled, int WindowDays = 30)
{
    /// <summary>No wash-sale deferral; realized losses are always recognized in full.</summary>
    public static WashSalePolicy Disabled { get; } = new(false);

    /// <summary>The US IRC §1091 default: enabled with a 30-day symmetric replacement window.</summary>
    public static WashSalePolicy UnitedStates { get; } = new(true, 30);

    /// <summary>Validates the window magnitude.</summary>
    public WashSalePolicy EnsureValid()
    {
        if (WindowDays < 0)
            throw new ArgumentOutOfRangeException(nameof(WindowDays), WindowDays, "Wash-sale window days cannot be negative.");
        return this;
    }
}

/// <summary>
/// A candidate replacement purchase considered for wash-sale matching against a loss-generating
/// sale. The relief engine filters these to acquisitions that fall within the policy window and
/// match the security being sold; callers supply recent purchases of the position without having
/// to pre-apply the window themselves.
/// </summary>
/// <param name="LotId">Identifier of the replacement lot into which any disallowed loss is capitalized.</param>
/// <param name="AcquiredDate">Acquisition date of the replacement shares.</param>
/// <param name="Quantity">Replacement quantity acquired (must be positive to count).</param>
/// <param name="SecurityId">Optional Security Master identity used to confirm the replacement is substantially identical to the sold shares.</param>
public sealed record WashSaleReplacementAcquisition(
    string LotId,
    DateOnly AcquiredDate,
    decimal Quantity,
    Guid? SecurityId = null);

/// <summary>
/// The outcome of applying a <see cref="WashSalePolicy"/> to a loss-generating relief. Present on a
/// projection only when a wash sale was detected.
/// </summary>
/// <param name="DisallowedLoss">The portion of the realized loss deferred (never recognized this period).</param>
/// <param name="AllowedLoss">The portion of the realized loss still recognized as a realized loss.</param>
/// <param name="MatchedReplacementQuantity">Replacement quantity (capped at the quantity sold) that drove the disallowance.</param>
/// <param name="BasisIncreases">Per-replacement-lot cost-basis increases that carry the deferred loss forward, plus the holding-period carry date.</param>
public sealed record WashSaleOutcome(
    decimal DisallowedLoss,
    decimal AllowedLoss,
    decimal MatchedReplacementQuantity,
    IReadOnlyList<WashSaleBasisIncrease> BasisIncreases);

/// <summary>
/// A cost-basis increase applied to a replacement lot to carry a deferred wash-sale loss forward.
/// The replacement lot's holding period is also extended to include the sold shares' holding
/// period (<see cref="HoldingPeriodCarryDate"/> is the sold lots' earliest acquisition date).
/// </summary>
/// <param name="ReplacementLotId">The replacement lot whose basis increases.</param>
/// <param name="Amount">The deferred loss amount added to the replacement lot's basis.</param>
/// <param name="HoldingPeriodCarryDate">The earliest acquisition date of the relieved (sold) lots, used to extend the replacement lot's holding period.</param>
public sealed record WashSaleBasisIncrease(
    string ReplacementLotId,
    decimal Amount,
    DateOnly HoldingPeriodCarryDate);
