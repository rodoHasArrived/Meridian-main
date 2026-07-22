namespace Meridian.Ledger;

/// <summary>
/// Realized-gain/loss projection produced by applying an account-level tax-lot relief method.
/// </summary>
public sealed record LedgerTaxLotReliefProjection(
    LedgerTaxLotReliefInput Input,
    IReadOnlyList<LedgerTaxLotReliefSelection> Selections,
    decimal Proceeds,
    decimal CostBasis,
    decimal RealizedGainOrLoss,
    IReadOnlyList<(LedgerAccount account, decimal debit, decimal credit)> Lines)
{
    public decimal TotalDebits => Lines.Sum(static line => line.debit);

    public decimal TotalCredits => Lines.Sum(static line => line.credit);

    public bool IsBalanced => TotalDebits == TotalCredits;

    /// <summary>
    /// The reference-data basis adjustments that were applied to the open lots before relief.
    /// Empty when the sale relieved lots at their recorded quantity and unit cost.
    /// </summary>
    public IReadOnlyList<LedgerTaxLotBasisAdjustment> AppliedAdjustments { get; init; } = [];

    /// <summary>The effective open lots after applying <see cref="AppliedAdjustments"/>.</summary>
    public IReadOnlyList<LedgerTaxLot> EffectiveLots { get; init; } = [];

    /// <summary>
    /// Wash-sale deferral outcome when a realized loss was disallowed against replacement
    /// acquisitions. Null when no wash sale applied (a gain, no replacements, or a disabled policy).
    /// When present, <see cref="RealizedGainOrLoss"/> still reports the full economic loss, while
    /// <see cref="Lines"/> recognize only <see cref="WashSaleOutcome.AllowedLoss"/> and capitalize
    /// <see cref="WashSaleOutcome.DisallowedLoss"/> into the replacement lot's basis.
    /// </summary>
    public WashSaleOutcome? WashSale { get; init; }
}
