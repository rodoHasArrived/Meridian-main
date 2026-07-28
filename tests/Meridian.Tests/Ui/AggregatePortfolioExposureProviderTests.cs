using FluentAssertions;
using Meridian.Execution.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Covers the exposure feed that turns <see cref="IAggregatePortfolioService"/> aggregated
/// positions into the snapshot the portfolio-aware risk rules consume.
/// </summary>
public sealed class AggregatePortfolioExposureProviderTests
{
    private static AggregatedPosition Position(
        string symbol,
        decimal longQty,
        decimal shortQty,
        decimal weightedAverageCost) => Position(
            symbol,
            new[] { (longQty, weightedAverageCost), (-shortQty, weightedAverageCost) }
                .Where(static lot => lot.Item1 != 0m)
                .ToArray());

    private static AggregatedPosition Position(
        string symbol,
        params (decimal Quantity, decimal CostBasis)[] lots)
    {
        var contributions = lots
            .Select((lot, index) => new RunPositionContribution(
                RunId: $"run-{index}",
                AccountId: $"acct-{index}",
                Quantity: lot.Quantity,
                CostBasis: lot.CostBasis,
                UnrealisedPnl: 0m))
            .ToArray();
        var totalQty = contributions.Sum(static c => c.Quantity);
        return new AggregatedPosition(
            Symbol: symbol,
            TotalQuantity: totalQty,
            LongQuantity: contributions.Where(static c => c.Quantity > 0).Sum(static c => c.Quantity),
            ShortQuantity: contributions.Where(static c => c.Quantity < 0).Sum(static c => Math.Abs(c.Quantity)),
            WeightedAverageCost: totalQty != 0m
                ? contributions.Sum(static c => c.Quantity * c.CostBasis) / totalQty
                : 0m,
            TotalUnrealisedPnl: 0m,
            Contributions: contributions);
    }

    [Fact]
    public void GetSnapshot_AggregatesGrossNetAndPerSymbolExposure()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns(
        [
            Position("AAPL", longQty: 100m, shortQty: 0m, weightedAverageCost: 200m),
            Position("MSFT", longQty: 0m, shortQty: 50m, weightedAverageCost: 100m)
        ]);
        var portfolioState = new Mock<IPortfolioState>();
        portfolioState.SetupGet(p => p.PortfolioValue).Returns(150_000m);

        var provider = new AggregatePortfolioExposureProvider(aggregate.Object, portfolioState.Object);
        var snapshot = provider.GetSnapshot();

