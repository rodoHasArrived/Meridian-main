using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Integration tests for Alpaca quote routing to QuoteCollector.
/// Tests the end-to-end flow from quote update to published event.
/// </summary>
public class AlpacaQuoteRoutingTests
{
    private readonly TestMarketEventPublisher _publisher;
    private readonly QuoteCollector _quoteCollector;
    private IReadOnlyList<MarketEvent> _publishedEvents => _publisher.PublishedEvents;

    public AlpacaQuoteRoutingTests()
    {
        _publisher = new TestMarketEventPublisher();
        _quoteCollector = new QuoteCollector(_publisher);
    }

    [Fact]
    public void OnQuote_WithAlpacaStyleUpdate_PublishesBboEvent()
    {
        // Arrange - simulate Alpaca quote format
        var quoteUpdate = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.Parse("2024-06-15T14:30:00Z"),
            Symbol: "AAPL",
            BidPrice: 185.50m,
            BidSize: 500,
            AskPrice: 185.55m,
            AskSize: 300,
            SequenceNumber: null,
            StreamId: "ALPACA",
            Venue: "ALPACA"
        );

        // Act
        _quoteCollector.OnQuote(quoteUpdate);

        // Assert
        _publishedEvents.Should().HaveCount(1);
        var evt = _publishedEvents[0];
        evt.Type.Should().Be(MarketEventType.BboQuote);
        evt.Symbol.Should().Be("AAPL");

