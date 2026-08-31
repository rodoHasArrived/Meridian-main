namespace Meridian.Ledger;

/// <summary>
/// The kind of cost-basis adjustment applied to an open <see cref="LedgerTaxLot"/> before
/// realized-gain relief. Each kind fixes the meaning of
/// <see cref="LedgerTaxLotBasisAdjustment.Value"/> so the relief engine can consume
/// reference-data-derived adjustments without depending on Security Master contracts.
/// </summary>
public enum LedgerTaxLotBasisAdjustmentKind
{
    /// <summary>
    /// Share-count restatement from a forward or reverse split.
    /// <see cref="LedgerTaxLotBasisAdjustment.Value"/> is the split ratio R (new shares per old
    /// share; forward split R&gt;1, reverse split 0&lt;R&lt;1). Quantity is multiplied by R and unit
    /// cost is divided by R, preserving total basis.
    /// </summary>
    Split,

    /// <summary>
    /// Return-of-capital distribution that reduces cost basis rather than recognizing income.
    /// <see cref="LedgerTaxLotBasisAdjustment.Value"/> is the per-unit capital returned; unit
    /// cost is reduced by that amount (floored at zero).
    /// </summary>
    ReturnOfCapital,

    /// <summary>
    /// Pool-factor restatement for amortizing debt (MBS/ABS/loans).
    /// <see cref="LedgerTaxLotBasisAdjustment.Value"/> is the factor ratio applied to the lot's
    /// outstanding face (current factor ÷ prior factor, 0&lt;ratio≤1). Quantity (face) is scaled
    /// by the ratio while unit cost is unchanged, so basis is relieved in proportion to the
    /// principal paid down. Lots are assumed to be recorded at their prior-factor face.
    /// </summary>
    Factor,

    /// <summary>
    /// Premium amortization / discount accretion of a fixed-income lot toward par.
    /// <see cref="LedgerTaxLotBasisAdjustment.Value"/> is the signed per-unit change applied to
    /// unit cost: negative amortizes a premium down toward par, positive accretes a discount up
    /// toward par (floored at zero).
    /// </summary>
    Amortization,

    /// <summary>
    /// A wash-sale loss deferred from an earlier disposal and capitalized into this replacement lot
    /// under IRC §1091(d). <see cref="LedgerTaxLotBasisAdjustment.Value"/> is the total (not
    /// per-unit) disallowed amount added to the lot's basis, spread across the lot's quantity, and
    /// <see cref="LedgerTaxLotBasisAdjustment.HoldingPeriodCarryDate"/> carries the disallowed
    /// sale's holding-period start onto the replacement under §1223(3). Unlike the reference-data
    /// kinds this adjustment targets one named lot and applies whether the replacement was acquired
    /// before or after the sale, because the §1091 window is symmetric.
    /// </summary>
    WashSale,
}
