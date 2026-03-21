using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests.Models;

/// <summary>
/// Unit tests for AdjustedHistoricalBar and its ToHistoricalBar conversion method.
/// </summary>
public class AdjustedHistoricalBarTests
{
    [Fact]
    public void ToHistoricalBar_WithNoAdjustedValues_ReturnsUnadjustedBar()
    {
        // Arrange
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 450.00m,
            High: 455.00m,
            Low: 448.00m,
            Close: 452.50m,
            Volume: 50000000,
            Source: "yahoo");

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert
        bar.Symbol.Should().Be("SPY");
        bar.Open.Should().Be(450.00m);
        bar.High.Should().Be(455.00m);
        bar.Low.Should().Be(448.00m);
        bar.Close.Should().Be(452.50m);
        bar.Volume.Should().Be(50000000);
    }

    [Fact]
    public void ToHistoricalBar_WithValidAdjustedValues_ReturnsAdjustedBar()
    {
        // Arrange - 2:1 split adjustment (all prices halved)
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 450.00m,
            High: 455.00m,
            Low: 448.00m,
            Close: 452.50m,
            Volume: 50000000,
            Source: "yahoo",
            AdjustedOpen: 225.00m,
            AdjustedHigh: 227.50m,
            AdjustedLow: 224.00m,
            AdjustedClose: 226.25m,
            SplitFactor: 0.5m);

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert
        bar.Open.Should().Be(225.00m);
        bar.High.Should().Be(227.50m);
        bar.Low.Should().Be(224.00m);
        bar.Close.Should().Be(226.25m);
    }

    [Fact]
    public void ToHistoricalBar_WhenAdjustedOpenExceedsAdjustedHigh_ClampsToValidRange()
    {
        // Arrange - simulating rounding error where adjusted open > adjusted high
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "PCG-PA",
            SessionDate: new DateOnly(2024, 1, 15),
            Open: 25.00m,
            High: 26.00m,
            Low: 24.00m,
            Close: 25.50m,
            Volume: 10000,
            Source: "yahoo",
            AdjustedOpen: 12.50m,
            AdjustedHigh: 12.40m,  // Rounding error: high < open
            AdjustedLow: 12.00m,
            AdjustedClose: 12.75m);

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert - high should be clamped to >= max(open, close)
        bar.Open.Should().Be(12.50m);
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Open);
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Close);
        bar.Low.Should().Be(12.00m);
        bar.Close.Should().Be(12.75m);

        // Verify valid OHLC relationships
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
        bar.Open.Should().BeInRange(bar.Low, bar.High);
        bar.Close.Should().BeInRange(bar.Low, bar.High);
    }

    [Fact]
    public void ToHistoricalBar_WhenAdjustedCloseExceedsAdjustedHigh_ClampsToValidRange()
    {
        // Arrange - simulating rounding error where adjusted close > adjusted high
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "PCG-PB",
            SessionDate: new DateOnly(2024, 1, 15),
            Open: 25.00m,
            High: 26.00m,
            Low: 24.00m,
            Close: 25.50m,
            Volume: 10000,
            Source: "yahoo",
            AdjustedOpen: 12.50m,
            AdjustedHigh: 12.60m,  // Rounding error: high < close
            AdjustedLow: 12.00m,
            AdjustedClose: 12.75m);

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert - high should be clamped to >= max(open, close)
        bar.Open.Should().Be(12.50m);
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Open);
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Close);
        bar.Low.Should().Be(12.00m);
        bar.Close.Should().Be(12.75m);

        // Verify valid OHLC relationships
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
        bar.Open.Should().BeInRange(bar.Low, bar.High);
        bar.Close.Should().BeInRange(bar.Low, bar.High);
    }

    [Fact]
    public void ToHistoricalBar_WhenAdjustedOpenBelowAdjustedLow_ClampsToValidRange()
    {
        // Arrange - simulating rounding error where adjusted open < adjusted low
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "PCG-PC",
            SessionDate: new DateOnly(2024, 1, 15),
            Open: 25.00m,
            High: 26.00m,
            Low: 24.00m,
            Close: 25.50m,
            Volume: 10000,
            Source: "yahoo",
            AdjustedOpen: 11.90m,  // Rounding error: open < low
            AdjustedHigh: 13.00m,
            AdjustedLow: 12.00m,
            AdjustedClose: 12.75m);

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert - low should be clamped to <= min(open, close)
        bar.Open.Should().BeLessThanOrEqualTo(bar.High);
        bar.Open.Should().BeGreaterThanOrEqualTo(bar.Low);
        bar.High.Should().Be(13.00m);
        bar.Close.Should().Be(12.75m);

        // Verify valid OHLC relationships
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
        bar.Open.Should().BeInRange(bar.Low, bar.High);
        bar.Close.Should().BeInRange(bar.Low, bar.High);
    }

    [Fact]
    public void ToHistoricalBar_WhenAdjustedCloseBelowAdjustedLow_ClampsToValidRange()
    {
        // Arrange - simulating rounding error where adjusted close < adjusted low
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "PCG-PD",
            SessionDate: new DateOnly(2024, 1, 15),
            Open: 25.00m,
            High: 26.00m,
            Low: 24.00m,
            Close: 25.50m,
            Volume: 10000,
            Source: "yahoo",
            AdjustedOpen: 12.50m,
            AdjustedHigh: 13.00m,
            AdjustedLow: 12.10m,  // Rounding error: low > close
            AdjustedClose: 12.00m);

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert - low should be clamped to <= min(open, close)
        bar.Open.Should().Be(12.50m);
        bar.High.Should().Be(13.00m);
        bar.Low.Should().BeLessThanOrEqualTo(bar.Open);
        bar.Low.Should().BeLessThanOrEqualTo(bar.Close);
        bar.Close.Should().Be(12.00m);

        // Verify valid OHLC relationships
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
        bar.Open.Should().BeInRange(bar.Low, bar.High);
        bar.Close.Should().BeInRange(bar.Low, bar.High);
    }

    [Fact]
    public void ToHistoricalBar_WithPreferAdjustedFalse_ReturnsUnadjustedBar()
    {
        // Arrange
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 450.00m,
            High: 455.00m,
            Low: 448.00m,
            Close: 452.50m,
            Volume: 50000000,
            Source: "yahoo",
            AdjustedOpen: 225.00m,
            AdjustedHigh: 227.50m,
            AdjustedLow: 224.00m,
            AdjustedClose: 226.25m);

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: false);

        // Assert - should use original unadjusted values
        bar.Open.Should().Be(450.00m);
        bar.High.Should().Be(455.00m);
        bar.Low.Should().Be(448.00m);
        bar.Close.Should().Be(452.50m);
    }

    [Fact]
    public void ToHistoricalBar_WithExtremeRoundingErrors_StillProducesValidBar()
    {
        // Arrange - multiple rounding errors combined
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "PCG-PE",
            SessionDate: new DateOnly(2024, 1, 15),
            Open: 25.00m,
            High: 26.00m,
            Low: 24.00m,
            Close: 25.50m,
            Volume: 10000,
            Source: "yahoo",
            AdjustedOpen: 11.90m,   // Below adjusted low
            AdjustedHigh: 12.40m,   // Below adjusted close
            AdjustedLow: 12.50m,    // Above adjusted open and high
            AdjustedClose: 12.75m); // Above adjusted high

        // Act - this should not throw despite invalid adjusted relationships
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert - all OHLC relationships should be valid
        bar.High.Should().BeGreaterThanOrEqualTo(bar.Low);
        bar.Open.Should().BeInRange(bar.Low, bar.High);
        bar.Close.Should().BeInRange(bar.Low, bar.High);
        bar.Open.Should().BeLessThanOrEqualTo(bar.High);
        bar.Close.Should().BeLessThanOrEqualTo(bar.High);
        bar.Open.Should().BeGreaterThanOrEqualTo(bar.Low);
        bar.Close.Should().BeGreaterThanOrEqualTo(bar.Low);
    }

    [Fact]
    public void ToHistoricalBar_PreservesSymbolAndOtherMetadata()
    {
        // Arrange
        var adjustedBar = new AdjustedHistoricalBar(
            Symbol: "AAPL",
            SessionDate: new DateOnly(2024, 6, 15),
            Open: 180.00m,
            High: 182.00m,
            Low: 179.00m,
            Close: 181.00m,
            Volume: 25000000,
            Source: "alpaca",
            SequenceNumber: 12345,
            AdjustedOpen: 90.00m,
            AdjustedHigh: 91.00m,
            AdjustedLow: 89.50m,
            AdjustedClose: 90.50m,
            AdjustedVolume: 50000000,
            SplitFactor: 0.5m);

        // Act
        var bar = adjustedBar.ToHistoricalBar(preferAdjusted: true);

        // Assert - metadata should be preserved
        bar.Symbol.Should().Be("AAPL");
        bar.SessionDate.Should().Be(new DateOnly(2024, 6, 15));
        bar.Source.Should().Be("alpaca");
        bar.SequenceNumber.Should().Be(12345);
        bar.Volume.Should().Be(50000000); // Adjusted volume
    }
}
