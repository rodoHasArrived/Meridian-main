using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AccountingPostingCandidateServiceTests
{
    [Fact]
    public async Task PostCandidateAsync_RetryRetainsOriginalPostingActorInsteadOfLaterCaller()
    {
        var (_, store, service, request) = await CreateActorReplayHarnessAsync();
        var first = await service.PostCandidateAsync(request);
        var second = await service.PostCandidateAsync(request with { Actor = "later-posting-controller" });
        second.WasReplay.Should().BeTrue();
        second.PostedJournal.JournalEntryId.Should().Be(first.PostedJournal.JournalEntryId);
        second.Candidate.PostingCommand!.Actor.Should().Be("reviewer@meridian.local");
        store.Appended.Should().ContainSingle();
    }

    [Fact]
    public async Task PostCandidateAsync_LegacyUnattributedJournalNeverAcquiresRetryActor()
    {
        var (candidates, store, service, request) = await CreateActorReplayHarnessAsync();
        await service.PostCandidateAsync(request);
        var original = store.Appended.Single();
        // Reproduce the pre-actor serialized command shape and normalized metadata. Existing
        // approval evidence remains present; it must never be borrowed as posting identity.
        var legacyTags = original.Entry.Metadata.Tags!
            .Where(pair => pair.Key is not ("postingActor" or "postingActorAttribution" or "postingCommandFingerprint"))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var legacyWrite = original with
        {
            PostingCommand = original.PostingCommand! with { Actor = null },
            Entry = new JournalEntry(original.Entry.JournalEntryId, original.Entry.Timestamp,
                original.Entry.Description, original.Entry.Lines, original.Entry.Metadata with { Tags = legacyTags })
        };
        var legacyStore = new RecordingLedgerJournalStore(
            (await store.GetLedgerBookAsync(original.LedgerBookId!.Value))!,
            (await store.GetPeriodAsync(original.PeriodId))!);
        await legacyStore.AppendAsync(legacyWrite);
        var legacyService = new AccountingPostingCandidatePostService(candidates, legacyStore);

        var replay = await legacyService.PostCandidateAsync(request with { Actor = "later-posting-controller" });

        replay.WasReplay.Should().BeTrue();
        replay.Candidate.PostingCommand!.Actor.Should().BeNull();
        legacyStore.Appended.Should().ContainSingle();
        legacyStore.Appended[0].Entry.Metadata.Tags.Should().NotContainKey("postingActor");
        legacyStore.Appended[0].Entry.Metadata.Tags.Should().NotContainKey("postingActorAttribution");
    }

    private static async Task<(AccountingPostingCandidateService Candidates, RecordingLedgerJournalStore Store,
        AccountingPostingCandidatePostService Service, PostPostingRuleJournalCandidateRequestDto Request)> CreateActorReplayHarnessAsync()
    {
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var candidates = await CreateSeededCandidateServiceAsync(ledgerBookId: bookId);
        var store = new RecordingLedgerJournalStore(BuildLedgerBook(bookId, AccountingBasisKindDto.Gaap), BuildPeriod(periodId, bookId));
        var request = new PostPostingRuleJournalCandidateRequestDto(
            BuildCandidateRequest(bookId, periodId, sourceEventId, AccountingBasisKindDto.Gaap, "gaap-accrual-v1"),
            "reviewer@meridian.local", "approval-generated-interest-202605",
            EvidenceLinks: [ApprovalEvidence("fund-alpha", bookId, sourceEventId)]);
        return (candidates, store, new AccountingPostingCandidatePostService(candidates, store), request);
    }
}
