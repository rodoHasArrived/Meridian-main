using FluentAssertions;
using Meridian.Platform.Tracing;
using Xunit;

namespace Meridian.Tests.Platform.Tracing;

public sealed class DefaultEventMetricsTests : IDisposable
{
    private readonly DefaultEventMetrics _metrics = new();

    public DefaultEventMetricsTests()
    {
        _metrics.Reset();
    }

    public void Dispose()
    {
        _metrics.Reset();
    }

    [Fact]
    public void IncrementMethods_ShouldUpdateSharedSnapshot()
    {
        _metrics.IncPublished();
        _metrics.IncDropped();
        _metrics.IncIntegrity();
        _metrics.IncTrades();
        _metrics.IncDepthUpdates();
        _metrics.IncQuotes();
        _metrics.IncHistoricalBars();

        var snapshot = _metrics.GetSnapshot();

        snapshot.Published.Should().Be(1);
        snapshot.Dropped.Should().Be(1);
        snapshot.Integrity.Should().Be(1);
        snapshot.Trades.Should().Be(1);
        snapshot.DepthUpdates.Should().Be(1);
        snapshot.Quotes.Should().Be(1);
        snapshot.HistoricalBars.Should().Be(1);
    }

    [Fact]
    public void Reset_ShouldClearCounters()
    {
        _metrics.IncPublished();
        _metrics.IncDropped();

        _metrics.Reset();

        _metrics.Published.Should().Be(0);
        _metrics.Dropped.Should().Be(0);
        _metrics.GetSnapshot().Published.Should().Be(0);
    }
}
