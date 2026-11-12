using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Meridian.Tests.Execution;

public sealed class PaperFinancingPostingTests
{
    private static readonly TradeFillLedgerPostingContext Scope = new("financing-tests",
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"));

    private static PaperFinancingCharge Charge(string session = "paper-1", string account = "default") =>
        new(session, account, new DateOnly(2026, 11, 12), 12.50m, "broker-interest-20261112",
            "Broker-assessed daily margin financing", "test-operator");

    [Fact]
    public async Task DuplicateAndRestart_RetainOneBalancedJournalAndRejectChangedEconomics()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var service = new PaperFinancingPostingService(root, Scope, target))
        {
            (await service.PostAsync(Charge())).Should().BeTrue();
            (await service.PostAsync(Charge())).Should().BeFalse();
            await service.Invoking(s => s.PostAsync(Charge() with { Amount = 20m }))
                .Should().ThrowAsync<InvalidDataException>();
        }
        await using var recovered = new PaperFinancingPostingService(root, Scope, target);
        (await recovered.LoadAndRecoverAsync("paper-1")).Should().Equal(Charge());
        (await recovered.PostAsync(Charge())).Should().BeFalse();
        var entry = target.Records.Values.Should().ContainSingle().Which.Entry;
        entry.Lines.Sum(static line => line.Debit - line.Credit).Should().Be(0m);
        entry.Lines.Single(line => line.Account == LedgerAccounts.Cash).Credit.Should().Be(12.50m);
        AccountingPostingCommandValidator.ReadRetainedPostingActor(entry.Metadata).Should().Be("test-operator");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureBeforeOrAfterAuthoritativeAppend_ReplaysWithoutDoublePosting(bool afterAppend)
    {
        var root = TempRoot();
        var target = new RetainedTarget { FailBeforeAppend = !afterAppend, FailAfterAppend = afterAppend };
        await using (var interrupted = new PaperFinancingPostingService(root, Scope, target))
            await interrupted.Invoking(s => s.PostAsync(Charge())).Should().ThrowAsync<IOException>();
        target.FailBeforeAppend = target.FailAfterAppend = false;
        await using var recovered = new PaperFinancingPostingService(root, Scope, target);
        (await recovered.LoadSourcesAsync("paper-1")).Should().Equal(Charge());
        target.AppendCount.Should().Be(afterAppend ? 1 : 0,
            "reading independent source evidence cannot itself post a journal");
        (await recovered.LoadAndRecoverAsync("paper-1")).Should().Equal(Charge());
        target.Records.Should().ContainSingle();
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task SingleWriterLease_RejectsCompetingWriterAndReleasesAfterDispose()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var first = new PaperFinancingPostingService(root, Scope, target))
        {
            Action competing = () => _ = new PaperFinancingPostingService(root, Scope, target);
            competing.Should().Throw<IOException>();
            await first.PostAsync(Charge());
        }
        await using var successor = new PaperFinancingPostingService(root, Scope, target);
        (await successor.LoadAndRecoverAsync("paper-1")).Should().ContainSingle();
    }

    [Fact]
    public async Task LostInitializedSnapshot_FailsClosedInsteadOfDroppingPaidFinancing()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var first = new PaperFinancingPostingService(root, Scope, target))
            await first.PostAsync(Charge());
        var scopeDirectory = new TradeFillPostingStoreOptions(root, Scope).ScopeDirectory;
        File.Delete(Path.Combine(scopeDirectory, "financing", "sources.snapshot.json"));
        await using var recovered = new PaperFinancingPostingService(root, Scope, target);
        await recovered.Invoking(service => service.LoadAndRecoverAsync("paper-1"))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*missing*retained snapshot*");
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task EmptyInitializedStore_RestartsAndAcceptsItsFirstCharge()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var empty = new PaperFinancingPostingService(root, Scope, target))
            (await empty.LoadSourcesAsync("paper-1")).Should().BeEmpty();
        await using var recovered = new PaperFinancingPostingService(root, Scope, target);
        (await recovered.PostAsync(Charge())).Should().BeTrue();
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task FirstAcceptedWalWithoutCheckpointOrMarker_ReplaysBeforeInitializationIsPublished()
    {
        var root = TempRoot();
        var scopeDirectory = new TradeFillPostingStoreOptions(root, Scope).ScopeDirectory;
        await using (var wal = new WriteAheadLog(Path.Combine(scopeDirectory, "financing", "wal"),
            new WalOptions { SyncMode = WalSyncMode.EveryWrite, CorruptionMode = WalCorruptionMode.Halt }))
        {
            await wal.InitializeAsync();
            var payload = new PaperFinancingWalPayload(Scope, new PaperFinancingPostingState(Charge(), Posted: false));
            await wal.AppendAsync(JsonSerializer.Serialize(payload, ExecutionJsonContext.Default.PaperFinancingWalPayload),
                "FinancingAccepted");
        }
        var target = new RetainedTarget();
        await using var recovered = new PaperFinancingPostingService(root, Scope, target);
        (await recovered.LoadAndRecoverAsync("paper-1")).Should().Equal(Charge());
        File.Exists(Path.Combine(scopeDirectory, "financing", "initialized.json")).Should().BeTrue();
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task ChangedBookUnderSameScopeLabel_FailsClosed()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var first = new PaperFinancingPostingService(root, Scope, target))
            await first.PostAsync(Charge());
        await using var changed = new PaperFinancingPostingService(root, Scope with { LedgerBookId = Guid.NewGuid() }, target);
        await changed.Invoking(s => s.LoadAndRecoverAsync("paper-1")).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task AccountAndSourceIdentity_RemainDistinctAndScoped()
    {
        var target = new RetainedTarget();
        await using var service = new PaperFinancingPostingService(TempRoot(), Scope, target);
        await service.PostAsync(Charge(account: "account-a"));
        await service.PostAsync(Charge(account: "account-b"));
        await service.PostAsync(Charge(account: "account-a") with { SourceReference = "second-assessment" });
        target.Records.Should().HaveCount(3);
        target.Records.Values.SelectMany(record => record.Entry.Lines)
            .Where(line => line.Account.FinancialAccountId == "account-b")
            .Should().HaveCount(2);
    }

    [Fact]
    public async Task SessionCashAndBooks_RebuildAcrossRestart_AndLaterProjectionRebuildKeepsCharge()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        string id;
        await using (var financing = new PaperFinancingPostingService(root, Scope, target))
        await using (var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing))
        {
            id = (await sessions.CreateSessionAsync(new("strategy-1", "Paper", 1_000m))).SessionId;
            (await sessions.RecordFinancingAsync(Charge(id))).Should().BeTrue();
            (await sessions.RecordFinancingAsync(Charge(id))).Should().BeFalse();
            sessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
            sessions.GetLedger(id)!.GetBalance(LedgerAccounts.MarginInterestExpense).Should().Be(12.50m);
            await sessions.RecordFillAsync(id, new ExecutionReport
            {
                OrderId = "after-financing",
                Symbol = "AAPL",
                ReportType = ExecutionReportType.Fill,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = 1m,
                FilledQuantity = 1m,
                FillPrice = 100m,
                Timestamp = Charge(id).OccurredAt.AddHours(12)
            });
            sessions.GetSession(id)!.Portfolio!.Cash.Should().Be(887.50m);
            sessions.GetLedger(id)!.GetBalance(LedgerAccounts.MarginInterestExpense).Should().Be(12.50m);
        }
        await using var recoveredFinancing = new PaperFinancingPostingService(root, Scope, target);
        await using var recoveredSessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        recoveredSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(887.50m);
        recoveredSessions.GetLedger(id)!.GetBalance(LedgerAccounts.MarginInterestExpense).Should().Be(12.50m);
        (await recoveredSessions.RecordFinancingAsync(Charge(id))).Should().BeFalse();
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task BoundSessionWithoutAccountingOwner_FailsInsteadOfLosingFinancing()
    {
        var root = TempRoot();
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        await using (var financing = new PaperFinancingPostingService(root, Scope, new RetainedTarget()))
        await using (var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing))
            await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m));
        await using var unbound = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance, store);
        await unbound.Invoking(service => service.InitialiseAsync()).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task PendingAssessment_BlocksCloseUntilExactChargeIsRecovered()
    {
        var root = TempRoot();
        var target = new RetainedTarget { FailBeforeAppend = true };
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        await using var financing = new PaperFinancingPostingService(root, Scope, target);
        await using var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing);
        var id = (await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m))).SessionId;
        await sessions.Invoking(service => service.RecordFinancingAsync(Charge(id))).Should().ThrowAsync<IOException>();
        await sessions.Invoking(service => service.CloseSessionAsync(id)).Should().ThrowAsync<InvalidOperationException>();
        await sessions.Invoking(service => service.RecordFinancingAsync(Charge(id) with { SourceReference = "another-source" }))
            .Should().ThrowAsync<InvalidOperationException>();
        target.FailBeforeAppend = false;
        await sessions.RecordFinancingAsync(Charge(id));
        sessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
        (await sessions.CloseSessionAsync(id)).Should().BeTrue();
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "meridian-financing-tests", Guid.NewGuid().ToString("N"));

    private sealed class RetainedTarget : ITradeFillLedgerPostingTarget
    {
        public Dictionary<Guid, LedgerJournalEntryRecord> Records { get; } = [];
        public bool FailBeforeAppend { get; set; }
        public bool FailAfterAppend { get; set; }
        public int AppendCount { get; private set; }

        public Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(LedgerJournalEntryWrite write, CancellationToken ct = default)
        {
            if (FailBeforeAppend)
                throw new IOException("Synthetic authoritative store outage.");
            var isNew = !Records.TryGetValue(write.Entry.JournalEntryId, out var retained);
            if (isNew)
            {
                retained = new(write.Entry, write.AggregateId, write.PeriodId, write.CommandId, write.CorrelationId,
                    ++AppendCount, DateTimeOffset.UtcNow, write.AccountingBasis, write.AccountingPolicyId,
                    write.AccountingPolicyVersion, write.RuleId, write.RuleVersion, write.SourceEventId,
                    write.SourceJournalEntryId, write.PostingKind, write.AdjustmentApproval);
                Records.Add(write.Entry.JournalEntryId, retained);
            }
            if (FailAfterAppend)
                throw new IOException("Synthetic interruption before confirmation.");
            return Task.FromResult(new TradeFillLedgerPostingConfirmation(retained!, isNew));
        }
    }
}
