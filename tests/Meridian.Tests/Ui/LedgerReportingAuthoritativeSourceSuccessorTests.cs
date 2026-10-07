using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.AssetOperations;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed partial class LedgerReportingAuthoritativeSourceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CaptureAsync_SuccessorBasisAndRefundedOnlyScheduleD_AreRetainedInCertifiedRows(bool advanceRefunding, bool functionalOnly)
    {
        var fixture = CreateFixture(successors: true,
            accountingBasis: advanceRefunding ? AccountingBasisKindDto.Statutory : AccountingBasisKindDto.Gaap);
        var batch = SuccessorBatch(fixture, advanceRefunding, functionalOnly);
        fixture.JournalStore.Records.Add(batch.Journal);
        fixture.JournalStore.Successors.Add(batch);

        var capture = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        var evidence = capture.DatasetRows[0]["openLotSuccessorEvidence"];
        using var document = JsonDocument.Parse(evidence);
        var retained = document.RootElement;
        capture.DatasetRows.Should().HaveCount(batch.Journal.Entry.Lines.Count,
            "successor evidence must not add another monetary row");
        capture.DatasetRows.Should().OnlyContain(row => row["openLotSuccessorEvidence"] == evidence);
        capture.DatasetRows[0]["openLotSuccessorEvidenceHash"].Should().Be(Sha256Digest.ComputeUtf8(evidence));
        capture.Checkpoint.EvidenceIds.Should().Contain($"open-lot-successor-evidence:{Sha256Digest.ComputeUtf8(evidence)}");
        retained.GetProperty("Mutations").GetArrayLength().Should().Be(advanceRefunding ? 3 : 2);
        evidence.Should().Contain("AcquisitionFxRateToFunctional").And.Contain("HoldingPeriodStartDate")
            .And.Contain("OpenTransactionCostBasis").And.Contain("OpenFunctionalCostBasis");
        var instruction = batch.CorporateAction!;
        instruction.Successors.Sum(item => item.Lot.OpenTransactionCostBasis).Should().Be(instruction.ExpectedLot.OpenTransactionCostBasis);
        instruction.Successors.Sum(item => item.Lot.OpenFunctionalCostBasis).Should().Be(instruction.ExpectedLot.OpenFunctionalCostBasis);
        if (advanceRefunding)
        {
            var plans = retained.GetProperty("Instruction").GetProperty("Projection").GetProperty("LotMutations").GetProperty("Mutations");
            plans[0].GetProperty("ReportingTags").EnumerateArray().Select(item => item.GetString()).Should().Equal("ScheduleD");
            plans[1].GetProperty("ReportingTags").GetArrayLength().Should().Be(0);
            capture.DatasetRows.Where(row => row["openLotScheduleD"] == "true").Should().ContainSingle()
                .Which["openLotSuccessorRole"].Should().Be("Refunded");
            capture.DatasetRows.Where(row => row["openLotSuccessorRole"] != "Refunded")
                .Should().OnlyContain(row => row["openLotScheduleD"] == "false");
        }
        else
            capture.DatasetRows.Should().OnlyContain(row => row["openLotScheduleD"] == "false");

        fixture.JournalStore.Successors[0] = batch with { IsExactReplay = true };
        var replay = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        replay.Checkpoint.CheckpointHash.Should().Be(capture.Checkpoint.CheckpointHash,
            "restart/replay status cannot alter retained accounting evidence");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("partial")]
    [InlineData("duplicate")]
    [InlineData("predecessor-open")]
    [InlineData("basis")]
    [InlineData("fx")]
    [InlineData("holding-period")]
    [InlineData("journal")]
    [InlineData("scope")]
    [InlineData("schedule-d")]
    [InlineData("missing-evidence")]
    public async Task CaptureAsync_IncompleteOrChangedSuccessorEvidence_FailsClosed(string fault)
    {
        var fixture = CreateFixture(successors: true, accountingBasis: AccountingBasisKindDto.Statutory);
        var original = SuccessorBatch(fixture, advanceRefunding: true);
        fixture.JournalStore.Records.Add(original.Journal);
        var changed = original;
        if (fault == "partial")
            changed = original with { Mutations = original.Mutations.Skip(1).ToArray() };
        else if (fault is "predecessor-open" or "basis" or "fx" or "holding-period")
        {
            var index = fault == "predecessor-open" ? 0 : 1;
            var mutation = original.Mutations[index];
            var after = fault switch
            {
                "predecessor-open" => mutation.LotAfter with { OpenQuantity = 0.01m },
                "basis" => mutation.LotAfter with
                {
                    BasisAdjustment = mutation.LotAfter.BasisAdjustment! with { FunctionalCostBasis = 1m }
                },
                "fx" => mutation.LotAfter with
                {
                    Acquisition = mutation.LotAfter.Acquisition! with { AcquisitionFxRateToFunctional = 2m }
                },
                _ => mutation.LotAfter with
                {
                    Acquisition = mutation.LotAfter.Acquisition! with { HoldingPeriodStartDate = new DateOnly(2024, 1, 1) }
                }
            };
            changed = original with
            {
                Mutations = original.Mutations.Select((item, ordinal) => ordinal == index ? item with { LotAfter = after } : item).ToArray(),
                MutatedLots = original.MutatedLots.Select((item, ordinal) => ordinal == index ? after : item).ToArray()
            };
        }
        else if (fault == "journal")
            changed = original with { Journal = original.Journal with { GlobalSequence = 99 } };
        else if (fault == "missing-evidence")
            changed = original with { RetainedEvidence = original.RetainedEvidence.Skip(1).ToArray() };
        else if (fault == "schedule-d")
            changed = original with
            {
                CorporateAction = original.CorporateAction! with
                {
                    Projection = original.CorporateAction.Projection with
                    {
                        LotMutations = original.CorporateAction.Projection.LotMutations! with
                        {
                            Mutations = original.CorporateAction.Projection.LotMutations.Mutations
                                .Select(item => item with { ReportingTags = ["ScheduleD"] }).ToArray()
                        }
                    }
                }
            };
        else if (fault == "scope")
            changed = original with
            {
                CorporateAction = original.CorporateAction! with
                {
                    ExpectedLot = original.CorporateAction.ExpectedLot with { LedgerBookId = Guid.NewGuid() }
                }
            };

        if (fault != "missing")
            fixture.JournalStore.Successors.Add(changed);
        if (fault == "duplicate")
            fixture.JournalStore.Successors.Add(changed);

        var capture = () => fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access).AsTask();
        await capture.Should().ThrowAsync<ReportingAuthoritativeSourceUnavailableException>();

        fixture.JournalStore.Successors.Clear();
        fixture.JournalStore.Successors.Add(original);
        var repaired = await fixture.Source.CaptureAsync(fixture.Parameters, fixture.Access);
        repaired.DatasetRows.Should().OnlyContain(row => row.ContainsKey("openLotSuccessorEvidenceHash"));
    }

    private static AtomicTaxLotJournalResult SuccessorBatch(Fixture fixture, bool advanceRefunding, bool functionalOnly = false)
    {
        var predecessor = OpenLotSuccessorTestData.Predecessor() with { LedgerBookId = fixture.Book.LedgerBookId };
        var targets = advanceRefunding
            ? new[] { OpenLotSuccessorTestData.Successor(predecessor, 0.6m), OpenLotSuccessorTestData.Successor(predecessor, 0.4m) }
            : [OpenLotSuccessorTestData.Successor(predecessor, 1m)];
        var instruction = OpenLotSuccessorTestData.Build(predecessor, targets, advanceRefunding, periodId: fixture.Period.PeriodId);
        var batchId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);
        var dimensions = new LedgerLineDimensionSet(fixture.FundId, CostCenterId: "cost-center-a",
            OrganizationId: fixture.OrganizationId.ToString("D"), BookId: fixture.Book.LedgerBookId.ToString("D"));
        var lines = targets.Select(target => new LedgerEntry(Guid.NewGuid(), journalId, at,
            new LedgerAccount("Assets:Successor", LedgerAccountType.Asset), target.Lot.OpenFunctionalCostBasis, 0m,
            "Corporate action", dimensions with
            {
                InstrumentId = target.Lot.SecurityId,
                PositionId = target.Lot.BookPositionId,
                TaxLotId = target.Lot.LotId
            }, new LedgerEntryCurrency(functionalOnly ? target.Lot.Acquisition.FunctionalCurrency : target.Lot.Acquisition.AcquisitionCurrency,
                target.Lot.Acquisition.FunctionalCurrency,
                functionalOnly ? target.Lot.OpenFunctionalCostBasis : target.Lot.OpenTransactionCostBasis, 0m,
                functionalOnly ? 1m : target.Lot.Acquisition.AcquisitionFxRateToFunctional)))
            .Append(new LedgerEntry(Guid.NewGuid(), journalId, at,
                new LedgerAccount("Assets:Predecessor", LedgerAccountType.Asset), 0m, predecessor.OpenFunctionalCostBasis,
                "Corporate action", dimensions with
                {
                    InstrumentId = predecessor.SecurityId,
                    PositionId = predecessor.BookPositionId,
                    TaxLotId = predecessor.LotId
                }, new LedgerEntryCurrency(functionalOnly ? predecessor.Acquisition.FunctionalCurrency : predecessor.Acquisition.AcquisitionCurrency,
                    predecessor.Acquisition.FunctionalCurrency, 0m,
                    functionalOnly ? predecessor.OpenFunctionalCostBasis : predecessor.OpenTransactionCostBasis,
                    functionalOnly ? 1m : predecessor.Acquisition.AcquisitionFxRateToFunctional))).ToArray();
        var journal = new LedgerJournalEntryRecord(new JournalEntry(journalId, at, "Corporate action", lines,
                new JournalEntryMetadata(ActivityType: "CorporateAction", SecurityId: predecessor.SecurityId,
                    EffectiveDate: instruction.Projection.EconomicEvent!.EffectiveDate, Tags: new Dictionary<string, string>
                    {
                        [CanonicalOpenLotSuccessorEvidence.InstructionFingerprintTag] = OpenLotSuccessors.Fingerprint(instruction)
                    })), Guid.NewGuid(), fixture.Period.PeriodId, null, null, 11, at,
            AccountingBasis: fixture.Book.AccountingBasis);
        var before = DurableSuccessorSnapshot(predecessor, Guid.NewGuid(), at);
        var after = before with { OpenQuantity = 0m, Version = before.Version + 1, LastMutationBatchId = batchId };
        var mutations = new List<LedgerTaxLotMutationRecord>
        {
            SuccessorMutation(before, after, 0)
        };
        mutations.AddRange(targets.Select((target, index) => SuccessorMutation(null,
            DurableSuccessorSnapshot(target.Lot, batchId, at, instruction, journalId), index + 1)));
        var retainedEvidence = predecessor.Acquisition.Evidence.Concat(targets.SelectMany(target => target.Lot.Acquisition.Evidence))
            .Concat(instruction.Projection.EvidenceManifest.Select(dependency => new RetainedEvidenceIdentityDto(
                dependency.EvidenceId, dependency.EvidenceUri, dependency.ContentHashSha256, "SecurityMaster", dependency.SubjectId,
                "Accepted", "reviewer", at, instruction.Projection.EconomicEvent!.EffectiveDate, dependency.EvidenceVersion,
                at, "retention-service", dependency.SubjectType, dependency.SubjectId)))
            .Distinct().ToArray();
        mutations = mutations.Select(mutation => mutation with { RetainedEvidence = retainedEvidence }).ToList();
        return new AtomicTaxLotJournalResult(batchId, AtomicTaxLotMutationKind.CorporateAction,
            "sha256:" + new string('f', 64), false, journal, mutations.Select(item => item.LotAfter).ToArray(), mutations,
            retainedEvidence, CorporateAction: instruction);

        LedgerTaxLotMutationRecord SuccessorMutation(LedgerTaxLotRecord? source, LedgerTaxLotRecord target, int ordinal)
            => new(Guid.NewGuid(), batchId, AtomicTaxLotMutationKind.CorporateAction, target.TaxLotRecordId, target.LotId,
                ordinal, source?.OpenQuantity ?? 0m, target.OpenQuantity - (source?.OpenQuantity ?? 0m), target.OpenQuantity,
                target.UnitCost, source?.ToOpenLot().OpenFunctionalCostBasis ?? target.ToOpenLot().OpenFunctionalCostBasis,
                source?.Version ?? 0, target.Version, "approved-successor", journalId, instruction.Projection.EconomicEvent!.EventId,
                predecessor.Acquisition.Evidence, at, source, target, SecurityId: target.SecurityId, BookPositionId: target.BookPositionId);
    }

    private static LedgerTaxLotRecord DurableSuccessorSnapshot(OpenLotDto lot, Guid batchId, DateTimeOffset at,
        OpenLotSuccessorInstructionDto? instruction = null, Guid? journalId = null)
    {
        if (instruction is not null)
            lot = OpenLotSuccessors.WithLineage(instruction, lot);
        var scale = lot.Acquisition.QuantityBasis == LotQuantityBasis.Face ? LedgerTaxLotFaceValueTerms.LedgerLotParBasis : 1m;
        var quantity = lot.OriginalQuantity / scale;
        return new LedgerTaxLotRecord(lot.TaxLotRecordId, lot.LedgerBookId,
            new LedgerAccount(instruction is null ? "Assets:Predecessor" : "Assets:Successor", LedgerAccountType.Asset),
            lot.LotId, lot.AcquiredDate, quantity, lot.OpenQuantity / scale,
            lot.Acquisition.FunctionalCostBasis / quantity, lot.Acquisition.FunctionalCurrency, at, at,
            SourceJournalEntryId: journalId, Version: lot.Version, OriginatingMutationBatchId: instruction is null ? null : batchId,
            LastMutationBatchId: batchId, SecurityId: lot.SecurityId, BookPositionId: lot.BookPositionId,
            OriginalFace: lot.Acquisition.QuantityBasis == LotQuantityBasis.Face ? lot.OriginalQuantity : null,
            BookedFactor: lot.Acquisition.FaceValueTerms?.BookedFactor, ParBasis: lot.Acquisition.FaceValueTerms?.ParBasis,
            Acquisition: lot.Acquisition, BasisAdjustment: new OpenLotBasisAdjustmentDto(batchId,
                instruction is null ? OpenLotBasisAdjustmentReasons.Amortization : OpenLotBasisAdjustmentReasons.CorporateActionSuccessor,
                lot.OpenQuantity / scale, lot.OpenTransactionCostBasis, lot.OpenFunctionalCostBasis, CorporateAction: instruction));
    }
}
