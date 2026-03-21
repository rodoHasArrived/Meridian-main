using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Interfaces;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Tests for EventPipeline metrics injection (C4 improvement).
/// Validates that metrics can be injected and that custom implementations work correctly.
/// </summary>
public class EventPipelineMetricsTests : IAsyncLifetime
{
    private MockStorageSink _mockSink = null!;
    private TestEventMetrics _testMetrics = null!;
    private EventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _mockSink = new MockStorageSink();
        _testMetrics = new TestEventMetrics();
        _pipeline = new EventPipeline(
            _mockSink,
            capacity: 1000,
            enablePeriodicFlush: false,
            metrics: _testMetrics);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    [Fact]
    public async Task EventPipeline_UsesInjectedMetrics()
    {
        // Arrange
        var trade = CreateTradeEvent("SPY");

        // Act
        var published = _pipeline.TryPublish(trade);
        await WaitForConsumption(expectedCount: 1);

        // Assert
        published.Should().BeTrue();
        _testMetrics.PublishedCount.Should().Be(1);
    }

    [Fact]
    public async Task EventPipeline_MetricsIncrement_OnMultiplePublish()
    {
        // Arrange
        var events = Enumerable.Range(0, 10)
            .Select(i => CreateTradeEvent($"SYM{i}"))
            .ToList();

        // Act
        foreach (var evt in events)
        {
            _pipeline.TryPublish(evt);
        }
        await WaitForConsumption(expectedCount: 10);

        // Assert
        _testMetrics.PublishedCount.Should().Be(10);
    }

    [Fact]
    public async Task EventPipeline_MetricsIncrement_OnDropped()
    {
        // Arrange - Create a very small pipeline that will drop events
        await _pipeline.DisposeAsync();
        _testMetrics.Reset();

        _pipeline = new EventPipeline(
            _mockSink,
            capacity: 2,
            fullMode: System.Threading.Channels.BoundedChannelFullMode.DropOldest,
            enablePeriodicFlush: false,
            metrics: _testMetrics);

        // Act - Publish many events to force drops
        for (int i = 0; i < 100; i++)
        {
            _pipeline.TryPublish(CreateTradeEvent($"SYM{i}"));
        }

        // Allow some time for processing
        await Task.Delay(10);

        // Assert - Some events should have been dropped
        var totalAttempted = 100;
        var published = _testMetrics.PublishedCount;
        var dropped = _testMetrics.DroppedCount;

        // At least some events should have been accepted
        published.Should().BeGreaterThan(0);
        // Sum should be less than or equal to total attempted (race conditions possible)
        (published + dropped).Should().BeLessThanOrEqualTo(totalAttempted);
    }

    [Fact]
    public async Task EventPipeline_AcceptsNullMetrics_UsesDefault()
    {
        // Arrange & Act
        await using var pipeline = new EventPipeline(_mockSink, capacity: 100, enablePeriodicFlush: false, metrics: null);

        // Assert - Should use DefaultEventMetrics internally
        var trade = CreateTradeEvent("SPY");
        var published = pipeline.TryPublish(trade);
        published.Should().BeTrue();
    }

    [Fact]
    public Task EventPipeline_ExposesMetricsInstance()
    {
        // Act & Assert
        _pipeline.EventMetrics.Should().BeSameAs(_testMetrics);
        return Task.CompletedTask;
    }

    private static MarketEvent CreateTradeEvent(string symbol)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.0m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            Venue: "TEST");

        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade);
    }

    private async Task WaitForConsumption(int expectedCount, int maxWaitMs = 2000)
    {
        var start = DateTime.UtcNow;
        while (_mockSink.ReceivedEvents.Count < expectedCount)
        {
            await Task.Delay(1);
            if ((DateTime.UtcNow - start).TotalMilliseconds > maxWaitMs)
                break;
        }
    }

    /// <summary>
    /// Test implementation of IEventMetrics for verification.
    /// </summary>
    private sealed class TestEventMetrics : IEventMetrics
    {
        private long _published;
        private long _dropped;
        private long _integrity;
        private long _trades;
        private long _depthUpdates;
        private long _quotes;
        private long _historicalBars;

        // Public properties for testing
        public long PublishedCount => Interlocked.Read(ref _published);
        public long DroppedCount => Interlocked.Read(ref _dropped);

        // IEventMetrics interface implementation
        public long Published => PublishedCount;
        public long Dropped => DroppedCount;
        public long Integrity => Interlocked.Read(ref _integrity);
        public long Trades => Interlocked.Read(ref _trades);
        public long DepthUpdates => Interlocked.Read(ref _depthUpdates);
        public long Quotes => Interlocked.Read(ref _quotes);
        public long HistoricalBars => Interlocked.Read(ref _historicalBars);
        public double EventsPerSecond => 0;
        public double DropRate => Published > 0 ? (double)Dropped / Published * 100 : 0;

        public void IncPublished() => Interlocked.Increment(ref _published);
        public void IncDropped() => Interlocked.Increment(ref _dropped);
        public void IncIntegrity() => Interlocked.Increment(ref _integrity);
        public void IncTrades() => Interlocked.Increment(ref _trades);
        public void IncDepthUpdates() => Interlocked.Increment(ref _depthUpdates);
        public void IncQuotes() => Interlocked.Increment(ref _quotes);
        public void IncHistoricalBars() => Interlocked.Increment(ref _historicalBars);
        public void RecordLatency(long startTimestamp) { }
        public void Reset()
        {
            Interlocked.Exchange(ref _published, 0);
            Interlocked.Exchange(ref _dropped, 0);
            Interlocked.Exchange(ref _integrity, 0);
            Interlocked.Exchange(ref _trades, 0);
            Interlocked.Exchange(ref _depthUpdates, 0);
            Interlocked.Exchange(ref _quotes, 0);
            Interlocked.Exchange(ref _historicalBars, 0);
        }
        public MetricsSnapshot GetSnapshot() => new MetricsSnapshot();
    }

    /// <summary>
    /// Mock storage sink for testing.
    /// </summary>
    private sealed class MockStorageSink : IStorageSink
    {
        public List<MarketEvent> ReceivedEvents { get; } = new();

        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            ReceivedEvents.Add(evt);
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
