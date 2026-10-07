using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;

namespace Meridian.Tests.Storage;

public sealed class CorporateActionSuccessorAncestryTests
{
    private static readonly LedgerAccount Investment = new("Split investments", LedgerAccountType.Asset);
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierSourceAction_CannotRepeatAfterAnInterveningActionEvenWithFreshCaseAndVersion(bool legacy)
    {
        var chain = BuildChain(legacy);
        var repeated = Split(chain.Current, chain.ActionA, 2m, 2, legacy);
        var validateImmediateOrigin = () => OpenLotSuccessors.Validate(repeated);
        validateImmediateOrigin.Should().NotThrow("the current lot's immediate origin is action B");
        repeated.Projection.CaseId.Should().NotBe(chain.First.Projection.CaseId);
        repeated.Projection.EconomicEvent!.EventId.Should().NotBe(chain.First.Projection.EconomicEvent!.EventId);
        repeated.Projection.EconomicEvent.EventVersion.Should().Be(2);
        OpenLotSuccessors.GetSourceCorporateActionId(repeated.Projection).Should().Be(chain.ActionA);
        if (legacy)
        {
            chain.Current.Acquisition!.CorporateActionLineage.Should().BeNull();
            chain.First.Projection.SourceCorporateActionId.Should().BeNull();
            repeated.Projection.SourceCorporateActionId.Should().BeNull();
        }
        var loaded = new List<Guid>();

        var validateAncestry = () => CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, repeated,
            id => { loaded.Add(id); return Task.FromResult(chain.Receipts.GetValueOrDefault(id)); });

        await validateAncestry.Should().ThrowAsync<LedgerValidationException>().WithMessage("*repeat a source corporate action*");
        loaded.Should().Equal(chain.SecondBatch, chain.FirstBatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctSourceAction_TraversesBothSuccessorBirthReceiptsAndAcceptsOriginalAcquisition(bool legacy)
    {
        var chain = BuildChain(legacy);
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(2));
        OpenLotSuccessors.Validate(instruction);
        var loaded = new List<Guid>();

        await CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => { loaded.Add(id); return Task.FromResult(chain.Receipts.GetValueOrDefault(id)); });

        loaded.Should().Equal(chain.SecondBatch, chain.FirstBatch, chain.AcquisitionBatch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HistoricalLineageWithoutStableSourceId_IsCertifiedWithoutChangingRetainedInstructions(bool suppliedTargetOrigins)
    {
        var chain = BuildChain(legacy: true, retainLegacyLineage: true, suppliedTargetOrigins: suppliedTargetOrigins);
        var origin = chain.Current.Acquisition!.CorporateActionLineage!;
        origin.Should().NotBeNull();
        origin.SourceCorporateActionId.Should().BeNull();
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy: false,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(2));
        OpenLotSuccessors.Validate(instruction);
        var retained = JsonSerializer.Serialize(chain.Receipts);
        var fingerprints = chain.Receipts.Values.Where(receipt => receipt.CorporateAction is not null)
            .ToDictionary(receipt => receipt.MutationBatchId, receipt => OpenLotSuccessors.Fingerprint(receipt.CorporateAction!));

        await CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => Task.FromResult(chain.Receipts.GetValueOrDefault(id)));

        JsonSerializer.Serialize(chain.Receipts).Should().Be(retained);
        foreach (var receipt in chain.Receipts.Values.Where(receipt => receipt.CorporateAction is not null))
            OpenLotSuccessors.Fingerprint(receipt.CorporateAction!).Should().Be(fingerprints[receipt.MutationBatchId]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitSourceIdentity_RequiresThatIdentityInRetainedLineage(bool suppliedTargetOrigins)
    {
        var chain = BuildChain(legacy: true, retainLegacyLineage: true, suppliedTargetOrigins: suppliedTargetOrigins);
        var receipts = chain.Receipts.ToDictionary(pair => pair.Key, pair => pair.Value.CorporateAction is not { } ancestor
            ? pair.Value : pair.Value with
            {
                CorporateAction = ancestor with
                {
                    Projection = ancestor.Projection with
                    { SourceCorporateActionId = OpenLotSuccessors.GetSourceCorporateActionId(ancestor.Projection) }
                }
            });
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy: false,
            effectiveDate: OpenLotSuccessorTestData.EffectiveDate.AddDays(2));

        var certify = () => CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => Task.FromResult(receipts.GetValueOrDefault(id)));

