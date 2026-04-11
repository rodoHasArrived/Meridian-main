using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Interfaces;
using Moq;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Integration tests for the EventPipeline with mock storage sinks.
/// Tests backpressure, throughput, statistics, and lifecycle management.
/// </summary>
public class EventPipelineTests : IAsyncLifetime
{
    private MockStorageSink _mockSink = null!;
    private EventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _mockSink = new MockStorageSink();
        _pipeline = new EventPipeline(
            _mockSink,
            capacity: 1000,
            flushInterval: TimeSpan.FromMilliseconds(100),
            enablePeriodicFlush: false); // Disable for deterministic testing
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    #region Basic Publishing Tests

    [Fact]
    public async Task TryPublish_SingleEvent_EventIsConsumed()
    {
        // Arrange
        var trade = CreateTradeEvent("SPY");

        // Act
        var published = _pipeline.TryPublish(trade);

        // Allow consumer to process
        await WaitForConsumption(expectedCount: 1);

        // Assert
        published.Should().BeTrue();
        _mockSink.ReceivedEvents.Should().ContainSingle()
            .Which.Symbol.Should().Be("SPY");
    }

    [Fact]
    public async Task TryPublish_MultipleEvents_AllEventsConsumed()
    {
        // Arrange
        var events = Enumerable.Range(0, 100)
            .Select(i => CreateTradeEvent($"SYM{i}"))
            .ToList();

        // Act
        foreach (var evt in events)
        {
            _pipeline.TryPublish(evt);
        }

        await WaitForConsumption(expectedCount: 100);

        // Assert
        _mockSink.ReceivedEvents.Should().HaveCount(100);
    }

