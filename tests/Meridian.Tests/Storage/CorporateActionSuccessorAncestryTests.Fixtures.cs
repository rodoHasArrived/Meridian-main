using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;

namespace Meridian.Tests.Storage;

public sealed partial class CorporateActionSuccessorAncestryTests
{
    private static Chain BuildChain(bool legacy, bool retainLegacyLineage = false, bool suppliedTargetOrigins = false)
    {
        var basis = OpenLotSuccessorTestData.Predecessor();
        var original = basis with
        {
            OriginalQuantity = 60m,
            OpenQuantity = 60m,
            OpenTransactionCostBasis = 600m,
            OpenFunctionalCostBasis = 660m,
            Version = 1,
            Acquisition = basis.Acquisition with
            {
                QuantityBasis = LotQuantityBasis.Units,
                FaceValueTerms = null,
                TransactionCostBasis = 600m,
                FunctionalCostBasis = 660m
            }
        };
        var acquisitionBatch = Guid.NewGuid();
        var retained = Record(original, acquisitionBatch);
        var acquisition = Receipt(acquisitionBatch, AtomicTaxLotMutationKind.Acquisition, [Opening(retained, acquisitionBatch, Guid.NewGuid())]);
        var actionA = Guid.NewGuid();
        var first = Split(retained, actionA, 2m, 1, legacy);
        var (firstReceipt, firstTarget) = SuccessorReceipt(retained, first, legacy, retainLegacyLineage, suppliedTargetOrigins);
        var second = Split(firstTarget, Guid.NewGuid(), 0.5m, 1, legacy);
        var (secondReceipt, secondTarget) = SuccessorReceipt(firstTarget, second, legacy, retainLegacyLineage, suppliedTargetOrigins);
        return new(secondTarget, first, actionA, acquisitionBatch, firstReceipt.MutationBatchId, secondReceipt.MutationBatchId,
            new Dictionary<Guid, AtomicTaxLotJournalResult>
            {
                [acquisitionBatch] = acquisition,
                [firstReceipt.MutationBatchId] = firstReceipt,
                [secondReceipt.MutationBatchId] = secondReceipt
            });
    }

    private static OpenLotSuccessorInstructionDto Split(LedgerTaxLotRecord source, Guid actionId, decimal ratio,
        long sourceVersion, bool legacy, DateOnly? effectiveDate = null)
    {
        var predecessor = source.ToOpenLot();
        var successor = OpenLotSuccessorTestData.Successor(predecessor, 1m);
        var target = successor with
        {
            Lot = successor.Lot with
            {
                SecurityId = predecessor.SecurityId,
                BookPositionId = predecessor.BookPositionId,
                OriginalQuantity = predecessor.OpenQuantity * ratio,
                OpenQuantity = predecessor.OpenQuantity * ratio,
                Acquisition = successor.Lot.Acquisition with { CorporateActionLineage = null }
            }
        };
        var date = effectiveDate ?? source.Acquisition!.CorporateActionLineage?.EffectiveDate.AddDays(1)
            ?? OpenLotSuccessorTestData.EffectiveDate.AddDays(source.OriginalQuantity == 120m ? 1 : sourceVersion > 1 ? 2 : 0);
        var instruction = OpenLotSuccessorTestData.Build(predecessor, [target],
            actionType: ratio > 1m ? CorporateActionAccountingTypeDto.StockSplit : CorporateActionAccountingTypeDto.ReverseStockSplit,
            policyInputs: new(CarryHoldingPeriod: true), splitRatio: ratio, actionId: actionId,
            effectiveDate: date, sourceEventVersion: sourceVersion);
        return legacy ? instruction with { Projection = instruction.Projection with { SourceCorporateActionId = null } } : instruction;
    }

