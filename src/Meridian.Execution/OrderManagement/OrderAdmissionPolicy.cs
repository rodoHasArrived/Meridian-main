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
    private readonly OrderAuthorityResolver _authority;
    private readonly IPortfolioState? _portfolioState;
    private readonly ILogger _logger;
    private readonly IOrderSessionPolicy? _sessionPolicy;
    private readonly IOrderMarketReferenceSource? _marketReferences;
    private readonly IOrderInstrumentCoverageSource? _instrumentCoverage;
    private readonly TimeProvider _clock;

    internal OrderAdmissionPolicy(
        IExecutionGateway gateway,
        ILogger logger,
        BrokerageConfiguration? configuration = null,
        ISecurityMasterGate? securityMasterGate = null,
        ExecutionOperatorControlService? operatorControls = null,
        ILiveOrderReadinessGate? liveReadinessGate = null,
        IPortfolioState? portfolioState = null,
        IOrderSessionPolicy? sessionPolicy = null,
        IOrderMarketReferenceSource? marketReferences = null,
        IOrderInstrumentCoverageSource? instrumentCoverage = null,
        TimeProvider? clock = null,
        IStrategyRunAuthoritySource? runAuthority = null,
        OrderAuthorityOptions? authorityOptions = null)
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
        _portfolioState = portfolioState;
        _sessionPolicy = sessionPolicy;
        _marketReferences = marketReferences;
        _instrumentCoverage = instrumentCoverage;
        _clock = clock ?? TimeProvider.System;
        _authority = new OrderAuthorityResolver(
            _executionMode, liveReadinessGate, runAuthority, authorityOptions, _clock, securityMasterGate);
    }

    /// <summary>
    /// Stamps identity and removes internal probe keys before any request is retained. The safe
    /// request snapshots metadata and legs once for risk and routing; paper control evaluation
    /// retains the existing original-request override behavior.
    /// </summary>
    internal PreparedOrderAdmission Prepare(OrderRequest request, string orderId, OrderSubmissionAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Legacy callers carry identity in metadata; typed callers have already stamped the same
        // keys from their authority, so the reads below agree with it either way.
        authority ??= OrderSubmissionAuthority.Unattributed(request.Metadata);
        request = request.ClientOrderId is null ? request with { ClientOrderId = orderId } : request;
        if (_portfolioState is PaperTradingPortfolio { UsesCanonicalAccounting: true } canonical && request.FundAccountId is null)
            request = request with { FundAccountId = Guid.Parse(canonical.PrimaryAccountId) };
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
            orderId, request, safeRequest, controlRequest, brokerName, actor, correlationId, runId, sessionId, authority);
    }

    /// <summary>
    /// Runs after the caller's duplicate-id guard and before its serialized reserving risk gate.
    /// Refusals retain the first failing gate and its exact evidence; later gates are not invoked.
    /// </summary>
    internal async Task<OrderAdmissionDecision> EvaluatePreflightAsync(
        PreparedOrderAdmission prepared,
        CancellationToken ct,
        bool observationOnly = false)
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

        if (_portfolioState is PaperTradingPortfolio { UsesCanonicalAccounting: true } scoped
            && prepared.SafeRequest.FundAccountId != Guid.Parse(scoped.PrimaryAccountId))
            return new OrderAdmissionDecision(prepared.SafeRequest, false, null, null,
                new OrderAdmissionRefusal("This canonical paper host can execute only for its configured accounting account.",
                    "canonical accounting account", "CANONICAL_ACCOUNT_MISMATCH"));

        if (_portfolioState is PaperTradingPortfolio paper && !paper.CanAdmitCanonicalOrders(DateTimeOffset.UtcNow, out var recoveryBlocker))
            return new OrderAdmissionDecision(prepared.SafeRequest, false, null, null,
                new OrderAdmissionRefusal(recoveryBlocker, "canonical accounting recovery", "CANONICAL_RECOVERY_NOT_READY"));

        var safeRequest = validated.Request;
        prepared = prepared with { SafeRequest = safeRequest };
        if (!GatewayResolvedOrder.TryResolve(validated, _gateway, out var resolved, out var rejectionReason))
        {
            return new OrderAdmissionDecision(safeRequest, false, null, null,
                new OrderAdmissionRefusal(rejectionReason, "notional metadata gate"));
        }

        if (_portfolioState is PaperTradingPortfolio { UsesCanonicalAccounting: true } canonical)
        {
            var canonicalRefusal = resolved.Sizing is not OrderSizing.Quantity
                || OrderRiskSizingPolicy.ResolveContractMultiplier(safeRequest) != 1m
                ? "Canonical accounting requires unit quantity with a multiplier of one."
                : await canonical.ValidateCanonicalOrderAsync(safeRequest, ct).ConfigureAwait(false);
            if (canonicalRefusal is not null)
                return new OrderAdmissionDecision(safeRequest, false, null, null,
                    new OrderAdmissionRefusal(canonicalRefusal, "canonical accounting authority", "CANONICAL_AUTHORITY_REJECTED"));
        }

        var usesFaceValue = resolved.Sizing is OrderSizing.FaceValuePercentageOfPar;
        var riskRequest = resolved.ToRiskRequest();
        if (safeRequest.SessionIntent == OrderSessionIntent.QueueNextSession && _gateway is PaperTradingGateway)
            return new OrderAdmissionDecision(riskRequest, usesFaceValue, null, null,
                new OrderAdmissionRefusal("The local paper simulator does not support next-session queuing.",
                    "market session", "QUEUED_SESSION_UNSUPPORTED"));
        if (await EvaluateSessionAsync(safeRequest, ct).ConfigureAwait(false) is { Status: "blocked" } session)
            return new OrderAdmissionDecision(riskRequest, usesFaceValue, null, null,
                new OrderAdmissionRefusal(session.Message, "market session", session.Code));
        if (await EvaluateMarketReferenceAsync(safeRequest, ct).ConfigureAwait(false) is { } quoteRefusal)
            return new OrderAdmissionDecision(riskRequest, usesFaceValue, null, null, quoteRefusal);
        var placementGate = BrokerageOrderPlacementGate.Evaluate(_configuration, _gateway.GatewayId, _executionMode);
        if (!placementGate.IsAllowed)
        {
            return new OrderAdmissionDecision(riskRequest, usesFaceValue, null, null,
                new OrderAdmissionRefusal(placementGate.RejectReason, "execution placement gate"));
        }

        // Order authority sits where the live readiness gate always ran; for legacy (unattributed)
        // orders it is exactly that gate, so the refusal order of the other gates is unchanged.
        var authority = await _authority.EvaluatePreflightAsync(prepared, ct, observationOnly).ConfigureAwait(false);
        var readiness = authority.Readiness;
        if (!authority.IsApproved)
            return new OrderAdmissionDecision(riskRequest, usesFaceValue, null, readiness, authority.Refusal);

        var controlDecision = _operatorControls?.EvaluateOrder(prepared.ControlRequest, _portfolioState, prepared.RunId, observationOnly);
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

    /// <summary>
    /// Re-checks typed order authority after risk validation and before the order becomes
    /// visible; see <see cref="OrderAuthorityResolver.EvaluateDispatchAsync"/>.
    /// </summary>
    internal Task<OrderAdmissionRefusal?> EvaluateDispatchAuthorityAsync(PreparedOrderAdmission prepared, CancellationToken ct) =>
        _authority.EvaluateDispatchAsync(prepared, ct);

    internal static OrderAdmissionRefusal ControlRefusal(ExecutionControlDecision decision, string source) =>
        new(decision.RejectReason, source, decision.RejectCode ?? "OPERATOR_CONTROL_REJECTED",
            OrderAuditMetadata.BuildOrderRejectedByControlAuditMetadata(decision));

    /// <summary>New placements measure the sizing already resolved by the gateway.</summary>
    internal string? EvaluateBrokerageNotionalLimit(GatewayResolvedOrder order) =>
        EvaluateBrokerageNotionalLimit(() => OrderRiskSizingPolicy.ResolveBrokerageOrderNotional(order));

    internal Task<OrderCheck?> EvaluateSessionAsync(OrderRequest request, CancellationToken ct) =>
        _sessionPolicy is null ? Task.FromResult<OrderCheck?>(null) : EvaluateSessionCoreAsync(request, ct);

    internal async Task<OrderAdmissionRefusal?> EvaluateMarketReferenceAsync(OrderRequest request, CancellationToken ct) =>
        await RequiresMarketReferenceAsync(request, ct).ConfigureAwait(false) ? EvaluateMarketReference(request) : null;

    internal async Task<OrderAdmissionRefusal?> EvaluateDispatchMarketChecksAsync(OrderRequest request, CancellationToken ct)
    {
        var requiresReference = await RequiresMarketReferenceAsync(request, ct).ConfigureAwait(false);
        if (await EvaluateSessionAsync(request, ct).ConfigureAwait(false) is { Status: "blocked" } session)
            return new(session.Message, "market session", session.Code);
        // Read the quote after the asynchronous session check so it cannot age during that wait.
        return requiresReference ? EvaluateMarketReference(request) : null;
    }

    private Task<bool> RequiresMarketReferenceAsync(OrderRequest request, CancellationToken ct) =>
        _instrumentCoverage?.IsUsdEquityAsync(request, ct) ?? Task.FromResult(false);

    private OrderAdmissionRefusal? EvaluateMarketReference(OrderRequest request)
    {
        var reference = _marketReferences?.GetReference(request.Symbol, request.Side);
        var now = _clock.GetUtcNow();
        return reference is null || reference.Price <= 0m || reference.ObservedAt > now || now - reference.ObservedAt >= TimeSpan.FromSeconds(30)
            ? new("A fresh executable-side equity quote is required before routing.", "market reference", "REFERENCE_UNAVAILABLE") : null;
    }

    private async Task<OrderCheck?> EvaluateSessionCoreAsync(OrderRequest request, CancellationToken ct) =>
        await _sessionPolicy!.EvaluateAsync(request, ct).ConfigureAwait(false);

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
    string? SessionId,
    OrderSubmissionAuthority Authority);

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
