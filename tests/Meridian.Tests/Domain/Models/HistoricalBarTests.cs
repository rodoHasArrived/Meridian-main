using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Xunit;

namespace Meridian.Tests.Models;

/// <summary>
/// Unit tests for the HistoricalBar model and its utility methods.
/// </summary>
public class HistoricalBarTests
{
    #region Constructor Validation Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange & Act
        var bar = new HistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 450.00m,
            High: 455.00m,
            Low: 448.00m,
            Close: 452.50m,
            Volume: 50000000,
            Source: "alpaca",
            SequenceNumber: 12345);

        // Assert
        bar.Symbol.Should().Be("SPY");
        bar.SessionDate.Should().Be(new DateOnly(2026, 1, 15));
        bar.Open.Should().Be(450.00m);
        bar.High.Should().Be(455.00m);
        bar.Low.Should().Be(448.00m);
        bar.Close.Should().Be(452.50m);
        bar.Volume.Should().Be(50000000);
        bar.Source.Should().Be("alpaca");
        bar.SequenceNumber.Should().Be(12345);
    }

    [Fact]
    public void Constructor_WithMinimalParameters_UsesDefaultValues()
    {
        // Arrange & Act
        var bar = new HistoricalBar(
            Symbol: "AAPL",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 180.00m,
            High: 182.00m,
            Low: 179.00m,
            Close: 181.00m,
            Volume: 10000000);

        // Assert - verify defaults
        bar.Source.Should().Be("stooq");
        bar.SequenceNumber.Should().Be(0);
    }

    [Fact]
    public void Constructor_WhenOpenExceedsHigh_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => new HistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 455.00m,  // Open exceeds high
            High: 450.00m,
            Low: 445.00m,
            Close: 448.00m,
            Volume: 1000000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Open/Close cannot exceed high*");
    }

    [Fact]
    public void Constructor_WhenCloseExceedsHigh_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => new HistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 448.00m,
            High: 450.00m,
            Low: 445.00m,
            Close: 455.00m,  // Close exceeds high
            Volume: 1000000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Open/Close cannot exceed high*");
    }

    [Fact]
    public void Constructor_WhenOpenBelowLow_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => new HistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 440.00m,  // Open below low
            High: 450.00m,
            Low: 445.00m,
            Close: 448.00m,
            Volume: 1000000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Open/Close cannot be below low*");
    }

    [Fact]
    public void Constructor_WhenCloseBelowLow_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => new HistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 448.00m,
            High: 450.00m,
            Low: 445.00m,
            Close: 440.00m,  // Close below low
            Volume: 1000000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Open/Close cannot be below low*");
    }

    [Fact]
    public void Constructor_WhenLowExceedsHigh_ThrowsArgumentOutOfRangeException()
    {
        // Arrange & Act
        var act = () => new HistoricalBar(
            Symbol: "SPY",
            SessionDate: new DateOnly(2026, 1, 15),
            Open: 448.00m,
            High: 450.00m,
            Low: 455.00m,  // Low exceeds high
            Close: 448.00m,
            Volume: 1000000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Low cannot exceed high*");
    }

    #endregion

    #region Range Property Tests

    [Fact]
    public void Range_ReturnsHighMinusLow()
    {
        // Arrange
        var bar = CreateBar(high: 455.00m, low: 448.00m);

        // Act
        var range = bar.Range;

        // Assert
        range.Should().Be(7.00m);
    }

    [Fact]
    public void Range_WhenHighEqualsLow_ReturnsZero()
    {
        // Arrange - flat bar with no range (e.g., very illiquid stock)
        var bar = CreateBar(open: 100m, high: 100m, low: 100m, close: 100m);

        // Act
        var range = bar.Range;

        // Assert
        range.Should().Be(0m);
    }

    #endregion

    #region BodySize Property Tests

    [Fact]
    public void BodySize_WhenCloseGreaterThanOpen_ReturnsPositiveDifference()
    {
        // Arrange - bullish bar
        var bar = CreateBar(open: 450.00m, close: 455.00m);

        // Act
        var bodySize = bar.BodySize;

        // Assert
        bodySize.Should().Be(5.00m);
    }

    [Fact]
    public void BodySize_WhenCloseLessThanOpen_ReturnsAbsoluteDifference()
    {
        // Arrange - bearish bar
        var bar = CreateBar(open: 455.00m, high: 456.00m, low: 449.00m, close: 450.00m);

        // Act
        var bodySize = bar.BodySize;

        // Assert
        bodySize.Should().Be(5.00m);
    }

    [Fact]
    public void BodySize_WhenCloseEqualsOpen_ReturnsZero()
    {
        // Arrange - doji pattern
        var bar = CreateBar(open: 450.00m, close: 450.00m);

        // Act
        var bodySize = bar.BodySize;

        // Assert
        bodySize.Should().Be(0m);
    }

    #endregion

    #region IsBullish Property Tests

    [Fact]
    public void IsBullish_WhenCloseGreaterThanOpen_ReturnsTrue()
    {
        // Arrange
        var bar = CreateBar(open: 450.00m, close: 455.00m);

        // Act & Assert
        bar.IsBullish.Should().BeTrue();
    }

    [Fact]
    public void IsBullish_WhenCloseLessThanOpen_ReturnsFalse()
    {
        // Arrange
        var bar = CreateBar(open: 455.00m, high: 456.00m, low: 449.00m, close: 450.00m);

        // Act & Assert
        bar.IsBullish.Should().BeFalse();
    }

    [Fact]
    public void IsBullish_WhenCloseEqualsOpen_ReturnsFalse()
    {
        // Arrange - doji pattern is not considered bullish
        var bar = CreateBar(open: 450.00m, close: 450.00m);

        // Act & Assert
        bar.IsBullish.Should().BeFalse();
    }

    #endregion

    #region IsBearish Property Tests

    [Fact]
    public void IsBearish_WhenCloseLessThanOpen_ReturnsTrue()
    {
        // Arrange
        var bar = CreateBar(open: 455.00m, high: 456.00m, low: 449.00m, close: 450.00m);

        // Act & Assert
        bar.IsBearish.Should().BeTrue();
    }

    [Fact]
    public void IsBearish_WhenCloseGreaterThanOpen_ReturnsFalse()
    {
        // Arrange
        var bar = CreateBar(open: 450.00m, close: 455.00m);

        // Act & Assert
        bar.IsBearish.Should().BeFalse();
    }

    [Fact]
    public void IsBearish_WhenCloseEqualsOpen_ReturnsFalse()
    {
        // Arrange - doji pattern is not considered bearish
        var bar = CreateBar(open: 450.00m, close: 450.00m);

        // Act & Assert
        bar.IsBearish.Should().BeFalse();
    }

    [Fact]
    public void IsBullishAndIsBearish_AreMutuallyExclusive()
    {
        // Arrange
        var bullishBar = CreateBar(open: 450.00m, close: 455.00m);
        var bearishBar = CreateBar(open: 455.00m, high: 456.00m, low: 449.00m, close: 450.00m);
        var dojiBar = CreateBar(open: 450.00m, close: 450.00m);

        // Assert
        bullishBar.IsBullish.Should().BeTrue();
        bullishBar.IsBearish.Should().BeFalse();

        bearishBar.IsBullish.Should().BeFalse();
        bearishBar.IsBearish.Should().BeTrue();

        // Doji is neither bullish nor bearish
        dojiBar.IsBullish.Should().BeFalse();
        dojiBar.IsBearish.Should().BeFalse();
    }

    #endregion

    #region ChangePercent Property Tests

    [Fact]
    public void ChangePercent_WhenPriceIncreased_ReturnsPositivePercentage()
    {
        // Arrange - 2% increase (from 100 to 102)
        var bar = CreateBar(open: 100.00m, high: 103.00m, low: 99.00m, close: 102.00m);

        // Act
        var changePercent = bar.ChangePercent;

        // Assert
        changePercent.Should().Be(2.00m);
    }

    [Fact]
    public void ChangePercent_WhenPriceDecreased_ReturnsNegativePercentage()
    {
        // Arrange - 5% decrease (from 100 to 95)
        var bar = CreateBar(open: 100.00m, high: 101.00m, low: 94.00m, close: 95.00m);

        // Act
        var changePercent = bar.ChangePercent;

        // Assert
        changePercent.Should().Be(-5.00m);
    }

    [Fact]
    public void ChangePercent_WhenPriceUnchanged_ReturnsZero()
    {
        // Arrange
        var bar = CreateBar(open: 100.00m, close: 100.00m);

        // Act
        var changePercent = bar.ChangePercent;

        // Assert
        changePercent.Should().Be(0m);
    }

    [Theory]
    [InlineData(200.00, 210.00, 5.00)]   // 5% up
    [InlineData(200.00, 190.00, -5.00)]  // 5% down
    [InlineData(50.00, 55.00, 10.00)]    // 10% up
    [InlineData(100.00, 75.00, -25.00)]  // 25% down
    public void ChangePercent_CalculatesCorrectlyForVariousScenarios(
        decimal open, decimal close, decimal expectedChange)
    {
        // Arrange
        var high = Math.Max(open, close) + 1m;
        var low = Math.Min(open, close) - 1m;
        var bar = CreateBar(open: open, high: high, low: low, close: close);

        // Act
        var changePercent = bar.ChangePercent;

        // Assert
        changePercent.Should().Be(expectedChange);
    }

    #endregion

    #region TypicalPrice Property Tests

    [Fact]
    public void TypicalPrice_CalculatesAverageOfHighLowClose()
    {
        // Arrange - (455 + 448 + 452) / 3 = 451.666...
        var bar = CreateBar(high: 455.00m, low: 448.00m, close: 452.00m);

        // Act
        var typicalPrice = bar.TypicalPrice;

        // Assert
        typicalPrice.Should().BeApproximately(451.6666666666666666666666667m, 0.0000001m);
    }

    [Fact]
    public void TypicalPrice_WhenAllPricesSame_ReturnsThatPrice()
    {
        // Arrange
        var bar = CreateBar(open: 100m, high: 100m, low: 100m, close: 100m);

        // Act
        var typicalPrice = bar.TypicalPrice;

        // Assert
        typicalPrice.Should().Be(100m);
    }

    #endregion

    #region Notional Property Tests

    [Fact]
    public void Notional_ReturnsCloseMultipliedByVolume()
    {
        // Arrange
        var bar = CreateBar(close: 450.00m, volume: 1000000);

        // Act
        var notional = bar.Notional;

        // Assert
        notional.Should().Be(450000000m); // 450 * 1,000,000
    }

    [Fact]
    public void Notional_WhenVolumeIsZero_ReturnsZero()
    {
        // Arrange
        var bar = CreateBar(close: 450.00m, volume: 0);

        // Act
        var notional = bar.Notional;

        // Assert
        notional.Should().Be(0m);
    }

    [Fact]
    public void Notional_WithLargeValues_CalculatesCorrectly()
    {
        // Arrange - simulating a high-priced stock with significant volume
        var bar = CreateBar(close: 3500.00m, volume: 5000000); // e.g., AMZN or similar

        // Act
        var notional = bar.Notional;

        // Assert
        notional.Should().Be(17500000000m); // 3500 * 5,000,000 = 17.5 billion
    }

    #endregion

    #region ToTimestampUtc Tests

    [Fact]
    public void ToTimestampUtc_ReturnsCorrectUtcTimestamp()
    {
        // Arrange
        var sessionDate = new DateOnly(2026, 1, 15);
        var bar = new HistoricalBar(
            Symbol: "SPY",
            SessionDate: sessionDate,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Act
        var timestamp = bar.ToTimestampUtc();

        // Assert
        timestamp.Year.Should().Be(2026);
        timestamp.Month.Should().Be(1);
        timestamp.Day.Should().Be(15);
        timestamp.Hour.Should().Be(0);
        timestamp.Minute.Should().Be(0);
        timestamp.Second.Should().Be(0);
        timestamp.Offset.Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a test HistoricalBar with sensible defaults.
    /// Only specify the values you care about for the test.
    /// </summary>
    private static HistoricalBar CreateBar(
        string symbol = "SPY",
        DateOnly? sessionDate = null,
        decimal open = 450.00m,
        decimal? high = null,
        decimal? low = null,
        decimal close = 452.50m,
        long volume = 50000000,
        string source = "test")
    {
        // Ensure OHLC constraints are valid
        var actualHigh = high ?? Math.Max(open, close) + 2m;
        var actualLow = low ?? Math.Min(open, close) - 2m;

        return new HistoricalBar(
            Symbol: symbol,
            SessionDate: sessionDate ?? new DateOnly(2026, 1, 15),
            Open: open,
            High: actualHigh,
            Low: actualLow,
            Close: close,
            Volume: volume,
            Source: source);
    }

    #endregion
}
