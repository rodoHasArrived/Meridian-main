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

        expirations.Should().HaveCountGreaterOrEqualTo(4, "monthly expirations should cover at least 4 months");
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
        var today = DateOnly.FromDateTime(DateTime.Today);
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
            calls[i].Mid.Should().BeLessOrEqualTo(calls[i - 1].Mid,
                "ITM calls have higher mid prices than OTM calls");
        }
    }

    [Fact]
    public async Task GetChainSnapshotAsync_PutPricesIncreaseAsStrikeDecreases()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();

        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);

        var puts = snapshot!.Puts.OrderByDescending(p => p.Contract.Strike).ToList();
        for (var i = 1; i < puts.Count; i++)
        {
            puts[i].Mid.Should().BeLessOrEqualTo(puts[i - 1].Mid,
                "ITM puts (low strike) have higher mid prices than OTM puts (high strike)");
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
            c => c.Greeks != null && c.Greeks.Delta.HasValue,
            "call greeks must include delta");
        snapshot.Puts.Should().OnlyContain(
            p => p.Greeks != null && p.Greeks.Delta.HasValue,
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
            c => c.Greeks!.Delta >= 0m && c.Greeks.Delta <= 1m,
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
            p => p.Greeks!.Delta >= -1m && p.Greeks.Delta <= 0m,
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

        narrowed!.Calls.Should().HaveCountLessOrEqualTo(
            full!.Calls.Count,
            "strikeRange=3 should return no more strikes than the full chain");

        narrowed.Calls.Count.Should().BeLessOrEqualTo(
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
    //  GetContractDetailsAsync                                             //
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetContractDetailsAsync_ReturnsNull_ForUnknownOccSymbol()
    {
        var provider = CreateProvider();

        var details = await provider.GetContractDetailsAsync("INVALID_CONTRACT_SYMBOL");

        details.Should().BeNull("an unrecognised OCC symbol should return null");
    }

    [Fact]
    public async Task GetContractDetailsAsync_ReturnsQuote_ForKnownContract()
    {
        var provider = CreateProvider();
        var expirations = await provider.GetExpirationsAsync("SPY");
        var expiry = expirations.First();
        var snapshot = await provider.GetChainSnapshotAsync("SPY", expiry);
        var firstCall = snapshot!.Calls.First();
        var occSymbol = firstCall.Contract.ToOccSymbol();

        var details = await provider.GetContractDetailsAsync(occSymbol);

        details.Should().NotBeNull("a known OCC symbol should return contract details");
        details!.Contract.Right.Should().Be(OptionRight.Call);
        details.Contract.Expiration.Should().Be(expiry);
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
        provider.ProviderName.Should().NotBeNullOrWhiteSpace();
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
        snap1.Calls.First().Mid.Should().Be(snap2.Calls.First().Mid, "deterministic provider must return same mid price");
    }
}
