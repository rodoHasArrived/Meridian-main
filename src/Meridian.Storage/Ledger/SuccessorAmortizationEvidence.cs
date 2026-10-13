using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Resolves continuation authority from immutable receipts, never from inherited yield alone.</summary>
public static class SuccessorAmortizationEvidence
{
    public static async Task<OpenLotAmortizationContinuationDto> ResolveAsync(LedgerTaxLotRecord current,
        Func<Guid, Task<AtomicTaxLotJournalResult?>> loadReceipt, CancellationToken ct = default)
    {
        LedgerValidationException Unproved() => new("Successor amortization requires complete immutable exchange and predecessor amortization receipts.");
        ct.ThrowIfCancellationRequested();
        if (current.Acquisition?.CorporateActionLineage is null
            || current.OriginatingMutationBatchId is not { } birthId || birthId == Guid.Empty)
            throw Unproved();
        var birth = await loadReceipt(birthId).ConfigureAwait(false);
        if (birth is null || birth.MutationBatchId != birthId || birth.MutationKind != AtomicTaxLotMutationKind.CorporateAction
            || birth.CorrectsMutationBatchId is not null || birth.CorporateAction is not { } exchange
            || exchange.Projection.Treatment.ActionType != CorporateActionAccountingTypeDto.RegS144AExchange
            || exchange.Successors.Count != 1 || birth.Mutations.Count != 2
            || birth.Mutations.Any(item => item.MutationBatchId != birthId || item.MutationKind != birth.MutationKind
                || item.JournalEntryId != birth.Journal.Entry.JournalEntryId || item.LotAfter.LedgerBookId != current.LedgerBookId))
            throw Unproved();
        try
        { OpenLotSuccessors.ValidateRetained(exchange); }
        catch (ArgumentException) { throw Unproved(); }
        var openings = birth.Mutations.Where(item => item.LotBefore is null && item.TaxLotRecordId == current.TaxLotRecordId).ToArray();
        var parents = birth.Mutations.Where(item => item.LotBefore is not null
            && item.TaxLotRecordId == exchange.ExpectedLot.TaxLotRecordId).ToArray();
        if (openings.Length != 1 || parents.Length != 1)
            throw Unproved();
        var opening = openings[0];
        var created = opening.LotAfter;
        var parent = parents[0];
        var before = parent.LotBefore!;
        if (created.OriginatingMutationBatchId != birthId || created.SourceJournalEntryId != birth.Journal.Entry.JournalEntryId
            || created.Acquisition?.CorporateActionLineage is null
            || opening.ExpectedVersion != 0 || opening.ResultVersion != 1 || created.Version != 1
            || opening.QuantityBefore != 0m || opening.QuantityDelta != created.OpenQuantity
            || opening.QuantityAfter != created.OpenQuantity || created.OriginalQuantity != created.OpenQuantity
            || current.Version < created.Version || current.OpenQuantity <= 0m || current.OpenQuantity > created.OpenQuantity
            || !SameRecord(current with
            {
                Version = created.Version,
                OpenQuantity = created.OpenQuantity,
                BasisAdjustment = created.BasisAdjustment,
                LastMutationBatchId = created.LastMutationBatchId,
                UpdatedAt = created.UpdatedAt
            }, created)
            || !OpenLotAmortization.SameLot(before.ToOpenLot(), exchange.ExpectedLot)
            || !OpenLotAmortization.SameLot(created.ToOpenLot(), OpenLotSuccessors.WithRetainedLineage(exchange,
                exchange.Successors[0].Lot, created.Acquisition!.CorporateActionLineage!))
            || parent.ExpectedVersion != before.Version || parent.ResultVersion != before.Version + 1
            || parent.QuantityBefore != before.OpenQuantity || parent.QuantityDelta != -before.OpenQuantity
            || parent.QuantityAfter != 0m || parent.LotAfter.OpenQuantity != 0m
            || before.Account != current.Account
            || before.BasisAdjustment is not { Reason: OpenLotBasisAdjustmentReasons.Amortization, Amortization: { } prior } adjustment
            || before.LastMutationBatchId != adjustment.MutationBatchId || adjustment.MutationBatchId == birthId)
            throw Unproved();
        ct.ThrowIfCancellationRequested();
        var amortization = await loadReceipt(adjustment.MutationBatchId).ConfigureAwait(false);
        if (amortization is null || amortization.MutationBatchId != adjustment.MutationBatchId
            || amortization.MutationKind != AtomicTaxLotMutationKind.Amortization || amortization.Mutations.Count != 1)
            throw Unproved();
        var retained = amortization.Mutations[0];
        if (retained.MutationBatchId != amortization.MutationBatchId || retained.MutationKind != amortization.MutationKind
            || retained.JournalEntryId != amortization.Journal.Entry.JournalEntryId
            || retained.LotBefore is null || !SameRecord(retained.LotAfter, before)
            || !OpenLotAmortization.SameLot(retained.LotBefore.ToOpenLot(), prior.ExpectedLot)
            || prior.Reversal is not null || prior.Continuation is not null
            || amortization.Journal.Entry.Metadata.EffectiveDate != prior.AsOfDate)
            throw Unproved();
        return new(OpenLotAmortizationContinuation.ModelVersion, birthId, exchange, prior);
    }

    private static bool SameRecord(LedgerTaxLotRecord left, LedgerTaxLotRecord right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left, AmortizationStorageJsonContext.Default.LedgerTaxLotRecord),
            JsonSerializer.SerializeToElement(right, AmortizationStorageJsonContext.Default.LedgerTaxLotRecord));
}
