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
    /// acquisitions. Null when no wash sale applied (a gain, no replacements, or a policy that does
    /// not govern this sale date). When present, <see cref="RealizedGainOrLoss"/> still reports the
    /// full economic loss, while <see cref="Lines"/> recognize only
    /// <see cref="WashSaleOutcome.AllowedLoss"/> and capitalize
    /// <see cref="WashSaleOutcome.DisallowedLoss"/> into the replacement lot's basis.
    /// </summary>
    public WashSaleOutcome? WashSale { get; init; }

    /// <summary>
    /// The wash-sale loss deferred out of this period, or zero when no deferral applied. Convenience
    /// accessor for exports and read models that would otherwise repeat the null coalesce.
    /// </summary>
    public decimal DisallowedWashSaleLoss => WashSale?.DisallowedLoss ?? 0m;

    /// <summary>
    /// The gain or loss actually recognized on the ledger this period: the full economic result less
    /// any deferred wash-sale portion. A realized loss is negative, so adding back the (positive)
    /// disallowed amount moves it toward zero.
    /// </summary>
    public decimal RecognizedGainOrLoss => RealizedGainOrLoss + DisallowedWashSaleLoss;

    /// <summary>
    /// Economic result from parcels held one year or less. Parcel amounts are allocated so this and
    /// <see cref="LongTermRealizedGainOrLoss"/> sum exactly to <see cref="RealizedGainOrLoss"/>.
    /// </summary>
    public decimal ShortTermRealizedGainOrLoss => SumByCharacter(TaxCharacter.ShortTerm);

    /// <summary>Economic result from parcels held more than one year.</summary>
    public decimal LongTermRealizedGainOrLoss => SumByCharacter(TaxCharacter.LongTerm);

    private decimal SumByCharacter(TaxCharacter character)
        => Selections
            .Where(selection => selection.TaxCharacter == character)
            .Sum(static selection => selection.RealizedGainOrLoss);
}
