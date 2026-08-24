using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Booking-side percent-of-par scaling for fixed-income fills. Pre-trade risk has always
/// measured a face-value order at <c>quantity × price / 100</c>; these tests pin that the
/// paper book and the accounting handoff move the same dollars the risk gate approved —
/// 100,000 face at 101.25 books $101,250, not $10,125,000.
/// </summary>
public sealed class FixedIncomeFillBookingTests
{
    [Fact]
    public void TradeExecutedEvent_FaceValueFill_ScalesGrossValueToParFraction()
    {
        var evt = new TradeExecutedEvent(
            FillId: Guid.NewGuid(),
            OrderId: "bond-1",
            Symbol: "912797AB1",
            Side: OrderSide.Buy,
            FilledQuantity: 100_000m,
            FillPrice: 101.25m,
            Commission: 0m,
            RealizedPnl: 0m,
            NewCash: 0m,
            OccurredAt: DateTimeOffset.UtcNow,
            FinancialAccountId: null,
            UsesFaceValuePercentageOfPar: true);

        evt.GrossValue.Should().Be(101_250m);
    }

    [Fact]
    public void TradeExecutedEvent_WithoutFaceValueMarker_KeepsRawQuantityTimesPrice()
    {
        var evt = new TradeExecutedEvent(
            Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            100m, 150m, 0m, 0m, 85_000m, DateTimeOffset.UtcNow);

        evt.UsesFaceValuePercentageOfPar.Should().BeFalse();
        evt.GrossValue.Should().Be(15_000m);
    }

    [Fact]
    public void ApplyFill_FaceValueBuy_BooksParScaledCashAndCostBasis()
    {
        var portfolio = new PaperTradingPortfolio(200_000m);

        portfolio.ApplyFill(
            FillReport("912797AB1", OrderSide.Buy, quantity: 100_000m, price: 101.25m),
            ownerAccountId: null,
            usesFaceValuePercentageOfPar: true);

        portfolio.Cash.Should().Be(200_000m - 101_250m,
            "a bond fill must charge the par-scaled notional, not face × quoted price");
        var position = portfolio.Positions["912797AB1"];
        position.Quantity.Should().Be(100_000L, "position quantity stays in face-value units");
        position.AverageCostBasis.Should().Be(1.0125m,
            "cost basis is held in dollars per unit of face once the clean price is scaled");
    }

    [Fact]
    public void ApplyFill_FaceValueRoundTrip_RealisesParScaledPnl()
    {
        var portfolio = new PaperTradingPortfolio(200_000m);

        portfolio.ApplyFill(
            FillReport("912797AB1", OrderSide.Buy, quantity: 100_000m, price: 101.25m),
            ownerAccountId: null,
            usesFaceValuePercentageOfPar: true);
        portfolio.ApplyFill(
            FillReport("912797AB1", OrderSide.Sell, quantity: 100_000m, price: 102.25m),
            ownerAccountId: null,
            usesFaceValuePercentageOfPar: true);

        portfolio.RealisedPnl.Should().Be(1_000m,
            "a one-point move on 100,000 face is $1,000, not $100,000");
        portfolio.Cash.Should().Be(201_000m);
    }

    [Fact]
    public async Task PlaceOrderAsync_FaceValueOrder_PublishesParScaledAccountingEvent()
    {
        var portfolio = new PaperTradingPortfolio(200_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            new FaceValuePaperGateway(),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "912797AB1",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 100_000m,
            LimitPrice = 101.25m,
            ClientOrderId = "FACE-VALUE-FILL-1",
            Metadata = new Dictionary<string, string> { ["asset_class"] = "treasury" }
        });

        result.Success.Should().BeTrue();
        portfolio.Cash.Should().Be(200_000m - 101_250m,
            "the paper book must move the notional the pre-trade rails measured");

        var tradeEvent = publisher.AcceptedEvents.Should().ContainSingle().Subject;
        tradeEvent.UsesFaceValuePercentageOfPar.Should().BeTrue(
            "the gateway-resolved sizing semantics must reach accounting consumers");
        tradeEvent.GrossValue.Should().Be(101_250m);
        tradeEvent.NewCash.Should().Be(200_000m - 101_250m);
    }

    [Fact]
    public async Task PlaceOrderAsync_EquityOrderOnFaceValueGateway_KeepsRawBooking()
    {
        var portfolio = new PaperTradingPortfolio(200_000m);
        var publisher = new RecordingTradeEventPublisher();
        using var oms = new OrderManagementSystem(
            new FaceValuePaperGateway(),
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m,
            ClientOrderId = "EQUITY-FILL-1"
        });

        result.Success.Should().BeTrue();
        portfolio.Cash.Should().Be(198_500m);
        var tradeEvent = publisher.AcceptedEvents.Should().ContainSingle().Subject;
        tradeEvent.UsesFaceValuePercentageOfPar.Should().BeFalse();
        tradeEvent.GrossValue.Should().Be(1_500m);
    }

    private static ExecutionReport FillReport(
        string symbol,
        OrderSide side,
        decimal quantity,
        decimal price) => new()
    {
        OrderId = $"{symbol}-{side}",
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = side,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = quantity,
        FilledQuantity = quantity,
        FillPrice = price,
        Timestamp = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Paper gateway that fills at the limit price and, mirroring Alpaca, routes treasury
    /// and corporate orders as face value priced at a percentage of par.
    /// </summary>
    private sealed class FaceValuePaperGateway
        : IExecutionGateway, IExecutionGatewayModeProvider, IFaceValueOrderSizingGateway
    {
        public string GatewayId => "paper";

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public bool IsConnected => true;

        public bool UsesFaceValuePercentageOfPar(OrderRequest request) =>
            request.Metadata is not null
            && request.Metadata.TryGetValue("asset_class", out var assetClass)
            && assetClass is "treasury" or "corporate";

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionReport
            {
                OrderId = request.ClientOrderId ?? "paper-1",
                ClientOrderId = request.ClientOrderId,
                ReportType = ExecutionReportType.Fill,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Filled,
                OrderQuantity = request.Quantity,
                FilledQuantity = request.Quantity,
                FillPrice = request.LimitPrice,
                Timestamp = DateTimeOffset.UtcNow
            });

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Cancelled,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Cancelled,
                Timestamp = DateTimeOffset.UtcNow
            });

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Modified,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Accepted,
                Timestamp = DateTimeOffset.UtcNow
            });

        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class RecordingTradeEventPublisher : ITradeEventPublisher
    {
        public ConcurrentQueue<TradeExecutedEvent> AcceptedEvents { get; } = new();

        public void Publish(TradeExecutedEvent tradeEvent) => AcceptedEvents.Enqueue(tradeEvent);
    }
}
