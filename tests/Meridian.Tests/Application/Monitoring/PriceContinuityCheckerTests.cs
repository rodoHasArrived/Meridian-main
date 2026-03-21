using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Monitoring;

public sealed class PriceContinuityCheckerTests : IDisposable
{
    private readonly PriceContinuityChecker _checker;

    public PriceContinuityCheckerTests()
    {
        _checker = new PriceContinuityChecker(new PriceContinuityConfig
        {
            GapThresholdPercent = 5m,
            LargeGapThresholdPercent = 15m
        });
    }

    [Fact]
    public void CheckPrice_FirstPrice_ShouldReturnOk()
    {
        // Act
        var result = _checker.CheckPrice("AAPL", 150.00m, DateTimeOffset.UtcNow);

        // Assert
        result.Should().Be(PriceContinuityResult.Ok);
    }

    [Fact]
    public void CheckPrice_SmallChange_ShouldReturnOk()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);

        // Act - 3% change is within threshold
        var result = _checker.CheckPrice("AAPL", 103.00m, DateTimeOffset.UtcNow);

        // Assert
        result.Should().Be(PriceContinuityResult.Ok);
    }

    [Fact]
    public void CheckPrice_GapUp_ShouldDetectGap()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);

        PriceDiscontinuityEvent? capturedEvent = null;
        _checker.OnDiscontinuity += e => capturedEvent = e;

        // Act - 8% gap up
        var result = _checker.CheckPrice("AAPL", 108.00m, DateTimeOffset.UtcNow);

        // Assert
        result.Should().Be(PriceContinuityResult.Gap);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Value.Type.Should().Be(DiscontinuityType.GapUp);
        capturedEvent.Value.Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void CheckPrice_GapDown_ShouldDetectGap()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);

        PriceDiscontinuityEvent? capturedEvent = null;
        _checker.OnDiscontinuity += e => capturedEvent = e;

        // Act - 8% gap down
        var result = _checker.CheckPrice("AAPL", 92.00m, DateTimeOffset.UtcNow);

        // Assert
        result.Should().Be(PriceContinuityResult.Gap);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Value.Type.Should().Be(DiscontinuityType.GapDown);
    }

    [Fact]
    public void CheckPrice_LargeGapUp_ShouldDetectLargeGap()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);

        PriceDiscontinuityEvent? capturedEvent = null;
        _checker.OnDiscontinuity += e => capturedEvent = e;

        // Act - 20% gap up
        var result = _checker.CheckPrice("AAPL", 120.00m, DateTimeOffset.UtcNow);

        // Assert
        result.Should().Be(PriceContinuityResult.LargeGap);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Value.Type.Should().Be(DiscontinuityType.LargeGapUp);
    }

    [Fact]
    public void CheckBar_InvalidBar_ShouldDetectInvalid()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);

        PriceDiscontinuityEvent? capturedEvent = null;
        _checker.OnDiscontinuity += e => capturedEvent = e;

        // Act - high < low is invalid
        var result = _checker.CheckBar("AAPL", 100.00m, 95.00m, 105.00m, 98.00m, DateTimeOffset.UtcNow);

        // Assert
        result.Should().Be(PriceContinuityResult.InvalidBar);
        capturedEvent.Should().NotBeNull();
        capturedEvent!.Value.Type.Should().Be(DiscontinuityType.InvalidBar);
    }

    [Fact]
    public void CheckBar_ValidBar_ShouldReturnOk()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);

        // Act - valid bar OHLC
        var result = _checker.CheckBar("AAPL", 101.00m, 105.00m, 99.00m, 103.00m, DateTimeOffset.UtcNow);

        // Assert
        result.Should().Be(PriceContinuityResult.Ok);
    }

    [Fact]
    public void CheckQuote_ShouldUseMiddlePrice()
    {
        // Arrange
        _checker.CheckQuote("AAPL", 99.00m, 101.00m, DateTimeOffset.UtcNow); // Mid = 100

        PriceDiscontinuityEvent? capturedEvent = null;
        _checker.OnDiscontinuity += e => capturedEvent = e;

        // Act - 10% jump in mid
        var result = _checker.CheckQuote("AAPL", 109.00m, 111.00m, DateTimeOffset.UtcNow); // Mid = 110

        // Assert
        result.Should().Be(PriceContinuityResult.Gap);
        capturedEvent.Should().NotBeNull();
    }

    [Fact]
    public void GetStatistics_ShouldReturnCorrectCounts()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);
        _checker.CheckPrice("AAPL", 110.00m, DateTimeOffset.UtcNow); // 10% gap - exceeds 5% threshold
        _checker.CheckPrice("MSFT", 200.00m, DateTimeOffset.UtcNow);
        _checker.CheckPrice("MSFT", 212.00m, DateTimeOffset.UtcNow); // 6% gap - exceeds 5% threshold

        // Act
        var stats = _checker.GetStatistics();

        // Assert
        stats.TotalChecks.Should().Be(4);
        stats.TotalDiscontinuities.Should().Be(2);
        stats.SymbolsTracked.Should().Be(2);
    }

    [Fact]
    public void GetTopDiscontinuitySymbols_ShouldOrderByCount()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);
        _checker.CheckPrice("AAPL", 110.00m, DateTimeOffset.UtcNow); // Gap
        _checker.CheckPrice("AAPL", 130.00m, DateTimeOffset.UtcNow); // Large gap

        _checker.CheckPrice("MSFT", 200.00m, DateTimeOffset.UtcNow);
        _checker.CheckPrice("MSFT", 214.00m, DateTimeOffset.UtcNow); // Gap

        // Act
        var top = _checker.GetTopDiscontinuitySymbols(10);

        // Assert
        top.Should().HaveCount(2);
        top[0].Symbol.Should().Be("AAPL");
        top[0].Count.Should().Be(2);
    }

    [Fact]
    public void MultipleSymbols_ShouldTrackIndependently()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);
        _checker.CheckPrice("MSFT", 200.00m, DateTimeOffset.UtcNow);

        // Act
        var resultAapl = _checker.CheckPrice("AAPL", 102.00m, DateTimeOffset.UtcNow); // 2% - ok
        var resultMsft = _checker.CheckPrice("MSFT", 220.00m, DateTimeOffset.UtcNow); // 10% - gap

        // Assert
        resultAapl.Should().Be(PriceContinuityResult.Ok);
        resultMsft.Should().Be(PriceContinuityResult.Gap);
    }

    [Fact]
    public void Reset_ShouldClearSymbolTracking()
    {
        // Arrange
        _checker.CheckPrice("AAPL", 100.00m, DateTimeOffset.UtcNow);

        // Act
        _checker.Reset("AAPL");

        // Assert - first price after reset, no baseline = OK
        var result = _checker.CheckPrice("AAPL", 200.00m, DateTimeOffset.UtcNow);
        result.Should().Be(PriceContinuityResult.Ok);
    }

    public void Dispose()
    {
        _checker.Dispose();
    }
}
