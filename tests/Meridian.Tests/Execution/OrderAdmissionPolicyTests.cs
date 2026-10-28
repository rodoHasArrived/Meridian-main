using Meridian.Execution;
using Meridian.Execution.OrderManagement;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class OrderAdmissionPolicyTests : IDisposable
{
    private readonly string _controlsRoot = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task NotionalRefusal_PrecedesSizingPlacementAndExternalGates()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls) { RoutesNotional = false };
        var policy = CreatePolicy(gateway, calls);
        var request = Request() with
        {
            Metadata = new Dictionary<string, string> { ["notional"] = "100", ["runId"] = "run-1" }
        };

        var result = await policy.EvaluatePreflightAsync(policy.Prepare(request, "order-1"), default);

        Assert.Equal("notional metadata gate", result.Refusal?.Source);
        Assert.Equal(["notional"], calls);
    }

    [Fact]
    public async Task PlacementRefusal_PrecedesReadinessAndSecurityMaster()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls);
        var configuration = LiveConfiguration();
        configuration.ProductionRoutingPhaseEnabled = false;
        var policy = CreatePolicy(gateway, calls, configuration: configuration);

        var result = await policy.EvaluatePreflightAsync(policy.Prepare(Request(), "order-1"), default);

        Assert.Equal("execution placement gate", result.Refusal?.Source);
        Assert.Equal(["face-value"], calls);
    }

    [Fact]
    public async Task ReadinessRefusal_PrecedesControlsAndPreservesRejectionEvidence()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls);
        var controls = CreateControls();
        await controls.SetCircuitBreakerAsync(true, "Operator halt", "test");
        var readiness = new ReadinessGate(calls,
            (_, _) => Task.FromResult(new LiveOrderReadinessDecision(false, EvidenceReference: "audit://refusal")));
        var policy = CreatePolicy(gateway, calls, controls, readiness);

        var result = await policy.EvaluatePreflightAsync(policy.Prepare(Request(), "order-1"), default);

        Assert.Equal("live order readiness gate", result.Refusal?.Source);
        Assert.Equal("LIVE_ORDER_READINESS_REJECTED", result.Refusal?.ReasonCode);
        Assert.Equal("Live order readiness gate rejected the order.", result.Refusal?.Reason);
        Assert.Equal("audit://refusal", result.Refusal?.Metadata?["liveReadinessEvidenceReference"]);
        Assert.Null(result.OperatorControlDecision);
        Assert.Equal(["face-value", "readiness"], calls);
    }

    [Fact]
    public async Task Controls_RunAfterReadinessAndBeforeSecurityMaster()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls);
        var controls = CreateControls();
        var readiness = new ReadinessGate(calls, async (_, ct) =>
        {
            await controls.SetCircuitBreakerAsync(true, "Halted during readiness", "test", ct: ct);
            return LiveOrderReadinessDecision.Approved("audit://ready");
        });
        var policy = CreatePolicy(gateway, calls, controls, readiness);

        var result = await policy.EvaluatePreflightAsync(policy.Prepare(Request(), "order-1"), default);

        Assert.Equal("operator controls", result.Refusal?.Source);
        Assert.Equal("CIRCUIT_BREAKER_OPEN", result.Refusal?.ReasonCode);
        Assert.Equal("CIRCUIT_BREAKER_OPEN", result.Refusal?.Metadata?["rejectCode"]);
        Assert.Equal(["face-value", "readiness"], calls);
    }

    [Fact]
    public async Task SecurityMasterRefusal_RetainsApprovedReadinessEvidence()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls);
        var security = new SecurityGate(calls, new SecurityMasterGateResult(false, "Unknown instrument"));
        var policy = CreatePolicy(gateway, calls, security: security);

        var result = await policy.EvaluatePreflightAsync(policy.Prepare(Request(), "order-1"), default);

        Assert.Equal("Security Master gate", result.Refusal?.Source);
        Assert.Equal("Unknown instrument", result.Refusal?.Reason);
        Assert.Equal("audit://ready", result.LiveReadinessDecision?.EvidenceReference);
        Assert.Equal(["face-value", "readiness", "security"], calls);
    }

    [Fact]
    public async Task ApprovedReadiness_RequiresRetainedEvidence()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls);
        var readiness = new ReadinessGate(calls, (_, _) => Task.FromResult(LiveOrderReadinessDecision.Approved("  ")));
        var policy = CreatePolicy(gateway, calls, readiness: readiness);

        var result = await policy.EvaluatePreflightAsync(policy.Prepare(Request(), "order-1"), default);

        Assert.False(result.IsApproved);
        Assert.Equal("Live order readiness gate approved the order without a retained evidence reference.", result.Refusal?.Reason);
        Assert.Equal(["face-value", "readiness"], calls);
    }

    [Fact]
    public async Task MissingRunId_FailsBeforeCallingReadiness()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls);
        var policy = CreatePolicy(gateway, calls);

        var result = await policy.EvaluatePreflightAsync(policy.Prepare(Request() with { Metadata = null }, "order-1"), default);

        Assert.Equal("live order readiness gate", result.Refusal?.Source);
        Assert.Equal(["face-value"], calls);
    }

    [Fact]
    public async Task SafeAndRiskRequests_RetainSnapshotsAcrossAwaitedReadiness()
    {
        var calls = new List<string>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new TestGateway(ExecutionMode.Live, calls) { FaceValue = true };
        var readiness = new ReadinessGate(calls, async (_, ct) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return LiveOrderReadinessDecision.Approved("  audit://snapshot  ");
        });
        var policy = CreatePolicy(gateway, calls, readiness: readiness);
        var metadata = new Dictionary<string, string>
        {
            ["runId"] = " run-1 ",
            ["actor"] = "operator",
            ["asset_class"] = "us_treasury"
        };
        var legs = new List<OrderLeg> { new() { Symbol = "LEG-1", Side = OrderSide.Buy, RatioQuantity = 1m } };
        var prepared = policy.Prepare(Request() with { Metadata = metadata, Legs = legs }, "generated-1");
        var pending = policy.EvaluatePreflightAsync(prepared, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        metadata["asset_class"] = "us_equity";
        legs[0] = legs[0] with { RatioQuantity = 1000m };
        release.TrySetResult();
        var decision = await pending;

        Assert.True(decision.IsApproved);
        Assert.Equal("generated-1", prepared.SafeRequest.ClientOrderId);
        Assert.Equal("us_treasury", prepared.SafeRequest.Metadata?["asset_class"]);
        Assert.Equal("us_treasury", decision.RiskRequest.Metadata?["asset_class"]);
        Assert.Equal(1m, Assert.Single(prepared.SafeRequest.Legs!).RatioQuantity);
        Assert.Equal(1m, Assert.Single(decision.RiskRequest.Legs!).RatioQuantity);
        Assert.False(OrderSizingMetadata.UsesFaceValuePercentageOfPar(prepared.SafeRequest.Metadata));
        Assert.True(OrderSizingMetadata.UsesFaceValuePercentageOfPar(decision.RiskRequest.Metadata));
        Assert.Equal("run-1", readiness.LastRequest?.RunId);
        Assert.Equal("audit://snapshot", decision.LiveReadinessDecision?.EvidenceReference);
    }

    [Fact]
    public async Task CallerInternalRiskKeys_AreRemovedBeforeCapabilityAndRiskEvaluation()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Paper, calls);
        var policy = CreatePolicy(gateway, calls);
        var request = Request() with
        {
            Metadata = new Dictionary<string, string>
            {
                [RiskEscalationQueueService.EvaluationOnlyMetadataKey.ToUpperInvariant()] = "true",
                [RiskEscalationQueueService.IncrementalNotionalMetadataKey] = "0",
                [OrderSizingMetadata.FaceValuePercentageOfParKey] = "true",
                ["broker_account_id"] = "caller-account"
            }
        };
        var prepared = policy.Prepare(request, "order-1");
        var result = await policy.EvaluatePreflightAsync(prepared, default);

        Assert.True(result.IsApproved);
        Assert.Empty(prepared.SafeRequest.Metadata!);
        Assert.Empty(result.RiskRequest.Metadata!);
        Assert.False(result.UsesFaceValuePercentageOfPar);
    }

    [Theory]
    [InlineData(ExecutionMode.Paper, true)]
    [InlineData(ExecutionMode.Live, false)]
    public async Task PaperOverridesRemainAvailable_WhileLiveUsesSanitizedControls(ExecutionMode mode, bool approved)
    {
        var calls = new List<string>();
        var gateway = new TestGateway(mode, calls);
        var controls = CreateControls();
        await controls.SetDefaultPositionLimitAsync(1m, "test", "test limit");
        var bypass = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            ExecutionManualOverrideKinds.BypassOrderControls, "Approved override", "test", RunId: "run-1"));
        var policy = CreatePolicy(gateway, calls, controls);
        var metadata = new Dictionary<string, string> { ["runId"] = "run-1", ["manualOverrideId"] = bypass.OverrideId };
        var prepared = policy.Prepare(Request() with { Metadata = metadata }, "order-1");
        var result = await policy.EvaluatePreflightAsync(prepared, default);

        Assert.Equal(approved, result.IsApproved);
        Assert.False(prepared.SafeRequest.Metadata!.ContainsKey("manualOverrideId"));
        if (mode == ExecutionMode.Paper)
        {
            Assert.Same(metadata, prepared.ControlRequest.Metadata);
            Assert.Equal(bypass.OverrideId, result.OperatorControlDecision?.AppliedManualOverrideId);
        }
        else
        {
            Assert.Same(prepared.SafeRequest, prepared.ControlRequest);
            Assert.Equal("POSITION_LIMIT_EXCEEDED", result.Refusal?.ReasonCode);
        }
    }

    [Fact]
    public async Task DispatchControls_ReevaluateChangedPolicyAndReportTheCurrentDecision()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Paper, calls);
        var controls = CreateControls();
        var policy = CreatePolicy(gateway, calls, controls);
        var prepared = policy.Prepare(Request(), "order-1");
        var preflight = await policy.EvaluatePreflightAsync(prepared, default);
        Assert.True(preflight.IsApproved);

        await controls.SetCircuitBreakerAsync(true, "Halted after preflight", "test");
        var dispatch = policy.EvaluateDispatchControls(prepared);
        Assert.NotNull(dispatch);
        Assert.False(dispatch.IsApproved);
        var refusal = OrderAdmissionPolicy.ControlRefusal(dispatch, "operator controls at dispatch");
        Assert.Equal("Halted after preflight", refusal.Reason);
        Assert.Equal("operator controls at dispatch", refusal.Source);
        Assert.True(preflight.OperatorControlDecision!.IsApproved);
    }

    [Fact]
    public async Task PlacementChecksCurrentGateway_WhileCorrelationKeepsThePreparedBrokerName()
    {
        var calls = new List<string>();
        var gateway = new TestGateway(ExecutionMode.Live, calls);
        var policy = CreatePolicy(gateway, calls);
        var prepared = policy.Prepare(Request(), "order-1");
        gateway.GatewayId = "changed";

        var decision = await policy.EvaluatePreflightAsync(prepared, default);

        Assert.Equal("test", prepared.BrokerName);
        Assert.Equal("execution placement gate", decision.Refusal?.Source);
        Assert.Contains("active broker 'changed'", decision.Refusal!.Reason);
        Assert.Equal(["face-value"], calls);
    }

    private OrderAdmissionPolicy CreatePolicy(
        TestGateway gateway,
        List<string> calls,
        ExecutionOperatorControlService? controls = null,
        ReadinessGate? readiness = null,
        SecurityGate? security = null,
        BrokerageConfiguration? configuration = null) =>
        new(gateway, NullLogger.Instance,
            configuration ?? (gateway.ExecutionMode == ExecutionMode.Live ? LiveConfiguration() : null),
            security ?? new SecurityGate(calls, new SecurityMasterGateResult(true)),
            controls,
            readiness ?? new ReadinessGate(calls, (_, _) => Task.FromResult(LiveOrderReadinessDecision.Approved(" audit://ready "))));

    private ExecutionOperatorControlService CreateControls() =>
        new(new ExecutionOperatorControlOptions(_controlsRoot), NullLogger<ExecutionOperatorControlService>.Instance);

    private static OrderRequest Request() => new()
    {
        Symbol = "AAPL",
        Side = OrderSide.Buy,
        Type = OrderType.Limit,
        Quantity = 10m,
        LimitPrice = 100m,
        Metadata = new Dictionary<string, string> { ["runId"] = "run-1" }
    };

    private static BrokerageConfiguration LiveConfiguration() => new()
    {
        Gateway = "test",
        LiveExecutionEnabled = true,
        ReadOnlyVerificationPassed = true,
        PaperLifecycleTestsPassed = true,
        ReplayEvidencePassed = true,
        ProductionRoutingPhaseEnabled = true,
        ValidationGates = new BrokerValidationGateOptions { RequireValidationArtifactsForOrderPlacement = false },
        BrokerFlows = new Dictionary<string, BrokerFlowFlags>
        {
            ["test"] = new() { PaperOrderFlowEnabled = true, ProductionOrderRoutingEnabled = true }
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_controlsRoot))
            Directory.Delete(_controlsRoot, recursive: true);
    }

    private sealed class ReadinessGate(
        List<string> calls,
        Func<LiveOrderReadinessRequest, CancellationToken, Task<LiveOrderReadinessDecision>> evaluate) : ILiveOrderReadinessGate
    {
        public LiveOrderReadinessRequest? LastRequest { get; private set; }

        public Task<LiveOrderReadinessDecision> EvaluateAsync(LiveOrderReadinessRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            calls.Add("readiness");
            return evaluate(request, ct);
        }
    }

    private sealed class SecurityGate(List<string> calls, SecurityMasterGateResult decision) : ISecurityMasterGate
    {
        public Task<SecurityMasterGateResult> CheckAsync(string symbol, CancellationToken ct = default)
        {
            calls.Add("security");
            return Task.FromResult(decision);
        }
    }

    private sealed class TestGateway(ExecutionMode mode, List<string> calls)
        : IExecutionGateway, IExecutionGatewayModeProvider, INotionalOrderSizingGateway, IFaceValueOrderSizingGateway
    {
        public string GatewayId { get; set; } = "test";
        public ExecutionMode ExecutionMode => mode;
        public bool RoutesNotional { get; init; } = true;
        public bool FaceValue { get; init; }
        public bool IsConnected => true;
        public bool RoutesNotionalMetadata(OrderRequest request)
        {
            calls.Add("notional");
            return RoutesNotional;
        }
        public bool UsesFaceValuePercentageOfPar(OrderRequest request)
        {
            calls.Add("face-value");
            return FaceValue;
        }
        public Task ConnectAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ExecutionReport> SubmitOrderAsync(OrderRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ExecutionReport> CancelOrderAsync(string orderId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ExecutionReport> ModifyOrderAsync(string orderId, OrderModification modification, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ExecutionReport> StreamExecutionReportsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
