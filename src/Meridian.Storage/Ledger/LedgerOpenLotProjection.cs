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
        var retainedBasis = record.Currency == acquisition.AcquisitionCurrency ? acquisition.TransactionCostBasis
            : record.Currency == acquisition.FunctionalCurrency ? acquisition.FunctionalCostBasis
            : throw new LedgerValidationException("Lot currency must identify its retained acquisition or functional basis convention.");
        if (record.OriginalQuantity * record.UnitCost != retainedBasis)
            throw new LedgerValidationException("Lot basis differs from retained acquisition facts; a governed adjustment projection is required.");
        var face = acquisition.QuantityBasis == LotQuantityBasis.Face;
        if (face != record.HasFaceValueTerms || (face && (record.ParBasis != acquisition.FaceValueTerms?.ParBasis
            || record.BookedFactor != acquisition.FaceValueTerms?.BookedFactor
            || record.OriginalFace != record.OriginalQuantity * LedgerTaxLotFaceValueTerms.LedgerLotParBasis)))
            throw new LedgerValidationException("Canonical quantity basis must match the lot of record's retained par terms.");
        var scale = face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        var (openTransactionBasis, openFunctionalBasis) = ProjectOpenBasis(record, acquisition);
        var lot = new OpenLotDto(record.TaxLotRecordId, record.SecurityId, record.BookPositionId, record.LedgerBookId,
            record.LotId, record.AcquiredDate, record.OriginalQuantity * scale, record.OpenQuantity * scale,
            openTransactionBasis, openFunctionalBasis, record.Version, acquisition);
        OpenLotValidation.Validate(lot);
        return lot;
    }

    // Open basis follows the latest governed restatement when one exists, scaled by the quantity
    // relieved since it; otherwise it is the immutable acquisition basis scaled by open quantity.
    private static (decimal Transaction, decimal Functional) ProjectOpenBasis(
        LedgerTaxLotRecord record,
        OpenLotAcquisitionDto acquisition)
    {
        if (record.BasisAdjustment is not { } adjustment)
        {
            // Unchanged convention: retained reporting was certified against exactly this form.
            var fraction = record.OriginalQuantity > 0 ? record.OpenQuantity / record.OriginalQuantity : 0m;
            return (acquisition.TransactionCostBasis * fraction, acquisition.FunctionalCostBasis * fraction);
        }

        if (adjustment.MutationBatchId == Guid.Empty ||
            string.IsNullOrWhiteSpace(adjustment.Reason) ||
            adjustment.OpenQuantity <= 0m ||
            adjustment.OpenQuantity > record.OriginalQuantity ||
            record.OpenQuantity > adjustment.OpenQuantity ||
            adjustment.TransactionCostBasis < 0m ||
            adjustment.FunctionalCostBasis < 0m ||
            (acquisition.AcquisitionCurrency == acquisition.FunctionalCurrency &&
             adjustment.TransactionCostBasis != adjustment.FunctionalCostBasis))
        {
            throw new LedgerValidationException(
                "Governed lot basis adjustment is incomplete or does not bind the lot's open quantity and currency convention.");
        }

        return record.OpenQuantity == adjustment.OpenQuantity
            ? (adjustment.TransactionCostBasis, adjustment.FunctionalCostBasis)
            : (adjustment.TransactionCostBasis * record.OpenQuantity / adjustment.OpenQuantity,
               adjustment.FunctionalCostBasis * record.OpenQuantity / adjustment.OpenQuantity);
    }
}
