using System.Globalization;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed class FinancialOperationsCommandCenterReadService : IFinancialOperationsCommandCenterReadService
{
    private readonly IOperationsContinuityWorkflowService _workflowService;
    private readonly IOperationsCloseCalendarService? _closeCalendarService;
    private readonly IPrivateCapitalCloseCockpitService? _privateCapitalCloseCockpitService;

    public FinancialOperationsCommandCenterReadService(
        IOperationsContinuityWorkflowService workflowService,
        IOperationsCloseCalendarService? closeCalendarService = null,
        IPrivateCapitalCloseCockpitService? privateCapitalCloseCockpitService = null)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _closeCalendarService = closeCalendarService;
        _privateCapitalCloseCockpitService = privateCapitalCloseCockpitService;
    }

    public async Task<FinancialOperationsCommandCenterDto> GetCommandCenterAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        Guid? fundAccountId = null,
        string? periodId = null,
        string? entityId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var summaries = await _workflowService
            .ListAsync(fundAccountId, periodId, status: null, ct, ledgerBookId: ledgerBookId)
            .ConfigureAwait(false);
        var workflows = new List<OperationsContinuityWorkflowDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            ct.ThrowIfCancellationRequested();
            var workflow = await _workflowService.GetAsync(summary.WorkflowId, ct).ConfigureAwait(false);
            if (workflow is not null)
            {
                workflows.Add(workflow);
            }
        }

        var activeWorkflow = ResolveActiveWorkflow(workflows);
        var effectiveFundAccountId = fundAccountId ?? activeWorkflow?.FundAccountId;
        var effectivePeriodId = periodId ?? activeWorkflow?.PeriodId;
        var closeCalendar = _closeCalendarService is null
            ? null
            : await _closeCalendarService
                .GetCalendarAsync(effectiveFundAccountId, effectivePeriodId, ct)
                .ConfigureAwait(false);
        var privateCapitalCloseCockpit = _privateCapitalCloseCockpitService is null
            ? null
            : await _privateCapitalCloseCockpitService
                .GetCockpitAsync(fundProfileId, ledgerBookId, effectiveFundAccountId, effectivePeriodId, entityId, ct)
                .ConfigureAwait(false);

        var rows = new List<FinancialOperationsQueueRowDto>();
        if (activeWorkflow is not null)
        {
            AddWorkflowRows(rows, activeWorkflow);
        }

        if (closeCalendar is not null)
        {
            AddCloseCalendarRows(rows, closeCalendar);
        }

        if (privateCapitalCloseCockpit is not null)
        {
            AddPrivateCapitalRows(rows, privateCapitalCloseCockpit);
        }

        var orderedRows = rows
            .OrderBy(static row => row.SortOrder)
            .ThenByDescending(static row => row.IsBlocked)
            .ThenBy(static row => row.KindLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var blockedCount = orderedRows.Count(static row => row.IsBlocked);
        var reviewCount = orderedRows.Length - blockedCount;
        var isReady = activeWorkflow is not null
            && (activeWorkflow.CloseReadiness?.IsReadyToClose == true || activeWorkflow.Status is OperationsWorkflowStatusDto.ReadyForClose or OperationsWorkflowStatusDto.Closed)
            && orderedRows.Length == 0
            && (privateCapitalCloseCockpit?.IsReadyToClose ?? true);
        var status = activeWorkflow is null && privateCapitalCloseCockpit is null
            ? "Unavailable"
            : blockedCount > 0
                ? "Blocked"
                : isReady
                    ? "Ready"
                    : "AtRisk";

        return new FinancialOperationsCommandCenterDto(
            DateTimeOffset.UtcNow,
            fundProfileId,
            ledgerBookId ?? activeWorkflow?.LedgerBookId ?? privateCapitalCloseCockpit?.LedgerBookId,
            effectiveFundAccountId ?? privateCapitalCloseCockpit?.FundAccountId,
            effectivePeriodId ?? privateCapitalCloseCockpit?.PeriodId,
            status,
            isReady,
            BuildSummary(status, orderedRows.Length, blockedCount, reviewCount),
            orderedRows.Length,
            blockedCount,
            reviewCount,
            BuildMetrics(activeWorkflow, closeCalendar, privateCapitalCloseCockpit, orderedRows),
            orderedRows,
            activeWorkflow,
            closeCalendar,
            privateCapitalCloseCockpit);
    }

    private static OperationsContinuityWorkflowDto? ResolveActiveWorkflow(IReadOnlyList<OperationsContinuityWorkflowDto> workflows)
        => workflows
            .OrderBy(static workflow => IsClosedWorkflow(workflow) ? 1 : 0)
            .ThenByDescending(static workflow => workflow.UpdatedAtUtc)
            .FirstOrDefault();

    private static bool IsClosedWorkflow(OperationsContinuityWorkflowDto workflow)
        => workflow.Status is OperationsWorkflowStatusDto.Closed;

    private static void AddWorkflowRows(ICollection<FinancialOperationsQueueRowDto> rows, OperationsContinuityWorkflowDto workflow)
    {
        foreach (var breakCase in (workflow.BreakCases ?? Array.Empty<OperationsBreakCaseDto>())
            .Where(static item => !IsBreakCaseClosed(item)))
        {
            var blockedOutputs = breakCase.BlockedOutputs?
                .Where(static output => !string.IsNullOrWhiteSpace(output))
                .ToArray() ?? [];
            var escalation = string.IsNullOrWhiteSpace(breakCase.EscalationLevel)
                ? "No escalation recorded"
                : string.IsNullOrWhiteSpace(breakCase.EscalationReason)
                    ? breakCase.EscalationLevel!
                    : $"{breakCase.EscalationLevel}: {breakCase.EscalationReason}";
            var action = string.IsNullOrWhiteSpace(breakCase.SuggestedAction)
                ? blockedOutputs.Length == 0 ? "Resolve or assign the reconciliation break." : $"Unblock {string.Join(", ", blockedOutputs)}."
                : breakCase.SuggestedAction!;

            rows.Add(new FinancialOperationsQueueRowDto(
                $"break:{breakCase.BreakId}",
                "reconciliation-break",
                "Break",
                breakCase.BreakId,
                FormatStatusLabel(breakCase.Status),
                $"{breakCase.Category} / {breakCase.CheckId}; {escalation}",
                string.IsNullOrWhiteSpace(breakCase.Owner) ? "Unassigned" : breakCase.Owner!,
                BuildBreakCaseDueLabel(breakCase),
                FormatEvidenceCount(breakCase.EvidenceLinks.Count),
                action,
                breakCase.EvidenceLinks.FirstOrDefault()?.Route,
                IsBreakCaseBlocked(breakCase),
                100,
                workflow.WorkflowId,
                breakCase.EvidenceLinks));
        }

        foreach (var task in (workflow.CloseChecklist ?? Array.Empty<OperationsCloseChecklistTaskDto>())
            .Where(static item => !IsChecklistTaskComplete(item)))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"checklist:{task.TaskId}",
                "close-checklist",
                "Checklist",
                string.IsNullOrWhiteSpace(task.Label) ? task.Gate.ToString() : task.Label,
                FormatStatusLabel(task.Status),
                string.IsNullOrWhiteSpace(task.RequiredEvidence) ? "Required evidence pending." : task.RequiredEvidence,
                string.IsNullOrWhiteSpace(task.Owner) ? "Owner pending" : task.Owner,
                BuildChecklistDueLabel(task),
                string.IsNullOrWhiteSpace(task.EvidencePointer) ? "Evidence pointer pending" : task.EvidencePointer!,
                string.IsNullOrWhiteSpace(task.BlockingReason)
                    ? task.CanAcknowledge ? "Ready for acknowledgement." : "Complete checklist evidence and approvals."
                    : task.BlockingReason!,
                task.RemediationRoute,
                IsChecklistTaskBlocked(task),
                200,
                workflow.WorkflowId));
        }

        foreach (var approval in (workflow.Approvals ?? Array.Empty<OperationsApprovalDto>())
            .Where(static item => item.Status is not OperationsApprovalStateDto.Approved)
            .OrderByDescending(static item => item.DecidedAtUtc ?? item.SubmittedAtUtc ?? DateTimeOffset.MinValue))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"approval:{approval.ApprovalId}",
                "approval",
                "Approval",
                approval.ApprovalId,
                FormatStatusLabel(approval.Status.ToString()),
                string.IsNullOrWhiteSpace(approval.Rationale) ? "Approval rationale pending." : approval.Rationale!,
                BuildApprovalOwnerLabel(approval.Operator, approval.Reviewer),
                BuildApprovalDueLabel(approval.SubmittedAtUtc, approval.DecidedAtUtc),
                FormatEvidenceCount(approval.EvidenceLinks.Count),
                approval.Status is OperationsApprovalStateDto.Rejected
                    ? "Resolve rejection before close sign-off."
                    : "Complete workflow approval.",
                approval.EvidenceLinks.FirstOrDefault()?.Route,
                approval.Status is not OperationsApprovalStateDto.Approved,
                300,
                workflow.WorkflowId,
                approval.EvidenceLinks));
        }

        foreach (var lane in (workflow.ReconciliationLanes ?? Array.Empty<OperationsReconciliationLaneSummaryDto>())
            .Where(static item => !item.IsReady || item.BreakCount > 0))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"reconciliation-lane:{lane.LaneId}",
                "reconciliation-lane",
                "Reconciliation",
                string.IsNullOrWhiteSpace(lane.Label) ? lane.LaneId : lane.Label,
                FormatReconciliationLaneStatusLabel(lane.Status),
                string.IsNullOrWhiteSpace(lane.Summary) ? "Reconciliation lane requires review." : lane.Summary,
                "Accounting operations",
                FormatBreakCount(lane.BreakCount),
                FormatEvidenceCount(lane.EvidenceLinks.Count),
                FormatRequiredActions(lane.RequiredActions),
                lane.RouteHint,
                lane.Status is OperationsReconciliationLaneStatusDto.Blocked or OperationsReconciliationLaneStatusDto.Missing,
                400,
                workflow.WorkflowId,
                lane.EvidenceLinks));
        }

        foreach (var package in (workflow.EvidencePackages ?? Array.Empty<OperationsEvidencePackageSummaryDto>())
            .Where(static item => !item.IsReady))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"evidence-package:{package.PackageId}",
                "evidence-package",
                "Evidence package",
                string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                FormatEvidenceStatusLabel(package.Status),
                string.IsNullOrWhiteSpace(package.Summary) ? "Evidence package requires review." : package.Summary,
                "Evidence operations",
                FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                FormatEvidenceCount(package.EvidenceLinkCount),
                FormatRequiredActions(package.RequiredActions),
                package.RouteHint,
                IsEvidenceStatusBlocked(package.Status),
                500,
                workflow.WorkflowId,
                package.EvidenceLinks));
        }
    }

    private static void AddCloseCalendarRows(ICollection<FinancialOperationsQueueRowDto> rows, OperationsCloseCalendarDto calendar)
    {
        foreach (var item in calendar.Items.Where(static item => !item.IsReadyToClose || item.BlockerCount > 0))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"close-calendar:{item.WorkflowId:D}:{item.NextDueTaskId ?? "period"}",
                "close-calendar",
                "Close calendar",
                string.IsNullOrWhiteSpace(item.NextDueLabel) ? item.NextDueTaskId ?? item.PeriodId : item.NextDueLabel,
                item.IsReadyToClose ? "Review" : "Blocked",
                BuildCloseCalendarDetail(item),
                string.IsNullOrWhiteSpace(item.NextDueOwner) ? "Owner pending" : item.NextDueOwner,
                item.NextDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Due date pending",
                $"{item.CompletedApprovalCount.ToString("N0", CultureInfo.InvariantCulture)}/{item.RequiredApprovalCount.ToString("N0", CultureInfo.InvariantCulture)} approvals",
                FormatCloseCalendarAction(item),
                item.Route,
                !item.IsReadyToClose || item.BlockerCount > 0,
                600,
                item.WorkflowId));
        }
    }

    private static void AddPrivateCapitalRows(ICollection<FinancialOperationsQueueRowDto> rows, PrivateCapitalCloseCockpitDto cockpit)
    {
        foreach (var lane in cockpit.Lanes.Where(static item => !item.IsReady))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"private-capital-lane:{lane.LaneId}",
                "private-capital-close-lane",
                "Private-capital close",
                string.IsNullOrWhiteSpace(lane.Label) ? lane.LaneId : lane.Label,
                FormatEvidenceStatusLabel(lane.Status),
                string.IsNullOrWhiteSpace(lane.Summary) ? "Private-capital close lane requires review." : lane.Summary,
                "Fund operations",
                "Close lane",
                FormatEvidenceCount(lane.EvidenceLinkCount),
                FormatRequiredActions(lane.RequiredActions),
                lane.Route,
                IsEvidenceStatusBlocked(lane.Status),
                700,
                EvidenceLinks: lane.EvidenceLinks));
        }

        foreach (var package in cockpit.EvidencePackages.Where(static item => !item.IsReady))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"private-capital-package:{package.PackageId}",
                "private-capital-evidence-package",
                "Private-capital package",
                string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                FormatEvidenceStatusLabel(package.Status),
                string.IsNullOrWhiteSpace(package.Summary) ? "Private-capital evidence package requires review." : package.Summary,
                "Fund operations",
                FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                FormatEvidenceCount(package.EvidenceLinkCount),
                FormatRequiredActions(package.RequiredActions),
                package.RouteHint,
                IsEvidenceStatusBlocked(package.Status),
                800,
                EvidenceLinks: package.EvidenceLinks));
        }

        foreach (var package in cockpit.NavSupportPackages.Where(static item => !item.IsReady))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"nav-support:{package.PackageId}",
                "nav-support",
                "NAV support",
                string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                FormatEvidenceStatusLabel(package.Status),
                string.IsNullOrWhiteSpace(package.Summary) ? "NAV support package requires review." : package.Summary,
                "Fund operations",
                FormatNavSupportComponentLabel(package.Components),
                FormatEvidenceCount(package.EvidenceLinkCount),
                FormatRequiredActions(package.RequiredActions),
                package.Route,
                IsEvidenceStatusBlocked(package.Status),
                900,
                EvidenceLinks: package.EvidenceLinks));
        }

        foreach (var approval in cockpit.ApprovalHistory
            .Where(static item => item.Status is not OperationsApprovalStateDto.Approved)
            .OrderByDescending(static item => item.DecidedAtUtc ?? item.SubmittedAtUtc ?? DateTimeOffset.MinValue))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"private-capital-approval:{approval.ApprovalId}",
                "private-capital-approval",
                "Private-capital approval",
                approval.ApprovalId,
                FormatStatusLabel(approval.Status.ToString()),
                string.IsNullOrWhiteSpace(approval.Rationale) ? "Approval rationale pending." : approval.Rationale!,
                BuildApprovalOwnerLabel(approval.Operator, approval.Reviewer),
                BuildApprovalDueLabel(approval.SubmittedAtUtc, approval.DecidedAtUtc),
                FormatEvidenceCount(approval.EvidenceLinkCount),
                approval.Status is OperationsApprovalStateDto.Rejected
                    ? "Resolve rejection before close sign-off."
                    : "Complete private-capital approval.",
                approval.WorkflowRoute,
                approval.Status is OperationsApprovalStateDto.Rejected,
                1000,
                approval.WorkflowId,
                approval.EvidenceLinks));
        }
    }

    private static IReadOnlyList<FinancialOperationsCommandCenterMetricDto> BuildMetrics(
        OperationsContinuityWorkflowDto? workflow,
        OperationsCloseCalendarDto? closeCalendar,
        PrivateCapitalCloseCockpitDto? cockpit,
        IReadOnlyList<FinancialOperationsQueueRowDto> rows)
    {
        var openBreakCount = workflow?.BreakCases.Count(static item => !IsBreakCaseClosed(item)) ?? 0;
        var missingEvidenceCount = rows.Count(static row =>
            row.SourceKind.Contains("evidence", StringComparison.OrdinalIgnoreCase)
            || row.SourceKind.Contains("nav-support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SourceKind, "close-checklist", StringComparison.OrdinalIgnoreCase));
        var pendingApprovalCount = rows.Count(static row => row.SourceKind.Contains("approval", StringComparison.OrdinalIgnoreCase));
        var calendarBlockedCount = closeCalendar?.Items.Count(static item => !item.IsReadyToClose || item.BlockerCount > 0) ?? 0;

        return
        [
            new("period", "Period status", workflow?.CloseReadiness?.Severity ?? workflow?.Status.ToString() ?? "Detail pending", workflow is null ? "No active close workflow matched the requested scope." : $"Workflow {workflow.WorkflowId:D} for {workflow.PeriodId}.", workflow?.CloseReadiness?.IsReadyToClose == true ? "Ready" : "Review", workflow is null ? null : $"/api/workstation/operations/continuity/{workflow.WorkflowId:D}"),
            new("breaks", "Open breaks", openBreakCount.ToString(CultureInfo.InvariantCulture), openBreakCount == 0 ? "No open reconciliation break cases are surfaced." : FormatBreakCount(openBreakCount), openBreakCount == 0 ? "Ready" : "Review", null),
            new("evidence", "Missing support", missingEvidenceCount.ToString(CultureInfo.InvariantCulture), missingEvidenceCount == 0 ? "Required evidence packages are not blocking completion." : "Evidence, checklist, or NAV support is incomplete.", missingEvidenceCount == 0 ? "Ready" : "Blocked", null),
            new("approvals", "Pending approvals", pendingApprovalCount.ToString(CultureInfo.InvariantCulture), pendingApprovalCount == 0 ? "No pending approval rows are surfaced." : "Approval rows must clear before completion.", pendingApprovalCount == 0 ? "Ready" : "Blocked", null),
            new("calendar", "Close calendar", calendarBlockedCount.ToString(CultureInfo.InvariantCulture), calendarBlockedCount == 0 ? "Close calendar items are ready or unavailable." : "Close-calendar due tasks remain blocked or under review.", calendarBlockedCount == 0 ? "Ready" : "Blocked", null),
            new("private-capital", "Private-capital close", cockpit?.OverallStatus.ToString() ?? "Unavailable", cockpit is null ? "Private-capital close cockpit is not registered." : cockpit.IsReadyToClose ? "Private-capital close cockpit is ready." : cockpit.ReadyLaneCount + "/" + cockpit.Lanes.Count + " close lanes ready.", cockpit?.IsReadyToClose == true ? "Ready" : "Review", cockpit?.CockpitRoute),
            new("queue", "Active queue", rows.Count.ToString(CultureInfo.InvariantCulture), BuildSummary(rows.Any(static row => row.IsBlocked) ? "Blocked" : "AtRisk", rows.Count, rows.Count(static row => row.IsBlocked), rows.Count(static row => !row.IsBlocked)), rows.Any(static row => row.IsBlocked) ? "Blocked" : "Review", null)
        ];
    }

    private static string BuildSummary(string status, int itemCount, int blockedCount, int reviewCount)
        => status switch
        {
            "Ready" => "Financial Operations completion is unblocked; retained evidence, approvals, close support, and private-capital support are ready.",
            "Unavailable" => "Financial Operations command center has no matching workflow or private-capital close cockpit for the requested scope.",
            "Blocked" => $"{blockedCount.ToString("N0", CultureInfo.InvariantCulture)} blocked item(s) and {reviewCount.ToString("N0", CultureInfo.InvariantCulture)} review item(s) must clear before completion.",
            _ => itemCount == 0
                ? "Financial Operations command center is waiting on refreshed workflow evidence."
                : $"{itemCount.ToString("N0", CultureInfo.InvariantCulture)} active item(s) require controller review before completion."
        };

    private static bool IsBreakCaseClosed(OperationsBreakCaseDto breakCase)
        => string.Equals(breakCase.Status, "Resolved", StringComparison.OrdinalIgnoreCase)
           || string.Equals(breakCase.Status, "Closed", StringComparison.OrdinalIgnoreCase)
           || string.Equals(breakCase.Status, "Dismissed", StringComparison.OrdinalIgnoreCase);

    private static bool IsBreakCaseBlocked(OperationsBreakCaseDto breakCase)
        => string.Equals(breakCase.Severity, "Critical", StringComparison.OrdinalIgnoreCase)
           || string.Equals(breakCase.Status, "Blocked", StringComparison.OrdinalIgnoreCase)
           || breakCase.BlockedOutputs is { Count: > 0 };

    private static bool IsChecklistTaskComplete(OperationsCloseChecklistTaskDto task)
        => string.Equals(task.Status, "Complete", StringComparison.OrdinalIgnoreCase)
           || string.Equals(task.Status, "Acknowledged", StringComparison.OrdinalIgnoreCase)
           || task.AcknowledgedAtUtc is not null;

    private static bool IsChecklistTaskBlocked(OperationsCloseChecklistTaskDto task)
        => string.Equals(task.Status, "Blocked", StringComparison.OrdinalIgnoreCase)
           || !string.IsNullOrWhiteSpace(task.BlockingReason)
           || string.IsNullOrWhiteSpace(task.EvidencePointer)
           || task.RequiredApprovalCount > 0 && !task.CanAcknowledge;

    private static bool IsEvidenceStatusBlocked(EvidenceStatusDto status)
        => status is EvidenceStatusDto.Blocked or EvidenceStatusDto.Missing;

    private static string BuildBreakCaseDueLabel(OperationsBreakCaseDto breakCase)
    {
        if (breakCase.SlaDueAtUtc is not null)
        {
            return $"{breakCase.SlaState ?? "SLA"} {breakCase.SlaDueAtUtc:yyyy-MM-dd HH:mm}Z";
        }

        return breakCase.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Due date pending";
    }

    private static string BuildChecklistDueLabel(OperationsCloseChecklistTaskDto task)
        => task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
           ?? task.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
           ?? "Due date pending";

    private static string BuildApprovalOwnerLabel(string? operatorName, string? reviewer)
    {
        if (!string.IsNullOrWhiteSpace(reviewer))
        {
            return $"Reviewer {reviewer}";
        }

        return string.IsNullOrWhiteSpace(operatorName) ? "Reviewer pending" : $"Operator {operatorName}";
    }

    private static string BuildApprovalDueLabel(DateTimeOffset? submittedAtUtc, DateTimeOffset? decidedAtUtc)
        => decidedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
           ?? submittedAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
           ?? "Submission pending";

    private static string BuildCloseCalendarDetail(OperationsCloseCalendarItemDto item)
        => item.ReadinessScore is null
            ? $"{item.Status} workflow; {item.BlockerCount.ToString("N0", CultureInfo.InvariantCulture)} blocker(s), {item.OpenChecklistCount.ToString("N0", CultureInfo.InvariantCulture)} checklist item(s) open."
            : $"{item.ReadinessSeverity ?? item.Status.ToString()} readiness score {item.ReadinessScore.Value.ToString("N0", CultureInfo.InvariantCulture)}; {item.BlockerCount.ToString("N0", CultureInfo.InvariantCulture)} blocker(s), {item.OpenChecklistCount.ToString("N0", CultureInfo.InvariantCulture)} checklist item(s) open.";

    private static string FormatCloseCalendarAction(OperationsCloseCalendarItemDto item)
        => item.ReadinessNextActions is { Count: > 0 }
            ? string.Join("; ", item.ReadinessNextActions.Select(static action => action.Label).Where(static label => !string.IsNullOrWhiteSpace(label)))
            : item.IsReadyToClose ? "Review close-calendar readiness." : "Resolve close-calendar blockers.";

    private static string FormatStatusLabel(string status)
        => string.IsNullOrWhiteSpace(status)
            ? "Unknown"
            : string.Concat(status.SelectMany(static (ch, index) =>
                index > 0 && char.IsUpper(ch) ? [' ', ch] : new[] { ch }));

    private static string FormatReconciliationLaneStatusLabel(OperationsReconciliationLaneStatusDto status)
        => status switch
        {
            OperationsReconciliationLaneStatusDto.Ready => "Ready",
            OperationsReconciliationLaneStatusDto.ReviewRequired => "Review required",
            OperationsReconciliationLaneStatusDto.Blocked => "Blocked",
            OperationsReconciliationLaneStatusDto.Missing => "Missing",
            _ => status.ToString()
        };

    private static string FormatEvidenceStatusLabel(EvidenceStatusDto status)
        => status switch
        {
            EvidenceStatusDto.Ready => "Ready",
            EvidenceStatusDto.ReviewRequired => "Review required",
            EvidenceStatusDto.Blocked => "Blocked",
            EvidenceStatusDto.Missing => "Missing",
            EvidenceStatusDto.Stale => "Stale",
            EvidenceStatusDto.Unknown => "Unknown",
            _ => status.ToString()
        };

    private static string FormatEvidenceCount(int count)
        => count == 1 ? "1 evidence link" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} evidence links";

    private static string FormatBreakCount(int count)
        => count == 1 ? "1 break" : $"{count.ToString("N0", CultureInfo.InvariantCulture)} breaks";

    private static string FormatEvidencePackageCategoryLabel(int completeCategoryCount, int requiredCategoryCount)
        => $"{completeCategoryCount.ToString("N0", CultureInfo.InvariantCulture)}/{requiredCategoryCount.ToString("N0", CultureInfo.InvariantCulture)} categories";

    private static string FormatRequiredActions(IReadOnlyList<string>? requiredActions)
        => requiredActions is { Count: > 0 }
            ? string.Join("; ", requiredActions.Where(static action => !string.IsNullOrWhiteSpace(action)))
            : "Review retained support.";

    private static string FormatNavSupportComponentLabel(IReadOnlyList<PrivateCapitalNavSupportComponentDto> components)
    {
        if (components.Count == 0)
        {
            return "NAV support";
        }

        var readyCount = components.Count(static component => component.IsReady);
        return $"{readyCount.ToString("N0", CultureInfo.InvariantCulture)}/{components.Count.ToString("N0", CultureInfo.InvariantCulture)} components";
    }
}
