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

    /// <summary>Days to resolve a HardBlock data quality violation before SLA breach.</summary>
    public int QualityHardBlockDays { get; init; } = 1;

    /// <summary>Days to resolve an Error data quality violation before SLA breach.</summary>
    public int QualityErrorDays { get; init; } = 3;

    /// <summary>Days to resolve a Warning data quality violation before SLA breach.</summary>
    public int QualityWarningDays { get; init; } = 5;
}

/// <summary>
/// Outcome of synchronising a data quality report into the exception case queue.
/// </summary>
public sealed record SecurityMasterQualityCaseworkSyncResult(
    int Opened,
    int Reopened,
    int Refreshed,
    int AutoClosed,
    int FlappingLeftOpen);

/// <summary>
/// Projects Security Master exceptions into the shared reconciliation case queue.
/// </summary>
public sealed class SecurityMasterExceptionCaseworkService
{
    internal const string QualityRunId = "security-master-quality";

    private const string DefaultActor = "security-master-casework";
    private const int FlapGuardReopenThreshold = 3;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;
    private readonly ILogger<SecurityMasterExceptionCaseworkService> _logger;

    public SecurityMasterExceptionCaseworkService(
        IReconciliationBreakQueueRepository? breakQueueRepository,
        ILogger<SecurityMasterExceptionCaseworkService> logger)
    {
        _breakQueueRepository = breakQueueRepository;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns all open Security Master exception cases that have breached or are past their SLA deadline.
    /// </summary>
    public async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetAgingExceptionsAsync(
        CancellationToken ct = default)
    {
        if (_breakQueueRepository is null)
            return [];

        return await _breakQueueRepository
            .GetAgingByTeamAsync("Security Master", DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);
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
                .CreateIfMissingAsync(BuildConflictCase(conflict, actor), ct)
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
        var desired = BuildConflictCase(conflict, actor);
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

    /// <summary>
    /// Synchronises the outcome of a data quality run into the shared reconciliation case queue.
    /// New violations open cases keyed by (rule, security, field); repeat violations refresh the
    /// existing open case; cleared violations auto-close their case with run evidence. When a
    /// violation recurs after its case was resolved, a next-generation case (":g2", ":g3", ...)
    /// is opened under the same base key — the governed case lifecycle has no Resolved→Reopened
    /// transition, so recurrences get a fresh case with a fresh SLA clock and audit trail. Cases
    /// that recurred <see cref="FlapGuardReopenThreshold"/> or more times are left open for
    /// manual review, and operator-dismissed or signed-off cases are never regenerated.
    /// </summary>
    public async Task<SecurityMasterQualityCaseworkSyncResult> SyncQualityViolationCasesAsync(
        SecurityMasterQualityReportDto report,
        string? actor,
        CancellationToken ct = default)
    {
        if (_breakQueueRepository is null || report is null)
        {
            return new SecurityMasterQualityCaseworkSyncResult(0, 0, 0, 0, 0);
        }

        var normalizedActor = NormalizeActor(actor);
        var violationsByBaseId = new Dictionary<string, DataQualityRuleViolationDto>(StringComparer.Ordinal);
        foreach (var violation in report.Violations)
        {
            // First occurrence wins if a run reports the same (rule, security, field) twice.
            violationsByBaseId.TryAdd(BuildQualityCaseId(violation), violation);
        }

        var qualityCasesByBaseId = (await _breakQueueRepository.GetAllAsync(null, ct).ConfigureAwait(false))
            .Where(static item => string.Equals(item.RunId, QualityRunId, StringComparison.Ordinal))
            .GroupBy(static item => GetQualityBaseCaseId(item.BreakId), StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderByDescending(static item => GetQualityCaseGeneration(item.BreakId)).ToArray(),
                StringComparer.Ordinal);

        var opened = 0;
        var reopened = 0;
        var refreshed = 0;
        var autoClosed = 0;
        var flappingLeftOpen = 0;

        foreach (var (baseId, violation) in violationsByBaseId)
        {
            ct.ThrowIfCancellationRequested();

            if (!qualityCasesByBaseId.TryGetValue(baseId, out var generations))
            {
                await _breakQueueRepository
                    .CreateIfMissingAsync(
                        BuildQualityCase(baseId, generation: 1, violation, report.RunAt, normalizedActor), ct)
                    .ConfigureAwait(false);
                opened++;
                continue;
            }

            var openCase = generations.FirstOrDefault(static item =>
                item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview);
            if (openCase is not null)
            {
                await _breakQueueRepository.SaveAsync(
                    openCase with
                    {
                        Reason = BuildQualityReason(violation),
                        Severity = MapQualitySeverity(violation.Severity),
                        LastUpdatedAt = report.RunAt,
                        LastUpstreamSyncAt = report.RunAt,
                        UpstreamSyncCursor = BuildQualityCursor(
                            report.RunAt, GetQualityCaseGeneration(openCase.BreakId) - 1)
                    },
                    ct).ConfigureAwait(false);
                refreshed++;
                continue;
            }

            // Dismissed and signed-off cases reflect an explicit operator decision; do not fight it.
            var latest = generations[0];
            if (latest.Status is not ReconciliationBreakQueueStatus.Resolved
                || string.Equals(latest.ResolutionCode, "DismissedFalsePositive", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nextGeneration = GetQualityCaseGeneration(latest.BreakId) + 1;
            await _breakQueueRepository
                .CreateIfMissingAsync(
                    BuildQualityCase(
                        BuildQualityCaseId(violation, nextGeneration),
                        nextGeneration,
                        violation,
                        report.RunAt,
                        normalizedActor),
                    ct)
                .ConfigureAwait(false);
            reopened++;
        }

        foreach (var generations in qualityCasesByBaseId.Values)
        {
            foreach (var existing in generations)
            {
                ct.ThrowIfCancellationRequested();

                if (violationsByBaseId.ContainsKey(GetQualityBaseCaseId(existing.BreakId))
                    || existing.Status is not (ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview))
                {
                    continue;
                }

                if (GetQualityCaseGeneration(existing.BreakId) - 1 >= FlapGuardReopenThreshold)
                {
                    flappingLeftOpen++;
                    continue;
                }

                if (await TryAutoCloseQualityCaseAsync(existing, report.RunAt, normalizedActor, ct).ConfigureAwait(false))
                {
                    autoClosed++;
                }
            }
        }

        _logger.LogInformation(
            "Security Master quality casework sync: {Opened} opened, {Reopened} reopened, {Refreshed} refreshed, {AutoClosed} auto-closed, {FlappingLeftOpen} flapping left open.",
            opened, reopened, refreshed, autoClosed, flappingLeftOpen);

        return new SecurityMasterQualityCaseworkSyncResult(opened, reopened, refreshed, autoClosed, flappingLeftOpen);
    }

    private async Task<bool> TryAutoCloseQualityCaseAsync(
        ReconciliationBreakQueueItem existing,
        DateTimeOffset runAt,
        string actor,
        CancellationToken ct)
    {
        if (existing.Status == ReconciliationBreakQueueStatus.Open)
        {
            var review = await _breakQueueRepository!.StartReviewAsync(
                    new ReviewReconciliationBreakRequest(
                        existing.BreakId,
                        existing.AssignedTo ?? actor,
                        actor,
                        $"Quality run at {runAt:O} no longer reports this violation.",
                        "Security Master"),
                    ct)
                .ConfigureAwait(false);
            if (review.Status != ReconciliationBreakQueueTransitionStatus.Success)
            {
                _logger.LogWarning(
                    "Could not start review for Security Master quality case {BreakId}: {Error}",
                    existing.BreakId,
                    review.Error);
                return false;
            }
        }

        var resolve = await _breakQueueRepository!.ResolveAsync(
                new ResolveReconciliationBreakRequest(
                    existing.BreakId,
                    ReconciliationBreakQueueStatus.Resolved,
                    actor,
                    $"Resolved by quality run at {runAt:O}.",
                    "The data quality rule no longer reports a violation for this security."),
                ct)
            .ConfigureAwait(false);
        if (resolve.Status != ReconciliationBreakQueueTransitionStatus.Success)
        {
            _logger.LogWarning(
                "Could not resolve Security Master quality case {BreakId}: {Error}",
                existing.BreakId,
                resolve.Error);
            return false;
        }

        return true;
    }

    private static ReconciliationBreakQueueItem BuildQualityCase(
        string caseId,
        int generation,
        DataQualityRuleViolationDto violation,
        DateTimeOffset runAt,
        string actor)
    {
        var reopenCount = generation - 1;
        var isFlapping = reopenCount >= FlapGuardReopenThreshold;
        return new(
            BreakId: caseId,
            RunId: QualityRunId,
            StrategyName: "Security Master exception casework",
            Category: MapQualityCategory(violation.Category),
            Status: ReconciliationBreakQueueStatus.Open,
            Variance: 0m,
            Reason: BuildQualityReason(violation),
            AssignedTo: actor,
            DetectedAt: generation == 1 ? violation.DetectedAt : runAt,
            LastUpdatedAt: runAt,
            Severity: MapQualitySeverity(violation.Severity),
            ExceptionRoute: "security-master/quality",
            ToleranceProfileId: "security-master-quality",
            RequiredSignoffRole: "Security Master steward",
            SignoffStatus: "pending-signoff",
            FundAccountId: null,
            ExplainabilitySummary: string.Join(
                ", ",
                $"securityId={violation.SecurityId:D}",
                $"rule={violation.RuleId}",
                $"category={violation.Category}",
                $"field={violation.FieldPath ?? "record"}",
                $"severity={violation.Severity}",
                $"recurrences={reopenCount}"),
            RoutingTarget: UiApiRoutes.SecurityMasterQualityReportLatest,
            RoutingDetail: violation.SecurityId.ToString("D"),
            RecommendedAction: "Correct the underlying security data; the case auto-closes when a subsequent quality run passes.",
            LifecycleState: ReconciliationCaseLifecycleState.Open,
            LifecycleRationale: generation == 1
                ? "Auto-generated from a Security Master data quality violation."
                : isFlapping
                    ? $"Violation recurred {reopenCount} times after resolution — flapping; auto-close disabled pending manual review."
                    : $"Violation recurred after the generation {generation - 1} case was resolved.",
            ExternalAccountId: null,
            CustodianId: null,
            UpstreamSyncCursor: BuildQualityCursor(runAt, reopenCount),
            LastUpstreamSyncAt: runAt,
            Team: "Security Master",
            StateTransitions: []);
    }

    internal static string BuildQualityCaseId(DataQualityRuleViolationDto violation, int generation = 1)
    {
        var baseId = $"security-master:quality:{violation.RuleId.ToLowerInvariant()}:{violation.SecurityId:N}:{NormalizeFieldToken(violation.FieldPath)}";
        return generation <= 1 ? baseId : $"{baseId}:g{generation}";
    }

    internal static string GetQualityBaseCaseId(string breakId)
    {
        var index = breakId.LastIndexOf(":g", StringComparison.Ordinal);
        return index > 0 && int.TryParse(breakId[(index + 2)..], out var generation) && generation > 1
            ? breakId[..index]
            : breakId;
    }

    internal static int GetQualityCaseGeneration(string breakId)
    {
        var index = breakId.LastIndexOf(":g", StringComparison.Ordinal);
        return index > 0 && int.TryParse(breakId[(index + 2)..], out var generation) && generation > 1
            ? generation
            : 1;
    }

    private static string NormalizeFieldToken(string? fieldPath)
        => string.IsNullOrWhiteSpace(fieldPath)
            ? "record"
            : new string(fieldPath.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray());

    private static string BuildQualityReason(DataQualityRuleViolationDto violation)
        => $"Data quality rule {violation.RuleId} ({violation.RuleName}): {violation.Message}";

    private static string BuildQualityCursor(DateTimeOffset runAt, int reopenCount)
        => $"{QualityRunId}:{runAt:O}:reopens={reopenCount}";

    private static ReconciliationBreakSeverity MapQualitySeverity(DataQualityRuleSeverity severity) => severity switch
    {
        DataQualityRuleSeverity.HardBlock => ReconciliationBreakSeverity.Critical,
        DataQualityRuleSeverity.Error => ReconciliationBreakSeverity.High,
        _ => ReconciliationBreakSeverity.Medium
    };

    private static ReconciliationBreakCategory MapQualityCategory(DataQualityRuleCategory category) => category switch
    {
        DataQualityRuleCategory.StalenessMonitor => ReconciliationBreakCategory.TimingMismatch,
        DataQualityRuleCategory.ExternalComparison => ReconciliationBreakCategory.ExternalStatementMismatch,
        _ => ReconciliationBreakCategory.ClassificationGap
    };

    private static ReconciliationBreakQueueItem BuildConflictCase(
        SecurityMasterConflict conflict,
        string? actor)
    {
        var assignedTo = NormalizeActor(actor) ?? "security-master-steward";
        var route = UiApiRoutes.SecurityMasterConflicts;
        // SLA fields (SlaDueAt/SlaBreached/SlaPolicyId) are computed by the break queue
        // repository via SecurityMasterReconciliationSlaPolicyProvider, keyed off Team/RunId.
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
            StateTransitions: []);
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
