using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

// Disambiguate the two PaperTradingGateway types that exist in this project
using ExecutionGateway = Meridian.Execution.PaperTradingGateway;
using ExecutionPositionModel = Meridian.Execution.Models.ExecutionPosition;

namespace Meridian.Tests.Execution;

/// <summary>
/// Tests for <see cref="OrderManagementSystem"/>, focused on completed-order tracking
/// via <see cref="IOrderManager.GetCompletedOrders"/>.
/// </summary>
public sealed class OrderManagementSystemTests : IDisposable
{
    private static readonly Guid LiveFundAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ExecutionGateway _gateway;
    private readonly OrderManagementSystem _oms;

    public OrderManagementSystemTests()
    {
        // These tests exercise OMS behavior over a gateway that fills feed-less market
        // orders, so scaffold pricing is explicitly opted in.
        _gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true });
        _oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance);
    }

    public void Dispose()
    {
        _oms.Dispose();
    }

    // ---- GetCompletedOrders — no orders yet ----

    [Fact]
    public void GetCompletedOrders_WhenNoOrdersExist_ReturnsEmpty()
    {
        var completed = _oms.GetCompletedOrders();

        completed.Should().BeEmpty();
    }

    // ---- GetCompletedOrders — open orders are excluded ----

    [Fact]
    public async Task GetCompletedOrders_WhenOnlyOpenOrdersExist_ReturnsEmpty()
    {
        // Limit orders are accepted but NOT immediately filled by PaperTradingGateway,
        // so they remain in the open/accepted state.
        var request = new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10,
            LimitPrice = 150m
        };

        await _oms.PlaceOrderAsync(request);

        var completed = _oms.GetCompletedOrders();

        completed.Should().BeEmpty("an accepted (open) limit order must not appear in the completed feed");
    }

    // ---- GetCompletedOrders — filled orders are included ----

    [Fact]
    public async Task GetCompletedOrders_AfterMarketFill_ReturnsFilled()
    {
        // PaperTradingGateway fills market orders immediately.
        var request = new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5
        };

        var result = await _oms.PlaceOrderAsync(request);
        result.Success.Should().BeTrue();

        var completed = _oms.GetCompletedOrders();
        completed.Should().ContainSingle(o =>
            o.Symbol == "MSFT" && o.Status == OrderStatus.Filled,
            "an immediately-filled market order should appear in the completed feed");

        var open = _oms.GetOpenOrders();
        open.Should().NotContain(o => o.Symbol == "MSFT",
            "a filled order must not remain in the open order list");
    }

    [Fact]
    public async Task PlaceOrderAsync_WithPaperTradingPortfolio_AppliesFillToSharedPortfolio()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        using var oms = new OrderManagementSystem(
            _gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 4,
            LimitPrice = 25m
        });

        result.Success.Should().BeTrue();
        portfolio.Cash.Should().Be(99_900m);
        portfolio.Positions.Should().ContainKey("MSFT");

        var position = portfolio.Positions["MSFT"].Should().BeOfType<ExecutionPositionModel>().Subject;
        position.Quantity.Should().Be(4);
        position.AverageCostBasis.Should().Be(25m);
    }

    // ---- GetCompletedOrders — cancelled orders are included ----

    [Fact]
    public async Task GetCompletedOrders_AfterCancel_ReturnsCancelledOrder()
    {
        var request = new OrderRequest
        {
            Symbol = "TSLA",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 3,
            LimitPrice = 200m
        };

        var placeResult = await _oms.PlaceOrderAsync(request);
        placeResult.Success.Should().BeTrue();

        var cancelResult = await _oms.CancelOrderAsync(placeResult.OrderId);
        cancelResult.Success.Should().BeTrue();

        var completed = _oms.GetCompletedOrders();
        completed.Should().ContainSingle(o =>
            o.OrderId == placeResult.OrderId &&
            o.Status == OrderStatus.Cancelled);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenAuditTrailConfigured_RecordsCancelledOutcome()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        using var oms = new OrderManagementSystem(
            _gateway,
            NullLogger<OrderManagementSystem>.Instance,
            auditTrail: auditTrail);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "TSLA",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 3m,
            LimitPrice = 200m,
            StrategyId = "strategy-live"
        });

        var cancelResult = await oms.CancelOrderAsync(placed.OrderId);

        cancelResult.Success.Should().BeTrue();
        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Action == "OrderCancelled" &&
            entry.Outcome == OrderStatus.Cancelled.ToString() &&
            entry.OrderId == placed.OrderId &&
            entry.Symbol == "TSLA" &&
            entry.Scope == "strategy:strategy-live/symbol:TSLA" &&
            entry.Metadata != null &&
            entry.Metadata["reportType"] == ExecutionReportType.Cancelled.ToString());
    }

    // ---- GetCompletedOrders — take limit is respected ----

    [Fact]
    public async Task GetCompletedOrders_TakeLimit_ReturnsAtMostTake()
    {
        // Place and cancel 5 orders
        for (var i = 0; i < 5; i++)
        {
            var r = await _oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = $"SYM{i:D2}",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1,
                LimitPrice = 100m
            });
            await _oms.CancelOrderAsync(r.OrderId);
        }

        var completed = _oms.GetCompletedOrders(take: 3);

        completed.Should().HaveCount(3);
    }

    // ---- GetCompletedOrders — default take returns no more than 20 ----

    [Fact]
    public async Task GetCompletedOrders_DefaultTake_ReturnsAtMost20()
    {
        for (var i = 0; i < 25; i++)
        {
            var r = await _oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = $"SYM{i:D2}",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1,
                LimitPrice = 50m
            });
            await _oms.CancelOrderAsync(r.OrderId);
        }

        var completed = _oms.GetCompletedOrders();

        completed.Should().HaveCountLessThanOrEqualTo(20);
    }

    // ---- GetCompletedOrders — most recent first ----

    [Fact]
    public async Task GetCompletedOrders_IsOrderedByCompletionTimeDescending()
    {
        var ids = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var r = await _oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = $"SYM{i:D2}",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1,
                LimitPrice = 100m
            });
            await _oms.CancelOrderAsync(r.OrderId);
            ids.Add(r.OrderId);
        }

        var completed = _oms.GetCompletedOrders();
        var completedIds = completed.Select(o => o.OrderId).ToList();

        // Last cancelled should appear first
        completedIds[0].Should().Be(ids[^1]);
    }

    [Fact]
    public async Task CancelOrderAsync_WhenGatewayRejectsCancel_ReturnsFailureAndKeepsWorkingState()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<OrderRequest>();
                return new ExecutionReport
                {
                    OrderId = request.ClientOrderId ?? "ord-1",
                    ClientOrderId = request.ClientOrderId,
                    ReportType = ExecutionReportType.New,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    OrderStatus = OrderStatus.Accepted,
                    OrderQuantity = request.Quantity,
                    Timestamp = DateTimeOffset.UtcNow
                };
            });
        gateway.CancelOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ExecutionReport
            {
                OrderId = "ord-1",
                ReportType = ExecutionReportType.Rejected,
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Rejected,
                RejectReason = "too late to cancel",
                Timestamp = DateTimeOffset.UtcNow
            });

        var tempRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            auditTrail: auditTrail);
        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 100m
        });

        var result = await oms.CancelOrderAsync(placed.OrderId);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("too late to cancel");
        result.OrderState.Should().NotBeNull();
        result.OrderState!.Status.Should().Be(OrderStatus.Accepted);
        oms.GetOrder(placed.OrderId)!.Status.Should().Be(OrderStatus.Accepted);

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Action == "OrderCancelRejected" &&
            entry.Outcome == OrderStatus.Rejected.ToString() &&
            entry.OrderId == placed.OrderId &&
            entry.Symbol == "AAPL" &&
            entry.Message == "too late to cancel");
    }

    [Fact]
    public async Task ModifyOrderAsync_WhenAuditTrailConfigured_RecordsModificationOutcome()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("ibkr");
        gateway.SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<OrderRequest>();
                return new ExecutionReport
                {
                    OrderId = request.ClientOrderId ?? "ord-1",
                    ClientOrderId = request.ClientOrderId,
                    ReportType = ExecutionReportType.New,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    OrderStatus = OrderStatus.Accepted,
                    OrderQuantity = request.Quantity,
                    Timestamp = DateTimeOffset.UtcNow
                };
            });
        gateway.ModifyOrderAsync(Arg.Any<string>(), Arg.Any<OrderModification>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var orderId = callInfo.ArgAt<string>(0);
                return new ExecutionReport
                {
                    OrderId = orderId,
                    ReportType = ExecutionReportType.Modified,
                    Symbol = "MSFT",
                    Side = OrderSide.Buy,
                    OrderStatus = OrderStatus.Accepted,
                    OrderQuantity = 12m,
                    Timestamp = DateTimeOffset.UtcNow
                };
            });

        var tempRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            auditTrail: auditTrail);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 100m
        });

        var result = await oms.ModifyOrderAsync(placed.OrderId, new OrderModification
        {
            NewQuantity = 12m,
            NewLimitPrice = 101m
        });

        result.Success.Should().BeTrue();
        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Action == "OrderModified" &&
            entry.BrokerName == "ibkr" &&
            entry.OrderId == placed.OrderId &&
            entry.Symbol == "MSFT" &&
            entry.Metadata != null &&
            entry.Metadata["newQuantity"] == "12" &&
            entry.Metadata["newLimitPrice"] == "101");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenGatewayStartsDisconnected_DoesNotAutoConnect()
    {
        var connected = false;
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("robinhood");
        gateway.IsConnected.Returns(_ => connected);
        gateway.ConnectAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                connected = true;
                return Task.CompletedTask;
            });
        gateway.SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns((_) => Task.FromException<ExecutionReport>(new InvalidOperationException("robinhood is not connected. Call ConnectAsync first.")));

        var tempRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            auditTrail: auditTrail,
            brokerageConfiguration: CreateEnabledBrokerageConfiguration("robinhood"),
            liveOrderReadinessGate: new RecordingLiveOrderReadinessGate(
                LiveOrderReadinessDecision.Approved("audit://live/run-disconnected")));

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-disconnected"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not connected");
        await gateway.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Action == "OrderRejected" &&
            entry.BrokerName == "robinhood" &&
            entry.Symbol == "AAPL");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenBrokerRoutingGateRejects_DoesNotSubmitToGateway()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");
        var config = new BrokerageConfiguration
        {
            Gateway = "alpaca",
            LiveExecutionEnabled = true,
            BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpaca"] = new() { ProductionOrderRoutingEnabled = false }
            }
        };

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: config);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Production order routing is disabled");
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderAsync_WithNonPaperGatewayAndMissingBrokerageConfiguration_RejectsWithoutSubmitting()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Brokerage configuration is required");
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderAsync_WithLiveBrokerAndNoRunId_RejectsBeforeReadinessGate()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");
        var liveReadinessGate = new RecordingLiveOrderReadinessGate(
            LiveOrderReadinessDecision.Approved("audit://live/run-live-001"));

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: CreateEnabledBrokerageConfiguration("alpaca"),
            liveOrderReadinessGate: liveReadinessGate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("requires runId metadata");
        liveReadinessGate.Request.Should().BeNull("the OMS should fail closed before calling the readiness gate without a run id");
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderAsync_WithLiveBrokerAndNoReadinessGate_RejectsWithoutSubmitting()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: CreateEnabledBrokerageConfiguration("alpaca"));

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-live-001"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("requires a live order readiness gate");
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenLiveReadinessGateRejects_DoesNotSubmitToGateway()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");
        var liveReadinessGate = new RecordingLiveOrderReadinessGate(
            LiveOrderReadinessDecision.Rejected("W7 live-readiness evidence is incomplete."));

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: CreateEnabledBrokerageConfiguration("alpaca"),
            liveOrderReadinessGate: liveReadinessGate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            StrategyId = "strategy-live",
            FundAccountId = LiveFundAccountId,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-live-001"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("W7 live-readiness evidence is incomplete");
        liveReadinessGate.Request.Should().NotBeNull();
        liveReadinessGate.Request!.RunId.Should().Be("run-live-001");
        liveReadinessGate.Request.StrategyId.Should().Be("strategy-live");
        liveReadinessGate.Request.FundAccountId.Should().Be(LiveFundAccountId);
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenLiveReadinessGateApprovesWithoutEvidence_RejectsWithoutSubmitting()
    {
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");
        var liveReadinessGate = new RecordingLiveOrderReadinessGate(new LiveOrderReadinessDecision(true));

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: CreateEnabledBrokerageConfiguration("alpaca"),
            liveOrderReadinessGate: liveReadinessGate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            FundAccountId = LiveFundAccountId,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["runId"] = "run-live-001"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("without a retained evidence reference");
        liveReadinessGate.Request.Should().NotBeNull();
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PlaceOrderAsync_WithLiveBrokerAndClientOverrideMetadata_DoesNotBypassOperatorControls()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        var bypassOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.BypassOrderControls,
            Reason: "Operator approved emergency closeout.",
            CreatedBy: "ops",
            Symbol: "AAPL",
            StrategyId: "strategy-live",
            RunId: "run-live-001"));
        await controls.SetCircuitBreakerAsync(
            isOpen: true,
            reason: "Operator halt",
            changedBy: "ops");

        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");
        var liveReadinessGate = new RecordingLiveOrderReadinessGate(
            LiveOrderReadinessDecision.Approved("audit://live/run-live-001"));
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls,
            auditTrail: auditTrail,
            brokerageConfiguration: CreateEnabledBrokerageConfiguration("alpaca"),
            liveOrderReadinessGate: liveReadinessGate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = 1m,
            StrategyId = "strategy-live",
            FundAccountId = LiveFundAccountId,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = "ops",
                ["correlationId"] = "act-live-forged-override",
                ["manualOverrideId"] = bypassOverride.OverrideId,
                ["runId"] = "run-live-001"
            }
        });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Operator halt");
        liveReadinessGate.Request.Should().NotBeNull();
        await gateway.DidNotReceive().SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>());

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Action == "OrderRejected" &&
            entry.Reason == "CIRCUIT_BREAKER_OPEN" &&
            entry.CorrelationId == "act-live-forged-override" &&
            entry.Metadata != null &&
            entry.Metadata["rejectCode"] == "CIRCUIT_BREAKER_OPEN");
    }

    [Fact]
    public async Task PlaceOrderAsync_WithClientBrokerAccountMetadata_StripsRoutingKeysBeforeGatewaySubmit()
    {
        OrderRequest? submittedRequest = null;
        var gateway = Substitute.For<IExecutionGateway>();
        gateway.GatewayId.Returns("alpaca");
        var liveReadinessGate = new RecordingLiveOrderReadinessGate(
            LiveOrderReadinessDecision.Approved("audit://live/run-live-001"));
        gateway.SubmitOrderAsync(Arg.Do<OrderRequest>(request => submittedRequest = request), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var request = callInfo.Arg<OrderRequest>();
                return new ExecutionReport
                {
                    OrderId = request.ClientOrderId ?? "ord-1",
                    ClientOrderId = request.ClientOrderId,
                    ReportType = ExecutionReportType.New,
                    Symbol = request.Symbol,
                    Side = request.Side,
                    OrderStatus = OrderStatus.Accepted,
                    OrderQuantity = request.Quantity,
                    Timestamp = DateTimeOffset.UtcNow
                };
            });

        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: CreateEnabledBrokerageConfiguration("alpaca"),
            liveOrderReadinessGate: liveReadinessGate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "912797AB1",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1000m,
            FundAccountId = LiveFundAccountId,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["asset_class"] = "treasury",
                ["broker_account_id"] = "attacker-broker-account",
                ["account_id"] = "attacker-ledger-account",
                ["manualOverrideId"] = "forged-override",
                ["liveReadinessEvidenceReference"] = "forged-evidence-ref",
                ["runId"] = "run-live-001"
            }
        });

        result.Success.Should().BeTrue();
        liveReadinessGate.Request.Should().NotBeNull();
        liveReadinessGate.Request!.RunId.Should().Be("run-live-001");
        liveReadinessGate.Request.FundAccountId.Should().Be(LiveFundAccountId);
        submittedRequest.Should().NotBeNull();
        submittedRequest!.Metadata.Should().NotBeNull();
        submittedRequest.Metadata!.Should().NotContainKey("broker_account_id");
        submittedRequest.Metadata.Should().NotContainKey("account_id");
        submittedRequest.Metadata.Should().NotContainKey("manualOverrideId");
        submittedRequest.Metadata.Should().NotContainKey("liveReadinessEvidenceReference");
        submittedRequest.Metadata["asset_class"].Should().Be("treasury");
    }

    [Fact]
    public async Task PlaceOrderAsync_WithTypedPaperGatewayIdNotNamedPaper_DoesNotRequireLiveReadinessGate()
    {
        var gateway = new TypedPaperExecutionGateway("sandbox-paper");
        var liveReadinessGate = new RecordingLiveOrderReadinessGate(
            LiveOrderReadinessDecision.Rejected("should not be evaluated for typed paper gateways"));
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            liveOrderReadinessGate: liveReadinessGate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m,
            LimitPrice = 100m
        });

        result.Success.Should().BeTrue();
        liveReadinessGate.Request.Should().BeNull();
    }

    [Fact]
    public async Task CancelAllAsync_WhenManyOpenOrders_RespectsConfiguredConcurrencyCap()
    {
        var gateway = new ConcurrencyObservingCancelGateway();
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            options: new OrderManagementSystemOptions
            {
                CancelAllMaxConcurrency = 2
            });

        for (var i = 0; i < 6; i++)
        {
            await oms.PlaceOrderAsync(new OrderRequest
            {
                Symbol = $"SYM{i}",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 1m,
                LimitPrice = 10m
            });
        }

        var cancelAllTask = oms.CancelAllAsync();
        try
        {
            await gateway.WaitForSecondCancelStartedAsync();

            gateway.MaxObservedConcurrency.Should().BeLessThanOrEqualTo(2);
        }
        finally
        {
            gateway.ReleaseCancels();
            await cancelAllTask;
        }
    }

    private static BrokerageConfiguration CreateEnabledBrokerageConfiguration(string gatewayId) =>
        new()
        {
            Gateway = gatewayId,
            LiveExecutionEnabled = true,
            ReadOnlyVerificationPassed = true,
            PaperLifecycleTestsPassed = true,
            ReplayEvidencePassed = true,
            ProductionRoutingPhaseEnabled = true,
            ValidationGates = new BrokerValidationGateOptions
            {
                RequireValidationArtifactsForOrderPlacement = false
            },
            BrokerFlows = new Dictionary<string, BrokerFlowFlags>(StringComparer.OrdinalIgnoreCase)
            {
                [gatewayId] = new()
                {
                    ReadOnlyDataEnabled = true,
                    PaperOrderFlowEnabled = true,
                    ProductionOrderRoutingEnabled = true
                }
            }
        };

    private sealed class RecordingLiveOrderReadinessGate(LiveOrderReadinessDecision decision)
        : ILiveOrderReadinessGate
    {
        public LiveOrderReadinessRequest? Request { get; private set; }

        public Task<LiveOrderReadinessDecision> EvaluateAsync(
            LiveOrderReadinessRequest request,
            CancellationToken ct = default)
        {
            Request = request;
            return Task.FromResult(decision);
        }
    }

    private sealed class TypedPaperExecutionGateway(string gatewayId) : IExecutionGateway, IExecutionGatewayModeProvider
    {
        public string GatewayId => gatewayId;

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public bool IsConnected => true;

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

    private sealed class ConcurrencyObservingCancelGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly TaskCompletionSource _secondCancelStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseCancels = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _currentConcurrency;
        private int _startedCancels;
        private int _maxObservedConcurrency;

        public string GatewayId => "paper";

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public bool IsConnected => true;

        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

        public Task WaitForSecondCancelStartedAsync() =>
            _secondCancelStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void ReleaseCancels() =>
            _releaseCancels.TrySetResult();

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ExecutionReport
            {
                OrderId = request.ClientOrderId ?? Guid.NewGuid().ToString("N"),
                ClientOrderId = request.ClientOrderId,
                ReportType = ExecutionReportType.New,
                Symbol = request.Symbol,
                Side = request.Side,
                OrderStatus = OrderStatus.Accepted,
                OrderQuantity = request.Quantity,
                Timestamp = DateTimeOffset.UtcNow
            });

        public async Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
        {
            var current = Interlocked.Increment(ref _currentConcurrency);
            RecordMaxObservedConcurrency(current);
            if (Interlocked.Increment(ref _startedCancels) == 2)
            {
                _secondCancelStarted.TrySetResult();
            }

            try
            {
                await _releaseCancels.Task.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }

            return new ExecutionReport
            {
                OrderId = orderId,
                ReportType = ExecutionReportType.Cancelled,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = OrderStatus.Cancelled,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        private void RecordMaxObservedConcurrency(int current)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxObservedConcurrency);
                if (current <= observed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxObservedConcurrency, current, observed) == observed)
                {
                    return;
                }
            }
        }

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

    // ---- Duplicate client order id guard ----

    [Fact]
    public async Task PlaceOrderAsync_DuplicateClientOrderIdForActiveOrder_RejectsWithoutTouchingOriginal()
    {
        // Limit orders stay accepted (active) in the paper gateway.
        var originalResult = await _oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10,
            LimitPrice = 150m,
            ClientOrderId = "CLIENT-1"
        });
        originalResult.Success.Should().BeTrue();
        var originalState = _oms.GetOrder("CLIENT-1");
        originalState.Should().NotBeNull();

        var duplicateResult = await _oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "TSLA",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = 99,
            ClientOrderId = "CLIENT-1"
        });

        duplicateResult.Success.Should().BeFalse();
        duplicateResult.ErrorMessage.Should().Contain("Duplicate client order id");
        _oms.GetOrder("CLIENT-1").Should().Be(originalState,
            "a duplicate submission must not overwrite the tracked state of the active order");
    }

    [Fact]
    public async Task PlaceOrderAsync_ClientOrderIdReuseAfterTerminalOrder_Succeeds()
    {
        // Market orders fill immediately in the paper gateway, so the first order is terminal.
        var firstResult = await _oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5,
            ClientOrderId = "CLIENT-2"
        });
        firstResult.Success.Should().BeTrue();
        _oms.GetOrder("CLIENT-2")!.Status.Should().Be(OrderStatus.Filled);

        var secondResult = await _oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "GOOG",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 3,
            LimitPrice = 100m,
            ClientOrderId = "CLIENT-2"
        });

        secondResult.Success.Should().BeTrue(
            "a terminal order's client order id may be reclaimed, consistent with retention trimming");
        _oms.GetOrder("CLIENT-2")!.Symbol.Should().Be("GOOG");
    }
}

