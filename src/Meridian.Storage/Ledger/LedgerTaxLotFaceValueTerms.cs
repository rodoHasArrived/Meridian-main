using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// The seam between the canonical <see cref="FaceValueLot"/> aggregate and the durable lot of
/// record. <see cref="FaceValueLot"/> owns the par-denominated economics — cost basis,
/// premium/discount, factor-restated face, amortized basis — and this is where those conventions
/// are written onto <see cref="LedgerTaxLotRecord"/> and read back off it, so a persisted lot can
/// be restated as the aggregate rather than re-derived by each consumer against a hardcoded 100.
/// </summary>
public static class LedgerTaxLotFaceValueTerms
{
    /// <summary>
    /// The par basis the ledger engines' lot math assumes: price per 100 of par, quantity = face
    /// / 100. A lot's own <see cref="LedgerTaxLotRecord.ParBasis"/> records the basis its price was
    /// originally struck in, which is what lets a per-unit quote survive persistence without
    /// silently mis-amortizing through math that assumes this one.
    /// </summary>
    public const decimal LedgerLotParBasis = 100m;

    /// <summary>
    /// Records <paramref name="lot"/>'s acquisition-time par conventions on the lot of record. The
    /// aggregate and the record must already describe the same acquisition: the record's quantity
    /// is the face expressed in the engines' unit convention, so it must equal the aggregate's face
    /// over <see cref="LedgerLotParBasis"/>, and cost basis must tie under both readings.
    /// </summary>
    public static LedgerTaxLotRecord WithFaceValueTerms(this LedgerTaxLotRecord record, FaceValueLot lot)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(lot);

        if (record.OriginalQuantity * LedgerLotParBasis != lot.OriginalFace)
        {
            throw new LedgerValidationException(
                $"Face-value lot '{lot.LotId}' states an original face of {lot.OriginalFace} but the " +
                $"lot of record holds a quantity of {record.OriginalQuantity}; the record's quantity " +
                "is the face expressed per 100 of par, so the two must agree.");
        }

        if (record.UnitCost * lot.OriginalFace / LedgerLotParBasis != lot.CostBasis)
        {
            throw new LedgerValidationException(
                $"Face-value lot '{lot.LotId}' states a cost basis of {lot.CostBasis} but the lot of " +
                $"record's quantity times unit cost is {record.OriginalQuantity * record.UnitCost}; a " +
                "lot whose par and unit statements of the same acquisition disagree must not be retained.");
        }

        return record with
        {
            OriginalFace = lot.OriginalFace,
            BookedFactor = lot.BookedFactor,
            ParBasis = lot.ParBasis
        };
    }

    /// <summary>
    /// Restates a persisted lot as the canonical aggregate, or returns <see langword="null"/> when
    /// the lot recorded no face terms — the lot of record carries them all-three-or-none, and a lot
    /// booked before they existed states nothing rather than defaulting to a price-per-100 quote at
    /// factor 1. Callers that need face economics must fail closed on the null rather than
    /// substituting a quantity.
    /// </summary>
    public static FaceValueLot? ToFaceValueLot(this LedgerTaxLotRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.HasFaceValueTerms)
        {
            return null;
        }

        // Inverts the normalization applied on the way in: the record holds the price in the
        // engines' per-100 convention, and ParBasis is the basis the quote was originally struck in.
        var pricePercentOfPar = record.UnitCost * record.ParBasis!.Value / LedgerLotParBasis;

        return new FaceValueLot(
            record.LotId,
            record.SecurityId,
            record.AcquiredDate,
            record.OriginalFace!.Value,
            pricePercentOfPar,
            record.BookedFactor!.Value,
            record.ParBasis!.Value);
    }
}
