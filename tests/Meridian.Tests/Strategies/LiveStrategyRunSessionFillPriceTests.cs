using System.Runtime.CompilerServices;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Live;
using Meridian.Execution.Services;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ExecutionSdk = Meridian.Execution.Sdk;

namespace Meridian.Tests.Strategies;

/// <summary>
/// The session books strategy fills only at the price the execution report states. It must not
/// substitute the last market price, the limit price, or zero: the portfolio ledger books nothing
/// for a report without a price, so an invented one makes strategy P&amp;L drift from the ledger.
/// </summary>
public sealed class LiveStrategyRunSessionFillPriceTests
{
    [Fact]
    public async Task FillReportWithoutPrice_BooksNoStrategyFill()
    {
        using var fixture = new Fixture(averageFillPrice: null);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        fixture.Strategy.OrdersPlaced.Should().Be(1);
        fixture.Strategy.Fills.Should().BeEmpty(
            "no price was reported, and neither the last trade (100) nor the limit (99) is the execution price");
    }

    [Fact]
    public async Task FillReportWithPrice_BooksTheReportedPrice()
    {
        using var fixture = new Fixture(averageFillPrice: 98.75m);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Succeeded);
        fixture.Strategy.Fills.Should().ContainSingle().Which.FillPrice.Should().Be(98.75m);
    }

    [Fact]
    public async Task FillReportWithNonPositivePrice_FailsTheRunClosed()
    {
        using var fixture = new Fixture(averageFillPrice: 0m);

        var outcome = await fixture.Session.ExecuteAsync(CancellationToken.None);

        outcome!.State.Should().Be(OperationTerminalState.Failed);
        fixture.Strategy.Fills.Should().BeEmpty();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"session-fill-price-{Guid.NewGuid():N}");

        public OrderingStrategy Strategy { get; } = new();
        public LiveStrategyRunSession Session { get; }

        public Fixture(decimal? averageFillPrice)
        {
            var run = StrategyRunEntry.Start("ordering-strategy", "Ordering strategy", RunType.Paper);
            var repository = new StrategyRunStore(new FileOperationalCaseHistoryStore(_dataRoot));
            var gateway = new Mock<IOrderGateway>();
            gateway.SetupGet(item => item.BrokerName).Returns("Paper");
            var context = new LiveStrategyExecutionContext(gateway.Object, new LiveMarketDataCache(),
                new PaperTradingPortfolio(100_000m), new HashSet<string> { "AAPL" });

            var orders = new Mock<ExecutionSdk.IOrderManager>();
            orders.Setup(manager => manager.PlaceOrderAsync(It.IsAny<ExecutionSdk.OrderRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ExecutionSdk.OrderRequest request, CancellationToken _) => new ExecutionSdk.OrderResult
                {
                    Success = true,
                    OrderId = request.ClientOrderId!,
                    OrderState = new ExecutionSdk.OrderState
                    {
                        OrderId = request.ClientOrderId!,
                        Symbol = request.Symbol,
                        Side = request.Side,
                        Type = request.Type,
                        Quantity = request.Quantity,
                        FilledQuantity = request.Quantity,
                        Status = ExecutionSdk.OrderStatus.Filled,
                        AverageFillPrice = averageFillPrice,
                        LastUpdatedAt = DateTimeOffset.UtcNow
                    }
                });

            Session = new LiveStrategyRunSession(run, Strategy, context, new OneEventFeed(),
                orders.Object, repository, auditTrail: null,
                NullLogger.Instance, useSynchronousFillFallback: true, fillReportQueueCapacity: 2);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dataRoot))
                Directory.Delete(_dataRoot, recursive: true);
        }
    }

    private sealed class OrderingStrategy : BacktestStrategyBase, ILiveStrategy
    {
        public override string Name => "Ordering strategy";
        public string StrategyId => "ordering-strategy";
        public StrategyStatus Status { get; private set; } = StrategyStatus.Registered;
        public int OrdersPlaced { get; private set; }
        public List<FillEvent> Fills { get; } = new();

        public override void OnTrade(Trade trade, IBacktestContext ctx)
        {
            if (OrdersPlaced++ == 0)
                ctx.PlaceLimitOrder("AAPL", 10, 99m);
        }

        public override void OnOrderFill(FillEvent fill, IBacktestContext ctx) => Fills.Add(fill);

        public Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
        {
            Status = StrategyStatus.Running;
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default)
        {
            Status = StrategyStatus.Stopped;
            return Task.CompletedTask;
        }
    }

    private sealed class OneEventFeed : ILiveMarketEventFeed
    {
        public async IAsyncEnumerable<LiveMarketEvent> SubscribeAsync(IReadOnlyCollection<string> symbols,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var timestamp = DateTimeOffset.UtcNow;
            yield return new LiveMarketEvent(timestamp, "AAPL", new Trade(timestamp, "AAPL", 100m, 1, AggressorSide.Buy, 1));
            await Task.CompletedTask;
        }
    }
}
