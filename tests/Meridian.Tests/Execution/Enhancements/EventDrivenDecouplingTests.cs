using System.Diagnostics;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
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
    private static readonly TradeFillLedgerPostingContext TestPostingContext = new(
        TestPostingScope,
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            50, 200m, Commission: 5m, RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(2); // buy + commission
        ledger.Journal.Should().Contain(e => e.Description.Contains("Commission"));
    }

    [Fact]
    public async Task LedgerPostingConsumer_DoesNotAcknowledgeOrProjectBeforeDurableTargetConfirms()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);
        var releasePersistence = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = new InMemoryTradeFillLedgerPostingTarget
        {
            BeforePersistAsync = _ => releasePersistence.Task
        };
        var consumer = new LedgerPostingConsumer(
            target,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(MakeBuyEvent("AAPL"));
        await target.PersistStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        store.PostedCount.Should().Be(0, "the WAL acknowledgement must wait for durable persistence");
        (await store.LoadPendingAsync()).Should().ContainSingle();
        ledger.Journal.Should().BeEmpty("the in-memory projection advances only after persistence");

        releasePersistence.TrySetResult();
        await consumer.DisposeAsync();

        store.PostedCount.Should().Be(1);
        ledger.Journal.Should().ContainSingle();
    }

    [Fact]
    public async Task LedgerPostingConsumer_EconomicFingerprint_CoversSideQuantityPriceFeesPnlAndSecurity()
    {
        var baseline = new TradeExecutedEvent(
            Guid.NewGuid(), "ord-fingerprint", "AAPL", OrderSide.Sell,
            FilledQuantity: 10m, FillPrice: 100m, Commission: 2m,
            RealizedPnl: 5m, NewCash: 1_005m, DateTimeOffset.UtcNow);
        var baselineFingerprint = await CaptureTradeFingerprintAsync(baseline, PassingSecurityValidationGate.SecurityId);
        var variants = new[]
        {
            baseline with { Side = OrderSide.Buy },
            baseline with { FilledQuantity = 11m },
            baseline with { FillPrice = 101m },
            baseline with { Commission = 3m },
            baseline with { RealizedPnl = 6m }
        };

        foreach (var variant in variants)
        {
            (await CaptureTradeFingerprintAsync(variant, PassingSecurityValidationGate.SecurityId))
                .Should().NotBe(baselineFingerprint);
        }
        (await CaptureTradeFingerprintAsync(
                baseline,
                Guid.Parse("99999999-9999-9999-9999-999999999999")))
            .Should().NotBe(baselineFingerprint);
    }

    [Fact]
    public async Task LedgerPostingConsumer_SellWithGain_PostsRealizedGainEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger);

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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
                    new InMemoryTradeFillLedgerPostingTarget(),
                    NullLogger<LedgerPostingConsumer>.Instance,
                    firstStore,
                    TestPostingContext,
                    projectionLedger: firstLedger,
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
                new InMemoryTradeFillLedgerPostingTarget(),
                NullLogger<LedgerPostingConsumer>.Instance,
                recoveredStore,
                TestPostingContext,
                projectionLedger: recoveredLedger,
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
    public async Task Scenario_WalAppendFailure_SnapshotRetainsFillAcrossProcessRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fill-posting-tests", Guid.NewGuid().ToString("N"));
        var options = new TradeFillPostingStoreOptions(root, TestPostingScope)
        {
            WalAppendOverride = (_, _, _) => throw new IOException("simulated WAL device outage")
        };
        var evt = MakeBuyEvent("AAPL");
        try
        {
            await using (var failedWalStore = new WalTradeFillPostingStore(
                             options,
                             NullLogger<WalTradeFillPostingStore>.Instance))
            {
                var acceptance = await failedWalStore.AcceptAsync(evt);
                acceptance.ShouldEnqueue.Should().BeTrue();
                (await failedWalStore.LoadPendingAsync()).Should().ContainSingle();
            }

            File.Exists(options.SnapshotPath).Should().BeTrue();
            await using var recovered = new WalTradeFillPostingStore(
                new TradeFillPostingStoreOptions(root, TestPostingScope),
                NullLogger<WalTradeFillPostingStore>.Instance);

            var pending = await recovered.LoadPendingAsync();

            pending.Should().ContainSingle();
            pending[0].TradeEvent.Should().Be(evt);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scenario_OmsAccountingHandoff_WalOutage_ReplaysAfterProcessRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fill-posting-tests", Guid.NewGuid().ToString("N"));
        var failedWalOptions = new TradeFillPostingStoreOptions(root, TestPostingScope)
        {
            WalAppendOverride = (_, _, _) => throw new IOException("simulated WAL device outage")
        };
        try
        {
            Guid retainedFillId;
            {
                await using var firstStore = new WalTradeFillPostingStore(
                    failedWalOptions,
                    NullLogger<WalTradeFillPostingStore>.Instance);
                var gate = new NonCooperativeSecurityValidationGate();
                var consumer = new LedgerPostingConsumer(
                    new InMemoryTradeFillLedgerPostingTarget(),
                    NullLogger<LedgerPostingConsumer>.Instance,
                    firstStore,
                    TestPostingContext,
                    projectionLedger: new Meridian.Ledger.Ledger(),
                    securityValidationGate: gate,
                    drainTimeout: TimeSpan.FromMilliseconds(25),
                    cancellationTimeout: TimeSpan.FromMilliseconds(25));
                using var oms = new OrderManagementSystem(
                    new ImmediateFillGateway(),
                    NullLogger<OrderManagementSystem>.Instance,
                    tradeEventPublisher: consumer);

                var result = await oms.PlaceOrderAsync(new OrderRequest
                {
                    Symbol = "AAPL",
                    Side = OrderSide.Buy,
                    Type = OrderType.Market,
                    Quantity = 10m
                });
                result.Success.Should().BeTrue();
                await gate.ValidationStarted.WaitAsync(TimeSpan.FromSeconds(2));
                var pending = await firstStore.LoadPendingAsync();
                pending.Should().ContainSingle();
                retainedFillId = pending[0].TradeEvent.FillId;

                await consumer.DisposeAsync();
                gate.Release();
                Func<Task> completion = async () => await consumer.ProcessingCompletion;
                await completion.Should().ThrowAsync<OperationCanceledException>();
                (await firstStore.LoadPendingAsync()).Should().ContainSingle();
            }

            var recoveredLedger = new Meridian.Ledger.Ledger();
            await using var recoveredStore = new WalTradeFillPostingStore(
                new TradeFillPostingStoreOptions(root, TestPostingScope),
                NullLogger<WalTradeFillPostingStore>.Instance);
            var recoveredConsumer = new LedgerPostingConsumer(
                new InMemoryTradeFillLedgerPostingTarget(),
                NullLogger<LedgerPostingConsumer>.Instance,
                recoveredStore,
                TestPostingContext,
                projectionLedger: recoveredLedger,
                securityValidationGate: new PassingSecurityValidationGate());

            await recoveredConsumer.DisposeAsync();

            recoveredLedger.Journal.Should().ContainSingle(entry => entry.Metadata.FillId == retainedFillId);
            (await recoveredStore.LoadPendingAsync()).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scenario_OmsPrimaryWalAndSnapshotFail_ResultFailsClosedAndFallbackReplaysAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fill-posting-tests", Guid.NewGuid().ToString("N"));
        var primaryOptions = new TradeFillPostingStoreOptions(Path.Combine(root, "primary"), TestPostingScope)
        {
            WalAppendOverride = (_, _, _) => throw new IOException("simulated primary WAL outage"),
            SnapshotWriteOverride = (_, _) => throw new IOException("simulated primary snapshot outage")
        };
        var failureOptions = new TradeFillHandoffFailureStoreOptions(
            Path.Combine(root, "fallback"),
            TestPostingScope);
        try
        {
            Guid fillId;
            await using (var primaryStore = new WalTradeFillPostingStore(
                             primaryOptions,
                             NullLogger<WalTradeFillPostingStore>.Instance))
            await using (var failureStore = new AtomicTradeFillHandoffFailureStore(failureOptions))
            {
                var consumer = new LedgerPostingConsumer(
                    new InMemoryTradeFillLedgerPostingTarget(),
                    NullLogger<LedgerPostingConsumer>.Instance,
                    primaryStore,
                    TestPostingContext,
                    securityValidationGate: new PassingSecurityValidationGate());
                using var oms = new OrderManagementSystem(
                    new ImmediateFillGateway(),
                    NullLogger<OrderManagementSystem>.Instance,
                    tradeEventPublisher: consumer,
                    tradeFillHandoffFailureStore: failureStore);

                var result = await oms.PlaceOrderAsync(new OrderRequest
                {
                    Symbol = "AAPL",
                    Side = OrderSide.Buy,
                    Type = OrderType.Market,
                    Quantity = 10m
                });

                result.Success.Should().BeFalse("a broker fill without primary accounting acceptance is fail-closed");
                result.OrderState!.Status.Should().Be(OrderStatus.Filled,
                    "the broker fill must not be rewritten as a rejected order");
                result.ErrorMessage.Should().Contain("durably retained for restart replay");
                var failures = await oms.GetAccountingHandoffFailuresAsync();
                failures.Should().ContainSingle();
                fillId = failures[0].TradeEvent.FillId;
                await consumer.DisposeAsync();
            }

            var recoveredLedger = new Meridian.Ledger.Ledger();
            await using var recoveredPrimaryStore = new WalTradeFillPostingStore(
                new TradeFillPostingStoreOptions(Path.Combine(root, "primary"), TestPostingScope),
                NullLogger<WalTradeFillPostingStore>.Instance);
            await using var recoveredFailureStore = new AtomicTradeFillHandoffFailureStore(failureOptions);
            var recoveredConsumer = new LedgerPostingConsumer(
                new InMemoryTradeFillLedgerPostingTarget(),
                NullLogger<LedgerPostingConsumer>.Instance,
                recoveredPrimaryStore,
                TestPostingContext,
                projectionLedger: recoveredLedger,
                securityValidationGate: new PassingSecurityValidationGate());
            using var recoveredOms = new OrderManagementSystem(
                new ImmediateFillGateway(),
                NullLogger<OrderManagementSystem>.Instance,
                tradeEventPublisher: recoveredConsumer,
                tradeFillHandoffFailureStore: recoveredFailureStore);

            await WaitForNoHandoffFailuresAsync(recoveredOms);
            await recoveredConsumer.DisposeAsync();

            recoveredLedger.Journal.Should().ContainSingle(entry => entry.Metadata.FillId == fillId);
            (await recoveredPrimaryStore.LoadPendingAsync()).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WalTradeFillPostingStore_BookPeriodRollover_UsesIndependentScopePartitions()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fill-posting-tests", Guid.NewGuid().ToString("N"));
        var periodOne = new TradeFillPostingStoreOptions(root, "book-a/period-2026-06");
        var periodTwo = new TradeFillPostingStoreOptions(root, "book-a/period-2026-07");
        try
        {
            await using var first = new WalTradeFillPostingStore(
                periodOne,
                NullLogger<WalTradeFillPostingStore>.Instance);
            await using var second = new WalTradeFillPostingStore(
                periodTwo,
                NullLogger<WalTradeFillPostingStore>.Instance);
            var firstFill = MakeBuyEvent("AAPL");
            var secondFill = MakeBuyEvent("MSFT");

            await first.AcceptAsync(firstFill);
            await second.AcceptAsync(secondFill);

            periodOne.ScopeDirectory.Should().NotBe(periodTwo.ScopeDirectory);
            (await first.LoadPendingAsync()).Select(item => item.TradeEvent.FillId)
                .Should().Equal(firstFill.FillId);
            (await second.LoadPendingAsync()).Select(item => item.TradeEvent.FillId)
                .Should().Equal(secondFill.FillId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WalTradeFillPostingStore_CompactsCompletedHistoryAndRecoversPendingSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fill-posting-tests", Guid.NewGuid().ToString("N"));
        var options = new TradeFillPostingStoreOptions(root, TestPostingScope)
        {
            CompactionRecordThreshold = 2,
            MaxWalFileSizeBytes = 1_024
        };
        var stillPending = MakeBuyEvent("PENDING");
        try
        {
            await using (var store = new WalTradeFillPostingStore(
                             options,
                             NullLogger<WalTradeFillPostingStore>.Instance))
            {
                for (var index = 0; index < 24; index++)
                {
                    var completed = MakeBuyEvent($"DONE{index}");
                    await store.AcceptAsync(completed);
                    await store.MarkPostedAsync(completed.FillId);
                }
                await store.AcceptAsync(stillPending);
            }

            Directory.GetFiles(options.WalDirectory, "*.wal").Should().HaveCountLessThanOrEqualTo(3);
            new FileInfo(options.SnapshotPath).Length.Should().BeLessThan(16 * 1024);

            await using var recovered = new WalTradeFillPostingStore(
                options,
                NullLogger<WalTradeFillPostingStore>.Instance);
            var pending = await recovered.LoadPendingAsync();

            pending.Should().ContainSingle();
            pending[0].TradeEvent.Should().Be(stillPending);
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: gate);

        firstConsumer.Publish(evt);
        await firstConsumer.DisposeAsync();

        ledger.Journal.Should().BeEmpty();
        var pendingAfterFailure = await store.LoadPendingAsync();
        pendingAfterFailure.Should().ContainSingle();
        pendingAfterFailure[0].FailureCount.Should().Be(1);

        var recoveredConsumer = new LedgerPostingConsumer(
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: ledger,
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
        var postingTarget = new InMemoryTradeFillLedgerPostingTarget();
        var firstConsumer = new LedgerPostingConsumer(
            postingTarget,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        firstConsumer.Publish(evt);
        await firstConsumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle("ledger mutation precedes durable acknowledgement");
        (await store.LoadPendingAsync()).Should().ContainSingle();

        var recoveredConsumer = new LedgerPostingConsumer(
            postingTarget,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());
        await recoveredConsumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle(
            "replay must detect the journal already carrying the fill id before acknowledging");
        (await store.LoadPendingAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task LedgerPostingConsumer_ReplayWithReorderedRetainedLines_FailsClosedAndRemainsPending()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var store = new FailOnceAcknowledgementStore(TestPostingScope);
        var postingTarget = new InMemoryTradeFillLedgerPostingTarget();
        var evt = MakeBuyEvent("AAPL");
        var firstConsumer = new LedgerPostingConsumer(
            postingTarget,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());
        firstConsumer.Publish(evt);
        await firstConsumer.DisposeAsync();
        (await store.LoadPendingAsync()).Should().ContainSingle();

        postingTarget.ReplaceRetainedJournal(entry => new JournalEntry(
            entry.JournalEntryId,
            entry.Timestamp,
            entry.Description,
            entry.Lines.Reverse().ToArray(),
            entry.Metadata));
        var recoveredProjection = new Meridian.Ledger.Ledger();
        var recoveredConsumer = new LedgerPostingConsumer(
            postingTarget,
            NullLogger<LedgerPostingConsumer>.Instance,
            store,
            TestPostingContext,
            projectionLedger: recoveredProjection,
            securityValidationGate: new PassingSecurityValidationGate());

        await recoveredConsumer.DisposeAsync();

        recoveredProjection.Journal.Should().BeEmpty();
        var retained = await store.LoadPendingAsync();
        retained.Should().ContainSingle();
        retained[0].LastFailure.Should().Contain("canonical economic fingerprint and ordered lines");
    }

    [Fact]
    public async Task LedgerPostingConsumer_ConcurrentDisposeCalls_AwaitSameBoundedShutdown()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
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
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: gate,
            drainTimeout: TimeSpan.FromMilliseconds(25),
            cancellationTimeout: TimeSpan.FromMilliseconds(25));
        consumer.Publish(MakeBuyEvent("AAPL"));
        await gate.ValidationStarted.WaitAsync(TimeSpan.FromSeconds(1));

        var elapsed = Stopwatch.StartNew();
        var disposeTask = consumer.DisposeAsync().AsTask();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(1));
        elapsed.Stop();

        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750));
        ledger.Journal.Should().BeEmpty();

        await gate.CancellationCallbackStarted.WaitAsync(TimeSpan.FromSeconds(5));
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

    private static async Task WaitForNoHandoffFailuresAsync(OrderManagementSystem oms)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(5);
        while ((await oms.GetAccountingHandoffFailuresAsync()).Count != 0)
        {
            if (DateTimeOffset.UtcNow >= timeoutAt)
                throw new TimeoutException("Timed out waiting for retained accounting handoff replay.");
            await Task.Delay(10);
        }
    }

    private static async Task<string> CaptureTradeFingerprintAsync(
        TradeExecutedEvent tradeEvent,
        Guid securityId)
    {
        var target = new InMemoryTradeFillLedgerPostingTarget();
        var consumer = new LedgerPostingConsumer(
            target,
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            securityValidationGate: new PassingSecurityValidationGate(securityId));
        consumer.Publish(tradeEvent);
        await consumer.DisposeAsync();

        return target.RetainedEntries
            .Single(entry => entry.Metadata.ActivityType == "trade-fill")
            .Metadata.Tags!["tradeFill.fingerprint"];
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

    private sealed class ImmediateFillGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        public string GatewayId => "paper";

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public bool IsConnected => true;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            var orderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N");
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ClientOrderId = orderId,
                ReportType = ExecutionReportType.Fill,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = request.Quantity,
                FilledQuantity = request.Quantity,
                FillPrice = request.LimitPrice ?? 100m,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(
            string orderId,
            OrderModification modification,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class InMemoryTradeFillLedgerPostingTarget : ITradeFillLedgerPostingTarget
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, LedgerJournalEntryRecord> _retained = [];
        private long _sequence;

        public Func<LedgerJournalEntryWrite, Task>? BeforePersistAsync { get; init; }

        public TaskCompletionSource PersistStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<JournalEntry> RetainedEntries
        {
            get
            {
                lock (_sync)
                {
                    return _retained.Values
                        .OrderBy(static record => record.GlobalSequence)
                        .Select(static record => record.Entry)
                        .ToArray();
                }
            }
        }

        public async Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(
            LedgerJournalEntryWrite write,
            CancellationToken ct = default)
        {
            PersistStarted.TrySetResult();
            if (BeforePersistAsync is not null)
                await BeforePersistAsync(write).WaitAsync(ct);

            lock (_sync)
            {
                if (_retained.TryGetValue(write.Entry.JournalEntryId, out var retained))
                    return new TradeFillLedgerPostingConfirmation(retained, WasAppended: false);

                var record = new LedgerJournalEntryRecord(
                    write.Entry,
                    write.AggregateId,
                    write.PeriodId,
                    write.CommandId,
                    write.CorrelationId,
                    ++_sequence,
                    DateTimeOffset.UtcNow,
                    write.AccountingBasis,
                    write.AccountingPolicyId,
                    write.AccountingPolicyVersion,
                    write.RuleId,
                    write.RuleVersion,
                    write.SourceEventId,
                    write.SourceJournalEntryId,
                    write.PostingKind,
                    write.AdjustmentApproval);
                _retained.Add(write.Entry.JournalEntryId, record);
                return new TradeFillLedgerPostingConfirmation(record, WasAppended: true);
            }
        }

        public void ReplaceRetainedJournal(Func<JournalEntry, JournalEntry> replace)
        {
            lock (_sync)
            {
                foreach (var (id, record) in _retained.ToArray())
                    _retained[id] = record with { Entry = replace(record.Entry) };
            }
        }
    }

    private sealed class InMemoryTradeFillPostingStore(string postingScope) : ITradeFillPostingStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, PendingTradeFillPosting> _pending = [];
        private readonly HashSet<Guid> _posted = [];
        private long _sequence;

        public string PostingScope { get; } = postingScope;

        public int PostedCount
        {
            get
            {
                lock (_sync)
                {
                    return _posted.Count;
                }
            }
        }

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
        private readonly Guid _securityId;

        public PassingSecurityValidationGate(Guid? securityId = null)
        {
            _securityId = securityId ?? SecurityId;
        }

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

        private SecurityValidationGateResultDto BuildResult(string symbol, SecurityValidationWorkflowDto workflow)
        {
            var report = new SecurityValidationReportDto(
                _securityId,
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
