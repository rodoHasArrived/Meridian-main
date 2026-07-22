using System.Collections.Concurrent;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Events;
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
/// and the stream must not be double-applied. It also guards venue-replay and
/// subscriber-saturation failure modes in the fill-to-accounting handoff.
/// </summary>
public sealed class OrderManagementSystemReportStreamTests
{
    private const string HandoffPostingScope = "book-a/period-open";

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

    [Fact]
    public async Task CumulativePartialFills_ApplyOnlyTheIncrementToPortfolio()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.New, filledQty: 0m, fillPrice: null)
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10,
            LimitPrice = 150m
        });
        result.Success.Should().BeTrue();

        // Gateways report cumulative filled quantities: 5 filled, then 10 filled in total.
        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.PartiallyFilled, ExecutionReportType.PartialFill, filledQty: 5m, fillPrice: 150m));
        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 10m, fillPrice: 150m));

        await WaitUntilAsync(() => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Filled,
            "the completion report must reach tracked order state");

        portfolio.Positions["AAPL"].Quantity.Should().Be(10,
            because: "cumulative reports must apply as increments (5 + 5), never summed (5 + 10)");
        portfolio.Cash.Should().Be(100_000m - 1_500m);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await oms.ExecutionReports.ReadAsync(readCts.Token);
        var second = await oms.ExecutionReports.ReadAsync(readCts.Token);
        first.FilledQuantity.Should().Be(5m);
        second.FilledQuantity.Should().Be(5m,
            because: "published fills must carry the increment, not the cumulative quantity");
    }

    [Fact]
    public async Task OversizedStreamedFill_IsCappedToRemainingOrderQuantity()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.New, filledQty: 0m, fillPrice: null)
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10,
            LimitPrice = 150m
        });
        result.Success.Should().BeTrue();

        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 1_000m, fillPrice: 150m));

        await WaitUntilAsync(() => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Filled,
            "the oversized completion report still reaches tracked order state");

        var order = oms.GetOrder(result.OrderId)!;
        order.FilledQuantity.Should().Be(10m,
            because: "streamed cumulative fill quantities must be capped to the original order quantity");
        portfolio.Positions["AAPL"].Quantity.Should().Be(10m,
            because: "portfolio side effects may only apply the remaining authorized quantity");
        portfolio.Cash.Should().Be(100_000m - 1_500m);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = await oms.ExecutionReports.ReadAsync(readCts.Token);
        published.FilledQuantity.Should().Be(10m,
            because: "downstream consumers must receive the validated fill delta, not the oversized broker value");
    }

    [Fact]
    public async Task AcceptedQuantityIncrease_AllowsStreamedFillUpToAmendedQuantity()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.New, filledQty: 0m, fillPrice: null),
            ModifyAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.Modified, filledQty: 0m, fillPrice: null, orderQuantity: 30m)
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 25m,
            LimitPrice = 150m
        });
        result.Success.Should().BeTrue();

        var modification = await oms.ModifyOrderAsync(result.OrderId, new OrderModification { NewQuantity = 30m });
        modification.Success.Should().BeTrue();
        modification.OrderState!.Quantity.Should().Be(30m,
            because: "the accepted broker amendment establishes the authorized order quantity");

        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 30m, fillPrice: 150m, orderQuantity: 30m));

        await WaitUntilAsync(() => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Filled,
            "the streamed completion report must reach the amended tracked order");

        var order = oms.GetOrder(result.OrderId)!;
        order.Quantity.Should().Be(30m);
        order.FilledQuantity.Should().Be(30m,
            because: "fills must be capped to the broker-accepted amended quantity, not the original request");
        portfolio.Positions["AAPL"].Quantity.Should().Be(30m);
        portfolio.Cash.Should().Be(100_000m - 4_500m);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = await oms.ExecutionReports.ReadAsync(readCts.Token);
        published.FilledQuantity.Should().Be(30m,
            because: "downstream consumers must receive the full authorized amended fill increment");
    }

    [Fact]
    public async Task UnsolicitedAcceptedModification_CannotIncreaseAuthorizedQuantity()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.New, filledQty: 0m, fillPrice: null)
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        result.Success.Should().BeTrue();

        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.Accepted, ExecutionReportType.Modified, filledQty: 0m, fillPrice: null, orderQuantity: 1_000m));
        await WaitUntilAsync(() => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Accepted,
            "the unsolicited report still reaches the tracked order");

        oms.GetOrder(result.OrderId)!.Quantity.Should().Be(10m,
            because: "a gateway report without a local modification must not authorize a larger order");

        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 1_000m, fillPrice: 150m));
        await WaitUntilAsync(() => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Filled,
            "the oversized completion report reaches the tracked order");

        oms.GetOrder(result.OrderId)!.FilledQuantity.Should().Be(10m);
        portfolio.Positions["AAPL"].Quantity.Should().Be(10m,
            because: "the portfolio must only receive the originally authorized fill quantity");
        portfolio.Cash.Should().Be(100_000m - 1_500m);
    }

    [Fact]
    public async Task LateFillAfterTerminalOrder_DoesNotMutatePortfolioOrOrderState()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 10m, fillPrice: 150m)
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

        await gateway.PublishAsync(
            BuildReport(result.OrderId, OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 1_000m, fillPrice: 150m));
        await gateway.PublishAsync(
            BuildReport("external-2", OrderStatus.Filled, ExecutionReportType.Fill, filledQty: 1m, fillPrice: 10m, symbol: "ZZZ"));

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while ((await oms.ExecutionReports.ReadAsync(readCts.Token)).OrderId != "external-2")
        {
        }

        var order = oms.GetOrder(result.OrderId)!;
        order.Status.Should().Be(OrderStatus.Filled);
        order.FilledQuantity.Should().Be(10m,
            because: "late reports for terminal orders must not resize completed orders");
        portfolio.Positions["AAPL"].Quantity.Should().Be(10m,
            because: "late reports for terminal orders must not apply additional portfolio fills");
        portfolio.Cash.Should().Be(100_000m - 1_500m);
    }

    private static ExecutionReport BuildReport(
        string orderId,
        OrderStatus status,
        ExecutionReportType reportType,
        decimal filledQty,
        decimal? fillPrice,
        string symbol = "AAPL",
        decimal? orderQuantity = null) =>
        new()
        {
            OrderId = orderId,
            ClientOrderId = orderId,
            ReportType = reportType,
            Symbol = symbol,
            Side = OrderSide.Buy,
            OrderStatus = status,
            OrderQuantity = orderQuantity ?? filledQty,
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

    private static async Task<IReadOnlyList<RetainedTradeFillHandoffFailure>> WaitForHandoffFailuresAsync(
        OrderManagementSystem oms,
        int expected)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var failures = await oms.GetAccountingHandoffFailuresAsync();
            if (failures.Count == expected)
                return failures;
            await Task.Delay(10);
        }

        return await oms.GetAccountingHandoffFailuresAsync();
    }

    /// <summary>
    /// Gateway double whose asynchronous report stream is driven by the test. Optionally
    /// replays the submit ack on the stream, mirroring <c>BaseBrokerageGateway</c>.
    /// </summary>
    private sealed class StreamingGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();

        public required ExecutionReport SubmitAck { get; set; }
        public ExecutionReport? ModifyAck { get; set; }
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

        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
        {
            if (ModifyAck is null)
                throw new NotSupportedException();

            return Task.FromResult(ModifyAck with
            {
                OrderId = orderId,
                ClientOrderId = orderId
            });
        }

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public ValueTask PublishAsync(ExecutionReport report) => _reports.Writer.WriteAsync(report);
    }

    private sealed class BlockingSubmitFillGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SubmitStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string GatewayId => "blocking-submit-test";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            SubmitStarted.TrySetResult();
            await _release.Task.WaitAsync(ct);
            var orderId = request.ClientOrderId ?? "blocking-submit";
            return BuildReport(
                orderId,
                OrderStatus.Filled,
                ExecutionReportType.Fill,
                filledQty: request.Quantity,
                fillPrice: 150m,
                symbol: request.Symbol);
        }

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ExecutionReport> ModifyOrderAsync(
            string orderId,
            OrderModification modification,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public void Release() => _release.TrySetResult();
    }

    private class RecordingTradeEventPublisher : ITradeEventPublisher
    {
        public ConcurrentQueue<TradeExecutedEvent> AcceptedEvents { get; } = new();

        public virtual void Publish(TradeExecutedEvent tradeEvent) => AcceptedEvents.Enqueue(tradeEvent);
    }

    private sealed class FailOnceTradeEventPublisher : RecordingTradeEventPublisher
    {
        private int _publishAttempts;

        public int PublishAttempts => Volatile.Read(ref _publishAttempts);

        public override void Publish(TradeExecutedEvent tradeEvent)
        {
            if (Interlocked.Increment(ref _publishAttempts) == 1)
                throw new InvalidOperationException("simulated durable handoff outage");

            base.Publish(tradeEvent);
        }
    }

    private sealed class AlwaysFailTradeEventPublisher : IScopedTradeEventPublisher
    {
        public string PostingScope => HandoffPostingScope;

        public void Publish(TradeExecutedEvent tradeEvent)
            => throw new IOException("primary accounting persistence unavailable");
    }

    private sealed class BlockingFailingTradeEventPublisher : IScopedTradeEventPublisher
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PublishStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string PostingScope => HandoffPostingScope;

        public void Publish(TradeExecutedEvent tradeEvent)
        {
            PublishStarted.TrySetResult();
            _release.Task.GetAwaiter().GetResult();
            throw new IOException("primary accounting persistence unavailable during shutdown");
        }

        public void Release() => _release.TrySetResult();
    }
}
