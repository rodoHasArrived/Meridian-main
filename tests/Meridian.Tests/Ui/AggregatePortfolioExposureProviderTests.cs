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
