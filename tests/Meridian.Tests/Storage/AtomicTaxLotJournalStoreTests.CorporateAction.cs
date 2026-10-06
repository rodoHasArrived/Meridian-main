using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_AdvanceRefunding_RetainsBothBasesDatesFxAndReportingAfterRestart()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var command = fixture.Command();
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        result.IsExactReplay.Should().BeFalse();
        result.Mutations.Should().HaveCount(3);
        result.Mutations.Count(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose).Should().Be(1);
        result.Mutations.Count(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor).Should().Be(2);
        var retained = (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId))!;
        retained.Should().NotBeNull();
        var proof = CorporateReport(command.CorporateAction!, retained);
        proof.PredecessorAfter.OpenQuantity.Should().Be(0m);
        proof.PredecessorAfter.Version.Should().Be(proof.PredecessorBefore.Version + 1);
        proof.Successors.Select(lot => lot.OpenQuantity).Should().Equal(3_600m, 2_400m);
        proof.Successors.Select(lot => lot.OpenTransactionCostBasis).Should().Equal(3_780m, 2_520m);
        proof.Successors.Select(lot => lot.OpenFunctionalCostBasis).Should().Equal(4_158m, 2_772m);
        proof.Successors.Select(lot => lot.Acquisition.TransactionCostBasis).Should().Equal(3_960m, 2_640m);
        proof.Successors.Select(lot => lot.Acquisition.FunctionalCostBasis).Should().Equal(4_356m, 2_904m);
        proof.Successors.Should().OnlyContain(lot => lot.AcquiredDate == AmortAcquired
            && lot.Acquisition.HoldingPeriodStartDate == AmortAcquired
            && lot.Acquisition.AcquisitionFxRateToFunctional == 1.1m
            && lot.Acquisition.QuantityBasis == LotQuantityBasis.Face);
        proof.Successors[0].Acquisition.CorporateActionLineage!.ReportingTags.Should().Equal("ScheduleD");
        proof.Successors[1].Acquisition.CorporateActionLineage!.ReportingTags.Should().BeEmpty();
        proof.Successors.Sum(lot => lot.OpenFunctionalCostBasis).Should().Be(proof.PredecessorBefore.OpenFunctionalCostBasis);
        proof.Successors.Sum(lot => lot.OpenTransactionCostBasis).Should().Be(proof.PredecessorBefore.OpenTransactionCostBasis);
        foreach (var successor in proof.Successors)
        {
            var current = (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId, [successor.TaxLotRecordId])).Single().ToOpenLot();
            current.Should().BeEquivalentTo(successor);
            (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, successor.SecurityId,
                successor.BookPositionId, fixture.Instruction.EffectiveDate.AddDays(-1))).Should().BeEmpty();
            (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, successor.SecurityId,
                successor.BookPositionId, fixture.Instruction.EffectiveDate)).Should().ContainSingle()
                .Which.ToOpenLot().OpenQuantity.Should().Be(successor.OpenQuantity);
        }
        (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, proof.PredecessorBefore.SecurityId,
            proof.PredecessorBefore.BookPositionId, fixture.Instruction.EffectiveDate.AddDays(-1))).Should().ContainSingle()
            .Which.ToOpenLot().OpenQuantity.Should().Be(6_000m);
        (await fixture.Restart().ListOpenTaxLotsByAssetScopeAsync(fixture.BookId, proof.PredecessorBefore.SecurityId,
            proof.PredecessorBefore.BookPositionId, fixture.Instruction.EffectiveDate)).Should().BeEmpty();
        var replay = await fixture.Restart().AppendAssetPostingAsync(command);
        replay.IsExactReplay.Should().BeTrue();
        replay.Mutations.Select(m => m.MutationRecordId).Should().Equal(result.Mutations.Select(m => m.MutationRecordId));
        await fixture.Base.LockPeriodAsync();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
        (await fixture.Store.GetByPeriodAsync(fixture.Base.Period.PeriodId)).Should().HaveCount(fixture.InitialJournalCount + 1);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_ConcurrentCommands_CommitOnceAndRejectStalePredecessor()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var first = fixture.Command();
        var freshSuccessors = fixture.Instruction.Successors.Select(successor =>
        {
            var id = Guid.NewGuid();
            return successor with
            {
                TaxLotRecordId = id,
                LotId = successor.LotId + "-competing",
                AcquisitionEvidence = successor.AcquisitionEvidence with
                { EvidenceId = "competing-" + id.ToString("N"), SubjectId = id.ToString("D") }
            };
        }).ToArray();
        var competingInstruction = fixture.Instruction with
        {
            CorporateActionId = Guid.NewGuid(),
            Successors = freshSuccessors,
            Mutations = fixture.Instruction.Mutations.Select((mutation, index) =>
                mutation with { TargetLotId = freshSuccessors[index].TaxLotRecordId }).ToArray()
        };
        var second = new CorporateFixture(fixture.Base, competingInstruction, fixture.InitialJournalCount, fixture.BookContext).Command();
        async Task<bool> Post(AtomicTaxLotJournalCommand command)
        {
            try
            { await fixture.Restart().AppendAssetPostingAsync(command); return true; }
            catch (LedgerValidationException) { return false; }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure) { return false; }
        }
        var outcomes = await Task.WhenAll(Post(first), Post(second));
        outcomes.Count(success => success).Should().Be(1);
        var loser = outcomes[0] ? second : first;
        var retry = () => fixture.Restart().AppendAssetPostingAsync(loser);
        await retry.Should().ThrowAsync<LedgerValidationException>().WithMessage("*predecessor*changed*");
        (await fixture.Store.GetAtomicTaxLotPostingAsync(loser.MutationBatchId)).Should().BeNull();
        (await fixture.Store.GetByPeriodAsync(fixture.Base.Period.PeriodId)).Should().HaveCount(fixture.InitialJournalCount + 1);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_LateSuccessorFailure_RollsBackJournalPredecessorSuccessorsAndAudit()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var command = fixture.Command();
        var auditBefore = await fixture.Store.VerifyLedgerEventAuditAsync();
        await using var connection = new NpgsqlConnection(fixture.Base.Options.ConnectionString);
        await connection.OpenAsync();
        await using var injection = connection.CreateCommand();
        injection.CommandText = $"""
            create function "{fixture.Base.Options.SchemaName}".reject_test_corporate_successor() returns trigger
            language plpgsql as $$ begin raise exception 'injected second successor failure'; end $$;
            create trigger reject_test_corporate_successor before insert on "{fixture.Base.Options.SchemaName}".tax_lot_mutations
            for each row when (new.mutation_kind = 'CorporateActionSuccessor' and new.tax_lot_record_id = '{fixture.Instruction.Successors[1].TaxLotRecordId}')
            execute function "{fixture.Base.Options.SchemaName}".reject_test_corporate_successor();
            """;
        await injection.ExecuteNonQueryAsync();
        var post = () => fixture.Store.AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<PostgresException>().WithMessage("*injected second successor failure*");
        await fixture.AssertUnchangedAsync(command);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore.ChainedEvents);
        injection.CommandText = $"drop trigger reject_test_corporate_successor on \"{fixture.Base.Options.SchemaName}\".tax_lot_mutations";
        await injection.ExecuteNonQueryAsync();
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_SequentialRefundingCarriesOriginalFactsAndRejectsPredatingSuccessorBirth()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var first = await fixture.Store.AppendAssetPostingAsync(fixture.Command());
        var firstRefunded = fixture.Instruction.Successors[0];
        var continuing = first.Mutations.Single(mutation => mutation.TaxLotRecordId == firstRefunded.TaxLotRecordId).LotAfter.ToOpenLot();
        var nextTargets = fixture.Instruction.Successors.Select((target, index) =>
        {
            var id = Guid.NewGuid();
            return target with
            {
                TaxLotRecordId = id,
                LotId = "second-" + target.LotId,
                AssetAccountId = "Second " + target.AssetAccountId,
                Security = index == 0 ? fixture.Instruction.Security : target.Security,
                SecurityEvidence = index == 0 ? fixture.Instruction.SecurityEvidence : target.SecurityEvidence,
                BookPositionId = index == 0 ? fixture.Instruction.ExpectedLot.BookPositionId : target.BookPositionId,
                Quantity = continuing.OpenQuantity * target.BasisAllocationPercent / 100m,
                AcquisitionEvidence = target.AcquisitionEvidence with
                { EvidenceId = "second-" + id.ToString("N"), SubjectId = id.ToString("D") }
            };
        }).ToArray();
        var nextMutations = nextTargets.Select(target =>
        {
            var fraction = target.BasisAllocationPercent / 100m;
            return new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Allocate, continuing.SecurityId,
                target.Security.SecurityId, target.Quantity, continuing.OpenFunctionalCostBasis * fraction, fraction,
                CorporateActionHoldingPeriodTreatmentDto.CarryOver, ReportingTags: target.ReportingTags,
                SourceLotId: continuing.TaxLotRecordId, ExpectedSourceLotVersion: continuing.Version,
                SourceBefore: new(continuing.OpenQuantity, continuing.OpenFunctionalCostBasis, continuing.OpenTransactionCostBasis),
                SourceAfter: new(0m, 0m, 0m), TargetLotId: target.TaxLotRecordId, TargetOperation: CorporateActionLotTargetOperationDto.Create,
                TargetAfter: new(target.Quantity, continuing.OpenFunctionalCostBasis * fraction, continuing.OpenTransactionCostBasis * fraction),
                BasisAmount: continuing.OpenTransactionCostBasis * fraction, SourceQuantity: target.Quantity,
                SourceCarryingAmount: continuing.OpenFunctionalCostBasis * fraction, SourceBasisAmount: continuing.OpenTransactionCostBasis * fraction);
        }).ToArray();
        var secondInstruction = new OpenLotCorporateActionInstructionDto(Guid.NewGuid(), CorporateActionAccountingTypeDto.AdvanceRefunding,
            fixture.Instruction.EffectiveDate.AddMonths(1), continuing, firstRefunded.Security, firstRefunded.SecurityEvidence,
            firstRefunded.ExpectedBookPositionVersion, nextTargets, nextMutations, firstRefunded.AssetAccountId);
        var secondFixture = new CorporateFixture(fixture.Base, secondInstruction, fixture.InitialJournalCount + 1, fixture.BookContext);
        var secondCommand = secondFixture.Command();
        var predating = secondInstruction with { EffectiveDate = fixture.Instruction.EffectiveDate.AddDays(-1) };
        var prepareBeforeBirth = () => new CorporateFixture(fixture.Base, predating, fixture.InitialJournalCount + 1, fixture.BookContext).Command();
        prepareBeforeBirth.Should().Throw<ArgumentException>().WithMessage("*before its retained origin*");
        await secondFixture.AssertUnchangedAsync(secondCommand);
        var second = await fixture.Restart().AppendAssetPostingAsync(secondCommand);
        var proof = CorporateReport(secondInstruction, second);
        proof.Successors.Select(lot => lot.OpenQuantity).Should().Equal(2_160m, 1_440m);
        proof.Successors.Select(lot => lot.OpenTransactionCostBasis).Should().Equal(2_268m, 1_512m);
        proof.Successors.Select(lot => lot.OpenFunctionalCostBasis).Should().Equal(2_494.8m, 1_663.2m);
        proof.Successors.Select(lot => lot.Acquisition.TransactionCostBasis).Should().Equal(2_376m, 1_584m);
        proof.Successors.Should().OnlyContain(lot => lot.AcquiredDate == AmortAcquired
            && lot.Acquisition.HoldingPeriodStartDate == AmortAcquired && lot.Acquisition.AcquisitionFxRateToFunctional == 1.1m);
        proof.Successors.SelectMany(lot => lot.Acquisition.Evidence).Should().NotContain(evidence =>
            evidence.SubjectType == AssetAccountingEvidenceSubjects.Event || evidence.SubjectType == AssetAccountingEvidenceSubjects.PostingApproval);
        var predecessors = await fixture.Store.GetTaxLotsByIdsAsync(fixture.BookId,
            [fixture.Instruction.ExpectedLot.TaxLotRecordId, continuing.TaxLotRecordId]);
        predecessors.Should().HaveCount(2).And.OnlyContain(lot => lot.OpenQuantity == 0m);
        (await fixture.Restart().AppendAssetPostingAsync(secondCommand)).IsExactReplay.Should().BeTrue();
        CorporateReport(fixture.Instruction, (await fixture.Restart().GetAtomicTaxLotPostingAsync(first.MutationBatchId))!)
            .Successors[0].OpenQuantity.Should().Be(3_600m, "the first immutable receipt remains unchanged after its successor is transformed");
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_JournalOnlyAppendCannotBypassAtomicLotBoundary()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var command = fixture.Command();
        var appendJournal = () => fixture.Store.AppendAsync(command.Journal);
        await appendJournal.Should().ThrowAsync<LedgerValidationException>();
        await fixture.AssertUnchangedAsync(command);
        (await fixture.Store.AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_NoncanonicalTypeOrMissingIndependentApproval_LeaveNoWrites()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var command = fixture.Command();
        var posting = command.Journal.PostingCommand!;
        foreach (var invalidPosting in new[]
        {
            posting with
            {
                SourceEventType = "CorporateAction",
                EconomicEvent = posting.EconomicEvent! with { EventType = "CorporateAction" },
                ProjectionLineage = posting.ProjectionLineage! with
                { TriggerEvent = posting.EconomicEvent! with { EventType = "CorporateAction" } }
            },
            posting with { ApprovalState = AccountingPostingApprovalStateDto.NotRequired },
            posting with { ApprovalId = null }
        })
        {
            var rejected = AtomicTaxLotJournalCommand.Create(command.MutationBatchId, fixture.BookId,
                command.Journal with { PostingCommand = invalidPosting }, command.SourceEventId, command.IdempotencyKey,
                command.ExpectedPeriodVersion, AtomicTaxLotMutationKind.CorporateAction, command.RetainedEvidence,
                corporateAction: fixture.Instruction);
            var post = () => fixture.Store.AppendAssetPostingAsync(rejected);

            await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*approved corporate-action command*");
            await fixture.AssertUnchangedAsync(rejected);
        }
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_SecondSuccessorLotNameCollision_RollsBackEveryWrite()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        // The second successor collides with the retained predecessor's account/lot natural key.
        // Its new durable ID passes the early identity check; the unique constraint fires after
        // the journal, predecessor close, and first successor insertion have been attempted.
        var instruction = fixture.Instruction with
        {
            Successors = [fixture.Instruction.Successors[0], fixture.Instruction.Successors[1] with
            { LotId = fixture.Instruction.ExpectedLot.LotId, AssetAccountId = AmortAccount.ToString() }]
        };
        var altered = new CorporateFixture(fixture.Base, instruction, fixture.InitialJournalCount, fixture.BookContext);
        var command = altered.Command();
        var auditBefore = await fixture.Store.VerifyLedgerEventAuditAsync();
        var post = () => fixture.Store.AppendAssetPostingAsync(command);
        await post.Should().ThrowAsync<PostgresException>().Where(exception => exception.SqlState == PostgresErrorCodes.UniqueViolation);
        await fixture.AssertUnchangedAsync(command);
        (await fixture.Store.VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore.ChainedEvents);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_StaleSuccessorReferenceAndPositionOrClosedPeriod_LeaveNoWrites()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var command = fixture.Command();
        var successor = fixture.Instruction.Successors[0];
        await fixture.Base.Securities.UpsertProjectionAsync(successor.Security with { Version = successor.Security.Version + 1 });
        var staleReference = () => fixture.Restart().AppendAssetPostingAsync(command);
        await staleReference.Should().ThrowAsync<LedgerValidationException>();
        await fixture.AssertUnchangedAsync(command);
        await fixture.Base.Securities.UpsertProjectionAsync(successor.Security);
        var snapshot = await fixture.Base.Positions.GetSecurityAsync(successor.Security.SecurityId);
        var position = snapshot.BookPositions.Single();
        await fixture.Base.Positions.UpsertAsync(snapshot.InstrumentRoles.Single(), position with { Version = position.Version + 1 },
            null, position.Version, new("independent-controller", "evidence://successor-position-revision", "Review successor", DateTimeOffset.UtcNow));
        var stalePosition = () => fixture.Restart().AppendAssetPostingAsync(command);
        await stalePosition.Should().ThrowAsync<LedgerValidationException>();
        await fixture.AssertUnchangedAsync(command);
        await fixture.Base.LockPeriodAsync();
        var currentPositions = fixture.Instruction with
        {
            Successors = [successor with { ExpectedBookPositionVersion = position.Version + 1 }, fixture.Instruction.Successors[1]]
        };
        var closedCommand = new CorporateFixture(fixture.Base, currentPositions, fixture.InitialJournalCount, fixture.BookContext).Command();
        var closedPeriod = () => fixture.Restart().AppendAssetPostingAsync(closedCommand);
        await closedPeriod.Should().ThrowAsync<LedgerValidationException>().WithMessage("*closed*");
        await fixture.AssertUnchangedAsync(command);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_ForeignSuccessorSleeveOrUnreviewedJournalDimensions_LeaveNoWrites()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var target = fixture.Instruction.Successors[0];
        var snapshot = await fixture.Base.Positions.GetSecurityAsync(target.Security.SecurityId);
        var position = snapshot.BookPositions.Single();
        var changed = await fixture.Base.Positions.UpsertAsync(snapshot.InstrumentRoles.Single(), position with
        {
            Version = position.Version + 1,
            BookContext = position.BookContext with
            {
                Dimensions = new(FundId: position.BookContext.FundProfileId, SleeveId: "foreign-sleeve",
                    InstrumentId: position.SecurityId, BookId: fixture.BookId.ToString("D"))
                { PositionId = position.PositionId }
            }
        }, null, position.Version,
            new("independent-controller", "evidence://foreign-sleeve-position", "Retain reviewed separate sleeve.", DateTimeOffset.UtcNow));
        var instruction = fixture.Instruction with
        { Successors = [target with { ExpectedBookPositionVersion = changed.Version }, fixture.Instruction.Successors[1]] };
        var foreignScope = new CorporateFixture(fixture.Base, instruction, fixture.InitialJournalCount, fixture.BookContext).Command();
        var post = () => fixture.Store.AppendAssetPostingAsync(foreignScope);
        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*dimension*");
        await fixture.AssertUnchangedAsync(foreignScope);

        var restored = await fixture.Base.Positions.UpsertAsync(snapshot.InstrumentRoles.Single(), position with { Version = changed.Version + 1 },
            null, changed.Version,
            new("independent-controller", "evidence://same-sleeve-position", "Retain reviewed predecessor scope.", DateTimeOffset.UtcNow));
        instruction = instruction with
        { Successors = [target with { ExpectedBookPositionVersion = restored.Version }, fixture.Instruction.Successors[1]] };
        var sameScope = new CorporateFixture(fixture.Base, instruction, fixture.InitialJournalCount, fixture.BookContext).Command();
        var entry = sameScope.Journal.Entry;
        var changedLines = entry.Lines.Select(line => line.Dimensions?.PositionId == target.BookPositionId
            ? new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account, line.Debit, line.Credit,
                line.Description, line.Dimensions with { SleeveId = "unreviewed-sleeve" }, line.Currency)
            : line).ToArray();
        var unreviewedJournal = sameScope.Journal with
        { Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, changedLines, entry.Metadata) };
        var unreviewed = AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), fixture.BookId, unreviewedJournal,
            sameScope.SourceEventId, sameScope.IdempotencyKey, sameScope.ExpectedPeriodVersion,
            AtomicTaxLotMutationKind.CorporateAction, sameScope.RetainedEvidence, corporateAction: instruction);
        var postUnreviewed = () => fixture.Store.AppendAssetPostingAsync(unreviewed);
        await postUnreviewed.Should().ThrowAsync<LedgerValidationException>().WithMessage("*dimension*");
        await fixture.AssertUnchangedAsync(unreviewed);
        (await fixture.Store.AppendAssetPostingAsync(sameScope)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CanonicalCorporateAction_ReportingRejectsTamperedSnapshotsAndJournal()
    {
        await using var fixture = await CorporateFixture.CreateAsync();
        var command = fixture.Command();
        var result = await fixture.Store.AppendAssetPostingAsync(command);
        var close = result.Mutations.Single(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose);
        var successors = result.Mutations.Where(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor)
            .Select(m => m.LotAfter.ToOpenLot()).ToArray();
        void Reject(OpenLotDto before, OpenLotDto after, IReadOnlyList<OpenLotDto> lots, JournalEntry? journal = null)
        {
            var project = () => CanonicalCorporateActionLotProjection.Project(fixture.Instruction,
                journal ?? result.Journal.Entry, close.LotBefore!.Account, before, after, lots);
            project.Should().Throw<LedgerValidationException>();
        }
        var before = close.LotBefore!.ToOpenLot();
        var after = close.LotAfter.ToOpenLot();
        Reject(before with { OpenTransactionCostBasis = before.OpenTransactionCostBasis + 1m }, after, successors);
        Reject(before, after with { Version = after.Version + 1 }, successors);
        Reject(before, after, [successors[0] with { OpenFunctionalCostBasis = successors[0].OpenFunctionalCostBasis + 1m }, successors[1]]);
        Reject(before, after, [successors[0], successors[1] with { AcquiredDate = fixture.Instruction.EffectiveDate }]);
        Reject(before, after, [successors[0], successors[1] with { Acquisition = successors[1].Acquisition with
        { CorporateActionLineage = successors[1].Acquisition.CorporateActionLineage! with { ReportingTags = ["ScheduleD"] } } }]);
        var journal = result.Journal.Entry;
        var alteredTags = journal.Metadata!.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value);
        alteredTags["lotCorporateActionHash"] = new string('f', 64);
        Reject(before, after, successors, new(journal.JournalEntryId, journal.Timestamp, journal.Description,
            journal.Lines, journal.Metadata with { Tags = alteredTags }));
        alteredTags = journal.Metadata.Tags.ToDictionary(pair => pair.Key, pair => pair.Value);
        alteredTags["lotCorporateActionInputs"] = System.Text.Json.JsonSerializer.Serialize(fixture.Instruction with
        { CorporateActionId = Guid.NewGuid() });
        Reject(before, after, successors, new(journal.JournalEntryId, journal.Timestamp, journal.Description,
            journal.Lines, journal.Metadata with { Tags = alteredTags }));
        var changedLines = journal.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp,
            line.Account, line.Debit, line.Credit, line.Description, line.Dimensions,
            new LedgerEntryCurrency("USD", "USD", line.Debit, line.Credit, 1m))).ToArray();
        Reject(before, after, successors, new(journal.JournalEntryId, journal.Timestamp, journal.Description, changedLines, journal.Metadata));
        CorporateReport(fixture.Instruction, result).Successors.Should().HaveCount(2);
    }

    private static CanonicalCorporateActionLotReport CorporateReport(OpenLotCorporateActionInstructionDto instruction, AtomicTaxLotJournalResult receipt)
    {
        var close = receipt.Mutations.Single(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionClose);
        var proof = CanonicalCorporateActionLotProjection.Project(receipt.Journal.Entry, close.LotBefore!.Account,
            close.LotBefore.ToOpenLot(), close.LotAfter.ToOpenLot(), receipt.Mutations
                .Where(m => m.MutationKind == AtomicTaxLotMutationKind.CorporateActionSuccessor).Select(m => m.LotAfter.ToOpenLot()).ToArray());
        proof.CorporateActionId.Should().Be(instruction.CorporateActionId);
        return proof;
    }

    private sealed class CorporateFixture(AmortFixture source, OpenLotCorporateActionInstructionDto instruction,
        int initialJournalCount, AccountingBookContextDto bookContext) : IAsyncDisposable
    {
        public AmortFixture Base { get; } = source;
        public OpenLotCorporateActionInstructionDto Instruction { get; } = instruction;
        public int InitialJournalCount { get; } = initialJournalCount;
        public AccountingBookContextDto BookContext { get; } = bookContext;
        public Guid BookId => Base.BookId;
        public PostgresLedgerJournalStore Store => Base.Store;
        public PostgresLedgerJournalStore Restart() => Base.Restart();

        public static async Task<CorporateFixture> CreateAsync()
        {
            var source = await AmortFixture.CreateAsync(premium: true);
            try
            {
                var amortized = await source.Store.AppendAssetPostingAsync(source.Command());
                var lot = amortized.MutatedLots.Single().ToOpenLot();
                var existing = await source.Positions.GetSecurityAsync(lot.SecurityId);
                var originalPosition = existing.BookPositions.Single();
                var originalRole = existing.InstrumentRoles.Single();
                var ledgerBook = (await source.Store.GetLedgerBookAsync(source.BookId))!;
                await source.Store.SaveLedgerBookAsync(ledgerBook with
                {
                    AccountingPolicyId = originalPosition.BookContext.AccountingPolicyId,
                    AccountingPolicyVersion = originalPosition.BookContext.AccountingPolicyVersion
                });
                var successors = new List<OpenLotCorporateActionSuccessorDto>();
                foreach (var refunded in new[] { true, false })
                {
                    var securityId = Guid.NewGuid();
                    var security = source.Security with
                    {
                        SecurityId = securityId,
                        DisplayName = refunded ? "Refunded bond" : "Unrefunded bond",
                        PrimaryIdentifierValue = "TESTREFUND" + securityId.ToString("N")
                    };
                    await source.Securities.UpsertProjectionAsync(security);
                    security = (await source.Securities.GetProjectionAsync(security.SecurityId))!;
                    var roleId = Guid.NewGuid();
                    var positionId = Guid.NewGuid();
                    var origin = originalPosition.OriginEvent! with { SecurityId = security.SecurityId, BookPositionId = positionId };
                    await source.Positions.UpsertAsync(originalRole with { RoleId = roleId, SecurityId = security.SecurityId, OriginEvent = origin },
                        originalPosition with
                        {
                            PositionId = positionId,
                            SecurityId = security.SecurityId,
                            RoleId = roleId,
                            Version = 1,
                            OriginEvent = origin
                        }, null, 0,
                        new("independent-controller", "evidence://successor-position", "Review successor ownership", DateTimeOffset.UtcNow));
                    var successorId = Guid.NewGuid();
                    var acquisitionEvidence = BuildEvidence("successor-" + successorId.ToString("N"), refunded ? 'c' : 'd') with
                    { EffectiveDate = lot.AcquiredDate, SubjectType = "OpenLotAcquisition", SubjectId = successorId.ToString("D") };
                    var securityEvidence = AmortSecurityEvidence(security) with { EvidenceId = "security-" + security.SecurityId.ToString("N") };
                    successors.Add(new(successorId, refunded ? "refunded-face" : "unrefunded-face", refunded ? "Refunded bond" : "Unrefunded bond",
                        security, securityEvidence, positionId, 1, refunded ? 3_600m : 2_400m, refunded ? 60m : 40m,
                        refunded ? CorporateActionSuccessorRoleDto.Refunded : CorporateActionSuccessorRoleDto.Unrefunded,
                        refunded ? ["ScheduleD"] : [], acquisitionEvidence));
                }
                var mutations = successors.Select(s => new CorporateActionLotMutationDto(CorporateActionLotMutationKindDto.Allocate,
                    lot.SecurityId, s.Security.SecurityId, s.Quantity, lot.OpenFunctionalCostBasis * s.BasisAllocationPercent / 100m,
                    s.BasisAllocationPercent / 100m, CorporateActionHoldingPeriodTreatmentDto.CarryOver, ReportingTags: s.ReportingTags,
                    SourceLotId: lot.TaxLotRecordId, ExpectedSourceLotVersion: lot.Version,
                    SourceBefore: new(lot.OpenQuantity, lot.OpenFunctionalCostBasis, lot.OpenTransactionCostBasis), SourceAfter: new(0m, 0m, 0m),
                    TargetLotId: s.TaxLotRecordId, TargetOperation: CorporateActionLotTargetOperationDto.Create,
                    TargetAfter: new(s.Quantity, lot.OpenFunctionalCostBasis * s.BasisAllocationPercent / 100m, lot.OpenTransactionCostBasis * s.BasisAllocationPercent / 100m),
                    BasisAmount: lot.OpenTransactionCostBasis * s.BasisAllocationPercent / 100m, SourceQuantity: s.Quantity,
                    SourceCarryingAmount: lot.OpenFunctionalCostBasis * s.BasisAllocationPercent / 100m,
                    SourceBasisAmount: lot.OpenTransactionCostBasis * s.BasisAllocationPercent / 100m)).ToArray();
                var instruction = new OpenLotCorporateActionInstructionDto(Guid.NewGuid(), CorporateActionAccountingTypeDto.AdvanceRefunding,
                    new DateOnly(2026, 6, 1), lot, source.Security, AmortSecurityEvidence(source.Security), originalPosition.Version, successors, mutations,
                    AmortAccount.ToString());
                var count = (await source.Store.GetByPeriodAsync(source.Period.PeriodId)).Count;
                return new(source, instruction, count, originalPosition.BookContext with { PeriodId = source.Period.PeriodId });
            }
            catch { await source.DisposeAsync(); throw; }
        }

        public AtomicTaxLotJournalCommand Command()
        {
            var projected = OpenLotCorporateAction.Project(Instruction);
            var id = Guid.NewGuid();
            var sourceId = Instruction.CorporateActionId;
            var key = "corporate-lot:" + Guid.NewGuid().ToString("N");
            var at = new DateTimeOffset(Instruction.EffectiveDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            LedgerEntry Leg(LedgerAccount account, Guid securityId, Guid positionId, decimal transaction, decimal functional, bool debit)
                => new(Guid.NewGuid(), id, at, account, debit ? functional : 0m, debit ? 0m : functional, "Reviewed advance refunding",
                    new LedgerLineDimensionSet(InstrumentId: securityId) { PositionId = positionId },
                    new("EUR", "USD", debit ? transaction : 0m, debit ? 0m : transaction, 1.1m));
            var lines = new List<LedgerEntry>
            {
                Leg(new(Instruction.SourceAssetAccountId, LedgerAccountType.Asset), Instruction.ExpectedLot.SecurityId, Instruction.ExpectedLot.BookPositionId,
                    Instruction.ExpectedLot.OpenTransactionCostBasis, Instruction.ExpectedLot.OpenFunctionalCostBasis, false)
            };
            lines.AddRange(projected.Select(p => Leg(new(p.Successor.AssetAccountId, LedgerAccountType.Asset), p.Successor.Security.SecurityId,
                p.Successor.BookPositionId, p.OpenTransactionCostBasis, p.OpenFunctionalCostBasis, true)));
            var tags = SecurityMasterLineageTags(Instruction.Security.SecurityId);
            tags["lotCorporateActionHash"] = OpenLotCorporateAction.Fingerprint(Instruction);
            var journal = new JournalEntry(id, at, "Reviewed advance refunding", lines,
                new(SecurityId: Instruction.Security.SecurityId, EffectiveDate: Instruction.EffectiveDate, IdempotencyKey: key, Tags: tags));
            var eventEvidence = BuildEvidence("corporate-event-" + sourceId.ToString("N"), 'e') with
            { EffectiveDate = Instruction.EffectiveDate, SubjectId = sourceId.ToString("D") };
            var evidence = OpenLotCorporateAction.Evidence(Instruction).Append(eventEvidence).ToArray();
            var economicEvent = new EconomicEventReferenceDto(sourceId,
                AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction), 1, Instruction.EffectiveDate, at,
                "SecurityMaster", "reviewed-refunding")
            {
                SecurityId = Instruction.Security.SecurityId,
                BookPositionId = Instruction.ExpectedLot.BookPositionId,
                RetainedEvidence = evidence,
                SourceContentHash = eventEvidence.ContentHashSha256
            };
            var posting = new AccountingPostingCommandDto(Guid.NewGuid(), BookId, Base.Period.PeriodId, Instruction.EffectiveDate, at, key,
                AccountingPostingIntentDto.Adjustment, sourceId, ExpectedVersion: Base.Period.Version,
                SourceEventType: AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction),
                ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "independent-controller-review",
                OperatorRationale: "Reviewed exact predecessor and successor lot allocation.", LedgerBookId: BookId)
            {
                Actor = "independent-controller",
                LotCorporateAction = Instruction,
                EconomicEvent = economicEvent,
                BookContext = BookContext,
                BookPositionId = Instruction.ExpectedLot.BookPositionId,
                ProjectionLineage = new(Guid.NewGuid(), null, "corporate-action", "1", "1", "Base", Instruction.EffectiveDate,
                    at, "SecurityMaster", "reviewed-refunding", economicEvent)
                { BookPositionId = Instruction.ExpectedLot.BookPositionId },
                RulePackReference = new("corporate-action", "1", "advance-refunding", "1"),
                Evidence = evidence.Select(e => new AccountingPostingEvidenceReferenceDto(e.EvidenceId, e.EvidenceUri,
                    AccountingPostingEvidenceKindDto.Source, e.SourceSystem, e.RetainedAtUtc, e.RetainedBy,
                    e.SubjectId, e.ContentHashSha256, SourceReference: e.SourceReference, Reviewer: e.ReviewedBy,
                    ReviewedAtUtc: e.ReviewedAtUtc, EffectiveDate: e.EffectiveDate, EvidenceVersion: e.EvidenceVersion,
                    ReviewStatus: e.ReviewStatus, SubjectType: e.SubjectType)).ToArray()
            };
            var write = new LedgerJournalEntryWrite(journal, BookId, Base.Period.PeriodId, SourceEventId: sourceId,
                LedgerBookId: BookId, PostingCommand: posting, AccountingBasis: BookContext.AccountingBasis,
                AccountingPolicyId: BookContext.AccountingPolicyId, AccountingPolicyVersion: BookContext.AccountingPolicyVersion);
            return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), BookId, write, sourceId, key, Base.Period.Version,
                AtomicTaxLotMutationKind.CorporateAction, evidence, corporateAction: Instruction);
        }

        public async Task AssertUnchangedAsync(AtomicTaxLotJournalCommand failed)
        {
            (await Store.GetAtomicTaxLotPostingAsync(failed.MutationBatchId)).Should().BeNull();
            (await Store.GetByPeriodAsync(Base.Period.PeriodId)).Should().HaveCount(InitialJournalCount);
            var original = (await Store.GetTaxLotsByIdsAsync(BookId, [Instruction.ExpectedLot.TaxLotRecordId])).Single().ToOpenLot();
            original.Should().BeEquivalentTo(Instruction.ExpectedLot);
            (await Store.GetTaxLotsByIdsAsync(BookId, Instruction.Successors.Select(s => s.TaxLotRecordId).ToArray())).Should().BeEmpty();
        }

        public ValueTask DisposeAsync() => Base.DisposeAsync();
    }
}
