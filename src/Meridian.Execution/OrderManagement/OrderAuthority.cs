using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using ExecutionMode = Meridian.Execution.Sdk.ExecutionMode;

namespace Meridian.Execution.OrderManagement;

/// <summary>Configures order-authority checks in the OMS.</summary>
public sealed class OrderAuthorityOptions
{
    /// <summary>Maximum age of an account grant when the OMS evaluates it.</summary>
    public TimeSpan MaxGrantAge { get; init; } = TimeSpan.FromHours(8);
}

/// <summary>Reason codes for order-authority refusals.</summary>
public static class OrderAuthorityReasonCodes
{
    public const string KillSwitchSubmitNotAllowed = "KILL_SWITCH_SUBMIT_NOT_ALLOWED";
    public const string UnattributedLiveOrder = "ORDER_ORIGIN_UNATTRIBUTED_LIVE";
    public const string ManualLiveIntentContractNotReviewed = "MANUAL_LIVE_INTENT_CONTRACT_NOT_REVIEWED";
    public const string AccountGrantMissing = "ACCOUNT_GRANT_MISSING";
    public const string AccountGrantMismatch = "ACCOUNT_GRANT_MISMATCH";
    public const string AccountGrantStale = "ACCOUNT_GRANT_STALE";
    public const string RunNotFound = "RUN_NOT_FOUND";
    public const string RunNotActive = "RUN_NOT_ACTIVE";
    public const string RunNotPromotedTarget = "RUN_NOT_PROMOTED_TARGET";
    public const string RunArtifactMismatch = "RUN_ARTIFACT_MISMATCH";
    public const string AuthoritySourceUnavailable = "AUTHORITY_SOURCE_UNAVAILABLE";
    public const string LiveOrderReadinessRejected = "LIVE_ORDER_READINESS_REJECTED";
    public const string ReadinessChangedBeforeDispatch = "READINESS_CHANGED_BEFORE_DISPATCH";
    public const string SecurityMasterRejectedAtDispatch = "SECURITY_MASTER_REJECTED_AT_DISPATCH";
}

/// <summary>Retained run, promotion, and artifact state that strategy-run authority is checked against.</summary>
public sealed record StrategyRunAuthoritySnapshot(
    string RunId,
    Guid? FundAccountId,
    bool IsActive,
    string? PromotionId,
    string? PromotionTargetRunId,
    string? PromotionAuditReference,
    string? RunStrategyArtifactId,
    string? PromotedStrategyArtifactId);

/// <summary>Reads retained strategy-run state for <see cref="OrderCommandOrigin.StrategyRun"/> authority.</summary>
public interface IStrategyRunAuthoritySource
{
    /// <summary>Returns the run's retained authority state, or null when no such run is retained.</summary>
    Task<StrategyRunAuthoritySnapshot?> GetAsync(string runId, CancellationToken ct = default);
}

/// <summary>The authority outcome at preflight: a refusal, or approval with any readiness evidence.</summary>
internal sealed record OrderAuthorityDecision(
    OrderAdmissionRefusal? Refusal,
    LiveOrderReadinessDecision? Readiness)
{
    public bool IsApproved => Refusal is null;

    public static readonly OrderAuthorityDecision Approved = new(null, null);
}

/// <summary>
/// Decides whether an order's authority permits it to route. Called by
/// <see cref="OrderAdmissionPolicy"/> at the position the live readiness gate used to occupy, so
/// the refusal order of the other gates is unchanged.
/// </summary>
/// <remarks>
/// <see cref="OrderCommandOrigin.Unattributed"/> authority (every legacy
/// <see cref="IOrderManager.PlaceOrderAsync"/> call) keeps exactly the behavior the OMS had before
/// typed authority: paper orders pass, live orders need an approved readiness decision for their
/// <c>runId</c> metadata. Refusing unattributed live orders waits until every caller submits typed
/// commands (governed order command boundary, Phase 3).
/// </remarks>
internal sealed class OrderAuthorityResolver
{
    private const string ReadinessSource = "live order readiness gate";
    private const string AuthoritySource = "order authority";
    private const string DispatchSource = "order authority at dispatch";

    private readonly ExecutionMode _executionMode;
    private readonly ILiveOrderReadinessGate? _liveReadinessGate;
    private readonly IStrategyRunAuthoritySource? _runAuthority;
    private readonly ISecurityMasterGate? _securityMasterGate;
    private readonly OrderAuthorityOptions _options;
    private readonly TimeProvider _clock;

    internal OrderAuthorityResolver(
        ExecutionMode executionMode,
        ILiveOrderReadinessGate? liveReadinessGate,
        IStrategyRunAuthoritySource? runAuthority = null,
        OrderAuthorityOptions? options = null,
        TimeProvider? clock = null,
        ISecurityMasterGate? securityMasterGate = null)
    {
        _executionMode = executionMode;
        _liveReadinessGate = liveReadinessGate;
        _runAuthority = runAuthority;
        _securityMasterGate = securityMasterGate;
        _options = options ?? new OrderAuthorityOptions();
        _clock = clock ?? TimeProvider.System;
    }