        await certify.Should().ThrowAsync<LedgerValidationException>().WithMessage("*ancestry cannot be certified*");
    }

    [Fact]
    public async Task LegacyAverageCostSurvivor_TraversesMixedDisposalAndBasisRedistributionReceipt()
    {
        var chain = BuildChain(legacy: false);
        var pristine = chain.Receipts[chain.AcquisitionBatch].MutatedLots.Single() with
        {
            OriginatingMutationBatchId = null,
            LastMutationBatchId = null
        };
        var batch = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var current = pristine with
        {
            Version = 2,
            LastMutationBatchId = batch,
            BasisAdjustment = new(batch, OpenLotBasisAdjustmentReasons.AverageCostRedistribution,
                60m, 630m, 693m)
        };
        var otherId = Guid.NewGuid();
        var other = pristine with
        {
            TaxLotRecordId = otherId,
            LotId = "selected-average-cost-lot",
            UnitCost = 12.1m,
            Acquisition = pristine.Acquisition! with
            {
                TransactionCostBasis = 660m,
                FunctionalCostBasis = 726m,
                Evidence = [pristine.Acquisition!.Evidence[0] with
                {
                    EvidenceId = "selected-average-cost-acquisition",
                    SubjectId = otherId.ToString("D")
                }]
            }
        };
        var closed = other with { OpenQuantity = 0m, Version = 2, LastMutationBatchId = batch };
        var survivorMutation = new LedgerTaxLotMutationRecord(Guid.NewGuid(), batch, AtomicTaxLotMutationKind.BasisRedistribution,
            current.TaxLotRecordId, current.LotId, 1, 60m, 0m, 60m, current.UnitCost, 33m, 1, 2,
            current.Acquisition!.Evidence[0].EvidenceId, Guid.NewGuid(), eventId, current.Acquisition.Evidence,
            RecordedAt, pristine, current, ReliefMethod: "AverageCost", SecurityId: current.SecurityId, BookPositionId: current.BookPositionId);
        var disposal = new LedgerTaxLotMutationRecord(Guid.NewGuid(), batch, AtomicTaxLotMutationKind.Disposal,
            other.TaxLotRecordId, other.LotId, 0, 60m, -60m, 0m, other.UnitCost, 693m, 1, 2,
            other.Acquisition!.Evidence[0].EvidenceId, Guid.NewGuid(), eventId, other.Acquisition.Evidence,
            RecordedAt, other, closed, ReliefMethod: "AverageCost", SecurityId: other.SecurityId, BookPositionId: other.BookPositionId);
        var receipt = Receipt(batch, AtomicTaxLotMutationKind.Disposal, [disposal, survivorMutation]);
        var instruction = Split(current, Guid.NewGuid(), 2m, 1, legacy: false);
        OpenLotSuccessors.Validate(instruction);
        var loaded = new List<Guid>();

        await CorporateActionSuccessorAncestry.ValidateAsync(current, instruction,
            id => { loaded.Add(id); return Task.FromResult<AtomicTaxLotJournalResult?>(id == batch ? receipt : null); });

        loaded.Should().Equal(batch);
        current.BasisAdjustment!.TransactionCostBasis.Should().Be(630m);
        pristine.BasisAdjustment.Should().BeNull();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cyclic")]
    [InlineData("cross-book")]
    public async Task UncertifiableBirthReceipt_RefusesFreshAction(string corruption)
    {
        var chain = BuildChain(legacy: false);
        var instruction = Split(chain.Current, Guid.NewGuid(), 2m, 1, legacy: false);
        var receipts = new Dictionary<Guid, AtomicTaxLotJournalResult>(chain.Receipts);
        var second = receipts[chain.SecondBatch];
        if (corruption == "missing")
            receipts.Remove(chain.FirstBatch);
        else if (corruption == "cyclic")
        {
            var predecessor = second.Mutations.Single(item => item.LotBefore is not null);
            receipts[chain.SecondBatch] = second with
            {
                Mutations = second.Mutations.Select(item => item == predecessor
                    ? item with { LotBefore = item.LotBefore! with { OriginatingMutationBatchId = chain.SecondBatch } }
                    : item).ToArray()
            };
        }
        else
        {
            var opening = second.Mutations.Single(item => item.LotBefore is null);
            receipts[chain.SecondBatch] = second with
            {
                Mutations = second.Mutations.Select(item => item == opening
                    ? item with { LotAfter = item.LotAfter with { LedgerBookId = Guid.NewGuid() } }
                    : item).ToArray()
            };
        }

        var validate = () => CorporateActionSuccessorAncestry.ValidateAsync(chain.Current, instruction,
            id => Task.FromResult(receipts.GetValueOrDefault(id)));

        await validate.Should().ThrowAsync<LedgerValidationException>().WithMessage("*ancestry cannot be certified*");
    }

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
