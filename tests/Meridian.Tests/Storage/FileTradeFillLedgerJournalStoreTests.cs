using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Execution.Events;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// The file-backed trade-fill journal keeps paper fills on disk without Postgres: postings survive
/// a restart with every posting identity intact, retries never post twice, and a damaged journal
/// stops startup instead of being trusted.
/// </summary>
public sealed class FileTradeFillLedgerJournalStoreTests : IAsyncLifetime
{
    private const string Scope = "ledger-book/test/period/test/aggregate/test";
    private static readonly DateTimeOffset Start = new(2026, 3, 2, 14, 30, 0, TimeSpan.Zero);
    private static readonly Guid AggregateId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PeriodId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-file-trade-fill-" + Guid.NewGuid().ToString("N"));
    private readonly IChartOfAccounts _chart = StandardChartOfAccounts.Instance;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Post_ThenReopen_RebuildsRecordsAndLedger()
    {
        var first = Write(Buy(1_000m, "fill-1"), commandId: Guid.NewGuid(), sourceEventId: Guid.NewGuid());
        var second = Write(Buy(250m, "fill-2", Start.AddMinutes(5)));
        global::Meridian.Ledger.Ledger original;
        await using (var store = OpenStore())
        {
            var target = FileTradeFillLedger.CreatePostingTarget(store);
            (await target.PostAndConfirmAsync(first)).WasAppended.Should().BeTrue();
            (await target.PostAndConfirmAsync(second)).WasAppended.Should().BeTrue();
            original = await store.LoadLedgerAsync();
        }

        await using var reopened = OpenStore();
        var verification = await FileTradeFillLedger.VerifyBeforeHostStartupAsync(reopened);
        var records = await reopened.GetAllAsync();
        var reloaded = await reopened.LoadLedgerAsync();

        verification.IsIntact.Should().BeTrue();
        verification.EntryCount.Should().Be(2);
        records.Select(record => record.Entry.JournalEntryId)
            .Should().Equal(first.Entry.JournalEntryId, second.Entry.JournalEntryId);
        records.Select(record => record.GlobalSequence).Should().Equal(1L, 2L);
        records[0].CommandId.Should().Be(first.CommandId);
        records[0].SourceEventId.Should().Be(first.SourceEventId);
        records[0].AggregateId.Should().Be(AggregateId);
        records[0].PeriodId.Should().Be(PeriodId);
        records[0].AccountingPolicyId.Should().Be("execution-trade-fill");
        records[0].Entry.Metadata.IdempotencyKey.Should().Be("execution:test:fill-1");
        reloaded.TrialBalance().Should().BeEquivalentTo(original.TrialBalance());
        reloaded.GetBalance(_chart.Resolve(LedgerAccountRole.Cash)).Should().Be(-1_250m);
    }

    [Fact]
    public async Task LateFillWithEarlierTimestamp_KeepsPostingSequenceAfterRestart()
    {
        var later = Write(Buy(1_000m, "fill-later", Start.AddMinutes(2)));
        var lateArrival = Write(Buy(250m, "fill-late-arrival", Start.AddMinutes(1)));
        IReadOnlyList<LedgerJournalEntryRecord> beforeRestart;
        await using (var store = OpenStore())
        {
            var target = FileTradeFillLedger.CreatePostingTarget(store);
            await target.PostAndConfirmAsync(later);
            await target.PostAndConfirmAsync(lateArrival);
            beforeRestart = await store.GetAllAsync();
        }

        await using var reopened = OpenStore();
        var afterRestart = await reopened.GetAllAsync();

        beforeRestart.Select(record => record.Entry.JournalEntryId)
            .Should().Equal(later.Entry.JournalEntryId, lateArrival.Entry.JournalEntryId);
        afterRestart.Select(record => (record.Entry.JournalEntryId, record.GlobalSequence))
            .Should().Equal(beforeRestart.Select(record => (record.Entry.JournalEntryId, record.GlobalSequence)),
                "a record's sequence is its durable posting position, not its timestamp rank");
    }

