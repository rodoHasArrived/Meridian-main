using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Tests.Ui;
using Xunit.Abstractions;

namespace Meridian.Tests.Integration;

/// <summary>
/// Opt-in, read-only check of the Alpaca options chain adapter against the real market-data
/// host: an option chain snapshot and a single-contract quote. Separately gated from the paper
/// Trading API checks because it needs a second host and depends on the account's data
/// entitlement (the free indicative-options feed is enough).
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "LiveProvider")]
[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class AlpacaOptionsMarketDataSandboxTests(ITestOutputHelper output)
{
    private const string Underlying = "AAPL";

    [AlpacaOptionsMarketDataFact]
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
}

/// <summary>
/// Gates the options market-data check behind the market-data opt-in on top of the sandbox
/// opt-in: it reaches a second host and depends on the account's data entitlement.
/// </summary>
public sealed class AlpacaOptionsMarketDataFactAttribute : FactAttribute
{
    public AlpacaOptionsMarketDataFactAttribute()
    {
        if (!AlpacaPaperSandbox.IsOptedIn(AlpacaPaperSandbox.OptInVariable)
            || !AlpacaPaperSandbox.IsOptedIn(AlpacaPaperSandbox.MarketDataOptInVariable))
            Skip = "Alpaca options market-data validation reaches the market-data host and is opt-in: set MERIDIAN_RUN_ALPACA_PAPER_SANDBOX=1, MERIDIAN_RUN_ALPACA_MARKET_DATA=1 and the documented paper credentials.";
    }
}