        var payload = evt.Payload.Should().BeOfType<BboQuotePayload>().Subject;
        payload.BidPrice.Should().Be(185.50m);
        payload.AskPrice.Should().Be(185.55m);
        payload.BidSize.Should().Be(500);
        payload.AskSize.Should().Be(300);
        payload.Spread.Should().Be(0.05m);
        payload.StreamId.Should().Be("ALPACA");
        payload.Venue.Should().Be("ALPACA");
    }

    [Fact]
    public void OnQuote_WithMultipleSymbols_TracksSequencePerSymbol()
    {
        // Arrange
        var aaplQuote1 = CreateAlpacaQuote("AAPL", 185.50m, 185.55m);
        var msftQuote1 = CreateAlpacaQuote("MSFT", 380.00m, 380.10m);
        var aaplQuote2 = CreateAlpacaQuote("AAPL", 185.60m, 185.65m);
        var msftQuote2 = CreateAlpacaQuote("MSFT", 380.20m, 380.30m);

        // Act
        _quoteCollector.OnQuote(aaplQuote1);
        _quoteCollector.OnQuote(msftQuote1);
        _quoteCollector.OnQuote(aaplQuote2);
        _quoteCollector.OnQuote(msftQuote2);

        // Assert
        _publishedEvents.Should().HaveCount(4);

        // AAPL sequences: 1, 2
        var aaplEvents = _publishedEvents
            .Where(e => e.Symbol == "AAPL")
            .Select(e => ((BboQuotePayload)e.Payload!).SequenceNumber)
            .ToList();
        aaplEvents.Should().BeEquivalentTo(new[] { 1L, 2L });

        // MSFT sequences: 1, 2
        var msftEvents = _publishedEvents
            .Where(e => e.Symbol == "MSFT")
            .Select(e => ((BboQuotePayload)e.Payload!).SequenceNumber)
            .ToList();
        msftEvents.Should().BeEquivalentTo(new[] { 1L, 2L });
    }

    [Fact]
    public void OnQuote_WithRapidUpdates_ProcessesAllSuccessfully()
    {
        // Arrange - simulate rapid quote updates
        var quotes = Enumerable.Range(0, 100)
            .Select(i => CreateAlpacaQuote("SPY", 450.00m + i * 0.01m, 450.05m + i * 0.01m))
            .ToList();

        // Act
        foreach (var quote in quotes)
        {
            _quoteCollector.OnQuote(quote);
        }

        // Assert
        _publishedEvents.Should().HaveCount(100);
        var sequences = _publishedEvents
            .Select(e => ((BboQuotePayload)e.Payload!).SequenceNumber)
            .ToList();
        sequences.Should().BeInAscendingOrder();
        sequences.Last().Should().Be(100);
    }

    [Fact]
    public void OnQuote_UpdatesLatestState()
    {
        // Arrange
        var quote1 = CreateAlpacaQuote("AAPL", 185.50m, 185.55m);
        var quote2 = CreateAlpacaQuote("AAPL", 186.00m, 186.05m);

        // Act
        _quoteCollector.OnQuote(quote1);
        _quoteCollector.OnQuote(quote2);

        // Assert
        var found = _quoteCollector.TryGet("AAPL", out var latestQuote);
        found.Should().BeTrue();
        latestQuote!.BidPrice.Should().Be(186.00m);
        latestQuote.AskPrice.Should().Be(186.05m);
    }

    [Fact]
    public void OnQuote_WithZeroPrices_StillPublishes()
    {
        // Arrange - pre-market or stale quote scenarios
        var quote = new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "AAPL",
            BidPrice: 0m,
            BidSize: 0,
            AskPrice: 0m,
            AskSize: 0,
            StreamId: "ALPACA",
            Venue: "ALPACA"
        );

        // Act
        _quoteCollector.OnQuote(quote);

        // Assert
        _publishedEvents.Should().HaveCount(1);
        var payload = (BboQuotePayload)_publishedEvents[0].Payload!;
        payload.Spread.Should().BeNull(); // No spread calculation for zero prices
        payload.MidPrice.Should().BeNull();
    }

    [Fact]
    public void Snapshot_ReturnsAllTrackedQuotes()
    {
        // Arrange
        _quoteCollector.OnQuote(CreateAlpacaQuote("AAPL", 185.50m, 185.55m));
        _quoteCollector.OnQuote(CreateAlpacaQuote("MSFT", 380.00m, 380.10m));
        _quoteCollector.OnQuote(CreateAlpacaQuote("GOOGL", 140.00m, 140.10m));

        // Act
        var snapshot = _quoteCollector.Snapshot();

        // Assert
        snapshot.Should().HaveCount(3);
        snapshot.Keys.Should().Contain(new[] { "AAPL", "MSFT", "GOOGL" });
    }

    [Fact]
    public void TryRemove_RemovesQuoteAndResetsSequence()
    {
        // Arrange
        _quoteCollector.OnQuote(CreateAlpacaQuote("AAPL", 185.50m, 185.55m));
        _quoteCollector.OnQuote(CreateAlpacaQuote("AAPL", 186.00m, 186.05m));

        // Act
        var removed = _quoteCollector.TryRemove("AAPL", out var removedQuote);

        // Assert
        removed.Should().BeTrue();
        removedQuote!.SequenceNumber.Should().Be(2);

        // Add new quote - sequence should restart
        _publisher.Clear();
        _quoteCollector.OnQuote(CreateAlpacaQuote("AAPL", 187.00m, 187.05m));
        var newPayload = (BboQuotePayload)_publishedEvents[0].Payload!;
        newPayload.SequenceNumber.Should().Be(1);
    }

    [Fact]
    public void OnQuote_WhenPublisherRejectsDueToBackpressure_DoesNotThrow()
    {
        // Arrange
        _publisher.SetReturnValue(false); // Simulate backpressure

        var quote = CreateAlpacaQuote("AAPL", 185.50m, 185.55m);

        // Act
        var act = () => _quoteCollector.OnQuote(quote);

        // Assert
        act.Should().NotThrow();
        // State should still be updated even if publish fails
        _quoteCollector.TryGet("AAPL", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("AAPL")]
    [InlineData("aapl")]
    [InlineData("Aapl")]
    public void TryGet_IsCaseInsensitive(string lookupSymbol)
    {
        // Arrange
        _quoteCollector.OnQuote(CreateAlpacaQuote("AAPL", 185.50m, 185.55m));

        // Act
        var found = _quoteCollector.TryGet(lookupSymbol, out var quote);

        // Assert
        found.Should().BeTrue();
        quote!.BidPrice.Should().Be(185.50m);
    }

    private static MarketQuoteUpdate CreateAlpacaQuote(string symbol, decimal bidPrice, decimal askPrice)
    {
        return new MarketQuoteUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            BidPrice: bidPrice,
            BidSize: 100,
            AskPrice: askPrice,
            AskSize: 100,
            StreamId: "ALPACA",
            Venue: "ALPACA"
        );
    }
}
