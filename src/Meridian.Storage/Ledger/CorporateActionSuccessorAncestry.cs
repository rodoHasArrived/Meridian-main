using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Certifies source-action history from immutable birth receipts, independently of later basis treatments.</summary>
internal static class CorporateActionSuccessorAncestry
{
    public static async Task ValidateAsync(LedgerTaxLotRecord source, OpenLotSuccessorInstructionDto instruction,
        Func<Guid, Task<AtomicTaxLotJournalResult?>> loadReceipt, CancellationToken ct = default,
        Func<LedgerTaxLotRecord, Task>? validateHoldingPeriod = null)
    {
        var sourceActionId = OpenLotSuccessors.GetSourceCorporateActionId(instruction.Projection);
        var eventId = instruction.Projection.EconomicEvent!.EventId;
        var bookId = source.LedgerBookId;
        var cursor = source;
        var visited = new HashSet<Guid>();

        LedgerValidationException Unproved() => new("Successor ancestry cannot be certified from complete immutable predecessor receipts.");

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (cursor.LedgerBookId != bookId)
                throw Unproved();
            if (validateHoldingPeriod is not null)
                await validateHoldingPeriod(cursor).ConfigureAwait(false);
            var birthBatchId = cursor.OriginatingMutationBatchId;
            if (birthBatchId is null)
            {
                // A legacy source may have ordinary relief receipts but no original acquisition batch.
                // Walk those exact before/after snapshots back to a pristine retained legacy acquisition.
                if (cursor.Acquisition?.CorporateActionLineage is not null || cursor.BasisAdjustment?.CorporateAction is not null)
                    throw Unproved();
                if (cursor.LastMutationBatchId is null)
                {
                    if (cursor.Version != 1 || cursor.OpenQuantity != cursor.OriginalQuantity)
                        throw Unproved();
                    return;
                }
                var legacyReceipt = await LoadAsync(cursor.LastMutationBatchId.Value).ConfigureAwait(false);
                if (legacyReceipt.MutationKind is not (AtomicTaxLotMutationKind.Disposal
                    or AtomicTaxLotMutationKind.BasisRedistribution or AtomicTaxLotMutationKind.Amortization))
                    throw Unproved();
                var changed = legacyReceipt.Mutations.Where(item => item.TaxLotRecordId == cursor.TaxLotRecordId).ToArray();
                if (changed.Length != 1 || changed[0].LotBefore is not { } prior || prior.Version >= cursor.Version
                    || !SameSnapshot(cursor, changed[0].LotAfter))
                    throw Unproved();
                cursor = prior;
                continue;
            }

            var receipt = await LoadAsync(birthBatchId.Value).ConfigureAwait(false);
            var openings = receipt.Mutations.Where(item => item.TaxLotRecordId == cursor.TaxLotRecordId
                && item.LotBefore is null).ToArray();
            if (openings.Length != 1)
                throw Unproved();
            var opening = openings[0];
            if (opening.QuantityBefore != 0m || opening.QuantityDelta <= 0m
                || opening.QuantityAfter != opening.QuantityDelta || opening.QuantityAfter != cursor.OriginalQuantity
                || opening.ExpectedVersion != 0 || opening.ResultVersion != 1 || opening.LotAfter.Version != 1
                || opening.LotAfter.OriginatingMutationBatchId != birthBatchId
                || !SameAcquisition(cursor, opening.LotAfter))
                throw Unproved();

            if (receipt.MutationKind == AtomicTaxLotMutationKind.Acquisition)
            {
                if (receipt.Mutations.Count != 1 || receipt.CorporateAction is not null
                    || opening.LotAfter.Acquisition?.CorporateActionLineage is not null
                    || opening.LotAfter.BasisAdjustment?.CorporateAction is not null)
                    throw Unproved();
                return;
            }
            if (receipt.MutationKind != AtomicTaxLotMutationKind.CorporateAction || receipt.CorporateAction is not { } ancestor
                || receipt.CorrectsMutationBatchId is not null || ancestor.ExpectedLot.LedgerBookId != bookId
                || ancestor.Projection.AccountingScope?.LedgerBookId != bookId
                || (ancestor.Projection.SourceCorporateActionId is not null
                    && opening.LotAfter.Acquisition?.CorporateActionLineage is null))
                throw Unproved();
            try
            {
                OpenLotSuccessors.ValidateRetained(ancestor);
                if (OpenLotSuccessors.GetSourceCorporateActionId(ancestor.Projection) == sourceActionId
                    || ancestor.Projection.EconomicEvent!.EventId == eventId)
                    throw new LedgerValidationException("A successor cannot repeat a source corporate action retained anywhere in its immutable predecessor ancestry.");
            }
            catch (ArgumentException)
            {
                throw Unproved();
            }
            var predecessor = receipt.Mutations.Where(item => item.LotBefore is not null
                && item.TaxLotRecordId == ancestor.ExpectedLot.TaxLotRecordId).ToArray();
            var targets = ancestor.Successors.Where(item => item.Lot.TaxLotRecordId == cursor.TaxLotRecordId).ToArray();
            if (receipt.Mutations.Count != ancestor.Successors.Count + 1 || predecessor.Length != 1 || targets.Length != 1
                || predecessor[0].QuantityDelta >= 0m || predecessor[0].QuantityAfter != 0m
                || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(predecessor[0].LotBefore!.ToOpenLot()),
                    JsonSerializer.SerializeToElement(ancestor.ExpectedLot))
                || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(opening.LotAfter.ToOpenLot()),
                    JsonSerializer.SerializeToElement(opening.LotAfter.Acquisition?.CorporateActionLineage is { } retainedOrigin
                        ? OpenLotSuccessors.WithRetainedLineage(ancestor, targets[0].Lot, retainedOrigin) : targets[0].Lot)))
                throw Unproved();
            cursor = predecessor[0].LotBefore!;
        }

        async Task<AtomicTaxLotJournalResult> LoadAsync(Guid batchId)
        {
            if (batchId == Guid.Empty || !visited.Add(batchId))
                throw Unproved();
            AtomicTaxLotJournalResult? receipt;
            try
            { receipt = await loadReceipt(batchId).ConfigureAwait(false); }
            catch (InvalidOperationException) { throw Unproved(); }
            if (receipt is null || receipt.MutationBatchId != batchId || receipt.Mutations.Count == 0
                || receipt.Mutations.Any(item => item.MutationBatchId != batchId
                    || (item.MutationKind != receipt.MutationKind
                        && !(receipt.MutationKind == AtomicTaxLotMutationKind.Disposal
                            && item.MutationKind == AtomicTaxLotMutationKind.BasisRedistribution))
                    || item.LotAfter.LedgerBookId != bookId || item.LotBefore is { } before && before.LedgerBookId != bookId
                    || item.TaxLotRecordId != item.LotAfter.TaxLotRecordId))
                throw Unproved();
            return receipt;
        }
    }

    private static bool SameAcquisition(LedgerTaxLotRecord current, LedgerTaxLotRecord created)
        => current.TaxLotRecordId == created.TaxLotRecordId && current.LedgerBookId == created.LedgerBookId
           && current.Account == created.Account && current.LotId == created.LotId && current.AcquiredDate == created.AcquiredDate
           && current.SecurityId == created.SecurityId && current.BookPositionId == created.BookPositionId
           && current.OriginalQuantity == created.OriginalQuantity && current.OriginalFace == created.OriginalFace
           && current.BookedFactor == created.BookedFactor && current.ParBasis == created.ParBasis
           && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(current.Acquisition), JsonSerializer.SerializeToElement(created.Acquisition));

    private static bool SameSnapshot(LedgerTaxLotRecord current, LedgerTaxLotRecord retained)
        => SameAcquisition(current, retained)
           && JsonElement.DeepEquals(JsonSerializer.SerializeToElement(current.ToOpenLot()), JsonSerializer.SerializeToElement(retained.ToOpenLot()));
}
