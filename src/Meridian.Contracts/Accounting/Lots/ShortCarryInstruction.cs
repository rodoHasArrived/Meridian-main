namespace Meridian.Contracts.Accounting.Lots;

/// <summary>Period carrying charges on an open short lot: the stock borrow fee and the manufactured dividend.</summary>
public enum ShortCarryKind
{
    /// <summary>Daily borrow fee charged by the lender on the borrowed quantity.</summary>
    BorrowFee = 0,
    /// <summary>Payment in lieu of the dividend the lender would have received (manufactured dividend).</summary>
    PaymentInLieu = 1
}

/// <summary>
/// Reviewed instruction for one short carrying charge. The journal is derived from these retained inputs by
/// <c>ShortCarryProjector</c>; displayed amounts are never resubmitted. The instruction is functional-currency
/// only: a non-functional carrying charge needs the separate foreign-currency short treatment and is refused.
/// </summary>
/// <param name="TaxLotRecordId">The durable record identity of the open short lot the charge belongs to.</param>
/// <param name="LotId">The lot's business identity, retained beside the record identity so a renamed or re-keyed lot is refused rather than charged.</param>
/// <param name="Direction">Must be <see cref="OpenLotDirection.Short"/>; long lots carry no borrow fee or payment in lieu.</param>
/// <param name="Kind">Borrow fee or payment in lieu.</param>
/// <param name="AccrualStart">First accrued day (inclusive).</param>
/// <param name="AccrualEnd">Day after the last accrued day (exclusive); for a payment in lieu this is the pay date and the interval is informational.</param>
/// <param name="Quantity">Borrowed units the charge applies to; at most the lot's open quantity.</param>
/// <param name="ReferencePrice">Borrow fee only: the lender's reference price per unit for the period.</param>
/// <param name="AnnualRate">Borrow fee only: annual fee rate as a fraction (0.0325 for 3.25%).</param>
/// <param name="DaysPerYear">Borrow fee only: 360 or 365.</param>
/// <param name="AmountPerUnit">Payment in lieu only: the declared gross dividend per unit.</param>
/// <param name="Currency">Charge currency; must equal <paramref name="FunctionalCurrency"/>.</param>
/// <param name="FunctionalCurrency">The ledger book's functional currency.</param>
/// <param name="SourceEvidenceId">Lender statement or dividend notice the inputs were taken from.</param>
/// <param name="ModelVersion">Calculation version retained with the instruction.</param>
public sealed record ShortCarryInstruction(
    Guid TaxLotRecordId,
    string LotId,
    OpenLotDirection Direction,
    ShortCarryKind Kind,
    DateOnly AccrualStart,
    DateOnly AccrualEnd,
    decimal Quantity,
    decimal? ReferencePrice,
    decimal? AnnualRate,
    int? DaysPerYear,
    decimal? AmountPerUnit,
    string Currency,
    string FunctionalCurrency,
    string SourceEvidenceId,
    string ModelVersion = ShortCarryInstruction.CurrentModelVersion)
{
    public const string CurrentModelVersion = "short-carry-v1";

    public int AccruedDays => AccrualEnd.DayNumber - AccrualStart.DayNumber;
}
