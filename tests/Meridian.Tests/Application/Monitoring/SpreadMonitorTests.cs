using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

namespace Meridian.Tests.Monitoring;

public sealed class SpreadMonitorTests : IDisposable
{
    private readonly SpreadMonitor _monitor;

    public SpreadMonitorTests()
    {
        _monitor = new SpreadMonitor(new SpreadMonitorConfig
        {
            WideSpreadThresholdBps = 100.0, // 1%
            WideSpreadThresholdPercent = 1.0,
            AlertCooldownMs = 0 // Disable cooldown for tests
        });
    }

    [Fact]
    public void ProcessQuote_WithNormalSpread_ShouldNotAlert()
    {
        // Arrange - tight spread
        WideSpreadAlert? capturedAlert = null;
        _monitor.OnWideSpread += alert => capturedAlert = alert;

        // Act - 0.1% spread (10 bps)
        var detected = _monitor.ProcessQuote("AAPL", 149.95m, 150.05m, "Provider1");

        // Assert
        detected.Should().BeFalse();
        capturedAlert.Should().BeNull();
        _monitor.TotalWideSpreadEvents.Should().Be(0);
    }

    [Fact]
    public void ProcessQuote_WithWideSpread_ShouldAlert()
    {
        // Arrange
        WideSpreadAlert? capturedAlert = null;
        _monitor.OnWideSpread += alert => capturedAlert = alert;

        // Act - 2% spread (200 bps)
        var detected = _monitor.ProcessQuote("AAPL", 147.00m, 150.00m, "Provider1");

        // Assert
        detected.Should().BeTrue();
        _monitor.TotalWideSpreadEvents.Should().Be(1);
        capturedAlert.Should().NotBeNull();
        capturedAlert!.Value.Symbol.Should().Be("AAPL");
        capturedAlert.Value.SpreadBps.Should().BeGreaterThan(100);
    }

    [Fact]
    public void ProcessQuote_WithInvalidPrices_ShouldNotProcess()
    {
        // Arrange
        WideSpreadAlert? capturedAlert = null;
        _monitor.OnWideSpread += alert => capturedAlert = alert;

        // Act - zero prices
        var detected1 = _monitor.ProcessQuote("AAPL", 0m, 150.00m, "Provider1");
        var detected2 = _monitor.ProcessQuote("AAPL", 149.00m, 0m, "Provider1");

        // Assert
        detected1.Should().BeFalse();
        detected2.Should().BeFalse();
        capturedAlert.Should().BeNull();
    }

    [Fact]
    public void ProcessQuote_ShouldTrackSpreadStatistics()
    {
        // Arrange & Act
        // Quote 1: spread=0.20, mid=150.00, spreadBps=(0.20/150.00)*10000=13.33 bps
        _monitor.ProcessQuote("AAPL", 149.90m, 150.10m, "Provider1");
        // Quote 2: spread=0.30, mid=150.00, spreadBps=(0.30/150.00)*10000=20.00 bps
        _monitor.ProcessQuote("AAPL", 149.85m, 150.15m, "Provider1");

        // Assert
        var snapshot = _monitor.GetSpreadSnapshot("AAPL");
        snapshot.Should().NotBeNull();
        snapshot!.Value.TotalQuotes.Should().Be(2);
        // Average: (13.33 + 20.00) / 2 = 16.67 bps
        snapshot.Value.AverageSpreadBps.Should().BeApproximately(16.67, 1.0);
    }

    [Fact]
    public void GetAllSpreadSnapshots_ShouldReturnAllSymbols()
    {
        // Arrange
        _monitor.ProcessQuote("AAPL", 149.90m, 150.10m, "Provider1");
        _monitor.ProcessQuote("MSFT", 299.90m, 300.10m, "Provider1");
        _monitor.ProcessQuote("GOOGL", 129.90m, 130.10m, "Provider1");

        // Act
        var snapshots = _monitor.GetAllSpreadSnapshots();

        // Assert
        snapshots.Should().HaveCount(3);
        snapshots.Select(s => s.Symbol).Should().Contain("AAPL");
        snapshots.Select(s => s.Symbol).Should().Contain("MSFT");
        snapshots.Select(s => s.Symbol).Should().Contain("GOOGL");
    }

    [Fact]
    public void GetStats_ShouldReturnCorrectStatistics()
    {
        // Arrange
        _monitor.ProcessQuote("AAPL", 149.90m, 150.10m, "Provider1"); // Narrow
        _monitor.ProcessQuote("AAPL", 145.00m, 155.00m, "Provider1"); // Wide

        // Act
        var stats = _monitor.GetStats();

        // Assert
        stats.TotalQuotesProcessed.Should().Be(2);
        stats.TotalWideSpreadEvents.Should().Be(1);
    }

    [Fact]
    public void LargeCapConfig_ShouldBeSensitiveToSmallSpreads()
    {
        // Arrange
        using var largeCapMonitor = new SpreadMonitor(SpreadMonitorConfig.LargeCap);
        WideSpreadAlert? capturedAlert = null;
        largeCapMonitor.OnWideSpread += alert => capturedAlert = alert;

        // Act - 0.2% spread (20 bps) exceeds 10 bps threshold
        var detected = largeCapMonitor.ProcessQuote("AAPL", 149.70m, 150.30m, "Provider1");

        // Assert
        detected.Should().BeTrue();
        capturedAlert.Should().NotBeNull();
    }

    [Fact]
    public void SmallCapConfig_ShouldAllowWiderSpreads()
    {
        // Arrange
        using var smallCapMonitor = new SpreadMonitor(SpreadMonitorConfig.SmallCap);
        WideSpreadAlert? capturedAlert = null;
        smallCapMonitor.OnWideSpread += alert => capturedAlert = alert;

        // Act - 3% spread (300 bps) is below 500 bps threshold
        // Bid: 0.985, Ask: 1.015, Spread: 0.03, Mid: 1.00, SpreadBps: 300
        var detected = smallCapMonitor.ProcessQuote("PENNY", 0.985m, 1.015m, "Provider1");

        // Assert
        detected.Should().BeFalse();
        capturedAlert.Should().BeNull();
    }

    [Fact]
    public void ConsecutiveWideSpread_ShouldIncrementCount()
    {
        // Arrange
        var alerts = new List<WideSpreadAlert>();
        _monitor.OnWideSpread += alert => alerts.Add(alert);

        // Act - consecutive wide spreads
        _monitor.ProcessQuote("AAPL", 140.00m, 160.00m, "Provider1");
        _monitor.ProcessQuote("AAPL", 135.00m, 165.00m, "Provider1");

        // Assert
        alerts.Should().HaveCount(2);
        alerts[0].ConsecutiveWideCount.Should().Be(1);
        alerts[1].ConsecutiveWideCount.Should().Be(2);
    }

    [Fact]
    public void Dispose_ShouldStopProcessing()
    {
        // Arrange
        _monitor.Dispose();

        // Act
        var detected = _monitor.ProcessQuote("AAPL", 100.00m, 200.00m, "Provider1");

        // Assert
        detected.Should().BeFalse();
    }

    public void Dispose()
    {
        _monitor.Dispose();
    }
}
