using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Domain.Collectors;

/// <summary>
/// Unit tests for the OptionDataCollector domain collector.
/// Tests quote/trade/greeks/chain capture, event publication, and state queries.
/// </summary>
public sealed class OptionDataCollectorTests
{
    private readonly TestMarketEventPublisher _publisher = new();
    private readonly OptionDataCollector _sut;

    public OptionDataCollectorTests()
    {
        _sut = new OptionDataCollector(_publisher);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullPublisher_ThrowsArgumentNullException()
    {
        var act = () => new OptionDataCollector(null!);
        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("publisher");
    }

    #endregion

    #region OnOptionQuote Tests

    [Fact]
    public void OnOptionQuote_PublishesMarketEvent()
    {
        var quote = CreateOptionQuote("AAPL", 150m, OptionRight.Call);

        _sut.OnOptionQuote(quote);

        _publisher.PublishedEvents.Should().HaveCount(1);
        _publisher.PublishedEvents[0].Type.Should().Be(MarketEventType.OptionQuote);
        _publisher.PublishedEvents[0].Payload.Should().Be(quote);
    }

    [Fact]
    public void OnOptionQuote_CachesQuoteByContract()
    {
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);
        var quote = CreateOptionQuote(contract);

        _sut.OnOptionQuote(quote);

        var cached = _sut.GetLatestQuote(contract);
        cached.Should().NotBeNull();
        cached!.BidPrice.Should().Be(quote.BidPrice);
    }

    [Fact]
    public void OnOptionQuote_UpdatesExistingQuote()
    {
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);
        var quote1 = CreateOptionQuote(contract, bidPrice: 2.0m);
        var quote2 = CreateOptionQuote(contract, bidPrice: 3.0m);

        _sut.OnOptionQuote(quote1);
        _sut.OnOptionQuote(quote2);

