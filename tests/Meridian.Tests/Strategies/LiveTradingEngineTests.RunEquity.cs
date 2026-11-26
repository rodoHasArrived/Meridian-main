using FluentAssertions;
using Meridian.Execution.Adapters;
using Meridian.Execution.Services;
using Meridian.Strategies.Live;
using Meridian.Strategies.Live.Strategies;
using Meridian.Strategies.Storage;
using Meridian.Storage.Operations;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

public sealed partial class LiveTradingEngineTests
{
    [Fact]
    public async Task PaperRun_EquityComesFromItsOwnFills_NotTheSharedPortfolio()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-run-equity", Guid.NewGuid().ToString("N"));
        try
        {
            // The shared portfolio already holds MSFT, bought before the run.
            var shared = new PaperTradingPortfolio(100_000m);
            shared.ApplyFill(new ExecutionSdk.ExecutionReport
            {
                OrderId = "pre-run",
                ClientOrderId = "pre-run",
                Symbol = "MSFT",
                Side = ExecutionSdk.OrderSide.Buy,
                OrderQuantity = 100m,
                FilledQuantity = 100m,
                FillPrice = 50m,
                OrderStatus = ExecutionSdk.OrderStatus.Filled,
                ReportType = ExecutionSdk.ExecutionReportType.Fill,
                Timestamp = DateTimeOffset.UtcNow
            });
            shared.UpdateMarketPrice("MSFT", 50m);
            var openingValue = shared.PortfolioValue;

            var repository = new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(root, "runs")));
            var hub = new Meridian.Execution.Live.LiveMarketEventHub();
            var cache = new LiveMarketDataCache();
            var orders = new RecordingOrderManager(100m);
            await using var engine = CreateEngine(repository, hub, cache, orders, portfolio: shared);
            var run = CreatePaperRun(BuyAndHoldLiveStrategy.CatalogId,
                new Dictionary<string, string> { ["symbols"] = "AAPL", ["quantity"] = "2" });
            await repository.RecordRunAsync(run);
            var launch = await engine.TryLaunchAsync(run);
            launch.Launched.Should().BeTrue(launch.Reason);
            await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () => orders.Requests.Count >= 1);

            // The run's AAPL rises 10; the pre-existing MSFT holding, which is not the run's, rises 30.
            var sequence = 0L;
            for (var i = 0; i < 3; i++)
            {
                var trade = new Meridian.Contracts.Domain.Models.Trade(DateTimeOffset.UtcNow, "AAPL", 110m, Size: 100,
                    Aggressor: Meridian.Contracts.Domain.Enums.AggressorSide.Buy, SequenceNumber: ++sequence);
                cache.RecordTrade("AAPL", trade);
            }
            shared.UpdateMarketPrice("MSFT", 80m);
            (await engine.StopRunAsync(run.RunId)).Should().BeTrue();

            var metrics = (await repository.GetRunByIdAsync(run.RunId))!.Metrics!.Metrics;
            metrics.InitialCapital.Should().Be(openingValue);
            metrics.NetPnl.Should().Be(2m * (110m - 100m), "only the run's own AAPL fill moved its equity");
            metrics.FinalEquity.Should().Be(openingValue + 20m);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, 1)] // first activation: the run book is flat, so the strategy enters
    [InlineData(true, 0)]  // recovered after a host restart: the shared portfolio shows the holding
    public async Task RecoveredRun_KeepsTheSharedPortfolioView_SoItDoesNotBuyWhatItAlreadyHolds(
        bool recovered, int expectedOrders)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-run-recovery", Guid.NewGuid().ToString("N"));
        try
        {
            // The shared portfolio holds AAPL. For a recovered run, that is the run's own
            // position from before the restart; its fills are not retained, so only the shared
            // portfolio can show it.
            var shared = new PaperTradingPortfolio(100_000m);
            shared.ApplyFill(new ExecutionSdk.ExecutionReport
            {
                OrderId = "before-restart",
                ClientOrderId = "before-restart",
                Symbol = "AAPL",
                Side = ExecutionSdk.OrderSide.Buy,
                OrderQuantity = 10m,
                FilledQuantity = 10m,
                FillPrice = 100m,
                OrderStatus = ExecutionSdk.OrderStatus.Filled,
                ReportType = ExecutionSdk.ExecutionReportType.Fill,
                Timestamp = DateTimeOffset.UtcNow
            });

            var strategy = new EnterWhenFlatStrategy();
            var catalog = new LiveStrategyCatalog().Register(strategy.StrategyId, _ => strategy);
            var repository = new StrategyRunStore(new FileOperationalCaseHistoryStore(Path.Combine(root, "runs")));
            var hub = new Meridian.Execution.Live.LiveMarketEventHub();
            var cache = new LiveMarketDataCache();
            var orders = new RecordingOrderManager(100m);
            await using var engine = CreateEngine(repository, hub, cache, orders, catalog, portfolio: shared);
            var run = CreatePaperRun(strategy.StrategyId, new Dictionary<string, string> { ["symbols"] = "AAPL" });
            if (recovered)
                run = run.Activated().Suspended();
            await repository.RecordRunAsync(run);

            var launch = await engine.TryLaunchAsync(run);
            launch.Launched.Should().BeTrue(launch.Reason);
            await PublishTradesUntilAsync(hub, cache, "AAPL", 100m, () => strategy.TradesSeen >= 3);

            orders.Requests.Should().HaveCount(expectedOrders);
            (await engine.StopRunAsync(run.RunId)).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Buys one lot of a symbol only while the portfolio it sees holds none.</summary>
    private sealed class EnterWhenFlatStrategy : LiveStrategyBase
    {
        private int _tradesSeen;

        public override string StrategyId => "enter-when-flat";
        public override string Name => "Enter when flat";
        public int TradesSeen => Volatile.Read(ref _tradesSeen);

        public override void OnTrade(Meridian.Contracts.Domain.Models.Trade trade, Meridian.Backtesting.Sdk.IBacktestContext ctx)
        {
            if (!ctx.Positions.TryGetValue(trade.Symbol, out var held) || held.Quantity == 0)
                ctx.PlaceMarketOrder(trade.Symbol, 10);
            Interlocked.Increment(ref _tradesSeen);
        }
    }
}
