using Meridian.Execution.Logging;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using ExecutionMode = Meridian.Execution.Sdk.ExecutionMode;

namespace Meridian.Execution.OrderManagement;

/// <summary>
/// Evaluates order-routing policy against the configured gateway and governance services.
/// It returns decisions and evidence without registering orders, writing lifecycle audit, taking
/// risk reservations, or dispatching to the broker. The OMS owns those ordered transitions.
/// </summary>
internal sealed class OrderAdmissionPolicy
{
    private readonly IExecutionGateway _gateway;
    private readonly BrokerageConfiguration? _configuration;
    private readonly ExecutionMode _executionMode;
    private readonly ISecurityMasterGate? _securityMasterGate;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly ILiveOrderReadinessGate? _liveReadinessGate;
    private readonly IPortfolioState? _portfolioState;
    private readonly ILogger _logger;

    internal OrderAdmissionPolicy(
        IExecutionGateway gateway,
        ILogger logger,
        BrokerageConfiguration? configuration = null,
        ISecurityMasterGate? securityMasterGate = null,
        ExecutionOperatorControlService? operatorControls = null,
        ILiveOrderReadinessGate? liveReadinessGate = null,
        IPortfolioState? portfolioState = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gateway = gateway;
        _configuration = configuration;
        _executionMode = gateway is IExecutionGatewayModeProvider modeProvider
            ? modeProvider.ExecutionMode
            : BrokerageOrderPlacementGate.ResolveExecutionMode(configuration, gateway.GatewayId);
        _securityMasterGate = securityMasterGate;
        _operatorControls = operatorControls;
        _liveReadinessGate = liveReadinessGate;
        _portfolioState = portfolioState;
    }

    /// <summary>
    /// Stamps identity and removes internal probe keys before any request is retained. The safe
    /// request snapshots metadata and legs once for risk and routing; paper control evaluation
    /// retains the existing original-request override behavior.
    /// </summary>
    internal PreparedOrderAdmission Prepare(OrderRequest request, string orderId)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = request.ClientOrderId is null ? request with { ClientOrderId = orderId } : request;
        request = StripInternalRiskMetadata(request);
        var brokerName = _gateway.GatewayId;

        string? actor = null;
        string? correlationId = null;
        string? runId = null;
        string? sessionId = null;
        request.Metadata?.TryGetValue("actor", out actor);
        request.Metadata?.TryGetValue("correlationId", out correlationId);
        request.Metadata?.TryGetValue("runId", out runId);
        request.Metadata?.TryGetValue("sessionId", out sessionId);

