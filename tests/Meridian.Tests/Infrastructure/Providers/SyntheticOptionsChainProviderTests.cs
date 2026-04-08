using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Synthetic;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class SyntheticOptionsChainProviderTests
{
    private static SyntheticOptionsChainProvider CreateProvider() =>
        new(new SyntheticMarketDataConfig(Enabled: true));

    // ------------------------------------------------------------------ //
    //  GetExpirationsAsync                                                 //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetExpirationsAsync_ReturnsAtLeastFourExpirations()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");

        expirations.Should().HaveCountGreaterThanOrEqualTo(4, "monthly expirations should cover at least 4 months");
    }

    [Fact]
    public async Task GetExpirationsAsync_ReturnsOnlyFridayDates()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("AAPL");

        expirations.Should().OnlyContain(
            d => d.DayOfWeek == DayOfWeek.Friday,
            "standard US equity option expirations fall on Fridays");
    }

    [Fact]
    public async Task GetExpirationsAsync_ReturnsSortedAscending()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("TSLA");

        expirations.Should().BeInAscendingOrder("expirations should be sorted from nearest to furthest");
    }

    [Fact]
    public async Task GetExpirationsAsync_AllExpirationsInFuture()
    {
        var provider = CreateProvider();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expirations = await provider.GetExpirationsAsync("SPY");

        expirations.Should().OnlyContain(d => d > today, "all returned expirations should be in the future");
    }

    // ------------------------------------------------------------------ //
    //  GetChainSnapshotAsync                                               //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetChainSnapshotAsync_ReturnsNonNullSnapshot()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        snapshot.Should().NotBeNull("a snapshot must be returned for a known symbol");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_SnapshotHasBothCallsAndPuts()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        snapshot!.Calls.Should().NotBeEmpty("call options must be present");
        snapshot.Puts.Should().NotBeEmpty("put options must be present");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_CallPricesDecreaseAsStrikeIncreases()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        var calls = snapshot!.Calls.OrderBy(c => c.Contract.Strike).ToList();
        for (var i = 1; i < calls.Count; i++)
        {
            var price = calls[i].MidPrice ?? calls[i].AskPrice;
            var prevPrice = calls[i - 1].MidPrice ?? calls[i - 1].AskPrice;
            price.Should().BeLessThanOrEqualTo(prevPrice,
                "ITM calls have higher mid prices than OTM calls");
        }
    }

    [Fact]
    public async Task GetChainSnapshotAsync_PutPricesDecreaseAsStrikeDecreases()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        var puts = snapshot!.Puts.OrderByDescending(p => p.Contract.Strike).ToList();
        for (var i = 1; i < puts.Count; i++)
        {
            var price = puts[i].MidPrice ?? puts[i].AskPrice;
            var prevPrice = puts[i - 1].MidPrice ?? puts[i - 1].AskPrice;
            price.Should().BeLessThanOrEqualTo(prevPrice,
                "ITM puts (high strike) have higher mid prices than OTM puts (low strike)");
        }
    }

    [Fact]
    public async Task GetChainSnapshotAsync_GreeksArePopulated()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("AAPL");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("AAPL", expiry);

        snapshot!.Calls.Should().OnlyContain(
            c => c.Delta.HasValue,
            "call greeks must include delta");
        snapshot.Puts.Should().OnlyContain(
            p => p.Delta.HasValue,
            "put greeks must include delta");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_CallDeltaBetweenZeroAndOne()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        snapshot!.Calls.Should().OnlyContain(
            c => c.Delta >= 0m && c.Delta <= 1m,
            "call delta must be in [0, 1]");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_PutDeltaBetweenNegativeOneAndZero()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        snapshot!.Puts.Should().OnlyContain(
            p => p.Delta >= -1m && p.Delta <= 0m,
            "put delta must be in [-1, 0]");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_StrikeRangeFiltersCorrectly()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var full = await provider.GetChainSnapshotAsync("SPY", expiry);
        var narrowed = await provider.GetChainSnapshotAsync("SPY", expiry, strikeRange: 3);

        narrowed!.Calls.Should().HaveCountLessThanOrEqualTo(
            full!.Calls.Count,
            "strikeRange=3 should return no more strikes than the full chain");

        narrowed.Calls.Count.Should().BeLessThanOrEqualTo(
            7, // 3 below ATM + ATM + 3 above ATM
            "strikeRange=3 should return at most 7 call strikes");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_ContractSpecMatchesExpiration()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        snapshot!.Calls.Should().OnlyContain(
            c => c.Contract.Expiration == expiry,
            "all calls must have the requested expiration date");
        snapshot.Puts.Should().OnlyContain(
            p => p.Contract.Expiration == expiry,
            "all puts must have the requested expiration date");
    }

    [Fact]
    public async Task GetChainSnapshotAsync_AllCallContractsHaveCallRight()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        snapshot!.Calls.Should().OnlyContain(
            c => c.Contract.Right == OptionRight.Call,
            "items in the Calls collection must have Right == Call");
        snapshot.Puts.Should().OnlyContain(
            p => p.Contract.Right == OptionRight.Put,
            "items in the Puts collection must have Right == Put");
    }

    // ------------------------------------------------------------------ //
    //  GetOptionQuoteAsync                                                 //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetOptionQuoteAsync_ReturnsQuote_ForArbitraryValidStrike()
    {
        // The synthetic provider generates Black-Scholes prices for any valid OptionContractSpec,
        // regardless of whether that strike appears in its pre-generated chain.
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();
        var arbitrarySpec = new OptionContractSpec("SPY", 999m, expiry, OptionRight.Call);

        var quote = await provider.GetOptionQuoteAsync(arbitrarySpec);

        quote.Should().NotBeNull("the synthetic provider can price any valid contract spec");
        quote!.Contract.Strike.Should().Be(999m);
    }

    [Fact]
    public async Task GetOptionQuoteAsync_ReturnsQuote_ForKnownContract()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();
        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);
        var firstCall = snapshot!.Calls.First();

        var quote = await provider.GetOptionQuoteAsync(firstCall.Contract);

        quote.Should().NotBeNull("a known contract spec should return a quote");
        quote!.Contract.Right.Should().Be(OptionRight.Call);
        quote.Contract.Expiration.Should().Be(expiry);
    }

    // ------------------------------------------------------------------ //
    //  Metadata                                                            //
    // ------------------------------------------------------------------ //

    [Fact]
    public void ProviderCapabilities_SupportsOptionsChain()
    {
        var provider = CreateProvider();
        provider.ProviderCapabilities.SupportsOptionsChain.Should().BeTrue();
    }

    [Fact]
    public void ProviderName_IsNonEmpty()
    {
        var provider = CreateProvider();
        provider.ProviderDisplayName.Should().NotBeNullOrWhiteSpace();
    }

    // ------------------------------------------------------------------ //
    //  Determinism                                                         //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetChainSnapshotAsync_IsDeterministic_SameInputSameOutput()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snap1 = await provider.GetChainSnapshotAsync("SPY", expiry);
        var snap2 = await provider.GetChainSnapshotAsync("SPY", expiry);

        snap1!.Calls.Count.Should().Be(snap2!.Calls.Count, "deterministic provider must return same strike count");
        snap1.Calls.First().MidPrice.Should().Be(snap2.Calls.First().MidPrice, "deterministic provider must return same mid price");
    }
}
