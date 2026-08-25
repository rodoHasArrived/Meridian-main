using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
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

        // The broker book holds the tracked order twice over (once keyed by client order id, once
        // by its own id) plus one order the OMS has never heard of — a post-restart survivor.
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
            OrderId = placed.OrderId,
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

        gateway.CancelRequests.Should().ContainSingle(id => id == placed.OrderId,
            "an order present in both views is cancelled once, through the tracked path");
        gateway.CancelRequests.Should().Contain("broker-orphan-1",
            "a broker-known order the in-memory book does not track must still be cancelled");
        gateway.CancelRequests.Should().NotContain("broker-1",
            "the broker row keyed by client order id is the same order the tracked path cancelled");
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

        var sweep = await oms.CancelAllAsync();

        sweep.Outcome.Should().Be(KillSwitchSweepOutcome.Completed);
        sweep.Requested.Should().Be(3, "the parent and both bracket legs are all working orders");
        sweep.Cancelled.Should().Be(3);
        gateway.CancelRequests.Should().Contain(new[] { placed.OrderId, "leg-tp-client", "leg-sl-client" });
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
    private sealed class UnionSweepBrokerageGateway : IBrokerageGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        private readonly List<string> _cancelRequests = new();

        public List<BrokerOrder> BrokerOpenOrders { get; } = new();

        public Exception? OpenOrdersFailure { get; init; }

        public IReadOnlyList<BrokerOrder>? ChildOrdersOnSubmit { get; init; }

        /// <summary>Order ids this double refuses to cancel, standing in for a broker that says no.</summary>
        public HashSet<string> RefuseToCancel { get; } = new(StringComparer.Ordinal);

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

        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
        {
            lock (_cancelRequests)
            {
                _cancelRequests.Add(orderId);
            }

            var refused = RefuseToCancel.Contains(orderId);
            return Task.FromResult(new ExecutionReport
            {
                OrderId = orderId,
                ReportType = refused ? ExecutionReportType.Rejected : ExecutionReportType.Cancelled,
                Symbol = string.Empty,
                Side = OrderSide.Buy,
                OrderStatus = refused ? OrderStatus.Rejected : OrderStatus.Cancelled,
                RejectReason = refused ? "Broker refused the cancellation." : null,
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

        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
            OpenOrdersFailure is { } failure
                ? Task.FromException<IReadOnlyList<BrokerOrder>>(failure)
                : Task.FromResult<IReadOnlyList<BrokerOrder>>(BrokerOpenOrders.ToList());

        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) =>
            Task.FromResult(BrokerHealthStatus.Healthy());

        public ValueTask DisposeAsync()
        {
            _reports.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
