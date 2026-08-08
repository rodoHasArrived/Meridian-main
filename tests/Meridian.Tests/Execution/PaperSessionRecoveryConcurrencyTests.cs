using System.Text.Json;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

/// <summary>
/// Paper-trade recovery scenarios guarding session-open hydration races, atomic fill claim and
/// acknowledgement, duplicate broker fills, append-before-apply crashes, and fills racing the
/// operator's session close.
/// </summary>
public sealed class PaperSessionRecoveryConcurrencyTests : IDisposable
{
    private static readonly DateTimeOffset FillTime =
        DateTimeOffset.Parse("2026-08-05T14:30:00Z");

    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "meridian-paper-recovery-" + Guid.NewGuid().ToString("N"));

    public PaperSessionRecoveryConcurrencyTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup on Windows when a failed assertion still owns a transient handle.
        }
    }

    [Fact]
    public async Task Scenario_ConcurrentSessionOpen_AllCallersAwaitOneCompleteHydration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new BlockingLoadStore();
        store.Seed(BuildSessionRecord("PAPER-RETAINED-001"));
        await using var service = CreateService(store);

        var first = service.InitialiseAsync(timeout.Token);
        await store.LoadStarted.Task.WaitAsync(timeout.Token);
        var second = service.InitialiseAsync(timeout.Token);

        service.GetSessions().Should().BeEmpty("an off-side candidate is not published piecemeal");
        store.LoadAllCallCount.Should().Be(1);

        store.ReleaseLoad.TrySetResult(true);
        await Task.WhenAll(first, second);

        service.GetSessions().Should().ContainSingle(session => session.SessionId == "PAPER-RETAINED-001");
        store.LoadAllCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Scenario_PartialHydrationFailure_NoSessionVisibleAndNextAttemptRetries()
    {
        var store = new FailOnceSecondSessionStore("PAPER-RETAINED-002");
        store.Seed(BuildSessionRecord("PAPER-RETAINED-001"));
        store.Seed(BuildSessionRecord("PAPER-RETAINED-002"));
        await using var service = CreateService(store);

        var first = () => service.InitialiseAsync();

        await first.Should().ThrowAsync<IOException>();
        service.GetSessions().Should().BeEmpty("the first session existed only in the failed candidate");

        await service.InitialiseAsync();

        service.GetSessions().Should().HaveCount(2);
        store.LoadAllCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Scenario_CreateDuringSessionOpen_WaitsForHydrationBeforePublishingCandidate()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new BlockingLoadStore();
        store.Seed(BuildSessionRecord("PAPER-RETAINED-001"));
        await using var service = CreateService(store);

        var hydration = service.InitialiseAsync(timeout.Token);
        await store.LoadStarted.Task.WaitAsync(timeout.Token);
        var create = service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-created-during-open", null, 25_000m),
            timeout.Token);

        create.IsCompleted.Should().BeFalse();
        store.SaveMetadataCallCount.Should().Be(0);

        store.ReleaseLoad.TrySetResult(true);
        await hydration;
        var created = await create;

        service.GetSessions().Should().Contain(session => session.SessionId == "PAPER-RETAINED-001");
        service.GetSessions().Should().Contain(session => session.SessionId == created.SessionId);
        store.SaveMetadataCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Scenario_PersistenceFaults_FailedCandidatesRemainInvisible()
    {
        var createStore = new FaultingMutationStore { FailMetadata = true };
        await using var createService = CreateService(createStore);
        var failedCreate = () => createService.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-create-failure", null, 10_000m));

        await failedCreate.Should().ThrowAsync<IOException>();
        createService.GetSessions().Should().BeEmpty();

        var store = new FaultingMutationStore();
        await using var service = CreateService(store);
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-mutation-failure", null, 10_000m));
        var initial = service.GetSession(session.SessionId)!;

        store.FailOrderAppend = true;
        var failedOrder = () => service.RecordOrderUpdateAsync(
            session.SessionId,
            BuildOrder("order-failed"));
        await failedOrder.Should().ThrowAsync<IOException>();
        service.GetSession(session.SessionId)!.OrderHistory.Should().BeEmpty();

        store.FailOrderAppend = false;
        store.FailFillAppend = true;
        var failedFill = () => service.RecordFillAsync(
            session.SessionId,
            BuildFill("order-failed-fill", 10m, 100m));
        await failedFill.Should().ThrowAsync<IOException>();
        var afterFillFailure = service.GetSession(session.SessionId)!;
        afterFillFailure.FillCount.Should().Be(0);
        afterFillFailure.Portfolio!.Cash.Should().Be(initial.Portfolio!.Cash);

        store.FailFillAppend = false;
        store.FailMetadata = true;
        var failedClose = () => service.CloseSessionAsync(session.SessionId);
        await failedClose.Should().ThrowAsync<IOException>();
        service.GetSession(session.SessionId)!.Summary.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Scenario_StaleLedgerSnapshot_FinalSaveFailureKeepsSessionOpenAndRestartReplaysAllFills()
    {
        var store = new ToggleLedgerSaveStore();
        string sessionId;
        int authoritativeJournalCount;
        int staleJournalCount;

        await using (var service = CreateService(store))
        {
            var session = await service.CreateSessionAsync(
                new CreatePaperSessionDto("strategy-final-ledger-durability", null, 100_000m));
            sessionId = session.SessionId;

            await service.RecordFillAsync(sessionId, BuildFill("order-ledger-stable", 10m, 100m));
            staleJournalCount = (await store.LoadLedgerJournalAsync(sessionId)).Count;
            staleJournalCount.Should().BeGreaterThan(0);

            store.FailLedgerSave = true;
            await service.RecordFillAsync(sessionId, BuildFill("order-ledger-newer", 5m, 125m));
            authoritativeJournalCount = service.GetLedger(sessionId)!.JournalEntryCount;
            authoritativeJournalCount.Should().BeGreaterThan(staleJournalCount);
            (await store.LoadLedgerJournalAsync(sessionId)).Should().HaveCount(staleJournalCount);

            var close = () => service.CloseSessionAsync(sessionId);
            await close.Should().ThrowAsync<IOException>();

            service.GetSession(sessionId)!.Summary.IsActive.Should().BeTrue();
            (await store.LoadAllSessionsAsync())
                .Should().ContainSingle(record => record.SessionId == sessionId && record.IsActive);
        }

        // Simulate process restart while the last successful snapshot is stale. Because close
        // failed before closed metadata became durable, the authoritative fill log is replayed.
        await using var restarted = CreateService(store);
        await restarted.InitialiseAsync();

        var recovered = restarted.GetSession(sessionId)!;
        recovered.Summary.IsActive.Should().BeTrue();
        recovered.FillCount.Should().Be(2);
        restarted.GetLedger(sessionId)!.JournalEntryCount.Should().Be(authoritativeJournalCount);
        (await store.LoadLedgerJournalAsync(sessionId)).Should().HaveCount(staleJournalCount);
    }

    [Fact]
    public async Task Scenario_DuplicateBrokerFill_SameClaimNoOpsAndConflictFailsClosed()
    {
        var store = CreateFileStore("duplicate");
        await using var service = CreateService(store);
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-duplicate-fill", null, 100_000m));
        var fill = BuildFill("order-duplicate", 10m, 125m);
        var fillId = PaperSessionFillRecord.ComputeCanonicalFillId(fill);

        await service.RecordFillAsync(session.SessionId, fillId, fill);
        await service.RecordFillAsync(session.SessionId, fillId, fill);

        var afterRetry = service.GetSession(session.SessionId)!;
        afterRetry.FillCount.Should().Be(1);
        afterRetry.Portfolio!.Cash.Should().Be(98_750m);

        var conflict = () => service.RecordFillAsync(
            session.SessionId,
            fillId,
            fill with { OrderQuantity = fill.OrderQuantity + 1m });
        await conflict.Should().ThrowAsync<InvalidDataException>();
        service.GetSession(session.SessionId)!.FillCount.Should().Be(1);
    }

    [Fact]
    public async Task Scenario_LegacyStore_DefaultClaimAndAcknowledgementFailClosedWithoutAppending()
    {
        IPaperSessionStore store = new LegacyPaperSessionStore();
        var fill = BuildFill("order-legacy-store", 3m, 101m);
        var record = PaperSessionFillRecord.CreateCanonical(fill, FillTime);

        Func<Task> claim = async () =>
        {
            _ = await store.TryAppendFillAsync("PAPER-LEGACY-STORE", record);
        };
        Func<Task> acknowledgement = () => store.MarkFillAppliedAsync(
            "PAPER-LEGACY-STORE",
            record.FillId,
            record.CanonicalHash);

        await claim.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*atomic durable fill-claim contract*");
        await acknowledgement.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*durable fill-acknowledgement contract*");
        ((LegacyPaperSessionStore)store).AppendFillCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Scenario_DirectFillRetry_ContentIdentityProjectsAndPersistsExactlyOnce()
    {
        var store = CreateFileStore("direct-retry");
        await using var service = CreateService(store);
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-direct-retry", null, 10_000m));
        var fill = BuildFill("order-direct-retry", 2m, 100m);

        await service.RecordFillAsync(session.SessionId, fill);
        await service.RecordFillAsync(session.SessionId, fill with { });

        var expectedId = PaperSessionFillRecord.ComputeCanonicalFillId(fill);
        (await store.LoadFillRecordsAsync(session.SessionId))
            .Should().ContainSingle(record => record.FillId == expectedId && record.IsApplied);
        var detail = service.GetSession(session.SessionId)!;
        detail.FillCount.Should().Be(1);
        detail.Portfolio!.Cash.Should().Be(9_800m);
    }

    [Fact]
    public async Task Scenario_ConcurrentDuplicateDelivery_AtomicClaimAndAcknowledgementPersistOnce()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var baseDirectory = Path.Combine(_tempDirectory, "concurrent-claim");
        var stores = new[]
        {
            new JsonlFilePaperSessionStore(
                baseDirectory,
                NullLogger<JsonlFilePaperSessionStore>.Instance),
            new JsonlFilePaperSessionStore(
                baseDirectory,
                NullLogger<JsonlFilePaperSessionStore>.Instance)
        };
        var fill = BuildFill("order-concurrent-claim", 2m, 100m);
        var record = PaperSessionFillRecord.CreateCanonical(fill, FillTime);

        var claims = await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
            stores[index % stores.Length].TryAppendFillAsync(
                "PAPER-CONCURRENT-CLAIM",
                record,
                timeout.Token)));

        claims.Should().ContainSingle(result => result.Status == PaperSessionFillAppendStatus.Added);
        claims.Should().HaveCount(16);
        claims.Count(result => result.Status == PaperSessionFillAppendStatus.ExistingSame).Should().Be(15);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(index =>
            stores[index % stores.Length].MarkFillAppliedAsync(
                "PAPER-CONCURRENT-CLAIM",
                record.FillId,
                record.CanonicalHash,
                timeout.Token)));

        (await stores[0].LoadFillRecordsAsync("PAPER-CONCURRENT-CLAIM", timeout.Token))
            .Should().ContainSingle(candidate => candidate.FillId == record.FillId && candidate.IsApplied);
        var acknowledgementPath = Path.Combine(
            baseDirectory,
            "PAPER-CONCURRENT-CLAIM",
            "fills.applied.jsonl");
        (await File.ReadAllLinesAsync(acknowledgementPath, timeout.Token)).Should().ContainSingle();
    }

    [Fact]
    public async Task Scenario_LightweightAndDirectFillPaths_IdenticalContentUsesOneCanonicalIdentity()
    {
        var store = CreateFileStore("cross-path");
        await using var service = CreateService(store);
        var lightweightSession = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-lightweight-path", null, 10_000m));
        var directSession = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-direct-path", null, 10_000m));
        var lightweightFill = new ExecutionFill
        {
            Symbol = "AAPL",
            Quantity = 2m,
            FillPrice = 100m,
            FilledAt = FillTime,
            FillType = FillType.Trade
        };

        await service.RecordPaperFillAsync(lightweightSession.SessionId, lightweightFill);
        await service.RecordPaperFillAsync(lightweightSession.SessionId, lightweightFill with { });
        var lightweightRecord = (await store.LoadFillRecordsAsync(lightweightSession.SessionId)).Single();

        await service.RecordFillAsync(directSession.SessionId, lightweightRecord.Fill with { });
        var directRecord = (await store.LoadFillRecordsAsync(directSession.SessionId)).Single();

        directRecord.FillId.Should().Be(lightweightRecord.FillId);
        directRecord.CanonicalHash.Should().Be(lightweightRecord.CanonicalHash);
        service.GetSession(lightweightSession.SessionId)!.FillCount.Should().Be(1);
        service.GetSession(directSession.SessionId)!.FillCount.Should().Be(1);
    }

    [Fact]
    public async Task Scenario_OperatorReadsSession_ReturnedCollectionsAndPortfolioAreDefensiveSnapshots()
    {
        await using var service = CreateService(new MemoryPaperSessionStore());
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-defensive-read", null, 10_000m, ["AAPL"]));
        var detail = service.GetSession(session.SessionId)!;
        var portfolio = service.GetActivePortfolio(session.SessionId)!;

        ((string[])detail.Symbols)[0] = "MUTATED";
        portfolio.ApplyFill(BuildFill("external-mutation", 1m, 100m));

        var reread = service.GetSession(session.SessionId)!;
        reread.Symbols.Should().Equal("AAPL");
        reread.FillCount.Should().Be(0);
        reread.Portfolio!.Cash.Should().Be(10_000m);
    }

    [Fact]
    public async Task Scenario_AppendBeforeApplyCrash_RestartProjectsClaimExactlyOnceAndAcknowledgesIt()
    {
        var store = CreateFileStore("append-before-apply");
        var metadata = BuildSessionRecord("PAPER-CRASH-001", initialCash: 50_000m);
        await store.SaveSessionMetadataAsync(metadata);
        var fill = BuildFill("order-crash", 5m, 200m);
        var fillId = PaperSessionFillRecord.ComputeCanonicalFillId(fill);
        var claim = PaperSessionFillRecord.Create(fillId, fill, FillTime);
        (await store.TryAppendFillAsync(metadata.SessionId, claim)).Status
            .Should().Be(PaperSessionFillAppendStatus.Added);

        await using (var recovered = CreateService(store))
        {
            await recovered.InitialiseAsync();
            var detail = recovered.GetSession(metadata.SessionId)!;
            detail.FillCount.Should().Be(1);
            detail.Portfolio!.Cash.Should().Be(49_000m);
        }

        var durableRecords = await store.LoadFillRecordsAsync(metadata.SessionId);
        durableRecords.Should().ContainSingle(record => record.FillId == fillId && record.IsApplied);

        await using var secondRestart = CreateService(store);
        await secondRestart.InitialiseAsync();
        secondRestart.GetSession(metadata.SessionId)!.FillCount.Should().Be(1);
        secondRestart.GetSession(metadata.SessionId)!.Portfolio!.Cash.Should().Be(49_000m);
    }

    [Fact]
    public async Task Scenario_ApplyAckOutage_RetryAcknowledgesWithoutProjectingFillTwice()
    {
        var store = new FailFirstAcknowledgementStore();
        await using var service = CreateService(store);
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-ack-retry", null, 10_000m));
        var fill = BuildFill("order-ack-retry", 2m, 100m);
        var fillId = PaperSessionFillRecord.ComputeCanonicalFillId(fill);

        var first = () => service.RecordFillAsync(session.SessionId, fillId, fill);
        await first.Should().ThrowAsync<IOException>();
        service.GetSession(session.SessionId)!.FillCount.Should().Be(1);

        await service.RecordFillAsync(session.SessionId, fillId, fill);

        var detail = service.GetSession(session.SessionId)!;
        detail.FillCount.Should().Be(1);
        detail.Portfolio!.Cash.Should().Be(9_800m);
        (await store.LoadFillRecordsAsync(session.SessionId))
            .Should().ContainSingle(record => record.FillId == fillId && record.IsApplied);
    }

    [Fact]
    public async Task Scenario_ProjectionFailure_DurableClaimStaysUnappliedAndRetryPublishesNoPartialState()
    {
        var store = CreateFileStore("projection-failure");
        await using var service = CreateService(store);
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-projection-failure", null, 10_000m));
        var invalidFill = BuildFill("order-invalid-price", 1m, -1m);
        var fillId = PaperSessionFillRecord.ComputeCanonicalFillId(invalidFill);

        var first = () => service.RecordFillAsync(session.SessionId, fillId, invalidFill);
        await first.Should().ThrowAsync<LedgerValidationException>();
        var afterFirst = service.GetSession(session.SessionId)!;
        afterFirst.FillCount.Should().Be(0);
        afterFirst.Portfolio!.Cash.Should().Be(10_000m);
        (await store.LoadFillRecordsAsync(session.SessionId))
            .Should().ContainSingle(record => record.FillId == fillId && !record.IsApplied);

        var retry = () => service.RecordFillAsync(session.SessionId, fillId, invalidFill);
        await retry.Should().ThrowAsync<LedgerValidationException>();
        service.GetSession(session.SessionId)!.FillCount.Should().Be(0);

        await using var restarted = CreateService(store);
        var restart = () => restarted.InitialiseAsync();
        await restart.Should().ThrowAsync<LedgerValidationException>();
        restarted.GetSessions().Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_FillClaimsGateBeforeClose_FillCommitsThenSessionCloses()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new BlockingFillStore();
        await using var service = CreateService(store);
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-fill-wins-close", null, 10_000m),
            timeout.Token);

        var fill = service.RecordFillAsync(
            session.SessionId,
            BuildFill("order-before-close", 2m, 100m),
            timeout.Token);
        await store.FillClaimed.Task.WaitAsync(timeout.Token);
        var close = service.CloseSessionAsync(session.SessionId, timeout.Token);
        close.IsCompleted.Should().BeFalse();

        store.ReleaseFill.TrySetResult(true);
        await fill;
        (await close).Should().BeTrue();

        var detail = service.GetSession(session.SessionId)!;
        detail.FillCount.Should().Be(1);
        detail.Summary.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Scenario_CloseClaimsGateBeforeFill_CloseCommitsAndLateFillIsIgnored()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new BlockingCloseStore();
        await using var service = CreateService(store);
        var session = await service.CreateSessionAsync(
            new CreatePaperSessionDto("strategy-close-wins-fill", null, 10_000m),
            timeout.Token);

        var close = service.CloseSessionAsync(session.SessionId, timeout.Token);
        await store.ClosePersisting.Task.WaitAsync(timeout.Token);
        var fill = service.RecordFillAsync(
            session.SessionId,
            BuildFill("order-after-close", 2m, 100m),
            timeout.Token);
        fill.IsCompleted.Should().BeFalse();

        store.ReleaseClose.TrySetResult(true);
        (await close).Should().BeTrue();
        await fill;

        var detail = service.GetSession(session.SessionId)!;
        detail.FillCount.Should().Be(0);
        detail.Summary.IsActive.Should().BeFalse();
        (await store.LoadFillRecordsAsync(session.SessionId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Scenario_LegacyRawFillLog_ReloadsWithDeterministicCompatibilityIdentity()
    {
        var store = CreateFileStore("legacy");
        var metadata = BuildSessionRecord("PAPER-LEGACY-001", initialCash: 20_000m);
        await store.SaveSessionMetadataAsync(metadata);
        var legacyFill = BuildFill("order-legacy", 4m, 250m);
        var fillsPath = Path.Combine(store.BaseDirectory, metadata.SessionId, "fills.jsonl");
        await File.WriteAllTextAsync(
            fillsPath,
            JsonSerializer.Serialize(legacyFill) + Environment.NewLine);

        await using var service = CreateService(store);
        await service.InitialiseAsync();

        var detail = service.GetSession(metadata.SessionId)!;
        detail.FillCount.Should().Be(1);
        detail.Portfolio!.Cash.Should().Be(19_000m);
        var records = await store.LoadFillRecordsAsync(metadata.SessionId);
        var expectedId = PaperSessionFillRecord.ComputeCanonicalFillId(legacyFill);
        records.Should().ContainSingle(record => record.FillId == expectedId && record.IsApplied);
    }

    [Fact]
    public async Task Scenario_CancelledHydration_NextInitialisationAttemptCanRetry()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new CancelFirstLoadStore();
        store.Seed(BuildSessionRecord("PAPER-RETRY-001"));
        await using var service = CreateService(store);

        var first = service.InitialiseAsync(cancellation.Token);
        await store.FirstLoadStarted.Task;
        await cancellation.CancelAsync();
        var cancelled = async () => await first;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        service.GetSessions().Should().BeEmpty();

        await service.InitialiseAsync();

        service.GetSessions().Should().ContainSingle(session => session.SessionId == "PAPER-RETRY-001");
        store.LoadAllCallCount.Should().Be(2);
    }

    [Fact]
    public async Task Scenario_DisposeDuringHydration_CancelsAttemptAndRejectsNewReaders()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var store = new BlockingLoadStore();
        store.Seed(BuildSessionRecord("PAPER-DISPOSE-001"));
        var service = CreateService(store);

        var initialisation = service.InitialiseAsync(timeout.Token);
        await store.LoadStarted.Task.WaitAsync(timeout.Token);
        await service.DisposeAsync();

        var cancelled = async () => await initialisation;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        Action read = () => _ = service.GetSessions();
        read.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Scenario_FillIdentitySerializationRoundTrip_DerivesSameOmsIdentityAndEnvelopeId()
    {
        var fill = BuildFill("order-identity", 7.5m, 321.125m) with
        {
            GatewayOrderId = "gateway-42",
            ClientOrderId = "client-42",
            Commission = 1.25m
        };
        var before = PaperSessionFillRecord.ComputeCanonicalFillId(fill);
        var reloaded = JsonSerializer.Deserialize<ExecutionReport>(JsonSerializer.Serialize(fill))!;
        var after = PaperSessionFillRecord.ComputeCanonicalFillId(reloaded);
        var omsFirstAccount = OrderManagementSystem.CreateDeterministicFillId(
            reloaded,
            cumulativeFilledQuantity: 12.5m,
            financialAccountId: "fund-42");
        var omsSecondAccount = OrderManagementSystem.CreateDeterministicFillId(
            reloaded,
            cumulativeFilledQuantity: reloaded.FilledQuantity,
            financialAccountId: "fund-99");

        after.Should().Be(before);
        omsFirstAccount.Should().Be(before);
        omsSecondAccount.Should().Be(before);
        var envelope = PaperSessionFillRecord.Create(before, reloaded, FillTime);
        var store = CreateFileStore("identity-roundtrip");
        var metadata = BuildSessionRecord("PAPER-IDENTITY-001");
        await store.SaveSessionMetadataAsync(metadata);
        await store.TryAppendFillAsync(metadata.SessionId, envelope);

        (await store.LoadFillRecordsAsync(metadata.SessionId))
            .Should().ContainSingle(record =>
                record.FillId == before
                && record.CanonicalHash == PaperSessionFillRecord.ComputeCanonicalHash(reloaded));
    }

    private JsonlFilePaperSessionStore CreateFileStore(string name) => new(
        Path.Combine(_tempDirectory, name),
        NullLogger<JsonlFilePaperSessionStore>.Instance);

    private static PaperSessionPersistenceService CreateService(IPaperSessionStore store) => new(
        NullLogger<PaperSessionPersistenceService>.Instance,
        store);

    private static PersistedSessionRecord BuildSessionRecord(
        string sessionId,
        decimal initialCash = 100_000m) => new(
        sessionId,
        "strategy-retained",
        "Retained strategy",
        initialCash,
        DateTimeOffset.Parse("2026-08-05T13:00:00Z"),
        ClosedAt: null,
        IsActive: true,
        Symbols: ["AAPL"]);

    private static ExecutionReport BuildFill(string orderId, decimal quantity, decimal price) => new()
    {
        OrderId = orderId,
        ReportType = ExecutionReportType.Fill,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = price,
        Timestamp = FillTime
    };

    private static OrderState BuildOrder(string orderId) => new()
    {
        OrderId = orderId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Quantity = 10m,
        Status = Meridian.Execution.Sdk.OrderStatus.Accepted,
        CreatedAt = FillTime.AddMinutes(-1)
    };

    private class MemoryPaperSessionStore : IPaperSessionStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, PersistedSessionRecord> _sessions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<PaperSessionFillRecord>> _fills = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<OrderState>> _orders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<PersistedJournalEntryDto>> _ledgers = new(StringComparer.Ordinal);

        public int LoadAllCallCount;
        public int SaveMetadataCallCount;

        public void Seed(PersistedSessionRecord record)
        {
            lock (_sync)
                _sessions[record.SessionId] = record;
        }

        public virtual Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref SaveMetadataCallCount);
            lock (_sync)
                _sessions[record.SessionId] = record;
            return Task.CompletedTask;
        }

        public virtual async Task AppendFillAsync(
            string sessionId,
            ExecutionReport fill,
            CancellationToken ct = default)
        {
            var record = PaperSessionFillRecord.CreateCanonical(
                fill,
                fill.Timestamp);
            var result = await TryAppendFillAsync(sessionId, record, ct);
            if (result.Status == PaperSessionFillAppendStatus.Conflict)
                throw new InvalidDataException("Conflicting in-memory paper fill.");
        }

        public virtual Task<PaperSessionFillAppendResult> TryAppendFillAsync(
            string sessionId,
            PaperSessionFillRecord record,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!_fills.TryGetValue(sessionId, out var fills))
                    _fills[sessionId] = fills = [];
                var existing = fills.FirstOrDefault(candidate => candidate.FillId == record.FillId);
                if (existing is not null)
                {
                    return Task.FromResult(new PaperSessionFillAppendResult(
                        existing.CanonicalHash == record.CanonicalHash
                            ? PaperSessionFillAppendStatus.ExistingSame
                            : PaperSessionFillAppendStatus.Conflict,
                        existing.CanonicalHash));
                }

                fills.Add(record with { IsApplied = false });
                return Task.FromResult(new PaperSessionFillAppendResult(PaperSessionFillAppendStatus.Added));
            }
        }

        public virtual Task MarkFillAppliedAsync(
            string sessionId,
            Guid fillId,
            string canonicalHash,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                var fills = _fills.GetValueOrDefault(sessionId)
                    ?? throw new InvalidDataException("Unknown in-memory paper session fill.");
                var index = fills.FindIndex(record => record.FillId == fillId);
                if (index < 0 || fills[index].CanonicalHash != canonicalHash)
                    throw new InvalidDataException("Conflicting in-memory paper fill acknowledgement.");
                fills[index] = fills[index] with { IsApplied = true };
            }

            return Task.CompletedTask;
        }

        public virtual Task AppendOrderUpdateAsync(
            string sessionId,
            OrderState order,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!_orders.TryGetValue(sessionId, out var orders))
                    _orders[sessionId] = orders = [];
                orders.Add(order);
            }

            return Task.CompletedTask;
        }

        public virtual Task SaveLedgerJournalAsync(
            string sessionId,
            IReadOnlyList<PersistedJournalEntryDto> entries,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
                _ledgers[sessionId] = entries.ToArray();
            return Task.CompletedTask;
        }

        public virtual Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref LoadAllCallCount);
            lock (_sync)
                return Task.FromResult<IReadOnlyList<PersistedSessionRecord>>(_sessions.Values.ToArray());
        }

        public virtual Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<ExecutionReport>>(
                    _fills.GetValueOrDefault(sessionId)?.Select(record => record.Fill).ToArray() ?? []);
            }
        }

        public virtual Task<IReadOnlyList<PaperSessionFillRecord>> LoadFillRecordsAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<PaperSessionFillRecord>>(
                    _fills.GetValueOrDefault(sessionId)?.ToArray() ?? []);
            }
        }

        public virtual Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<OrderState>>(
                    _orders.GetValueOrDefault(sessionId)?.ToArray() ?? []);
            }
        }

        public virtual Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult(
                    _ledgers.GetValueOrDefault(sessionId) ?? (IReadOnlyList<PersistedJournalEntryDto>)[]);
            }
        }
    }

    private sealed class LegacyPaperSessionStore : IPaperSessionStore
    {
        public int AppendFillCallCount { get; private set; }

        public Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task AppendFillAsync(
            string sessionId,
            ExecutionReport fill,
            CancellationToken ct = default)
        {
            AppendFillCallCount++;
            return Task.CompletedTask;
        }

        public Task AppendOrderUpdateAsync(
            string sessionId,
            OrderState order,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveLedgerJournalAsync(
            string sessionId,
            IReadOnlyList<PersistedJournalEntryDto> entries,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([]);

        public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExecutionReport>>([]);

        public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrderState>>([]);

        public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
    }

    private sealed class BlockingLoadStore : MemoryPaperSessionStore
    {
        public TaskCompletionSource<bool> LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseLoad { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref LoadAllCallCount);
            LoadStarted.TrySetResult(true);
            await ReleaseLoad.Task.WaitAsync(ct);
            return await LoadAllWithoutCountingAsync(ct);
        }

        private async Task<IReadOnlyList<PersistedSessionRecord>> LoadAllWithoutCountingAsync(CancellationToken ct)
        {
            var before = LoadAllCallCount;
            var records = await base.LoadAllSessionsAsync(ct);
            Interlocked.Exchange(ref LoadAllCallCount, before);
            return records;
        }
    }

    private sealed class FailOnceSecondSessionStore(string failingSessionId) : MemoryPaperSessionStore
    {
        private int _failurePending = 1;

        public override Task<IReadOnlyList<PaperSessionFillRecord>> LoadFillRecordsAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            if (sessionId == failingSessionId && Interlocked.Exchange(ref _failurePending, 0) == 1)
                return Task.FromException<IReadOnlyList<PaperSessionFillRecord>>(new IOException("simulated hydration failure"));
            return base.LoadFillRecordsAsync(sessionId, ct);
        }
    }

    private sealed class FaultingMutationStore : MemoryPaperSessionStore
    {
        public bool FailMetadata { get; set; }
        public bool FailOrderAppend { get; set; }
        public bool FailFillAppend { get; set; }

        public override Task SaveSessionMetadataAsync(PersistedSessionRecord record, CancellationToken ct = default) =>
            FailMetadata
                ? Task.FromException(new IOException("simulated metadata failure"))
                : base.SaveSessionMetadataAsync(record, ct);

        public override Task AppendOrderUpdateAsync(string sessionId, OrderState order, CancellationToken ct = default) =>
            FailOrderAppend
                ? Task.FromException(new IOException("simulated order append failure"))
                : base.AppendOrderUpdateAsync(sessionId, order, ct);

        public override Task<PaperSessionFillAppendResult> TryAppendFillAsync(
            string sessionId,
            PaperSessionFillRecord record,
            CancellationToken ct = default) =>
            FailFillAppend
                ? Task.FromException<PaperSessionFillAppendResult>(new IOException("simulated fill append failure"))
                : base.TryAppendFillAsync(sessionId, record, ct);
    }

    private sealed class ToggleLedgerSaveStore : MemoryPaperSessionStore
    {
        public bool FailLedgerSave { get; set; }

        public override Task SaveLedgerJournalAsync(
            string sessionId,
            IReadOnlyList<PersistedJournalEntryDto> entries,
            CancellationToken ct = default) =>
            FailLedgerSave
                ? Task.FromException(new IOException("simulated final ledger snapshot failure"))
                : base.SaveLedgerJournalAsync(sessionId, entries, ct);
    }

    private sealed class BlockingFillStore : MemoryPaperSessionStore
    {
        public TaskCompletionSource<bool> FillClaimed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseFill { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<PaperSessionFillAppendResult> TryAppendFillAsync(
            string sessionId,
            PaperSessionFillRecord record,
            CancellationToken ct = default)
        {
            var result = await base.TryAppendFillAsync(sessionId, record, ct);
            FillClaimed.TrySetResult(true);
            await ReleaseFill.Task.WaitAsync(ct);
            return result;
        }
    }

    private sealed class FailFirstAcknowledgementStore : MemoryPaperSessionStore
    {
        private int _failurePending = 1;

        public override Task MarkFillAppliedAsync(
            string sessionId,
            Guid fillId,
            string canonicalHash,
            CancellationToken ct = default) =>
            Interlocked.Exchange(ref _failurePending, 0) == 1
                ? Task.FromException(new IOException("simulated fill acknowledgement outage"))
                : base.MarkFillAppliedAsync(sessionId, fillId, canonicalHash, ct);
    }

    private sealed class BlockingCloseStore : MemoryPaperSessionStore
    {
        public TaskCompletionSource<bool> ClosePersisting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseClose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task SaveSessionMetadataAsync(
            PersistedSessionRecord record,
            CancellationToken ct = default)
        {
            if (!record.IsActive)
            {
                ClosePersisting.TrySetResult(true);
                await ReleaseClose.Task.WaitAsync(ct);
            }

            await base.SaveSessionMetadataAsync(record, ct);
        }
    }

    private sealed class CancelFirstLoadStore : MemoryPaperSessionStore
    {
        private int _attempt;
        private readonly TaskCompletionSource<bool> _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> FirstLoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _attempt) == 1)
            {
                Interlocked.Increment(ref LoadAllCallCount);
                FirstLoadStarted.TrySetResult(true);
                await _never.Task.WaitAsync(ct);
            }

            return await base.LoadAllSessionsAsync(ct);
        }
    }
}