    [Fact]
    public async Task PublishAsync_SingleEvent_EventIsConsumed()
    {
        // Arrange
        var trade = CreateTradeEvent("AAPL");

        // Act
        await _pipeline.PublishAsync(trade);
        await WaitForConsumption(expectedCount: 1);

        // Assert
        _mockSink.ReceivedEvents.Should().ContainSingle()
            .Which.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public async Task TryPublish_DifferentEventTypes_AllTypesHandled()
    {
        // Arrange
        var trade = CreateTradeEvent("SPY");
        var quote = CreateQuoteEvent("SPY");

        // Act
        _pipeline.TryPublish(trade);
        _pipeline.TryPublish(quote);

        await WaitForConsumption(expectedCount: 2);

        // Assert
        _mockSink.ReceivedEvents.Should().HaveCount(2);
        _mockSink.ReceivedEvents.Should().Contain(e => e.Type == MarketEventType.Trade);
        _mockSink.ReceivedEvents.Should().Contain(e => e.Type == MarketEventType.BboQuote);
    }

    [Fact]
    public async Task MultiConsumerMode_AllowsConcurrentSlowPathAppends()
    {
        var releaseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sink = new ConcurrencyTrackingSink(releaseTcs.Task);
        await using var pipeline = new EventPipeline(
            sink,
            capacity: 1_000,
            batchSize: 1,
            enablePeriodicFlush: false,
            consumerCount: 2);

        // Publish enough events to ensure both consumers get work.
        // With batchSize:1, _maxAdaptiveBatchSize = 4 (batchSize * 4).
        // Publishing 10 events guarantees at least one event for each consumer
        // even if consumer 1 drains a full batch of 4 first.
        for (var i = 0; i < 10; i++)
        {
            pipeline.TryPublish(CreateTradeEvent($"SYM{i}"));
        }

        // Wait until at least 2 consumers are blocked in the sink simultaneously — this
        // proves they overlap before we let them proceed, making the assertion deterministic.
        await sink.ConcurrencyReachedTask.WaitAsync(TimeSpan.FromSeconds(5));
        releaseTcs.SetResult(true);

        await pipeline.FlushAsync();

        sink.MaxConcurrentAppends.Should().BeGreaterThan(1,
            "multiple consumers should overlap sink appends when advanced persistence features are disabled");
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task Statistics_AfterPublishing_TracksCounts()
    {
        // Arrange
        for (int i = 0; i < 50; i++)
        {
            _pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        await WaitForConsumption(expectedCount: 50);

        // Act
        var stats = _pipeline.GetStatistics();

        // Assert
        stats.PublishedCount.Should().Be(50);
        stats.ConsumedCount.Should().Be(50);
        stats.DroppedCount.Should().Be(0);
    }

    [Fact]
    public void PublishedCount_Increments_OnSuccessfulPublish()
    {
        // Arrange & Act
        _pipeline.TryPublish(CreateTradeEvent("SPY"));
        _pipeline.TryPublish(CreateTradeEvent("AAPL"));
        _pipeline.TryPublish(CreateTradeEvent("GOOGL"));

        // Assert
        _pipeline.PublishedCount.Should().Be(3);
    }

    [Fact]
    public async Task ConsumedCount_Increments_AsEventsAreProcessed()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        // Act
        await WaitForConsumption(expectedCount: 10);

        // Assert
        _pipeline.ConsumedCount.Should().Be(10);
    }

    [Fact]
    public async Task AverageProcessingTimeUs_Calculated_AfterProcessing()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            _pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        await WaitForConsumption(expectedCount: 100);

        // Act
        var avgTime = _pipeline.AverageProcessingTimeUs;

        // Assert
        avgTime.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QueueUtilization_ReflectsQueueFill()
    {
        // Arrange - batchSize=1 so the consumer processes one event at a time.
        // The BlockingStorageSink holds the consumer on the first event while
        // the remaining 49 events stay in the channel, making utilization > 0.
        var releaseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sink = new BlockingStorageSink(releaseTcs.Task);
        await using var pipeline = new EventPipeline(sink, capacity: 100, batchSize: 1, enablePeriodicFlush: false);

        // Act - Publish 50 events
        for (int i = 0; i < 50; i++)
        {
            pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        // Wait until the consumer has started processing the first event and is blocked
        await sink.WaitForFirstBlockAsync(TimeSpan.FromSeconds(5));

        // Assert - remaining 49 events are still in the channel
        pipeline.QueueUtilization.Should().BeGreaterThan(0);

        // Release the sink so the pipeline can drain and dispose cleanly
        releaseTcs.SetResult(true);
    }

    [Fact]
    public async Task PeakQueueSize_TracksHighWaterMark()
    {
        // Arrange - Use a slow consumer so events queue up
        await using var sink = new MockStorageSink { ProcessingDelay = TimeSpan.FromMilliseconds(50) };
        await using var pipeline = new EventPipeline(sink, capacity: 1000, enablePeriodicFlush: false);

        // Act - Publish events faster than they can be consumed
        for (int i = 0; i < 100; i++)
        {
            pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        // Wait until at least some events are consumed (proves pipeline ran)
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (pipeline.ConsumedCount < 1 && sw.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(1);
        }

        // Assert - Peak should have been recorded when events were queued
        pipeline.PeakQueueSize.Should().BeGreaterThan(0);
    }

    #endregion

    #region Backpressure Tests

    [Fact]
    public async Task TryPublish_WhenQueueFull_DropOldestMode_DropsEvents()
    {
        // Arrange - Small capacity with slow consumer
        await using var sink = new MockStorageSink { ProcessingDelay = TimeSpan.FromMilliseconds(100) };
        await using var pipeline = new EventPipeline(
            sink,
            capacity: 10,
            fullMode: BoundedChannelFullMode.DropOldest,
            enablePeriodicFlush: false);

        // Act - Publish more events than capacity
        // When publishing 100 events quickly, the channel (capacity=10) will drop the oldest
        for (int i = 0; i < 100; i++)
        {
            pipeline.TryPublish(CreateTradeEvent($"SYM{i}"));
        }

        // Wait for processing to complete - wait for a reasonable number of events
        // With capacity=10 and DropOldest, we expect roughly capacity + small epsilon
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var targetCount = 15; // capacity + small buffer for in-flight processing
        while (sink.ReceivedEvents.Count < targetCount && stopwatch.Elapsed < TimeSpan.FromSeconds(2))
        {
            await Task.Delay(1);
        }
        await pipeline.FlushAsync();

        // Assert - With DropOldest mode, all TryPublish calls succeed, so DroppedCount is 0
        // But sink should receive approximately capacity worth of events (latest ones)
        pipeline.DroppedCount.Should().Be(0, "DropOldest mode succeeds on TryPublish and doesn't increment DroppedCount");
        sink.ReceivedEvents.Count.Should().BeLessThan(100, "some events should have been dropped by the channel");

        // The events received should be the latest ones (high symbol numbers)
        // because oldest were dropped
        var receivedSymbols = sink.ReceivedEvents.Select(e => e.Symbol).ToList();
        var highSymbolCount = receivedSymbols.Count(s => int.Parse(s.Replace("SYM", "")) >= 90);
        highSymbolCount.Should().BeGreaterThan(0, "should have received some of the latest events (SYM90+)");
    }

    [Fact]
    public async Task TryPublish_WhenQueueFull_DropWriteMode_ReturnsFalse()
    {
        // Arrange - Use a blocking sink so the consumer stalls and the queue fills up.
        var releaseConsumer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sink = new BlockingStorageSink(releaseConsumer.Task);
        await using var pipeline = new EventPipeline(
            sink,
            capacity: 5,
            fullMode: BoundedChannelFullMode.DropWrite,
            enablePeriodicFlush: false);

        // Publish one event to trigger the consumer.
        pipeline.TryPublish(CreateTradeEvent("SPY"));

        // Wait until the consumer has started and is blocked on the first event.
        // The consumer drains the entire batch from the channel before processing,
        // so the channel is empty once the consumer is blocked.
        await sink.WaitForFirstBlockAsync(TimeSpan.FromSeconds(2));

        // Re-fill the channel to capacity now that the consumer is blocked and cannot drain it.
        for (int i = 0; i < 5; i++)
        {
            pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        // Publish additional events to overflow the now-full queue.
        var dropCount = 0;
        for (int i = 0; i < 10; i++)
        {
            if (!pipeline.TryPublish(CreateTradeEvent("SPY")))
                dropCount++;
        }

        // Assert - At least some additional publishes must have been rejected.
        pipeline.DroppedCount.Should().BeGreaterThan(0,
            "DropWrite mode should reject events when the channel is full");
        dropCount.Should().BeGreaterThan(0, "TryPublish should return false for rejected events");

        // Cleanup
        releaseConsumer.TrySetResult(true);
    }

    #endregion

    #region Flush Tests

    [Fact]
    public async Task FlushAsync_FlushesUnderlyingSink()
    {
        // Arrange
        _pipeline.TryPublish(CreateTradeEvent("SPY"));
        await WaitForConsumption(expectedCount: 1);

        // Act
        await _pipeline.FlushAsync();

        // Assert
        _mockSink.FlushCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task TimeSinceLastFlush_UpdatesAfterFlush()
    {
        // Arrange
        await Task.Delay(5);
        var timeBefore = _pipeline.TimeSinceLastFlush;

        // Act
        await _pipeline.FlushAsync();
        var timeAfter = _pipeline.TimeSinceLastFlush;

        // Assert
        timeAfter.Should().BeLessThan(timeBefore);
    }

    [Fact]
    public async Task PeriodicFlush_WhenEnabled_FlushesAutomatically()
    {
        // Arrange
        await using var sink = new MockStorageSink();
        await using var pipeline = new EventPipeline(
            sink,
            capacity: 1000,
            flushInterval: TimeSpan.FromMilliseconds(50),
            enablePeriodicFlush: true);

        pipeline.TryPublish(CreateTradeEvent("SPY"));

        // Act - Wait for periodic flush (generous margin for CI runners)
        await Task.Delay(300);

        // Assert
        sink.FlushCount.Should().BeGreaterThanOrEqualTo(1);
    }

    #endregion

    #region Constructor Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task Constructor_WithInvalidCapacity_ThrowsArgumentOutOfRangeException(int invalidCapacity)
    {
        // Arrange
        await using var sink = new MockStorageSink();

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new EventPipeline(sink, capacity: invalidCapacity));

        exception.ParamName.Should().Be("capacity");
        exception.ActualValue.Should().Be(invalidCapacity);
    }

    [Fact]
    public async Task Constructor_WithNullSink_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new EventPipeline(null!));
        await Task.CompletedTask;
    }

    #endregion

    #region Lifecycle Tests

    [Fact]
    public async Task Complete_SignalsNoMoreEvents()
    {
        // Arrange
        _pipeline.TryPublish(CreateTradeEvent("SPY"));

        // Act
        _pipeline.Complete();

        // Wait for pipeline to drain
        await Task.Delay(5);

        // Assert - Further publishes may fail
        // The channel is marked complete
        _mockSink.ReceivedEvents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_FlushesFinalEvents()
    {
        // Arrange
        await using var sink = new MockStorageSink();
        var pipeline = new EventPipeline(sink, capacity: 1000, enablePeriodicFlush: false);

        pipeline.TryPublish(CreateTradeEvent("SPY"));
        pipeline.TryPublish(CreateTradeEvent("AAPL"));

        // Act
        await pipeline.DisposeAsync();

        // Assert - Sink should have received a final flush
        sink.FlushCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task DisposeAsync_ProcessesPendingEvents()
    {
        // Arrange
        await using var sink = new MockStorageSink();
        var pipeline = new EventPipeline(sink, capacity: 1000, enablePeriodicFlush: false);

        for (int i = 0; i < 10; i++)
        {
            pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        // Give consumer time to start processing before disposal
        await Task.Delay(5);

        // Act
        await pipeline.DisposeAsync();

        // Assert - All events should be processed
        sink.ReceivedEvents.Should().HaveCount(10);
    }

    #endregion

    #region Shutdown Timeout Tests

    [Fact]
    public async Task DisposeAsync_WhenSinkFlushBlocks_CompletesWithinTimeout()
    {
        // Arrange - Use a sink whose flush blocks until its CancellationToken is cancelled.
        // Before the fix, the pipeline passed CancellationToken.None to the final flush,
        // meaning it would hang indefinitely. After the fix, a timeout token is used.
        // Use a short finalFlushTimeout (1s) so the test completes quickly.
        await using var sink = new CancellationAwareSlowFlushSink();
        var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false,
            finalFlushTimeout: TimeSpan.FromSeconds(1));

        pipeline.TryPublish(CreateTradeEvent("SPY"));
        await Task.Delay(5); // Let consumer process the event

        // Act - Dispose should not hang; the final flush will be cancelled by finalFlushTimeout
        var disposeTask = pipeline.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(3)));

        // Assert - Disposal must complete (not hang indefinitely)
        completed.Should().Be(disposeTask,
            "DisposeAsync should complete within the timeout, not hang indefinitely");
    }

    [Fact]
    public async Task DisposeAsync_FinalFlush_ReceivesCancellableToken()
    {
        // Arrange - Sink that captures the CancellationToken passed to FlushAsync
        var sink = new TokenCapturingSink();
        var pipeline = new EventPipeline(sink, capacity: 100, enablePeriodicFlush: false);

        pipeline.TryPublish(CreateTradeEvent("SPY"));
        await Task.Delay(5); // Let consumer process

        // Act
        await pipeline.DisposeAsync();

        // Assert - The final flush should receive a cancellable token (not CancellationToken.None)
        sink.LastFlushToken.Should().NotBe(default(CancellationToken),
            "Final flush should receive a timeout-based CancellationToken, not CancellationToken.None");
        sink.LastFlushToken.CanBeCanceled.Should().BeTrue(
            "The CancellationToken passed to the final flush should be cancellable (tied to a timeout)");
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task PublishAsync_WithCancellation_ThrowsWhenCancelled()
    {
        // Arrange - Use a blocking sink and Wait mode so PublishAsync blocks on the third write.
        var releaseConsumer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sink = new BlockingStorageSink(releaseConsumer.Task);
        await using var pipeline = new EventPipeline(sink, capacity: 2, fullMode: BoundedChannelFullMode.Wait, enablePeriodicFlush: false);

        // Publish one event to trigger the consumer.
        pipeline.TryPublish(CreateTradeEvent("SPY"));

        // Wait until the consumer has started and is blocked on the first event.
        // The consumer drains the entire batch from the channel before processing,
        // so the channel is empty once the consumer is blocked.
        await sink.WaitForFirstBlockAsync(TimeSpan.FromSeconds(2));

        // Re-fill the channel to capacity (2 items) now that the consumer is blocked.
        pipeline.TryPublish(CreateTradeEvent("SPY"));
        pipeline.TryPublish(CreateTradeEvent("MSFT"));

        // The channel is now full. A further PublishAsync must block because the channel
        // is full in Wait mode, and the cancellation token fires after 200 ms.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pipeline.PublishAsync(CreateTradeEvent("AAPL"), cts.Token));

        // Cleanup
        releaseConsumer.TrySetResult(true);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task Consumer_WhenSinkThrows_ContinuesProcessing()
    {
        // Arrange
        await using var sink = new MockStorageSink { ShouldThrowOnAppend = true, ThrowAfterCount = 5 };
        await using var pipeline = new EventPipeline(sink, capacity: 1000, enablePeriodicFlush: false);

        // Act - Publish events, some will cause exceptions
        for (int i = 0; i < 10; i++)
        {
            pipeline.TryPublish(CreateTradeEvent($"SYM{i}"));
        }

        await Task.Delay(10);

        // Assert - Pipeline should still be alive and processing
        // At least some events should have been processed before the throw
        sink.ReceivedEvents.Count.Should().BeGreaterThanOrEqualTo(5);
    }

    #endregion

    #region PublishResult Tests

    [Fact]
    public void TryPublishWithResult_WhenAccepted_ReturnsAccepted()
    {
        // Arrange
        var evt = CreateTradeEvent("SPY");

        // Act
        var result = _pipeline.TryPublishWithResult(in evt);

        // Assert
        result.Should().Be(PublishResult.Accepted);
    }

    [Fact]
    public async Task TryPublishWithResult_WhenQueueFull_ReturnsDropped()
    {
        // Arrange — tiny pipeline in DropWrite mode so it fills immediately,
        // and a blocking sink + batchSize: 1 so the queue stays at capacity.
        var releaseConsumer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sink = new BlockingStorageSink(releaseConsumer.Task);
        await using var pipeline = new EventPipeline(
            sink,
            capacity: 2,
            batchSize: 1,
            fullMode: BoundedChannelFullMode.DropWrite,
            enablePeriodicFlush: false);

        // Trigger the consumer and wait for it to block so the queue won't be drained.
        pipeline.TryPublish(CreateTradeEvent("SPY"));
        await sink.WaitForFirstBlockAsync(TimeSpan.FromSeconds(2));

        // Fill the channel (consumer is blocked so these stay queued)
        pipeline.TryPublish(CreateTradeEvent("SPY"));
        pipeline.TryPublish(CreateTradeEvent("SPY"));

        // Act — one more publish with no room
        var result = pipeline.TryPublishWithResult(CreateTradeEvent("SPY"));

        // Assert
        result.Should().Be(PublishResult.Dropped);

        // Cleanup - release the blocked consumer so the pipeline can drain during disposal
        releaseConsumer.TrySetResult(true);
    }

    #endregion

    #region Throughput Tests

    [Fact]
    public async Task HighThroughput_ProcessesManyEventsQuickly()
    {
        // Arrange
        const int eventCount = 10000;
        await using var sink = new MockStorageSink();
        await using var pipeline = new EventPipeline(sink, capacity: 100000, enablePeriodicFlush: false);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act
        for (int i = 0; i < eventCount; i++)
        {
            pipeline.TryPublish(CreateTradeEvent("SPY"));
        }

        // Wait for all to be consumed
        while (pipeline.ConsumedCount < eventCount && sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(1);
        }

        sw.Stop();

        // Assert
        pipeline.ConsumedCount.Should().Be(eventCount);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete well within 5 seconds

        var eventsPerSecond = eventCount / (sw.ElapsedMilliseconds / 1000.0);
        eventsPerSecond.Should().BeGreaterThan(1000); // At least 1k events/sec
    }

    #endregion

    #region Helper Methods

    private static MarketEvent CreateTradeEvent(string symbol)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            Venue: "NYSE");

        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade);
    }

    private static MarketEvent CreateQuoteEvent(string symbol)
    {
        var quote = BboQuotePayload.FromUpdate(
            new MarketQuoteUpdate(
                Timestamp: DateTimeOffset.UtcNow,
                Symbol: symbol,
                BidPrice: 100.00m,
                BidSize: 100L,
                AskPrice: 100.10m,
                AskSize: 200L),
            seq: 1);

        return MarketEvent.BboQuote(DateTimeOffset.UtcNow, symbol, quote);
    }

    private async Task WaitForConsumption(int expectedCount, int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (_mockSink.ReceivedEvents.Count < expectedCount && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(1);
        }
    }

    #endregion
}

/// <summary>
/// Mock storage sink for testing the EventPipeline.
/// </summary>
internal sealed class MockStorageSink : IStorageSink
{
    private readonly List<MarketEvent> _receivedEvents = new();
    private readonly object _lock = new();
    private int _appendCount;

    public IReadOnlyList<MarketEvent> ReceivedEvents
    {
        get
        {
            lock (_lock)
            {
                return _receivedEvents.ToList();
            }
        }
    }

    public int FlushCount { get; private set; }

    public TimeSpan ProcessingDelay { get; set; } = TimeSpan.Zero;

    public bool ShouldThrowOnAppend { get; set; }

    public int ThrowAfterCount { get; set; } = int.MaxValue;

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        if (ProcessingDelay > TimeSpan.Zero)
        {
            await Task.Delay(ProcessingDelay, ct);
        }

        var count = Interlocked.Increment(ref _appendCount);

        if (ShouldThrowOnAppend && count > ThrowAfterCount)
        {
            throw new InvalidOperationException("Simulated sink failure");
        }

        lock (_lock)
        {
            _receivedEvents.Add(evt);
        }
    }

    public Task FlushAsync(CancellationToken ct = default)
    {
        FlushCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Mock sink whose FlushAsync blocks until the provided CancellationToken is cancelled.
/// Used to verify that the pipeline's final flush uses a cancellable token (not CancellationToken.None).
/// </summary>
internal sealed class CancellationAwareSlowFlushSink : IStorageSink
{
    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        await Task.CompletedTask;
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        // Block until cancelled - simulates a hung I/O operation that respects cancellation
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock sink that captures the CancellationToken passed to FlushAsync
/// for assertion in tests.
/// </summary>
internal sealed class TokenCapturingSink : IStorageSink
{
    public CancellationToken LastFlushToken { get; private set; }

    public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default) => ValueTask.CompletedTask;

    public Task FlushAsync(CancellationToken ct = default)
    {
        LastFlushToken = ct;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Mock sink that blocks each <see cref="AppendAsync"/> call until an external
/// <see cref="Task"/> is completed. This gives tests deterministic control over
/// when the pipeline consumer can proceed, avoiding all timing-dependent behaviour.
/// </summary>
internal sealed class BlockingStorageSink : IStorageSink
{
    private readonly Task _releaseSignal;
    private readonly TaskCompletionSource<bool> _firstBlockReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _receivedCount;

    public BlockingStorageSink(Task releaseSignal)
    {
        _releaseSignal = releaseSignal;
    }

    /// <summary>
    /// Waits until the consumer has entered the blocking sink for the first time.
    /// </summary>
    public Task WaitForFirstBlockAsync(TimeSpan timeout)
    {
        return Task.WhenAny(_firstBlockReached.Task, Task.Delay(timeout));
    }

    public int ReceivedCount => Volatile.Read(ref _receivedCount);

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        // Signal that the consumer has arrived
        _firstBlockReached.TrySetResult(true);
        Interlocked.Increment(ref _receivedCount);

        // Block until released or cancelled
        await _releaseSignal.WaitAsync(ct).ConfigureAwait(false);
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ConcurrencyTrackingSink : IStorageSink
{
    private readonly Task _releaseSignal;
    private readonly TaskCompletionSource _concurrencyReached =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _receivedCount;
    private int _activeAppends;
    private int _maxConcurrentAppends;

    public ConcurrencyTrackingSink(Task releaseSignal)
    {
        _releaseSignal = releaseSignal;
    }

    /// <summary>
    /// Completes when at least 2 concurrent appends have been observed simultaneously.
    /// Await this before releasing the sink to make concurrency assertions deterministic.
    /// </summary>
    public Task ConcurrencyReachedTask => _concurrencyReached.Task;

    public int ReceivedCount => Volatile.Read(ref _receivedCount);
    public int MaxConcurrentAppends => Volatile.Read(ref _maxConcurrentAppends);

    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
    {
        var active = Interlocked.Increment(ref _activeAppends);
        UpdateMax(active);

        if (active >= 2)
            _concurrencyReached.TrySetResult();

        try
        {
            // Block until released — this keeps each consumer inside AppendAsync long
            // enough for a second consumer to also enter, proving concurrent overlap.
            await _releaseSignal.WaitAsync(ct).ConfigureAwait(false);
            Interlocked.Increment(ref _receivedCount);
        }
        finally
        {
            Interlocked.Decrement(ref _activeAppends);
        }
    }

    public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void UpdateMax(int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maxConcurrentAppends);
            if (candidate <= current)
                return;

            if (Interlocked.CompareExchange(ref _maxConcurrentAppends, candidate, current) == current)
                return;
        }
    }
}