    [Fact]
    public async Task RepostAfterRestart_ConfirmsRetainedJournalWithoutAppending()
    {
        var write = Write(Buy(1_000m, "fill-1"), commandId: Guid.NewGuid());
        await using (var store = OpenStore())
            await FileTradeFillLedger.CreatePostingTarget(store).PostAndConfirmAsync(write);

        await using var reopened = OpenStore();
        var confirmation = await FileTradeFillLedger.CreatePostingTarget(reopened).PostAndConfirmAsync(write);

        confirmation.WasAppended.Should().BeFalse();
        confirmation.RetainedRecord.Entry.JournalEntryId.Should().Be(write.Entry.JournalEntryId);
        (await reopened.GetAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task SameJournalWithDifferentEconomics_IsRejected()
    {
        var original = Buy(1_000m, "fill-1");
        var changed = Rebuild(original, 1_001m);
        await using var store = OpenStore();
        var target = FileTradeFillLedger.CreatePostingTarget(store);
        await target.PostAndConfirmAsync(Write(original));

        var repost = () => target.PostAndConfirmAsync(Write(changed));

        await repost.Should().ThrowAsync<LedgerValidationException>();
        (await store.GetAllAsync()).Should().ContainSingle()
            .Which.Entry.Lines.Sum(line => line.Debit).Should().Be(1_000m);
    }

    [Theory]
    [InlineData("idempotency")]
    [InlineData("command")]
    [InlineData("source")]
    public async Task DifferentJournalSharingAPostingIdentity_IsRejected(string sharedIdentity)
    {
        var commandId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var first = Write(Buy(1_000m, "fill-1"), commandId, sourceEventId);
        var second = sharedIdentity switch
        {
            "idempotency" => Write(Buy(500m, "fill-1")),
            "command" => Write(Buy(500m, "fill-2"), commandId: commandId),
            _ => Write(Buy(500m, "fill-2"), sourceEventId: sourceEventId)
        };
        await using var store = OpenStore();
        var target = FileTradeFillLedger.CreatePostingTarget(store);
        await target.PostAndConfirmAsync(first);

        var post = () => target.PostAndConfirmAsync(second);

        await post.Should().ThrowAsync<Exception>();
        (await store.GetAllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task EnvelopeWithoutJournal_IsSupersededWhenTheFillIsReplayed()
    {
        var write = Write(Buy(1_000m, "fill-1"));
        var directory = FileTradeFillLedgerJournalStore.DirectoryFor(_root, Scope);

        // Simulate a crash after the envelope became durable but before the journal append: the
        // envelope WAL holds a record for an entry the journal never received.
        await using (var envelopes = new Meridian.Storage.Archival.WriteAheadLog(Path.Combine(directory, "envelopes")))
        {
            await envelopes.InitializeAsync();
            await envelopes.AppendAsync(
                System.Text.Json.JsonSerializer.Serialize(
                    TradeFillJournalEnvelope.FromWrite(write with { CorrelationId = Guid.NewGuid() }, "stale-hash", Start),
                    FileTradeFillLedgerJsonContext.Default.TradeFillJournalEnvelope),
                FileTradeFillLedgerJournalStore.EnvelopeRecordType);
            await envelopes.FlushAsync();
        }

        await using (var store = OpenStore())
        {
            (await store.GetAllAsync()).Should().BeEmpty("an envelope without its entry is not a posting");
            (await FileTradeFillLedger.CreatePostingTarget(store).PostAndConfirmAsync(write)).WasAppended.Should().BeTrue();
        }

        await using var reopened = OpenStore();
        var record = (await reopened.GetAllAsync()).Should().ContainSingle().Subject;
        record.CorrelationId.Should().Be(write.CorrelationId, "the envelope written with the entry supersedes the orphan");
    }

    [Fact]
    public async Task MissingEnvelopes_FailStartupVerification()
    {
        await using (var store = OpenStore())
            await FileTradeFillLedger.CreatePostingTarget(store).PostAndConfirmAsync(Write(Buy(1_000m, "fill-1")));
        Directory.Delete(Path.Combine(FileTradeFillLedgerJournalStore.DirectoryFor(_root, Scope), "envelopes"), recursive: true);

        await using var reopened = OpenStore();
        var verify = () => FileTradeFillLedger.VerifyBeforeHostStartupAsync(reopened);

        await verify.Should().ThrowAsync<InvalidDataException>().WithMessage("*has no posting envelope*");
    }

    [Fact]
    public async Task TamperedJournal_FailsStartupVerification()
    {
        await using (var store = OpenStore())
            await FileTradeFillLedger.CreatePostingTarget(store).PostAndConfirmAsync(Write(Buy(1_000m, "fill-1")));
        var journalDirectory = Path.Combine(FileTradeFillLedgerJournalStore.DirectoryFor(_root, Scope), "journal");
        foreach (var file in Directory.GetFiles(journalDirectory, "*.wal"))
            File.WriteAllText(file, File.ReadAllText(file).Replace("1000", "9000", StringComparison.Ordinal));

        await using var reopened = OpenStore();
        var verify = () => FileTradeFillLedger.VerifyBeforeHostStartupAsync(reopened);

        await verify.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task PeriodsBooksAndAdjustmentApprovals_RemainPostgresOnly()
    {
        await using var store = OpenStore();
        ILedgerJournalStore journals = store;

        await journals.Invoking(s => s.GetPeriodAsync(PeriodId)).Should().ThrowAsync<NotSupportedException>();
        await journals.Invoking(s => s.GetLedgerBookAsync(Guid.NewGuid())).Should().ThrowAsync<NotSupportedException>();
        var adjustment = Write(Buy(1_000m, "fill-1")) with
        {
            AdjustmentApproval = new LedgerAdjustmentApprovalMetadataDto(
                "approval-1", LedgerAdjustmentApprovalStatusDto.Approved, "reviewer", Start, "correction")
        };
        await journals.Invoking(s => s.AppendAsync(adjustment)).Should().ThrowAsync<NotSupportedException>();
    }

    private FileTradeFillLedgerJournalStore OpenStore()
        => new(FileTradeFillLedgerJournalStore.DirectoryFor(_root, Scope), Scope, LedgerPolicy.Trading());

    private static LedgerJournalEntryWrite Write(JournalEntry entry, Guid? commandId = null, Guid? sourceEventId = null)
        => new(entry, AggregateId, PeriodId,
            CommandId: commandId,
            CorrelationId: Guid.NewGuid(),
            AccountingPolicyId: "execution-trade-fill",
            AccountingPolicyVersion: "1",
            SourceEventId: sourceEventId);

    private JournalEntry Buy(decimal amount, string fillKey, DateTimeOffset? at = null)
    {
        var timestamp = at ?? Start;
        var journalId = Guid.NewGuid();
        return new JournalEntry(journalId, timestamp, "Buy AAPL", [
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, _chart.Resolve(LedgerAccountRole.Securities, symbol: "AAPL"), amount, 0m,
                "Buy AAPL", currency: new LedgerEntryCurrency("USD", "USD", amount, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), journalId, timestamp, _chart.Resolve(LedgerAccountRole.Cash), 0m, amount,
                "Buy AAPL", currency: new LedgerEntryCurrency("USD", "USD", 0m, amount, 1m))
        ], new JournalEntryMetadata(ActivityType: "trade-fill", Symbol: "AAPL", IdempotencyKey: "execution:test:" + fillKey));
    }

    private static JournalEntry Rebuild(JournalEntry entry, decimal amount)
        => new(entry.JournalEntryId, entry.Timestamp, entry.Description,
            entry.Lines.Select(line => new LedgerEntry(line.EntryId, line.JournalEntryId, line.Timestamp, line.Account,
                line.Debit == 0m ? 0m : amount, line.Credit == 0m ? 0m : amount, line.Description,
                currency: new LedgerEntryCurrency("USD", "USD", line.Debit == 0m ? 0m : amount, line.Credit == 0m ? 0m : amount, 1m))).ToArray(),
            entry.Metadata);
}
