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
    public async Task Scenario_DuplicateVenueFillAfterHandoffOutage_ReplayResumesWithoutPortfolioDuplication()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var publisher = new FailOnceTradeEventPublisher();
        var accountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.New, filledQty: 0m, fillPrice: null)
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio,
            tradeEventPublisher: publisher);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10m,
            FundAccountId = accountId
        });
        var fill = BuildReport(
            result.OrderId,
            OrderStatus.Filled,
            ExecutionReportType.Fill,
            filledQty: 10m,
            fillPrice: 150m);

        await gateway.PublishAsync(fill);
        await WaitUntilAsync(() => publisher.PublishAttempts == 1,
            "the first publication attempt must reach the configured accounting handoff");
        await gateway.PublishAsync(fill);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var publishedReport = await oms.ExecutionReports.ReadAsync(readCts.Token);

        publisher.PublishAttempts.Should().Be(2);
        publisher.AcceptedEvents.Should().ContainSingle();
        publisher.AcceptedEvents.Single().FillId.Should().NotBeEmpty();
        publisher.AcceptedEvents.Single().FinancialAccountId.Should().Be(accountId.ToString("D"));
        publishedReport.FilledQuantity.Should().Be(10m);
        portfolio.Positions["AAPL"].Quantity.Should().Be(10L,
            "retry resumes after publication and must not reapply the portfolio side effect");
        portfolio.Cash.Should().Be(98_500m);
    }

    [Fact]
    public async Task Scenario_TerminalClientOrderIdReuse_UnscopedReplacementFillDoesNotInheritPriorFundAccount()
    {
        var publisher = new RecordingTradeEventPublisher();
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport(
                "pending",
                OrderStatus.Accepted,
                ExecutionReportType.New,
                filledQty: 0m,
                fillPrice: null)
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            tradeEventPublisher: publisher);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        const string reusedClientOrderId = "terminal-reuse-1";
        var firstFundAccountId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var first = await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = reusedClientOrderId,
            Symbol = "AAA",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 3m,
            FundAccountId = firstFundAccountId
        }, cts.Token);
        first.Success.Should().BeTrue();
        await gateway.PublishAsync(BuildReport(
            reusedClientOrderId,
            OrderStatus.Filled,
            ExecutionReportType.Fill,
            filledQty: 3m,
            fillPrice: 10m,
            symbol: "AAA"));
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 1,
            "the scoped order fill must reach the accounting publisher before its terminal id is reused");

        var replacement = await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = reusedClientOrderId,
            Symbol = "BBB",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 2m
        }, cts.Token);
        replacement.Success.Should().BeTrue(
            "a client order id may be reused only after the prior order is terminal");
        await gateway.PublishAsync(BuildReport(
            reusedClientOrderId,
            OrderStatus.Filled,
            ExecutionReportType.Fill,
            filledQty: 2m,
            fillPrice: 20m,
            symbol: "BBB"));
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 2,
            "the unscoped replacement fill must reach the accounting publisher");

        var published = publisher.AcceptedEvents.ToArray();
        published.Should().HaveCount(2);
        published[0].Symbol.Should().Be("AAA");
        published[0].FinancialAccountId.Should().Be(firstFundAccountId.ToString("D"),
            "the first fill must retain the exact account scope captured before id reuse");
        published[1].Symbol.Should().Be("BBB");
        published[1].FinancialAccountId.Should().BeNull(
            "an unscoped replacement must not inherit the prior terminal order's account map entry");
    }

    [Fact]
    public async Task AsyncFill_WhenPublisherCannotAccept_IsExposedAsDurableHandoffFailureAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-oms-handoff-tests", Guid.NewGuid().ToString("N"));
        var options = new TradeFillHandoffFailureStoreOptions(root, HandoffPostingScope);
        try
        {
            Guid fillId;
            await using (var failureStore = new AtomicTradeFillHandoffFailureStore(options))
            {
                var gateway = new StreamingGateway
                {
                    SubmitAck = BuildReport(
                        "pending",
                        OrderStatus.Accepted,
                        ExecutionReportType.New,
                        filledQty: 0m,
                        fillPrice: null)
                };
                using var oms = new OrderManagementSystem(
                    gateway,
                    NullLogger<OrderManagementSystem>.Instance,
                    tradeEventPublisher: new AlwaysFailTradeEventPublisher(),
                    tradeFillHandoffFailureStore: failureStore);
                var result = await oms.PlaceOrderAsync(new OrderRequest
                {
                    Symbol = "AAPL",
                    Side = OrderSide.Buy,
                    Type = OrderType.Market,
                    Quantity = 10m
                });

                await gateway.PublishAsync(BuildReport(
                    result.OrderId,
                    OrderStatus.Filled,
                    ExecutionReportType.Fill,
                    filledQty: 10m,
                    fillPrice: 150m));
                var failures = await WaitForHandoffFailuresAsync(oms, expected: 1);

                failures.Should().ContainSingle();
                failures[0].LastFailure.Should().Contain("primary accounting persistence unavailable");
                fillId = failures[0].TradeEvent.FillId;
                oms.GetOrder(result.OrderId)!.Status.Should().Be(OrderStatus.Filled);
            }

            await using var reopened = new AtomicTradeFillHandoffFailureStore(options);
            var recovered = await reopened.LoadPendingAsync();

            recovered.Should().ContainSingle(item => item.TradeEvent.FillId == fillId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scenario_AccountingHandoffDuringShutdown_DurableFallbackSurvivesRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-oms-handoff-tests", Guid.NewGuid().ToString("N"));
        var options = new TradeFillHandoffFailureStoreOptions(root, HandoffPostingScope);
        var publisher = new BlockingFailingTradeEventPublisher();
        try
        {
            Guid retainedFillId;
            await using (var failureStore = new AtomicTradeFillHandoffFailureStore(options))
            {
                var gateway = new StreamingGateway
                {
                    SubmitAck = BuildReport(
                        "pending",
                        OrderStatus.Accepted,
                        ExecutionReportType.New,
                        filledQty: 0m,
                        fillPrice: null)
                };
                var oms = new OrderManagementSystem(
                    gateway,
                    NullLogger<OrderManagementSystem>.Instance,
                    tradeEventPublisher: publisher,
                    tradeFillHandoffFailureStore: failureStore);
                try
                {
                    var result = await oms.PlaceOrderAsync(new OrderRequest
                    {
                        Symbol = "AAPL",
                        Side = OrderSide.Buy,
                        Type = OrderType.Market,
                        Quantity = 10m
                    });
                    await gateway.PublishAsync(BuildReport(
                        result.OrderId,
                        OrderStatus.Filled,
                        ExecutionReportType.Fill,
                        filledQty: 10m,
                        fillPrice: 150m));
                    await publisher.PublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                    var shutdown = oms.DisposeAsync().AsTask();
                    shutdown.IsCompleted.Should().BeFalse(
                        "OMS shutdown must await an in-flight accounting handoff before dependencies can be disposed");
                    publisher.Release();
                    await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

                    var retained = await failureStore.LoadPendingAsync();
                    retained.Should().ContainSingle(
                        "the failed primary handoff must finish writing its fallback during coordinated shutdown");
                    retainedFillId = retained[0].TradeEvent.FillId;
                }
                finally
                {
                    publisher.Release();
                    await oms.DisposeAsync();
                }
            }

            await using var reopened = new AtomicTradeFillHandoffFailureStore(options);
            var recovered = await reopened.LoadPendingAsync();
            recovered.Should().ContainSingle(item => item.TradeEvent.FillId == retainedFillId,
                "the shutdown-time accounting handoff must survive process restart");
        }
        finally
        {
            publisher.Release();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scenario_InFlightSubmitDuringShutdown_DurableFallbackSurvivesRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-oms-handoff-tests", Guid.NewGuid().ToString("N"));
        var options = new TradeFillHandoffFailureStoreOptions(root, HandoffPostingScope);
        var gateway = new BlockingSubmitFillGateway();
        try
        {
            Guid retainedFillId;
            await using (var failureStore = new AtomicTradeFillHandoffFailureStore(options))
            {
                var oms = new OrderManagementSystem(
                    gateway,
                    NullLogger<OrderManagementSystem>.Instance,
                    tradeEventPublisher: new AlwaysFailTradeEventPublisher(),
                    tradeFillHandoffFailureStore: failureStore);
                try
                {
                    var placement = oms.PlaceOrderAsync(new OrderRequest
                    {
                        Symbol = "AAPL",
                        Side = OrderSide.Buy,
                        Type = OrderType.Market,
                        Quantity = 10m
                    });
                    await gateway.SubmitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

                    var shutdown = oms.DisposeAsync().AsTask();
                    shutdown.IsCompleted.Should().BeFalse(
                        "shutdown must wait for a PlaceOrder operation admitted before disposal");

                    Func<Task> placeAfterShutdown = async () =>
                    {
                        await oms.PlaceOrderAsync(new OrderRequest
                        {
                            Symbol = "MSFT",
                            Side = OrderSide.Buy,
                            Type = OrderType.Market,
                            Quantity = 1m
                        });
                    };
                    await placeAfterShutdown.Should().ThrowAsync<ObjectDisposedException>();

                    gateway.Release();
                    var result = await placement.WaitAsync(TimeSpan.FromSeconds(5));
                    result.Success.Should().BeFalse();
                    result.ErrorMessage.Should().Contain("durably retained for restart replay");
                    await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

                    var retained = await failureStore.LoadPendingAsync();
                    retained.Should().ContainSingle(
                        "the admitted submit-time fill must reach durable fallback before OMS shutdown completes");
                    retainedFillId = retained[0].TradeEvent.FillId;
                }
                finally
                {
                    gateway.Release();
                    await oms.DisposeAsync();
                }
            }

            await using var reopened = new AtomicTradeFillHandoffFailureStore(options);
            var recovered = await reopened.LoadPendingAsync();
            recovered.Should().ContainSingle(item => item.TradeEvent.FillId == retainedFillId,
                "the submit-time accounting handoff must survive process restart");
        }
        finally
        {
            gateway.Release();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scenario_ExecutionBurstSaturatesSubscriber_DropsOldestObserverReportAndCountsIt()
    {
        var publisher = new RecordingTradeEventPublisher();
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport("pending", OrderStatus.Accepted, ExecutionReportType.New, filledQty: 0m, fillPrice: null)
        };
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            options: new OrderManagementSystemOptions { ExecutionChannelCapacity = 1 },
            tradeEventPublisher: publisher);
        var firstOrder = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAA",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });
        var secondOrder = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "BBB",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 2m
        });
        var first = BuildReport(
            firstOrder.OrderId,
            OrderStatus.Filled,
            ExecutionReportType.Fill,
            filledQty: 1m,
            fillPrice: 10m,
            symbol: "AAA");
        var second = BuildReport(
            secondOrder.OrderId,
            OrderStatus.Filled,
            ExecutionReportType.Fill,
            filledQty: 2m,
            fillPrice: 20m,
            symbol: "BBB");

        await gateway.PublishAsync(first);
        await WaitUntilAsync(
            () => oms.ExecutionReports.CanCount && oms.ExecutionReports.Count == 1,
            "the first fill must occupy the bounded channel");
        await gateway.PublishAsync(second);
        await WaitUntilAsync(() => publisher.AcceptedEvents.Count == 2,
            "the second fill must reach publication without waiting on the saturated observer channel");
        await WaitUntilAsync(() => oms.DroppedExecutionReports == 1,
            "the saturated observer channel must drop the oldest unread report");

        // The observer stream is lossy by design: the newest report survives, the accounting
        // publisher (the lossless path) received both fills, and the drop was counted.
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var survivingReport = await oms.ExecutionReports.ReadAsync(readCts.Token);

        survivingReport.Symbol.Should().Be("BBB",
            "DropOldest must evict the unread AAA report in favour of the newest fill");
        publisher.AcceptedEvents.Should().HaveCount(2,
            "the durable accounting handoff must remain lossless regardless of observer lag");
        oms.DroppedExecutionReports.Should().Be(1);
        oms.ExecutionReports.Count.Should().Be(0, "only the surviving report was readable");
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
