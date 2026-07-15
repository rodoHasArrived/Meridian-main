using System.Diagnostics;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for the Event-Driven Decoupling types (Phase 1).
/// Validates <see cref="TradeExecutedEvent"/> and <see cref="LedgerPostingConsumer"/>, including
/// the WAL crash-recovery failure mode for accepted market fills during bounded shutdown.
/// </summary>
public sealed class EventDrivenDecouplingTests
{
    private const string TestPostingScope = "test-ledger/open-period";

    // -------------------------------------------------------------------------
    // TradeExecutedEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void TradeExecutedEvent_GrossValue_IsQuantityTimesPrice()
    {
        var evt = new TradeExecutedEvent(
            FillId: Guid.NewGuid(),
            OrderId: "ord-1",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            FilledQuantity: 100,
            FillPrice: 150m,
            Commission: 1m,
            RealizedPnl: 0m,
            NewCash: 85_000m,
            OccurredAt: DateTimeOffset.UtcNow);

        evt.GrossValue.Should().Be(100 * 150m);
    }

    [Fact]
    public void TradeExecutedEvent_FinancialAccountId_DefaultsToNull()
    {
        var evt = new TradeExecutedEvent(Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            100, 150m, 0m, 0m, 85_000m, DateTimeOffset.UtcNow);

        evt.FinancialAccountId.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // LedgerPostingConsumer — synchronous equivalence via async drain
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LedgerPostingConsumer_BuyEvent_PostsSecuritiesAndCashEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());

        var evt = new TradeExecutedEvent(
            FillId: Guid.NewGuid(),
            OrderId: "ord-1",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            FilledQuantity: 100,
            FillPrice: 150m,
            Commission: 0m,
            RealizedPnl: 0m,
            NewCash: 85_000m,
            OccurredAt: DateTimeOffset.UtcNow);

