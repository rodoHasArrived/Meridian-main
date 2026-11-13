namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// A retained lot's entitlement for one accounting date. <see cref="OpenShare"/> reflects
/// closing disposals, while <see cref="AccrualFactor"/> is the factor before that date's
/// principal paydown: interest accrues through the paydown date before principal is reduced.
/// <see cref="Lot"/> retains the acquisition facts and the factor held at that day's close.
/// This read projection must never be persisted as a replacement lot of record.
/// </summary>
public sealed record FaceValueAccrualHolding(
    DateOnly Date,
    FaceValueLot Lot,
    decimal OpenShare,
    decimal AccrualFactor,
    Guid? TaxLotRecordId = null,
    long? Version = null)
{
    /// <summary>Face entitled to the date's interest, with the factor and open share applied once.</summary>
    public decimal AccrualFace => Lot.ParFace * OpenShare * AccrualFactor;
}
