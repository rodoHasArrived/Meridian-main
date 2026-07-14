using System.Threading;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Application.Accounting;

/// <summary>
/// Tests the valuation-policy rigor added to the daily mark-to-market loop: the ASC 820
/// fair-value hierarchy, the price-source selection waterfall, and stale-price handling.
/// </summary>
public sealed class DailyValuationPolicyTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 07, 03, 21, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AsOfDate = new(2026, 07, 03);

    private static DailyPortfolioPricingPolicy PolicyWith(
        StalePricePolicy stalePricePolicy,
        FairValueLevel defaultLevel = FairValueLevel.Unclassified) =>
        new(
            fundId: "fund-alpha",
            policyId: "vp-1",
            policyName: "Daily fair value",
            valuationMethod: "market-close",
            approvedBy: "cfo",
            approvedAtUtc: AsOf.AddDays(-60),
            defaultFairValueLevel: defaultLevel,
            stalePricePolicy: stalePricePolicy);

    // -------------------------------------------------------------------------
    // Stale-price policy (pure)
    // -------------------------------------------------------------------------

    [Fact]
    public void StalePricePolicy_Disabled_TreatsEveryPriceAsFresh()
    {
        var assessment = StalePricePolicy.Disabled.Assess(new DateOnly(2020, 1, 1), AsOfDate);
        assessment.IsStale.Should().BeFalse();
    }

    [Fact]
    public void StalePricePolicy_WithinMaxAge_IsFresh_Inclusive()
    {
        var policy = StalePricePolicy.Of(3, StalePriceHandling.Block);
        policy.Assess(new DateOnly(2026, 6, 30), AsOfDate).IsStale.Should().BeFalse(); // exactly 3 days
    }

    [Fact]
    public void StalePricePolicy_BeyondMaxAge_IsStaleWithConfiguredHandling()
    {
        var policy = StalePricePolicy.Of(3, StalePriceHandling.Flag);
        var assessment = policy.Assess(new DateOnly(2026, 6, 28), AsOfDate); // 5 days old

        assessment.IsStale.Should().BeTrue();
        assessment.AgeDays.Should().Be(5);
        assessment.Handling.Should().Be(StalePriceHandling.Flag);
    }

    [Fact]
    public void StalePricePolicy_FuturePrice_IsFreshWithZeroAge()
    {
        var policy = StalePricePolicy.Of(3, StalePriceHandling.Block);
        var assessment = policy.Assess(new DateOnly(2026, 7, 10), AsOfDate);

        assessment.IsStale.Should().BeFalse();
        assessment.AgeDays.Should().Be(0);
    }

    [Fact]
    public void StalePricePolicy_NegativeMaxAge_Throws()
    {
        var act = () => StalePricePolicy.Of(-1, StalePriceHandling.Block).EnsureValid();
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -------------------------------------------------------------------------
    // Price-source waterfall
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Waterfall_ReturnsFirstTierThatPrices_StampingItsLevel()
    {
        var waterfall = new WaterfallMarkPriceSource(
        [
            new PriceSourceTier(new StubSource(null), FairValueLevel.Level1, "exchange-close"),
            new PriceSourceTier(new StubSource(new MarkPriceQuote(100m, "vendor", "ev")), FairValueLevel.Level2, "vendor-composite"),
        ]);

        var quote = await waterfall.GetMarkPriceAsync("AAPL", AsOfDate);

        quote.Should().NotBeNull();
        quote!.Price.Should().Be(100m);
        quote.Level.Should().Be(FairValueLevel.Level2); // stamped from the resolving tier
    }

    [Fact]
    public async Task Waterfall_PreservesSourceAssignedLevel()
    {
        var waterfall = new WaterfallMarkPriceSource(
        [
            new PriceSourceTier(
                new StubSource(new MarkPriceQuote(100m, "model", "ev", FairValueLevel.Level3)),
                FairValueLevel.Level1,
                "matrix-model"),
        ]);

        var quote = await waterfall.GetMarkPriceAsync("PRIVATECO", AsOfDate);

        quote!.Level.Should().Be(FairValueLevel.Level3); // the source classified it; the tier does not override
    }

    [Fact]
    public async Task Waterfall_AllTiersEmpty_ReturnsNull()
    {
        var waterfall = new WaterfallMarkPriceSource(
            [new PriceSourceTier(new StubSource(null), FairValueLevel.Level1, "exchange-close")]);

        (await waterfall.GetMarkPriceAsync("AAPL", AsOfDate)).Should().BeNull();
    }

    [Fact]
    public void Waterfall_EmptyTiers_Throws()
    {
        var act = () => new WaterfallMarkPriceSource([]);
        act.Should().Throw<ArgumentException>();
    }

    // -------------------------------------------------------------------------
    // Stale-price handling in the valuation run
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PrepareAsync_BlockPolicy_ExcludesStaleMarkAndReportsIt()
    {
        var prices = new DatedPriceSource()
            .Add("AAPL", 160m, new DateOnly(2026, 7, 3))  // fresh, moves +1,000
            .Add("MSFT", 190m, new DateOnly(2026, 6, 1)); // ~32 days stale
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            PolicyWith(StalePricePolicy.Of(5, StalePriceHandling.Block)),
            "2026-07", AsOf, "USD",
            [
                new MarkToMarketPosition("AAPL", 100m, 150m),
                new MarkToMarketPosition("MSFT", 50m, 200m),
            ],
            "ops", "daily"));

        run.StalePricedSymbols.Should().Equal("MSFT");
        run.Projection!.Lines.Should().ContainSingle(line => line.Symbol == "AAPL");
        run.Projection.Lines.Should().NotContain(line => line.Symbol == "MSFT");
    }

    [Fact]
    public async Task PrepareAsync_FlagPolicy_IncludesStaleMarkFlaggedAndReportsIt()
    {
        var prices = new DatedPriceSource().Add("MSFT", 190m, new DateOnly(2026, 6, 1));
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            PolicyWith(StalePricePolicy.Of(5, StalePriceHandling.Flag)),
            "2026-07", AsOf, "USD",
            [new MarkToMarketPosition("MSFT", 50m, 200m)],
            "ops", "daily"));

        run.StalePricedSymbols.Should().Equal("MSFT");
        run.Projection!.Lines.Should().ContainSingle(line => line.Symbol == "MSFT" && line.IsStalePriced);
    }

    [Fact]
    public async Task PrepareAsync_AllowPolicy_IncludesStaleMarkSilently()
    {
        var prices = new DatedPriceSource().Add("MSFT", 190m, new DateOnly(2026, 6, 1));
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            PolicyWith(StalePricePolicy.Of(5, StalePriceHandling.Allow)),
            "2026-07", AsOf, "USD",
            [new MarkToMarketPosition("MSFT", 50m, 200m)],
            "ops", "daily"));

        run.StalePricedSymbols.Should().BeEmpty();
        run.Projection!.Lines.Should().ContainSingle(line => line.Symbol == "MSFT" && !line.IsStalePriced);
    }

    [Fact]
    public async Task PrepareAsync_StampsFairValueLevelIntoLineAndDraftTags()
    {
        var prices = new DatedPriceSource().Add("PRIVATECO", 190m, new DateOnly(2026, 7, 3), FairValueLevel.Level3);
        var service = new DailyMarkToMarketService(prices);

        var run = await service.PrepareAsync(new DailyMarkToMarketRequest(
            PolicyWith(StalePricePolicy.Disabled),
            "2026-07", AsOf, "USD",
            [new MarkToMarketPosition("PRIVATECO", 50m, 200m)],
            "ops", "daily"));

        run.Projection!.Lines.Single().FairValueLevel.Should().Be(FairValueLevel.Level3);
        run.Approval!.Draft.Metadata.Tags!["valuation.fairValueLevels"].Should().Contain("Level3");
    }

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class StubSource(MarkPriceQuote? quote) : IMarkPriceSource
    {
        public Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(quote);
    }

    private sealed class DatedPriceSource : IMarkPriceSource
    {
        private readonly Dictionary<string, MarkPriceQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);

        public DatedPriceSource Add(string symbol, decimal price, DateOnly priceAsOf, FairValueLevel level = FairValueLevel.Level1)
        {
            _quotes[symbol] = new MarkPriceQuote(price, "test-source", $"evidence:{symbol}", level, priceAsOf);
            return this;
        }

        public Task<MarkPriceQuote?> GetMarkPriceAsync(string symbol, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(_quotes.TryGetValue(symbol, out var quote) ? quote : null);
    }
}
