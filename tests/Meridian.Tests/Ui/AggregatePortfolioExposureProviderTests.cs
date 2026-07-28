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
        decimal weightedAverageCost) => new(
            Symbol: symbol,
            TotalQuantity: longQty - shortQty,
            LongQuantity: longQty,
            ShortQuantity: shortQty,
            WeightedAverageCost: weightedAverageCost,
            TotalUnrealisedPnl: 0m,
            Contributions: []);

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
}