        var safeRequest = SanitizeAndSnapshotRequest(request, orderId);
        var controlRequest = _executionMode is ExecutionMode.Live ? safeRequest : request;
        return new PreparedOrderAdmission(
            orderId, request, safeRequest, controlRequest, brokerName, actor, correlationId, runId, sessionId);
    }

    /// <summary>
    /// Runs after the caller's duplicate-id guard and before its serialized reserving risk gate.
    /// Refusals retain the first failing gate and its exact evidence; later gates are not invoked.
    /// </summary>
    internal async Task<OrderAdmissionDecision> EvaluatePreflightAsync(
        PreparedOrderAdmission prepared,
        CancellationToken ct)
    {
        if (!ValidatedOrder.TryCreate(prepared.SafeRequest, out var validated, out var errors))
        {
            return new OrderAdmissionDecision(prepared.SafeRequest, false, null, null,
                new OrderAdmissionRefusal(
                    string.Join(" ", errors.Select(error => error.Message)),
                    "order request validation", "ORDER_REQUEST_INVALID",
                    new Dictionary<string, string>
                    {
                        ["validationErrors"] = System.Text.Json.JsonSerializer.Serialize(errors)
                    }));
        }

        var safeRequest = validated.Request;
        prepared = prepared with { SafeRequest = safeRequest };
        if (!GatewayResolvedOrder.TryResolve(validated, _gateway, out var resolved, out var rejectionReason))
        {
            return new OrderAdmissionDecision(safeRequest, false, null, null,
                new OrderAdmissionRefusal(rejectionReason, "notional metadata gate"));
        }

        var usesFaceValue = resolved.Sizing is OrderSizing.FaceValuePercentageOfPar;
        var riskRequest = resolved.ToRiskRequest();
        var placementGate = BrokerageOrderPlacementGate.Evaluate(_configuration, _gateway.GatewayId, _executionMode);
        if (!placementGate.IsAllowed)
        {
            return new OrderAdmissionDecision(riskRequest, usesFaceValue, null, null,
                new OrderAdmissionRefusal(placementGate.RejectReason, "execution placement gate"));
        }

        LiveOrderReadinessDecision? readiness = null;
        if (_executionMode is ExecutionMode.Live)
        {
            readiness = await EvaluateLiveOrderReadinessAsync(prepared, ct).ConfigureAwait(false);
            if (!readiness.IsApproved)
            {
                return new OrderAdmissionDecision(riskRequest, usesFaceValue, null, readiness,
                    new OrderAdmissionRefusal(
                        readiness.Reason ?? "Live order readiness gate rejected the order.",
                        "live order readiness gate",
                        "LIVE_ORDER_READINESS_REJECTED",
                        OrderAuditMetadata.BuildLiveOrderReadinessRejectedAuditMetadata(readiness)));
            }
        }

        var controlDecision = EvaluateDispatchControls(prepared);
        if (controlDecision is { IsApproved: false })
        {
            return new OrderAdmissionDecision(riskRequest, usesFaceValue, controlDecision, readiness,
                ControlRefusal(controlDecision, "operator controls"));
        }

        if (_securityMasterGate is not null)
        {
            var securityDecision = await _securityMasterGate.CheckAsync(safeRequest.Symbol, ct).ConfigureAwait(false);
            if (!securityDecision.IsApproved)
            {
                return new OrderAdmissionDecision(riskRequest, usesFaceValue, controlDecision, readiness,
                    new OrderAdmissionRefusal(securityDecision.Reason, "Security Master gate"));
            }
        }

        return new OrderAdmissionDecision(riskRequest, usesFaceValue, controlDecision, readiness, null, resolved);
    }

    /// <summary>
    /// Re-evaluates controls after reserving risk validation and before order registration. The
    /// caller must use this decision, rather than the earlier preflight one, in its routing audit.
    /// </summary>
    internal ExecutionControlDecision? EvaluateDispatchControls(PreparedOrderAdmission prepared) =>
        _operatorControls?.EvaluateOrder(prepared.ControlRequest, _portfolioState, prepared.RunId);

    internal static OrderAdmissionRefusal ControlRefusal(ExecutionControlDecision decision, string source) =>
        new(decision.RejectReason, source, decision.RejectCode ?? "OPERATOR_CONTROL_REJECTED",
            OrderAuditMetadata.BuildOrderRejectedByControlAuditMetadata(decision));

    /// <summary>New placements measure the sizing already resolved by the gateway.</summary>
    internal string? EvaluateBrokerageNotionalLimit(GatewayResolvedOrder order) =>
        EvaluateBrokerageNotionalLimit(() => OrderRiskSizingPolicy.ResolveBrokerageOrderNotional(order));

    /// <summary>Used under the caller's reservation gate for placements and amendments.</summary>
    internal string? EvaluateBrokerageNotionalLimit(OrderRequest request) =>
        EvaluateBrokerageNotionalLimit(() => OrderRiskSizingPolicy.ResolveBrokerageOrderNotional(request));

    private string? EvaluateBrokerageNotionalLimit(Func<decimal?> measure)
    {
        if (_configuration is not { MaxOrderNotional: > 0m } configuration)
            return null;

        decimal? notional;
        try
        {
            notional = measure();
        }
        catch (OverflowException)
        {
            return $"Order notional exceeds configured maximum of {configuration.MaxOrderNotional}.";
        }

        if (notional is null or <= 0m)
            return "Unable to evaluate order notional against the configured maximum without a positive price input.";

        return notional > configuration.MaxOrderNotional
            ? $"Order notional exceeds configured maximum of {configuration.MaxOrderNotional}."
            : null;
    }

    private async Task<LiveOrderReadinessDecision> EvaluateLiveOrderReadinessAsync(
        PreparedOrderAdmission prepared,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prepared.RunId))
        {
            return LiveOrderReadinessDecision.Rejected(
                "Live order placement requires runId metadata for a W7-approved live run.");
        }

        if (_liveReadinessGate is null)
        {
            return LiveOrderReadinessDecision.Rejected(
                "Live order placement requires a live order readiness gate before broker routing.");
        }

        var request = prepared.SafeRequest;
        var decision = await _liveReadinessGate.EvaluateAsync(new LiveOrderReadinessRequest(
            RunId: prepared.RunId.Trim(),
            BrokerName: prepared.BrokerName,
            Symbol: request.Symbol,
            Side: request.Side,
            OrderType: request.Type,
            Quantity: request.Quantity,
            StrategyId: request.StrategyId,
            Actor: prepared.Actor,
            CorrelationId: prepared.CorrelationId,
            FundAccountId: request.FundAccountId), ct).ConfigureAwait(false);

        if (!decision.IsApproved)
        {
            return string.IsNullOrWhiteSpace(decision.Reason)
                ? decision with { Reason = "Live order readiness gate rejected the order." }
                : decision;
        }

        return string.IsNullOrWhiteSpace(decision.EvidenceReference)
            ? LiveOrderReadinessDecision.Rejected(
                "Live order readiness gate approved the order without a retained evidence reference.")
            : decision with { EvidenceReference = decision.EvidenceReference.Trim() };
    }

    private OrderRequest SanitizeAndSnapshotRequest(OrderRequest request, string orderId)
    {
        var safeRequest = ExecutionOrderMetadataPolicy.RemoveServerOwnedRoutingKeys(request);
        if (!ReferenceEquals(safeRequest, request))
        {
            _logger.LogWarning(
                "Order {OrderId} for {Symbol} contained server-owned broker routing metadata; routing keys were removed before gateway submission.",
                LogSanitizer.Sanitize(orderId),
                LogSanitizer.Sanitize(request.Symbol));
        }

        return safeRequest with
        {
            Metadata = safeRequest.Metadata is { } callerMetadata
                ? new Dictionary<string, string>(callerMetadata)
                : safeRequest.Metadata,
            Legs = safeRequest.Legs is { } callerLegs ? [.. callerLegs] : safeRequest.Legs
        };
    }

    private static readonly string[] InternalRiskMetadataKeys =
    [
        RiskEscalationQueueService.EvaluationOnlyMetadataKey,
        RiskEscalationQueueService.IncrementalNotionalMetadataKey,
        OrderSizingMetadata.FaceValuePercentageOfParKey
    ];

    private static OrderRequest StripInternalRiskMetadata(OrderRequest request)
    {
        if (request.Metadata is null
            || !request.Metadata.Keys.Any(candidate => InternalRiskMetadataKeys.Any(
                key => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))))
        {
            return request;
        }

        var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (candidate, value) in request.Metadata)
        {
            if (!InternalRiskMetadataKeys.Any(
                    key => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)))
            {
                sanitized[candidate] = value;
            }
        }

        return request with { Metadata = sanitized };
    }


}

internal sealed record PreparedOrderAdmission(
    string OrderId,
    OrderRequest Request,
    OrderRequest SafeRequest,
    OrderRequest ControlRequest,
    string BrokerName,
    string? Actor,
    string? CorrelationId,
    string? RunId,
    string? SessionId);

internal sealed record OrderAdmissionDecision(
    OrderRequest RiskRequest,
    bool UsesFaceValuePercentageOfPar,
    ExecutionControlDecision? OperatorControlDecision,
    LiveOrderReadinessDecision? LiveReadinessDecision,
    OrderAdmissionRefusal? Refusal,
    GatewayResolvedOrder? ResolvedOrder = null)
{
    public bool IsApproved => Refusal is null;
}

internal sealed record OrderAdmissionRefusal(
    string? Reason,
    string Source,
    string? ReasonCode = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
