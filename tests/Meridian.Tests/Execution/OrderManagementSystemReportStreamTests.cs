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

        // Reading both published fills is the portfolio barrier: each increment is applied before
        // its report is published, so observing the second one means both have landed. Order
        // status is not a barrier - see WaitForPublishedFillAsync.
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await oms.ExecutionReports.ReadAsync(readCts.Token);
        var second = await oms.ExecutionReports.ReadAsync(readCts.Token);
        first.FilledQuantity.Should().Be(5m);
        second.FilledQuantity.Should().Be(5m,
            because: "published fills must carry the increment, not the cumulative quantity");

        portfolio.Positions["AAPL"].Quantity.Should().Be(10,
            because: "cumulative reports must apply as increments (5 + 5), never summed (5 + 10)");
        portfolio.Cash.Should().Be(100_000m - 1_500m);
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

        // The published fill is the portfolio barrier - see WaitForPublishedFillAsync.
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = await oms.ExecutionReports.ReadAsync(readCts.Token);
        published.FilledQuantity.Should().Be(10m,
            because: "downstream consumers must receive the validated fill delta, not the oversized broker value");

        portfolio.Positions["AAPL"].Quantity.Should().Be(10L,
            because: "portfolio side effects may only apply the remaining authorized quantity");
        portfolio.Cash.Should().Be(100_000m - 1_500m);
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

        // The tracked status flips before the fill reaches the portfolio, so wait on the
        // effect this test asserts rather than racing the accounting handoff.
        await WaitUntilAsync(
            () => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Filled &&
                portfolio.Positions.ContainsKey("AAPL"),
            "the streamed completion report must reach the amended tracked order and the portfolio");

        var order = oms.GetOrder(result.OrderId)!;
        order.Quantity.Should().Be(30m);
        order.FilledQuantity.Should().Be(30m,
            because: "fills must be capped to the broker-accepted amended quantity, not the original request");

        // The published fill is the portfolio barrier - see WaitForPublishedFillAsync.
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var published = await oms.ExecutionReports.ReadAsync(readCts.Token);
        published.FilledQuantity.Should().Be(30m,
            because: "downstream consumers must receive the full authorized amended fill increment");

        portfolio.Positions["AAPL"].Quantity.Should().Be(30L);
        portfolio.Cash.Should().Be(100_000m - 4_500m);
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
        // The tracked status flips before the fill reaches the portfolio, so wait on the
        // effect this test asserts rather than racing the accounting handoff.
        await WaitUntilAsync(
            () => oms.GetOrder(result.OrderId)!.Status == OrderStatus.Filled &&
                portfolio.Positions.ContainsKey("AAPL"),
            "the oversized completion report reaches the tracked order and the portfolio");

        oms.GetOrder(result.OrderId)!.FilledQuantity.Should().Be(10m);

        await WaitForPublishedFillAsync(oms, result.OrderId);

        portfolio.Positions["AAPL"].Quantity.Should().Be(10L,
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
        portfolio.Positions["AAPL"].Quantity.Should().Be(10L,
            because: "late reports for terminal orders must not apply additional portfolio fills");
        portfolio.Cash.Should().Be(100_000m - 1_500m);
    }

    [Fact]
    public async Task Scenario_BurstFills_MultipleLosslessSubscribersEachReceiveEveryFill()
    {
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport(
                "pending",
                OrderStatus.Filled,
                ExecutionReportType.Fill,
                filledQty: 1m,
                fillPrice: 100m)
        };
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance);
        await using var strategySubscriber = oms.SubscribeLosslessExecutionReports(capacity: 2);
        await using var auditSubscriber = oms.SubscribeLosslessExecutionReports(capacity: 2);

        var first = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });
        var second = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var strategyReports = await ReadReportsAsync(strategySubscriber.Reports, count: 2, timeout.Token);
        var auditReports = await ReadReportsAsync(auditSubscriber.Reports, count: 2, timeout.Token);

        strategyReports.Select(static report => report.OrderId).Should().Equal(first.OrderId, second.OrderId);
        auditReports.Select(static report => report.OrderId).Should().Equal(first.OrderId, second.OrderId);
    }

    [Fact]
    public async Task Scenario_SaturatedSubscriber_IsAccountedWithoutBlockingHealthySubscriber()
    {
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport(
                "pending",
                OrderStatus.Filled,
                ExecutionReportType.Fill,
                filledQty: 1m,
                fillPrice: 100m)
        };
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance);
        var accounted = new ConcurrentQueue<ExecutionReport>();
        await using var saturatedSubscriber = oms.SubscribeLosslessExecutionReports(
            capacity: 1,
            subscriberName: "saturated-test",
            undeliverableHandler: (report, _, _) =>
            {
                accounted.Enqueue(report);
                return ValueTask.CompletedTask;
            });
        await using var healthySubscriber = oms.SubscribeLosslessExecutionReports(capacity: 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var first = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });
        var healthyFirst = await healthySubscriber.Reports.ReadAsync(timeout.Token);
        healthyFirst.OrderId.Should().Be(first.OrderId);

        var second = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        }).WaitAsync(timeout.Token);

        var healthySecond = await healthySubscriber.Reports.ReadAsync(timeout.Token);
        healthySecond.OrderId.Should().Be(second.OrderId,
            "one saturated subscriber must not block delivery to every remaining subscriber");
        accounted.Should().ContainSingle(report => report.OrderId == second.OrderId,
            "the full subscriber must route the rejected admission through its explicit recovery seam");

        await saturatedSubscriber.DisposeAsync();
        accounted.Should().Contain(report => report.OrderId == first.OrderId,
            "disposing an abandoned subscriber must account for reports it accepted but never drained");
    }

    [Fact]
    public async Task Scenario_UnrecoverableSubscriberDelivery_StopsPumpAndClosesOrderAdmission()
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
            NullLogger<OrderManagementSystem>.Instance);
        await using var subscriber = oms.SubscribeLosslessExecutionReports(capacity: 1);
        var first = BuildReport(
            "unrecoverable-1",
            OrderStatus.Filled,
            ExecutionReportType.Fill,
            filledQty: 1m,
            fillPrice: 100m);
        var second = BuildReport(
            "unrecoverable-2",
            OrderStatus.Filled,
            ExecutionReportType.Fill,
            filledQty: 1m,
            fillPrice: 101m);

        Exception? shutdownFailure = null;
        try
        {
            await gateway.PublishAsync(first);
            await WaitForPublishedFillAsync(oms, first.OrderId);
            await gateway.PublishAsync(second);

            InvalidOperationException? admissionFailure = null;
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
            while (admissionFailure is null && DateTimeOffset.UtcNow < deadline)
            {
                try
                {
                    await oms.GetAccountingHandoffFailuresAsync();
                }
                catch (InvalidOperationException ex)
                    when (ex.InnerException is OrderManagementSystem.ExecutionReportDeliveryException)
                {
                    admissionFailure = ex;
                }

                if (admissionFailure is null)
                {
                    await Task.Delay(25);
                }
            }

            admissionFailure.Should().NotBeNull(
                "an async broker fill with neither subscriber delivery nor durable recovery must stop new OMS work");
            (await subscriber.Reports.ReadAsync()).OrderId.Should().Be(first.OrderId);
        }
        finally
        {
            while (subscriber.Reports.TryRead(out _))
            {
            }

            try
            {
                await oms.DisposeAsync();
            }
            catch (Exception ex)
            {
                shutdownFailure = ex;
            }
        }

        shutdownFailure.Should().BeOfType<OrderManagementSystem.ExecutionReportDeliveryException>();
    }

    [Fact]
    public async Task PaperSessionClaimFailure_DoesNotAcknowledgeFillAndReplayCompletesIt()
    {
        var store = new FailFirstFillClaimStore();
        await using var persistence = new PaperSessionPersistenceService(
            NullLogger<PaperSessionPersistenceService>.Instance,
            store);
        var session = await persistence.CreateSessionAsync(
            new CreatePaperSessionDto("session-retry-strategy", null, 10_000m));
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport(
                "session-retry-order",
                OrderStatus.Filled,
                ExecutionReportType.Fill,
                filledQty: 1m,
                fillPrice: 100m)
        };
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            sessionPersistence: persistence);
        await using var subscriber = oms.SubscribeLosslessExecutionReports(capacity: 1);
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            ClientOrderId = "session-retry-order",
            Metadata = new Dictionary<string, string> { ["sessionId"] = session.SessionId }
        };

        var firstAttempt = await oms.PlaceOrderAsync(request);

        firstAttempt.Success.Should().BeFalse(
            "the broker filled the order but the durable paper-session claim failed");
        firstAttempt.OrderState!.Status.Should().Be(OrderStatus.Filled);
        store.ClaimAttempts.Should().Be(1);
        subscriber.Reports.TryRead(out _).Should().BeFalse(
            "subscriber publication must remain behind the durable session claim");

        var replay = gateway.SubmitAck with
        {
            OrderId = request.ClientOrderId!,
            ClientOrderId = request.ClientOrderId,
            Symbol = request.Symbol
        };
        await gateway.PublishAsync(replay);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        (await subscriber.Reports.ReadAsync(timeout.Token)).OrderId.Should().Be(request.ClientOrderId);
        store.ClaimAttempts.Should().Be(2,
            "an unacknowledged FillProcessingProgress must retry the durable claim on provider replay");
        persistence.GetSession(session.SessionId)!.FillCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_InFlightFill_DrainsToRealSubscriptionBeforeCompletingIt()
    {
        var gateway = new BlockingSubmitFillGateway();
        var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance);
        await using var subscription = oms.SubscribeLosslessExecutionReports(capacity: 1);

        var placement = oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });
        await gateway.SubmitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var shutdown = oms.DisposeAsync().AsTask();
        shutdown.IsCompleted.Should().BeFalse(
            "shutdown must await the broker operation admitted before order admission closed");
        var reportRead = subscription.Reports.ReadAsync().AsTask();

        gateway.Release();

        var result = await placement.WaitAsync(TimeSpan.FromSeconds(5));
        var report = await reportRead.WaitAsync(TimeSpan.FromSeconds(5));
        await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

        report.OrderId.Should().Be(result.OrderId,
            "subscriber completion must follow publication of the admitted in-flight fill");
        await subscription.Reports.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Scenario_UnreadCompatibilityObserver_DropsOldestWithoutBlockingFillPath()
    {
        var gateway = new StreamingGateway
        {
            SubmitAck = BuildReport(
                "pending",
                OrderStatus.Filled,
                ExecutionReportType.Fill,
                filledQty: 1m,
                fillPrice: 100m)
        };
        await using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            options: new OrderManagementSystemOptions { ExecutionChannelCapacity = 1 });

        var first = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });
        var second = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue("the compatibility observer is never authoritative backpressure");
        oms.DroppedExecutionReports.Should().Be(1);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var retainedObserverReport = await oms.ExecutionReports.ReadAsync(timeout.Token);
        retainedObserverReport.OrderId.Should().Be(second.OrderId);
    }

    [Fact]
    public void Dispose_SingleThreadSynchronizationContext_CompletesWithoutDeadlock()
    {
        using var completed = new ManualResetEventSlim();
        Exception? disposalFailure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
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
                    NullLogger<OrderManagementSystem>.Instance);
                oms.Dispose();
            }
            catch (Exception ex)
            {
                disposalFailure = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "oms-sync-context-disposal-test"
        };

        thread.Start();

        completed.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
            "the synchronous compatibility bridge must not wait on a continuation posted to the blocked caller context");
        disposalFailure.Should().BeNull();
        thread.Join(TimeSpan.FromSeconds(1)).Should().BeTrue();
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

    private static async Task<IReadOnlyList<ExecutionReport>> ReadReportsAsync(
        ChannelReader<ExecutionReport> reader,
        int count,
        CancellationToken ct)
    {
        var reports = new List<ExecutionReport>(count);
        while (reports.Count < count)
        {
            reports.Add(await reader.ReadAsync(ct));
        }

        return reports;
    }

    /// <summary>
    /// Waits until the OMS has published the fill for <paramref name="orderId"/> on its observer
    /// stream, which is the only barrier these tests have for the portfolio side effect.
    /// <c>ProcessFillReportAsync</c> applies the fill to the portfolio and only then writes to the
    /// execution-report channel, so observing the report happens-after the portfolio mutation.
    /// Order status is not a barrier: the OMS mutates it before calling
    /// <c>ProcessFillReportAsync</c> at all, so a test that waits on <c>Status == Filled</c> and
    /// then reads <c>portfolio.Positions</c> is racing an unfinished fill.
    /// </summary>
    private static async Task WaitForPublishedFillAsync(OrderManagementSystem oms, string orderId)
    {
        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var report = await oms.ExecutionReports.ReadAsync(readCts.Token);
            if (report.OrderId == orderId)
                return;
        }
    }

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

    private sealed class FailFirstFillClaimStore : IPaperSessionStore
    {
        private readonly ConcurrentDictionary<string, PersistedSessionRecord> _sessions =
            new(StringComparer.Ordinal);
        private readonly object _fillSync = new();
        private PaperSessionFillRecord? _fill;
        private int _claimAttempts;

        public int ClaimAttempts => Volatile.Read(ref _claimAttempts);

        public Task SaveSessionMetadataAsync(
            PersistedSessionRecord record,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            _sessions[record.SessionId] = record;
            return Task.CompletedTask;
        }

        public Task AppendFillAsync(
            string sessionId,
            ExecutionReport fill,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<PaperSessionFillAppendResult> TryAppendFillAsync(
            string sessionId,
            PaperSessionFillRecord record,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _claimAttempts) == 1)
            {
                return Task.FromException<PaperSessionFillAppendResult>(
                    new IOException("simulated durable fill-claim outage"));
            }

            record.Validate();
            lock (_fillSync)
            {
                if (_fill is null)
                {
                    _fill = record;
                    return Task.FromResult(
                        new PaperSessionFillAppendResult(PaperSessionFillAppendStatus.Added));
                }

                return Task.FromResult(new PaperSessionFillAppendResult(
                    _fill.FillId == record.FillId
                    && string.Equals(_fill.CanonicalHash, record.CanonicalHash, StringComparison.Ordinal)
                        ? PaperSessionFillAppendStatus.ExistingSame
                        : PaperSessionFillAppendStatus.Conflict,
                    _fill.CanonicalHash));
            }
        }

        public Task MarkFillAppliedAsync(
            string sessionId,
            Guid fillId,
            string canonicalHash,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_fillSync)
            {
                if (_fill is null
                    || _fill.FillId != fillId
                    || !string.Equals(_fill.CanonicalHash, canonicalHash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Cannot acknowledge an unknown test fill.");
                }

                _fill = _fill with { IsApplied = true };
            }

            return Task.CompletedTask;
        }

        public Task AppendOrderUpdateAsync(
            string sessionId,
            OrderState order,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task SaveLedgerJournalAsync(
            string sessionId,
            IReadOnlyList<PersistedJournalEntryDto> entries,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PersistedSessionRecord>> LoadAllSessionsAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PersistedSessionRecord>>(_sessions.Values.ToArray());

        public Task<IReadOnlyList<ExecutionReport>> LoadFillsAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            lock (_fillSync)
            {
                return Task.FromResult<IReadOnlyList<ExecutionReport>>(
                    _fill is null ? [] : [_fill.Fill]);
            }
        }

        public Task<IReadOnlyList<PaperSessionFillRecord>> LoadFillRecordsAsync(
            string sessionId,
            CancellationToken ct = default)
        {
            lock (_fillSync)
            {
                return Task.FromResult<IReadOnlyList<PaperSessionFillRecord>>(
                    _fill is null ? [] : [_fill]);
            }
        }

        public Task<IReadOnlyList<OrderState>> LoadOrderHistoryAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrderState>>([]);

        public Task<IReadOnlyList<PersistedJournalEntryDto>> LoadLedgerJournalAsync(
            string sessionId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]);
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

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            // Intentionally do not execute posted continuations. A sync-over-async disposal
            // bridge that captures this context will deadlock and fail the bounded test above.
        }
    }
}
