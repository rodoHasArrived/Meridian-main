using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static LedgerTaxLotRecord ValidateAmortizationPaydownReceipt(
        LedgerTaxLotRecord cursor, DateOnly asOfDate, AtomicTaxLotJournalResult? receipt)
    {
        LedgerValidationException Unproved() => new("Remaining-face amortization requires the exact immutable paydown receipt and complete prior basis history.");
        if (receipt is not { MutationKind: AtomicTaxLotMutationKind.FactorPaydown, FactorPaydown: { } inputs }
            || receipt.MutationBatchId != cursor.BasisAdjustment!.MutationBatchId
            || inputs.EconomicEvent.EffectiveDate > asOfDate)
            throw Unproved();
        var matches = receipt.Mutations.Where(item => item.TaxLotRecordId == cursor.TaxLotRecordId).ToArray();
        if (matches.Length != 1 || matches[0].LotBefore is not { } before)
            throw Unproved();
        var mutation = matches[0];
        var after = mutation.LotAfter;
        var reviewed = inputs.Lots.SingleOrDefault(item => item.ExpectedLot.TaxLotRecordId == cursor.TaxLotRecordId);
        var projected = OpenLotFactorPaydown.Project(inputs).SingleOrDefault(item => item.TaxLotRecordId == cursor.TaxLotRecordId);
        if (reviewed is null || projected is null || mutation.MutationBatchId != receipt.MutationBatchId
            || mutation.JournalEntryId != receipt.Journal.Entry.JournalEntryId
            || mutation.ExpectedVersion != before.Version || mutation.ResultVersion != before.Version + 1
            || after.Version != mutation.ResultVersion || after.Version > cursor.Version
            || after.LastMutationBatchId != receipt.MutationBatchId
            || !OpenLotAmortization.SameLot(before.ToOpenLot(), reviewed.ExpectedLot)
            || !OpenLotAmortization.SameLot(after.ToOpenLot(), projected)
            || !SameAmortizationLot(cursor, after with
            { Version = cursor.Version, LastMutationBatchId = cursor.LastMutationBatchId, UpdatedAt = cursor.UpdatedAt }))
            throw Unproved();
        return before;
    }
}