    private static (AtomicTaxLotJournalResult Receipt, LedgerTaxLotRecord Target) SuccessorReceipt(
        LedgerTaxLotRecord source, OpenLotSuccessorInstructionDto instruction, bool legacy,
        bool retainLegacyLineage = false, bool suppliedTargetOrigins = false)
    {
        var batch = Guid.NewGuid();
        var eventId = instruction.Projection.EconomicEvent!.EventId;
        var targetLot = instruction.Successors.Single().Lot;
        var created = legacy && !retainLegacyLineage ? targetLot : OpenLotSuccessors.WithLineage(instruction, targetLot);
        if (legacy && retainLegacyLineage)
            created = created with
            {
                Acquisition = created.Acquisition with
                {
                    CorporateActionLineage = created.Acquisition.CorporateActionLineage! with { SourceCorporateActionId = null }
                }
            };
        var target = Record(created, batch);
        if (suppliedTargetOrigins)
            instruction = instruction with { Successors = [instruction.Successors.Single() with { Lot = created }] };
        var closed = source with { OpenQuantity = 0m, Version = source.Version + 1, LastMutationBatchId = batch };
        var predecessor = new LedgerTaxLotMutationRecord(Guid.NewGuid(), batch, AtomicTaxLotMutationKind.CorporateAction,
            source.TaxLotRecordId, source.LotId, 0, source.OpenQuantity, -source.OpenQuantity, 0m,
            source.UnitCost, source.ToOpenLot().OpenFunctionalCostBasis, source.Version, closed.Version,
            source.Acquisition!.Evidence[0].EvidenceId, Guid.NewGuid(), eventId, source.Acquisition.Evidence,
            RecordedAt, source, closed, SecurityId: source.SecurityId, BookPositionId: source.BookPositionId);
        var opening = Opening(target, batch, eventId) with { MutationKind = AtomicTaxLotMutationKind.CorporateAction, SelectionOrdinal = 1 };
        var receipt = Receipt(batch, AtomicTaxLotMutationKind.CorporateAction, [predecessor, opening], instruction);
        return (receipt, target);
    }

    private static LedgerTaxLotRecord Record(OpenLotDto lot, Guid birthBatch)
        => new(lot.TaxLotRecordId, lot.LedgerBookId, Investment, lot.LotId, lot.AcquiredDate,
            lot.OriginalQuantity, lot.OpenQuantity, lot.Acquisition.FunctionalCostBasis / lot.OriginalQuantity,
            lot.Acquisition.FunctionalCurrency, RecordedAt, RecordedAt, Guid.NewGuid(), lot.Acquisition.Evidence[0].EvidenceId,
            lot.Version, birthBatch, birthBatch, lot.SecurityId, lot.BookPositionId, Acquisition: lot.Acquisition);

    private static LedgerTaxLotMutationRecord Opening(LedgerTaxLotRecord target, Guid batch, Guid eventId)
        => new(Guid.NewGuid(), batch, AtomicTaxLotMutationKind.Acquisition, target.TaxLotRecordId,
            target.LotId, 0, 0m, target.OriginalQuantity, target.OriginalQuantity, target.UnitCost,
            target.ToOpenLot().OpenFunctionalCostBasis, 0, 1, target.Acquisition!.Evidence[0].EvidenceId,
            Guid.NewGuid(), eventId, target.Acquisition.Evidence, RecordedAt, null, target,
            SecurityId: target.SecurityId, BookPositionId: target.BookPositionId);

    private static AtomicTaxLotJournalResult Receipt(Guid batch, AtomicTaxLotMutationKind kind,
        IReadOnlyList<LedgerTaxLotMutationRecord> mutations, OpenLotSuccessorInstructionDto? instruction = null)
    {
        var journalId = Guid.NewGuid();
        var book = mutations[0].LotAfter.LedgerBookId;
        var amount = mutations.Max(item => item.CostBasis);
        var eventId = mutations[0].SourceEventId;
        var journal = new JournalEntry(journalId, RecordedAt, "Retained immutable successor ancestry receipt",
            [new LedgerEntry(Guid.NewGuid(), journalId, RecordedAt, Investment, amount, 0m, "Successor opening"),
             new LedgerEntry(Guid.NewGuid(), journalId, RecordedAt, Investment, 0m, amount, "Predecessor relief")]);
        var rows = mutations.Select(item => item with { JournalEntryId = journalId }).ToArray();
        return new(batch, kind, new string('a', 64), false,
            new LedgerJournalEntryRecord(journal, book, Guid.NewGuid(), null, null, 1, RecordedAt, SourceEventId: eventId),
            rows.Select(item => item.LotAfter).ToArray(), rows,
            rows.SelectMany(item => item.RetainedEvidence).DistinctBy(item => item.EvidenceId).ToArray(), CorporateAction: instruction);
    }

    private sealed record Chain(LedgerTaxLotRecord Current, OpenLotSuccessorInstructionDto First, Guid ActionA,
        Guid AcquisitionBatch, Guid FirstBatch, Guid SecondBatch, Dictionary<Guid, AtomicTaxLotJournalResult> Receipts);
}
