using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public interface IOperationsContinuityReconciliationBridge
{
    Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct = default);

    Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default);
}

public sealed class OperationsContinuityReconciliationBridge : IOperationsContinuityReconciliationBridge
{
    private readonly IOperationsContinuityWorkflowService _workflowService;
    private readonly IReconciliationRunService? _reconciliationRunService;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;

    private static readonly ReconciliationLaneDefinition[] ReconciliationLaneDefinitions =
    [
        new(
            "cash-reconciliation",
            "Cash reconciliation",
            "/workstation/accounting/reconciliation",
            breakCase => BreakContains(breakCase, "cash", "cash-balance", "missingcashcoverage", "cashmismatch")),
        new(
            "position-reconciliation",
            "Position reconciliation",
            "/workstation/accounting/reconciliation",
            breakCase => BreakContains(breakCase, "position", "portfolio", "securities", "security-master-coverage")),
        new(
            "trade-reconciliation",
            "Trade reconciliation",
            "/workstation/accounting/reconciliation",
            breakCase => BreakContains(breakCase, "trade", "fill", "order", "execution")),
        new(
            "income-reconciliation",
            "Income reconciliation",
            "/workstation/accounting/reconciliation",
            breakCase => BreakContains(breakCase, "income", "dividend", "interest", "coupon", "accrual")),
        new(
            "mbs-factor-reconciliation",
            "MBS factor reconciliation",
            "/workstation/accounting/reconciliation",
            breakCase => BreakContains(breakCase, "factor", "paydown", "principal", "schedule")),
        new(
            "bank-reconciliation",
            "Bank reconciliation",
            "/workstation/accounting/reconciliation",
            breakCase => BreakContains(breakCase, "bank", "externalstatement", "external statement")),
        new(
            "gl-reconciliation",
            "GL reconciliation support",
            "/workstation/accounting/ledger",
            breakCase => BreakContains(breakCase, "ledger", "journal", "gl", "general ledger", "missingledgercoverage"))
    ];

