using System.Collections.Concurrent;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class BrokerageExecutionAdmissionLimitTests
{
    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task RegisteredOms_EnforcesConfiguredNotionalBeforeBrokerDispatch(int notional, bool accepted)
    {
        await using var fixture = new Fixture(config => config.MaxOrderNotional = 100m);

        var result = await fixture.Oms.PlaceOrderAsync(Request("notional") with { Quantity = notional });

        result.Success.Should().Be(accepted);
        fixture.Broker.Submissions.Should().HaveCount(accepted ? 1 : 0);
        if (!accepted)
            result.ErrorMessage.Should().Contain("notional");
    }

    [Fact]
    public async Task RegisteredOms_RefusesUnmeasurableNotional()
    {
        await using var fixture = new Fixture(config => config.MaxOrderNotional = 100m);

        var result = await fixture.Oms.PlaceOrderAsync(Request("market") with { Type = OrderType.Market, LimitPrice = null });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Unable to evaluate order notional");
        fixture.Broker.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredOms_ValuesNativeNotionalAndOptionMultiplier()
    {
        await using var fixture = new Fixture(config => config.MaxOrderNotional = 100m);

        var nativeNotional = await fixture.Oms.PlaceOrderAsync(Request("native-notional") with
        {
            Metadata = new Dictionary<string, string> { ["runId"] = "run-limits", ["notional"] = "101" }
        });
        var option = await fixture.Oms.PlaceOrderAsync(Request("option") with
        {
            Quantity = 2m,
            OptionContract = new OptionContractIdentity { Multiplier = "100" }
        });

        nativeNotional.Success.Should().BeFalse();
        option.Success.Should().BeFalse();
        fixture.Broker.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredOms_UsesGatewayFaceValueSizingForNotional()
    {
        await using var fixture = new Fixture(config => config.MaxOrderNotional = 100m);

        var result = await fixture.Oms.PlaceOrderAsync(Request("face-value") with
        {
            Quantity = 100m,
            LimitPrice = 100m,
            Metadata = new Dictionary<string, string> { ["runId"] = "run-limits", ["asset_class"] = "fixed_income" }
        });

        result.Success.Should().BeTrue();
        fixture.Broker.Submissions.Should().ContainSingle();
    }

    [Fact]
    public async Task RegisteredOms_EnforcesBrokerOpenOrderCount()
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 1);
        fixture.Broker.OpenOrders = [BrokerOrder("external", "broker-external")];

        var result = await fixture.Oms.PlaceOrderAsync(Request("blocked-by-external"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Open order count");
        fixture.Broker.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredOms_DoesNotCountTrackedBrokerOrderTwice()
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 2);
        (await fixture.Oms.PlaceOrderAsync(Request("first"))).Success.Should().BeTrue();
        fixture.Broker.OpenOrders = [BrokerOrder("first", "broker-first")];

        var second = await fixture.Oms.PlaceOrderAsync(Request("second"));

        second.Success.Should().BeTrue();
        fixture.Broker.Submissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task RegisteredOms_DuplicateClientIdsAtDifferentBrokerOrdersStillConsumeCapacity()
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 2);
        (await fixture.Oms.PlaceOrderAsync(Request("first"))).Success.Should().BeTrue();
        fixture.Broker.OpenOrders = [BrokerOrder("first", "broker-first"), BrokerOrder("first", "other-broker-order")];

        var second = await fixture.Oms.PlaceOrderAsync(Request("second"));

        second.Success.Should().BeFalse();
        second.ErrorMessage.Should().Contain("Open order count");
        fixture.Broker.Submissions.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisteredOms_RetainedReplacementAliases_OverlapOnlyOneLocalSlot(bool includeCurrentBrokerRow)
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 2);
        (await fixture.Oms.PlaceOrderAsync(Request("first"))).Success.Should().BeTrue();
        fixture.Broker.ModificationResponse = new ExecutionReport
        {
            OrderId = "broker-first-replacement",
            GatewayOrderId = "broker-first-replacement",
            ClientOrderId = "replacement-client",
            ReplacesOrderId = "broker-first",
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            ReportType = ExecutionReportType.Modified,
            OrderStatus = OrderStatus.Accepted,
            OrderQuantity = 1m
        };
        (await fixture.Oms.ModifyOrderAsync("first", new OrderModification { NewLimitPrice = 1m })).Success.Should().BeTrue();
        fixture.Broker.OpenOrders = includeCurrentBrokerRow
            ? [BrokerOrder("first", "broker-first"), BrokerOrder("replacement-client", "broker-first-replacement")]
            : [BrokerOrder("first", "broker-first")];

        var second = await fixture.Oms.PlaceOrderAsync(Request("second"));

        second.Success.Should().Be(!includeCurrentBrokerRow,
            "a stale predecessor overlaps its working replacement, while two live broker rows consume two slots");
        fixture.Broker.Submissions.Should().HaveCount(includeCurrentBrokerRow ? 1 : 2);
        if (includeCurrentBrokerRow)
            second.ErrorMessage.Should().Contain("Open order count");
    }

    [Fact]
    public async Task RegisteredOms_ReservesOpenOrderSlotWhileBrokerAcknowledgementIsPending()
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 1);
        fixture.Broker.HoldSubmissions = true;
        var first = fixture.Oms.PlaceOrderAsync(Request("pending-ack"));
        await fixture.Broker.SubmissionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var competing = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
                fixture.Oms.PlaceOrderAsync(Request($"competing-{index}")))).WaitAsync(TimeSpan.FromSeconds(5));

            competing.Should().OnlyContain(result => !result.Success && result.ErrorMessage!.Contains("Open order count"));
            fixture.Broker.Submissions.Should().ContainSingle();
        }
        finally
        {
            fixture.Broker.ReleaseSubmissions.TrySetResult();
            await first;
        }
        (await first).Success.Should().BeTrue();
    }

    [Fact]
    public async Task RegisteredOms_AmbiguousDispatchKeepsOpenOrderCapacityReserved()
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 1);
        fixture.Broker.FailSubmission = true;
        (await fixture.Oms.PlaceOrderAsync(Request("uncertain"))).Success.Should().BeFalse();

        var retry = await fixture.Oms.PlaceOrderAsync(Request("new-order"));

        retry.Success.Should().BeFalse();
        retry.ErrorMessage.Should().Contain("Open order count");
        fixture.Broker.Submissions.Should().ContainSingle();
    }

    [Fact]
    public async Task RegisteredOms_DefinitiveRejectionReleasesOpenOrderCapacity()
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 1);
        fixture.Broker.SubmissionStatus = OrderStatus.Rejected;
        (await fixture.Oms.PlaceOrderAsync(Request("rejected"))).Success.Should().BeFalse();
        fixture.Broker.SubmissionStatus = OrderStatus.Accepted;

        var accepted = await fixture.Oms.PlaceOrderAsync(Request("accepted"));

        accepted.Success.Should().BeTrue();
        fixture.Broker.Submissions.Should().HaveCount(2);
    }

    [Fact]
    public async Task RegisteredOms_UnavailableBrokerSnapshotFailsClosed()
    {
        await using var fixture = new Fixture(config => config.MaxOpenOrders = 1);
        fixture.Broker.FailOpenOrderQuery = true;

        var result = await fixture.Oms.PlaceOrderAsync(Request("snapshot-failure"));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("snapshot is unavailable");
        fixture.Broker.Submissions.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredOms_AmendmentCannotBypassConfiguredNotional()
    {
        await using var fixture = new Fixture(config => config.MaxOrderNotional = 100m);
        (await fixture.Oms.PlaceOrderAsync(Request("amended") with { Quantity = 100m })).Success.Should().BeTrue();

        var result = await fixture.Oms.ModifyOrderAsync("amended", new OrderModification { NewQuantity = 101m });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("notional");
        fixture.Oms.GetOrder("amended")!.Quantity.Should().Be(100m);
        fixture.Broker.ModifyCount.Should().Be(0);
    }

    private static OrderRequest Request(string id) => new()
    {
        ClientOrderId = id,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 1m,
        LimitPrice = 1m,
        FundAccountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        Metadata = new Dictionary<string, string> { ["runId"] = "run-limits" }
    };

    private static BrokerOrder BrokerOrder(string clientId, string brokerId) => new()
    {
        ClientOrderId = clientId,
        OrderId = brokerId,
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Status = OrderStatus.Accepted,
        Quantity = 1m
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "brokerage-admission", Guid.NewGuid().ToString("N"));
        private readonly ServiceProvider _provider;
        public RecordingBroker Broker { get; } = new();
        public IOrderManager Oms { get; }

        public Fixture(Action<BrokerageConfiguration> configure)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddBrokerageGateway(Broker.GatewayId, _ => Broker);
            services.AddSingleton<ILiveOrderReadinessGate>(new ApprovedReadinessGate());
            services.AddSingleton(new FileBrokerageOrderRecoveryStore(Path.Combine(_root, "orders.json"), Broker.GatewayId));
            services.AddBrokerageExecution(config =>
            {
                config.Gateway = Broker.GatewayId;
                config.LiveExecutionEnabled = true;
                config.ProductionRoutingPhaseEnabled = true;
                config.ReadOnlyVerificationPassed = true;
                config.PaperLifecycleTestsPassed = true;
                config.ReplayEvidencePassed = true;
                config.ValidationGates.RequireValidationArtifactsForOrderPlacement = false;
                config.BrokerFlows[Broker.GatewayId] = new BrokerFlowFlags { ProductionOrderRoutingEnabled = true };
                configure(config);
            });
            _provider = services.BuildServiceProvider();
            Oms = _provider.GetRequiredService<IOrderManager>();
            ((IOrderManagementLifecycle)Oms).StartAsync().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            Broker.ReleaseSubmissions.TrySetResult();
            await _provider.DisposeAsync();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ApprovedReadinessGate : ILiveOrderReadinessGate
    {
        public Task<LiveOrderReadinessDecision> EvaluateAsync(LiveOrderReadinessRequest request, CancellationToken ct = default) =>
            Task.FromResult(LiveOrderReadinessDecision.Approved("retained/readiness/limits"));
    }

    private sealed class RecordingBroker : IBrokerageGateway, IExecutionGatewayModeProvider, IBrokerageConnectionState,
        INotionalOrderSizingGateway, IFaceValueOrderSizingGateway
    {
        private readonly Channel<ExecutionReport> _reports = Channel.CreateUnbounded<ExecutionReport>();
        public ConcurrentQueue<OrderRequest> Submissions { get; } = new();
        public TaskCompletionSource SubmissionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSubmissions { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<BrokerOrder> OpenOrders { get; set; } = [];
        public bool HoldSubmissions { get; set; }
        public bool FailSubmission { get; set; }
        public bool FailOpenOrderQuery { get; set; }
        public OrderStatus SubmissionStatus { get; set; } = OrderStatus.Accepted;
        public int ModifyCount { get; private set; }
        public ExecutionReport? ModificationResponse { get; set; }
        public string GatewayId => "alpaca";
        public string BrokerDisplayName => "Recording admission broker";
        public BrokerageCapabilities BrokerageCapabilities => BrokerageCapabilities.UsEquity();
        public bool IsConnected => true;
        public ExecutionMode ExecutionMode => ExecutionMode.Live;
        public long ConnectionGeneration => 1;
        public bool IsExecutionStreamHealthy => true;
        public string ScopeIdentity => "alpaca|live|account-admission";

        public bool RoutesNotionalMetadata(OrderRequest request) => !UsesFaceValuePercentageOfPar(request);
        public bool UsesFaceValuePercentageOfPar(OrderRequest request) =>
            request.Metadata?.GetValueOrDefault("asset_class") == "fixed_income";
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public async Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default)
        {
            Submissions.Enqueue(request);
            SubmissionEntered.TrySetResult();
            if (HoldSubmissions)
                await ReleaseSubmissions.Task.WaitAsync(ct);
            if (FailSubmission)
                throw new IOException("Acknowledgement lost after dispatch.");
            return new ExecutionReport
            {
                OrderId = $"broker-{request.ClientOrderId}",
                GatewayOrderId = $"broker-{request.ClientOrderId}",
                ClientOrderId = request.ClientOrderId,
                Symbol = request.Symbol,
                Side = request.Side,
                ReportType = ExecutionReportType.New,
                OrderStatus = SubmissionStatus,
                OrderQuantity = request.Quantity
            };
        }

        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) => _reports.Reader.ReadAllAsync(ct);
        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default)
        {
            ModifyCount++;
            return Task.FromResult(ModificationResponse ?? throw new NotSupportedException());
        }
        public Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrokerPosition>> GetPositionsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrokerOrder>> GetOpenOrdersAsync(CancellationToken ct = default) =>
            FailOpenOrderQuery
                ? Task.FromException<IReadOnlyList<BrokerOrder>>(new IOException("Snapshot unavailable."))
                : Task.FromResult(OpenOrders);
        public Task<BrokerHealthStatus> CheckHealthAsync(CancellationToken ct = default) => Task.FromResult(BrokerHealthStatus.Healthy());
    }
}
