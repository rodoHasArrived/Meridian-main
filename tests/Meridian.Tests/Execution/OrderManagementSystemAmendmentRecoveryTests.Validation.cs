using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed partial class OrderManagementSystemAmendmentRecoveryTests
{
    [Fact]
    public async Task PendingAmendment_ReservesTheOutcomeWithTheLargerRemainingExposure()
    {
        var gateway = new ScriptedAmendmentGateway { SubmitFill = 90m };
        gateway.Modify = (_, _) => Task.FromResult(gateway.Report(ExecutionReportType.ModificationPending,
            OrderStatus.PartiallyFilled, 90m) with
        {
            OrderQuantity = 200m,
            CandidateGatewayOrderId = "candidate",
            RejectReason = "Replacement acknowledgement timed out; reconcile before retrying."
        });
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            portfolioState: new PaperTradingPortfolio(1_000_000m));
        (await oms.PlaceOrderAsync(LimitOrder(100m, 100m))).Success.Should().BeTrue();

        (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 200m, NewLimitPrice = 40m }))
            .Success.Should().BeFalse();

        // Original leaves 10 @ $100 ($1,000) working; the proposal leaves 110 @ $40 ($4,400).
        var reserved = oms.GetExposureReservingOrders().Single();
        reserved.Quantity.Should().Be(200m);
        reserved.LimitPrice.Should().Be(40m);
    }

    [Fact]
    public async Task InvalidAmendment_IsRejectedBeforeAnythingIsPersisted()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"amendment-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "alpaca");
            store.BindScope("paper:account");
            var gateway = new ScriptedAmendmentGateway();
            await using (var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: new PaperTradingPortfolio(100_000m)))
            {
                (await oms.PlaceOrderAsync(LimitOrder(10m, 100m))).Success.Should().BeTrue();

                var rejected = await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 0m });

                rejected.Success.Should().BeFalse();
                gateway.ModifyCalls.Should().Be(0);
                store.Load().Single().PendingAmendment.Should().BeNull();
                store.Load().Single().RequiresRecovery.Should().BeFalse();
            }

            var restarted = new FileBrokerageOrderRecoveryStore(path, "alpaca");
            restarted.BindScope("paper:account");
            restarted.Load().Single().State.Quantity.Should().Be(10m);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GatewayFailureBeforeSend_ReleasesReservationsWithoutRecovery()
    {
        var gateway = new ScriptedAmendmentGateway
        {
            Modify = (_, _) => throw new BrokerageRequestNotSentException("identity unresolved")
        };
        await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
            portfolioState: new PaperTradingPortfolio(100_000m));
        (await oms.PlaceOrderAsync(LimitOrder(10m, 100m))).Success.Should().BeTrue();

        var result = await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("identity unresolved");
        oms.GetExposureReservingOrders().Single().Quantity.Should().Be(10m);
        gateway.Modify = (_, _) => throw new HttpRequestException("connection reset");
        await Assert.ThrowsAsync<HttpRequestException>(() => oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m }));
        oms.GetExposureReservingOrders().Single().Quantity.Should().Be(20m);
    }

    private static OrderRequest LimitOrder(decimal quantity, decimal limit) => new()
    {
        ClientOrderId = "local",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = quantity,
        LimitPrice = limit
    };

    private sealed class ScriptedAmendmentGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        public decimal SubmitFill { get; init; }
        public int ModifyCalls { get; private set; }
        public Func<string, OrderModification, Task<ExecutionReport>>? Modify { get; set; }
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        public string GatewayId => "alpaca";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(SubmitFill > 0m
                ? Report(ExecutionReportType.PartialFill, OrderStatus.PartiallyFilled, SubmitFill) with { OrderQuantity = request.Quantity }
                : Report(ExecutionReportType.New, OrderStatus.Accepted, 0m) with { OrderQuantity = request.Quantity });
        public Task<ExecutionReport> ModifyOrderAsync(string id, OrderModification modification, CancellationToken ct = default)
        {
            ModifyCalls++;
            return Modify?.Invoke(id, modification) ?? throw new InvalidOperationException("unexpected modify");
        }
        public Task<ExecutionReport> CancelOrderAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(Report(ExecutionReportType.CancelRejected, OrderStatus.Accepted, 0m));
        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var report in _reports.Reader.ReadAllAsync(ct))
                yield return report;
        }
        public ExecutionReport Report(ExecutionReportType type, OrderStatus status, decimal filled) => new()
        {
            OrderId = "broker",
            GatewayOrderId = "broker",
            ClientOrderId = "local",
            ReportType = type,
            OrderStatus = status,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderQuantity = 10m,
            FilledQuantity = filled,
            FillPrice = filled > 0m ? 100m : null
        };
    }
}
