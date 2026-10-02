using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static OpenLotBasisAdjustmentDto? RetainDiscreteReliefRemainder(
        Guid mutationBatchId,
        LedgerTaxLotRecord before,
        LedgerTaxLotDisposalSelection selection)
    {
        var remainingQuantity = before.OpenQuantity - selection.Quantity;
        // Untreated acquisitions retain their existing proportional projection. A new disposal
        // treatment is needed only for already adjusted basis, preserving first amortization
        // after an ordinary acquisition-basis partial disposal.
        if (remainingQuantity == 0m || before.BasisAdjustment is null)
            return null;

        var canonical = before.ToOpenLot();
        var scale = canonical.Acquisition.QuantityBasis == LotQuantityBasis.Face
            ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        var relievedTransaction = canonical.OpenTransactionCostBasis * (selection.Quantity * scale) / canonical.OpenQuantity;
        // Subtract the exact certified relief instead of independently scaling the old adjustment:
        // both currencies conserve even when decimal division places a residual on this survivor.
        return new OpenLotBasisAdjustmentDto(
            mutationBatchId,
            OpenLotBasisAdjustmentReasons.DisposalRelief,
            remainingQuantity,
            canonical.OpenTransactionCostBasis - relievedTransaction,
            canonical.OpenFunctionalCostBasis - selection.ExpectedCostBasis);
    }
}
