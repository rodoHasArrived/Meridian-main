namespace Meridian.Ledger;

/// <summary>
/// A single reference-data-derived cost-basis adjustment applied to open tax lots before
/// realized-gain relief. The adjustment is intentionally numeric and Security-Master-agnostic:
/// the application layer reads day-count, factor, and corporate-action data from the master and
/// projects it into these primitives, keeping the ledger relief engine free of reference-data
/// dependencies.
/// </summary>
/// <param name="Kind">Which transform to apply; fixes the meaning of <paramref name="Value"/>.</param>
/// <param name="Value">
/// Kind-specific magnitude: a split/factor ratio for <see cref="LedgerTaxLotBasisAdjustmentKind.Split"/>
/// and <see cref="LedgerTaxLotBasisAdjustmentKind.Factor"/>, a per-unit amount for
/// <see cref="LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital"/>, and a signed per-unit delta for
/// <see cref="LedgerTaxLotBasisAdjustmentKind.Amortization"/>.
/// </param>
/// <param name="EffectiveDate">
/// The date the adjustment takes economic effect (corporate-action ex-date, or the as-of date for
/// factor/amortization). Only lots acquired strictly before this date are adjusted.
/// </param>
/// <param name="SecurityId">
/// When set, the adjustment applies only to lots carrying the same
/// <see cref="LedgerTaxLot.SecurityId"/>; a null scope applies to every candidate lot.
/// </param>
/// <param name="LotId">
/// When set, the adjustment targets a single lot by identifier (used for per-lot amortization);
/// a null value applies to every lot matched by <paramref name="SecurityId"/>.
/// </param>
/// <param name="Reference">Optional provenance reference (e.g. corporate-action id or source tag).</param>
public sealed record LedgerTaxLotBasisAdjustment(
    LedgerTaxLotBasisAdjustmentKind Kind,
    decimal Value,
    DateOnly EffectiveDate,
    Guid? SecurityId = null,
    string? LotId = null,
    string? Reference = null)
{
    /// <summary>Validates the <see cref="Value"/> magnitude against the <see cref="Kind"/>.</summary>
    public LedgerTaxLotBasisAdjustment EnsureValid()
    {
        switch (Kind)
        {
            case LedgerTaxLotBasisAdjustmentKind.Split when Value <= 0m:
                throw new ArgumentOutOfRangeException(nameof(Value), Value, "Split ratio must be positive.");
            case LedgerTaxLotBasisAdjustmentKind.Factor when Value <= 0m:
                throw new ArgumentOutOfRangeException(nameof(Value), Value, "Factor ratio must be positive.");
            case LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital when Value < 0m:
                throw new ArgumentOutOfRangeException(nameof(Value), Value, "Return-of-capital amount cannot be negative.");
        }

        return this;
    }

    /// <summary>Returns true when this adjustment applies to <paramref name="lot"/>.</summary>
    public bool AppliesTo(LedgerTaxLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);

        if (SecurityId is { } securityScope && lot.SecurityId != securityScope)
            return false;
        if (!string.IsNullOrWhiteSpace(LotId) && !string.Equals(LotId, lot.LotId, StringComparison.OrdinalIgnoreCase))
            return false;

        // Only lots that existed before the adjustment's economic effect are restated; a lot
        // acquired on or after the ex-date already reflects the post-event terms.
        return EffectiveDate > lot.AcquiredDate;
    }
}
