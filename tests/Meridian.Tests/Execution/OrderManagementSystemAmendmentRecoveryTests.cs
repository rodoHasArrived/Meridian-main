using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementSystemAmendmentRecoveryTests
{
    [Fact]
    public async Task PendingAmendment_RetainsLargerExposureAndRecovery_UntilReplacementIsAuthoritativelyReconciled()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"amendment-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "orders.json");
            var store = new FileBrokerageOrderRecoveryStore(path, "alpaca");
            store.BindScope("paper:account");
            var gateway = new PendingAmendmentGateway();
            await using var oms = new OrderManagementSystem(gateway, NullLogger<OrderManagementSystem>.Instance,
                recoveryStore: store, portfolioState: new PaperTradingPortfolio(100_000m));
            (await oms.PlaceOrderAsync(new OrderRequest
            {
                ClientOrderId = "local",
                Symbol = "AAPL",
                Side = OrderSide.Buy,
                Type = OrderType.Limit,
                Quantity = 10m,
                LimitPrice = 100m
            })).Success.Should().BeTrue();

            var pending = await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m });
            pending.Success.Should().BeFalse();
            pending.OrderState!.Quantity.Should().Be(20m);
            store.Load().Single().RequiresRecovery.Should().BeTrue();
            store.Load().Single().State.Quantity.Should().Be(20m);
            store.Load().Single().State.FilledQuantity.Should().Be(2m);

            await gateway.Reports.Writer.WriteAsync(gateway.PendingReport);
            (await oms.CancelOrderAsync("local")).Success.Should().BeFalse();
            await gateway.Reports.Writer.WriteAsync(PendingAmendmentGateway.Report(ExecutionReportType.PartialFill,
                OrderStatus.PartiallyFilled, 4m));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var delivered = 0m;
            while (delivered < 4m)
                delivered += (await oms.ExecutionReports.ReadAsync(timeout.Token)).FilledQuantity;
            while (store.Load().Single().State.FilledQuantity < 4m)
                await Task.Delay(10, timeout.Token);

            oms.GetOrder("local")!.Quantity.Should().Be(20m);
            var retained = new FileBrokerageOrderRecoveryStore(path, "alpaca").Load().Single();
            retained.RequiresRecovery.Should().BeTrue();
            retained.RequiresAmendmentRecovery.Should().BeTrue();
            retained.State.FilledQuantity.Should().Be(4m);

            var oldOrder = PendingAmendmentGateway.Report(ExecutionReportType.Cancelled, OrderStatus.Cancelled, 4m);
            var reconcileOld = () => oms.ReconcileRecoveryOrderAsync(oldOrder);
            await reconcileOld.Should().ThrowAsync<InvalidDataException>();
            store.Load().Single().RequiresRecovery.Should().BeTrue();

            await oms.ReconcileRecoveryOrderAsync(oldOrder with
            {
                GatewayOrderId = "replacement-broker",
                ReplacesOrderId = "original-broker",
                ReportType = ExecutionReportType.PartialFill,
                OrderStatus = OrderStatus.PartiallyFilled,
                OrderQuantity = 20m
            });
            store.Load().Single().RequiresRecovery.Should().BeFalse();
            store.Load().Single().RequiresAmendmentRecovery.Should().BeFalse();
            oms.GetOrder("local")!.FilledQuantity.Should().Be(4m);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class PendingAmendmentGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        public Channel<ExecutionReport> Reports { get; } = Channel.CreateUnbounded<ExecutionReport>();
        public ExecutionReport PendingReport => Report(ExecutionReportType.ModificationPending, OrderStatus.PendingNew, 0m) with
        { OrderQuantity = 20m, RejectReason = "Replacement acknowledgement timed out; reconcile before retrying." };
        public string GatewayId => "alpaca";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(Report(ExecutionReportType.PartialFill, OrderStatus.PartiallyFilled, 2m));
        public Task<ExecutionReport> ModifyOrderAsync(string id, OrderModification modification, CancellationToken ct = default) =>
            Task.FromResult(PendingReport);
        public Task<ExecutionReport> CancelOrderAsync(string id, CancellationToken ct = default) => Task.FromResult(
            Report(ExecutionReportType.CancelRejected, OrderStatus.PartiallyFilled, 3m));
        public async IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var report in Reports.Reader.ReadAllAsync(ct))
                yield return report;
        }
        public static ExecutionReport Report(ExecutionReportType type, OrderStatus status, decimal filled) => new()
        {
            OrderId = "original-broker",
            GatewayOrderId = "original-broker",
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
