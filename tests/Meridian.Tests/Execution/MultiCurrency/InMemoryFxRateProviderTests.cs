using FluentAssertions;
using Meridian.Execution.MultiCurrency;

namespace Meridian.Tests.Execution.MultiCurrency;

/// <summary>
/// Tests for <see cref="InMemoryFxRateProvider"/> — the first concrete implementation of
/// <see cref="IFxRateProvider"/>, which unblocks cross-currency reconciliation and ledger
/// translation. Covers identity, direct, inverse, triangulated, and as-of resolution paths.
/// </summary>
public sealed class InMemoryFxRateProviderTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 5, 28, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetRateAsync_SameCurrency_ReturnsIdentityRate()
    {
        var provider = new InMemoryFxRateProvider([]);

        var rate = await provider.GetRateAsync("USD", "usd", AsOf);

        rate.Should().NotBeNull();
        rate!.Rate.Should().Be(1m);
        rate.BaseCurrency.Should().Be("USD");
        rate.QuoteCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task GetRateAsync_DirectPair_ReturnsSeededRate()
    {
        var provider = new InMemoryFxRateProvider([new FxRate("EUR", "USD", 1.085m, AsOf)]);

        var rate = await provider.GetRateAsync("EUR", "USD", AsOf);

        rate.Should().NotBeNull();
        rate!.Rate.Should().Be(1.085m);
    }

    [Fact]
    public async Task GetRateAsync_InversePair_InvertsSeededRate()
    {
        var provider = new InMemoryFxRateProvider([new FxRate("EUR", "USD", 1.25m, AsOf)]);

        var rate = await provider.GetRateAsync("USD", "EUR", AsOf);

        rate.Should().NotBeNull();
        rate!.Rate.Should().Be(1m / 1.25m);
        rate.BaseCurrency.Should().Be("USD");
        rate.QuoteCurrency.Should().Be("EUR");
    }

    [Fact]
    public async Task GetRateAsync_Triangulates_ThroughConfiguredPivot()
    {
        var provider = new InMemoryFxRateProvider(
            [
                new FxRate("EUR", "USD", 1.10m, AsOf),
                new FxRate("GBP", "USD", 1.30m, AsOf),
            ],
            pivotCurrency: "USD");

        // EUR -> USD (1.10) then USD -> GBP (1/1.30) => EUR/GBP
        var rate = await provider.GetRateAsync("EUR", "GBP", AsOf);

        rate.Should().NotBeNull();
        rate!.Rate.Should().BeApproximately(1.10m / 1.30m, 0.0000001m);
    }

    [Fact]
    public async Task GetRateAsync_WithoutPivot_ReturnsNullForUnrelatedPair()
    {
        var provider = new InMemoryFxRateProvider(
            [
                new FxRate("EUR", "USD", 1.10m, AsOf),
                new FxRate("GBP", "USD", 1.30m, AsOf),
            ]);

        var rate = await provider.GetRateAsync("EUR", "GBP", AsOf);

        rate.Should().BeNull();
    }

    [Fact]
    public async Task GetRateAsync_SelectsMostRecentQuoteAtOrBeforeAsOf()
    {
        var provider = new InMemoryFxRateProvider(
            [
                new FxRate("EUR", "USD", 1.05m, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
                new FxRate("EUR", "USD", 1.09m, new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)),
                new FxRate("EUR", "USD", 1.20m, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)),
            ]);

        var rate = await provider.GetRateAsync("EUR", "USD", AsOf);

        rate.Should().NotBeNull();
        rate!.Rate.Should().Be(1.09m);
    }

    [Fact]
    public async Task GetRateAsync_WhenAllQuotesAreFuture_ReturnsNull()
    {
        var provider = new InMemoryFxRateProvider(
            [
                new FxRate("EUR", "USD", 1.15m, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)),
                new FxRate("EUR", "USD", 1.20m, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),
            ]);

        // AsOf (2026-05-28) precedes every seeded quote, so no rate was known as of that instant.
        // Returning the earliest (future) quote would leak look-ahead information into a historical
        // valuation, so the provider reports no rate and GetRequiredRateAsync fails closed.
        var rate = await provider.GetRateAsync("EUR", "USD", AsOf);
        rate.Should().BeNull();

        var act = async () => await provider.GetRequiredRateAsync("EUR", "USD", AsOf);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetRateAsync_IgnoresNonPositiveSeededQuotes()
    {
        var provider = new InMemoryFxRateProvider(
            [
                new FxRate("EUR", "USD", 0m, AsOf),
                new FxRate("GBP", "USD", -1.30m, AsOf),
            ]);

        (await provider.GetRateAsync("EUR", "USD", AsOf)).Should().BeNull("a zero rate would convert every value to zero");
        (await provider.GetRateAsync("GBP", "USD", AsOf)).Should().BeNull("a negative rate would invert cash and valuation signs");
    }

    [Fact]
    public async Task GetRequiredRateAsync_WhenMissing_Throws()
    {
        var provider = new InMemoryFxRateProvider([]);

        var act = async () => await provider.GetRequiredRateAsync("EUR", "JPY", AsOf);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetRequiredRateAsync_WhenPresent_ReturnsRate()
    {
        var provider = new InMemoryFxRateProvider([new FxRate("EUR", "USD", 1.085m, AsOf)]);

        var rate = await provider.GetRequiredRateAsync("EUR", "USD", AsOf);

        rate.Rate.Should().Be(1.085m);
    }
}
