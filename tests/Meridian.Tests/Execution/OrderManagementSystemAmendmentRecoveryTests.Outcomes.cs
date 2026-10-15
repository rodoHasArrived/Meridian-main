using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed partial class OrderManagementSystemAmendmentRecoveryTests
{
    [Theory]
    [InlineData(8, true)]
    [InlineData(8, false)]
    [InlineData(20, true)]
    [InlineData(20, false)]
    public async Task RestartRecovery_SelectsOriginalOrProposedQuantityFromAuthoritativeEvidence(int proposed, bool accepted)
    {
        await VerifyRestartOutcomeAsync(new OrderModification { NewQuantity = proposed }, accepted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RestartRecovery_PriceOnlyAmendmentRestoresExactConfirmedTerms(bool accepted)
    {
        await VerifyRestartOutcomeAsync(new OrderModification { NewLimitPrice = 200m }, accepted);
    }

    private static async Task VerifyRestartOutcomeAsync(OrderModification modification, bool accepted)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"amendment-outcome-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "orders.json");
            var originalGateway = new RecoverableAmendmentGateway();
            await using (var oms = CreateRecoveryOms(originalGateway, path))
            {
                (await oms.PlaceOrderAsync(RecoveryRequest())).Success.Should().BeTrue();
                (await oms.ModifyOrderAsync("local", modification)).Success.Should().BeFalse();
                oms.GetOrder("local")!.Quantity.Should().Be(10m, "pending terms are not confirmed order terms");
                oms.GetOrder("local")!.LimitPrice.Should().Be(100m);
                var exposure = oms.GetExposureReservingOrders().Single();
                exposure.Quantity.Should().Be(Math.Max(10m, modification.NewQuantity ?? 10m));
                exposure.LimitPrice.Should().Be(Math.Max(100m, modification.NewLimitPrice ?? 100m));
            }

            var store = new FileBrokerageOrderRecoveryStore(path, "alpaca");
            var retained = store.Load().Single();
            retained.PendingAmendment!.Original.Quantity.Should().Be(10m);
            retained.PendingAmendment.Proposed.Quantity.Should().Be(modification.NewQuantity ?? 10m);
            retained.PendingAmendment.CandidateClientOrderId.Should().Be("attempted-ref");
            var expectedQuantity = accepted ? modification.NewQuantity ?? 10m : 10m;
            var expectedPrice = accepted ? modification.NewLimitPrice ?? 100m : 100m;
            var recoveredGateway = new RecoverableAmendmentGateway
            {
                RecoveryReport = RecoverableAmendmentGateway.Report(
                    accepted ? ExecutionReportType.Modified : ExecutionReportType.ModificationRejected,
                    OrderStatus.PartiallyFilled, 2m, expectedQuantity, expectedPrice) with
                {
                    GatewayOrderId = accepted ? "candidate-broker" : "original-broker",
                    ReplacesOrderId = accepted ? "original-broker" : null
                }
            };
            await using var restartedOms = CreateRecoveryOms(recoveredGateway, path);
            var recovery = new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance);
            if (accepted && modification.NewLimitPrice is not null)
            {
                var insufficient = () => restartedOms.ReconcileRecoveryOrderAsync(recoveredGateway.RecoveryReport! with
                { HasOrderTerms = false, OrderLimitPrice = null });
                await insufficient.Should().ThrowAsync<InvalidDataException>();
                restartedOms.GetRecoveryOrders().Should().ContainSingle();
            }

            await recovery.RecoverOrdersAsync(recoveredGateway, restartedOms, fundAccountId: null);

            recoveredGateway.RecoveredContext!.CandidateClientOrderId.Should().Be("attempted-ref");
            recoveredGateway.RecoveredState!.FilledQuantity.Should().Be(2m);
            var actual = restartedOms.GetOrder("local")!;
            actual.Quantity.Should().Be(expectedQuantity);
            actual.LimitPrice.Should().Be(expectedPrice);
            actual.Status.Should().Be(OrderStatus.PartiallyFilled);
            restartedOms.GetExposureReservingOrders().Single().Quantity.Should().Be(expectedQuantity);
            store = new FileBrokerageOrderRecoveryStore(path, "alpaca");
            store.Load().Single().PendingAmendment.Should().BeNull();
            store.Load().Single().RequiresAmendmentRecovery.Should().BeFalse();
            store.Load().Single().RequiresRecovery.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LateAmendmentAcknowledgement_PreservesTerminalLifecycleAndSeparateExposure(bool cancellation, bool pending)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"amendment-terminal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var gateway = new RecoverableAmendmentGateway { HoldModification = true, ReturnPending = pending };
            var configuration = new BrokerageConfiguration
            {
                Gateway = "alpaca",
                MaxOpenOrders = 1,
                BrokerFlows = new() { ["alpaca"] = new BrokerFlowFlags { PaperOrderFlowEnabled = true } }
            };
            await using var oms = CreateRecoveryOms(gateway, Path.Combine(directory, "orders.json"), configuration);
            (await oms.PlaceOrderAsync(RecoveryRequest())).Success.Should().BeTrue();
            var amendment = oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m, NewLimitPrice = 200m });
            await gateway.ModificationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var terminal = cancellation ? OrderStatus.Cancelled : OrderStatus.Filled;
            await gateway.Reports.Writer.WriteAsync(RecoverableAmendmentGateway.Report(
                cancellation ? ExecutionReportType.Cancelled : ExecutionReportType.Fill,
                terminal, cancellation ? 2m : 10m, 10m, 100m));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (oms.GetOrder("local")!.Status != terminal)
                await Task.Delay(10, timeout.Token);

            gateway.ReleaseModification.TrySetResult();
            (await amendment).Success.Should().BeFalse();

            oms.GetOrder("local")!.Status.Should().Be(terminal);
            oms.GetOrder("local")!.Quantity.Should().Be(10m);
            oms.GetOrder("local")!.LimitPrice.Should().Be(100m);
            var reservation = oms.GetExposureReservingOrders().Single();
            reservation.Quantity.Should().Be(20m);
            reservation.LimitPrice.Should().Be(200m);
            oms.GetOpenOrders().Should().BeEmpty();
            (await oms.PlaceOrderAsync(RecoveryRequest() with { ClientOrderId = "competing" })).Success.Should().BeFalse(
                "unresolved replacement exposure still consumes its open-order slot after the original becomes terminal");
            oms.GetOrder("local")!.Status.Should().Be(terminal);
            if (!cancellation)
            {
                await oms.ReconcileRecoveryOrderAsync(RecoverableAmendmentGateway.Report(ExecutionReportType.Modified,
                    OrderStatus.PartiallyFilled, 15m, 20m, 200m) with
                { GatewayOrderId = "candidate-broker", ReplacesOrderId = "original-broker" });
                oms.GetOrder("local")!.Status.Should().Be(OrderStatus.Filled);
                oms.GetOrder("local")!.FilledQuantity.Should().Be(15m);
                var remaining = oms.GetExposureReservingOrders().Single();
                remaining.Quantity.Should().Be(20m);
                remaining.FilledQuantity.Should().Be(15m);
                oms.GetRecoveryOrders().Should().ContainSingle("a working candidate still has five possible executions");

                await oms.ReconcileRecoveryOrderAsync(RecoverableAmendmentGateway.Report(ExecutionReportType.Cancelled,
                    OrderStatus.Cancelled, 15m, 20m, 200m) with
                { GatewayOrderId = "candidate-broker", ReplacesOrderId = "original-broker" });
                oms.GetOrder("local")!.Status.Should().Be(OrderStatus.Cancelled);
                oms.GetOrder("local")!.Quantity.Should().Be(20m);
                oms.GetOrder("local")!.FilledQuantity.Should().Be(15m);
                oms.GetExposureReservingOrders().Should().BeEmpty();
                oms.GetRecoveryOrders().Should().BeEmpty();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryService_DispatchesAmendmentOnlyCapability_AndLeavesUnsupportedOrdinaryRecoveryUnresolved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"amendment-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "orders.json");
            var inner = new RecoverableAmendmentGateway
            {
                RecoveryReport = RecoverableAmendmentGateway.Report(ExecutionReportType.Modified,
                    OrderStatus.PartiallyFilled, 2m, 20m, 100m) with
                { GatewayOrderId = "candidate-broker", ReplacesOrderId = "original-broker" }
            };
            var gateway = new AmendmentOnlyGateway(inner);
            var recovery = new BrokerageExecutionReconciliationService(NullLogger<BrokerageExecutionReconciliationService>.Instance);
            await using (var oms = CreateRecoveryOms(gateway, path))
            {
                (await oms.PlaceOrderAsync(RecoveryRequest())).Success.Should().BeTrue();
                (await oms.ModifyOrderAsync("local", new OrderModification { NewQuantity = 20m })).Success.Should().BeFalse();

                await recovery.RecoverOrdersAsync(gateway, oms, fundAccountId: null);

                inner.RecoveredContext.Should().NotBeNull();
                oms.GetOrder("local")!.Quantity.Should().Be(20m);
                oms.GetRecoveryOrders().Should().BeEmpty();
            }
            var freshInner = new RecoverableAmendmentGateway();
            var freshGateway = new AmendmentOnlyGateway(freshInner);
            await using var restarted = CreateRecoveryOms(freshGateway, path);
            restarted.GetRecoveryOrders().Should().ContainSingle();

            await recovery.RecoverOrdersAsync(freshGateway, restarted, fundAccountId: null);

            restarted.GetRecoveryOrders().Should().ContainSingle("ordinary recovery is unsupported and must remain unresolved");
            freshInner.RecoveredContext.Should().BeNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static OrderManagementSystem CreateRecoveryOms(IBrokerageGateway gateway, string path,
        BrokerageConfiguration? configuration = null) => new(gateway, NullLogger<OrderManagementSystem>.Instance,
            brokerageConfiguration: configuration, recoveryStore: new FileBrokerageOrderRecoveryStore(path, "alpaca"),
            portfolioState: new PaperTradingPortfolio(100_000m));

    private static OrderRequest RecoveryRequest() => new()
    {
        ClientOrderId = "local",
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m
    };

    private sealed class RecoverableAmendmentGateway : IBrokerageGateway, IExecutionGatewayModeProvider,
        IBrokerageOrderRecoveryGateway, IBrokerageOrderAmendmentRecoveryGateway, IBrokerageConnectionState
    {
        public Channel<ExecutionReport> Reports { get; } = Channel.CreateUnbounded<ExecutionReport>();
        public TaskCompletionSource ModificationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseModification { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldModification { get; init; }
        public bool ReturnPending { get; init; } = true;
        public ExecutionReport? RecoveryReport { get; init; }
        public PendingOrderAmendment? RecoveredContext { get; private set; }
        public OrderState? RecoveredState { get; private set; }
        public string GatewayId => "alpaca";
        public string BrokerDisplayName => "Recoverable amendment broker";
        public BrokerageCapabilities BrokerageCapabilities => BrokerageCapabilities.UsEquity();
        public ExecutionMode ExecutionMode => ExecutionMode.Paper;
        public bool IsConnected => true;
        public bool IsExecutionStreamHealthy => true;
        public string ScopeIdentity => "paper:account";
        public long ConnectionGeneration => 1;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) =>
            Task.FromResult(Report(ExecutionReportType.PartialFill, OrderStatus.PartiallyFilled, 2m, request.Quantity, request.LimitPrice));
        public Task<ExecutionReport> CancelOrderAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public async Task<ExecutionReport> ModifyOrderAsync(string id, OrderModification modification, CancellationToken ct = default)
        {
            ModificationEntered.TrySetResult();
            if (HoldModification)
                await ReleaseModification.Task.WaitAsync(ct);
            return Report(ReturnPending ? ExecutionReportType.ModificationPending : ExecutionReportType.Modified,
                OrderStatus.Accepted, 2m, modification.NewQuantity ?? 10m, modification.NewLimitPrice ?? 100m) with
            {
                HasOrderTerms = !ReturnPending,
                CandidateGatewayOrderId = "candidate-broker",
                CandidateClientOrderId = "attempted-ref"
            };
        }
        public Task<ExecutionReport?> GetOrderForRecoveryAsync(string id, CancellationToken ct = default) => throw new InvalidOperationException("Use retained amendment context.");
        public Task<ExecutionReport?> GetAmendmentForRecoveryAsync(string clientOrderId, OrderState retainedOrder,
            PendingOrderAmendment amendment, CancellationToken ct = default)
        {
            RecoveredContext = amendment;
            RecoveredState = retainedOrder;
            return Task.FromResult(RecoveryReport);
        }
        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) => Reports.Reader.ReadAllAsync(ct);
        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BrokerOrder>>([]);
        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BrokerPosition>>([]);
        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(BrokerHealthStatus.Healthy());
        public static ExecutionReport Report(ExecutionReportType type, OrderStatus status, decimal filled, decimal quantity, decimal? limit) => new()
        {
            OrderId = "original-broker",
            GatewayOrderId = "original-broker",
            ClientOrderId = "local",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = type,
            OrderStatus = status,
            OrderQuantity = quantity,
            FilledQuantity = filled,
            FillPrice = filled > 0m ? 100m : null,
            HasOrderTerms = true,
            OrderLimitPrice = limit,
            FillPriceIsCumulativeAverage = true
        };
    }

    private sealed class AmendmentOnlyGateway(RecoverableAmendmentGateway inner) : IBrokerageGateway,
        IExecutionGatewayModeProvider, IBrokerageConnectionState, IBrokerageOrderAmendmentRecoveryGateway
    {
        public string GatewayId => inner.GatewayId;
        public string BrokerDisplayName => inner.BrokerDisplayName;
        public BrokerageCapabilities BrokerageCapabilities => inner.BrokerageCapabilities;
        public ExecutionMode ExecutionMode => inner.ExecutionMode;
        public bool IsConnected => inner.IsConnected;
        public bool IsExecutionStreamHealthy => true;
        public string ScopeIdentity => inner.ScopeIdentity;
        public long ConnectionGeneration => 1;
        public ValueTask DisposeAsync() => inner.DisposeAsync();
        public Task ConnectAsync(CancellationToken ct = default) => inner.ConnectAsync(ct);
        public Task DisconnectAsync(CancellationToken ct = default) => inner.DisconnectAsync(ct);
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) => inner.SubmitOrderAsync(request, ct);
        public Task<ExecutionReport> ModifyOrderAsync(string id, OrderModification modification, CancellationToken ct = default) => inner.ModifyOrderAsync(id, modification, ct);
        public Task<ExecutionReport> CancelOrderAsync(string id, CancellationToken ct = default) => inner.CancelOrderAsync(id, ct);
        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) => inner.StreamExecutionReportsAsync(ct);
        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) => inner.GetAccountInfoAsync(ct);
        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) => inner.GetPositionsAsync(ct);
        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) => inner.GetOpenOrdersAsync(ct);
        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) => inner.CheckHealthAsync(ct);
        public Task<ExecutionReport?> GetAmendmentForRecoveryAsync(string id, OrderState retained, PendingOrderAmendment pending, CancellationToken ct = default) =>
            inner.GetAmendmentForRecoveryAsync(id, retained, pending, ct);
    }
}
