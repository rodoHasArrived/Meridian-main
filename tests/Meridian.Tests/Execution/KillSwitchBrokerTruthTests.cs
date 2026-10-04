using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Covers the broker-truthful half of the kill-switch sweep: the in-memory dictionary is a claim
/// about the book, not the book. After an OMS restart, for bracket child legs, or for orders
/// placed out of band, the broker can hold working orders the process has never heard of — so the
/// sweep must cancel the union of both views, say when the broker view could not be established,
/// and track the child legs a bracket submission spawns server-side.
/// </summary>
public sealed class KillSwitchBrokerTruthTests
{
    // ---- Union sweep: broker-only orders ----

    [Fact]
    public async Task CancelAllAsync_CancelsBrokerOnlyOrders_AndCancelsSharedOrdersExactlyOnce()
    {
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        placed.Success.Should().BeTrue();

        // The broker book holds the tracked order under its own UUID plus one order the OMS has
        // never heard of — a post-restart survivor.
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-1",
            ClientOrderId = placed.OrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Status = OrderStatus.Accepted
        });
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-orphan-1",
            ClientOrderId = "external-1",
            Symbol = "MSFT",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Status = OrderStatus.Accepted
        });

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        sweep.Requested.Should().Be(2, "the tracked order and the broker-only order, deduplicated");
        sweep.Cancelled.Should().Be(2);
        sweep.StillWorking.Should().BeEmpty();
        sweep.BrokerViewUnavailable.Should().BeFalse();

        gateway.CancelRequests.Should().ContainSingle(id => id == "broker-1",
            "the tracked path must cancel through the broker UUID from the matching broker row");
        gateway.CancelRequests.Should().Contain("broker-orphan-1",
            "a broker-known order the in-memory book does not track must still be cancelled");
        gateway.CancelRequests.Should().NotContain(placed.OrderId,
            "the client order id is not valid at a broker UUID cancellation endpoint");
        gateway.CancellationIdentifiers.Should().OnlyContain(identifier =>
            identifier.Kind == OrderCancellationIdentifierKind.BrokerOrderId,
            "broker snapshot matches and residual rows must use Alpaca's broker-ID namespace explicitly");
    }

    [Fact]
    public async Task CancelAllAsync_UuidShapedClientIdCollision_DoesNotCancelTheWrongBrokerOrder()
    {
        const string collidingClientId = "11111111-1111-1111-1111-111111111111";
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            ClientOrderId = collidingClientId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        placed.Success.Should().BeTrue();

        // Put the colliding broker UUID first. A value-only match used to associate this
        // unrelated row with the tracked client id and then cancel the wrong order.
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = collidingClientId,
            ClientOrderId = "external-order",
            Symbol = "MSFT",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 5m,
            Status = OrderStatus.Accepted
        });
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "actual-aapl-broker-id",
            ClientOrderId = collidingClientId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            Status = OrderStatus.Accepted
        });

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        sweep.Requested.Should().Be(2);
        sweep.Cancelled.Should().Be(2);
        gateway.CancelRequests.Should().BeEquivalentTo(
            ["actual-aapl-broker-id", collidingClientId]);
        gateway.CancellationIdentifiers.Should().OnlyContain(identifier =>
            identifier.Kind == OrderCancellationIdentifierKind.BrokerOrderId);
    }

    [Fact]
    public async Task CancelAllAsync_WhenABrokerOnlyOrderRefusesToCancel_NamesItStillWorking()
    {
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-orphan-1",
            ClientOrderId = "external-1",
            Symbol = "MSFT",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Status = OrderStatus.Accepted
        });
        gateway.RefuseToCancel.Add("broker-orphan-1");

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Failed);
        sweep.Requested.Should().Be(1);
        sweep.Cancelled.Should().Be(0);
        sweep.StillWorking.Should().ContainSingle(failure =>
            failure.OrderId == "broker-orphan-1" && failure.Symbol == "MSFT");
        sweep.RequiresOperatorAction.Should().BeTrue();
    }

    [Fact]
    public async Task CancelAllAsync_WhenBrokerCancellationRemainsPending_DoesNotReportCompleted()
    {
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        placed.Success.Should().BeTrue();
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-1",
            ClientOrderId = placed.OrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Status = OrderStatus.Accepted
        });
        gateway.RemainPendingAfterCancel.Add("broker-1");

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Failed);
        sweep.Cancelled.Should().Be(0);
        sweep.StillWorking.Should().ContainSingle(failure =>
            failure.OrderId == placed.OrderId && failure.Reason.Contains("pending", StringComparison.OrdinalIgnoreCase));
        gateway.CancelRequests.Should().ContainSingle(id => id == "broker-1");
    }

    [Fact]
    public async Task CancelAllAsync_WhenAnOrderAppearsDuringConvergence_DoesNotReportCompleted()
    {
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-1",
            ClientOrderId = placed.OrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            Status = OrderStatus.Accepted
        });
        gateway.BrokerOrderAppearingOnVerification = new BrokerOrder
        {
            OrderId = "late-broker-order",
            ClientOrderId = "external-late-order",
            Symbol = "MSFT",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 5m,
            Status = OrderStatus.Accepted
        };

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().NotBe(KillSwitchSweepOutcome.Completed);
        sweep.StillWorking.Should().ContainSingle(failure =>
            failure.OrderId == "late-broker-order"
            && failure.Symbol == "MSFT"
            && failure.Reason.Contains("broker verification", StringComparison.OrdinalIgnoreCase));
        gateway.OpenOrdersReadCount.Should().Be(2,
            "completion requires a fresh fully enumerated broker book after cancellation");
    }

    [Fact]
    public async Task CancelAllAsync_WhenCancelLosesToFill_AppliesAndPublishesVerifiedFill()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-fill-race",
            ClientOrderId = placed.OrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            Status = OrderStatus.Accepted
        });
        gateway.FillDuringCancel.Add("broker-fill-race");

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Failed,
            "an execution is terminal but is not a confirmed cancellation");
        sweep.Cancelled.Should().Be(0);
        var state = oms.GetOrder(placed.OrderId!);
        state.Should().NotBeNull();
        state!.Status.Should().Be(OrderStatus.Filled);
        state.FilledQuantity.Should().Be(10m);
        state.AverageFillPrice.Should().Be(151.25m);

        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fill = await oms.ExecutionReports.ReadAsync(reportTimeout.Token);
        fill.ReportType.Should().Be(ExecutionReportType.Fill);
        fill.FilledQuantity.Should().Be(10m);
        fill.FillPrice.Should().Be(151.25m);
        portfolio.Positions["AAPL"].ExactQuantity.Should().Be(10m,
            "the verified fill must reach portfolio state, not merely adapter fields");
    }

    [Fact]
    public async Task CancelAllAsync_WhenBrokerOrderBecomesRejected_AppliesTerminalBrokerState()
    {
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-terminal-reject",
            ClientOrderId = placed.OrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            Status = OrderStatus.Accepted
        });
        gateway.TerminalRejectDuringCancel.Add("broker-terminal-reject");

        var firstSweep = await oms.CancelAllAsync();

        firstSweep.Outcome.Should().Be(KillSwitchSweepOutcome.Failed,
            "the order was not cancelled even though the broker made it non-fillable");
        oms.GetOrder(placed.OrderId!)!.Status.Should().Be(OrderStatus.Rejected,
            "the broker-order terminal status must replace the previously working local state");

        var secondSweep = await oms.CancelAllAsync();
        secondSweep.Requested.Should().Be(0,
            "a broker-terminal rejection must not be swept repeatedly as a working order");
        gateway.CancelRequests.Should().ContainSingle();
    }

    [Fact]
    public async Task CancelAllAsync_CancelledAfterPartialFill_PublishesAndBooksTheExecution()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        await using var gateway = new UnionSweepBrokerageGateway();
        using var oms = new OrderManagementSystem(
            gateway,
            NullLogger<OrderManagementSystem>.Instance,
            portfolioState: portfolio);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "broker-partial-cancel",
            ClientOrderId = placed.OrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            Status = OrderStatus.PartiallyFilled
        });
        gateway.PartialFillDuringCancel["broker-partial-cancel"] = 3m;

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        var state = oms.GetOrder(placed.OrderId!);
        state!.Status.Should().Be(OrderStatus.Cancelled);
        state.FilledQuantity.Should().Be(3m);
        portfolio.Positions["AAPL"].ExactQuantity.Should().Be(3m);

        using var reportTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var fill = await oms.ExecutionReports.ReadAsync(reportTimeout.Token);
        fill.ReportType.Should().Be(ExecutionReportType.PartialFill);
        fill.OrderStatus.Should().Be(OrderStatus.PartiallyFilled);
        fill.FilledQuantity.Should().Be(3m);
        fill.FillPrice.Should().Be(151.25m);
    }

    // ---- Broker view unavailable ----

    [Fact]
    public async Task CancelAllAsync_WhenTheBrokerBookCannotBeEnumerated_StillSweepsInMemoryAndFlagsTheBrokerView()
    {
        await using var gateway = new UnionSweepBrokerageGateway
        {
            OpenOrdersFailure = new InvalidOperationException("the broker order listing timed out")
        };
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 150m
        });
        placed.Success.Should().BeTrue();

        var sweep = await oms.CancelAllAsync();

        sweep.Cancelled.Should().Be(1, "an unenumerable broker book must not abort the in-memory sweep");
        gateway.CancelRequests.Should().Contain(placed.OrderId);
        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed, "every order the sweep saw was cancelled");
        sweep.BrokerViewUnavailable.Should().BeTrue();
        sweep.BrokerViewError.Should().Contain("timed out");
        sweep.RequiresOperatorAction.Should().BeTrue(
            "a sweep that never saw the broker book cannot establish the book is empty");
        sweep.Describe().Should().Contain("broker", "the rendered verdict must carry the warning too");
    }

    [Fact]
    public async Task CancelAllAsync_OverAnEmptyInMemoryBook_WithAnUnreachableBroker_DoesNotClaimAVerifiedEmptyBook()
    {
        await using var gateway = new UnionSweepBrokerageGateway
        {
            OpenOrdersFailure = new InvalidOperationException("connection refused")
        };
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var sweep = await oms.CancelAllAsync();

        sweep.Requested.Should().Be(0);
        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        sweep.BrokerViewUnavailable.Should().BeTrue();
        sweep.RequiresOperatorAction.Should().BeTrue(
            "this is exactly the post-restart sweep that used to report Empty over a loaded broker book");
    }

    // ---- Bracket child registration ----

    [Fact]
    public async Task PlaceOrderAsync_WhenTheAckCarriesBrokerChildOrders_RegistersThemAsTrackedOrders()
    {
        var fundAccountId = Guid.NewGuid();
        await using var gateway = new UnionSweepBrokerageGateway
        {
            ChildOrdersOnSubmit = BracketChildLegs()
        };
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 200m,
            FundAccountId = fundAccountId
        });
        placed.Success.Should().BeTrue();

        var takeProfit = oms.GetOrder("leg-tp-client");
        takeProfit.Should().NotBeNull("the TP leg's execution reports must land on tracked state");
        takeProfit!.Side.Should().Be(OrderSide.Sell);
        takeProfit.LimitPrice.Should().Be(210m);
        takeProfit.FundAccountId.Should().Be(
            fundAccountId,
            "a bracket's exit legs settle into the same fund account the entry was admitted under");

        var stopLoss = oms.GetOrder("leg-sl-client");
        stopLoss.Should().NotBeNull();
        stopLoss!.StopPrice.Should().Be(190m);

        oms.GetOpenOrders().Select(order => order.OrderId).Should()
            .Contain(new[] { "leg-tp-client", "leg-sl-client" }, "the sweep enumerates the open book");
    }

    [Fact]
    public async Task CancelAllAsync_SweepsRegisteredBracketChildren()
    {
        await using var gateway = new UnionSweepBrokerageGateway
        {
            ChildOrdersOnSubmit = BracketChildLegs()
        };
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 200m
        });
        placed.Success.Should().BeTrue();

        gateway.BrokerOpenOrders.Add(new BrokerOrder
        {
            OrderId = "parent-broker",
            ClientOrderId = placed.OrderId,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            Status = OrderStatus.Accepted
        });
        gateway.BrokerOpenOrders.AddRange(BracketChildLegs());

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        sweep.Requested.Should().Be(3, "the parent and both bracket legs are all working orders");
        sweep.Cancelled.Should().Be(3);
        gateway.CancelRequests.Should().BeEquivalentTo(
            ["parent-broker", "leg-tp-broker", "leg-sl-broker"],
            "every tracked bracket order must cancel through its broker-assigned UUID");
    }

    [Fact]
    public async Task ChildExecutionReports_AreNoLongerDroppedAsUntracked()
    {
        await using var gateway = new UnionSweepBrokerageGateway
        {
            ChildOrdersOnSubmit = BracketChildLegs()
        };
        using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance);

        var placed = await oms.PlaceOrderAsync(new OrderRequest
        {
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 200m
        });
        placed.Success.Should().BeTrue();

        // The broker streams the TP leg's cancellation under the leg's own ids, exactly the
        // report shape that used to be logged as "not tracked by this OMS" and discarded.
        await gateway.PublishReportAsync(new ExecutionReport
        {
            OrderId = "leg-tp-broker",
            ClientOrderId = "leg-tp-client",
            ReportType = ExecutionReportType.Cancelled,
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            OrderStatus = OrderStatus.Cancelled,
            Timestamp = DateTimeOffset.UtcNow
        });

        await WaitUntilAsync(() => oms.GetOrder("leg-tp-client")?.Status == OrderStatus.Cancelled);
        oms.GetOrder("leg-tp-client")!.Status.Should().Be(OrderStatus.Cancelled);
    }

    private static IReadOnlyList<BrokerOrder> BracketChildLegs() =>
    [
        new BrokerOrder
        {
            OrderId = "leg-tp-broker",
            ClientOrderId = "leg-tp-client",
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Type = OrderType.Limit,
            Quantity = 10m,
            LimitPrice = 210m,
            Status = OrderStatus.PendingNew
        },
        new BrokerOrder
        {
            OrderId = "leg-sl-broker",
            ClientOrderId = "leg-sl-client",
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            Type = OrderType.StopMarket,
            Quantity = 10m,
            StopPrice = 190m,
            Status = OrderStatus.PendingNew
        }
    ];

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The awaited condition did not become true within 5 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    /// <summary>
    /// Brokerage gateway double with a configurable broker-side open-order book, so the tests can
    /// stage the exact disagreement between the broker's book and the OMS's in-memory dictionary
    /// that the union sweep exists to resolve.
    /// </summary>
    private sealed class UnionSweepBrokerageGateway :
        IBrokerageGateway,
        IExecutionGatewayModeProvider,
        IExplicitOrderCancellationGateway
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        private readonly List<string> _cancelRequests = new();
        private readonly List<OrderCancellationIdentifier> _cancellationIdentifiers = new();
        private int _openOrdersReadCount;

        public List<BrokerOrder> BrokerOpenOrders { get; } = new();

        public Exception? OpenOrdersFailure { get; init; }

        public IReadOnlyList<BrokerOrder>? ChildOrdersOnSubmit { get; init; }

        public BrokerOrder? BrokerOrderAppearingOnVerification { get; set; }

        /// <summary>Order ids this double refuses to cancel, standing in for a broker that says no.</summary>
        public HashSet<string> RefuseToCancel { get; } = new(StringComparer.Ordinal);

        public HashSet<string> RemainPendingAfterCancel { get; } = new(StringComparer.Ordinal);

        public HashSet<string> FillDuringCancel { get; } = new(StringComparer.Ordinal);

        public HashSet<string> TerminalRejectDuringCancel { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, decimal> PartialFillDuringCancel { get; } = new(StringComparer.Ordinal);

        public int OpenOrdersReadCount => Volatile.Read(ref _openOrdersReadCount);

        public IReadOnlyList<string> CancelRequests
        {
            get
            {
                lock (_cancelRequests)
                {
                    return _cancelRequests.ToList();
                }
            }
        }

        public IReadOnlyList<OrderCancellationIdentifier> CancellationIdentifiers
        {
            get
            {
                lock (_cancellationIdentifiers)
                {
                    return _cancellationIdentifiers.ToList();
                }
            }
        }

        public string GatewayId => "test-broker";

        public string BrokerDisplayName => "Test Broker";

        public ExecutionMode ExecutionMode => ExecutionMode.Paper;

        public bool IsConnected => true;

        public BrokerageCapabilities BrokerageCapabilities { get; } = BrokerageCapabilities.UsEquity();

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
                Timestamp = DateTimeOffset.UtcNow,
                ChildOrders = ChildOrdersOnSubmit
            });

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) =>
            CancelOrderAsync(
                new OrderCancellationIdentifier(orderId, OrderCancellationIdentifierKind.BrokerOrderId),
                ct);

        public Task<ExecutionReport> CancelOrderAsync(
            OrderCancellationIdentifier identifier,
            CancellationToken ct = default)
        {
            lock (_cancelRequests)
            {
                _cancelRequests.Add(identifier.Value);
            }
            lock (_cancellationIdentifiers)
            {
                _cancellationIdentifiers.Add(identifier);
            }

            BrokerOrder? brokerOrder;
            lock (BrokerOpenOrders)
            {
                brokerOrder = BrokerOpenOrders.FirstOrDefault(order => identifier.Kind switch
                {
                    OrderCancellationIdentifierKind.BrokerOrderId => string.Equals(
                        order.OrderId,
                        identifier.Value,
                        StringComparison.Ordinal),
                    OrderCancellationIdentifierKind.ClientOrderId => string.Equals(
                        order.ClientOrderId,
                        identifier.Value,
                        StringComparison.Ordinal),
                    _ => false
                });
            }

            var brokerOrderId = brokerOrder?.OrderId ?? identifier.Value;
            var refused = RefuseToCancel.Contains(brokerOrderId);
            var remainsPending = RemainPendingAfterCancel.Contains(brokerOrderId);
            var filled = FillDuringCancel.Contains(brokerOrderId);
            var terminalRejected = TerminalRejectDuringCancel.Contains(brokerOrderId);
            var cancelledAfterPartialFill = PartialFillDuringCancel.TryGetValue(
                brokerOrderId,
                out var partialFillQuantity);
            if (!refused && !remainsPending)
            {
                lock (BrokerOpenOrders)
                {
                    BrokerOpenOrders.RemoveAll(order => string.Equals(
                        order.OrderId,
                        brokerOrderId,
                        StringComparison.Ordinal));
                }
            }

            return Task.FromResult(new ExecutionReport
            {
                OrderId = brokerOrderId,
                ClientOrderId = brokerOrder?.ClientOrderId
                    ?? (identifier.Kind is OrderCancellationIdentifierKind.ClientOrderId
                        ? identifier.Value
                        : null),
                GatewayOrderId = brokerOrderId,
                ReportType = filled
                    ? ExecutionReportType.Fill
                    : terminalRejected || refused || remainsPending
                        ? ExecutionReportType.Rejected
                        : ExecutionReportType.Cancelled,
                Symbol = brokerOrder?.Symbol ?? string.Empty,
                Side = brokerOrder?.Side ?? OrderSide.Buy,
                OrderStatus = filled
                    ? OrderStatus.Filled
                    : terminalRejected
                    ? OrderStatus.Rejected
                    : refused
                        ? brokerOrder?.Status ?? OrderStatus.Accepted
                    : remainsPending
                        ? OrderStatus.PendingCancel
                        : OrderStatus.Cancelled,
                OrderQuantity = brokerOrder?.Quantity ?? 0m,
                FilledQuantity = filled
                    ? brokerOrder?.Quantity ?? 0m
                    : cancelledAfterPartialFill
                        ? partialFillQuantity
                        : brokerOrder?.FilledQuantity ?? 0m,
                FillPrice = filled || cancelledAfterPartialFill ? 151.25m : null,
                RejectReason = filled
                    ? "Broker order filled before cancellation completed."
                    : terminalRejected
                    ? "Broker order became terminal as Rejected before cancellation completed."
                    : refused
                    ? "Broker refused the cancellation."
                    : remainsPending
                        ? "Broker cancellation remains pending."
                        : null,
                Timestamp = DateTimeOffset.UtcNow
            });
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

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) =>
            _reports.Reader.ReadAllAsync(ct);

        public ValueTask PublishReportAsync(ExecutionReport report) => _reports.Writer.WriteAsync(report);

        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new AccountInfo { AccountId = "acct-1" });

        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BrokerPosition>>([]);

        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default)
        {
            var readCount = Interlocked.Increment(ref _openOrdersReadCount);
            if (OpenOrdersFailure is { } failure)
            {
                return Task.FromException<IReadOnlyList<BrokerOrder>>(failure);
            }

            List<BrokerOrder> snapshot;
            lock (BrokerOpenOrders)
            {
                snapshot = BrokerOpenOrders.ToList();
            }

            if (readCount >= 2 && BrokerOrderAppearingOnVerification is { } appearing
                && snapshot.All(order => !string.Equals(
                    order.OrderId,
                    appearing.OrderId,
                    StringComparison.Ordinal)))
            {
                snapshot.Add(appearing);
            }

            return Task.FromResult<IReadOnlyList<BrokerOrder>>(snapshot);
        }

        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(BrokerHealthStatus.Healthy());

        public ValueTask DisposeAsync()
        {
            _reports.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
