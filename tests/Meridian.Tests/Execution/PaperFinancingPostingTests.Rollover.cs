using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed partial class PaperFinancingPostingTests
{
    private static TradeFillLedgerPostingContext DecemberScope => Scope with
    {
        PostingScope = "financing-tests-december",
        PeriodId = Guid.Parse("44444444-4444-4444-4444-444444444444")
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodRollover_EmptyHistoricalSessionRemainsReadableWithoutRebinding(bool closed)
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            if (closed)
                (await sessions.CloseSessionAsync(id)).Should().BeTrue();
        }
        var retainedFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        target.UseDecemberPeriod();

        await using var currentFinancing = RolloverPosting(root, DecemberScope, target);
        await using var currentSessions = RolloverSessions(store, currentFinancing);
        await currentSessions.InitialiseAsync();

        currentSessions.GetSessions().Should().ContainSingle(summary => summary.SessionId == id);
        var historical = currentSessions.GetSession(id)!;
        historical.Summary.IsActive.Should().Be(!closed);
        historical.Portfolio!.Cash.Should().Be(1_000m);
        historical.AccountingUnavailableReason.Should().BeNull();
        currentSessions.GetActivePortfolio(id).Should().BeNull("historical accounting bindings are read-only");
        (await currentSessions.ReplaySessionAsync(id))!.Cash.Should().Be(1_000m);
        var created = await currentSessions.CreateSessionAsync(new("new-strategy", null, 2_000m));
        var records = await store.LoadAllSessionsAsync();
        records.Single(record => record.SessionId == id).AccountingContext.Should().Be(Scope);
        records.Single(record => record.SessionId == created.SessionId).AccountingContext.Should().Be(DecemberScope);
        currentSessions.GetActivePortfolio(created.SessionId).Should().NotBeNull();
        RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(retainedFiles);
        target.CallCount.Should().Be(0, "reading empty historical evidence never invokes the posting target");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodRollover_PostedNativeAndLegacyChargesKeepExactCashAndEvidenceAcrossRestart(bool legacy)
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            if (!legacy)
                (await sessions.RecordFinancingAsync(Charge(id))).Should().BeTrue();
        }
        if (legacy)
            await SeedPostedLegacyRolloverAsync(root, id, target);

        var retained = target.Inner.Records.Values.Should().ContainSingle().Which;
        var journalId = retained.Entry.JournalEntryId;
        var lineIds = retained.Entry.Lines.Select(line => line.EntryId).ToArray();
        var journal = SerializedJournal(retained.Entry);
        var retainedFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        var calls = target.CallCount;
        target.UseDecemberPeriod();

        for (var restart = 0; restart < 2; restart++)
        {
            await using var currentFinancing = RolloverPosting(root, DecemberScope, target);
            await using var currentSessions = RolloverSessions(store, currentFinancing);
            await currentSessions.InitialiseAsync();

            var historical = currentSessions.GetSession(id)!;
            historical.Portfolio!.Cash.Should().Be(987.50m);
            historical.AccountingUnavailableReason.Should().BeNull();
            currentSessions.GetLedger(id)!.GetBalance(LedgerAccounts.MarginInterestExpense).Should().Be(12.50m);
            (await currentSessions.ReplaySessionAsync(id))!.Cash.Should().Be(987.50m);
            currentSessions.GetActivePortfolio(id).Should().BeNull();
            target.CallCount.Should().Be(calls, "historical reads cannot confirm or repost a retained charge");
            target.Inner.Records.Should().ContainSingle();
            var unchanged = target.Inner.Records[journalId].Entry;
            unchanged.JournalEntryId.Should().Be(journalId);
            unchanged.Lines.Select(line => line.EntryId).Should().Equal(lineIds);
            SerializedJournal(unchanged).Should().Be(journal);
            if (legacy)
                unchanged.Lines.Should().OnlyContain(line => line.Currency == null);
            else
                AssertNative(unchanged, "USD");
            RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(retainedFiles);
            (await store.LoadAllSessionsAsync()).Single(record => record.SessionId == id)
                .AccountingContext.Should().Be(Scope);
        }
    }

    [Fact]
    public async Task PeriodRollover_ObsoleteActiveSessionRefusesEveryMutationBeforePersistence()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
        target.UseDecemberPeriod();
        await using var currentFinancing = RolloverPosting(root, DecemberScope, target);
        await using var currentSessions = RolloverSessions(store, currentFinancing);
        await currentSessions.InitialiseAsync();
        await currentSessions.CreateSessionAsync(new("new-strategy", null));
        var before = RolloverFileHashes(root);
        var timestamp = Charge(id).OccurredAt.AddHours(1);

        await currentSessions.Invoking(sessions => sessions.RecordFillAsync(id, new ExecutionReport
        {
            OrderId = "obsolete-fill",
            Symbol = "AAPL",
            ReportType = ExecutionReportType.Fill,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            FillPrice = 100m,
            Timestamp = timestamp
        })).Should().ThrowAsync<InvalidOperationException>();
        await currentSessions.Invoking(sessions => sessions.RecordOrderUpdateAsync(id, new OrderState
        {
            OrderId = "obsolete-order",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            Status = OrderStatus.Accepted,
            CreatedAt = timestamp
        })).Should().ThrowAsync<InvalidOperationException>();
        await currentSessions.Invoking(sessions => sessions.RecordSettlementAsync(id,
            PaperSessionSettlementRecord.Create("default", "AAPL", new DateOnly(2026, 11, 12), 100m, timestamp)))
            .Should().ThrowAsync<InvalidOperationException>();
        await currentSessions.Invoking(sessions => sessions.RecordFinancingAsync(Charge(id)))
            .Should().ThrowAsync<InvalidOperationException>();
        await currentSessions.Invoking(sessions => sessions.CloseSessionAsync(id))
            .Should().ThrowAsync<InvalidOperationException>();

        RolloverFileHashes(root).Should().BeEquivalentTo(before);
        currentSessions.GetSession(id)!.Summary.IsActive.Should().BeTrue();
        currentSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(1_000m);
        target.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodRollover_PendingAssessmentIsUnavailableWithoutDeliveryOrAcknowledgement(bool afterAppend)
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        var originalPeriod = target.Inner.Period;
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            target.Inner.FailBeforeAppend = !afterAppend;
            target.Inner.FailAfterAppend = afterAppend;
            await sessions.Invoking(service => service.RecordFinancingAsync(Charge(id)))
                .Should().ThrowAsync<IOException>();
        }
        ReadFinancingSnapshot(root).States.Single().Posted.Should().BeFalse();
        var retainedFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        var calls = target.CallCount;
        target.Inner.FailBeforeAppend = target.Inner.FailAfterAppend = false;
        target.UseDecemberPeriod();

        await using (var currentFinancing = RolloverPosting(root, DecemberScope, target))
        await using (var currentSessions = RolloverSessions(store, currentFinancing))
        {
            await currentSessions.InitialiseAsync();

            var historical = currentSessions.GetSession(id)!;
            historical.Portfolio.Should().BeNull("an unconfirmed source cannot become complete historical cash");
            historical.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
            currentSessions.GetActivePortfolio(id).Should().BeNull();
            await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
                .Should().ThrowAsync<InvalidOperationException>();
            await currentSessions.CreateSessionAsync(new("new-strategy", null));
            target.CallCount.Should().Be(calls);
            target.Inner.AppendCount.Should().Be(afterAppend ? 1 : 0);
            ReadFinancingSnapshot(root).States.Single().Posted.Should().BeFalse();
            ReadFinancingSnapshot(root).States.Single().NativeCurrency.Should().Be("USD");
            RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(retainedFiles);
        }

        target.Inner.Period = originalPeriod;
        await using (var originalOwner = RolloverPosting(root, Scope, target))
        await using (var originalSessions = RolloverSessions(store, originalOwner))
        {
            await originalSessions.InitialiseAsync();
            originalSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
            (await originalOwner.LoadSourcesAsync(id)).Should().Equal(Charge(id));
        }
        target.Inner.AppendCount.Should().Be(1);
        AssertNative(target.Inner.Records.Values.Single().Entry, "USD");
        ReadFinancingSnapshot(root).States.Single().Posted.Should().BeTrue();
        target.UseDecemberPeriod();
        var recoveredCalls = target.CallCount;
        await using var recoveredFinancing = RolloverPosting(root, DecemberScope, target);
        await using var recoveredSessions = RolloverSessions(store, recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        recoveredSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
        recoveredSessions.GetSession(id)!.AccountingUnavailableReason.Should().BeNull();
        target.CallCount.Should().Be(recoveredCalls);
    }

    [Fact]
    public async Task PeriodRollover_UnappliedHistoricalFillRemainsUnavailableUntilItsOriginalOwnerRecovers()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        var originalPeriod = target.Inner.Period;
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
        var fill = new ExecutionReport
        {
            OrderId = "unapplied-old-fill",
            Symbol = "AAPL",
            ReportType = ExecutionReportType.Fill,
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 1m,
            FilledQuantity = 1m,
            FillPrice = 100m,
            Timestamp = Charge(id).OccurredAt.AddHours(1)
        };
        await store.TryAppendFillAsync(id, PaperSessionFillRecord.CreateCanonical(fill, fill.Timestamp));
        (await store.LoadFillRecordsAsync(id)).Single().IsApplied.Should().BeFalse();
        var retainedFiles = RolloverFileHashes(Path.Combine(root, "sessions", id));
        target.UseDecemberPeriod();

        await using (var currentFinancing = RolloverPosting(root, DecemberScope, target))
        await using (var currentSessions = RolloverSessions(store, currentFinancing))
        {
            await currentSessions.InitialiseAsync();
            currentSessions.GetSession(id)!.Portfolio.Should().BeNull();
            currentSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
            currentSessions.GetSessionFillHistory(id).Should().ContainSingle();
            await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
                .Should().ThrowAsync<InvalidOperationException>();
            (await store.LoadFillRecordsAsync(id)).Single().IsApplied.Should().BeFalse();
            RolloverFileHashes(Path.Combine(root, "sessions", id)).Should().BeEquivalentTo(retainedFiles);
            await currentSessions.CreateSessionAsync(new("new-strategy", null));
            target.CallCount.Should().Be(0);
        }

        target.Inner.Period = originalPeriod;
        await using var originalFinancing = RolloverPosting(root, Scope, target);
        await using var originalSessions = RolloverSessions(store, originalFinancing);
        await originalSessions.InitialiseAsync();
        (await store.LoadFillRecordsAsync(id)).Single().IsApplied.Should().BeTrue();
        originalSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(900m);
        originalSessions.GetSession(id)!.AccountingUnavailableReason.Should().BeNull();
        target.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task PeriodRollover_ReusedScopeLabelNeverReinterpretsTheOriginalCheckpoint()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
        var retainedFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        target.UseDecemberPeriod();
        await using var changedFinancing = RolloverPosting(root, DecemberScope with { PostingScope = Scope.PostingScope }, target);
        await using var changedSessions = RolloverSessions(store, changedFinancing);
        await changedSessions.InitialiseAsync();

        changedSessions.GetSession(id)!.Portfolio.Should().BeNull();
        changedSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
        await changedSessions.Invoking(sessions => sessions.CreateSessionAsync(new("new-strategy", null)))
            .Should().ThrowAsync<InvalidDataException>();
        (await store.LoadAllSessionsAsync()).Should().ContainSingle().Which.AccountingContext.Should().Be(Scope);
        RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(retainedFiles);
        target.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodRollover_MissingOrConflictingSourceSnapshotIsUnavailableInsteadOfEmpty(bool conflicting)
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            await sessions.RecordFinancingAsync(Charge(id));
        }
        if (conflicting)
        {
            var snapshot = ReadFinancingSnapshot(root);
            await File.WriteAllTextAsync(FinancingSnapshotPath(root), JsonSerializer.Serialize(
                snapshot with { Scope = Scope with { LedgerBookId = Guid.NewGuid() } },
                ExecutionJsonContext.Default.PaperFinancingPostingSnapshot));
        }
        else
            File.Delete(FinancingSnapshotPath(root));
        var retainedFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        var calls = target.CallCount;
        target.UseDecemberPeriod();

        await using var currentFinancing = RolloverPosting(root, DecemberScope, target);
        await using var currentSessions = RolloverSessions(store, currentFinancing);
        await currentSessions.InitialiseAsync();

        var historical = currentSessions.GetSession(id)!;
        historical.Portfolio.Should().BeNull();
        historical.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
        currentSessions.GetSessions().Should().ContainSingle(summary => summary.SessionId == id);
        await currentSessions.CreateSessionAsync(new("new-strategy", null));
        target.CallCount.Should().Be(calls);
        target.Inner.AppendCount.Should().Be(1);
        RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(retainedFiles);
        File.Exists(FinancingSnapshotPath(root)).Should().Be(conflicting,
            "historical reads cannot replace missing or conflicting evidence with a new checkpoint");
    }

    [Fact]
    public async Task PeriodRollover_ChecksumInvalidCompletedWalTailMakesHistoricalCoverageUnavailable()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            await sessions.RecordFinancingAsync(Charge(id));
        }
        ReadFinancingSnapshot(root).States.Single().Posted.Should().BeTrue();
        var walDirectory = Path.Combine(RolloverSourceDirectory(root), "wal");
        WalRecord tail;
        await using (var wal = new WriteAheadLog(walDirectory, new WalOptions
        {
            SyncMode = WalSyncMode.EveryWrite,
            CorruptionMode = WalCorruptionMode.Halt,
            ArchiveAfterTruncate = false
        }))
        {
            await wal.InitializeAsync();
            var extraCharge = Charge(id) with { SourceReference = "uncheckpointed-broker-assessment", Amount = 20m };
            var payload = new PaperFinancingWalPayload(Scope,
                new PaperFinancingPostingState(extraCharge, Posted: false, NativeCurrency: "USD"));
            tail = await wal.AppendAsync(JsonSerializer.Serialize(payload,
                ExecutionJsonContext.Default.PaperFinancingWalPayload), "FinancingAccepted");
        }
        var walPath = Directory.EnumerateFiles(walDirectory, "*.wal").Single(path =>
            File.ReadLines(path).Any(line => line.StartsWith($"{tail.Sequence}|", StringComparison.Ordinal)));
        var original = await File.ReadAllTextAsync(walPath);
        original.Should().EndWith("\n", "a complete corrupt record must not be treated as an interrupted final write");
        var badChecksum = (tail.Checksum[0] == '0' ? "1" : "0") + tail.Checksum[1..];
        var corrupted = original.Replace($"|{tail.Checksum}|", $"|{badChecksum}|", StringComparison.Ordinal);
        corrupted.Should().NotBe(original);
        await File.WriteAllTextAsync(walPath, corrupted);
        var sourceFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        var sessionFiles = RolloverFileHashes(Path.Combine(root, "sessions", id));
        var calls = target.CallCount;
        target.UseDecemberPeriod();

        await using var currentFinancing = RolloverPosting(root, DecemberScope, target);
        await using var currentSessions = RolloverSessions(store, currentFinancing);
        await currentSessions.InitialiseAsync();

        currentSessions.GetSession(id)!.Portfolio.Should().BeNull();
        currentSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
        currentSessions.GetActivePortfolio(id).Should().BeNull();
        await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
            .Should().ThrowAsync<InvalidOperationException>();
        await currentSessions.CreateSessionAsync(new("new-strategy", null));
        target.CallCount.Should().Be(calls);
        target.Inner.AppendCount.Should().Be(1);
        RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(sourceFiles);
        RolloverFileHashes(Path.Combine(root, "sessions", id)).Should().BeEquivalentTo(sessionFiles);
        ReadFinancingSnapshot(root).States.Should().ContainSingle().Which.Posted.Should().BeTrue();
        ReadFinancingSnapshot(root).States.Single().Charge.Should().Be(Charge(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodRollover_ChangedOrOmittedSourceMustMatchTheIndependentSessionJournal(bool omitted)
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            await sessions.RecordFinancingAsync(Charge(id));
        }
        (await store.LoadLedgerJournalAsync(id)).Should().ContainSingle(entry => entry.ActivityType == "margin-interest");
        var retainedJournal = SerializedJournal(target.Inner.Records.Values.Single().Entry);
        var snapshot = ReadFinancingSnapshot(root);
        var state = snapshot.States.Single();
        var changed = state with { Charge = state.Charge with { Amount = 25m } };
        changed.Charge.SourceEventId.Should().Be(state.Charge.SourceEventId,
            "source identity deliberately does not encode the assessed amount");
        await File.WriteAllTextAsync(FinancingSnapshotPath(root), JsonSerializer.Serialize(
            snapshot with { States = omitted ? [] : [changed] },
            ExecutionJsonContext.Default.PaperFinancingPostingSnapshot));
        var sourceFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        var sessionFiles = RolloverFileHashes(Path.Combine(root, "sessions", id));
        var calls = target.CallCount;
        target.UseDecemberPeriod();

        await using var currentFinancing = RolloverPosting(root, DecemberScope, target);
        await using var currentSessions = RolloverSessions(store, currentFinancing);
        await currentSessions.InitialiseAsync();

        currentSessions.GetSession(id)!.Portfolio.Should().BeNull();
        currentSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
        await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
            .Should().ThrowAsync<InvalidOperationException>();
        await currentSessions.CreateSessionAsync(new("new-strategy", null));
        target.CallCount.Should().Be(calls);
        target.Inner.AppendCount.Should().Be(1);
        SerializedJournal(target.Inner.Records.Values.Single().Entry).Should().Be(retainedJournal);
        RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(sourceFiles);
        RolloverFileHashes(Path.Combine(root, "sessions", id)).Should().BeEquivalentTo(sessionFiles);
    }

    [Fact]
    public async Task PeriodRollover_StaleFinancingProjectionRequiresOriginalOwnerConfirmationAndRefresh()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var originalPeriod = target.Inner.Period;
        var store = RolloverStore(root);
        var failingStore = new RolloverLedgerFailureStore(store);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(failingStore, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            failingStore.FailLedgerWrites = true;
            (await sessions.RecordFinancingAsync(Charge(id))).Should().BeTrue();
            sessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
        }
        failingStore.FailedLedgerWrites.Should().Be(1);
        ReadFinancingSnapshot(root).States.Single().Posted.Should().BeTrue();
        (await store.LoadLedgerJournalAsync(id)).Should().NotContain(entry => entry.ActivityType == "margin-interest");
        var sourceFiles = RolloverFileHashes(RolloverSourceDirectory(root));
        var sessionFiles = RolloverFileHashes(Path.Combine(root, "sessions", id));
        var calls = target.CallCount;
        target.UseDecemberPeriod();

        await using (var currentFinancing = RolloverPosting(root, DecemberScope, target))
        await using (var currentSessions = RolloverSessions(store, currentFinancing))
        {
            await currentSessions.InitialiseAsync();
            currentSessions.GetSession(id)!.Portfolio.Should().BeNull();
            currentSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
            await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
                .Should().ThrowAsync<InvalidOperationException>();
            await currentSessions.CreateSessionAsync(new("new-strategy", null));
            target.CallCount.Should().Be(calls);
            RolloverFileHashes(RolloverSourceDirectory(root)).Should().BeEquivalentTo(sourceFiles);
            RolloverFileHashes(Path.Combine(root, "sessions", id)).Should().BeEquivalentTo(sessionFiles);
        }

        target.Inner.Period = originalPeriod;
        await using (var originalFinancing = RolloverPosting(root, Scope, target))
        await using (var originalSessions = RolloverSessions(store, originalFinancing))
        {
            await originalSessions.InitialiseAsync();
            originalSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
            (await store.LoadLedgerJournalAsync(id)).Should().ContainSingle(entry => entry.ActivityType == "margin-interest");
        }
        target.CallCount.Should().Be(calls + 1, "the original owner must confirm the canonical charge before refreshing its projection");
        target.Inner.AppendCount.Should().Be(1);
        var confirmedCalls = target.CallCount;
        target.UseDecemberPeriod();
        await using var recoveredFinancing = RolloverPosting(root, DecemberScope, target);
        await using var recoveredSessions = RolloverSessions(store, recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        recoveredSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
        recoveredSessions.GetSession(id)!.AccountingUnavailableReason.Should().BeNull();
        target.CallCount.Should().Be(confirmedCalls);
    }

    [Fact]
    public async Task PeriodRollover_CurrentOwnerCannotEraseARetainedChargeWhenAnotherSourceStillConfirms()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        PaperFinancingCharge second;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            await sessions.RecordFinancingAsync(Charge(id));
            second = Charge(id) with { SourceReference = "second-paid-assessment", Amount = 7.50m };
            await sessions.RecordFinancingAsync(second);
            sessions.GetSession(id)!.Portfolio!.Cash.Should().Be(980m);
        }
        var snapshot = ReadFinancingSnapshot(root);
        await File.WriteAllTextAsync(FinancingSnapshotPath(root), JsonSerializer.Serialize(snapshot with
        {
            States = [snapshot.States.Single(state => state.Charge.SourceEventId == second.SourceEventId)]
        }, ExecutionJsonContext.Default.PaperFinancingPostingSnapshot));
        var retainedSnapshot = await File.ReadAllTextAsync(FinancingSnapshotPath(root));
        var sessionFiles = RolloverFileHashes(Path.Combine(root, "sessions", id));
        var calls = target.CallCount;

        await using var currentFinancing = RolloverPosting(root, Scope, target);
        await using var currentSessions = RolloverSessions(store, currentFinancing);
        await currentSessions.InitialiseAsync();

        currentSessions.GetSession(id)!.Portfolio.Should().BeNull();
        currentSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
        currentSessions.GetActivePortfolio(id).Should().BeNull();
        target.CallCount.Should().Be(calls + 1, "the remaining source confirms, but cannot authorize deleting the omitted charge");
        await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
            .Should().ThrowAsync<InvalidOperationException>();
        var confirmedCalls = target.CallCount;
        await currentSessions.Invoking(sessions => sessions.RecordFinancingAsync(
            Charge(id) with { SourceReference = "must-not-replace-missing-source" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await currentSessions.CreateSessionAsync(new("new-strategy", null));
        target.CallCount.Should().Be(confirmedCalls);
        target.Inner.AppendCount.Should().Be(2);
        (await store.LoadLedgerJournalAsync(id)).Count(entry => entry.ActivityType == "margin-interest").Should().Be(2);
        RolloverFileHashes(Path.Combine(root, "sessions", id)).Should().BeEquivalentTo(sessionFiles);
        (await File.ReadAllTextAsync(FinancingSnapshotPath(root))).Should().Be(retainedSnapshot);
    }

    [Fact]
    public async Task PeriodRollover_PreMetadataLegacyCacheRequiresOriginalOwnerRefreshWithoutChangingCanonicalEvidence()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var originalPeriod = target.Inner.Period;
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
            id = (await sessions.CreateSessionAsync(new("legacy-strategy", null, 1_000m))).SessionId;
        await SeedPostedLegacyRolloverAsync(root, id, target);
        var journal = await store.LoadLedgerJournalAsync(id);
        var legacyEntry = journal.Single(entry => entry.ActivityType == "margin-interest") with { Metadata = null };
        using (var legacyJson = JsonDocument.Parse(JsonSerializer.Serialize(legacyEntry,
            ExecutionJsonContext.Default.PersistedJournalEntryDto)))
            legacyJson.RootElement.TryGetProperty("metadata", out _).Should().BeFalse();
        await store.SaveLedgerJournalAsync(id, journal.Select(entry =>
            entry.JournalEntryId == legacyEntry.JournalEntryId ? legacyEntry : entry).ToArray());
        var sessionFiles = RolloverFileHashes(Path.Combine(root, "sessions", id));
        var snapshotBytes = await File.ReadAllTextAsync(FinancingSnapshotPath(root));
        var markerPath = Path.Combine(RolloverSourceDirectory(root), "initialized.json");
        var markerBytes = await File.ReadAllTextAsync(markerPath);
        ReadFinancingSnapshot(root).States.Single().NativeCurrency.Should().BeNull();
        var canonical = target.Inner.Records.Values.Single().Entry;
        canonical.Lines.Should().OnlyContain(line => line.Currency == null);
        var canonicalBytes = SerializedJournal(canonical);
        var calls = target.CallCount;
        target.UseDecemberPeriod();

        await using (var currentFinancing = RolloverPosting(root, DecemberScope, target))
        await using (var currentSessions = RolloverSessions(store, currentFinancing))
        {
            await currentSessions.InitialiseAsync();
            currentSessions.GetSession(id)!.Portfolio.Should().BeNull();
            currentSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
            await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
                .Should().ThrowAsync<InvalidOperationException>();
            await currentSessions.CreateSessionAsync(new("new-strategy", null));
            target.CallCount.Should().Be(calls);
            RolloverFileHashes(Path.Combine(root, "sessions", id)).Should().BeEquivalentTo(sessionFiles);
        }

        target.Inner.Period = originalPeriod;
        await using (var originalFinancing = RolloverPosting(root, Scope, target))
        await using (var originalSessions = RolloverSessions(store, originalFinancing))
        {
            await originalSessions.InitialiseAsync();
            originalSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
            var refreshed = (await store.LoadLedgerJournalAsync(id)).Single(entry => entry.ActivityType == "margin-interest");
            refreshed.Metadata.Should().NotBeNull();
            refreshed.Lines.Should().Equal(legacyEntry.Lines);
            (refreshed with { Lines = legacyEntry.Lines, Metadata = null }).Should().Be(legacyEntry);
        }
        target.CallCount.Should().Be(calls + 1);
        target.Inner.AppendCount.Should().Be(1);
        target.Inner.Records.Should().ContainSingle();
        SerializedJournal(target.Inner.Records[canonical.JournalEntryId].Entry).Should().Be(canonicalBytes);
        (await File.ReadAllTextAsync(FinancingSnapshotPath(root))).Should().Be(snapshotBytes);
        (await File.ReadAllTextAsync(markerPath)).Should().Be(markerBytes);
        ReadFinancingSnapshot(root).States.Single().NativeCurrency.Should().BeNull();
        var confirmedCalls = target.CallCount;
        target.UseDecemberPeriod();

        await using var recoveredFinancing = RolloverPosting(root, DecemberScope, target);
        await using var recoveredSessions = RolloverSessions(store, recoveredFinancing);
        await recoveredSessions.InitialiseAsync();
        recoveredSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
        recoveredSessions.GetSession(id)!.AccountingUnavailableReason.Should().BeNull();
        target.CallCount.Should().Be(confirmedCalls);
        SerializedJournal(target.Inner.Records[canonical.JournalEntryId].Entry).Should().Be(canonicalBytes);
    }

    [Fact]
    public async Task PeriodRollover_CurrentOwnerCannotRefreshContradictoryNonNullProjectionMetadata()
    {
        var root = TempRoot();
        var target = new RolloverTarget();
        var store = RolloverStore(root);
        string id;
        await using (var financing = RolloverPosting(root, Scope, target))
        await using (var sessions = RolloverSessions(store, financing))
        {
            id = (await sessions.CreateSessionAsync(new("old-strategy", null, 1_000m))).SessionId;
            await sessions.RecordFinancingAsync(Charge(id));
        }
        var journal = await store.LoadLedgerJournalAsync(id);
        var original = journal.Single(entry => entry.ActivityType == "margin-interest");
        original.Metadata.Should().NotBeNull();
        var changed = original with
        {
            Metadata = original.Metadata! with { IdempotencyKey = "paper-financing:contradictory-retained-key" }
        };
        changed.Lines.Should().Equal(original.Lines);
        (changed with { Metadata = original.Metadata }).Should().Be(original);
        await store.SaveLedgerJournalAsync(id, journal.Select(entry =>
            entry.JournalEntryId == changed.JournalEntryId ? changed : entry).ToArray());
        var sessionFiles = RolloverFileHashes(Path.Combine(root, "sessions", id));
        var snapshotBytes = await File.ReadAllTextAsync(FinancingSnapshotPath(root));
        var markerPath = Path.Combine(RolloverSourceDirectory(root), "initialized.json");
        var markerBytes = await File.ReadAllTextAsync(markerPath);
        var canonicalBytes = SerializedJournal(target.Inner.Records.Values.Single().Entry);
        var calls = target.CallCount;

        await using var currentFinancing = RolloverPosting(root, Scope, target);
        await using var currentSessions = RolloverSessions(store, currentFinancing);
        await currentSessions.InitialiseAsync();

        target.CallCount.Should().Be(calls + 1, "canonical confirmation does not authorize changing contradictory retained metadata");
        currentSessions.GetSession(id)!.Portfolio.Should().BeNull();
        currentSessions.GetSession(id)!.AccountingUnavailableReason.Should().NotBeNullOrWhiteSpace();
        currentSessions.GetActivePortfolio(id).Should().BeNull();
        await currentSessions.Invoking(sessions => sessions.ReplaySessionAsync(id))
            .Should().ThrowAsync<InvalidOperationException>();
        var confirmedCalls = target.CallCount;
        await currentSessions.Invoking(sessions => sessions.RecordFinancingAsync(
            Charge(id) with { SourceReference = "must-not-replace-contradictory-metadata" }))
            .Should().ThrowAsync<InvalidOperationException>();
        target.CallCount.Should().Be(confirmedCalls);
        target.Inner.AppendCount.Should().Be(1);
        RolloverFileHashes(Path.Combine(root, "sessions", id)).Should().BeEquivalentTo(sessionFiles);
        (await File.ReadAllTextAsync(FinancingSnapshotPath(root))).Should().Be(snapshotBytes);
        (await File.ReadAllTextAsync(markerPath)).Should().Be(markerBytes);
        SerializedJournal(target.Inner.Records.Values.Single().Entry).Should().Be(canonicalBytes);
    }

    private static JsonlFilePaperSessionStore RolloverStore(string root) => new(
        Path.Combine(root, "sessions"), NullLogger<JsonlFilePaperSessionStore>.Instance);

    private static PaperSessionPersistenceService RolloverSessions(
        IPaperSessionStore store, PaperFinancingPostingService financing) => new(
        NullLogger<PaperSessionPersistenceService>.Instance, store, financing: financing);

    private static PaperFinancingPostingService RolloverPosting(
        string root, TradeFillLedgerPostingContext context, RolloverTarget target) =>
        new(root, context, target, target.Inner.ReadPeriodAsync, target.Inner.ReadBookAsync);

    private static string RolloverSourceDirectory(string root) => Path.Combine(
        new TradeFillPostingStoreOptions(root, Scope).ScopeDirectory, "financing");

    private static Dictionary<string, string> RolloverFileHashes(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !string.Equals(Path.GetFileName(path), "writer.lock", StringComparison.Ordinal))
        .ToDictionary(path => Path.GetRelativePath(root, path),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static async Task SeedPostedLegacyRolloverAsync(string root, string id, RolloverTarget target)
    {
        await using (var builder = RolloverPosting(root, Scope, target))
            await target.PostAndConfirmAsync(AccountingPostingCommandValidator.NormalizeAndValidate(builder.BuildWrite(Charge(id))));
        var snapshot = new PaperFinancingPostingSnapshot(1, Scope, 0,
            [new PaperFinancingPostingState(Charge(id), Posted: true)]);
        await File.WriteAllTextAsync(FinancingSnapshotPath(root), JsonSerializer.Serialize(snapshot,
            ExecutionJsonContext.Default.PaperFinancingPostingSnapshot));
        await using var originalFinancing = RolloverPosting(root, Scope, target);
        await using var originalSessions = RolloverSessions(RolloverStore(root), originalFinancing);
        await originalSessions.InitialiseAsync();
        originalSessions.GetSession(id)!.Portfolio!.Cash.Should().Be(987.50m);
    }

    private sealed class RolloverLedgerFailureStore(IPaperSessionStore inner) : IPaperSessionStore
    {
        public bool FailLedgerWrites { get; set; }
        public int FailedLedgerWrites { get; private set; }

        public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default) =>
            inner.SaveSessionMetadataAsync(record, ct);

        public Task AppendFillAsync(string sessionId, ExecutionReport fill, CancellationToken ct = default) =>
            inner.AppendFillAsync(sessionId, fill, ct);

        public Task<PaperSessionFillAppendResult> TryAppendFillAsync(string sessionId,
            PaperSessionFillRecord record, CancellationToken ct = default) => inner.TryAppendFillAsync(sessionId, record, ct);

        public Task MarkFillAppliedAsync(string sessionId, Guid fillId, string canonicalHash, CancellationToken ct = default) =>
            inner.MarkFillAppliedAsync(sessionId, fillId, canonicalHash, ct);

        public Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default) =>
            inner.AppendOrderUpdateAsync(sessionId, order, ct);

        public Task SaveLedgerJournalAsync(string sessionId, IReadOnlyList<PersistedJournalEntryDto> entries,
            CancellationToken ct = default)
        {
            if (!FailLedgerWrites)
                return inner.SaveLedgerJournalAsync(sessionId, entries, ct);
            FailedLedgerWrites++;
            throw new IOException("Synthetic financing projection snapshot outage.");
        }

        public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(CancellationToken ct = default) =>
            inner.LoadAllSessionsAsync(ct);

        public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(string sessionId, CancellationToken ct = default) =>
            inner.LoadFillsAsync(sessionId, ct);

        public Task<IReadOnlyList<PaperSessionFillRecord>> LoadFillRecordsAsync(string sessionId, CancellationToken ct = default) =>
            inner.LoadFillRecordsAsync(sessionId, ct);

        public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(string sessionId, CancellationToken ct = default) =>
            inner.LoadOrderHistoryAsync(sessionId, ct);

        public Task<PaperSessionSettlementAppendStatus> TryAppendSettlementAsync(string sessionId,
            PaperSessionSettlementRecord record, CancellationToken ct = default) => inner.TryAppendSettlementAsync(sessionId, record, ct);

        public Task<IReadOnlyList<PaperSessionSettlementRecord>> LoadSettlementsAsync(string sessionId, CancellationToken ct = default) =>
            inner.LoadSettlementsAsync(sessionId, ct);

        public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(string sessionId, CancellationToken ct = default) =>
            inner.LoadLedgerJournalAsync(sessionId, ct);
    }

    private sealed class RolloverTarget : ITradeFillLedgerPostingTarget
    {
        public RetainedTarget Inner { get; } = new();
        public int CallCount { get; private set; }

        public void UseDecemberPeriod() => Inner.Period = Inner.Period! with
        {
            PeriodId = DecemberScope.PeriodId,
            PeriodNo = 12,
            Label = "December 2026",
            StartDate = new DateOnly(2026, 12, 1),
            EndDate = new DateOnly(2026, 12, 31)
        };

        public Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(
            LedgerJournalEntryWrite write, CancellationToken ct = default)
        {
            CallCount++;
            return Inner.PostAndConfirmAsync(write, ct);
        }
    }
}
