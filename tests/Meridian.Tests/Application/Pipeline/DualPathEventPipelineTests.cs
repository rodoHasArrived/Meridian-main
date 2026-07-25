using System.Threading.Channels;
using System.Reflection;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Performance;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Moq;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Guards routing and lifecycle behavior, including the feed-shutdown failure mode where
/// backpressure leaves a consumer owning a partially published hot-path batch.
/// </summary>
public class DualPathEventPipelineTests : IAsyncLifetime
{
    private MockStorageSink _sink = null!;
    private EventPipeline _slowPath = null!;
    private SymbolTable _symbolTable = null!;
    private DualPathEventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _sink = new MockStorageSink();
        _slowPath = new EventPipeline(_sink, capacity: 10_000, enablePeriodicFlush: false);
        _symbolTable = new SymbolTable();
        _pipeline = new DualPathEventPipeline(_slowPath, _symbolTable, ringBufferCapacity: 256, batchDrainSize: 64);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
        await _slowPath.DisposeAsync();
    }

    #region Constructor validation

    [Fact]
    public void Constructor_NullSlowPath_Throws()
    {
        var act = () => new DualPathEventPipeline(null!, new SymbolTable());
        act.Should().Throw<ArgumentNullException>().WithParameterName("slowPath");
    }

    [Fact]
    public void Constructor_NullSymbolTable_Throws()
    {
        var act = () => new DualPathEventPipeline(_slowPath, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("symbolTable");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    public void Constructor_InvalidRingBufferCapacity_Throws(int cap)
    {
        var act = () => new DualPathEventPipeline(_slowPath, _symbolTable, ringBufferCapacity: cap);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ringBufferCapacity");
    }

    [Fact]
    public void Constructor_NonPositiveShutdownTimeout_Throws()
    {
        var act = () => new DualPathEventPipeline(
            _slowPath,
            _symbolTable,
            shutdownTimeout: TimeSpan.Zero);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("shutdownTimeout");
    }

    #endregion

    #region Routing tests

    [Fact]
    public async Task TryPublish_TradeEvent_RoutedThroughHotPath()
    {
        var evt = CreateTradeEvent("SPY", seq: 1);
        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(1);
        _pipeline.HotTradeFallbacks.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "SPY");
    }

    [Fact]
    public async Task TryPublish_BboQuoteEvent_RoutedThroughHotPath()
    {
        var evt = CreateQuoteEvent("AAPL", seq: 1);
        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotQuotePublished.Should().Be(1);
        _pipeline.HotQuoteFallbacks.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "AAPL");
    }

    [Fact]
    public async Task QuoteHotPath_WhenSlowPathBackpressures_RetriesUntilPersisted()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var blockingSink = new BlockingStorageSink(release.Task);
        await using var constrainedSlowPath = new EventPipeline(
            blockingSink,
            capacity: 1,
            fullMode: System.Threading.Channels.BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false);
        await using var pipeline = new DualPathEventPipeline(
            constrainedSlowPath,
            new SymbolTable(),
            ringBufferCapacity: 32,
            batchDrainSize: 4);

        pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 1));
        pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 2));
        pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 3));

        var firstBlockSw = System.Diagnostics.Stopwatch.StartNew();
        while (blockingSink.ReceivedCount < 1 && firstBlockSw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        blockingSink.ReceivedCount.Should().BeGreaterThanOrEqualTo(1,
            "the slow path should have received at least one event before releasing backpressure in this test");
        release.SetResult(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (blockingSink.ReceivedCount < 3 && sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        blockingSink.ReceivedCount.Should().Be(3,
            "quote hot-path fallback should retry instead of silently dropping when the slow path is full");
    }

    [Fact]
    public async Task TradeHotPath_WhenSlowPathBackpressures_WaitsWithoutRecordingFalseDrops()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var blockingSink = new BlockingStorageSink(release.Task);
        await using var constrainedSlowPath = new EventPipeline(
            blockingSink,
            capacity: 1,
            fullMode: System.Threading.Channels.BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false);
        await using var pipeline = new DualPathEventPipeline(
            constrainedSlowPath,
            new SymbolTable(),
            ringBufferCapacity: 32,
            batchDrainSize: 4);

        pipeline.TryPublish(CreateTradeEvent("SPY", seq: 1));
        pipeline.TryPublish(CreateTradeEvent("SPY", seq: 2));
        pipeline.TryPublish(CreateTradeEvent("SPY", seq: 3));

        var firstBlockSw = System.Diagnostics.Stopwatch.StartNew();
        while (blockingSink.ReceivedCount < 1 && firstBlockSw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        blockingSink.ReceivedCount.Should().BeGreaterThanOrEqualTo(1,
            "the slow path should have received at least one trade before releasing backpressure in this test");
        release.SetResult(true);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (blockingSink.ReceivedCount < 3 && sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        blockingSink.ReceivedCount.Should().Be(3,
            "trade hot-path backpressure should wait for slow-path capacity instead of losing events");
        constrainedSlowPath.DroppedCount.Should().Be(0,
            "waiting for slow-path capacity must not be misreported as dropped-event loss");
    }

    [Fact]
    public async Task TryPublish_IntegrityEvent_GoesToSlowPath_NotHotPath()
    {
        var evt = MarketEvent.Integrity(
            DateTimeOffset.UtcNow, "SPY",
            new IntegrityEvent(DateTimeOffset.UtcNow, "SPY", IntegritySeverity.Warning, "test", 0, 1));

        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(0);
        _pipeline.HotQuotePublished.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Type == MarketEventType.Integrity);
    }

    [Fact]
    public async Task TryPublish_HeartbeatEvent_GoesToSlowPath()
    {
        var evt = MarketEvent.Heartbeat(DateTimeOffset.UtcNow);
        _pipeline.TryPublish(in evt);

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Type == MarketEventType.Heartbeat);
    }

    [Fact]
    public async Task TryPublish_MultipleTrades_AllReachSink()
    {
        const int count = 50;
        for (var i = 0; i < count; i++)
            _pipeline.TryPublish(CreateTradeEvent("SPY", seq: i));

        await WaitForSinkCount(count, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(count);
        _pipeline.HotTradeFallbacks.Should().Be(0);
        _sink.ReceivedEvents.Should().HaveCount(count);
    }

    #endregion

    #region Zero-allocation API tests

    [Fact]
    public async Task TryPublishTrade_DirectStruct_ReachesSlowPath()
    {
        var symbolId = _symbolTable.GetOrAdd("SPY");
        var raw = new RawTradeEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 100m, 10L, 1, 1L);

        _pipeline.TryPublishTrade(in raw).Should().BeTrue();

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotTradePublished.Should().Be(1);
        _pipeline.HotTradeFallbacks.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "SPY");
    }

    [Fact]
    public async Task TryPublishQuote_DirectStruct_ReachesSlowPath()
    {
        var symbolId = _symbolTable.GetOrAdd("AAPL");
        var raw = new RawQuoteEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 189m, 100L, 190m, 200L, 1L);

        _pipeline.TryPublishQuote(in raw).Should().BeTrue();

        await WaitForSinkCount(1, timeout: TimeSpan.FromSeconds(5));

        _pipeline.HotQuotePublished.Should().Be(1);
        _pipeline.HotQuoteFallbacks.Should().Be(0);
        _sink.ReceivedEvents.Should().ContainSingle(e => e.Symbol == "AAPL");
    }

    [Fact]
    public async Task TryPublishTrade_WhenBufferFull_FallsBackToSlowPath()
    {
        // Fill the ring buffer completely (consumers disabled so they cannot race to drain it)
        await using var tinyPipeline = new DualPathEventPipeline(_slowPath, _symbolTable, ringBufferCapacity: 2, batchDrainSize: 1, startConsumers: false);
        var symbolId = _symbolTable.GetOrAdd("SPY");

        // Fill the buffer (capacity rounds up to power of 2 = 2)
        var raw = new RawTradeEvent(DateTimeOffset.UtcNow.UtcTicks, symbolId, 1m, 1L, 0, 1L);
        tinyPipeline.TryPublishTrade(in raw);
        tinyPipeline.TryPublishTrade(in raw);

        // Buffer should now be full — next write should fall back to the slow path
        var result = tinyPipeline.TryPublishTrade(in raw);
        result.Should().BeTrue();
        tinyPipeline.HotTradeFallbacks.Should().Be(1);

        await tinyPipeline.DisposeAsync();
        await WaitForEventsAsync(_sink, expectedCount: 3, timeout: TimeSpan.FromSeconds(5));
        _sink.ReceivedEvents.Should().HaveCount(3);
    }

    #endregion

    #region Backpressure delegation tests

    [Fact]
    public void IsUnderPressure_DelegatesToSlowPath()
    {
        // The slow path is not under pressure with a large, empty channel
        _pipeline.IsUnderPressure.Should().BeFalse();
    }

    [Fact]
    public void QueueUtilization_DelegatesToSlowPath()
    {
        _pipeline.QueueUtilization.Should().BeInRange(0.0, 100.0);
    }

    #endregion

    #region Statistics tests

    [Fact]
    public async Task HotTradeConsumed_IncreasesAfterDrain()
    {
        _pipeline.TryPublish(CreateTradeEvent("SPY", seq: 1));
        await WaitForConsumed(1, timeout: TimeSpan.FromSeconds(5));
        _pipeline.HotTradeConsumed.Should().Be(1);
    }

    [Fact]
    public async Task HotQuoteConsumed_IncreasesAfterDrain()
    {
        _pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 1));
        await WaitForQuoteConsumed(1, timeout: TimeSpan.FromSeconds(5));
        _pipeline.HotQuoteConsumed.Should().Be(1);
    }

    #endregion

    #region Mixed event type tests

    [Fact]
    public async Task MixedEvents_EachRoutedToCorrectPath()
    {
        _pipeline.TryPublish(CreateTradeEvent("SPY", seq: 1));
        _pipeline.TryPublish(CreateQuoteEvent("AAPL", seq: 2));
        _pipeline.TryPublish(MarketEvent.Heartbeat(DateTimeOffset.UtcNow));

        await WaitForSinkCount(3, timeout: TimeSpan.FromSeconds(5));

        _sink.ReceivedEvents.Should().HaveCount(3);
        _pipeline.HotTradePublished.Should().Be(1);
        _pipeline.HotTradeFallbacks.Should().Be(0);
        _pipeline.HotQuotePublished.Should().Be(1);
        _pipeline.HotQuoteFallbacks.Should().Be(0);
    }

    #endregion

    #region Shutdown lifecycle tests

    [Theory]
    [InlineData(MarketEventType.Trade)]
    [InlineData(MarketEventType.BboQuote)]
    public async Task Scenario_FeedShutdown_BackpressuredHotPathBatch_ConcurrentDisposalDrainsAcceptedEventsExactlyOnce(
        MarketEventType eventType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new GateStorageSink(release.Task);
        var slowPath = new EventPipeline(
            sink,
            capacity: 1,
            fullMode: System.Threading.Channels.BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false);
        var pipeline = new DualPathEventPipeline(
            slowPath,
            new SymbolTable(),
            ringBufferCapacity: 32,
            batchDrainSize: 1);

        try
        {
            for (var sequence = 1; sequence <= 4; sequence++)
            {
                var evt = eventType == MarketEventType.Trade
                    ? CreateTradeEvent("SPY", sequence)
                    : CreateQuoteEvent("SPY", sequence);
                pipeline.TryPublish(in evt).Should().BeTrue();
            }

            await sink.FirstAppendStarted.WaitAsync(timeout.Token);

            var firstDispose = pipeline.DisposeAsync().AsTask();
            var secondDispose = pipeline.DisposeAsync().AsTask();

            firstDispose.IsCompleted.Should().BeFalse(
                "shutdown must not report completion while an accepted batch is backpressured");
            secondDispose.IsCompleted.Should().BeFalse(
                "every concurrent disposer must await the same in-progress drain");

            release.TrySetResult(true);
            await Task.WhenAll(firstDispose, secondDispose).WaitAsync(timeout.Token);
            await slowPath.FlushAsync(timeout.Token);

            sink.ReceivedEvents.Select(static evt => evt.Sequence)
                .Should().Equal(1L, 2L, 3L, 4L);
            sink.ReceivedEvents.Should().OnlyHaveUniqueItems(
                "an idempotent shutdown must not republish an accepted batch");
            slowPath.DroppedCount.Should().Be(0);

            if (eventType == MarketEventType.Trade)
            {
                pipeline.HotTradePublished.Should().Be(4);
                pipeline.HotTradeConsumed.Should().Be(4);
                pipeline.HotTradeDropped.Should().Be(0);
            }
            else
            {
                pipeline.HotQuotePublished.Should().Be(4);
                pipeline.HotQuoteConsumed.Should().Be(4);
                pipeline.HotQuoteDropped.Should().Be(0);
            }
        }
        finally
        {
            release.TrySetResult(true);
            await pipeline.DisposeAsync();
            await slowPath.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(MarketEventType.Trade)]
    [InlineData(MarketEventType.BboQuote)]
    public async Task Scenario_FeedShutdown_SlowPathClosesDuringHandoff_AccountsPrefixAndReportsUnpublishedSuffix(
        MarketEventType eventType)
    {
        const int eventCount = 8;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new GateStorageSink(release.Task);
        var slowPath = new EventPipeline(
            sink,
            capacity: 1,
            fullMode: BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false);
        var pipeline = new DualPathEventPipeline(
            slowPath,
            new SymbolTable(),
            ringBufferCapacity: 32,
            batchDrainSize: 4);

        try
        {
            PublishHotPathBatch(pipeline, eventType, eventCount);

            await sink.FirstAppendStarted.WaitAsync(timeout.Token);

            // Adaptive draining can prefetch up to four events before the sink blocks,
            // while one more may occupy the channel. Eight events guarantee an
            // unpublished suffix without assuming an exact scheduling-dependent prefix.
            slowPath.Complete();

            var outcome = await pipeline.StopAsync(timeout.Token);
            var acceptedPrefix = slowPath.PublishedCount;

            outcome.Status.Should().Be(DualPathPipelineStopStatus.Failed);
            outcome.Quiesced.Should().BeTrue();
            outcome.Failure.Should().NotBeNull();
            AssertOnlyChannelClosedFailures(outcome.Failure!);
            acceptedPrefix.Should().BeInRange(1L, eventCount - 1L);
            Exception? disposalFailure = null;
            try
            {
                await pipeline.DisposeAsync();
            }
            catch (Exception ex)
            {
                disposalFailure = ex;
            }

            disposalFailure.Should().NotBeNull(
                "IAsyncDisposable callers must observe the failed slow-path handoff");
            AssertOnlyChannelClosedFailures(disposalFailure!);

            if (eventType == MarketEventType.Trade)
            {
                pipeline.HotTradePublished.Should().Be(eventCount);
                pipeline.HotTradeConsumed.Should().Be(acceptedPrefix);
                pipeline.HotTradeDropped.Should().Be(eventCount - acceptedPrefix);
                pipeline.HotTradeInFlight.Should().Be(0);
                outcome.TradeConsumed.Should().Be(acceptedPrefix);
                outcome.TradeDropped.Should().Be(eventCount - acceptedPrefix);
                outcome.TradePending.Should().Be(0);
            }
            else
            {
                pipeline.HotQuotePublished.Should().Be(eventCount);
                pipeline.HotQuoteConsumed.Should().Be(acceptedPrefix);
                pipeline.HotQuoteDropped.Should().Be(eventCount - acceptedPrefix);
                pipeline.HotQuoteInFlight.Should().Be(0);
                outcome.QuoteConsumed.Should().Be(acceptedPrefix);
                outcome.QuoteDropped.Should().Be(eventCount - acceptedPrefix);
                outcome.QuotePending.Should().Be(0);
            }
        }
        finally
        {
            release.TrySetResult(true);
            await slowPath.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(MarketEventType.Trade)]
    [InlineData(MarketEventType.BboQuote)]
    public async Task Scenario_FeedShutdown_HungSlowPathCallerCancels_ReportsCancelledAccountedOutcome(
        MarketEventType eventType)
    {
        const int eventCount = 8;
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var stopCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new GateStorageSink(release.Task);
        var slowPath = new EventPipeline(
            sink,
            capacity: 1,
            fullMode: BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false);
        var pipeline = new DualPathEventPipeline(
            slowPath,
            new SymbolTable(),
            ringBufferCapacity: 32,
            batchDrainSize: 4);

        try
        {
            PublishHotPathBatch(pipeline, eventType, eventCount);
            await sink.FirstAppendStarted.WaitAsync(testTimeout.Token);

            var stopTask = pipeline.StopAsync(stopCancellation.Token);
            stopCancellation.Cancel();
            var outcome = await stopTask.WaitAsync(testTimeout.Token);
            var acceptedPrefix = slowPath.PublishedCount;

            outcome.Status.Should().Be(DualPathPipelineStopStatus.Cancelled);
            outcome.Quiesced.Should().BeTrue();
            outcome.Failure.Should().BeNull();
            acceptedPrefix.Should().BeInRange(1L, eventCount - 1L);

            if (eventType == MarketEventType.Trade)
            {
                pipeline.HotTradePublished.Should().Be(eventCount);
                pipeline.HotTradeConsumed.Should().Be(acceptedPrefix);
                pipeline.HotTradeDropped.Should().Be(eventCount - acceptedPrefix);
                outcome.TradeConsumed.Should().Be(acceptedPrefix);
                outcome.TradeDropped.Should().Be(eventCount - acceptedPrefix);
                outcome.TradePending.Should().Be(0);
            }
            else
            {
                pipeline.HotQuotePublished.Should().Be(eventCount);
                pipeline.HotQuoteConsumed.Should().Be(acceptedPrefix);
                pipeline.HotQuoteDropped.Should().Be(eventCount - acceptedPrefix);
                outcome.QuoteConsumed.Should().Be(acceptedPrefix);
                outcome.QuoteDropped.Should().Be(eventCount - acceptedPrefix);
                outcome.QuotePending.Should().Be(0);
            }
        }
        finally
        {
            release.TrySetResult(true);
            await slowPath.DisposeAsync();
        }
    }

    [Fact]
    public async Task Scenario_FeedShutdown_HungSlowPathConfiguredDeadline_ReportsTimedOutAccountedOutcome()
    {
        const int eventCount = 8;
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new GateStorageSink(release.Task);
        var slowPath = new EventPipeline(
            sink,
            capacity: 1,
            fullMode: BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false);
        var pipeline = new DualPathEventPipeline(
            slowPath,
            new SymbolTable(),
            ringBufferCapacity: 32,
            batchDrainSize: 4,
            shutdownTimeout: TimeSpan.FromMilliseconds(100));

        try
        {
            PublishHotPathBatch(pipeline, MarketEventType.Trade, eventCount);
            await sink.FirstAppendStarted.WaitAsync(testTimeout.Token);

            var outcome = await pipeline.StopAsync().WaitAsync(testTimeout.Token);
            var acceptedPrefix = slowPath.PublishedCount;

            outcome.Status.Should().Be(DualPathPipelineStopStatus.TimedOut);
            outcome.Quiesced.Should().BeTrue();
            outcome.Failure.Should().BeNull();
            acceptedPrefix.Should().BeInRange(1L, eventCount - 1L);
            pipeline.HotTradePublished.Should().Be(eventCount);
            pipeline.HotTradeConsumed.Should().Be(acceptedPrefix);
            pipeline.HotTradeDropped.Should().Be(eventCount - acceptedPrefix);
            outcome.TradeConsumed.Should().Be(acceptedPrefix);
            outcome.TradeDropped.Should().Be(eventCount - acceptedPrefix);
            outcome.TradePending.Should().Be(0);
        }
        finally
        {
            release.TrySetResult(true);
            await slowPath.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(MarketEventType.Trade)]
    [InlineData(MarketEventType.BboQuote)]
    public async Task ConcurrentHotPathProducers_EverySuccessfulPublicationOwnsOneDistinctBufferSlot(
        MarketEventType eventType)
    {
        const int producerCount = 8;
        const int eventsPerProducer = 256;
        const int eventCount = producerCount * eventsPerProducer;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sink = new MockStorageSink();
        var slowPath = new EventPipeline(
            sink,
            capacity: 4_096,
            enablePeriodicFlush: false);
        var symbols = new SymbolTable();
        var pipeline = new DualPathEventPipeline(
            slowPath,
            symbols,
            ringBufferCapacity: 4_096,
            batchDrainSize: 128,
            startConsumers: false);
        var symbolId = symbols.GetOrAdd("SPY");
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var publishers = Enumerable.Range(0, producerCount)
                .Select(producer => Task.Run(async () =>
                {
                    await start.Task.ConfigureAwait(false);
                    var accepted = 0;
                    for (var offset = 0; offset < eventsPerProducer; offset++)
                    {
                        var sequence = (long)producer * eventsPerProducer + offset + 1;
                        var published = eventType == MarketEventType.Trade
                            ? pipeline.TryPublishTrade(new RawTradeEvent(
                                DateTimeOffset.UtcNow.UtcTicks,
                                symbolId,
                                100m,
                                100L,
                                (byte)AggressorSide.Buy,
                                sequence))
                            : pipeline.TryPublishQuote(new RawQuoteEvent(
                                DateTimeOffset.UtcNow.UtcTicks,
                                symbolId,
                                99.9m,
                                100L,
                                100.1m,
                                100L,
                                sequence));
                        if (published)
                            accepted++;
                    }

                    return accepted;
                }, timeout.Token))
                .ToArray();

            start.TrySetResult(true);
            var acceptedCounts = await Task.WhenAll(publishers).WaitAsync(timeout.Token);

            acceptedCounts.Sum().Should().Be(eventCount);
            if (eventType == MarketEventType.Trade)
            {
                pipeline.HotTradePublished.Should().Be(eventCount);
                pipeline.TradeBufferCount.Should().Be(eventCount);
            }
            else
            {
                pipeline.HotQuotePublished.Should().Be(eventCount);
                pipeline.QuoteBufferCount.Should().Be(eventCount);
            }

            var outcome = await pipeline.StopAsync().WaitAsync(timeout.Token);
            outcome.Succeeded.Should().BeTrue();
            await slowPath.FlushAsync(timeout.Token);

            var sequences = sink.ReceivedEvents.Select(static evt => evt.Sequence).ToArray();
            sequences.Should().HaveCount(eventCount);
            sequences.Should().OnlyHaveUniqueItems();
            sequences.Should().BeEquivalentTo(
                Enumerable.Range(1, eventCount).Select(static sequence => (long)sequence));
            if (eventType == MarketEventType.Trade)
            {
                pipeline.HotTradeConsumed.Should().Be(eventCount);
                pipeline.HotTradeDropped.Should().Be(0);
            }
            else
            {
                pipeline.HotQuoteConsumed.Should().Be(eventCount);
                pipeline.HotQuoteDropped.Should().Be(0);
            }
        }
        finally
        {
            start.TrySetResult(true);
            await pipeline.DisposeAsync();
            await slowPath.DisposeAsync();
        }
    }

    [Fact]
    public async Task TimedOutStop_WithAdmittedPublisher_ExposesAwaitableTerminalCleanupBeforeSlowPathDisposal()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var sink = new MockStorageSink();
        var slowPath = new EventPipeline(sink, capacity: 16, enablePeriodicFlush: false);
        var symbols = new SymbolTable();
        var pipeline = new DualPathEventPipeline(
            slowPath,
            symbols,
            ringBufferCapacity: 8,
            batchDrainSize: 2,
            startConsumers: false,
            shutdownTimeout: TimeSpan.FromMilliseconds(25));
        var symbolId = symbols.GetOrAdd("SPY");
        var trade = new RawTradeEvent(
            DateTimeOffset.UtcNow.UtcTicks,
            symbolId,
            100m,
            100L,
            (byte)AggressorSide.Buy,
            1L);
        var producerGate = typeof(DualPathEventPipeline)
            .GetField("_tradeProducerSync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(pipeline)!;
        var activePublishers = typeof(DualPathEventPipeline)
            .GetField("_activePublishers", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var producerGateAcquired = new ManualResetEventSlim();
        using var releaseProducerGate = new ManualResetEventSlim();
        var gateOwner = Task.Factory.StartNew(
            () =>
            {
                lock (producerGate)
                {
                    producerGateAcquired.Set();
                    releaseProducerGate.Wait();
                }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        Task<bool>? publisher = null;
        Task? terminalCleanup = null;

        try
        {
            await Task.Run(() => producerGateAcquired.Wait(timeout.Token), timeout.Token);
            publisher = Task.Factory.StartNew(
                () => pipeline.TryPublishTrade(trade),
                timeout.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            await WaitForConditionAsync(
                () => (int)activePublishers.GetValue(pipeline)! == 1,
                timeout.Token);

            var outcome = await pipeline.StopAsync().WaitAsync(timeout.Token);

            outcome.Status.Should().Be(DualPathPipelineStopStatus.TimedOut);
            outcome.Quiesced.Should().BeFalse();
            terminalCleanup = pipeline.AwaitTerminalCleanupAsync(timeout.Token);
            terminalCleanup.IsCompleted.Should().BeFalse(
                "the admitted publisher still owns the producer boundary");
        }
        finally
        {
            releaseProducerGate.Set();
            await gateOwner.WaitAsync(timeout.Token);
        }

        (await publisher!.WaitAsync(timeout.Token)).Should().BeTrue();
        await terminalCleanup!.WaitAsync(timeout.Token);

        pipeline.HotTradePublished.Should().Be(1);
        pipeline.HotTradeDropped.Should().Be(1);
        pipeline.TradeBufferCount.Should().Be(0);

        Func<Task> dispose = () => pipeline.DisposeAsync().AsTask();
        await dispose.Should().ThrowAsync<TimeoutException>(
            "disposal still reports the bounded stop outcome after terminal cleanup");
        await slowPath.DisposeAsync();
    }

    [Fact]
    public async Task TryPublish_AfterDisposal_ReturnsFalseWithoutMutatingBuffersOrSlowPath()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sink = new MockStorageSink();
        var slowPath = new EventPipeline(sink, capacity: 16, enablePeriodicFlush: false);
        var symbols = new SymbolTable();
        var pipeline = new DualPathEventPipeline(
            slowPath,
            symbols,
            ringBufferCapacity: 8,
            batchDrainSize: 2,
            startConsumers: false);
        var symbolId = symbols.GetOrAdd("SPY");

        try
        {
            await pipeline.DisposeAsync().AsTask().WaitAsync(timeout.Token);

            var trade = CreateTradeEvent("SPY", 1);
            var quote = CreateQuoteEvent("SPY", 2);
            var heartbeat = MarketEvent.Heartbeat(DateTimeOffset.UtcNow);
            var rawTrade = new RawTradeEvent(
                DateTimeOffset.UtcNow.UtcTicks,
                symbolId,
                100m,
                100L,
                (byte)AggressorSide.Buy,
                3L);
            var rawQuote = new RawQuoteEvent(
                DateTimeOffset.UtcNow.UtcTicks,
                symbolId,
                99.9m,
                100L,
                100.1m,
                100L,
                4L);

            pipeline.TryPublish(in trade).Should().BeFalse();
            pipeline.TryPublish(in quote).Should().BeFalse();
            pipeline.TryPublish(in heartbeat).Should().BeFalse();
            pipeline.TryPublishTrade(in rawTrade).Should().BeFalse();
            pipeline.TryPublishQuote(in rawQuote).Should().BeFalse();

            pipeline.TradeBufferCount.Should().Be(0);
            pipeline.QuoteBufferCount.Should().Be(0);
            pipeline.HotTradePublished.Should().Be(0);
            pipeline.HotQuotePublished.Should().Be(0);
            await slowPath.FlushAsync(timeout.Token);
            sink.ReceivedEvents.Should().BeEmpty();
        }
        finally
        {
            await pipeline.DisposeAsync();
            await slowPath.DisposeAsync();
        }
    }

    #endregion

    #region Helpers

    private static MarketEvent CreateTradeEvent(string symbol, int seq = 1)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.50m,
            Size: 100L,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: seq);
        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, seq);
    }

    private static MarketEvent CreateQuoteEvent(string symbol, int seq = 1)
    {
        var quote = BboQuotePayload.FromUpdate(
            timestamp: DateTimeOffset.UtcNow,
            symbol: symbol,
            bidPrice: 100m,
            bidSize: 100L,
            askPrice: 100.10m,
            askSize: 100L,
            sequenceNumber: seq);
        return MarketEvent.BboQuote(DateTimeOffset.UtcNow, symbol, quote, seq);
    }

    private static void PublishHotPathBatch(
        DualPathEventPipeline pipeline,
        MarketEventType eventType,
        int count)
    {
        for (var sequence = 1; sequence <= count; sequence++)
        {
            var evt = eventType == MarketEventType.Trade
                ? CreateTradeEvent("SPY", sequence)
                : CreateQuoteEvent("SPY", sequence);

            pipeline.TryPublish(in evt).Should().BeTrue();
        }
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        CancellationToken ct)
    {
        while (!condition())
            await Task.Delay(5, ct);
    }

    private static void AssertOnlyChannelClosedFailures(Exception failure)
    {
        var failures = failure is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [failure];

        failures.Should().NotBeEmpty();
        failures.Should().OnlyContain(static exception => exception is ChannelClosedException);
    }

    private async Task WaitForSinkCount(int expected, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_sink.ReceivedEvents.Count < expected && sw.Elapsed < timeout)
            await Task.Delay(5);
    }

    private async Task WaitForConsumed(long expected, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_pipeline.HotTradeConsumed < expected && sw.Elapsed < timeout)
            await Task.Delay(5);
    }

    private async Task WaitForQuoteConsumed(long expected, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_pipeline.HotQuoteConsumed < expected && sw.Elapsed < timeout)
            await Task.Delay(5);
    }

    private static async Task WaitForEventsAsync(MockStorageSink sink, int expectedCount, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sink.ReceivedEvents.Count < expectedCount && sw.Elapsed < timeout)
            await Task.Delay(5);
    }

    private sealed class GateStorageSink(Task releaseSignal) : IStorageSink
    {
        private readonly TaskCompletionSource<bool> _firstAppendStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<MarketEvent> _receivedEvents = [];
        private readonly object _sync = new();

        public Task FirstAppendStarted => _firstAppendStarted.Task;

        public IReadOnlyList<MarketEvent> ReceivedEvents
        {
            get
            {
                lock (_sync)
                {
                    return _receivedEvents.ToList();
                }
            }
        }

        public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            lock (_sync)
            {
                _receivedEvents.Add(evt);
            }

            _firstAppendStarted.TrySetResult(true);
            await releaseSignal.WaitAsync(ct).ConfigureAwait(false);
        }

        public Task FlushAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    #endregion
}