    internal async Task<OrderAuthorityDecision> EvaluatePreflightAsync(
        PreparedOrderAdmission prepared,
        CancellationToken ct,
        bool observationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var root = Root(prepared.Authority);
        var live = _executionMode is ExecutionMode.Live;

        switch (root.Origin)
        {
            case OrderCommandOrigin.Unattributed:
                // Every production caller submits live orders under typed authority, so an
                // untyped live order has no established authority at all: its identity is only
                // whatever metadata it carries. Paper keeps the untyped path.
                return live
                    ? Refuse(OrderAuthorityReasonCodes.UnattributedLiveOrder,
                        "Live orders require typed order authority (a strategy run or a reviewed manual intent); this order was submitted without it.")
                    : OrderAuthorityDecision.Approved;

            case OrderCommandOrigin.KillSwitch:
                return Refuse(OrderAuthorityReasonCodes.KillSwitchSubmitNotAllowed,
                    "Kill-switch authority can cancel orders but cannot submit them.");
        }

        if (EvaluateGrant(root, prepared.SafeRequest) is { } grantRefusal)
            return grantRefusal;

        switch (root.Origin)
        {
            case OrderCommandOrigin.OperatorTicket:
            case OrderCommandOrigin.PositionAction:
                // A manual live order needs a reviewed manual-intent authority contract, which does
                // not exist yet. Paper orders need only the account grant.
                return live
                    ? Refuse(OrderAuthorityReasonCodes.ManualLiveIntentContractNotReviewed,
                        "Manual live orders require a reviewed manual-intent authority contract, which is not yet defined.")
                    : OrderAuthorityDecision.Approved;

            case OrderCommandOrigin.StrategyRun:
                if (!live)
                    return OrderAuthorityDecision.Approved;
                if (await EvaluateRunBindingAsync(root, ct).ConfigureAwait(false) is { } runRefusal)
                    return runRefusal;
                return await EvaluateReadinessAsync(prepared, root.RunId, observationOnly, ct).ConfigureAwait(false);

            default:
                return Refuse(OrderAuthorityReasonCodes.AuthoritySourceUnavailable,
                    $"Order authority origin '{root.Origin}' is not supported.");
        }
    }

    /// <summary>
    /// Re-checks typed authority after risk validation, immediately before the order becomes
    /// visible and routes. Time passes between preflight and this point (waiting for the
    /// pre-trade gate, validating risk), and a grant can age past its limit, a run can end, a
    /// readiness snapshot can be withdrawn, or an instrument can be suspended in that time.
    /// Untyped (legacy) orders keep their pre-existing path, which has no dispatch recheck.
    /// </summary>
    /// <returns>Null when the order may route; otherwise the refusal.</returns>
    internal async Task<OrderAdmissionRefusal?> EvaluateDispatchAsync(PreparedOrderAdmission prepared, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var root = Root(prepared.Authority);
        if (root.Origin is OrderCommandOrigin.Unattributed or OrderCommandOrigin.KillSwitch)
            return null;

        if (EvaluateGrant(root, prepared.SafeRequest) is { Refusal: { } grantRefusal })
            return grantRefusal with { Source = DispatchSource };

        if (root.Origin == OrderCommandOrigin.StrategyRun && _executionMode is ExecutionMode.Live)
        {
            if (await EvaluateRunBindingAsync(root, ct).ConfigureAwait(false) is { Refusal: { } runRefusal })
                return runRefusal with { Source = DispatchSource };

            var readiness = await EvaluateLiveOrderReadinessAsync(prepared, root.RunId, observationOnly: false, ct)
                .ConfigureAwait(false);
            if (!readiness.IsApproved)
            {
                return new OrderAdmissionRefusal(
                    readiness.Reason ?? "Live order readiness was withdrawn before dispatch.",
                    DispatchSource,
                    OrderAuthorityReasonCodes.ReadinessChangedBeforeDispatch,
                    OrderAuditMetadata.BuildLiveOrderReadinessRejectedAuditMetadata(readiness));
            }
        }

        if (_securityMasterGate is not null)
        {
            var security = await _securityMasterGate.CheckAsync(prepared.SafeRequest.Symbol, ct).ConfigureAwait(false);
            if (!security.IsApproved)
            {
                return new OrderAdmissionRefusal(
                    security.Reason, DispatchSource, OrderAuthorityReasonCodes.SecurityMasterRejectedAtDispatch);
            }
        }

        return null;
    }

    /// <summary>A release is judged by the authority it was parked with, not by the approver.</summary>
    private static OrderSubmissionAuthority Root(OrderSubmissionAuthority authority)
    {
        var current = authority;
        while (current.Origin == OrderCommandOrigin.EscalationRelease && current.ReleasedFrom is { } parked)
            current = parked;
        return current;
    }

