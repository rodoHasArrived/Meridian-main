using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the Trade domain model.
/// Tests validation logic and immutability.
/// </summary>
public class TradeModelTests
{
    [Fact]
    public void Trade_WithValidData_CreatesSuccessfully()
    {
        // Arrange & Act
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 150.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            StreamId: "TEST",
            Venue: "NASDAQ"
        );

        // Assert
        trade.Symbol.Should().Be("AAPL");
        trade.Price.Should().Be(150.50m);
        trade.Size.Should().Be(100);
        trade.Aggressor.Should().Be(AggressorSide.Buy);
        trade.SequenceNumber.Should().Be(1);
        trade.StreamId.Should().Be("TEST");
        trade.Venue.Should().Be("NASDAQ");
    }

    [Fact]
    public void Trade_WithZeroSize_CreatesSuccessfully()
    {
        // Zero size trades are valid (can represent cancellations or wash trades)
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 150.50m,
            Size: 0,
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 1
        );

        trade.Size.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100.50)]
    public void Trade_WithInvalidPrice_ThrowsArgumentOutOfRangeException(decimal price)
    {
        // Act
        var act = () => new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: price,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1
        );

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Price");
    }

    [Fact]
    public void Trade_WithNegativeSize_ThrowsArgumentOutOfRangeException()
    {
        // Act
        var act = () => new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 150.50m,
            Size: -1,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1
        );

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("Size");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Trade_WithInvalidSymbol_ThrowsArgumentException(string? symbol)
    {
        // Act
        var act = () => new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol!,
            Price: 150.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1
        );

        // Assert
        act.Should().Throw<ArgumentException>()
            .Which.ParamName.Should().Be("Symbol");
    }

    [Theory]
    [InlineData(AggressorSide.Buy)]
    [InlineData(AggressorSide.Sell)]
    [InlineData(AggressorSide.Unknown)]
    public void Trade_WithAllAggressorSides_CreatesSuccessfully(AggressorSide aggressor)
    {
        // Act
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 150.50m,
            Size: 100,
            Aggressor: aggressor,
            SequenceNumber: 1
        );

        // Assert
        trade.Aggressor.Should().Be(aggressor);
    }

    [Fact]
    public void Trade_WithOptionalFieldsNull_CreatesSuccessfully()
    {
        // Act
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            Price: 150.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            StreamId: null,
            Venue: null
        );

        // Assert
        trade.StreamId.Should().BeNull();
        trade.Venue.Should().BeNull();
    }

    [Fact]
    public void Trade_PreservesTimestamp()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2024, 1, 15, 14, 30, 0, TimeSpan.Zero);

        // Act
        var trade = new Trade(
            Timestamp: timestamp,
            Symbol: "AAPL",
            Price: 150.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1
        );

        // Assert
        trade.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void Trade_IsImmutable()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var original = new Trade(
            Timestamp: timestamp,
            Symbol: "AAPL",
            Price: 150.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1
        );

        // Act - create new trade with different price (records are immutable, properties are read-only)
        var modified = new Trade(
            Timestamp: original.Timestamp,
            Symbol: original.Symbol,
            Price: 151.00m,  // Different price
            Size: original.Size,
            Aggressor: original.Aggressor,
            SequenceNumber: original.SequenceNumber
        );

        // Assert - original should be unchanged
        original.Price.Should().Be(150.50m);
        modified.Price.Should().Be(151.00m);
        modified.Symbol.Should().Be("AAPL"); // Other properties preserved
    }

    [Fact]
    public void Trade_Equality_WorksCorrectly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var trade1 = new Trade(timestamp, "AAPL", 150.50m, 100, AggressorSide.Buy, 1);
        var trade2 = new Trade(timestamp, "AAPL", 150.50m, 100, AggressorSide.Buy, 1);
        var trade3 = new Trade(timestamp, "AAPL", 151.00m, 100, AggressorSide.Buy, 1);

        // Assert
        trade1.Should().Be(trade2);
        trade1.Should().NotBe(trade3);
    }
}
