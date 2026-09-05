using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Projects the existing lot of record without inventing missing acquisition facts.</summary>
public static class LedgerOpenLotProjection
{
    public static OpenLotDto ToOpenLot(this LedgerTaxLotRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var acquisition = record.Acquisition
            ?? throw new LedgerValidationException("Retained acquisition quantity-basis, currency, and FX evidence is required for canonical lot projection.");
        if (record.Currency != acquisition.AcquisitionCurrency)
            throw new LedgerValidationException("Lot currency does not match the retained acquisition currency.");
        if (record.OriginalQuantity * record.UnitCost != acquisition.TransactionCostBasis)
            throw new LedgerValidationException("Lot basis differs from retained acquisition facts; a governed adjustment projection is required.");
        var face = acquisition.QuantityBasis == LotQuantityBasis.Face;
        if (face != record.HasFaceValueTerms || (face && (record.ParBasis != acquisition.FaceValueTerms?.ParBasis
            || record.BookedFactor != acquisition.FaceValueTerms?.BookedFactor
            || record.OriginalFace != record.OriginalQuantity * LedgerTaxLotFaceValueTerms.LedgerLotParBasis)))
            throw new LedgerValidationException("Canonical quantity basis must match the lot of record's retained par terms.");
        var scale = face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        var fraction = record.OriginalQuantity > 0 ? record.OpenQuantity / record.OriginalQuantity : 0m;
        var lot = new OpenLotDto(record.TaxLotRecordId, record.SecurityId, record.BookPositionId, record.LedgerBookId,
            record.LotId, record.AcquiredDate, record.OriginalQuantity * scale, record.OpenQuantity * scale,
            acquisition.TransactionCostBasis * fraction, acquisition.FunctionalCostBasis * fraction, record.Version, acquisition);
        OpenLotValidation.Validate(lot);
        return lot;
    }
}