        snapshot.GrossExposure.Should().Be(25_000m, "AAPL 100×200 long + MSFT 50×100 short");
        snapshot.NetExposure.Should().Be(15_000m, "20k long − 5k short");
        snapshot.PortfolioValue.Should().Be(150_000m);
        snapshot.GetSymbolExposure("aapl").GrossExposure.Should().Be(20_000m, "symbol lookup is case-insensitive");
        snapshot.GetSymbolExposure("AAPL").ReferencePrice.Should().Be(200m);
        snapshot.GetSymbolExposure("MSFT").NetQuantity.Should().Be(-50m);
        snapshot.GetSymbolExposure("ZZZZ").GrossExposure.Should().Be(0m, "unknown symbols report flat");
        snapshot.GetSymbolExposure("AAPL").NetNotional.Should().Be(20_000m);
        snapshot.GetSymbolExposure("MSFT").NetNotional.Should().Be(-5_000m);
    }

    [Fact]
    public void GetSnapshot_OffsettingLotsAcrossRuns_ReportsPositiveGrossExposure()
    {
        // Long 100 @ 100 and short 90 @ 200 in the same symbol: the netted weighted
        // average cost is meaningless (negative), but gross exposure is 10k + 18k = 28k.
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns(
        [
            Position("AAPL", (100m, 100m), (-90m, 200m))
        ]);

        var provider = new AggregatePortfolioExposureProvider(aggregate.Object);
        var snapshot = provider.GetSnapshot();

        snapshot.GrossExposure.Should().Be(28_000m, "each lot contributes its absolute quantity at its own cost");
        snapshot.GetSymbolExposure("AAPL").GrossExposure.Should().Be(28_000m);
        snapshot.GetSymbolExposure("AAPL").NetNotional.Should().Be(10_000m - 18_000m);
        snapshot.GetSymbolExposure("AAPL").ReferencePrice.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void GetSnapshot_WithoutPortfolioState_FallsBackToGrossExposure()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns(
        [
            Position("AAPL", longQty: 10m, shortQty: 0m, weightedAverageCost: 100m)
        ]);

        var provider = new AggregatePortfolioExposureProvider(aggregate.Object, portfolioState: null);
        var snapshot = provider.GetSnapshot();

        snapshot.PortfolioValue.Should().Be(1_000m, "portfolio value falls back to gross exposure so concentration stays defined");
    }

    [Fact]
    public void GetSnapshot_EmptyPortfolio_ReturnsZeroedSnapshot()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns([]);

        var provider = new AggregatePortfolioExposureProvider(aggregate.Object);
        var snapshot = provider.GetSnapshot();

        snapshot.GrossExposure.Should().Be(0m);
        snapshot.SymbolExposures.Should().BeEmpty();
    }

    [Fact]
    public void GetSnapshot_ReservesExposureForWorkingOrders()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns([]);

        // An accepted-but-unfilled limit buy: 100 x $600 = $60k of working exposure that
        // must be reserved, or a second identical order would also see a flat book.
        var orderManager = new Mock<Meridian.Execution.Sdk.IOrderManager>();
        orderManager.Setup(m => m.GetOpenOrders()).Returns(
        [
            new Meridian.Execution.Sdk.OrderState
            {
                OrderId = "working-1",
                Symbol = "AAPL",
                Side = Meridian.Execution.Sdk.OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Limit,
                Quantity = 100m,
                LimitPrice = 600m,
                Status = Meridian.Execution.Sdk.OrderStatus.Accepted,
                CreatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var provider = new AggregatePortfolioExposureProvider(
            aggregate.Object,
            orderManagerAccessor: () => orderManager.Object);
        var snapshot = provider.GetSnapshot();

        snapshot.GrossExposure.Should().Be(60_000m, "working orders reserve their projected exposure");
        snapshot.NetExposure.Should().Be(60_000m);
        snapshot.GetSymbolExposure("AAPL").GrossExposure.Should().Be(60_000m);
        snapshot.GetSymbolExposure("AAPL").ReferencePrice.Should().Be(600m);
    }

    [Fact]
    public void GetSnapshot_WorkingOrder_ReservesOnlyTheUnfilledRemainder()
    {
        // The filled 40 shares are already carried by the position below; only the
        // remaining 60 x $100 = $6k may be reserved again.
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns(
        [
            Position("AAPL", longQty: 40m, shortQty: 0m, weightedAverageCost: 100m)
        ]);

        var orderManager = new Mock<Meridian.Execution.Sdk.IOrderManager>();
        orderManager.Setup(m => m.GetOpenOrders()).Returns(
        [
            new Meridian.Execution.Sdk.OrderState
            {
                OrderId = "working-2",
                Symbol = "AAPL",
                Side = Meridian.Execution.Sdk.OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Limit,
                Quantity = 100m,
                FilledQuantity = 40m,
                LimitPrice = 100m,
                Status = Meridian.Execution.Sdk.OrderStatus.PartiallyFilled,
                CreatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var provider = new AggregatePortfolioExposureProvider(
            aggregate.Object,
            orderManagerAccessor: () => orderManager.Object);

        provider.GetSnapshot().GrossExposure.Should().Be(
            10_000m,
            "$4k filled position plus $6k unfilled remainder — the filled part is never double-counted");
    }

    [Fact]
    public void GetSnapshot_WorkingOrder_ReservesBrokerNativeRoutedNotional()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns([]);

        // Alpaca-style notional sizing: the gateway routes $500k and discards quantity,
        // so reserving quantity x price would hold back roughly one share.
        var orderManager = new Mock<Meridian.Execution.Sdk.IOrderManager>();
        orderManager.Setup(m => m.GetOpenOrders()).Returns(
        [
            new Meridian.Execution.Sdk.OrderState
            {
                OrderId = "notional-1",
                Symbol = "AAPL",
                Side = Meridian.Execution.Sdk.OrderSide.Buy,
                Type = Meridian.Execution.Sdk.OrderType.Market,
                Quantity = 1m,
                RoutedNotional = 500_000m,
                Status = Meridian.Execution.Sdk.OrderStatus.Accepted,
                CreatedAt = DateTimeOffset.UtcNow
            }
        ]);

        var provider = new AggregatePortfolioExposureProvider(
            aggregate.Object,
            orderManagerAccessor: () => orderManager.Object);

        provider.GetSnapshot().GrossExposure.Should().Be(
            500_000m,
            "the reserve must match the dollars the gateway actually routes");
    }

    [Fact]
    public void GetSnapshot_AttributesExposurePerAccount()
    {
        // Offsetting books across two accounts: the aggregate net says nothing about
        // whether an order in one of them adds or reduces risk, so the projection needs
        // per-account attribution.
        var longAccount = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var shortAccount = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns(
        [
            new AggregatedPosition(
                Symbol: "AAPL",
                TotalQuantity: 100m,
                LongQuantity: 1_000m,
                ShortQuantity: 900m,
                WeightedAverageCost: 100m,
                TotalUnrealisedPnl: 0m,
                Contributions:
                [
                    new RunPositionContribution("run-a", longAccount.ToString("D"), 1_000m, 100m, 0m),
                    new RunPositionContribution("run-b", shortAccount.ToString("D"), -900m, 100m, 0m)
                ])
        ]);

        var exposure = new AggregatePortfolioExposureProvider(aggregate.Object)
            .GetSnapshot()
            .GetSymbolExposure("AAPL");

        exposure.ResolveSignedExposureFor(longAccount).Should().Be(100_000m);
        exposure.ResolveSignedExposureFor(shortAccount).Should().Be(-90_000m);
        exposure.ResolveSignedExposureFor(null).Should().BeNull(
            "with several contributing accounts an unattributed order must fall back to the additive worst case");
    }

    [Fact]
    public void GetSnapshot_WithRegistry_SumsPortfolioValueAcrossRegisteredPortfolios()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns([]);

        // Positions aggregate across every registered portfolio, so the concentration
        // denominator must too — not just the host state's own value.
        var host = new Mock<IMultiAccountPortfolioState>();
        host.SetupGet(p => p.PortfolioValue).Returns(100_000m);
        var secondRun = new Mock<IMultiAccountPortfolioState>();
        secondRun.SetupGet(p => p.PortfolioValue).Returns(50_000m);

        var registry = new Meridian.Execution.Services.PortfolioRegistry();
        registry.Register("workstation-paper", host.Object);
        registry.Register("strategy-run", secondRun.Object);
        // The same instance under a second run id must not double-count.
        registry.Register("strategy-run-alias", secondRun.Object);

        var provider = new AggregatePortfolioExposureProvider(
            aggregate.Object,
            portfolioState: host.Object,
            registry: registry);

        provider.GetSnapshot().PortfolioValue.Should().Be(
            150_000m,
            "the denominator spans the same registry scope as the aggregated positions, counting each portfolio once");
    }

    [Fact]
    public void GetSnapshot_WithEmptyRegistry_FallsBackToHostPortfolioValue()
    {
        var aggregate = new Mock<IAggregatePortfolioService>();
        aggregate.Setup(a => a.GetAggregatedPositions(null)).Returns([]);
        var portfolioState = new Mock<IPortfolioState>();
        portfolioState.SetupGet(p => p.PortfolioValue).Returns(75_000m);

        var provider = new AggregatePortfolioExposureProvider(
            aggregate.Object,
            portfolioState: portfolioState.Object,
            registry: new Meridian.Execution.Services.PortfolioRegistry());

        provider.GetSnapshot().PortfolioValue.Should().Be(75_000m);
    }
}
