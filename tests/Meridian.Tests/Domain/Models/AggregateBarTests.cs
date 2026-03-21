using FluentAssertions;
using Meridian.Domain.Models;
using Xunit;

namespace Meridian.Tests.Models;

/// <summary>
/// Unit tests for the AggregateBar model.
/// </summary>
public class AggregateBarTests
{
    #region Constructor Validation Tests

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var bar = new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 450.00m,
            High: 451.00m,
            Low: 449.50m,
            Close: 450.75m,
            Volume: 1000000,
            Vwap: 450.50m,
            TradeCount: 5000,
            Timeframe: AggregateTimeframe.Minute,
            Source: "Polygon",
            SequenceNumber: 123456789);

        // Assert
        bar.Symbol.Should().Be("SPY");
        bar.StartTime.Should().Be(startTime);
        bar.EndTime.Should().Be(endTime);
        bar.Open.Should().Be(450.00m);
        bar.High.Should().Be(451.00m);
        bar.Low.Should().Be(449.50m);
        bar.Close.Should().Be(450.75m);
        bar.Volume.Should().Be(1000000);
        bar.Vwap.Should().Be(450.50m);
        bar.TradeCount.Should().Be(5000);
        bar.Timeframe.Should().Be(AggregateTimeframe.Minute);
        bar.Source.Should().Be("Polygon");
        bar.SequenceNumber.Should().Be(123456789);
    }

    [Fact]
    public void Constructor_WithNullSymbol_ThrowsArgumentException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: null!,
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Symbol*");
    }

    [Fact]
    public void Constructor_WithEmptySymbol_ThrowsArgumentException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Symbol*");
    }

    [Fact]
    public void Constructor_WithWhitespaceSymbol_ThrowsArgumentException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "   ",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Symbol*");
    }

    [Theory]
    [InlineData(0, 101, 99, 100.5)]      // Zero Open
    [InlineData(-1, 101, 99, 100.5)]     // Negative Open
    [InlineData(100, 0, 99, 100.5)]      // Zero High
    [InlineData(100, -101, 99, 100.5)]   // Negative High
    [InlineData(100, 101, 0, 100.5)]     // Zero Low
    [InlineData(100, 101, -99, 100.5)]   // Negative Low
    [InlineData(100, 101, 99, 0)]        // Zero Close
    [InlineData(100, 101, 99, -100.5)]   // Negative Close
    public void Constructor_WithInvalidOhlcValues_ThrowsArgumentOutOfRangeException(
        decimal open, decimal high, decimal low, decimal close)
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: open,
            High: high,
            Low: low,
            Close: close,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithLowGreaterThanHigh_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 99m,    // High is less than Low
            Low: 100m,
            Close: 99.5m,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Low*");
    }

    [Fact]
    public void Constructor_WithOpenGreaterThanHigh_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 102m,   // Open exceeds High
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*High*");
    }

    [Fact]
    public void Constructor_WithCloseGreaterThanHigh_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 102m,  // Close exceeds High
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*High*");
    }

    [Fact]
    public void Constructor_WithOpenLessThanLow_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 98m,    // Open below Low
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Low*");
    }

    [Fact]
    public void Constructor_WithCloseLessThanLow_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 98m,   // Close below Low
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Low*");
    }

    [Fact]
    public void Constructor_WithNegativeVolume_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: -1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Volume*");
    }

    [Fact]
    public void Constructor_WithEndTimeBeforeStartTime_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(-1); // End before start

        // Act
        var act = () => new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*End time*");
    }

    #endregion

    #region Default Values Tests

    [Fact]
    public void Constructor_WithMinimalParameters_UsesDefaultValues()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddSeconds(1);

        // Act
        var bar = new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000);

        // Assert - verify defaults
        bar.Vwap.Should().Be(0m);
        bar.TradeCount.Should().Be(0);
        bar.Timeframe.Should().Be(AggregateTimeframe.Minute);
        bar.Source.Should().Be("Polygon");
        bar.SequenceNumber.Should().Be(0);
    }

    #endregion

    #region Timeframe Tests

    [Theory]
    [InlineData(AggregateTimeframe.Second)]
    [InlineData(AggregateTimeframe.Minute)]
    [InlineData(AggregateTimeframe.Hour)]
    [InlineData(AggregateTimeframe.Day)]
    public void Constructor_WithDifferentTimeframes_SetsCorrectTimeframe(AggregateTimeframe timeframe)
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var bar = new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 1000,
            Timeframe: timeframe);

        // Assert
        bar.Timeframe.Should().Be(timeframe);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void Constructor_WithZeroVolume_Succeeds()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var bar = new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 101m,
            Low: 99m,
            Close: 100.5m,
            Volume: 0);

        // Assert
        bar.Volume.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithSameStartAndEndTime_Succeeds()
    {
        // Arrange
        var time = DateTimeOffset.UtcNow;

        // Act
        var bar = new AggregateBar(
            Symbol: "SPY",
            StartTime: time,
            EndTime: time,
            Open: 100m,
            High: 100m,
            Low: 100m,
            Close: 100m,
            Volume: 1000);

        // Assert
        bar.StartTime.Should().Be(bar.EndTime);
    }

    [Fact]
    public void Constructor_WithAllOhlcSameValue_Succeeds()
    {
        // Arrange
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddMinutes(1);

        // Act
        var bar = new AggregateBar(
            Symbol: "SPY",
            StartTime: startTime,
            EndTime: endTime,
            Open: 100m,
            High: 100m,
            Low: 100m,
            Close: 100m,
            Volume: 1000);

        // Assert
        bar.Open.Should().Be(100m);
        bar.High.Should().Be(100m);
        bar.Low.Should().Be(100m);
        bar.Close.Should().Be(100m);
    }

    #endregion
}
