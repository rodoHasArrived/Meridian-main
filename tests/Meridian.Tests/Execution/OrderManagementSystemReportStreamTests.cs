using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// Tests for <see cref="OrderManagementSystem"/> consumption of the gateway's
/// asynchronous execution report stream: order state must reflect reports that
/// arrive after the synchronous submit ack, and fills replayed on both the ack
/// and the stream must not be double-applied.
/// </summary>
public sealed class OrderManagementSystemReportStreamTests
{
    [Fact]
    public async Task AsyncFillReport_UpdatesOrderState_AndPublishesFill()
    {
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.New, filledQty: 0m, fillPrice: null)
        };
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10,
            LimitPrice = 150m
        });
        result.Success.Should().BeTrue();
        oms.GetOrder(result.OrderId)!.Status.Should().Be(OrderStatus.Accepted);

        // A fill arrives later on the asynchronous report stream only.
        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 10m, fillPrice: 150m));

        await WaitUntilAsync(() => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Filled,
            "the OMS must apply execution reports received via the gateway stream");

        var order = oms.GetOrder(result.OrderId)!;
        order.FilledQuantity.Should().Be(10m);
        order.AverageFillPrice.Should().Be(150m);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = await oms.ExecutionReports.ReadAsync(readCts.Token);
        published.OrderId.Should().Be(result.OrderId,
            because: "stream fills must be forwarded to ExecutionReports consumers");
    }

    [Fact]
    public async Task FillReplayedOnAckAndStream_IsAppliedToPortfolioOnlyOnce()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 10m, fillPrice: 150m),
            PublishAckOnStream = true
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10
        });
        result.Success.Should().BeTrue();

        // Publish a distinct marker fill after the replayed ack: once the marker is
        // observed, the pump has necessarily already processed the replayed ack.
        await gateway.PublishAsync(
            BuildReport("external-1", OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 1m, fillPrice: 10m, symbol: "ZZZ"));

        var seen = new List<ExecutionReport>();
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var report = await oms.ExecutionReports.ReadAsync(readCts.Token);
            seen.Add(report);
            if (report.OrderId == "external-1")
                break;
        }

        seen.Should().HaveCount(2,
            because: "the ack replayed on the stream must be deduplicated, not re-published");
        portfolio.Positions["AAPL"].Quantity.Should().Be(10,
            because: "the fill must be applied to the portfolio exactly once");
        portfolio.Positions.Should().NotContainKey("ZZZ",
            because: "fills for orders this OMS never placed must not mutate the paper portfolio");
        portfolio.Cash.Should().Be(100_000m - 1_500m);
    }

    private static ExecutionReport BuildReport(
        string orderId,
        OrderStatus status,
        ExecutionReportType reportType,
        decimal filledQty,
        decimal? fillPrice,
        string symbol = "AAPL") =>
        new()
        {
            OrderId = orderId,
            ClientOrderId = orderId,
            ReportType = reportType,
            Symbol = symbol,
            Side = OrderSide.Buy,
            OrderStatus = status,
            OrderQuantity = filledQty,
            FilledQuantity = filledQty,
            FillPrice = fillPrice,
            Commission = 0m,
            Timestamp = DateTimeOffset.UtcNow,
        };

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(10);
        }

        condition().Should().BeTrue(because);
    }

    /// <summary>
    /// Gateway double whose asynchronous report stream is driven by the test. Optionally
    /// replays the submit ack on the stream, mirroring <c>BaseBrokerageGateway</c>.
    /// </summary>
    private sealed class StreamingGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();

        public required ExecutionReport SubmitAck { get; set; }
        public bool PublishAckOnStream { get; set; }

        public string GatewayId => "stream-test";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            var ack = SubmitAck with
            {
                OrderId = request.ClientOrderId ?? SubmitAck.OrderId,
                ClientOrderId = request.ClientOrderId ?? SubmitAck.ClientOrderId,
                Symbol = request.Symbol
            };

            if (PublishAckOnStream)
                await _reports.Writer.WriteAsync(ack, ct);

            return ack;
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public ValueTask PublishAsync(ExecutionReport report) => _reports.Writer.WriteAsync(report);
    }
}