    public OperationsContinuityReconciliationBridge(
        IOperationsContinuityWorkflowService workflowService,
        IReconciliationRunService? reconciliationRunService = null,
        IReconciliationBreakQueueRepository? breakQueueRepository = null)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _reconciliationRunService = reconciliationRunService;
        _breakQueueRepository = breakQueueRepository;
    }

    public async Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct = default) =>
        await RunReconciliationCoreAsync(workflowId, request, accessScope: null, ct)
            .ConfigureAwait(false);

    public async Task<OperationsTransitionResultDto> RunReconciliationAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        return await RunReconciliationCoreAsync(workflowId, request, accessScope, ct).ConfigureAwait(false);
    }

    private async Task<OperationsTransitionResultDto> RunReconciliationCoreAsync(
        Guid workflowId,
        OperationsReconciliationRunRequestDto request,
        ReconciliationBreakQueueScope? accessScope,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ShouldUseDirectRequest(request))
        {
            return await _workflowService
                .RunReconciliationAsync(workflowId, request, ct)
                .ConfigureAwait(false);
        }

        if (accessScope is null)
        {
            return MissingReconciliationScope(
                "A tenant- and company-scoped reconciliation request context is required to consume retained casework.");
        }

        if (_reconciliationRunService is null)
        {
            return MissingReconciliationSource(
                "Reconciliation run service is not registered; submit explicit break cases and posture counts or register IReconciliationRunService.");
        }

        var detail = await ResolveReconciliationDetailAsync(request, ct).ConfigureAwait(false);
        if (detail is null)
        {
            return MissingReconciliationSource(
                "No reconciliation run detail was found for the requested source run or reconciliation run id.");
        }

        var breakQueueItems = await LoadBreakQueueLookupAsync(detail, accessScope, ct).ConfigureAwait(false);
        var bridgedRequest = BuildWorkflowRequest(request, detail, breakQueueItems);
        return await _workflowService
            .RunReconciliationAsync(workflowId, bridgedRequest, ct)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, ReconciliationBreakQueueItem>> LoadBreakQueueLookupAsync(
        ReconciliationRunDetail detail,
        ReconciliationBreakQueueScope accessScope,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return new Dictionary<string, ReconciliationBreakQueueItem>(StringComparer.OrdinalIgnoreCase);
        }

        var items = await _breakQueueRepository
            .GetAllAsync(accessScope, status: null, ct)
            .ConfigureAwait(false);
        var lookup = new Dictionary<string, ReconciliationBreakQueueItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.Where(item => IsRelatedBreakQueueItem(detail, item)))
        {
            foreach (var key in BuildBreakQueueLookupKeys(detail, item))
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    lookup.TryAdd(key.Trim(), item);
                }
            }
        }

        return lookup;
    }

    private async Task<ReconciliationRunDetail?> ResolveReconciliationDetailAsync(
        OperationsReconciliationRunRequestDto request,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.ReconciliationRunId))
        {
            return await _reconciliationRunService!
                .GetByIdAsync(request.ReconciliationRunId.Trim(), ct)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceRunId))
        {
            return await _reconciliationRunService!
                .GetLatestForRunAsync(request.SourceRunId.Trim(), ct)
                .ConfigureAwait(false);
        }

        return null;
    }

    private static OperationsReconciliationRunRequestDto BuildWorkflowRequest(
        OperationsReconciliationRunRequestDto request,
        ReconciliationRunDetail detail,
        IReadOnlyDictionary<string, ReconciliationBreakQueueItem> breakQueueItems)
    {
        var evidence = MergeEvidence(request.EvidenceLinks, detail);
        var breakCases = MapBreakCases(detail, evidence, breakQueueItems);
        return request with
        {
            BreakCases = breakCases,
            EvidenceLinks = evidence,
            SecurityCoverageIssueCount = detail.Summary.SecurityIssueCount,
            SecurityAccountingIssueCount = detail.Summary.SecurityMasterAccountingIssueCount,
            ExpectedAccountingEventCount = detail.Summary.ExpectedAccountingEventCount,
            ExpectedJournalPreviewCount = detail.Summary.ExpectedJournalPreviewCount,
            ReconciliationRunId = detail.Summary.ReconciliationRunId,
            SourceRunId = detail.Summary.RunId,
            ReconciliationLanes = BuildReconciliationLanes(detail, breakCases, evidence)
        };
    }

    private static IReadOnlyList<OperationsBreakCaseDto> MapBreakCases(
        ReconciliationRunDetail detail,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence,
        IReadOnlyDictionary<string, ReconciliationBreakQueueItem> breakQueueItems)
    {
        var reconciliationEvidence = evidence
            .Where(static link => string.Equals(link.Source, "reconciliation-run", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (reconciliationEvidence.Length == 0)
        {
            reconciliationEvidence = evidence.ToArray();
        }

        var reconciliationBreaks = detail.Breaks
            .Select(breakRow =>
            {
                var breakId = BuildBreakId(detail.Summary.ReconciliationRunId, breakRow.CheckId);
                var queueItem = FindBreakQueueItem(
                    breakQueueItems,
                    breakId,
                    breakRow.CheckId,
                    breakRow.CorrelationKeys?.ReconciliationCaseId);
                return BuildBreakCase(
                    breakId,
                    breakRow.CheckId,
                    breakRow.Category.ToString(),
                    breakRow.Severity,
                    breakRow.Status.ToString(),
                    queueItem,
                    detail.Summary.CreatedAt,
                    ExpectedSource: "reconciliation-run",
                    ActualSource: string.IsNullOrWhiteSpace(breakRow.MissingSource) ? null : breakRow.MissingSource.Trim(),
                    breakRow.ExpectedAmount,
                    breakRow.ActualAmount,
                    breakRow.Variance,
                    SecurityId: null,
                    Symbol: null,
                    SuggestedAction: breakRow.Reason,
                    reconciliationEvidence,
                    breakRow.CorrelationKeys ?? new OperationsContinuityCorrelationKeysDto(
                        RunId: detail.Summary.RunId,
                        ReconciliationCaseId: breakId));
            });

        var securityCoverageBreaks = (detail.SecurityCoverageIssues ?? Array.Empty<ReconciliationSecurityCoverageIssueDto>())
            .Select(issue =>
            {
                var breakId = BuildIssueBreakId(detail.Summary.ReconciliationRunId, "security-coverage", issue.Source, issue.Symbol, issue.Code);
                var checkId = string.IsNullOrWhiteSpace(issue.Code) ? "SM_RECON_SECURITY_UNRESOLVED" : issue.Code.Trim();
                var queueItem = FindBreakQueueItem(
                    breakQueueItems,
                    breakId,
                    checkId,
                    issue.Symbol);
                return BuildBreakCase(
                    breakId,
                    checkId,
                    "SecurityMasterCoverage",
                    issue.Severity,
                    ReconciliationBreakStatus.Open.ToString(),
                    queueItem,
                    detail.Summary.CreatedAt,
                    ExpectedSource: "security-master",
                    ActualSource: string.IsNullOrWhiteSpace(issue.Source) ? null : issue.Source.Trim(),
                    ExpectedAmount: null,
                    ActualAmount: null,
                    Variance: null,
                    SecurityId: null,
                    Symbol: string.IsNullOrWhiteSpace(issue.Symbol) ? null : issue.Symbol.Trim(),
                    SuggestedAction: issue.Reason,
                    BuildIssueEvidence(
                        reconciliationEvidence,
                        issue.EvidenceLink,
                        "security-master-coverage",
                        "Security Master coverage evidence",
                        detail.Summary.CreatedAt),
                    new OperationsContinuityCorrelationKeysDto(
                        RunId: detail.Summary.RunId,
                        ReconciliationCaseId: breakId));
            });

        var securityAccountingBreaks = (detail.SecurityMasterAccountingIssues ?? Array.Empty<SecurityMasterAccountingIssueDto>())
            .Select(issue =>
            {
                var breakId = BuildIssueBreakId(detail.Summary.ReconciliationRunId, "security-accounting", issue.Source, issue.Symbol, issue.Code);
                decimal? variance = issue.ExpectedAmount.HasValue && issue.ActualAmount.HasValue
                    ? issue.ActualAmount.Value - issue.ExpectedAmount.Value
                    : null;
                var queueItem = FindBreakQueueItem(
                    breakQueueItems,
                    breakId,
                    issue.Code,
                    issue.Symbol);
                return BuildBreakCase(
                    breakId,
                    issue.Code,
                    "SecurityMasterAccounting",
                    issue.Severity,
                    ReconciliationBreakStatus.Open.ToString(),
                    queueItem,
                    detail.Summary.CreatedAt,
                    ExpectedSource: "security-master-expected-event",
                    ActualSource: string.IsNullOrWhiteSpace(issue.Source) ? null : issue.Source.Trim(),
                    issue.ExpectedAmount,
                    issue.ActualAmount,
                    variance,
                    SecurityId: null,
                    Symbol: string.IsNullOrWhiteSpace(issue.Symbol) ? null : issue.Symbol.Trim(),
                    SuggestedAction: issue.Reason,
                    BuildIssueEvidence(
                        reconciliationEvidence,
                        issue.EvidenceLink,
                        "security-master-accounting",
                        "Security Master accounting evidence",
                        detail.Summary.CreatedAt),
                    new OperationsContinuityCorrelationKeysDto(
                        RunId: detail.Summary.RunId,
                        ReconciliationCaseId: breakId));
            });

        return reconciliationBreaks
            .Concat(securityCoverageBreaks)
            .Concat(securityAccountingBreaks)
            .DistinctBy(static breakCase => breakCase.BreakId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static OperationsBreakCaseDto BuildBreakCase(
        string breakId,
        string checkId,
        string category,
        ReconciliationBreakSeverity severity,
        string status,
        ReconciliationBreakQueueItem? queueItem,
        DateTimeOffset detectedAt,
        string? ExpectedSource,
        string? ActualSource,
        decimal? ExpectedAmount,
        decimal? ActualAmount,
        decimal? Variance,
        string? SecurityId,
        string? Symbol,
        string? SuggestedAction,
        IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
        OperationsContinuityCorrelationKeysDto? CorrelationKeys)
    {
        var slaDueAt = ResolveSlaDueAt(queueItem, severity, detectedAt);
        return new OperationsBreakCaseDto(
            breakId,
            checkId,
            category,
            severity.ToString(),
            status,
            Owner: ResolveOwner(queueItem),
            DueDate: slaDueAt.HasValue ? DateOnly.FromDateTime(slaDueAt.Value.UtcDateTime) : null,
            ExpectedSource,
            ActualSource,
            ExpectedAmount,
            ActualAmount,
            Variance,
            SecurityId,
            Symbol,
            SuggestedAction,
            EvidenceLinks,
            CorrelationKeys,
            EscalationLevel: queueItem?.Priority.ToString(),
            EscalationReason: queueItem?.RoutingDetail ?? queueItem?.LifecycleRationale,
            EscalatedAtUtc: queueItem?.LastActivityAt,
            SlaState: ResolveSlaState(queueItem, severity, status, detectedAt),
            SlaDueAtUtc: slaDueAt,
            Materiality: ResolveMateriality(queueItem, Variance, ExpectedAmount, ActualAmount),
            RootCauseCode: ResolveRootCauseCode(queueItem, category, ActualSource, SuggestedAction),
            ApprovalState: ResolveApprovalState(queueItem, severity, status),
            BlockedOutputs: ResolveBlockedOutputs(queueItem, category, severity, status));
    }

    private static bool IsRelatedBreakQueueItem(
        ReconciliationRunDetail detail,
        ReconciliationBreakQueueItem item)
    {
        return EqualsAny(item.RunId, detail.Summary.RunId, detail.Summary.ReconciliationRunId) ||
               EqualsAny(item.SourceImportId, detail.Summary.RunId, detail.Summary.ReconciliationRunId) ||
               EqualsAny(item.SourceReference, detail.Summary.RunId, detail.Summary.ReconciliationRunId) ||
               ContainsAny(item.BreakId, detail.Summary.RunId, detail.Summary.ReconciliationRunId) ||
               ContainsAny(item.SourceBreakId, detail.Summary.RunId, detail.Summary.ReconciliationRunId);
    }

    private static IEnumerable<string?> BuildBreakQueueLookupKeys(
        ReconciliationRunDetail detail,
        ReconciliationBreakQueueItem item)
    {
        yield return item.BreakId;
        yield return item.SourceBreakId;
        yield return item.SourceFingerprint;
        yield return item.SourceReference;
        yield return $"{detail.Summary.ReconciliationRunId}:{item.BreakId}";
        yield return $"{detail.Summary.RunId}:{item.BreakId}";

        if (!string.IsNullOrWhiteSpace(item.SourceBreakId))
        {
            yield return $"{detail.Summary.ReconciliationRunId}:{item.SourceBreakId}";
            yield return $"{detail.Summary.RunId}:{item.SourceBreakId}";
        }
    }

    private static ReconciliationBreakQueueItem? FindBreakQueueItem(
        IReadOnlyDictionary<string, ReconciliationBreakQueueItem> lookup,
        params string?[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            if (!string.IsNullOrWhiteSpace(key) && lookup.TryGetValue(key.Trim(), out var item))
            {
                return item;
            }
        }

        return null;
    }

    private static string? ResolveOwner(ReconciliationBreakQueueItem? queueItem)
    {
        if (!string.IsNullOrWhiteSpace(queueItem?.AssigneeDisplayName))
        {
            return queueItem.AssigneeDisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(queueItem?.AssigneeId))
        {
            return queueItem.AssigneeId.Trim();
        }

        return string.IsNullOrWhiteSpace(queueItem?.AssignedTo) ? null : queueItem.AssignedTo.Trim();
    }

    private static DateTimeOffset? ResolveSlaDueAt(
        ReconciliationBreakQueueItem? queueItem,
        ReconciliationBreakSeverity severity,
        DateTimeOffset detectedAt)
    {
        if (queueItem?.SlaDueAt is { } dueAt)
        {
            return dueAt;
        }

        var dueHours = severity switch
        {
            ReconciliationBreakSeverity.Critical => 4,
            ReconciliationBreakSeverity.High => 8,
            ReconciliationBreakSeverity.Medium => 24,
            _ => 40
        };
        return detectedAt.AddHours(dueHours);
    }

    private static string ResolveSlaState(
        ReconciliationBreakQueueItem? queueItem,
        ReconciliationBreakSeverity severity,
        string status,
        DateTimeOffset detectedAt)
    {
        if (queueItem is not null)
        {
            return queueItem.SlaState.ToString();
        }

        if (IsClosedBreakStatus(status))
        {
            return ReconciliationCaseSlaState.Stopped.ToString();
        }

        var dueAt = ResolveSlaDueAt(queueItem, severity, detectedAt);
        return dueAt.HasValue && DateTimeOffset.UtcNow >= dueAt.Value
            ? ReconciliationCaseSlaState.Breached.ToString()
            : ReconciliationCaseSlaState.OnTrack.ToString();
    }

    private static decimal? ResolveMateriality(
        ReconciliationBreakQueueItem? queueItem,
        decimal? variance,
        decimal? expectedAmount,
        decimal? actualAmount)
    {
        if (queueItem?.Score?.MaterialityComponent is { } materiality)
        {
            return materiality;
        }

        if (variance.HasValue)
        {
            return Math.Abs(variance.Value);
        }

        return expectedAmount.HasValue && actualAmount.HasValue
            ? Math.Abs(actualAmount.Value - expectedAmount.Value)
            : null;
    }

    private static string ResolveRootCauseCode(
        ReconciliationBreakQueueItem? queueItem,
        string category,
        string? actualSource,
        string? suggestedAction)
    {
        if (!string.IsNullOrWhiteSpace(queueItem?.RootCauseCode))
        {
            return queueItem.RootCauseCode.Trim();
        }

        var text = string.Join(' ', category, actualSource, suggestedAction).ToLowerInvariant();
        if (text.Contains("security", StringComparison.OrdinalIgnoreCase))
        {
            return "SecurityMasterCoverage";
        }

        if (text.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("coverage", StringComparison.OrdinalIgnoreCase))
        {
            return "SourceCoverageMissing";
        }

        if (text.Contains("timing", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("settlement", StringComparison.OrdinalIgnoreCase))
        {
            return "TimingDifference";
        }

        if (text.Contains("variance", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return "AmountVariance";
        }

        return "Unclassified";
    }

    private static string ResolveApprovalState(
        ReconciliationBreakQueueItem? queueItem,
        ReconciliationBreakSeverity severity,
        string status)
    {
        if (queueItem is not null)
        {
            if (queueItem.SignedOffAt.HasValue ||
                string.Equals(queueItem.SignoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(queueItem.SignoffStatus, "SignedOff", StringComparison.OrdinalIgnoreCase))
            {
                return "Approved";
            }

            if (queueItem.LifecycleState == ReconciliationCaseLifecycleState.AwaitingApproval)
            {
                return "AwaitingApproval";
            }

            if (!string.IsNullOrWhiteSpace(queueItem.SignoffStatus))
            {
                return queueItem.SignoffStatus.Trim();
            }

            if (!string.IsNullOrWhiteSpace(queueItem.RequiredSignoffRole))
            {
                return "ApprovalRequired";
            }
        }

        if (IsClosedBreakStatus(status))
        {
            return "NotRequired";
        }

        return IsHighRiskSeverity(severity) ? "ApprovalRequired" : "ReviewRequired";
    }

    private static IReadOnlyList<string> ResolveBlockedOutputs(
        ReconciliationBreakQueueItem? queueItem,
        string category,
        ReconciliationBreakSeverity severity,
        string status)
    {
        if (IsClosedBreakStatus(status))
        {
            return [];
        }

        var outputs = new List<string>();
        AddIfPresent(outputs, queueItem?.RoutingTarget);
        if (IsHighRiskSeverity(severity))
        {
            outputs.Add("Period close");
            outputs.Add("NAV readiness packet");
            outputs.Add("Report package release");
        }

        if (ContainsAny(category, "cash", "bank"))
        {
            outputs.Add("Cash confidence review");
        }

        if (ContainsAny(category, "security", "position", "factor"))
        {
            outputs.Add("NAV support package");
        }

        if (outputs.Count == 0)
        {
            outputs.Add("Close sign-off review");
        }

        return outputs
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsHighRiskSeverity(ReconciliationBreakSeverity severity) =>
        severity is ReconciliationBreakSeverity.Critical or ReconciliationBreakSeverity.High;

    private static bool EqualsAny(string? value, params string[] candidates) =>
        !string.IsNullOrWhiteSpace(value) &&
        candidates.Any(candidate => string.Equals(value.Trim(), candidate, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string? value, params string[] terms) =>
        !string.IsNullOrWhiteSpace(value) &&
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static void AddIfPresent(ICollection<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> MergeEvidence(
        IReadOnlyList<OperationsEvidenceLinkDto>? requestEvidence,
        ReconciliationRunDetail detail)
    {
        var merged = new List<OperationsEvidenceLinkDto>(requestEvidence ?? []);
        var reconciliationEvidenceId = $"reconciliation-run:{detail.Summary.ReconciliationRunId}";
        if (merged.All(link => !string.Equals(link.EvidenceId, reconciliationEvidenceId, StringComparison.OrdinalIgnoreCase)))
        {
            merged.Add(new OperationsEvidenceLinkDto(
                reconciliationEvidenceId,
                "Reconciliation run detail",
                $"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(detail.Summary.ReconciliationRunId)}",
                "reconciliation-run",
                detail.Summary.CreatedAt));
        }

        if (detail.Summary.BankTransactionCount > 0)
        {
            var bankEvidenceId = $"bank-normalized-activity:{detail.Summary.ReconciliationRunId}";
            if (merged.All(link => !string.Equals(link.EvidenceId, bankEvidenceId, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(new OperationsEvidenceLinkDto(
                    bankEvidenceId,
                    $"{detail.Summary.BankTransactionCount} normalized bank transaction(s)",
                    $"/api/workstation/reconciliation/runs/{Uri.EscapeDataString(detail.Summary.ReconciliationRunId)}",
                    "bank-normalized-activity",
                    detail.Summary.CreatedAt));
            }
        }

        return merged
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<OperationsReconciliationLaneSummaryDto> BuildReconciliationLanes(
        ReconciliationRunDetail detail,
        IReadOnlyList<OperationsBreakCaseDto> breakCases,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence)
    {
        return ReconciliationLaneDefinitions
            .Select(definition => BuildReconciliationLane(definition, detail, breakCases, evidence))
            .ToArray();
    }

    private static OperationsReconciliationLaneSummaryDto BuildReconciliationLane(
        ReconciliationLaneDefinition definition,
        ReconciliationRunDetail detail,
        IReadOnlyList<OperationsBreakCaseDto> breakCases,
        IReadOnlyList<OperationsEvidenceLinkDto> evidence)
    {
        var laneBreaks = breakCases
            .Where(definition.Matches)
            .ToArray();
        var openBreaks = laneBreaks
            .Where(static item => !IsClosedBreakStatus(item.Status))
            .ToArray();
        var hasCriticalBreak = openBreaks.Any(static item =>
            string.Equals(item.Severity, ReconciliationBreakSeverity.Critical.ToString(), StringComparison.OrdinalIgnoreCase));
        var status = hasCriticalBreak
            ? OperationsReconciliationLaneStatusDto.Blocked
            : openBreaks.Length > 0
                ? OperationsReconciliationLaneStatusDto.ReviewRequired
                : OperationsReconciliationLaneStatusDto.Ready;
        var retainedEvidence = laneBreaks
            .SelectMany(static breakCase => breakCase.EvidenceLinks)
            .Concat(status == OperationsReconciliationLaneStatusDto.Ready ? evidence : [])
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OperationsReconciliationLaneSummaryDto(
            definition.LaneId,
            definition.Label,
            status,
            status == OperationsReconciliationLaneStatusDto.Ready,
            openBreaks.Length,
            BuildReconciliationLaneSummary(definition, detail, status, openBreaks.Length),
            definition.RouteHint,
            retainedEvidence,
            status == OperationsReconciliationLaneStatusDto.Ready
                ? []
                : [$"Resolve or assign {definition.Label.ToLowerInvariant()} breaks and retain evidence."]);
    }

    private static string BuildReconciliationLaneSummary(
        ReconciliationLaneDefinition definition,
        ReconciliationRunDetail detail,
        OperationsReconciliationLaneStatusDto status,
        int openBreakCount)
    {
        if (status == OperationsReconciliationLaneStatusDto.Blocked)
        {
            return $"{definition.Label} has {openBreakCount} open critical break(s) from reconciliation run {detail.Summary.ReconciliationRunId}.";
        }

        if (status == OperationsReconciliationLaneStatusDto.ReviewRequired)
        {
            return $"{definition.Label} has {openBreakCount} open break(s) requiring controller review from reconciliation run {detail.Summary.ReconciliationRunId}.";
        }

        return definition.LaneId switch
        {
            "bank-reconciliation" when detail.Summary.BankTransactionCount > 0 =>
                $"{definition.Label} is covered by {detail.Summary.BankTransactionCount} normalized bank transaction(s).",
            "gl-reconciliation" when detail.Summary.ExpectedJournalPreviewCount > 0 =>
                $"{definition.Label} is covered by {detail.Summary.ExpectedJournalPreviewCount} expected journal preview(s).",
            "income-reconciliation" when detail.Summary.ExpectedAccountingEventCount > 0 =>
                $"{definition.Label} is covered by {detail.Summary.ExpectedAccountingEventCount} expected accounting event(s).",
            _ => $"{definition.Label} is covered by reconciliation run {detail.Summary.ReconciliationRunId}."
        };
    }

    private static bool ShouldUseDirectRequest(OperationsReconciliationRunRequestDto request) =>
        string.IsNullOrWhiteSpace(request.SourceRunId) &&
        string.IsNullOrWhiteSpace(request.ReconciliationRunId);

    private static string BuildBreakId(string reconciliationRunId, string checkId) =>
        string.IsNullOrWhiteSpace(checkId)
            ? reconciliationRunId
            : $"{reconciliationRunId}:{checkId.Trim()}";

    private static string BuildIssueBreakId(
        string reconciliationRunId,
        string issueKind,
        string source,
        string? symbol,
        string? code) =>
        string.Join(
            ":",
            new[]
            {
                reconciliationRunId,
                issueKind,
                NormalizeBreakIdPart(source),
                NormalizeBreakIdPart(symbol),
                NormalizeBreakIdPart(code)
            });

    private static IReadOnlyList<OperationsEvidenceLinkDto> BuildIssueEvidence(
        IReadOnlyList<OperationsEvidenceLinkDto> fallbackEvidence,
        string? issueRoute,
        string source,
        string label,
        DateTimeOffset capturedAt)
    {
        var links = new List<OperationsEvidenceLinkDto>();
        if (!string.IsNullOrWhiteSpace(issueRoute))
        {
            links.Add(new OperationsEvidenceLinkDto(
                $"{source}:{NormalizeBreakIdPart(issueRoute)}",
                label,
                issueRoute.Trim(),
                source,
                capturedAt));
        }

        links.AddRange(fallbackEvidence);
        return links
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeBreakIdPart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var trimmed = value.Trim();
        var chars = trimmed
            .Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static bool IsClosedBreakStatus(string? status) =>
        string.Equals(status, ReconciliationBreakStatus.Resolved.ToString(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, ReconciliationBreakStatus.Matched.ToString(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Dismissed", StringComparison.OrdinalIgnoreCase);

    private static bool BreakContains(OperationsBreakCaseDto breakCase, params string[] terms)
    {
        var searchable = string.Join(
            ' ',
            breakCase.BreakId,
            breakCase.CheckId,
            breakCase.Category,
            breakCase.ExpectedSource,
            breakCase.ActualSource,
            breakCase.Symbol,
            breakCase.SuggestedAction);
        return terms.Any(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ReconciliationLaneDefinition(
        string LaneId,
        string Label,
        string RouteHint,
        Func<OperationsBreakCaseDto, bool> Matches);

    private static OperationsTransitionResultDto MissingReconciliationSource(string message) =>
        new(
            Success: false,
            ErrorCode: "RECONCILIATION_RUN_NOT_FOUND",
            ErrorMessage: message,
            Workflow: null,
            Blockers:
            [
                new OperationsWorkflowBlockerDto(
                    "RECONCILIATION_RUN_NOT_FOUND",
                    message,
                    OperationsGateKeyDto.Reconciliation,
                    "Error",
                    [])
            ],
            NextActions: []);

    private static OperationsTransitionResultDto MissingReconciliationScope(string message) =>
        new(
            Success: false,
            ErrorCode: "RECONCILIATION_SCOPE_REQUIRED",
            ErrorMessage: message,
            Workflow: null,
            Blockers:
            [
                new OperationsWorkflowBlockerDto(
                    "RECONCILIATION_SCOPE_REQUIRED",
                    message,
                    OperationsGateKeyDto.Reconciliation,
                    "Error",
                    [])
            ],
            NextActions: []);
}
