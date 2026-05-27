using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests;

public class SessionStatsCollectorTests
{
    [Fact]
    public void OnTrade_FirstTrade_RecordsOpenHighLowAndLast()
    {
        var collector = new SessionStatsCollector();
        var ts = new DateTimeOffset(2026, 5, 8, 13, 30, 0, TimeSpan.Zero);

        collector.OnTrade("AAPL", price: 187.5m, size: 100, timestamp: ts);

        var stats = collector.TryGet("AAPL");
        stats.Should().NotBeNull();
        stats!.Open.Should().Be(187.5m);
        stats.High.Should().Be(187.5m);
        stats.Low.Should().Be(187.5m);
        stats.Last.Should().Be(187.5m);
        stats.Volume.Should().Be(100);
        stats.TradeCount.Should().Be(1);
        stats.Vwap.Should().Be(187.5m);
        stats.FirstTradeAt.Should().Be(ts);
        stats.LastTradeAt.Should().Be(ts);
        stats.Change.Should().Be(0m);
        stats.ChangePercent.Should().Be(0m);
    }

    [Fact]
    public void OnTrade_MultipleTrades_ComputesHighLowVolumeAndVwap()
    {
        var collector = new SessionStatsCollector();
        var ts0 = new DateTimeOffset(2026, 5, 8, 13, 30, 0, TimeSpan.Zero);

        collector.OnTrade("AAPL", price: 100m, size: 10, timestamp: ts0);
        collector.OnTrade("AAPL", price: 95m, size: 20, timestamp: ts0.AddMinutes(1));
        collector.OnTrade("AAPL", price: 110m, size: 10, timestamp: ts0.AddMinutes(2));

        var stats = collector.TryGet("AAPL")!;
        stats.Open.Should().Be(100m);
        stats.High.Should().Be(110m);
        stats.Low.Should().Be(95m);
        stats.Last.Should().Be(110m);
        stats.Volume.Should().Be(40);
        stats.TradeCount.Should().Be(3);
        // VWAP = (100*10 + 95*20 + 110*10) / 40 = 4000 / 40 = 100
        stats.Vwap.Should().Be(100m);
        stats.Change.Should().Be(10m);
        stats.ChangePercent.Should().Be(10m);
    }

    [Fact]
    public void OnTrade_WithZeroOrNegativePrice_IsIgnored()
    {
        var collector = new SessionStatsCollector();
        var ts = new DateTimeOffset(2026, 5, 8, 13, 30, 0, TimeSpan.Zero);

        collector.OnTrade("AAPL", price: 0m, size: 50, timestamp: ts);
        collector.OnTrade("AAPL", price: -5m, size: 50, timestamp: ts);

        collector.TryGet("AAPL").Should().BeNull();
    }

    [Fact]
    public void OnTrade_NewUtcDate_ResetsSessionState()
    {
        var collector = new SessionStatsCollector();
        var day1 = new DateTimeOffset(2026, 5, 8, 20, 0, 0, TimeSpan.Zero);
        var day2 = new DateTimeOffset(2026, 5, 9, 13, 30, 0, TimeSpan.Zero);

        collector.OnTrade("AAPL", price: 187.5m, size: 100, timestamp: day1);
        collector.OnTrade("AAPL", price: 200m, size: 50, timestamp: day1.AddMinutes(5));

        collector.OnTrade("AAPL", price: 195m, size: 25, timestamp: day2);

        var stats = collector.TryGet("AAPL")!;
        stats.SessionDate.Should().Be(new DateOnly(2026, 5, 9));
        stats.Open.Should().Be(195m);
        stats.High.Should().Be(195m);
        stats.Low.Should().Be(195m);
        stats.Last.Should().Be(195m);
        stats.Volume.Should().Be(25);
        stats.TradeCount.Should().Be(1);
    }

    [Fact]
    public void Snapshot_ReturnsAllSymbols()
    {
        var collector = new SessionStatsCollector();
        var ts = new DateTimeOffset(2026, 5, 8, 13, 30, 0, TimeSpan.Zero);

        collector.OnTrade("AAPL", 100m, 10, ts);
        collector.OnTrade("MSFT", 400m, 5, ts);

        var snapshot = collector.Snapshot();
        snapshot.Should().HaveCount(2);
        snapshot.Should().ContainKey("AAPL");
        snapshot.Should().ContainKey("MSFT");
    }

    [Fact]
    public void TryRemove_RemovesStateForSymbol()
    {
        var collector = new SessionStatsCollector();
        var ts = new DateTimeOffset(2026, 5, 8, 13, 30, 0, TimeSpan.Zero);

        collector.OnTrade("AAPL", 100m, 10, ts);

        collector.TryRemove("AAPL").Should().BeTrue();
        collector.TryGet("AAPL").Should().BeNull();
        collector.TryRemove("AAPL").Should().BeFalse();
    }

    [Fact]
    public void OnTrade_ThroughTradeDataCollector_PopulatesSessionStats()
    {
        var publisher = new TestMarketEventPublisher();
        var sessionStats = new SessionStatsCollector();
        var collector = new TradeDataCollector(publisher, quotes: null, sessionStats: sessionStats);

        var ts = new DateTimeOffset(2026, 5, 8, 13, 30, 0, TimeSpan.Zero);
        var update = new MarketTradeUpdate(
            Timestamp: ts,
            Symbol: "SPY",
            Price: 450.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            StreamId: "TEST",
            Venue: "NYSE");

        collector.OnTrade(update);

        var stats = sessionStats.TryGet("SPY");
        stats.Should().NotBeNull();
        stats!.Open.Should().Be(450.50m);
        stats.Last.Should().Be(450.50m);
        stats.Volume.Should().Be(100);
    }

    [Fact]
    public void OnTrade_RejectedBySequenceCheck_DoesNotUpdateSessionStats()
    {
        var publisher = new TestMarketEventPublisher();
        var sessionStats = new SessionStatsCollector();
        var collector = new TradeDataCollector(publisher, quotes: null, sessionStats: sessionStats);

        var ts = new DateTimeOffset(2026, 5, 8, 13, 30, 0, TimeSpan.Zero);
        collector.OnTrade(new MarketTradeUpdate(ts, "SPY", 450m, 100, AggressorSide.Buy, 5, "T", "NYSE"));

        // Out-of-order trade (lower sequence) — should be rejected without updating session stats.
        collector.OnTrade(new MarketTradeUpdate(ts.AddMilliseconds(1), "SPY", 999m, 50, AggressorSide.Buy, 3, "T", "NYSE"));

        var stats = sessionStats.TryGet("SPY")!;
        stats.High.Should().Be(450m);
        stats.Last.Should().Be(450m);
        stats.Volume.Should().Be(100);
        stats.TradeCount.Should().Be(1);
    }
}
