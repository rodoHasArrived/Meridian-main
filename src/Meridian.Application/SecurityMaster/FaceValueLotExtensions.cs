using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Adapts the canonical <see cref="FaceValueLot"/> aggregate to the <see cref="LedgerTaxLot"/>
/// shape the relief and amortization engines consume. This is the single seam where the engines'
/// price-per-100 lot convention is applied: whatever <see cref="FaceValueLot.ParBasis"/> the lot
/// was quoted in, the adapter normalizes quantity and unit cost so cost basis and premium/discount
/// are preserved exactly — a per-unit-priced lot can no longer silently mis-amortize through math
/// that assumes prices per 100.
/// </summary>
public static class FaceValueLotExtensions
{
    /// <summary>
    /// The par basis the ledger engines' lot math assumes (price per 100, quantity = face / 100).
    /// Sourced from the lot-of-record seam so the in-memory and persisted sides of the convention
    /// cannot drift apart.
    /// </summary>
    public const decimal LedgerLotParBasis = LedgerTaxLotFaceValueTerms.LedgerLotParBasis;

    public static LedgerTaxLot ToLedgerTaxLot(this FaceValueLot lot)
    {
        ArgumentNullException.ThrowIfNull(lot);

        // quantity x unitCost reproduces CostBasis and quantity x (unitCost - 100) reproduces
        // PremiumDiscount for any source ParBasis.
        return new LedgerTaxLot(
            lot.LotId,
            lot.AcquiredDate,
            quantity: lot.OriginalFace / LedgerLotParBasis,
            unitCost: lot.PricePercentOfPar * LedgerLotParBasis / lot.ParBasis,
            lot.SecurityId);
    }

    public static IReadOnlyList<LedgerTaxLot> ToLedgerTaxLots(this IEnumerable<FaceValueLot> lots)
    {
        ArgumentNullException.ThrowIfNull(lots);
        return lots.Select(static lot => lot.ToLedgerTaxLot()).ToArray();
    }
}
