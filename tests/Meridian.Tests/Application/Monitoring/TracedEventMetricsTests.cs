using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Application.Tracing;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

/// <summary>
/// Tests for the TracedEventMetrics decorator that wraps IEventMetrics
/// with OpenTelemetry-compatible System.Diagnostics.Metrics instrumentation.
/// Part of G2 (Observability Tracing with OpenTelemetry) improvement.
/// </summary>
public sealed class TracedEventMetricsTests
{
    private readonly FakeEventMetrics _inner;
    private readonly TracedEventMetrics _sut;

    public TracedEventMetricsTests()
    {
        _inner = new FakeEventMetrics();
        _sut = new TracedEventMetrics(_inner);
    }

    [Fact]
    public void Constructor_WithNullInner_ThrowsArgumentNullException()
    {
        var act = () => new TracedEventMetrics(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IncPublished_DelegatesToInner()
    {
        _sut.IncPublished();
        _inner.PublishedCalls.Should().Be(1);
    }

    [Fact]
    public void IncDropped_DelegatesToInner()
    {
        _sut.IncDropped();
        _inner.DroppedCalls.Should().Be(1);
    }

    [Fact]
    public void IncTrades_DelegatesToInner()
    {
        _sut.IncTrades();
        _inner.TradeCalls.Should().Be(1);
    }

    [Fact]
    public void IncDepthUpdates_DelegatesToInner()
    {
        _sut.IncDepthUpdates();
        _inner.DepthCalls.Should().Be(1);
    }

    [Fact]
    public void IncQuotes_DelegatesToInner()
    {
        _sut.IncQuotes();
        _inner.QuoteCalls.Should().Be(1);
    }

    [Fact]
    public void IncIntegrity_DelegatesToInner()
    {
        _sut.IncIntegrity();
        _inner.IntegrityCalls.Should().Be(1);
    }

    [Fact]
    public void IncHistoricalBars_DelegatesToInner()
    {
        _sut.IncHistoricalBars();
        _inner.HistoricalBarsCalls.Should().Be(1);
    }

    [Fact]
    public void Published_DelegatesToInner()
    {
        _inner.SetPublished(42);
        _sut.Published.Should().Be(42);
    }

    [Fact]
    public void Dropped_DelegatesToInner()
    {
        _inner.SetDropped(7);
        _sut.Dropped.Should().Be(7);
    }

    [Fact]
    public void GetSnapshot_DelegatesToInner()
    {
        _sut.IncPublished();
        _sut.IncPublished();
        _sut.IncTrades();

        var snapshot = _sut.GetSnapshot();
        snapshot.Should().NotBeNull();
    }

    [Fact]
    public void Reset_DelegatesToInner()
    {
        _sut.IncPublished();
        _sut.Reset();
        _inner.ResetCalled.Should().BeTrue();
    }

    [Fact]
    public void RecordLatency_DelegatesToInner()
    {
        var timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        _sut.RecordLatency(timestamp);
        _inner.LatencyCalls.Should().Be(1);
    }

    [Fact]
    public void MultipleIncrements_AllDelegateCorrectly()
    {
        for (int i = 0; i < 100; i++)
        {
            _sut.IncPublished();
            _sut.IncTrades();
        }

        _inner.PublishedCalls.Should().Be(100);
        _inner.TradeCalls.Should().Be(100);
    }

    /// <summary>
    /// Simple fake IEventMetrics for testing the decorator pattern.
    /// </summary>
    private sealed class FakeEventMetrics : IEventMetrics
    {
        private long _published;
        private long _dropped;

        public int PublishedCalls { get; private set; }
        public int DroppedCalls { get; private set; }
        public int IntegrityCalls { get; private set; }
        public int TradeCalls { get; private set; }
        public int DepthCalls { get; private set; }
        public int QuoteCalls { get; private set; }
        public int HistoricalBarsCalls { get; private set; }
        public int LatencyCalls { get; private set; }
        public bool ResetCalled { get; private set; }

        public long Published => _published;
        public long Dropped => _dropped;
        public long Integrity => 0;
        public long Trades => 0;
        public long DepthUpdates => 0;
        public long Quotes => 0;
        public long HistoricalBars => 0;
        public double EventsPerSecond => 0;
        public double DropRate => 0;

        public void SetPublished(long value) => _published = value;
        public void SetDropped(long value) => _dropped = value;

        public void IncPublished() => PublishedCalls++;
        public void IncDropped() => DroppedCalls++;
        public void IncIntegrity() => IntegrityCalls++;
        public void IncTrades() => TradeCalls++;
        public void IncDepthUpdates() => DepthCalls++;
        public void IncQuotes() => QuoteCalls++;
        public void IncHistoricalBars() => HistoricalBarsCalls++;
        public void RecordLatency(long startTimestamp) => LatencyCalls++;
        public void Reset() => ResetCalled = true;
        public MetricsSnapshot GetSnapshot() => new(
            Published: 0, Dropped: 0, Integrity: 0, Trades: 0, DepthUpdates: 0,
            Quotes: 0, HistoricalBars: 0, EventsPerSecond: 0, TradesPerSecond: 0,
            DepthUpdatesPerSecond: 0, HistoricalBarsPerSecond: 0, DropRate: 0,
            AverageLatencyUs: 0, MinLatencyUs: 0, MaxLatencyUs: 0, LatencySampleCount: 0,
            Gc0Collections: 0, Gc1Collections: 0, Gc2Collections: 0,
            Gc0Delta: 0, Gc1Delta: 0, Gc2Delta: 0,
            MemoryUsageMb: 0, HeapSizeMb: 0, Timestamp: DateTimeOffset.UtcNow);
    }
}
