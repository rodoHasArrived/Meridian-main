using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class IntercompanyAgreementPostingPostgresTests
{
    [LedgerDatabaseFact]
    public async Task ExplicitReference_RequiresAuthorityAndKeepsLeaseThroughCommit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var write = await PrepareAsync(database, ct);
        var missing = () => database.JournalStore.AppendAsync(write, ct);
        await missing.Should().ThrowAsync<LedgerValidationException>().WithMessage("*validation is unavailable*");

        var authority = new RecordingAuthority(async () =>
            (await database.JournalStore.QueryAsync(new(JournalEntryId: write.Entry.JournalEntryId), ct)).Should().ContainSingle());
        var store = new PostgresLedgerJournalStore(database.Options,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities, intercompanyAgreementAuthority: () => authority);
        await store.AppendAsync(write, ct);
        authority.Disposed.Should().BeTrue();
        authority.Request!.TenantId.Should().Be("tenant-agreement");
        authority.Request.Lines.Single().AgreementVersion.Should().Be(1);
        authority.Request.IsVerifiedSourceReversal.Should().BeFalse();
    }

    [LedgerDatabaseFact]
    public async Task ExactCommittedReplay_RecoversWithoutCurrentAgreementAuthority()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var write = await PrepareAsync(database, ct);
        var first = new PostgresLedgerJournalStore(database.Options,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities,
            intercompanyAgreementAuthority: () => new RecordingAuthority());
        await first.AppendAsync(write, ct);

        // No registry provider is available after the commit. Recovery validates the exact retained
        // journal and its audit chain rather than attempting a new approval under today's terms.
        await database.JournalStore.AppendAsync(write, ct);
        (await database.JournalStore.QueryAsync(new(JournalEntryId: write.Entry.JournalEntryId), ct)).Should().ContainSingle();
        var changedLine = write.Entry.Lines[0];
        var changed = write with
        {
            Entry = new(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
            [new(changedLine.EntryId, changedLine.JournalEntryId, changedLine.Timestamp, changedLine.Account,
                changedLine.Debit, changedLine.Credit, changedLine.Description,
                changedLine.Dimensions! with { IntercompanyAgreementVersion = 2 }), write.Entry.Lines[1]], write.Entry.Metadata)
        };
        var conflict = () => database.JournalStore.AppendAsync(changed, ct);
        await conflict.Should().ThrowAsync<LedgerValidationException>().WithMessage("*identity collision*");
    }

    [LedgerDatabaseFact]
    public async Task SourceVerifiedInverse_KeepsPinnedRevisionButAlteredDimensionsCannotClaimExemption()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var write = await PrepareAsync(database, ct);
        var authority = new RecordingAuthority();
        var store = new PostgresLedgerJournalStore(database.Options,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities, intercompanyAgreementAuthority: () => authority);
        await store.AppendAsync(write, ct);
        var forged = IntercompanyAgreementPostingGuardTests.Reverse(write, "cost-center");
        var forgeAppend = () => store.AppendAsync(forged, ct);
        await forgeAppend.Should().ThrowAsync<LedgerValidationException>().WithMessage("*exact inverse*");
        var reversal = IntercompanyAgreementPostingGuardTests.Reverse(write);
        await store.AppendAsync(reversal, ct);
        authority.Request!.IsVerifiedSourceReversal.Should().BeTrue();
        authority.Request.Lines.Single().AgreementVersion.Should().Be(1);
        var duplicate = () => store.AppendAsync(IntercompanyAgreementPostingGuardTests.Reverse(write), ct);
        await duplicate.Should().ThrowAsync<LedgerValidationException>().WithMessage("*already has a committed reversal*");
    }

    [LedgerDatabaseFact]
    public async Task AuthorityRefusal_RollsBackAllJournalAndAuditChanges()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var write = await PrepareAsync(database, ct);
        var before = await database.JournalStore.VerifyLedgerEventAuditAsync(ct);
        var store = new PostgresLedgerJournalStore(database.Options,
            postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities,
            intercompanyAgreementAuthority: () => new RefusingAuthority());
        var append = () => store.AppendAsync(write, ct);
        await append.Should().ThrowAsync<LedgerValidationException>().WithMessage("*approved revision changed*");
        (await database.JournalStore.GetByPeriodAsync(write.PeriodId, ct)).Should().BeEmpty();
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(before.ChainedEvents);
    }

    private static async Task<LedgerJournalEntryWrite> PrepareAsync(LedgerPostgresTestDatabase database, CancellationToken ct)
    {
        var book = IntercompanyAgreementPostingGuardTests.Book();
        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(book.FundProfileId, "tenant-agreement", ct: ct);
        await database.JournalStore.SaveLedgerBookAsync(book, ct);
        var period = await database.JournalStore.SavePeriodAsync(new(Guid.NewGuid(), book.LedgerBookId, 2026, 5,
            "Agreement period", new(2026, 5, 1), new(2026, 5, 31), "Open", DateTimeOffset.UtcNow, null, 0), 0, ct: ct);
        return IntercompanyAgreementPostingGuardTests.Write(IntercompanyAgreementPostingGuardTests.Dimensions()) with
        { LedgerBookId = book.LedgerBookId, PeriodId = period.PeriodId };
    }

    private sealed class RecordingAuthority(Func<Task>? onDispose = null) : IIntercompanyAgreementPostingAuthority, IAsyncDisposable
    {
        public IntercompanyAgreementPostingRequest? Request { get; private set; }
        public bool Disposed { get; private set; }
        public Task<IAsyncDisposable> AcquireValidatedLeaseAsync(IntercompanyAgreementPostingRequest request, CancellationToken ct = default)
        {
            Request = request;
            Disposed = false;
            return Task.FromResult<IAsyncDisposable>(this);
        }
        public async ValueTask DisposeAsync()
        {
            if (onDispose is not null)
                await onDispose();
            Disposed = true;
        }
    }

    private sealed class RefusingAuthority : IIntercompanyAgreementPostingAuthority
    {
        public Task<IAsyncDisposable> AcquireValidatedLeaseAsync(IntercompanyAgreementPostingRequest request, CancellationToken ct = default)
            => throw new InvalidOperationException("The approved revision changed after review.");
    }
}