    private OrderAuthorityDecision? EvaluateGrant(OrderSubmissionAuthority root, OrderRequest order)
    {
        if (root.AccountGrant is not { } grant)
            return Refuse(OrderAuthorityReasonCodes.AccountGrantMissing,
                "The order carries no execution account grant.");

        if (order.FundAccountId != grant.FundAccountId)
            return Refuse(OrderAuthorityReasonCodes.AccountGrantMismatch,
                "The order's fund account does not match the account the submitter was granted.");

        if (_clock.GetUtcNow() - grant.ResolvedAt > _options.MaxGrantAge)
            return Refuse(OrderAuthorityReasonCodes.AccountGrantStale,
                "The execution account grant is older than the configured maximum age.");

        return null;
    }

    private async Task<OrderAuthorityDecision?> EvaluateRunBindingAsync(OrderSubmissionAuthority root, CancellationToken ct)
    {
        var runId = root.RunId!;
        if (_runAuthority is null)
            return Refuse(OrderAuthorityReasonCodes.AuthoritySourceUnavailable,
                "Live strategy orders require a strategy-run authority source.");

        StrategyRunAuthoritySnapshot? run;
        try
        {
            run = await _runAuthority.GetAsync(runId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A failure to read authority is never an approval.
            return Refuse(OrderAuthorityReasonCodes.AuthoritySourceUnavailable,
                "Strategy-run authority could not be read: " + ex.Message);
        }

        if (run is null)
            return Refuse(OrderAuthorityReasonCodes.RunNotFound, $"Strategy run '{runId}' is not retained.");
        if (!run.IsActive)
            return Refuse(OrderAuthorityReasonCodes.RunNotActive, $"Strategy run '{runId}' is not active.");
        if (!string.Equals(run.PromotionTargetRunId, runId, StringComparison.Ordinal))
            return Refuse(OrderAuthorityReasonCodes.RunNotPromotedTarget,
                $"Strategy run '{runId}' is not the target of a live promotion.");
        if (run.FundAccountId != root.AccountGrant!.FundAccountId)
            return Refuse(OrderAuthorityReasonCodes.AccountGrantMismatch,
                "The strategy run's retained fund account does not match the order's account grant.");
        // Any two artifact ids that are known must agree: order vs run, run vs promotion, and order
        // vs promotion directly (the run record may not carry one).
        if (ArtifactsDiffer(root.StrategyArtifactId, run.RunStrategyArtifactId)
            || ArtifactsDiffer(run.RunStrategyArtifactId, run.PromotedStrategyArtifactId)
            || ArtifactsDiffer(root.StrategyArtifactId, run.PromotedStrategyArtifactId))
            return Refuse(OrderAuthorityReasonCodes.RunArtifactMismatch,
                "The strategy run executes a different strategy artifact than the one promoted.");

        return null;
    }

    private static bool ArtifactsDiffer(string? left, string? right) =>
        left is not null && right is not null && !string.Equals(left, right, StringComparison.Ordinal);

    private async Task<OrderAuthorityDecision> EvaluateReadinessAsync(
        PreparedOrderAdmission prepared,
        string? runId,
        bool observationOnly,
        CancellationToken ct)
    {
        var readiness = await EvaluateLiveOrderReadinessAsync(prepared, runId, observationOnly, ct).ConfigureAwait(false);
        return readiness.IsApproved
            ? new OrderAuthorityDecision(null, readiness)
            : new OrderAuthorityDecision(
                new OrderAdmissionRefusal(
                    readiness.Reason ?? "Live order readiness gate rejected the order.",
                    ReadinessSource,
                    OrderAuthorityReasonCodes.LiveOrderReadinessRejected,
                    OrderAuditMetadata.BuildLiveOrderReadinessRejectedAuditMetadata(readiness)),
                readiness);
    }

    private async Task<LiveOrderReadinessDecision> EvaluateLiveOrderReadinessAsync(
        PreparedOrderAdmission prepared,
        string? runId,
        bool observationOnly,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(runId))
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
        var readinessRequest = new LiveOrderReadinessRequest(
            RunId: runId.Trim(),
            BrokerName: prepared.BrokerName,
            Symbol: request.Symbol,
            Side: request.Side,
            OrderType: request.Type,
            Quantity: request.Quantity,
            StrategyId: request.StrategyId,
            Actor: prepared.Actor,
            CorrelationId: prepared.CorrelationId,
            FundAccountId: request.FundAccountId);
        var decision = observationOnly
            ? (_liveReadinessGate as ILiveOrderReadinessPreview)?.Observe(readinessRequest)
                ?? LiveOrderReadinessDecision.Rejected("Live readiness is unassessed: refresh the account's readiness evidence before previewing.")
            : await _liveReadinessGate.EvaluateAsync(readinessRequest, ct).ConfigureAwait(false);

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

    private static OrderAuthorityDecision Refuse(string reasonCode, string reason) =>
        new(new OrderAdmissionRefusal(reason, AuthoritySource, reasonCode), null);
}
