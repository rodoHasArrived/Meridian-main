using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SuccessorAmortization_PremiumExchangeContinuesBothBasesAndReplaysAfterPeriodLock()
        => await AssertSuccessorAmortizationAsync(true);

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SuccessorAmortization_DiscountExchangeContinuesBothBasesAndReplaysAfterPeriodLock()
        => await AssertSuccessorAmortizationAsync(false);

    private static async Task AssertSuccessorAmortizationAsync(bool premium)
    {
        await using var fixture = await AmortFixture.CreateAsync(premium);
        var source = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var exchange = await fixture.Store.AppendAssetPostingAsync(await SuccessorCommandAsync(fixture, false));
        var lot = exchange.Mutations.Single(item => item.LotBefore is null).LotAfter;
        // Closed predecessor reference is no longer live authority for an immutable schedule receipt.
        await fixture.Securities.UpsertProjectionAsync(fixture.Security with { Version = 2 });
        var preview = await PreviewSuccessorAmortizationAsync(fixture, lot, new DateOnly(2026, 9, 1));
        preview.Instruction.Continuation!.SuccessorMutationBatchId.Should().Be(exchange.MutationBatchId);
        OpenLotAmortization.SameInstruction(preview.Instruction.Continuation.PredecessorAmortization,
            source.MutatedLots.Single().BasisAdjustment!.Amortization).Should().BeTrue();
        preview.Projection.TransactionCostBasis.Should().Be(premium ? 6_100m : 5_900m);
        preview.Projection.FunctionalCostBasis.Should().Be(premium ? 6_710m : 6_490m);
        var command = await ContinuationCommandAsync(fixture, preview.Instruction);

        var result = await fixture.Restart().AppendAssetPostingAsync(command);

        var mutation = result.Mutations.Single();
        mutation.QuantityDelta.Should().Be(0m);
        mutation.LotAfter.Acquisition.Should().BeEquivalentTo(lot.Acquisition, options => AmortizationSnapshotOptions(options));
        mutation.LotAfter.ToOpenLot().OpenFunctionalCostBasis.Should().Be(preview.Projection.FunctionalCostBasis);
        mutation.LotAfter.ToOpenLot().OpenTransactionCostBasis.Should().Be(preview.Projection.TransactionCostBasis);
        result.Journal.Entry.Lines.Sum(line => line.Debit).Should().Be(Math.Abs(preview.Projection.FunctionalMovement));
        result.Journal.Entry.Lines.Sum(line => line.Credit).Should().Be(Math.Abs(preview.Projection.FunctionalMovement));
        var reloaded = (await fixture.Restart().GetAtomicTaxLotPostingAsync(result.MutationBatchId))!;
        OpenLotAmortization.SameInstruction(reloaded.MutatedLots.Single().BasisAdjustment!.Amortization,
            preview.Instruction).Should().BeTrue();
        await fixture.LockPeriodAsync();
        var auditBeforeReplay = (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Should().BeEquivalentTo(result.Mutations, options => AmortizationSnapshotOptions(options));
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(3);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBeforeReplay);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SuccessorAmortization_ForgedReceiptAndStaleTargetRefuseWithoutWriting()
    {
        await using var fixture = await AmortFixture.CreateAsync(true);
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var exchange = await fixture.Store.AppendAssetPostingAsync(await SuccessorCommandAsync(fixture, false));
        var lot = exchange.Mutations.Single(item => item.LotBefore is null).LotAfter;
        var preview = await PreviewSuccessorAmortizationAsync(fixture, lot, new DateOnly(2026, 9, 1));
        var forged = preview.Instruction with
        { Continuation = preview.Instruction.Continuation! with { SuccessorMutationBatchId = Guid.NewGuid() } };
        var command = await ContinuationCommandAsync(fixture, forged);
        var post = () => fixture.Restart().AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*exact immutable exchange*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();

        command = await ContinuationCommandAsync(fixture, preview.Instruction);
        await fixture.Securities.UpsertProjectionAsync(preview.Instruction.Security with { Version = 2 });
        post = () => fixture.Restart().AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*Security Master*stale*");
        (await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId, [lot.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(lot, options => AmortizationSnapshotOptions(options));
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SuccessorAmortization_MissingPredecessorReceiptCannotPrepareContinuation()
    {
        await using var fixture = await AmortFixture.CreateAsync(true);
        var amortization = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var exchange = await fixture.Store.AppendAssetPostingAsync(await SuccessorCommandAsync(fixture, false));
        var lot = exchange.Mutations.Single(item => item.LotBefore is null).LotAfter;
        var resolve = () => SuccessorAmortizationEvidence.ResolveAsync(lot,
            id => id == amortization.MutationBatchId ? Task.FromResult<AtomicTaxLotJournalResult?>(null)
                : fixture.Store.GetAtomicTaxLotPostingAsync(id));
        await resolve.Should().ThrowAsync<LedgerValidationException>().WithMessage("*complete immutable*");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SuccessorAmortization_LateFailureRollsBackThenRetriesExactly()
    {
        await using var fixture = await AmortFixture.CreateAsync(true);
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var exchange = await fixture.Store.AppendAssetPostingAsync(await SuccessorCommandAsync(fixture, false));
        var lot = exchange.Mutations.Single(item => item.LotBefore is null).LotAfter;
        var preview = await PreviewSuccessorAmortizationAsync(fixture, lot, new DateOnly(2026, 9, 1));
        var command = await ContinuationCommandAsync(fixture, preview.Instruction);
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var injection = connection.CreateCommand();
        injection.CommandText = $"""
            create function "{fixture.Options.SchemaName}".refuse_successor_amortization() returns trigger language plpgsql as $$
            begin raise exception 'injected late successor amortization failure'; end $$;
            create trigger refuse_successor_amortization before insert on "{fixture.Options.SchemaName}".tax_lot_mutations
                for each row execute function "{fixture.Options.SchemaName}".refuse_successor_amortization();
            """;
        await injection.ExecuteNonQueryAsync();
        var auditBeforeFailure = (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents;
        var post = () => fixture.Store.AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<PostgresException>().WithMessage("*injected late successor amortization failure*");
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [lot.TaxLotRecordId])).Single()
            .Should().BeEquivalentTo(lot, options => AmortizationSnapshotOptions(options));
        (await fixture.Store.GetAtomicTaxLotPostingAsync(command.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(2);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBeforeFailure);
        injection.CommandText = $"drop trigger refuse_successor_amortization on \"{fixture.Options.SchemaName}\".tax_lot_mutations;";
        await injection.ExecuteNonQueryAsync();
        var result = await fixture.Restart().AppendAssetPostingAsync(command);
        result.IsExactReplay.Should().BeFalse();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
        (await fixture.Store.GetByPeriodAsync(fixture.Period.PeriodId)).Should().HaveCount(3);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SuccessorAmortization_ReversalRestoresBirthBasisAndRebookRetainsTheSchedule()
    {
        await using var fixture = await AmortFixture.CreateAsync(true);
        await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var exchange = await fixture.Store.AppendAssetPostingAsync(await SuccessorCommandAsync(fixture, false));
        var lot = exchange.Mutations.Single(item => item.LotBefore is null).LotAfter;
        var date = new DateOnly(2026, 9, 1);
        var preview = await PreviewSuccessorAmortizationAsync(fixture, lot, date);
        var result = await fixture.Store.AppendAssetPostingAsync(await ContinuationCommandAsync(fixture, preview.Instruction));
        var service = new CanonicalLotAmortizationService(fixture.Restart(), fixture.Securities, fixture.Positions);
        var reversal = await service.PreviewReversalAsync(fixture.BookId, result.MutationBatchId);
        var reversed = await fixture.Store.AppendAssetPostingAsync(await ContinuationCommandAsync(fixture, reversal.Instruction, result));
        reversed.MutatedLots.Single().BasisAdjustment.Should().BeEquivalentTo(lot.BasisAdjustment, options => AmortizationSnapshotOptions(options));
        var restored = reversed.MutatedLots.Single();
        var rebookPreview = await PreviewSuccessorAmortizationAsync(fixture, restored, date);
        var unlinked = await ContinuationCommandAsync(fixture, rebookPreview.Instruction);
        var postUnlinked = () => fixture.Store.AppendAssetPostingAsync(unlinked);
        await postUnlinked.Should().ThrowAsync<LedgerValidationException>().WithMessage("*approved rebook lineage*");
        var rebook = await fixture.Store.AppendAssetPostingAsync(await ContinuationCommandAsync(fixture, rebookPreview.Instruction, reversed));
        rebook.MutatedLots.Single().ToOpenLot().OpenFunctionalCostBasis.Should().Be(preview.Projection.FunctionalCostBasis);
        rebook.CorrectsMutationBatchId.Should().Be(reversed.MutationBatchId);
        var later = await PreviewSuccessorAmortizationAsync(fixture, rebook.MutatedLots.Single(), new DateOnly(2026, 10, 1));
        var postedLater = await fixture.Store.AppendAssetPostingAsync(await ContinuationCommandAsync(fixture, later.Instruction));
        postedLater.MutatedLots.Single().ToOpenLot().OpenTransactionCostBasis.Should().Be(6_075m);
    }

    private static async Task<CanonicalLotAmortizationPreview> PreviewSuccessorAmortizationAsync(
        AmortFixture fixture, LedgerTaxLotRecord lot, DateOnly date)
    {
        var security = (await fixture.Securities.GetProjectionAsync(lot.SecurityId))!;
        return await new CanonicalLotAmortizationService(fixture.Restart(), fixture.Securities, fixture.Positions)
            .PreviewAsync(fixture.BookId, lot.TaxLotRecordId, date, AmortSecurityEvidence(security));
    }

    private static async Task<AtomicTaxLotJournalCommand> ContinuationCommandAsync(
        AmortFixture fixture, OpenLotAmortizationInstructionDto instruction, AtomicTaxLotJournalResult? corrects = null)
    {
        var projection = OpenLotAmortization.Project(instruction);
        var lot = instruction.ExpectedLot;
        var position = (await fixture.Positions.GetBookPositionAsync(lot.BookPositionId))!;
        var context = position.BookContext with { PeriodId = fixture.Period.PeriodId };
        var eventId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        var key = "successor-amortization:" + journalId.ToString("N");
        var at = new DateTimeOffset(instruction.AsOfDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var eventEvidence = BuildEvidence(key, 'c') with
        { EffectiveDate = instruction.AsOfDate, SubjectType = AssetAccountingEvidenceSubjects.Event, SubjectId = eventId.ToString("D") };
        RetainedEvidenceIdentityDto[] evidence = [.. lot.Acquisition.Evidence, instruction.SecurityEvidence, eventEvidence];
        var eventType = AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.DepreciationAmortization);
        var economicEvent = new EconomicEventReferenceDto(eventId, eventType, 1, instruction.AsOfDate, at,
            eventEvidence.SourceSystem, eventEvidence.SourceReference, SourceContentHash: eventEvidence.ContentHashSha256)
        {
            SecurityId = lot.SecurityId,
            BookPositionId = lot.BookPositionId,
            RetainedEvidence = evidence,
            EvidenceLinks = evidence.Select(item => item.EvidenceUri).ToArray()
        };
        var lineage = new ProjectionLineageDto(Guid.NewGuid(), null, "canonical-lot-amortization", OpenLotAmortization.ModelVersion,
            "amortization-v1", "base", instruction.AsOfDate, at, eventEvidence.SourceSystem, eventEvidence.SourceReference, economicEvent)
        { BookPositionId = lot.BookPositionId, RetainedEvidence = evidence, EvidenceLinks = economicEvent.EvidenceLinks };
        var posting = new AccountingPostingCommandDto(Guid.NewGuid(), fixture.BookId, fixture.Period.PeriodId,
            instruction.AsOfDate, at, key, instruction.Reversal is not null ? AccountingPostingIntentDto.Reversal
                : corrects is not null ? AccountingPostingIntentDto.Rebook : AccountingPostingIntentDto.Adjustment,
            eventId, ExpectedVersion: fixture.Period.Version, ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "independent-controller-review", OperatorRationale: "Reviewed unchanged exchange schedule.", LedgerBookId: fixture.BookId)
        {
            Actor = "independent-controller",
            SourceEventType = eventType,
            BookContext = context,
            BookPositionId = lot.BookPositionId,
            EconomicEvent = economicEvent,
            ProjectionLineage = lineage,
            RulePackReference = new("canonical-amortization", "1", "amortization", "1"),
            LotAmortization = instruction,
            SourceJournalEntryId = corrects?.Journal.Entry.JournalEntryId,
            Evidence = evidence.Select(item => new AccountingPostingEvidenceReferenceDto(item.EvidenceId,
                item.EvidenceUri, AccountingPostingEvidenceKindDto.Source, item.SourceSystem, item.RetainedAtUtc,
                item.RetainedBy, item.SubjectId, item.ContentHashSha256, SourceReference: item.SourceReference,
                Reviewer: item.ReviewedBy, ReviewedAtUtc: item.ReviewedAtUtc, EffectiveDate: item.EffectiveDate,
                EvidenceVersion: item.EvidenceVersion, ReviewStatus: item.ReviewStatus, SubjectType: item.SubjectType)).ToArray()
        };
        var dimensions = new LedgerLineDimensionSet(InstrumentId: lot.SecurityId) { PositionId = lot.BookPositionId };
        LedgerEntry Leg(LedgerAccount account, bool debit) => new(Guid.NewGuid(), journalId, at, account,
            debit ? Math.Abs(projection.FunctionalMovement) : 0m, debit ? 0m : Math.Abs(projection.FunctionalMovement),
            "Reviewed unchanged exchange schedule", dimensions, new(lot.Acquisition.AcquisitionCurrency, lot.Acquisition.FunctionalCurrency,
                debit ? Math.Abs(projection.TransactionMovement) : 0m, debit ? 0m : Math.Abs(projection.TransactionMovement), lot.Acquisition.AcquisitionFxRateToFunctional));
        var entry = new JournalEntry(journalId, at, "Reviewed unchanged exchange schedule",
            [Leg(AmortAccount, projection.FunctionalMovement > 0m), Leg(new("Amortization income", LedgerAccountType.Revenue), projection.FunctionalMovement < 0m)],
            new(SecurityId: lot.SecurityId, EffectiveDate: instruction.AsOfDate, IdempotencyKey: key, Tags: SecurityMasterLineageTags(lot.SecurityId)));
        var journal = new LedgerJournalEntryWrite(entry, fixture.BookId, fixture.Period.PeriodId,
            AccountingBasis: context.AccountingBasis, AccountingPolicyId: context.AccountingPolicyId, AccountingPolicyVersion: context.AccountingPolicyVersion,
            SourceEventId: eventId, LedgerBookId: fixture.BookId, PostingKind: LedgerPostingKindDto.Adjustment,
            RuleId: "amortization", RuleVersion: "1", PostingCommand: posting, SourceJournalEntryId: posting.SourceJournalEntryId,
            AdjustmentApproval: corrects is null ? null : new(posting.ApprovalId!, LedgerAdjustmentApprovalStatusDto.Approved,
                "independent-controller", at, "amortization-correction", EvidenceLink: "evidence://amortization/correction-approval"));
        return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, journal, eventId, key,
            fixture.Period.Version, AtomicTaxLotMutationKind.Amortization, evidence,
            correctsMutationBatchId: corrects?.MutationBatchId, amortization: instruction);
    }
}
