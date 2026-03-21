using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Unit tests for the BboQuotePayload domain model.
/// Tests the FromUpdate factory method and spread/mid-price calculations.
/// </summary>
public class BboQuotePayloadTests
{
    [Fact]
    public void FromUpdate_WithValidQuote_CreatesPayloadWithCalculatedSpread()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 450.10m,
            AskSize: 200,
            SequenceNumber: 1,
            StreamId: "TEST",
            Venue: "NYSE"
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Symbol.Should().Be("SPY");
        payload.BidPrice.Should().Be(450.00m);
        payload.AskPrice.Should().Be(450.10m);
        payload.BidSize.Should().Be(100);
        payload.AskSize.Should().Be(200);
        payload.Spread.Should().Be(0.10m);
        payload.MidPrice.Should().Be(450.05m);
        payload.SequenceNumber.Should().Be(1);
        payload.StreamId.Should().Be("TEST");
        payload.Venue.Should().Be("NYSE");
    }

    [Fact]
    public void FromUpdate_WithZeroBidPrice_DoesNotCalculateSpreadOrMid()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 0m,
            BidSize: 100,
            AskPrice: 450.10m,
            AskSize: 200
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Spread.Should().BeNull();
        payload.MidPrice.Should().BeNull();
    }

    [Fact]
    public void FromUpdate_WithZeroAskPrice_DoesNotCalculateSpreadOrMid()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 0m,
            AskSize: 200
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Spread.Should().BeNull();
        payload.MidPrice.Should().BeNull();
    }

    [Fact]
    public void FromUpdate_WithCrossedQuote_DoesNotCalculateSpreadOrMid()
    {
        // Arrange - bid > ask (crossed market)
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.10m,
            BidSize: 100,
            AskPrice: 450.00m,
            AskSize: 200
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Spread.Should().BeNull();
        payload.MidPrice.Should().BeNull();
    }

    [Fact]
    public void FromUpdate_WithLockedQuote_CalculatesZeroSpread()
    {
        // Arrange - bid == ask (locked market)
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 450.00m,
            AskSize: 200
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Spread.Should().Be(0m);
        payload.MidPrice.Should().Be(450.00m);
    }

    [Fact]
    public void FromUpdate_PreservesTimestamp()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2024, 6, 15, 9, 30, 0, TimeSpan.Zero);
        var update = new MarketQuoteUpdate(
            Timestamp: timestamp,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 450.10m,
            AskSize: 200
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void FromUpdate_UsesProvidedSequenceNumber()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 450.10m,
            AskSize: 200,
            SequenceNumber: 999  // This is from the provider, not the collector
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 42);

        // Assert - should use the seq parameter, not the update's sequence number
        payload.SequenceNumber.Should().Be(42);
    }

    [Fact]
    public void FromUpdate_WithWideSpread_CalculatesCorrectly()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "ILLIQUID",
            BidPrice: 100.00m,
            BidSize: 10,
            AskPrice: 102.00m,
            AskSize: 10
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Spread.Should().Be(2.00m);
        payload.MidPrice.Should().Be(101.00m);
    }

    [Fact]
    public void FromUpdate_WithPennySpread_CalculatesCorrectly()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 450.01m,
            AskSize: 100
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.Spread.Should().Be(0.01m);
        payload.MidPrice.Should().Be(450.005m);
    }

    [Fact]
    public void BboQuotePayload_IsImmutable()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 450.10m,
            AskSize: 200
        );
        var original = BboQuotePayload.FromUpdate(update, seq: 1);

        // Act - use with expression
        var modified = original with { BidPrice = 451.00m };

        // Assert
        original.BidPrice.Should().Be(450.00m);
        modified.BidPrice.Should().Be(451.00m);
    }

    [Fact]
    public void BboQuotePayload_Equality_WorksCorrectly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        var update1 = new MarketQuoteUpdate(timestamp, "SPY", 450.00m, 100, 450.10m, 200);
        var update2 = new MarketQuoteUpdate(timestamp, "SPY", 450.00m, 100, 450.10m, 200);
        var update3 = new MarketQuoteUpdate(timestamp, "SPY", 451.00m, 100, 451.10m, 200);

        var payload1 = BboQuotePayload.FromUpdate(update1, seq: 1);
        var payload2 = BboQuotePayload.FromUpdate(update2, seq: 1);
        var payload3 = BboQuotePayload.FromUpdate(update3, seq: 1);

        // Assert
        payload1.Should().Be(payload2);
        payload1.Should().NotBe(payload3);
    }

    [Fact]
    public void FromUpdate_WithOptionalFieldsNull_CreatesPayload()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 100,
            AskPrice: 450.10m,
            AskSize: 200,
            SequenceNumber: null,
            StreamId: null,
            Venue: null
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.StreamId.Should().BeNull();
        payload.Venue.Should().BeNull();
    }

    [Fact]
    public void FromUpdate_WithLargeSizes_HandlesCorrectly()
    {
        // Arrange
        var update = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            BidPrice: 450.00m,
            BidSize: 10_000_000,
            AskPrice: 450.10m,
            AskSize: 15_000_000
        );

        // Act
        var payload = BboQuotePayload.FromUpdate(update, seq: 1);

        // Assert
        payload.BidSize.Should().Be(10_000_000);
        payload.AskSize.Should().Be(15_000_000);
    }
}
