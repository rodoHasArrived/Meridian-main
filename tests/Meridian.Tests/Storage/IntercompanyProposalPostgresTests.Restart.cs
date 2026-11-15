using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.FinancialOperations.Intercompany;

namespace Meridian.Tests.Storage;

/// <summary>
/// A delivery worker or posting client dies between committing work and recording its receipt.
/// Guards against the retained source being lost behind a dead lease, a superseded worker
/// overwriting the restarted worker's outcome, and a replayed source commit duplicating either
/// the journal or its counterpart proposal.
/// </summary>
public sealed partial class IntercompanyProposalPostgresTests
{
    [LedgerDatabaseFact]
    public async Task ExpiredLease_IsReclaimedAfterWorkerRestart_AndSupersededClaimCannotCompleteOrFail()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var fixture = await SetupAsync(database);
        await fixture.Journals.AppendAsync(Write(fixture.Period, "crashed-delivery"), ct);
        var crashed = Assert.Single(await fixture.Proposals.ClaimAsync("crashed-worker", 1, TimeSpan.FromSeconds(3), ct));
        var proposalId = crashed.Source.ProposalId;
        // The crashed worker's lease still fences the source from a second concurrent delivery.
        Assert.Empty(await fixture.Proposals.ClaimAsync("concurrent-worker", 1, TimeSpan.FromMinutes(1), ct));

        // A restarted process holds no in-memory state; only the durable row carries the work forward.
        var restarted = new PostgresIntercompanyProposalStore(database.Options);
        IReadOnlyList<IntercompanyProposalDto> reclaimed = [];
        while (reclaimed.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            reclaimed = await restarted.ClaimAsync("restarted-worker", 1, TimeSpan.FromMinutes(1), ct);
        }

        var current = Assert.Single(reclaimed);
        Assert.Equal(proposalId, current.Source.ProposalId);
        Assert.Equal(crashed.SourceDigest, current.SourceDigest);
        Assert.NotEqual(crashed.ClaimToken, current.ClaimToken);
        Assert.Equal(crashed.Attempts + 1, current.Attempts);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.CompleteAsync("tenant-a", proposalId,
            crashed.ClaimToken!, IntercompanyProposalTestData.BookB, Guid.NewGuid(), ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Proposals.FailAsync("tenant-a", proposalId,
            crashed.ClaimToken!, "Late failure from the superseded worker", ct));
        await restarted.CompleteAsync("tenant-a", proposalId, current.ClaimToken!, IntercompanyProposalTestData.BookB, proposalId, ct);

        var retained = Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, ct));
        Assert.Equal(IntercompanyProposalStatusDto.Drafted, retained.Status);
        Assert.Equal(IntercompanyProposalTestData.BookB, retained.TargetBookId);
        Assert.Equal(proposalId, retained.TargetJournalId);
        Assert.Null(retained.LastError);
        Assert.Equal(crashed.SourceDigest, retained.SourceDigest);
        Assert.Empty(await restarted.ClaimAsync("late-worker", 10, TimeSpan.FromMinutes(1), ct));
    }

    [LedgerDatabaseFact]
    public async Task ExactSourceReplayAfterRestartAndConcurrentRetry_RetainsOneJournalAndOneProposal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var fixture = await SetupAsync(database);
        var write = Write(fixture.Period, "replayed-source");
        await fixture.Journals.AppendAsync(write, ct);
        var original = Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, ct));

        // The client never saw the commit acknowledgement and replays the identical command after restart.
        var restarted = new PostgresLedgerJournalStore(database.Options, new FixtureTenant(),
            postingAuthorities: () => [new FixtureAuthority("entity-lifecycle"), new FixtureAuthority("intercompany")],
            intercompanyAgreementAuthority: () => new FixtureNativeAuthority());
        await restarted.AppendAsync(write, ct);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Record.ExceptionAsync(() => restarted.AppendAsync(write, ct))));

        Assert.Equal(write.Entry.JournalEntryId, Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.Period.PeriodId, ct)).Entry.JournalEntryId);
        var retained = Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, ct));
        Assert.Equal(original.Source.ProposalId, retained.Source.ProposalId);
        Assert.Equal(original.SourceDigest, retained.SourceDigest);
        Assert.Equal(original.RecordedAt, retained.RecordedAt);
        Assert.Equal(IntercompanyProposalStatusDto.Pending, retained.Status);
        Assert.Equal(0, retained.Attempts);
    }

    [LedgerDatabaseFact]
    public async Task ConcurrentFirstCommitOfSameSource_RetainsOneJournalAndOneProposal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var fixture = await SetupAsync(database);
        var write = Write(fixture.Period, "raced-source");

        // A timed-out request and its retry reach the ledger together before either has committed.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Record.ExceptionAsync(() => fixture.Journals.AppendAsync(write, ct))));

        Assert.Contains(outcomes, error => error is null);
        Assert.Equal(write.Entry.JournalEntryId, Assert.Single(await fixture.Journals.GetByPeriodAsync(fixture.Period.PeriodId, ct)).Entry.JournalEntryId);
        var retained = Assert.Single(await fixture.Proposals.ListAsync("tenant-a", IntercompanyProposalTestData.BookA, ct));
        Assert.Equal(write.Entry.JournalEntryId, retained.Source.SourceJournalId);
        Assert.Equal("raced-source", retained.Source.TransactionId);
        Assert.Equal(IntercompanyProposalStatusDto.Pending, retained.Status);
    }
}
