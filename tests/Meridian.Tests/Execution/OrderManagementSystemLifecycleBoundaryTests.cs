using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderManagementSystemLifecycleBoundaryTests : IDisposable
{
    private readonly string _controlsRoot = Path.Combine(Path.GetTempPath(), "meridian-lifecycle-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkingReductionProbe_QueriesCurrentBookAndShutdownClearsOnlyItsOwnBinding(bool replaceProbe)
    {
        var controls = new ExecutionOperatorControlService(new ExecutionOperatorControlOptions(_controlsRoot),
            NullLogger<ExecutionOperatorControlService>.Instance);
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(new ExecutionReport
        {
            OrderId = "initial-position",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = 10m,
            FilledQuantity = 10m,
            FillPrice = 100m,
            Timestamp = DateTimeOffset.UtcNow
        });
        await using var oms = new OrderManagementSystem(new WorkingOrderGateway(), NullLogger<OrderManagementSystem>.Instance,
            operatorControls: controls, portfolioState: portfolio);
        controls.WorkingReductionQuantityProbe.Should().BeNull("construction does not own live controls");

        await oms.StartAsync();
        var installedProbe = controls.WorkingReductionQuantityProbe!;
        installedProbe.Should().NotBeNull();
        installedProbe("AAPL", null).Should().Be(0m);
        var placement = await oms.PlaceOrderAsync(Request());
        placement.Success.Should().BeTrue();
        installedProbe("AAPL", null).Should().Be(4m,
            "the lifecycle binding must query the evolving working book rather than a startup snapshot");
        (await oms.CancelOrderAsync(placement.OrderId!)).Success.Should().BeTrue();
        installedProbe("AAPL", null).Should().Be(0m);

        Func<string, Guid?, decimal> replacement = (_, _) => 7m;
        if (replaceProbe)
            controls.WorkingReductionQuantityProbe = replacement;
        await oms.StopAsync();

        if (replaceProbe)
            controls.WorkingReductionQuantityProbe.Should().BeSameAs(replacement);
        else
            controls.WorkingReductionQuantityProbe.Should().BeNull();
    }

    [Fact]
    public async Task PreStartInspectionAndSubscription_RemainAvailableWhileRecoveryMutationsRequireStartup()
    {
        await using var oms = new OrderManagementSystem(new WorkingOrderGateway(), NullLogger<OrderManagementSystem>.Instance);
        await using var subscription = oms.SubscribeLosslessExecutionReports();
        oms.GetAllRetainedOrderStates().Should().BeEmpty();
        oms.GetRecoveryOrders().Should().BeEmpty();
        oms.GetOpenOrders().Should().BeEmpty();
        await Assert.ThrowsAsync<InvalidOperationException>(() => oms.GetAccountingHandoffFailuresAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => oms.ReconcileRecoveryOrderAsync(new ExecutionReport
        {
            OrderId = "unknown",
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            ReportType = ExecutionReportType.New,
            OrderStatus = OrderStatus.Accepted,
            Timestamp = DateTimeOffset.UtcNow
        }));

        await oms.StopAsync();
        await subscription.Reports.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await oms.ExecutionReports.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var startFailure = Assert.Throws<ObjectDisposedException>(() => { _ = oms.StartAsync(); });
        startFailure.ObjectName.Should().Be(typeof(OrderManagementSystem).FullName);
        var subscriptionFailure = Assert.Throws<ObjectDisposedException>(() => oms.SubscribeLosslessExecutionReports());
        subscriptionFailure.ObjectName.Should().Be(typeof(OrderManagementSystem).FullName);
        var operationFailure = await Assert.ThrowsAsync<ObjectDisposedException>(() => oms.PlaceOrderAsync(Request()));
        operationFailure.ObjectName.Should().Be(nameof(OrderManagementSystem));
        Assert.Throws<ArgumentOutOfRangeException>(() => oms.SubscribeLosslessExecutionReports(capacity: 0));
    }

    [Fact]
    public async Task CancelledRepeatStart_PreservesCancellationPrecedenceWithoutStoppingRunningInstance()
    {
        await using var oms = new OrderManagementSystem(new WorkingOrderGateway(), NullLogger<OrderManagementSystem>.Instance);
        await oms.StartAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => { _ = oms.StartAsync(cancellation.Token); });
        (await oms.PlaceOrderAsync(Request())).Success.Should().BeTrue();
        await oms.StopAsync();
        Assert.Throws<ObjectDisposedException>(() => { _ = oms.StartAsync(cancellation.Token); });
    }

    private static OrderRequest Request() => new()
    {
        ClientOrderId = "close-order",
        Symbol = "AAPL",
        Side = OrderSide.Sell,
        Type = OrderType.Limit,
        Quantity = 4m,
        LimitPrice = 100m
    };

    private sealed class WorkingOrderGateway : IExecutionGateway, IExecutionGatewayModeProvider
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        public string GatewayId => "lifecycle-boundary";
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
            => Task.FromResult(Report(request.ClientOrderId!, OrderStatus.Accepted, ExecutionReportType.New));
        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default)
            => Task.FromResult(Report(orderId, OrderStatus.Cancelled, ExecutionReportType.Cancelled));
        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
            => throw new NotSupportedException();
        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default)
            => _reports.Reader.ReadAllAsync(ct);

        private static ExecutionReport Report(string orderId, OrderStatus status, ExecutionReportType type) => new()
        {
            OrderId = orderId,
            ClientOrderId = orderId,
            Symbol = "AAPL",
            Side = OrderSide.Sell,
            ReportType = type,
            OrderStatus = status,
            OrderQuantity = 4m,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_controlsRoot))
            Directory.Delete(_controlsRoot, recursive: true);
    }
}