        consumer.Publish(evt);
        await consumer.DisposeAsync(); // flushes and drains channel

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Single(l => l.Account == LedgerAccounts.Securities("AAPL")).Debit.Should()
            .Be(100 * 150m);
        entry.Lines.Single(l => l.Account == LedgerAccounts.Cash).Credit.Should()
            .Be(100 * 150m);
        entry.Metadata.SecurityId.Should().Be(PassingSecurityValidationGate.SecurityId);
        entry.Metadata.Symbol.Should().Be("AAPL");
        entry.Metadata.FillId.Should().Be(evt.FillId);
        entry.Metadata.Tags.Should().ContainKey("securityMaster.gate")
            .WhoseValue.Should().Be("resolved-approved-mapped");
    }

    [Fact]
    public async Task LedgerPostingConsumer_BuyWithCommission_PostsThreeEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            50, 200m, Commission: 5m, RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(2); // buy + commission
        ledger.Journal.Should().Contain(e => e.Description.Contains("Commission"));
    }

    [Fact]
    public async Task LedgerPostingConsumer_SellWithGain_PostsRealizedGainEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-2", "AAPL", OrderSide.Sell,
            FilledQuantity: 100, FillPrice: 160m, Commission: 0m,
            RealizedPnl: 1_000m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedGain && l.Credit == 1_000m);
    }

    [Fact]
    public async Task LedgerPostingConsumer_SellWithLoss_PostsRealizedLossEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-3", "MSFT", OrderSide.Sell,
            FilledQuantity: 100, FillPrice: 140m, Commission: 0m,
            RealizedPnl: -1_000m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedLoss && l.Debit == 1_000m);
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenSecurityValidationBlocks_DoesNotPostJournalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new BlockingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-blocked", "AAPL", OrderSide.Buy,
            FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
            RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty("blocked Security Master validation must prevent ledger posting");
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenSecurityValidationGateMissing_DoesNotPostJournalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope);

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-ungated", "AAPL", OrderSide.Buy,
            FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
            RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty("ledger posting now requires a configured Security Master gate");
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenSecurityIsUnresolved_DoesNotPostJournalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new UnresolvedSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-unresolved", "MISSING", OrderSide.Buy,
            FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
            RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty("ledger posting requires a resolved Security Master identity");
    }

    // -------------------------------------------------------------------------
    // LedgerPostingConsumer — fill durability under backpressure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LedgerPostingConsumer_WhenChannelIsFull_BlocksPublisherInsteadOfDroppingFills()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var gate = new BlockableSecurityValidationGate();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            channelCapacity: 1,
            securityValidationGate: gate);

        consumer.Publish(MakeBuyEvent("AAPL"));
        await gate.FirstValidationStarted; // consumer is now stalled inside the gate
        consumer.Publish(MakeBuyEvent("MSFT")); // fills the single-slot channel

        var blockedPublish = Task.Run(() => consumer.Publish(MakeBuyEvent("GOOG")));
        var winner = await Task.WhenAny(blockedPublish, Task.Delay(TimeSpan.FromMilliseconds(250)));
        winner.Should().NotBe(
            blockedPublish,
            "a publish against a full channel must block for capacity rather than drop the fill");

        gate.Release();
        await blockedPublish;
        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(3, "every published fill must reach the ledger");
    }

    [Fact]
    public async Task LedgerPostingConsumer_PublishAfterDispose_ThrowsInsteadOfSilentlyDropping()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());

        await consumer.DisposeAsync();

        var publish = () => consumer.Publish(MakeBuyEvent("AAPL"));
        publish.Should().Throw<ChannelClosedException>(
            "a disposed consumer must reject fills loudly, never drop them");
    }

    [Fact]
    public async Task Scenario_WalCrashRecovery_AcceptedFillsReplayAfterBoundedShutdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fill-posting-tests", Guid.NewGuid().ToString("N"));
        try
        {
            {
                var firstLedger = new Meridian.Ledger.Ledger();
                await using var firstStore = new WalTradeFillPostingStore(
                    new TradeFillPostingStoreOptions(root, TestPostingScope),
                    NullLogger<WalTradeFillPostingStore>.Instance);
                var gate = new NonCooperativeSecurityValidationGate();
                var consumer = new LedgerPostingConsumer(
                    firstLedger,
                    NullLogger<LedgerPostingConsumer>.Instance,
                    firstStore,
                    TestPostingScope,
                    channelCapacity: 1,
                    securityValidationGate: gate,
                    drainTimeout: TimeSpan.FromMilliseconds(25),
                    cancellationTimeout: TimeSpan.FromMilliseconds(25));

                consumer.Publish(MakeBuyEvent("AAPL"));
                await gate.ValidationStarted.WaitAsync(TimeSpan.FromSeconds(1));
                consumer.Publish(MakeBuyEvent("MSFT"));
                var blockedPublish = Task.Run(() => consumer.Publish(MakeBuyEvent("GOOG")));
                await WaitForPendingCountAsync(firstStore, 3);
                blockedPublish.IsCompleted.Should().BeFalse("the channel is saturated before disposal begins");

                var elapsed = Stopwatch.StartNew();
                await consumer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
                elapsed.Stop();

                elapsed.Elapsed.Should().BeLessThan(
                    TimeSpan.FromMilliseconds(750),
                    "shutdown has finite drain and cancellation phases even when a dependency ignores cancellation");
                Func<Task> blockedPublishAction = async () => await blockedPublish;
                await blockedPublishAction.Should().ThrowAsync<ChannelClosedException>(
                    "the publisher is told that the live consumer closed even though the accepted fill is durable");

                gate.Release();
                Func<Task> processingAction = async () => await consumer.ProcessingCompletion;
                await processingAction.Should().ThrowAsync<OperationCanceledException>();
                firstLedger.Journal.Should().BeEmpty(
                    "an in-flight validation that returns after the disposal boundary cannot mutate the ledger");
                (await firstStore.LoadPendingAsync()).Should().HaveCount(3);
            }

            var recoveredLedger = new Meridian.Ledger.Ledger();
            await using var recoveredStore = new WalTradeFillPostingStore(
                new TradeFillPostingStoreOptions(root, TestPostingScope),
                NullLogger<WalTradeFillPostingStore>.Instance);
            var recoveredConsumer = new LedgerPostingConsumer(
                recoveredLedger,
                NullLogger<LedgerPostingConsumer>.Instance,
                recoveredStore,
                TestPostingScope,
                channelCapacity: 1,
                securityValidationGate: new PassingSecurityValidationGate());

            await recoveredConsumer.DisposeAsync();

            recoveredLedger.Journal.Should().HaveCount(3,
                "every durably accepted fill must replay after a forced bounded shutdown");
            (await recoveredStore.LoadPendingAsync()).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scenario_TransientPostingDependencyFailure_ReplayPostsWithoutDuplicateJournals()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);
        var gate = new FailOnceSecurityValidationGate();
        var evt = MakeBuyEvent("AAPL") with { Commission = 2m };
        var firstConsumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingScope,
            securityValidationGate: gate);

        firstConsumer.Publish(evt);
        await firstConsumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty();
        var pendingAfterFailure = await store.LoadPendingAsync();
        pendingAfterFailure.Should().ContainSingle();
        pendingAfterFailure[0].FailureCount.Should().Be(1);

        var recoveredConsumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingScope,
            securityValidationGate: gate);
        await recoveredConsumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(2, "trade and commission post once after retry");
        (await store.LoadPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerPostingConsumer_DuplicateFill_IsAcknowledgedOnce()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());
        var evt = MakeBuyEvent("AAPL");

        consumer.Publish(evt);
        consumer.Publish(evt);
        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle();
        (await store.LoadPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenAcknowledgementFails_ReplayDoesNotDuplicateExistingJournal()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var store = new FailOnceAcknowledgementStore(TestPostingScope);
        var evt = MakeBuyEvent("AAPL");
        var firstConsumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());

        firstConsumer.Publish(evt);
        await firstConsumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle("ledger mutation precedes durable acknowledgement");
        (await store.LoadPendingAsync()).Should().ContainSingle();

        var recoveredConsumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());
        await recoveredConsumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle(
            "replay must detect the journal already carrying the fill id before acknowledging");
        (await store.LoadPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerPostingConsumer_ConcurrentDisposeCalls_AwaitSameBoundedShutdown()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: new PassingSecurityValidationGate());
        consumer.Publish(MakeBuyEvent("AAPL"));

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => consumer.DisposeAsync().AsTask()));

        ledger.Journal.Should().ContainSingle();
    }

    [Fact]
    public async Task LedgerPostingConsumer_WhenCancellationCallbackStalls_DisposeRemainsBounded()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var gate = new BlockingCancellationCallbackSecurityValidationGate();
        var consumer = new LedgerPostingConsumer(
            ledger,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingScope,
            securityValidationGate: gate,
            drainTimeout: TimeSpan.FromMilliseconds(25),
            cancellationTimeout: TimeSpan.FromMilliseconds(25));
        consumer.Publish(MakeBuyEvent("AAPL"));
        await gate.ValidationStarted.WaitAsync(TimeSpan.FromSeconds(1));

        var elapsed = Stopwatch.StartNew();
        var disposeTask = consumer.DisposeAsync().AsTask();
        await gate.CancellationCallbackStarted.WaitAsync(TimeSpan.FromSeconds(1));
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(1));
        elapsed.Stop();

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750));
        ledger.Journal.Should().BeEmpty();

        gate.ReleaseCancellationCallback();
        gate.ReleaseValidation();
        Func<Task> processingAction = async () => await consumer.ProcessingCompletion;
        await processingAction.Should().ThrowAsync<OperationCanceledException>();
        ledger.Journal.Should().BeEmpty("no validation may cross the closed posting boundary");
    }

    private static async Task WaitForPendingCountAsync(ITradeFillPostingStore store, int expected)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while ((await store.LoadPendingAsync()).Count < expected)
        {
            if (DateTimeOffset.UtcNow >= timeoutAt)
                throw new TimeoutException($"Timed out waiting for {expected} pending trade fills.");
            await Task.Delay(10);
        }
    }

    private static TradeExecutedEvent MakeBuyEvent(string symbol) => new(
        Guid.NewGuid(), "ord-bp", symbol, OrderSide.Buy,
        FilledQuantity: 10, FillPrice: 100m, Commission: 0m,
        RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow);

    /// <summary>
    /// Passing gate whose first validation stalls until <see cref="Release"/> is called,
    /// letting tests hold the consumer mid-event while the channel backs up.
    /// </summary>
    private sealed class BlockableSecurityValidationGate : ISecurityValidationGateService
    {
        private readonly PassingSecurityValidationGate _inner = new();
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _firstValidationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstValidationStarted => _firstValidationStarted.Task;

        public void Release() => _released.TrySetResult();

        public async Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            _firstValidationStarted.TrySetResult();
            await _released.Task.WaitAsync(ct);
            return await _inner.ValidateSymbolAsync(symbol, workflow, workflowReference, actor, persistSnapshot, ct);
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => _inner.ValidateSecurityAsync(securityId, workflow, workflowReference, actor, persistSnapshot, symbol, ct);
    }

    /// <summary>
    /// Deliberately ignores the supplied cancellation token while validation is blocked. This
    /// models a provider or remote dependency that does not cooperate with shutdown promptly.
    /// </summary>
    private sealed class NonCooperativeSecurityValidationGate : ISecurityValidationGateService
    {
        private readonly PassingSecurityValidationGate _inner = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _validationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ValidationStarted => _validationStarted.Task;

        public void Release() => _release.TrySetResult();

        public async Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            _validationStarted.TrySetResult();
            await _release.Task;
            return await _inner.ValidateSymbolAsync(
                symbol,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                CancellationToken.None);
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => _inner.ValidateSecurityAsync(
                securityId,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                symbol,
                CancellationToken.None);
    }

    private sealed class FailOnceSecurityValidationGate : ISecurityValidationGateService
    {
        private readonly PassingSecurityValidationGate _inner = new();
        private int _remainingFailures = 1;

        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
                throw new InvalidOperationException("simulated transient Security Master failure");

            return _inner.ValidateSymbolAsync(symbol, workflow, workflowReference, actor, persistSnapshot, ct);
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => _inner.ValidateSecurityAsync(
                securityId,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                symbol,
                ct);
    }

    private sealed class BlockingCancellationCallbackSecurityValidationGate : ISecurityValidationGateService
    {
        private readonly PassingSecurityValidationGate _inner = new();
        private readonly ManualResetEventSlim _releaseCancellationCallback = new(initialState: false);
        private readonly TaskCompletionSource _cancellationCallbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseValidation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _validationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CancellationCallbackStarted => _cancellationCallbackStarted.Task;

        public Task ValidationStarted => _validationStarted.Task;

        public void ReleaseCancellationCallback() => _releaseCancellationCallback.Set();

        public void ReleaseValidation() => _releaseValidation.TrySetResult();

        public async Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
        {
            using var registration = ct.Register(() =>
            {
                _cancellationCallbackStarted.TrySetResult();
                _releaseCancellationCallback.Wait();
            });
            _validationStarted.TrySetResult();
            await _releaseValidation.Task;
            return await _inner.ValidateSymbolAsync(
                symbol,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                CancellationToken.None);
        }

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => _inner.ValidateSecurityAsync(
                securityId,
                workflow,
                workflowReference,
                actor,
                persistSnapshot,
                symbol,
                ct);
    }

    private sealed class InMemoryTradeFillPostingStore(string postingScope) : ITradeFillPostingStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, PendingTradeFillPosting> _pending = [];
        private readonly HashSet<Guid> _posted = [];
        private long _sequence;

        public string PostingScope { get; } = postingScope;

        public Task<TradeFillPostingAcceptance> AcceptAsync(
            TradeExecutedEvent tradeEvent,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_posted.Contains(tradeEvent.FillId))
                {
                    return Task.FromResult(
                        new TradeFillPostingAcceptance(null, ShouldEnqueue: false, WasAlreadyPosted: true));
                }

                if (_pending.TryGetValue(tradeEvent.FillId, out var retained))
                {
                    if (retained.TradeEvent != tradeEvent)
                        throw new InvalidOperationException("A fill id cannot identify different economics.");

                    return Task.FromResult(
                        new TradeFillPostingAcceptance(retained, ShouldEnqueue: false, WasAlreadyPosted: false));
                }

                var posting = new PendingTradeFillPosting(
                    Interlocked.Increment(ref _sequence),
                    PostingScope,
                    tradeEvent,
                    DateTimeOffset.UtcNow);
                _pending.Add(tradeEvent.FillId, posting);
                return Task.FromResult(
                    new TradeFillPostingAcceptance(posting, ShouldEnqueue: true, WasAlreadyPosted: false));
            }
        }

        public Task<IReadOnlyList<PendingTradeFillPosting>> LoadPendingAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                IReadOnlyList<PendingTradeFillPosting> result = _pending.Values
                    .OrderBy(static posting => posting.StoreSequence)
                    .ToArray();
                return Task.FromResult(result);
            }
        }

        public Task MarkPostedAsync(Guid fillId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_posted.Contains(fillId))
                    return Task.CompletedTask;
                if (!_pending.Remove(fillId))
                    throw new InvalidOperationException("Only pending fills can be acknowledged.");
                _posted.Add(fillId);
                return Task.CompletedTask;
            }
        }

        public Task RecordFailureAsync(Guid fillId, string failure, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_posted.Contains(fillId))
                    return Task.CompletedTask;
                if (!_pending.TryGetValue(fillId, out var posting))
                    throw new InvalidOperationException("Only pending fills can record a failure.");
                _pending[fillId] = posting with
                {
                    FailureCount = posting.FailureCount + 1,
                    LastFailure = failure,
                    LastAttemptAtUtc = DateTimeOffset.UtcNow
                };
                return Task.CompletedTask;
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailOnceAcknowledgementStore(string postingScope) : ITradeFillPostingStore
    {
        private readonly InMemoryTradeFillPostingStore _inner = new(postingScope);
        private int _remainingFailures = 1;

        public string PostingScope => _inner.PostingScope;

        public Task<TradeFillPostingAcceptance> AcceptAsync(
            TradeExecutedEvent tradeEvent,
            CancellationToken ct = default)
            => _inner.AcceptAsync(tradeEvent, ct);

        public Task<IReadOnlyList<PendingTradeFillPosting>> LoadPendingAsync(CancellationToken ct = default)
            => _inner.LoadPendingAsync(ct);

        public Task MarkPostedAsync(Guid fillId, CancellationToken ct = default)
        {
            if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
                throw new IOException("simulated acknowledgement write failure");

            return _inner.MarkPostedAsync(fillId, ct);
        }

        public Task RecordFailureAsync(Guid fillId, string failure, CancellationToken ct = default)
            => _inner.RecordFailureAsync(fillId, failure, ct);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private sealed class PassingSecurityValidationGate : ISecurityValidationGateService
    {
        public static readonly Guid SecurityId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol, workflow));

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol ?? securityId.ToString(), workflow));

        private static SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                SecurityId,
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: false,
                CriticalIssueCount: 0,
                ErrorIssueCount: 0,
                Issues: []);

            return new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                report.SecurityId,
                IsResolved: true,
                IsBlocked: false,
                Report: report,
                Snapshot: null);
        }
    }

    private sealed class BlockingSecurityValidationGate : ISecurityValidationGateService
    {
        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol, workflow));

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol ?? securityId.ToString(), workflow));

        private static SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                SecurityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Scope: "Security",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Error,
                        "SM_ACCOUNTING_CLASSIFICATION_MISSING",
                        "Accounting classification is missing",
                        "The record does not expose an accounting classification for ledger posting.",
                        ["commonTerms.accountingClassification"],
                        "Attach the ledger/reporting accounting classification.",
                        [])
                ]);

            return new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                report.SecurityId,
                IsResolved: true,
                IsBlocked: true,
                Report: report,
                Snapshot: null);
        }
    }

    private sealed class UnresolvedSecurityValidationGate : ISecurityValidationGateService
    {
        public Task<SecurityValidationGateResultDto> ValidateSymbolAsync(
            string symbol,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol, workflow));

        public Task<SecurityValidationGateResultDto> ValidateSecurityAsync(
            Guid securityId,
            SecurityValidationWorkflowDto workflow,
            string? workflowReference = null,
            string? actor = null,
            bool persistSnapshot = false,
            string? symbol = null,
            CancellationToken ct = default)
            => Task.FromResult(BuildResult(symbol ?? securityId.ToString(), workflow));

        private static SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                SecurityId: null,
                Scope: "SecurityMasterResolution",
                EvaluatedAtUtc: DateTimeOffset.UtcNow,
                HasBlockingIssues: true,
                CriticalIssueCount: 0,
                ErrorIssueCount: 1,
                Issues:
                [
                    new SecurityValidationIssueDto(
                        SecurityValidationSeverityDto.Error,
                        "SM_SYMBOL_UNRESOLVED",
                        "Security Master identity is unresolved",
                        $"Ticker '{symbol}' could not be resolved.",
                        ["symbol"],
                        "Resolve the instrument before posting.",
                        [])
                ]);

            return new SecurityValidationGateResultDto(
                workflow,
                symbol.Trim().ToUpperInvariant(),
                SecurityId: null,
                IsResolved: false,
                IsBlocked: true,
                Report: report,
                Snapshot: null);
        }
    }
}
