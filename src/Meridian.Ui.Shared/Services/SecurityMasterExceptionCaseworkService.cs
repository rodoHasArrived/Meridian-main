using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Per-category SLA configuration for Security Master exceptions.
/// </summary>
public sealed record SecurityMasterExceptionSlaConfig
{
    /// <summary>Days to resolve an identifier conflict before SLA breach.</summary>
    public int IdentifierConflictDays { get; init; } = 5;

    /// <summary>Days to resolve an incomplete security record before SLA breach.</summary>
    public int IncompleteRecordDays { get; init; } = 3;

    /// <summary>Days to create a new unresolved security before SLA breach.</summary>
    public int NewSecurityUnresolvedDays { get; init; } = 1;
}

/// <summary>
/// Projects Security Master exceptions into the shared reconciliation case queue.
/// </summary>
public sealed class SecurityMasterExceptionCaseworkService
{
    private const string DefaultActor = "security-master-casework";
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;
    private readonly SecurityMasterExceptionSlaConfig _slaConfig;
    private readonly ILogger<SecurityMasterExceptionCaseworkService> _logger;

    public SecurityMasterExceptionCaseworkService(
        IReconciliationBreakQueueRepository? breakQueueRepository,
        ILogger<SecurityMasterExceptionCaseworkService> logger,
        SecurityMasterExceptionSlaConfig? slaConfig = null)
    {
        _breakQueueRepository = breakQueueRepository;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _slaConfig = slaConfig ?? new SecurityMasterExceptionSlaConfig();
    }