        var cached = _sut.GetLatestQuote(contract);
        cached!.BidPrice.Should().Be(3.0m);
        _publisher.PublishedEvents.Should().HaveCount(2);
    }

    [Fact]
    public void OnOptionQuote_WithNull_ThrowsArgumentNullException()
    {
        var act = () => _sut.OnOptionQuote(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region OnOptionTrade Tests

    [Fact]
    public void OnOptionTrade_PublishesMarketEvent()
    {
        var trade = CreateOptionTrade("AAPL", 150m, OptionRight.Call);

        _sut.OnOptionTrade(trade);

        _publisher.PublishedEvents.Should().HaveCount(1);
        _publisher.PublishedEvents[0].Type.Should().Be(MarketEventType.OptionTrade);
    }

    [Fact]
    public void OnOptionTrade_BuffersRecentTrades()
    {
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);

        for (int i = 0; i < 5; i++)
        {
            _sut.OnOptionTrade(CreateOptionTrade(contract, price: 2.0m + i * 0.1m));
        }

        var recent = _sut.GetRecentTrades(contract, 10);
        recent.Should().HaveCount(5);
        // Newest first
        recent[0].Price.Should().BeGreaterThan(recent[4].Price);
    }

    [Fact]
    public void OnOptionTrade_WithNull_ThrowsArgumentNullException()
    {
        var act = () => _sut.OnOptionTrade(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region OnGreeksUpdate Tests

    [Fact]
    public void OnGreeksUpdate_PublishesMarketEvent()
    {
        var greeks = CreateGreeksSnapshot("AAPL", 150m, OptionRight.Call);

        _sut.OnGreeksUpdate(greeks);

        _publisher.PublishedEvents.Should().HaveCount(1);
        _publisher.PublishedEvents[0].Type.Should().Be(MarketEventType.OptionGreeks);
    }

    [Fact]
    public void OnGreeksUpdate_CachesGreeksByContract()
    {
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);
        var greeks = CreateGreeksSnapshot(contract);

        _sut.OnGreeksUpdate(greeks);

        var cached = _sut.GetLatestGreeks(contract);
        cached.Should().NotBeNull();
        cached!.Delta.Should().Be(greeks.Delta);
    }

    [Fact]
    public void OnGreeksUpdate_WithNull_ThrowsArgumentNullException()
    {
        var act = () => _sut.OnGreeksUpdate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region OnChainSnapshot Tests

    [Fact]
    public void OnChainSnapshot_PublishesMarketEvent()
    {
        var chain = CreateChainSnapshot("AAPL", new DateOnly(2026, 3, 21));

        _sut.OnChainSnapshot(chain);

        _publisher.PublishedEvents.Should().HaveCount(1);
        _publisher.PublishedEvents[0].Type.Should().Be(MarketEventType.OptionChain);
        _publisher.PublishedEvents[0].Symbol.Should().Be("AAPL");
    }

    [Fact]
    public void OnChainSnapshot_CachesChainByUnderlyingAndExpiration()
    {
        var expiry = new DateOnly(2026, 3, 21);
        var chain = CreateChainSnapshot("AAPL", expiry);

        _sut.OnChainSnapshot(chain);

        var cached = _sut.GetLatestChain("AAPL", expiry);
        cached.Should().NotBeNull();
        cached!.UnderlyingSymbol.Should().Be("AAPL");
    }

    [Fact]
    public void GetChainsForUnderlying_ReturnsAllExpirations()
    {
        _sut.OnChainSnapshot(CreateChainSnapshot("AAPL", new DateOnly(2026, 3, 21)));
        _sut.OnChainSnapshot(CreateChainSnapshot("AAPL", new DateOnly(2026, 4, 18)));
        _sut.OnChainSnapshot(CreateChainSnapshot("SPY", new DateOnly(2026, 3, 21)));

        var aaplChains = _sut.GetChainsForUnderlying("AAPL");
        aaplChains.Should().HaveCount(2);

        var spyChains = _sut.GetChainsForUnderlying("SPY");
        spyChains.Should().HaveCount(1);
    }

    [Fact]
    public void OnChainSnapshot_WithNull_ThrowsArgumentNullException()
    {
        var act = () => _sut.OnChainSnapshot(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region OnOpenInterestUpdate Tests

    [Fact]
    public void OnOpenInterestUpdate_PublishesMarketEvent()
    {
        var oi = CreateOpenInterestUpdate("AAPL", 150m, OptionRight.Call);

        _sut.OnOpenInterestUpdate(oi);

        _publisher.PublishedEvents.Should().HaveCount(1);
        _publisher.PublishedEvents[0].Type.Should().Be(MarketEventType.OpenInterest);
    }

    [Fact]
    public void OnOpenInterestUpdate_CachesOpenInterest()
    {
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);
        var oi = CreateOpenInterestUpdate(contract, openInterest: 5000);

        _sut.OnOpenInterestUpdate(oi);

        var cached = _sut.GetLatestOpenInterest(contract);
        cached.Should().NotBeNull();
        cached!.OpenInterest.Should().Be(5000);
    }

    [Fact]
    public void OnOpenInterestUpdate_WithNull_ThrowsArgumentNullException()
    {
        var act = () => _sut.OnOpenInterestUpdate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Query Methods

    [Fact]
    public void GetQuotesForUnderlying_ReturnsMatchingQuotes()
    {
        _sut.OnOptionQuote(CreateOptionQuote("AAPL", 145m, OptionRight.Call));
        _sut.OnOptionQuote(CreateOptionQuote("AAPL", 150m, OptionRight.Call));
        _sut.OnOptionQuote(CreateOptionQuote("AAPL", 155m, OptionRight.Put));
        _sut.OnOptionQuote(CreateOptionQuote("SPY", 580m, OptionRight.Call));

        var aaplQuotes = _sut.GetQuotesForUnderlying("AAPL");
        aaplQuotes.Should().HaveCount(3);

        var spyQuotes = _sut.GetQuotesForUnderlying("SPY");
        spyQuotes.Should().HaveCount(1);
    }

    [Fact]
    public void GetQuotesForUnderlying_WithEmptySymbol_ReturnsEmpty()
    {
        var result = _sut.GetQuotesForUnderlying("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetTrackedUnderlyings_ReturnsUniqueUnderlyings()
    {
        _sut.OnOptionQuote(CreateOptionQuote("AAPL", 150m, OptionRight.Call));
        _sut.OnOptionQuote(CreateOptionQuote("AAPL", 155m, OptionRight.Put));
        _sut.OnOptionQuote(CreateOptionQuote("SPY", 580m, OptionRight.Call));
        _sut.OnChainSnapshot(CreateChainSnapshot("QQQ", new DateOnly(2026, 3, 21)));

        var underlyings = _sut.GetTrackedUnderlyings();
        underlyings.Should().HaveCount(3);
        underlyings.Should().Contain("AAPL");
        underlyings.Should().Contain("SPY");
        underlyings.Should().Contain("QQQ");
    }

    [Fact]
    public void GetSummary_ReturnsCorrectCounts()
    {
        _sut.OnOptionQuote(CreateOptionQuote("AAPL", 150m, OptionRight.Call));
        _sut.OnOptionQuote(CreateOptionQuote("AAPL", 155m, OptionRight.Put));
        _sut.OnGreeksUpdate(CreateGreeksSnapshot("AAPL", 150m, OptionRight.Call));
        _sut.OnChainSnapshot(CreateChainSnapshot("AAPL", new DateOnly(2026, 3, 21)));
        _sut.OnOpenInterestUpdate(CreateOpenInterestUpdate("AAPL", 150m, OptionRight.Call));

        var summary = _sut.GetSummary();
        summary.TrackedContracts.Should().Be(2);
        summary.TrackedChains.Should().Be(1);
        summary.ContractsWithGreeks.Should().Be(1);
        summary.ContractsWithOpenInterest.Should().Be(1);
        summary.TrackedUnderlyings.Should().Be(1);
    }

    [Fact]
    public void GetRecentTrades_WithNoTrades_ReturnsEmpty()
    {
        var contract = CreateContract("AAPL", 150m, OptionRight.Call);
        var result = _sut.GetRecentTrades(contract);
        result.Should().BeEmpty();
    }

    [Fact]
    public void GetLatestQuote_WithUnknownContract_ReturnsNull()
    {
        var contract = CreateContract("UNKNOWN", 999m, OptionRight.Put);
        _sut.GetLatestQuote(contract).Should().BeNull();
    }

    [Fact]
    public void GetLatestQuote_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => _sut.GetLatestQuote(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetLatestGreeks_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => _sut.GetLatestGreeks(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetLatestOpenInterest_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => _sut.GetLatestOpenInterest(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetRecentTrades_WithNullContract_ThrowsArgumentNullException()
    {
        var act = () => _sut.GetRecentTrades(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Helpers

    private static OptionContractSpec CreateContract(string underlying, decimal strike, OptionRight right)
    {
        return new OptionContractSpec(
            UnderlyingSymbol: underlying,
            Strike: strike,
            Expiration: new DateOnly(2026, 3, 21),
            Right: right,
            Style: right == OptionRight.Call ? OptionStyle.American : OptionStyle.American,
            Multiplier: 100,
            Exchange: "SMART",
            Currency: "USD");
    }

    private static OptionQuote CreateOptionQuote(string underlying, decimal strike, OptionRight right, decimal bidPrice = 2.5m)
    {
        var contract = CreateContract(underlying, strike, right);
        return CreateOptionQuote(contract, bidPrice);
    }

    private static OptionQuote CreateOptionQuote(OptionContractSpec contract, decimal bidPrice = 2.5m)
    {
        return new OptionQuote(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: $"{contract.UnderlyingSymbol}260321{(contract.Right == OptionRight.Call ? "C" : "P")}{contract.Strike:00000000}",
            Contract: contract,
            BidPrice: bidPrice,
            AskPrice: bidPrice + 0.10m,
            BidSize: 50,
            AskSize: 60,
            UnderlyingPrice: 155m);
    }

    private static OptionTrade CreateOptionTrade(string underlying, decimal strike, OptionRight right)
    {
        var contract = CreateContract(underlying, strike, right);
        return CreateOptionTrade(contract);
    }

    private static OptionTrade CreateOptionTrade(OptionContractSpec contract, decimal price = 2.5m)
    {
        return new OptionTrade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: $"{contract.UnderlyingSymbol}260321{(contract.Right == OptionRight.Call ? "C" : "P")}{contract.Strike:00000000}",
            Contract: contract,
            Price: price,
            Size: 10,
            Aggressor: AggressorSide.Buy,
            UnderlyingPrice: 155m);
    }

    private static GreeksSnapshot CreateGreeksSnapshot(string underlying, decimal strike, OptionRight right)
    {
        var contract = CreateContract(underlying, strike, right);
        return CreateGreeksSnapshot(contract);
    }

    private static GreeksSnapshot CreateGreeksSnapshot(OptionContractSpec contract)
    {
        return new GreeksSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: $"{contract.UnderlyingSymbol}260321{(contract.Right == OptionRight.Call ? "C" : "P")}{contract.Strike:00000000}",
            Contract: contract,
            Delta: 0.55m,
            Gamma: 0.03m,
            Theta: -0.05m,
            Vega: 0.15m,
            Rho: 0.01m,
            ImpliedVolatility: 0.25m,
            UnderlyingPrice: 155m);
    }

    private static OptionChainSnapshot CreateChainSnapshot(string underlying, DateOnly expiry)
    {
        var strikes = new[] { 145m, 150m, 155m };
        var calls = strikes.Select(s => CreateOptionQuote(underlying, s, OptionRight.Call)).ToList();
        var puts = strikes.Select(s => CreateOptionQuote(underlying, s, OptionRight.Put)).ToList();

        return new OptionChainSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            UnderlyingSymbol: underlying,
            UnderlyingPrice: 155m,
            Expiration: expiry,
            Strikes: strikes,
            Calls: calls,
            Puts: puts);
    }

    private static OpenInterestUpdate CreateOpenInterestUpdate(string underlying, decimal strike, OptionRight right)
    {
        var contract = CreateContract(underlying, strike, right);
        return CreateOpenInterestUpdate(contract);
    }

    private static OpenInterestUpdate CreateOpenInterestUpdate(OptionContractSpec contract, long openInterest = 5000)
    {
        return new OpenInterestUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: $"{contract.UnderlyingSymbol}260321{(contract.Right == OptionRight.Call ? "C" : "P")}{contract.Strike:00000000}",
            Contract: contract,
            OpenInterest: openInterest,
            Volume: 1200);
    }

    #endregion
}
