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
using System.Globalization;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;

namespace Meridian.Tests.Execution;

public sealed partial class PaperFinancingPostingTests
{
    private static readonly TradeFillLedgerPostingContext Scope = new("financing-tests",
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"), ExpectedPeriodVersion: 1);

    private static PaperFinancingCharge Charge(string session = "paper-1", string account = "default") =>
        new(session, account, new DateOnly(2026, 11, 12), 12.50m, "broker-interest-20261112",
            "Broker-assessed daily margin financing", "test-operator");

    [Fact]
    public async Task DuplicateAndRestart_RetainOneBalancedJournalAndRejectChangedEconomics()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var service = CreatePostingService(root, Scope, target))
        {
            (await service.PostAsync(Charge())).Should().BeTrue();
            (await service.PostAsync(Charge())).Should().BeFalse();
            await service.Invoking(s => s.PostAsync(Charge() with { Amount = 20m }))
                .Should().ThrowAsync<InvalidDataException>();
        }
        await using var recovered = CreatePostingService(root, Scope, target);
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
        await using (var interrupted = CreatePostingService(root, Scope, target))
            await interrupted.Invoking(s => s.PostAsync(Charge())).Should().ThrowAsync<IOException>();
        target.FailBeforeAppend = target.FailAfterAppend = false;
        await using var recovered = CreatePostingService(root, Scope, target);
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
        await using (var first = CreatePostingService(root, Scope, target))
        {
            Action competing = () => _ = CreatePostingService(root, Scope, target);
            competing.Should().Throw<IOException>();
            await first.PostAsync(Charge());
        }
        await using var successor = CreatePostingService(root, Scope, target);
        (await successor.LoadAndRecoverAsync("paper-1")).Should().ContainSingle();
    }

    [Fact]
    public async Task LostInitializedSnapshot_FailsClosedInsteadOfDroppingPaidFinancing()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var first = CreatePostingService(root, Scope, target))
            await first.PostAsync(Charge());
        var scopeDirectory = new TradeFillPostingStoreOptions(root, Scope).ScopeDirectory;
        File.Delete(Path.Combine(scopeDirectory, "financing", "sources.snapshot.json"));
        await using var recovered = CreatePostingService(root, Scope, target);
        await recovered.Invoking(service => service.LoadAndRecoverAsync("paper-1"))
            .Should().ThrowAsync<InvalidDataException>().WithMessage("*missing*retained snapshot*");
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task EmptyInitializedStore_RestartsAndAcceptsItsFirstCharge()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var empty = CreatePostingService(root, Scope, target))
            (await empty.LoadSourcesAsync("paper-1")).Should().BeEmpty();
        await using var recovered = CreatePostingService(root, Scope, target);
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
        await using var recovered = CreatePostingService(root, Scope, target);
        (await recovered.LoadAndRecoverAsync("paper-1")).Should().Equal(Charge());
        File.Exists(Path.Combine(scopeDirectory, "financing", "initialized.json")).Should().BeTrue();
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task ChangedBookUnderSameScopeLabel_FailsClosed()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var first = CreatePostingService(root, Scope, target))
            await first.PostAsync(Charge());
        await using var changed = CreatePostingService(root, Scope with { LedgerBookId = Guid.NewGuid() }, target);
        await changed.Invoking(s => s.LoadAndRecoverAsync("paper-1")).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task AccountAndSourceIdentity_RemainDistinctAndScoped()
    {
        var target = new RetainedTarget();
        await using var service = CreatePostingService(TempRoot(), Scope, target);
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
        await using (var financing = CreatePostingService(root, Scope, target))
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
        await using var recoveredFinancing = CreatePostingService(root, Scope, target);
        await using var recoveredSessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        recoveredSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(887.50m);
        recoveredSessions.GetLedger(id)!.GetBalance(LedgerAccounts.MarginInterestExpense).Should().Be(12.50m);
        (await recoveredSessions.RecordFinancingAsync(Charge(id))).Should().BeFalse();
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task BoundSessionWithoutAccountingOwner_PreservesHistoryWithUnavailablePortfolio()
    {
        var root = TempRoot();
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        string sessionId;
        await using (var financing = CreatePostingService(root, Scope, new RetainedTarget()))
        await using (var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing))
            sessionId = (await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m))).SessionId;
        await using var unbound = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance, store);
        await unbound.InitialiseAsync();
        var detail = unbound.GetSession(sessionId)!;
        detail.Portfolio.Should().BeNull("missing accounting ownership cannot silently omit retained financing");
        detail.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
        (await unbound.CreateSessionAsync(new("current-unbound", null, 1_000m))).SessionId.Should().NotBe(sessionId);
        await unbound.Invoking(service => service.CloseSessionAsync(sessionId))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*historical accounting scope*");
    }

    [Fact]
    public async Task PendingAssessment_BlocksCloseUntilExactChargeIsRecovered()
    {
        var root = TempRoot();
        var target = new RetainedTarget { FailBeforeAppend = true };
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        await using var financing = CreatePostingService(root, Scope, target);
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

    [Theory]
    [InlineData(2026, 10, 31)]
    [InlineData(2026, 12, 1)]
    public async Task OutOfPeriodAssessment_IsNotAcceptedOrLatched_AndValidChargeSurvivesRestart(int year, int month, int day)
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        string id;
        await using (var financing = CreatePostingService(root, Scope, target))
        await using (var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing))
        {
            id = (await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m))).SessionId;
            var invalid = Charge(id) with { AccrualDate = new DateOnly(year, month, day) };
            await sessions.Invoking(service => service.RecordFinancingAsync(invalid))
                .Should().ThrowAsync<LedgerValidationException>().WithMessage("*outside accounting period*");
            (await financing.LoadSourcesAsync(id)).Should().BeEmpty();
            target.Records.Should().BeEmpty();
            (await sessions.RecordFinancingAsync(Charge(id))).Should().BeTrue();
            sessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
        }
        await using var recoveredFinancing = CreatePostingService(root, Scope, target);
        await using var recoveredSessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        (await recoveredFinancing.LoadSourcesAsync(id)).Should().Equal(Charge(id));
        recoveredSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
        (await recoveredSessions.CloseSessionAsync(id)).Should().BeTrue();
    }

    [Theory]
    [InlineData("SoftClosed")]
    [InlineData("HardClosed")]
    public async Task AlreadyClosedPeriod_RejectsBeforeSourceAcceptance(string status)
    {
        var target = new RetainedTarget();
        target.Period = target.Period! with { Status = status };
        await using var service = CreatePostingService(TempRoot(), Scope, target);
        var refusal = await service.Invoking(posting => posting.PostAsync(Charge()))
            .Should().ThrowAsync<PaperFinancingAdmissionException>();
        refusal.Which.InnerException.Should().BeOfType<LedgerValidationException>();
        (await service.LoadSourcesAsync("paper-1")).Should().BeEmpty();
        target.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0.00000000001")]
    [InlineData("12.12345678901")]
    [InlineData("79228162514264337593543950335")]
    public async Task UnrepresentableNewAmount_RejectsWithoutPoisoningSession(string amountText)
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        await using var financing = CreatePostingService(root, Scope, target);
        await using var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing);
        var id = (await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m))).SessionId;
        var invalid = Charge(id) with { Amount = decimal.Parse(amountText, CultureInfo.InvariantCulture) };
        await sessions.Invoking(service => service.RecordFinancingAsync(invalid))
            .Should().ThrowAsync<LedgerValidationException>().WithMessage("*numeric(38,10)*");
        (await financing.LoadSourcesAsync(id)).Should().BeEmpty();
        (await sessions.RecordFinancingAsync(Charge(id))).Should().BeTrue();
        target.AppendCount.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(2L)]
    public async Task MissingOrStaleExpectedVersion_IsRejectedBeforeAcceptance(long? expectedVersion)
    {
        var target = new RetainedTarget();
        await using var service = CreatePostingService(TempRoot(), Scope with { ExpectedPeriodVersion = expectedVersion }, target);
        await service.Invoking(posting => posting.PostAsync(Charge())).Should().ThrowAsync<PaperFinancingAdmissionException>();
        (await service.LoadSourcesAsync("paper-1")).Should().BeEmpty();
        target.AppendCount.Should().Be(0);
    }

    [Fact]
    public async Task ConfirmedAssessment_ReplaysAfterPeriodClosesWithoutNewSourceAdmission()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        await using (var service = CreatePostingService(root, Scope, target))
            await service.PostAsync(Charge());
        target.Period = target.Period! with { Status = "HardClosed", Version = 2 };
        target.Book = target.Book! with { AccountingBasis = AccountingBasisKindDto.Tax, AccountingPolicyId = "later-policy" };
        await using var recovered = CreatePostingService(root, Scope, target);
        (await recovered.LoadAndRecoverAsync("paper-1")).Should().Equal(Charge());
        (await recovered.PostAsync(Charge())).Should().BeFalse();
        target.AppendCount.Should().Be(1);
    }

    [Fact]
    public async Task CanonicalScope_RefusesNewFinancingBeforeDurableAcceptance()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        var scope = Scope with { PostingScope = Scope.PostingScope + "/v2", AccountingProtocolVersion = 2 };
        await using (var service = CreatePostingService(root, scope, target))
        {
            var refusal = await service.Invoking(posting => posting.PostAsync(Charge())).Should()
                .ThrowAsync<PaperFinancingAdmissionException>();
            refusal.Which.InnerException.Should().BeOfType<LedgerValidationException>().Which.Message.Should()
                .Contain("unsupported in canonical");
            (await service.LoadSourcesAsync("paper-1")).Should().BeEmpty();
            target.Records.Should().BeEmpty();
        }
        await using var restarted = CreatePostingService(root, scope, target);
        (await restarted.LoadAndRecoverAsync("paper-1")).Should().BeEmpty();
        target.AppendCount.Should().Be(0);
    }

    [Theory]
    [InlineData("tax")]
    [InlineData("wrong-policy")]
    [InlineData("wrong-version")]
    [InlineData("missing")]
    [InlineData("wrong-book")]
    [InlineData("consolidation")]
    public async Task IncompatibleAuthoritativeBook_IsNotAcceptedOrLatched_AndValidPrimaryStillWorks(string defect)
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        var validBook = target.Book!;
        var scope = defect == "consolidation" ? Scope with { AccountingPolicyId = "consolidation-v1" } : Scope;
        target.Book = defect switch
        {
            "tax" => validBook with { AccountingBasis = AccountingBasisKindDto.Tax },
            "wrong-policy" => validBook with { AccountingPolicyId = "other-policy" },
            "wrong-version" => validBook with { AccountingPolicyVersion = "other-version" },
            "missing" => null,
            "wrong-book" => validBook with { LedgerBookId = Guid.NewGuid() },
            "consolidation" => validBook with { AccountingPolicyId = "consolidation-v1" },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        string id;
        await using (var financing = CreatePostingService(root, scope, target))
        await using (var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing))
        {
            id = (await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m))).SessionId;
            await sessions.Invoking(service => service.RecordFinancingAsync(Charge(id) with { SourceReference = "rejected-book-source" }))
                .Should().ThrowAsync<LedgerValidationException>();
            (await financing.LoadSourcesAsync(id)).Should().BeEmpty();
            target.Records.Should().BeEmpty();
            // Close remains usable: admission refused the source rather than creating a
            // durable recovery obligation. Consolidation cannot be silently changed in place.
            if (defect == "consolidation")
            {
                (await sessions.CloseSessionAsync(id)).Should().BeTrue();
            }
            else
            {
                target.Book = validBook;
                (await sessions.RecordFinancingAsync(Charge(id))).Should().BeTrue();
                sessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
            }
        }
        await using var recoveredFinancing = CreatePostingService(root, scope, target);
        await using var recoveredSessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        if (defect == "consolidation")
            (await recoveredFinancing.LoadSourcesAsync(id)).Should().BeEmpty();
        else
            (await recoveredFinancing.LoadSourcesAsync(id)).Should().Equal(Charge(id));
    }

    [Fact]
    public async Task AuthoritativeBookPolicyMatching_UsesRetainedStoreCaseAndWhitespaceRules()
    {
        var target = new RetainedTarget();
        target.Book = target.Book! with
        {
            AccountingPolicyId = "  EXECUTION-TRADE-FILL ",
            AccountingPolicyVersion = " 1 "
        };
        await using var service = CreatePostingService(TempRoot(), Scope, target);
        (await service.PostAsync(Charge())).Should().BeTrue();
        target.Records.Values.Single().AggregateId.Should().Be(Scope.AggregateId);
    }

    [Fact]
    public async Task CloseRacingAfterAdmission_RetainsPendingSourceAndKeepsSessionFailClosed()
    {
        var root = TempRoot();
        var target = new RetainedTarget();
        target.BeforeAppend = () => target.Period = target.Period! with { Status = "HardClosed", Version = 2 };
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        await using var financing = CreatePostingService(root, Scope, target);
        await using var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing);
        var id = (await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m))).SessionId;
        await sessions.Invoking(service => service.RecordFinancingAsync(Charge(id))).Should().ThrowAsync<LedgerValidationException>();
        (await financing.LoadSourcesAsync(id)).Should().Equal(Charge(id));
        target.Records.Should().BeEmpty();
        await sessions.Invoking(service => service.CloseSessionAsync(id)).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancellationWaitingForAnotherSessionsPosting_DoesNotLatchAnUnacceptedSource()
    {
        var root = TempRoot();
        var target = new RetainedTarget
        {
            AppendRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var store = new JsonlFilePaperSessionStore(Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);
        await using var financing = CreatePostingService(root, Scope, target);
        await using var sessions = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
            store, financing: financing);
        var firstId = (await sessions.CreateSessionAsync(new("strategy-1", null, 1_000m))).SessionId;
        var secondId = (await sessions.CreateSessionAsync(new("strategy-2", null, 1_000m))).SessionId;
        var firstPost = sessions.RecordFinancingAsync(Charge(firstId));
        await target.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource();
        try
        {
            var cancelledPost = sessions.RecordFinancingAsync(Charge(secondId), cts.Token);
            cts.Cancel();
            await ((Func<Task>)(() => cancelledPost)).Should().ThrowAsync<OperationCanceledException>();
        }
        finally { target.AppendRelease.TrySetResult(); }
        await firstPost;
        (await financing.LoadSourcesAsync(secondId)).Should().BeEmpty();
        (await sessions.RecordFinancingAsync(Charge(secondId) with { SourceReference = "after-cancellation" })).Should().BeTrue();
        (await sessions.CloseSessionAsync(secondId)).Should().BeTrue();
    }

    private static PaperFinancingPostingService CreatePostingService(string root,
        TradeFillLedgerPostingContext scope, RetainedTarget target) =>
        new(root, scope, target, target.ReadPeriodAsync, target.ReadBookAsync);

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "meridian-financing-tests", Guid.NewGuid().ToString("N"));

    private sealed class RetainedTarget : ITradeFillLedgerPostingTarget
    {
        public Dictionary<Guid, LedgerJournalEntryRecord> Records { get; } = [];
        public bool FailBeforeAppend { get; set; }
        public bool FailAfterAppend { get; set; }
        public int AppendCount { get; private set; }
        public LedgerAccountingPeriod? Period { get; set; } = new(Scope.PeriodId, Scope.LedgerBookId,
            2026, 11, "November 2026", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30),
            "Open", DateTimeOffset.UtcNow, null, 1);
        public LedgerBookRecord? Book { get; set; } = new(Scope.LedgerBookId, "test-fund",
            Guid.NewGuid(), FundStructureNodeKindDto.Account, "Primary financing book", "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            AccountingPolicyId: Scope.AccountingPolicyId, AccountingPolicyVersion: Scope.AccountingPolicyVersion);
        public Action? BeforeAppend { get; set; }
        public TaskCompletionSource AppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? AppendRelease { get; init; }

        public Task<LedgerAccountingPeriod?> ReadPeriodAsync(Guid periodId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Period);
        }

        public Task<LedgerBookRecord?> ReadBookAsync(Guid ledgerBookId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ledgerBookId.Should().Be(Scope.LedgerBookId, "book admission resolves the bound book, not the aggregate");
            return Task.FromResult(Book);
        }

        public async Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(LedgerJournalEntryWrite write, CancellationToken ct = default)
        {
            if (FailBeforeAppend)
                throw new IOException("Synthetic authoritative store outage.");
            var isNew = !Records.TryGetValue(write.Entry.JournalEntryId, out var retained);
            if (isNew)
            {
                AppendStarted.TrySetResult();
                if (AppendRelease is not null)
                    await AppendRelease.Task.WaitAsync(ct);
                BeforeAppend?.Invoke();
                LedgerPeriodPostingGuard.Validate(write, Period
                    ?? throw new LedgerValidationException("The accounting period is unavailable."));
            }
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
            return new TradeFillLedgerPostingConfirmation(retained!, isNew);
        }
    }
}