    /// <summary>
    /// Returns all open Security Master exception cases that have breached or are past their SLA deadline.
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAgingExceptionsAsync(
        CancellationToken ct = default)
    {
        if (_breakQueueRepository is null)
            return [];

        var all = await _breakQueueRepository.GetAllAsync(
            ReconciliationBreakQueueStatus.Open, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        return all
            .Where(item => item.Team == "Security Master"
                && item.SlaDueAt.HasValue
                && item.SlaDueAt.Value < now)
            .ToList();
    }

    public async Task SeedOpenConflictCasesAsync(
        IReadOnlyList<SecurityMasterConflict> conflicts,
        string? actor,
        CancellationToken ct = default)
    {
        if (_breakQueueRepository is null || conflicts.Count == 0)
        {
            return;
        }

        foreach (var conflict in conflicts.Where(static item =>
                     string.Equals(item.Status, "Open", StringComparison.OrdinalIgnoreCase)))
        {
            ct.ThrowIfCancellationRequested();
            await _breakQueueRepository
                .CreateIfMissingAsync(BuildConflictCase(conflict, actor, _slaConfig), ct)
                .ConfigureAwait(false);
        }
    }

    public async Task ApplyResolvedConflictAsync(
        SecurityMasterConflict conflict,
        ResolveConflictRequest request,
        CancellationToken ct = default)
    {
        if (_breakQueueRepository is null)
        {
            return;
        }

        var actor = NormalizeActor(request.ResolvedBy);
        var desired = BuildConflictCase(conflict, actor, _slaConfig);
        await _breakQueueRepository.CreateIfMissingAsync(desired, ct).ConfigureAwait(false);

        var existing = await _breakQueueRepository.GetByIdAsync(desired.BreakId, ct).ConfigureAwait(false);
        if (existing is null ||
            existing.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed)
        {
            return;
        }

        if (existing.Status == ReconciliationBreakQueueStatus.Open)
        {
            var review = await _breakQueueRepository.StartReviewAsync(
                    new ReviewReconciliationBreakRequest(
                        existing.BreakId,
                        existing.AssignedTo ?? actor,
                        actor,
                        request.Reason ?? $"Security Master conflict {conflict.ConflictId:N} selected for resolution.",
                        "Security Master"),
                    ct)
                .ConfigureAwait(false);
            if (review.Status != ReconciliationBreakQueueTransitionStatus.Success)
            {
                _logger.LogWarning(
                    "Could not start review for Security Master conflict case {BreakId}: {Error}",
                    existing.BreakId,
                    review.Error);
                return;
            }
        }

        var targetStatus = string.Equals(conflict.Status, "Dismissed", StringComparison.OrdinalIgnoreCase)
            ? ReconciliationBreakQueueStatus.Dismissed
            : ReconciliationBreakQueueStatus.Resolved;
        var resolve = await _breakQueueRepository.ResolveAsync(
                new ResolveReconciliationBreakRequest(
                    existing.BreakId,
                    targetStatus,
                    actor,
                    $"Security Master conflict {conflict.ConflictId:N} {conflict.Status.ToLowerInvariant()}.",
                    request.Reason ?? $"Resolution action {request.Resolution}."),
                ct)
            .ConfigureAwait(false);
        if (resolve.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            _logger.LogWarning(
                "Could not resolve Security Master conflict case {BreakId}: {Error}",
                existing.BreakId,
                resolve.Error);
            return;
        }
    }

    public async Task SeedOperatorOverrideCaseAsync(
        OperatorOverridesDto overrides,
        string? actor,
        CancellationToken ct = default)
    {
        if (_breakQueueRepository is null || overrides.Values.Count == 0)
        {
            return;
        }

        var desired = BuildOverrideCase(overrides, actor);
        await _breakQueueRepository.CreateIfMissingAsync(desired, ct).ConfigureAwait(false);

        if (overrides.ApprovalStatus != SecurityOverrideApprovalStatusDto.Approved)
        {
            return;
        }

        var existing = await _breakQueueRepository.GetByIdAsync(desired.BreakId, ct).ConfigureAwait(false);
        if (existing is null ||
            existing.Status is ReconciliationBreakQueueStatus.Resolved or ReconciliationBreakQueueStatus.Dismissed)
        {
            return;
        }

        var resolvedBy = NormalizeOptionalActor(overrides.ReviewedBy) ?? NormalizeActor(actor);
        if (existing.Status == ReconciliationBreakQueueStatus.Open)
        {
            var review = await _breakQueueRepository.StartReviewAsync(
                    new ReviewReconciliationBreakRequest(
                        existing.BreakId,
                        existing.AssignedTo ?? resolvedBy,
                        resolvedBy,
                        "Security Master override selected for approval resolution.",
                        "Security Master"),
                    ct)
                .ConfigureAwait(false);
            if (review.Status != ReconciliationBreakQueueTransitionStatus.Success)
            {
                _logger.LogWarning(
                    "Could not start review for Security Master override case {BreakId}: {Error}",
                    existing.BreakId,
                    review.Error);
                return;
            }
        }

        var resolve = await _breakQueueRepository.ResolveAsync(
                new ResolveReconciliationBreakRequest(
                    existing.BreakId,
                    ReconciliationBreakQueueStatus.Resolved,
                    resolvedBy,
                    "Security Master override approved.",
                    overrides.ReasonCode ?? "Override approval completed."),
                ct)
            .ConfigureAwait(false);
        if (resolve.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            _logger.LogWarning(
                "Could not resolve Security Master override case {BreakId}: {Error}",
                existing.BreakId,
                resolve.Error);
            return;
        }
    }

    private static ReconciliationBreakQueueItem BuildConflictCase(
        SecurityMasterConflict conflict,
        string? actor,
        SecurityMasterExceptionSlaConfig slaConfig)
    {
        var assignedTo = NormalizeActor(actor) ?? "security-master-steward";
        var route = UiApiRoutes.SecurityMasterConflicts;
        var slaDueAt = conflict.DetectedAt.AddDays(slaConfig.IdentifierConflictDays);
        var slaBreached = DateTimeOffset.UtcNow > slaDueAt;
        return new ReconciliationBreakQueueItem(
            BreakId: BuildConflictCaseId(conflict.ConflictId),
            RunId: "security-master-conflicts",
            StrategyName: "Security Master exception casework",
            Category: ReconciliationBreakCategory.ClassificationGap,
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 0m,
            Reason: $"Security Master {conflict.ConflictKind} conflict on {conflict.FieldPath}.",
            AssignedTo: assignedTo,
            DetectedAt: conflict.DetectedAt,
            LastUpdatedAt: conflict.DetectedAt,
            Severity: ReconciliationBreakSeverity.High,
            ExceptionRoute: "security-master/conflicts",
            ToleranceProfileId: "security-master-identity",
            RequiredSignoffRole: "Security Master steward",
            SignoffStatus: "pending-signoff",
            FundAccountId: null,
            ExplainabilitySummary: string.Join(
                ", ",
                $"securityId={conflict.SecurityId:D}",
                $"providerA={conflict.ProviderA}",
                $"providerB={conflict.ProviderB}",
                $"field={conflict.FieldPath}",
                $"status={conflict.Status}"),
            RoutingTarget: route,
            RoutingDetail: conflict.ConflictId.ToString("D"),
            RecommendedAction: "Review the identifier conflict, choose the authoritative provider value, or dismiss the conflict with rationale.",
            LifecycleState: ReconciliationCaseLifecycleState.Open,
            LifecycleRationale: "Auto-generated from an open Security Master identifier conflict.",
            ExternalAccountId: null,
            CustodianId: conflict.ProviderA,
            UpstreamSyncCursor: $"security-master-conflict:{conflict.ConflictId:N}:{conflict.Status}",
            LastUpstreamSyncAt: conflict.DetectedAt,
            Team: "Security Master",
            Counterparty: conflict.ProviderB,
            StateTransitions: [],
            SlaDueAt: slaDueAt,
            SlaBreached: slaBreached,
            SlaPolicyId: "security-master-conflict-sla");
    }

    private static ReconciliationBreakQueueItem BuildOverrideCase(
        OperatorOverridesDto overrides,
        string? actor)
    {
        var assignedTo = NormalizeActor(actor) ?? "security-master-steward";
        var route = UiApiRoutes.SecurityMasterOperatorOverrides.Replace(
            "{securityId:guid}",
            overrides.SecurityId.ToString("D"),
            StringComparison.Ordinal);
        var isApproved = overrides.ApprovalStatus == SecurityOverrideApprovalStatusDto.Approved;
        return new ReconciliationBreakQueueItem(
            BreakId: BuildOverrideCaseId(overrides.SecurityId),
            RunId: "security-master-overrides",
            StrategyName: "Security Master exception casework",
            Category: ReconciliationBreakCategory.ClassificationGap,
            Status: isApproved ? ReconciliationBreakQueueStatus.Resolved : ReconciliationBreakQueueStatus.Open,
            Variance: 0m,
            Reason: $"Security Master operator override is {overrides.ApprovalStatus}.",
            AssignedTo: assignedTo,
            DetectedAt: overrides.UpdatedAt,
            LastUpdatedAt: overrides.UpdatedAt,
            ReviewedBy: isApproved ? overrides.ReviewedBy : null,
            ReviewedAt: isApproved ? overrides.ReviewedAt : null,
            ResolvedBy: isApproved ? overrides.ReviewedBy : null,
            ResolvedAt: isApproved ? overrides.ReviewedAt : null,
            ResolutionNote: isApproved ? "Security Master override approved." : "Operator override requires reviewer approval.",
            Severity: isApproved ? ReconciliationBreakSeverity.Info : ReconciliationBreakSeverity.High,
            ExceptionRoute: "security-master/operator-overrides",
            ToleranceProfileId: "security-master-override",
            RequiredSignoffRole: "Security Master steward",
            SignoffStatus: isApproved ? "ready-for-signoff" : "pending-signoff",
            FundAccountId: null,
            ExplainabilitySummary: string.Join(
                ", ",
                $"securityId={overrides.SecurityId:D}",
                $"approvalStatus={overrides.ApprovalStatus}",
                $"overrideCount={overrides.Values.Count}",
                $"reasonCode={overrides.ReasonCode ?? "n/a"}"),
            RoutingTarget: route,
            RoutingDetail: overrides.SecurityId.ToString("D"),
            RecommendedAction: "Approve, reject, or remove the operator override before governed ledger, reconciliation, or report workflows consume it.",
            LifecycleState: isApproved ? ReconciliationCaseLifecycleState.Resolved : ReconciliationCaseLifecycleState.Open,
            LifecycleRationale: isApproved
                ? "Security Master operator override is approved."
                : "Auto-generated from a pending Security Master operator override.",
            ExternalAccountId: null,
            CustodianId: null,
            UpstreamSyncCursor: $"security-master-override:{overrides.SecurityId:N}:{overrides.UpdatedAt:O}:{overrides.ApprovalStatus}",
            LastUpstreamSyncAt: overrides.UpdatedAt,
            SignoffHistory: null,
            Team: "Security Master",
            StateTransitions: []);
    }

    private static string BuildConflictCaseId(Guid conflictId)
        => $"security-master:conflict:{conflictId:N}";

    private static string BuildOverrideCaseId(Guid securityId)
        => $"security-master:override:{securityId:N}";

    private static string NormalizeActor(string? actor)
        => NormalizeOptionalActor(actor) ?? DefaultActor;

    private static string? NormalizeOptionalActor(string? actor)
        => string.IsNullOrWhiteSpace(actor) ? null : actor.Trim();
}
