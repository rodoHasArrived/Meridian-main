using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.Ui;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in, read-only checks of the Alpaca market-data adapters against the real market-data
/// host: option chain snapshots and quotes, and historical bars, trades, quotes and auctions. Separately
/// gated from the paper Trading API checks because they need a second host and depend on the
/// account's data entitlement (the free IEX and indicative-options feeds are enough).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LiveProvider")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaMarketDataSandboxTests(ITestOutputHelper output)
{
    private const string Underlying = "AAPL";

    [AlpacaMarketDataFact]
    public async Task OptionChainSnapshotAndQuote_CarryUnderlyingPriceQuotesAndOpenInterest()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = timeout.Token;
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.ReadOnlyWithMarketData);
        using var httpClient = clients.CreateClient("alpaca-options");
        var provider = new AlpacaOptionsChainProvider(clients, key, secret, httpClient, useSandbox: true);

        // An expiration a few weeks out is quoted on both sides across the strikes near the money.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expirations = await provider.GetExpirationsAsync(Underlying, ct);
        var expiration = expirations.FirstOrDefault(candidate => candidate >= today.AddDays(21));
        (expiration != default).Should().BeTrue("the underlying lists an expiration at least three weeks out");

        var chain = await provider.GetChainSnapshotAsync(Underlying, expiration, strikeRange: null, ct);

        (chain is not null).Should().BeTrue("a chain is produced once the underlying price is read from the market-data host");
        chain!.UnderlyingSymbol.Should().Be(Underlying);
        chain.Expiration.Should().Be(expiration);
        chain.UnderlyingPrice.Should().BeGreaterThan(0m);
        chain.Calls.Should().NotBeEmpty();
        chain.Puts.Should().NotBeEmpty();
        chain.Strikes.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        chain.Calls.Should().OnlyContain(call => call.Contract.Right == OptionRight.Call && call.Symbol[call.Symbol.Length - 9] == 'C'
            && call.Contract.Expiration == expiration && call.UnderlyingPrice == chain.UnderlyingPrice);
        chain.Puts.Should().OnlyContain(put => put.Contract.Right == OptionRight.Put && put.Symbol[put.Symbol.Length - 9] == 'P'
            && put.Contract.Expiration == expiration && put.UnderlyingPrice == chain.UnderlyingPrice);
        chain.Calls.Concat(chain.Puts).Should().OnlyContain(quote => quote.BidPrice >= 0m && quote.AskPrice >= 0m);
        chain.Calls.Concat(chain.Puts).Should().Contain(quote => quote.OpenInterest > 0,
            "open interest is joined from the contract listing");
        chain.Calls.Concat(chain.Puts).Should().Contain(quote => quote.Delta.HasValue && quote.ImpliedVolatility > 0m,
            "the indicative feed computes greeks for liquid contracts");

        var nearMoney = await provider.GetChainSnapshotAsync(Underlying, expiration, strikeRange: 3, ct);
        (nearMoney is not null).Should().BeTrue();
        nearMoney!.Strikes.Count.Should().BeLessThanOrEqualTo(7);
        nearMoney.Strikes.Should().OnlyContain(strike => chain.Strikes.Contains(strike));
        // The kept strikes surround the underlying rather than sit at one end of the chain.
        nearMoney.Strikes.Min().Should().BeLessThan(nearMoney.UnderlyingPrice * 1.05m);
        nearMoney.Strikes.Max().Should().BeGreaterThan(nearMoney.UnderlyingPrice * 0.95m);

        var atTheMoney = chain.Calls.OrderBy(call => Math.Abs(call.Contract.Strike - chain.UnderlyingPrice)).First();
        var quote = await provider.GetOptionQuoteAsync(atTheMoney.Contract, ct);

        (quote is not null).Should().BeTrue("a single contract is read from its own snapshot route");
        quote!.Symbol.Should().Be(atTheMoney.Symbol);
        quote.Contract.Should().Be(atTheMoney.Contract);
        quote.UnderlyingPrice.Should().BeGreaterThan(0m);
        (quote.AskPrice >= quote.BidPrice).Should().BeTrue("an at-the-money quote is not crossed");

        output.WriteLine("Option chain {0:yyyy-MM-dd}: calls={1}, puts={2}, strikes={3}, near-money strikes={4}; requests={5}.",
            expiration, chain.Calls.Count, chain.Puts.Count, chain.Strikes.Count, nearMoney.Strikes.Count, clients.RequestCount);
    }

    [AlpacaMarketDataFact]
    public async Task HistoricalBarsTradesQuotesAndAuctions_AreReadFromTheMarketDataHost()
    {
        var (key, secret) = AlpacaPaperSandbox.RequirePaperCredentials();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var ct = timeout.Token;
        using var clients = new AlpacaPaperSandboxHttpClientFactory(AlpacaPaperSandboxAccess.ReadOnlyWithMarketData);
        using var httpClient = clients.CreateClient("alpaca-historical");
        using var provider = new AlpacaHistoricalDataProvider(key, secret, feed: "iex", httpClient: httpClient);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddDays(-45);
        var to = today.AddDays(-1);

        var bars = await provider.GetDailyBarsAsync(Underlying, from, to, ct);

        bars.Count.Should().BeGreaterThanOrEqualTo(20, "six weeks hold about thirty sessions");
        bars.Select(bar => bar.SessionDate).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        bars.Should().OnlyContain(bar =>
            bar.Symbol == Underlying
            && bar.SessionDate >= from && bar.SessionDate <= to
            && bar.Low > 0m && bar.Low <= bar.Open && bar.Low <= bar.Close
            && bar.High >= bar.Open && bar.High >= bar.Close
            && bar.Volume > 0);

        var adjusted = await provider.GetAdjustedDailyBarsAsync(Underlying, from, to, ct);
        adjusted.Count.Should().Be(bars.Count, "the adjusted series covers the same sessions");

        // Two regular-session minutes of the most recent completed session: enough to hold
        // prints and quotes for a liquid name, small enough to be a page or two.
        var session = bars[^1].SessionDate;
        var start = new DateTimeOffset(session.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);
        var end = start.AddMinutes(2);

        var trades = await provider.GetHistoricalTradesAsync(Underlying, start, end, limit: null, ct);
        trades.Trades.Should().NotBeEmpty("a liquid name trades on IEX in any regular-session minute");
        trades.Trades.Should().OnlyContain(trade => trade.Symbol == Underlying && trade.Price > 0m && trade.Size > 0
            && trade.TradeId != "" && trade.Timestamp >= start && trade.Timestamp <= end);
        trades.Trades.Select(trade => trade.Timestamp).Should().BeInAscendingOrder();

        var quotes = await provider.GetHistoricalQuotesAsync(Underlying, start, end, limit: null, ct);
        quotes.Quotes.Should().NotBeEmpty();
        quotes.Quotes.Should().OnlyContain(quote => quote.Symbol == Underlying
            && quote.Timestamp >= start && quote.Timestamp <= end);
        quotes.Quotes.Should().Contain(quote => quote.BidPrice > 0m && quote.AskPrice >= quote.BidPrice);

        // Auctions exist only on the consolidated feed; the provider's IEX default must not be sent.
        var auctions = await provider.GetHistoricalAuctionsAsync(Underlying, session, session, ct);
        var auction = auctions.Auctions.Should().ContainSingle().Subject;
        auction.SessionDate.Should().Be(session);
        auction.OpeningAuctions.Should().NotBeEmpty();
        auction.ClosingAuctions.Should().NotBeEmpty();
        auction.OpeningAuctions.Concat(auction.ClosingAuctions).Should().OnlyContain(print => print.Price > 0m);

        output.WriteLine("Historical: bars={0} ({1:yyyy-MM-dd} to {2:yyyy-MM-dd}), adjusted={3}, trades={4}, quotes={5}, auction days={6}; requests={7}.",
            bars.Count, bars[0].SessionDate, bars[^1].SessionDate, adjusted.Count, trades.Trades.Count, quotes.Quotes.Count,
            auctions.Auctions.Count, clients.RequestCount);
    }
}

/// <summary>
/// Gates the market-data checks behind their own opt-in on top of the sandbox opt-in: they reach
/// a second host and depend on the account's data entitlement.
/// </summary>
public sealed class AlpacaMarketDataFactAttribute : FactAttribute
{
    public AlpacaMarketDataFactAttribute()
    {
        if (!AlpacaPaperSandbox.IsOptedIn(AlpacaPaperSandbox.OptInVariable)
            || !AlpacaPaperSandbox.IsOptedIn(AlpacaPaperSandbox.MarketDataOptInVariable))
            Skip = "Alpaca market-data validation reaches the market-data host and is opt-in: set MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1, MERIDIAN_RUN_ALPACA_MARKET_DATA=1 and the documented paper credentials.";
    }
}
