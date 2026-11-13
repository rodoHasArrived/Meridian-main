using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Backtesting.Sdk.Strategies;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Strategies;
using Moq;

namespace Meridian.Tests.Strategies;

public sealed class MovingAverageParityTests
{
    [Fact]
    public void ReplayAndPaperWrapper_ProduceIdenticalIntents_WithPartialFillsAndMixedFeeds()
    {
        var replay = new MovingAverageCrossoverStrategy(2, 3, 2);
        var paper = new BacktestStrategyLiveAdapter("reference", new MovingAverageCrossoverStrategy(2, 3, 2));
        Execute(replay).Should().Equal(Execute(paper)).And.Equal(2L, -2L);
    }

    private static IReadOnlyList<long> Execute(IBacktestStrategy strategy)
    {
        var now = DateTimeOffset.Parse("2026-11-10T15:00:00Z");
        var positions = new Dictionary<string, Position>();
        var orders = new List<(Guid Id, long Quantity)>();
        var context = new Mock<IBacktestContext>();
        context.SetupGet(ctx => ctx.Universe).Returns(new HashSet<string> { "SPY" });
        context.SetupGet(ctx => ctx.Positions).Returns(positions);
        context.SetupGet(ctx => ctx.CurrentTime).Returns(() => now);
        context.Setup(ctx => ctx.PlaceMarketOrder("SPY", It.IsAny<long>())).Returns((string _, long quantity) =>
        { var id = Guid.NewGuid(); orders.Add((id, quantity)); return id; });
        void Observe(decimal price)
        {
            now = now.AddMinutes(1);
            strategy.OnTrade(new Trade(now, "SPY", price, 1, AggressorSide.Unknown, 1), context.Object);
            strategy.OnBar(new HistoricalBar("SPY", DateOnly.FromDateTime(now.DateTime), price, price, price, price, 100), context.Object);
        }
        Observe(100);
        Observe(101);
        orders.Should().BeEmpty();
        Observe(102);
        orders.Should().ContainSingle();
        var entry = orders[0].Id;
        positions["SPY"] = new("SPY", 1, 102m, 0m, 0m);
        strategy.OnOrderFill(new(Guid.NewGuid(), entry, "SPY", 1, 102m, 0m, now), context.Object);
        // A reversal while the entry is only partly filled must not create an overlapping exit.
        Observe(90);
        orders.Should().ContainSingle();
        positions["SPY"] = new("SPY", 2, 102m, 0m, 0m);
        strategy.OnOrderFill(new(Guid.NewGuid(), entry, "SPY", 1, 102m, 0m, now), context.Object);
        Observe(89);
        orders.Should().HaveCount(2);
        // Repeated delivery of the same observation cannot produce another intent.
        strategy.OnBar(new HistoricalBar("SPY", DateOnly.FromDateTime(now.DateTime), 89, 89, 89, 89, 100), context.Object);
        orders.Should().HaveCount(2);
        return orders.Select(order => order.Quantity).ToArray();
    }

    [Fact]
    public void RejectedWorkingOrder_ReleasesPendingSignalWithoutRewritingWarmup()
    {
        var strategy = new MovingAverageCrossoverLiveStrategy("reference", 2, 3, 1, barBased: true);
        var context = new Mock<IBacktestContext>();
        context.SetupGet(ctx => ctx.Universe).Returns(new HashSet<string> { "SPY" });
        context.SetupGet(ctx => ctx.Positions).Returns(new Dictionary<string, Position>());
        var now = DateTimeOffset.Parse("2026-11-10T15:00:00Z");
        context.SetupGet(ctx => ctx.CurrentTime).Returns(() => now);
        var ids = new List<Guid>();
        context.Setup(ctx => ctx.PlaceMarketOrder("SPY", 1)).Returns(() => { var id = Guid.NewGuid(); ids.Add(id); return id; });
        for (var price = 100; price < 104; price++)
        {
            now = now.AddMinutes(1);
            strategy.OnBar(new HistoricalBar("SPY", new(2026, 11, 10), price, price, price, price, 100), context.Object);
        }
        ids.Should().ContainSingle();
        strategy.OnOrderTerminated(ids[0], LiveOrderOutcome.Rejected);
        now = now.AddMinutes(1);
        strategy.OnBar(new HistoricalBar("SPY", new(2026, 11, 10), 104, 104, 104, 104, 100), context.Object);
        ids.Should().HaveCount(2);
    }
}