// ---------------------------------------------------------------------------
// Security Master gate tests (separate fixture to keep constructor clean)
// ---------------------------------------------------------------------------

public sealed class OrderManagementSystemGateTests : IDisposable
{
    private readonly ExecutionGateway _gateway;

    public OrderManagementSystemGateTests()
    {
        _gateway = new ExecutionGateway(
            NullLogger<ExecutionGateway>.Instance,
            options: new Meridian.Execution.Adapters.PaperTradingGatewayOptions { AllowScaffoldMarketFills = true });
    }

    public void Dispose() { }

    [Fact]
    public async Task PlaceOrderAsync_WhenGateApproves_OrderIsAccepted()
    {
        var gate = new ApproveAllGate();
        using var oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: gate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5
        });

        result.Success.Should().BeTrue("the gate approved the symbol");
        gate.CheckCount.Should().Be(1);
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenGateRejects_ReturnsFailureWithoutSubmittingToGateway()
    {
        var gate = new RejectAllGate("UNKNWN is not in Security Master");
        using var oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance,
            securityMasterGate: gate);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "UNKNWN",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 10
        });

        result.Success.Should().BeFalse("the gate rejected the symbol");
        result.ErrorMessage.Should().Contain("UNKNWN");
        oms.GetOpenOrders().Should().BeEmpty("rejected orders must not be tracked");
    }

    [Fact]
    public async Task PlaceOrderAsync_WithNoGateWired_AcceptsAnySymbol()
    {
        using var oms = new OrderManagementSystem(_gateway, NullLogger<OrderManagementSystem>.Instance);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "ANYTHING",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1
        });

        // Gateway fills market orders immediately, so no rejection from missing gate
        result.Success.Should().BeTrue("no gate means any symbol is accepted");
    }

    [Fact]
    public async Task PlaceOrderAsync_GateRejectedReuseOfTerminalOrderId_PreservesTerminalState()
    {
        var riskValidator = Substitute.For<IRiskValidator>();
        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(RiskValidationResult.Approved());
        using var oms = new OrderManagementSystem(
            _gateway,
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: riskValidator);

        var firstResult = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "MSFT",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 5,
            ClientOrderId = "CLIENT-3"
        });
        firstResult.Success.Should().BeTrue();
        var filledState = oms.GetOrder("CLIENT-3");
        filledState!.Status.Should().Be(OrderStatus.Filled);

        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(RiskValidationResult.Rejected("limit breach"));

        var rejectedResult = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "TSLA",
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = 99,
            ClientOrderId = "CLIENT-3"
        });

        rejectedResult.Success.Should().BeFalse();
        oms.GetOrder("CLIENT-3").Should().Be(filledState,
            "a gate-rejected submission reusing a terminal order's id must not overwrite the filled order's state");
    }

    [Fact]
    public async Task PlaceOrderAsync_GateRejectionWithFreshId_StillRecordsRejectedState()
    {
        var riskValidator = Substitute.For<IRiskValidator>();
        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(RiskValidationResult.Rejected("limit breach"));
        using var oms = new OrderManagementSystem(
            _gateway,
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: riskValidator);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1,
            ClientOrderId = "CLIENT-4"
        });

        result.Success.Should().BeFalse();
        oms.GetOrder("CLIENT-4")!.Status.Should().Be(OrderStatus.Rejected,
            "a gate rejection under a previously unused id must still be visible in the order table");
    }

    [Fact]
    public async Task PlaceOrderAsync_RiskEscalation_ReturnsTypedParkedOutcome()
    {
        var riskValidator = Substitute.For<IRiskValidator>();
        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(RiskValidationResult.Escalated("Parked for governed approval (esc-1): above band", "esc-1"));
        using var oms = new OrderManagementSystem(
            _gateway,
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: riskValidator);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1,
            ClientOrderId = "CLIENT-PARKED"
        });

        result.Success.Should().BeFalse("a parked order does not route");
        result.RequiresApproval.Should().BeTrue("an escalation awaits governed approval instead of hard-rejecting");
        result.EscalationId.Should().Be("esc-1");
        oms.GetOrder("CLIENT-PARKED")!.Status.Should().Be(OrderStatus.Rejected,
            "nothing is live at the broker while the escalation awaits its decision");
    }

    [Fact]
    public async Task PlaceOrderAsync_RiskRejectionWithWarnings_CarriesWarningsOnResult()
    {
        var riskValidator = Substitute.For<IRiskValidator>();
        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(RiskValidationResult.Rejected("limit breach") with
            {
                Warnings = ["concentration-watch: approaching cap"]
            });
        using var oms = new OrderManagementSystem(
            _gateway,
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: riskValidator);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1,
            ClientOrderId = "CLIENT-WARNED"
        });

        result.Success.Should().BeFalse();
        result.RequiresApproval.Should().BeFalse();
        result.RiskWarnings.Should().ContainSingle(warning => warning.Contains("approaching cap"),
            "non-blocking flags accumulated before the rejection must survive on the result");
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenGatewayFaults_ReArmsTheConsumedGovernedApproval()
    {
        // A validator that approves while reporting it consumed a one-shot approval.
        var queue = new RiskEscalationQueueService(
            NullLogger<RiskEscalationQueueService>.Instance,
            options: new RiskEscalationQueueOptions(
                Path.Combine(Path.GetTempPath(), "Meridian.Tests", $"escalations-{Guid.NewGuid():N}", "escalations.json")));
        var parked = queue.Park(
            new OrderRequest { Symbol = "AAPL", Side = OrderSide.Buy, Type = OrderType.Market, Quantity = 1 },
            "above band");
        queue.Approve(parked.EscalationId, actor: "risk-desk");
        queue.TryConsumeApproval(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [RiskEscalationQueueService.ApprovalMetadataKey] = parked.EscalationId
            }
        }).Should().NotBeNull();

        var riskValidator = Substitute.For<IRiskValidator>();
        riskValidator.ValidateOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(RiskValidationResult.Approved() with { ConsumedApprovalId = parked.EscalationId });

        // "paper" keeps the brokerage placement gate in simulated mode so the order
        // reaches the gateway, where submission then faults.
        var faultingGateway = Substitute.For<IExecutionGateway>();
        faultingGateway.GatewayId.Returns("paper");
        faultingGateway.SubmitOrderAsync(Arg.Any<OrderRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ExecutionReport>>(_ => throw new InvalidOperationException("gateway unreachable"));

        using var oms = new OrderManagementSystem(
            faultingGateway,
            NullLogger<OrderManagementSystem>.Instance,
            riskValidator: riskValidator,
            escalationQueue: queue);

        var result = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1,
            ClientOrderId = "CLIENT-FAULT"
        });

        result.Success.Should().BeFalse();
        queue.TryGet(parked.EscalationId)!.Status.Should().Be(
            RiskEscalationStatus.Approved,
            "the gateway faulted before anything routed, so the operator's approval must be retryable");
    }

    // ---- Stubs ----

    private sealed class ApproveAllGate : ISecurityMasterGate
    {
        public int CheckCount { get; private set; }

        public Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
        {
            CheckCount++;
            return Task.FromResult(new SecurityMasterGateResult(true));
        }
    }

    private sealed class RejectAllGate : ISecurityMasterGate
    {
        private readonly string _reason;

        public RejectAllGate(string reason) => _reason = reason;

        public Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
            => Task.FromResult(new SecurityMasterGateResult(false, _reason));
    }
}
