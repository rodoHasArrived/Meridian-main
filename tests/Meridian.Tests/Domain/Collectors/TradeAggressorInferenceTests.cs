using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Domain.Collectors;

/// <summary>
/// When a provider sends a trade without an aggressor side, the trade collector infers it from
/// the prevailing quote. It used to accept the latest quote from any provider, and quotes
/// stamped after the trade, so a trade could be classified against another feed's book or
/// against a quote that already reflected it.
/// </summary>
public sealed class TradeAggressorInferenceTests
{
    private const string Symbol = "SPY";
    private const string Feed = "polygon";
    private static readonly DateTimeOffset TradeTime = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);

    private readonly TestMarketEventPublisher _publisher = new();
    private readonly QuoteCollector _quotes;
    private readonly TradeDataCollector _trades;

    public TradeAggressorInferenceTests()
    {
        _quotes = new QuoteCollector(_publisher);
        _trades = new TradeDataCollector(_publisher, _quotes);
    }

    [Theory]
    [InlineData(500.10, AggressorSide.Buy)]  // at the ask
    [InlineData(499.90, AggressorSide.Sell)] // at the bid
    [InlineData(500.00, AggressorSide.Unknown)] // inside the spread
    public void PriorQuoteFromTheSameFeed_ClassifiesTheTrade(decimal price, AggressorSide expected)
    {
        Quote(TradeTime.AddMilliseconds(-100), Feed);

        Trade(price, Feed);

        LastAggressor().Should().Be(expected);
    }

    [Fact]
    public void QuoteFromAnotherFeed_IsNotUsed()
    {
        Quote(TradeTime.AddMilliseconds(-100), "alpaca");

        Trade(500.10m, Feed);

        LastAggressor().Should().Be(AggressorSide.Unknown, "another feed's book can sit at different prices");
    }

    [Fact]
    public void QuoteStampedAfterTheTrade_IsNotUsed()
    {
        Quote(TradeTime.AddMilliseconds(50), Feed);

        Trade(500.10m, Feed);

        LastAggressor().Should().Be(AggressorSide.Unknown, "a later quote may already reflect the trade");
    }

    [Fact]
    public void QuoteOlderThanTheFreshnessThreshold_IsNotUsed()
    {
        Quote(TradeTime.AddMilliseconds(-300), Feed);

        Trade(500.10m, Feed);

        LastAggressor().Should().Be(AggressorSide.Unknown);
    }

    [Fact]
    public void QuoteAtTheSameInstant_IsUsed()
    {
        Quote(TradeTime, Feed);

        Trade(500.10m, Feed);

        LastAggressor().Should().Be(AggressorSide.Buy);
    }

    [Fact]
    public void ProviderSuppliedAggressor_IsKept()
    {
        Quote(TradeTime.AddMilliseconds(-100), Feed);

        Trade(500.10m, Feed, AggressorSide.Sell);

        LastAggressor().Should().Be(AggressorSide.Sell);
    }

    [Fact]
    public void QuoteStore_TryGetFromSource_MatchesTheQuotingFeedCaseInsensitively()
    {
        Quote(TradeTime, Feed);

        _quotes.TryGetFromSource(Symbol, "POLYGON", out var same).Should().BeTrue();
        same!.AskPrice.Should().Be(500.10m);
        _quotes.TryGetFromSource(Symbol, "alpaca", out var other).Should().BeFalse();
        other.Should().BeNull();
        _quotes.TryGet(Symbol, out var any).Should().BeTrue("TryGet still returns the latest quote from any feed");
        any.Should().BeSameAs(same);
    }

    private void Quote(DateTimeOffset at, string source)
        => _quotes.OnQuote(new MarketQuoteUpdate(at, Symbol, 499.90m, 100, 500.10m, 100, Source: source));

    private void Trade(decimal price, string source, AggressorSide aggressor = AggressorSide.Unknown)
        => _trades.OnTrade(new MarketTradeUpdate(
            TradeTime, Symbol, price, 10, aggressor, SequenceNumber: 1, StreamId: "TEST", Venue: "ARCA", Source: source));

    private AggressorSide LastAggressor() => _trades.GetRecentTrades(Symbol).Should().ContainSingle().Subject.Aggressor;
}
