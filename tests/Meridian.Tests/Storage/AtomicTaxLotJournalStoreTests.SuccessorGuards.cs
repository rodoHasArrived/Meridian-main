using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_DistinctConcurrentCommands_CommitOneAndRejectTheStaleLoser()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var first = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var second = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        first.SourceEventId.Should().NotBe(second.SourceEventId);
        first.MutationBatchId.Should().NotBe(second.MutationBatchId);

        async Task<bool> PostAsync(AtomicTaxLotJournalCommand command)
        {
            try
            { await fixture.Restart().AppendAssetPostingAsync(command); return true; }
            catch (LedgerValidationException) { return false; }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
            { return false; }
        }

        var outcomes = await Task.WhenAll(PostAsync(first), PostAsync(second));

        outcomes.Count(success => success).Should().Be(1);
        var winner = outcomes[0] ? first : second;
        var loser = outcomes[0] ? second : first;
        var retry = () => fixture.Restart().AppendAssetPostingAsync(loser);
        await retry.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(loser.MutationBatchId)).Should().BeNull();
        (await fixture.Restart().GetTaxLotsByIdsAsync(fixture.BookId,
            loser.CorporateAction!.Successors.Select(target => target.Lot.TaxLotRecordId).ToArray())).Should().BeEmpty();
        var committed = (await fixture.Restart().GetAtomicTaxLotPostingAsync(winner.MutationBatchId))!;
        committed.Mutations.Should().HaveCount(3);
        var predecessor = committed.Mutations.Single(mutation => mutation.LotBefore is not null).LotAfter;
        predecessor.OpenQuantity.Should().Be(0m);
        predecessor.Version.Should().Be(winner.CorporateAction!.ExpectedLot.Version + 1);
        (await fixture.Restart().GetByPeriodAsync(fixture.Period.PeriodId)).Should().ContainSingle();
        (await fixture.Restart().AppendAssetPostingAsync(winner)).IsExactReplay.Should().BeTrue();
        (await fixture.Restart().GetOpenLotSuccessorHistoryAsync(fixture.BookId,
            [committed.Journal.Entry.JournalEntryId])).Should().ContainSingle();
        await fixture.Restart().VerifyLedgerEventAuditAsync();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_SecondNaturalKeyCollision_RollsBackJournalEveryLotEvidenceAndAudit()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var valid = await SuccessorCommandAsync(fixture, advanceRefunding: true);
        var instruction = valid.CorporateAction!;
        var ordered = instruction.Successors.OrderBy(target => target.Lot.TaxLotRecordId).ToArray();
        // The target IDs and reference scopes are distinct. The first insertion succeeds, then
        // the second collides on the durable book/account/lot-label key in the same transaction.
        var collision = ordered[1] with { Lot = ordered[1].Lot with { LotId = ordered[0].Lot.LotId } };
        instruction = instruction with
        {
            Successors = instruction.Successors.Select(target =>
                target.Lot.TaxLotRecordId == collision.Lot.TaxLotRecordId ? collision : target).ToArray()
        };
        var command = BindSuccessorGuardInstruction(valid, instruction);
        var auditBefore = await fixture.Restart().VerifyLedgerEventAuditAsync();
        var post = () => fixture.Store.AppendAssetPostingAsync(command);

        await post.Should().ThrowAsync<PostgresException>()
            .Where(exception => exception.SqlState == PostgresErrorCodes.UniqueViolation);

        await AssertSuccessorUnchangedAsync(fixture, command);
        (await fixture.Restart().VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore.ChainedEvents);
        (await fixture.Restart().GetOpenLotSuccessorHistoryAsync(fixture.BookId,
            [command.Journal.Entry.JournalEntryId])).Should().BeEmpty();
        var posted = await fixture.Restart().AppendAssetPostingAsync(valid);
        posted.IsExactReplay.Should().BeFalse();
        posted.Mutations.Should().HaveCount(3);
        (await fixture.Restart().AppendAssetPostingAsync(valid)).IsExactReplay.Should().BeTrue();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_TypeAliasesUntypedCommandsAndMissingApproval_LeaveNoWrites()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var posting = command.Journal.PostingCommand!;
        var aliasEvent = posting.EconomicEvent! with { EventType = "CorporateAction" };
        foreach (var invalid in new[]
        {
            posting with
            {
                SourceEventType = "CorporateAction", EconomicEvent = aliasEvent,
                ProjectionLineage = posting.ProjectionLineage! with { TriggerEvent = aliasEvent }
            },
            posting with
            {
                SourceEventType = null, EconomicEvent = null, ProjectionLineage = null,
                BookContext = null, BookPositionId = null, RulePackReference = null, Evidence = []
            },
            posting with { ApprovalState = AccountingPostingApprovalStateDto.NotRequired },
            posting with { ApprovalId = null }
        })
        {
            var rejected = (command with
            { Journal = command.Journal with { PostingCommand = invalid } }).WithComputedFingerprint();
            var post = () => fixture.Store.AppendAssetPostingAsync(rejected);
            await post.Should().ThrowAsync<LedgerValidationException>();
            await AssertSuccessorUnchangedAsync(fixture, rejected);
        }
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_CorrectionMetadataCannotAuthorizeAnUnsupportedCorporateActionCorrection()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var journalId = fixture.Lot.SourceJournalEntryId!.Value;
        var posting = command.Journal.PostingCommand!;
        var corrections = new[]
        {
            command with { CorrectsMutationBatchId = fixture.Lot.LastMutationBatchId },
            command with { Journal = command.Journal with { SourceJournalEntryId = journalId } },
            command with { Journal = command.Journal with { PostingCommand = posting with { SourceJournalEntryId = journalId } } },
            command with { Journal = command.Journal with { PostingCommand = posting with
                { Intent = AccountingPostingIntentDto.Reversal, SourceJournalEntryId = journalId } } },
            command with { Journal = command.Journal with { PostingCommand = posting with
                { Intent = AccountingPostingIntentDto.Rebook, SourceJournalEntryId = journalId } } },
            command with { Journal = command.Journal with { PostingKind = LedgerPostingKindDto.ClosingEntry,
                PostingCommand = posting with { Intent = AccountingPostingIntentDto.Originating } } }
        };
        foreach (var correction in corrections)
        {
            var rejected = correction.WithComputedFingerprint();
            var post = () => fixture.Store.AppendAssetPostingAsync(rejected);
            await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*correction*");
            await AssertSuccessorUnchangedAsync(fixture, rejected);
        }
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CorporateActionSuccessors_JournalDimensionTamperCannotChangeTheLockedPositionScope()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true);
        var command = await SuccessorCommandAsync(fixture, advanceRefunding: false);
        var entry = command.Journal.Entry;
        var target = command.CorporateAction!.Successors.Single().Lot;
        var lines = entry.Lines.Select(line => line.Dimensions?.PositionId == target.BookPositionId
            ? new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account, line.Debit, line.Credit,
                line.Description, line.Dimensions! with { SleeveId = "unreviewed-sleeve" }, line.Currency)
            : line).ToArray();
        var tampered = (command with
        {
            Journal = command.Journal with
            { Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata) }
        }).WithComputedFingerprint();
        var auditBefore = await fixture.Restart().VerifyLedgerEventAuditAsync();
        var post = () => fixture.Store.AppendAssetPostingAsync(tampered);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*dimension*");

        await AssertSuccessorUnchangedAsync(fixture, tampered);
        (await fixture.Restart().VerifyLedgerEventAuditAsync()).ChainedEvents.Should().Be(auditBefore.ChainedEvents);
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeFalse();
    }

    private static AtomicTaxLotJournalCommand BindSuccessorGuardInstruction(
        AtomicTaxLotJournalCommand command, OpenLotSuccessorInstructionDto instruction)
    {
        var entry = command.Journal.Entry;
        var tags = new Dictionary<string, string>(entry.Metadata.Tags!)
        { [OpenLotSuccessors.JournalFingerprintTag] = OpenLotSuccessors.Fingerprint(instruction) };
        return (command with
        {
            CorporateAction = instruction,
            Journal = command.Journal with
            {
                Entry = new(entry.JournalEntryId, entry.Timestamp, entry.Description, entry.Lines, entry.Metadata with { Tags = tags }),
                PostingCommand = command.Journal.PostingCommand! with { LotCorporateAction = instruction }
            }
        }).WithComputedFingerprint();
    }
}
