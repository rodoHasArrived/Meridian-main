using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadService : IFinancialOperationsCommandCenterReadService
{
    private static readonly IReadOnlyList<OperationsGateKeyDto> CoreFlowGateOrder =
    [
        OperationsGateKeyDto.BrokerIngest,
        OperationsGateKeyDto.SecurityMaster,
        OperationsGateKeyDto.Reconciliation,
        OperationsGateKeyDto.Approval,
        OperationsGateKeyDto.LedgerPosting
    ];

    private readonly IOperationsContinuityWorkflowService _workflowService;
    private readonly IOperationsCloseCalendarService? _closeCalendarService;
    private readonly IPrivateCapitalCloseCockpitService? _privateCapitalCloseCockpitService;
    private readonly ILedgerBookService? _ledgerBookService;
    private readonly IAccountingCloseManagementService? _closeManagementService;
    private readonly ICloseReadinessSubjectSource? _closeSubjectSource;

    public FinancialOperationsCommandCenterReadService(
        IOperationsContinuityWorkflowService workflowService,
        IOperationsCloseCalendarService? closeCalendarService = null,
        IPrivateCapitalCloseCockpitService? privateCapitalCloseCockpitService = null,
        ILedgerBookService? ledgerBookService = null,
        IAccountingCloseManagementService? closeManagementService = null,
        ICloseReadinessSubjectSource? closeSubjectSource = null)
    {
        _workflowService = workflowService ?? throw new ArgumentNullException(nameof(workflowService));
        _closeCalendarService = closeCalendarService;
        _privateCapitalCloseCockpitService = privateCapitalCloseCockpitService;
        _ledgerBookService = ledgerBookService;
        _closeManagementService = closeManagementService;
        _closeSubjectSource = closeSubjectSource;
    }

    public async Task<FinancialOperationsCommandCenterDto> GetCommandCenterAsync(
        string? fundProfileId = null,
        Guid? ledgerBookId = null,
        Guid? fundAccountId = null,
        string? periodId = null,
        string? entityId = null,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null)
    {
        ct.ThrowIfCancellationRequested();

        var inputs = await LoadCloseInputsAsync(new(fundProfileId, ledgerBookId, fundAccountId, entityId, periodId),
            tenantId, companyId, ct).ConfigureAwait(false);
        var activeWorkflow = inputs.Workflow;
        var closeCalendar = inputs.Calendar;
        var privateCapitalCloseCockpit = inputs.Cockpit;

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
        var projection = inputs.Projection.Build(orderedRows);
        var isReady = projection.IsReadyToClose;
        var status = projection.Status;
        var summary = isReady ? "All required contributors are ready for the selected close scope."
            : $"{projection.Blockers.Count} requirement(s) block close. " + projection.Blockers.FirstOrDefault()?.Message;
        var decision = BuildCloseSupportDecision(activeWorkflow, closeCalendar, privateCapitalCloseCockpit, orderedRows)
            with
        { Status = status, IsReady = isReady, Summary = summary };
        decision = decision with
        {
            Decisions = projection.Blockers.Where(b => !orderedRows.Any(r => r.QueueId == b.Code))
                .Select(b => new FinancialOperationsCloseSupportDecisionRowDto(b.Code, b.Owner, b.Type,
                    "Blocked", true, b.Message, "Resolve this requirement and refresh close readiness.", null))
                .Concat(decision.Decisions).ToArray()
        };

        return new FinancialOperationsCommandCenterDto(
            DateTimeOffset.UtcNow,
            fundProfileId,
            ledgerBookId,
            fundAccountId,
            periodId,
            status,
            isReady,
            summary,
            orderedRows.Length,
            blockedCount,
            reviewCount,
            BuildMetrics(activeWorkflow, closeCalendar, privateCapitalCloseCockpit, orderedRows),
            orderedRows,
            activeWorkflow,
            closeCalendar,
            privateCapitalCloseCockpit,
            decision,
            projection);
    }

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
                ? BuildBreakCaseActionLabel(breakCase, blockedOutputs)
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
                breakCase.EvidenceLinks,
                QueueValue(breakCase.Severity, "Review"),
                BuildBreakCaseDueLabel(breakCase),
                BuildBreakCaseBlockerType(breakCase, blockedOutputs),
                BuildBreakCaseCloseReportImpact(breakCase, blockedOutputs)));
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
                workflow.WorkflowId,
                SeverityLabel: IsChecklistTaskBlocked(task) ? "Blocked" : "Review",
                SlaLabel: BuildChecklistDueLabel(task),
                BlockerType: task.Gate.ToString(),
                CloseReportImpact: "Close checklist evidence and approval readiness"));
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
                approval.EvidenceLinks,
                SeverityLabel: approval.Status is OperationsApprovalStateDto.Rejected ? "Rejected" : "Pending approval",
                SlaLabel: BuildApprovalDueLabel(approval.SubmittedAtUtc, approval.DecidedAtUtc),
                BlockerType: "Approval",
                CloseReportImpact: "Close/report sign-off"));
        }

        foreach (var blocker in (workflow.CloseReadiness?.Blockers ?? Array.Empty<OperationsCloseReadinessBlockerDto>())
            .DistinctBy(static item => item.Code, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"workflow-blocker:{blocker.Code}",
                "workflow-readiness-blocker",
                "Workflow control",
                string.IsNullOrWhiteSpace(blocker.Category) ? WorkflowStageLabel(blocker.Gate) : blocker.Category,
                FormatStatusLabel(blocker.Severity),
                blocker.Message,
                "Financial operations",
                WorkflowStageLabel(blocker.Gate),
                "Evidence retained on workflow",
                $"Resolve {WorkflowStageLabel(blocker.Gate).ToLowerInvariant()} blocker.",
                blocker.RouteHint,
                true,
                325,
                workflow.WorkflowId,
                SeverityLabel: FormatStatusLabel(blocker.Severity),
                SlaLabel: WorkflowStageLabel(blocker.Gate),
                BlockerType: "Workflow blocker",
                CloseReportImpact: WorkflowStageCloseReportImpact(blocker.Gate)));
        }

        var blockerCodes = (workflow.CloseReadiness?.Blockers ?? Array.Empty<OperationsCloseReadinessBlockerDto>())
            .Select(static item => item.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var action in (workflow.CloseReadiness?.NextActions ?? workflow.NextActions ?? Array.Empty<OperationsNextActionDto>())
            .Where(action => !string.IsNullOrWhiteSpace(action.Code) && !blockerCodes.Contains(action.Code))
            .DistinctBy(static item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Take(6))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"workflow-action:{action.Code}",
                "workflow-next-action",
                "Workflow action",
                WorkflowStageLabel(action.Gate),
                "Review",
                string.IsNullOrWhiteSpace(action.Label) ? "Workflow next action requires operator review." : action.Label,
                "Financial operations",
                WorkflowStageLabel(action.Gate),
                "Evidence retained on workflow",
                string.IsNullOrWhiteSpace(action.Label) ? "Review workflow next action." : action.Label,
                action.RouteHint,
                false,
                350,
                workflow.WorkflowId,
                SeverityLabel: "Review",
                SlaLabel: WorkflowStageLabel(action.Gate),
                BlockerType: "Workflow action",
                CloseReportImpact: WorkflowStageCloseReportImpact(action.Gate)));
        }

        foreach (var lane in (workflow.ReconciliationLanes ?? Array.Empty<OperationsReconciliationLaneSummaryDto>())
            .Where(static item => !item.IsReady || item.BreakCount > 0))
        {
            var capabilityLabel = BuildReconciliationCapabilityLabel(lane);
            var requiredActions = FormatRequiredActions(lane.RequiredActions);
            rows.Add(new FinancialOperationsQueueRowDto(
                $"reconciliation-lane:{lane.LaneId}",
                "reconciliation-lane",
                "Reconciliation",
                string.IsNullOrWhiteSpace(lane.Label) ? lane.LaneId : lane.Label,
                FormatReconciliationLaneStatusLabel(lane.Status),
                BuildReconciliationLaneDetail(lane, capabilityLabel),
                "Accounting operations",
                FormatBreakCount(lane.BreakCount),
                FormatEvidenceCount(lane.EvidenceLinks.Count),
                requiredActions,
                lane.RouteHint,
                lane.Status is OperationsReconciliationLaneStatusDto.Blocked or OperationsReconciliationLaneStatusDto.Missing,
                400,
                workflow.WorkflowId,
                lane.EvidenceLinks,
                SeverityLabel: FormatReconciliationLaneStatusLabel(lane.Status),
                SlaLabel: FormatBreakCount(lane.BreakCount),
                BlockerType: capabilityLabel,
                CloseReportImpact: BuildReconciliationCloseReportImpact(lane, requiredActions)));
        }

        if (workflow.DashboardSummary is not null)
        {
            AddDashboardRows(rows, workflow, workflow.DashboardSummary);
        }

        if (workflow.ReviewedAutomation is not null)
        {
            AddReviewedAutomationRows(rows, workflow, workflow.ReviewedAutomation);
        }

        if (workflow.AccountingRecordSummary is not null)
        {
            AddAccountingRecordRows(rows, workflow, workflow.AccountingRecordSummary);
        }

        foreach (var package in (workflow.EvidencePackages ?? Array.Empty<OperationsEvidencePackageSummaryDto>())
            .Where(static item => !item.IsReady))
        {
            var capabilityLabel = BuildEvidencePackageCapabilityLabel(package.PackageId, package.Label);
            var requiredActions = FormatRequiredActions(package.RequiredActions);
            rows.Add(new FinancialOperationsQueueRowDto(
                $"evidence-package:{package.PackageId}",
                BuildEvidencePackageSourceKind(package.PackageId),
                "Evidence package",
                string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                FormatEvidenceStatusLabel(package.Status),
                BuildEvidencePackageDetail(package.Summary, capabilityLabel),
                "Evidence operations",
                FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                FormatEvidenceCount(package.EvidenceLinkCount),
                requiredActions,
                package.RouteHint,
                IsEvidenceStatusBlocked(package.Status),
                500,
                workflow.WorkflowId,
                package.EvidenceLinks,
                SeverityLabel: FormatEvidenceStatusLabel(package.Status),
                SlaLabel: FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                BlockerType: capabilityLabel,
                CloseReportImpact: BuildEvidencePackageCloseReportImpact(package.PackageId, requiredActions)));
        }
    }

    private static void AddReviewedAutomationRows(
        ICollection<FinancialOperationsQueueRowDto> rows,
        OperationsContinuityWorkflowDto workflow,
        OperationsReviewedAutomationSummaryDto reviewedAutomation)
    {
        if (reviewedAutomation.Status is not EvidenceStatusDto.Ready || reviewedAutomation.RequiresHumanReview)
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"reviewed-automation:{reviewedAutomation.SummaryId}",
                "reviewed-automation",
                "Reviewed automation",
                string.IsNullOrWhiteSpace(reviewedAutomation.Stage) ? "Automation review" : reviewedAutomation.Stage,
                FormatEvidenceStatusLabel(reviewedAutomation.Status),
                reviewedAutomation.Summary,
                "Controller",
                reviewedAutomation.RequiresHumanReview ? "Human review required" : "Review retained",
                FormatEvidenceCount(reviewedAutomation.EvidenceLinks.Count),
                FormatRequiredActions(reviewedAutomation.RequiredActions),
                reviewedAutomation.EvidenceLinks.FirstOrDefault(static link => !string.IsNullOrWhiteSpace(link.Route))?.Route,
                IsEvidenceStatusBlocked(reviewedAutomation.Status),
                425,
                workflow.WorkflowId,
                reviewedAutomation.EvidenceLinks,
                SeverityLabel: reviewedAutomation.RequiresHumanReview ? "Human review" : FormatEvidenceStatusLabel(reviewedAutomation.Status),
                SlaLabel: reviewedAutomation.RequiresHumanReview ? "Human review required" : "Review retained",
                BlockerType: "Reviewed automation",
                CloseReportImpact: "Material Financial Operations actions remain human-gated."));
        }

        foreach (var artifact in reviewedAutomation.Artifacts
            .Where(static artifact => artifact.RequiresHumanReview || artifact.Status is not EvidenceStatusDto.Ready)
            .DistinctBy(static artifact => artifact.ArtifactId, StringComparer.OrdinalIgnoreCase)
            .Take(6))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"reviewed-automation-artifact:{SanitizeQueueToken(artifact.ArtifactId)}",
                "reviewed-automation-artifact",
                "Automation artifact",
                string.IsNullOrWhiteSpace(artifact.Title) ? artifact.ArtifactId : artifact.Title,
                FormatEvidenceStatusLabel(artifact.Status),
                $"{artifact.SourceSummary} Blocked material action: {artifact.BlockedMaterialAction}",
                "Controller",
                reviewedAutomation.Stage,
                FormatEvidenceCount(artifact.EvidenceLinks.Count),
                string.IsNullOrWhiteSpace(artifact.SuggestedOperatorAction)
                    ? "Review automation artifact before material action."
                    : artifact.SuggestedOperatorAction,
                artifact.EvidenceLinks.FirstOrDefault(static link => !string.IsNullOrWhiteSpace(link.Route))?.Route,
                IsEvidenceStatusBlocked(artifact.Status),
                430,
                workflow.WorkflowId,
                artifact.EvidenceLinks,
                SeverityLabel: artifact.RequiresHumanReview ? "Human review" : FormatEvidenceStatusLabel(artifact.Status),
                SlaLabel: reviewedAutomation.Stage,
                BlockerType: string.IsNullOrWhiteSpace(artifact.ArtifactKind) ? "Automation artifact" : artifact.ArtifactKind,
                CloseReportImpact: artifact.BlockedMaterialAction));
        }
    }

    private static void AddAccountingRecordRows(
        ICollection<FinancialOperationsQueueRowDto> rows,
        OperationsContinuityWorkflowDto workflow,
        OperationsAccountingRecordSummaryDto accountingRecord)
    {
        foreach (var category in accountingRecord.EvidenceCategories.Where(static item => !item.IsComplete))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"accounting-record:{accountingRecord.RecordId}:{category.Key}",
                "accounting-record-evidence",
                "Accounting record",
                string.IsNullOrWhiteSpace(category.Label) ? category.Key : category.Label,
                FormatStatusLabel(category.Status),
                string.IsNullOrWhiteSpace(category.Status)
                    ? "Accounting record evidence category requires review."
                    : category.Status,
                "Accounting operations",
                FormatEvidencePackageCategoryLabel(accountingRecord.CompleteCategoryCount, accountingRecord.RequiredCategoryCount),
                FormatEvidenceCount(category.EvidenceLinks.Count),
                BuildAccountingRecordCategoryAction(category),
                category.RouteHint,
                true,
                425,
                workflow.WorkflowId,
                category.EvidenceLinks,
                SeverityLabel: "Missing evidence",
                SlaLabel: FormatEvidencePackageCategoryLabel(accountingRecord.CompleteCategoryCount, accountingRecord.RequiredCategoryCount),
                BlockerType: "Accounting record evidence",
                CloseReportImpact: BuildAccountingRecordCloseReportImpact(category)));
        }

        var auditPack = accountingRecord.AuditPackReadiness;
        if (auditPack is { IsComplete: false })
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"accounting-record:{accountingRecord.RecordId}:audit-pack",
                "accounting-record-evidence",
                "Accounting record",
                "Audit evidence package",
                "Review Required",
                accountingRecord.Summary,
                "Accounting operations",
                auditPack.SlaMet
                    ? $"{auditPack.GeneratedInSeconds:N0}s/{auditPack.SlaTargetSeconds:N0}s SLA"
                    : $"SLA missed: {auditPack.GeneratedInSeconds:N0}s/{auditPack.SlaTargetSeconds:N0}s",
                FormatEvidenceCount(accountingRecord.EvidenceLinks.Count),
                BuildAuditPackAction(auditPack),
                accountingRecord.EvidenceCategories.FirstOrDefault(static category => !category.IsComplete)?.RouteHint,
                true,
                435,
                workflow.WorkflowId,
                accountingRecord.EvidenceLinks,
                SeverityLabel: auditPack.SlaMet ? "Review" : "Blocked",
                SlaLabel: auditPack.SlaMet
                    ? $"{auditPack.GeneratedInSeconds:N0}s/{auditPack.SlaTargetSeconds:N0}s SLA"
                    : $"SLA missed: {auditPack.GeneratedInSeconds:N0}s/{auditPack.SlaTargetSeconds:N0}s",
                BlockerType: "Audit evidence package",
                CloseReportImpact: "Audit evidence package must be complete before retained evidence release."));
        }
    }

    private static void AddDashboardRows(
        ICollection<FinancialOperationsQueueRowDto> rows,
        OperationsContinuityWorkflowDto workflow,
        OperationsDashboardSummaryDto dashboard)
    {
        foreach (var metric in dashboard.Metrics.Where(static item => item.Status is not EvidenceStatusDto.Ready))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"dashboard:{dashboard.DashboardId}:{metric.MetricId}",
                "dashboard-metric",
                "Dashboard",
                string.IsNullOrWhiteSpace(metric.Label) ? metric.MetricId : metric.Label,
                FormatEvidenceStatusLabel(metric.Status),
                string.IsNullOrWhiteSpace(metric.Detail) ? dashboard.Summary : metric.Detail,
                "Financial operations",
                $"{dashboard.ReadyMetricCount.ToString("N0", CultureInfo.InvariantCulture)}/{dashboard.TotalMetricCount.ToString("N0", CultureInfo.InvariantCulture)} metrics ready",
                FormatEvidenceCount(metric.EvidenceLinks.Count),
                FormatRequiredActions(metric.RequiredActions),
                metric.RouteHint,
                IsEvidenceStatusBlocked(metric.Status),
                450,
                workflow.WorkflowId,
                metric.EvidenceLinks,
                SeverityLabel: FormatEvidenceStatusLabel(metric.Status),
                SlaLabel: dashboard.Stage,
                BlockerType: "Operational dashboard",
                CloseReportImpact: FormatRequiredActions(metric.RequiredActions)));
        }

        foreach (var action in dashboard.RequiredActions
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6))
        {
            rows.Add(new FinancialOperationsQueueRowDto(
                $"dashboard-action:{dashboard.DashboardId}:{SanitizeQueueToken(action)}",
                "dashboard-action",
                "Dashboard action",
                dashboard.Stage,
                FormatEvidenceStatusLabel(dashboard.Status),
                dashboard.Summary,
                "Financial operations",
                $"{dashboard.ReadyMetricCount.ToString("N0", CultureInfo.InvariantCulture)}/{dashboard.TotalMetricCount.ToString("N0", CultureInfo.InvariantCulture)} metrics ready",
                FormatEvidenceCount(dashboard.EvidenceLinks.Count),
                action,
                dashboard.EvidenceLinks.FirstOrDefault()?.Route,
                IsEvidenceStatusBlocked(dashboard.Status),
                475,
                workflow.WorkflowId,
                dashboard.EvidenceLinks,
                SeverityLabel: FormatEvidenceStatusLabel(dashboard.Status),
                SlaLabel: dashboard.Stage,
                BlockerType: "Operational dashboard",
                CloseReportImpact: action));
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
                item.WorkflowId,
                SeverityLabel: item.IsReadyToClose ? "Review" : "Blocked",
                SlaLabel: item.NextDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Due date pending",
                BlockerType: item.NextDueTaskId ?? "period-lock",
                CloseReportImpact: "Period lock/reopen readiness"));
        }
    }

    private static void AddPrivateCapitalRows(ICollection<FinancialOperationsQueueRowDto> rows, PrivateCapitalCloseCockpitDto cockpit)
    {
        foreach (var lane in cockpit.Lanes.Where(static item => !item.IsReady))
        {
            var capabilityLabel = BuildPrivateCapitalCapabilityLabel(lane.LaneId, lane.Label);
            var requiredActions = FormatRequiredActions(lane.RequiredActions);
            rows.Add(new FinancialOperationsQueueRowDto(
                $"private-capital-lane:{lane.LaneId}",
                BuildPrivateCapitalSourceKind(lane.LaneId, "private-capital-close-lane"),
                "Private-capital close",
                string.IsNullOrWhiteSpace(lane.Label) ? lane.LaneId : lane.Label,
                FormatEvidenceStatusLabel(lane.Status),
                BuildPrivateCapitalDetail(lane.Summary, capabilityLabel, "Private-capital close lane requires review."),
                "Fund operations",
                capabilityLabel,
                FormatEvidenceCount(lane.EvidenceLinkCount),
                requiredActions,
                lane.Route,
                IsEvidenceStatusBlocked(lane.Status),
                700,
                EvidenceLinks: lane.EvidenceLinks,
                SeverityLabel: FormatEvidenceStatusLabel(lane.Status),
                SlaLabel: capabilityLabel,
                BlockerType: capabilityLabel,
                CloseReportImpact: BuildPrivateCapitalCloseReportImpact(lane.LaneId, requiredActions)));
        }

        foreach (var package in cockpit.EvidencePackages.Where(static item => !item.IsReady))
        {
            var capabilityLabel = BuildPrivateCapitalCapabilityLabel(package.PackageId, package.Label);
            var requiredActions = FormatRequiredActions(package.RequiredActions);
            rows.Add(new FinancialOperationsQueueRowDto(
                $"private-capital-package:{package.PackageId}",
                BuildPrivateCapitalSourceKind(package.PackageId, "private-capital-evidence-package"),
                "Private-capital package",
                string.IsNullOrWhiteSpace(package.Label) ? package.PackageId : package.Label,
                FormatEvidenceStatusLabel(package.Status),
                BuildPrivateCapitalDetail(package.Summary, capabilityLabel, "Private-capital evidence package requires review."),
                "Fund operations",
                FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                FormatEvidenceCount(package.EvidenceLinkCount),
                requiredActions,
                package.RouteHint,
                IsEvidenceStatusBlocked(package.Status),
                800,
                EvidenceLinks: package.EvidenceLinks,
                SeverityLabel: FormatEvidenceStatusLabel(package.Status),
                SlaLabel: FormatEvidencePackageCategoryLabel(package.CompleteCategoryCount, package.RequiredCategoryCount),
                BlockerType: capabilityLabel,
                CloseReportImpact: BuildPrivateCapitalCloseReportImpact(package.PackageId, requiredActions)));
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
                EvidenceLinks: package.EvidenceLinks,
                SeverityLabel: FormatEvidenceStatusLabel(package.Status),
                SlaLabel: FormatNavSupportComponentLabel(package.Components),
                BlockerType: "NAV support",
                CloseReportImpact: FormatRequiredActions(package.RequiredActions)));
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
                approval.Status is not OperationsApprovalStateDto.Approved,
                1000,
                approval.WorkflowId,
                approval.EvidenceLinks,
                SeverityLabel: approval.Status is OperationsApprovalStateDto.Rejected ? "Rejected" : "Pending approval",
                SlaLabel: BuildApprovalDueLabel(approval.SubmittedAtUtc, approval.DecidedAtUtc),
                BlockerType: "Private-capital approval",
                CloseReportImpact: "Private-capital close sign-off"));
        }
    }

    private static IReadOnlyList<FinancialOperationsCommandCenterMetricDto> BuildMetrics(
        OperationsContinuityWorkflowDto? workflow,
        OperationsCloseCalendarDto? closeCalendar,
        PrivateCapitalCloseCockpitDto? cockpit,
        IReadOnlyList<FinancialOperationsQueueRowDto> rows)
    {
        var reconciliationLanes = workflow?.ReconciliationLanes ?? Array.Empty<OperationsReconciliationLaneSummaryDto>();
        var closeChecklist = workflow?.CloseChecklist ?? Array.Empty<OperationsCloseChecklistTaskDto>();
        var coreFlowStages = BuildCoreFlowStages(workflow);
        var openBreakCases = workflow?.BreakCases.Where(static item => !IsBreakCaseClosed(item)).ToArray() ?? [];
        var openBreakCount = openBreakCases.Length;
        var completedCoreFlowCount = coreFlowStages.Count(static item => item.Status is OperationsGateStatusDto.Passed);
        var readyReconciliationLaneCount = reconciliationLanes.Count(static item => item.IsReady);
        var completedChecklistCount = closeChecklist.Count(static item => IsChecklistTaskComplete(item));
        var missingEvidenceCount = rows.Count(static row =>
            row.SourceKind.Contains("evidence", StringComparison.OrdinalIgnoreCase)
            || row.SourceKind.Contains("nav-support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SourceKind, "close-checklist", StringComparison.OrdinalIgnoreCase));
        var pendingApprovalCount = rows.Count(static row => row.SourceKind.Contains("approval", StringComparison.OrdinalIgnoreCase));
        var calendarBlockedCount = closeCalendar?.Items.Count(static item => !item.IsReadyToClose || item.BlockerCount > 0) ?? 0;
        var dashboardReviewCount = rows.Count(static row => row.SourceKind.Contains("dashboard", StringComparison.OrdinalIgnoreCase));
        var dashboardBlocked = rows.Any(static row =>
            row.SourceKind.Contains("dashboard", StringComparison.OrdinalIgnoreCase) && row.IsBlocked);
        var workflowControlCount = rows.Count(static row => IsWorkflowControlMetricRow(row));
        var workflowControlBlocked = rows.Any(static row => IsWorkflowControlMetricRow(row) && row.IsBlocked);

        return
        [
            new("period", "Period status", workflow?.CloseReadiness?.Severity ?? workflow?.Status.ToString() ?? "Detail pending", workflow is null ? "No active close workflow matched the requested scope." : $"Workflow {workflow.WorkflowId:D} for {workflow.PeriodId}.", workflow?.CloseReadiness?.IsReadyToClose == true ? "Ready" : "Review", workflow is null ? null : $"/api/workstation/operations/continuity/{workflow.WorkflowId:D}"),
            new("core-flow", "Core flow", FormatReadyCount(completedCoreFlowCount, coreFlowStages.Count), BuildCoreFlowMetricDetail(workflow, coreFlowStages), ResolveCoreFlowMetricStatus(workflow, coreFlowStages), workflow is null ? null : "/workstation/accounting/operations-continuity"),
            new("breaks", "Open breaks", openBreakCount.ToString(CultureInfo.InvariantCulture), openBreakCount == 0 ? "No open reconciliation break cases are surfaced." : FormatBreakCount(openBreakCount), openBreakCount == 0 ? "Ready" : "Review", null),
            new("exception-management", "Exception management", openBreakCount.ToString(CultureInfo.InvariantCulture), BuildExceptionManagementMetricDetail(workflow, openBreakCases), ResolveExceptionManagementMetricStatus(workflow, openBreakCases), workflow is null ? null : "/workstation/accounting/exceptions"),
            new("reconciliation", "Reconciliation lanes", FormatReadyCount(readyReconciliationLaneCount, reconciliationLanes.Count), BuildReconciliationMetricDetail(workflow, reconciliationLanes), ResolveReconciliationMetricStatus(workflow, reconciliationLanes), reconciliationLanes.Count == 0 ? null : "/workstation/accounting/reconciliation"),
            new("dashboard", "Dashboard review", dashboardReviewCount.ToString(CultureInfo.InvariantCulture), dashboardReviewCount == 0 ? "Operational dashboard metrics are ready or unavailable." : "Operational dashboard metrics or required actions need review.", dashboardReviewCount == 0 ? "Ready" : dashboardBlocked ? "Blocked" : "Review", workflow?.DashboardSummary is null ? null : "/workstation/accounting/operations-continuity"),
            new("workflow-control", "Workflow control", workflowControlCount.ToString(CultureInfo.InvariantCulture), workflowControlCount == 0 ? "Core workflow stages and reviewed automation have no surfaced action rows." : "Core workflow stage blockers, next actions, or reviewed automation need operator control.", workflowControlCount == 0 ? "Ready" : workflowControlBlocked ? "Blocked" : "Review", workflow is null ? null : "/workstation/accounting/operations-continuity"),
            new("close-checklist", "Close checklists", FormatReadyCount(completedChecklistCount, closeChecklist.Count), BuildCloseChecklistMetricDetail(workflow, closeChecklist), ResolveCloseChecklistMetricStatus(workflow, closeChecklist), closeChecklist.Count == 0 ? null : "/workstation/accounting/operations-continuity"),
            new("evidence", "Missing support", missingEvidenceCount.ToString(CultureInfo.InvariantCulture), missingEvidenceCount == 0 ? "Required evidence packages are not blocking completion." : "Evidence, checklist, or NAV support is incomplete.", missingEvidenceCount == 0 ? "Ready" : "Blocked", null),
            new("approvals", "Pending approvals", pendingApprovalCount.ToString(CultureInfo.InvariantCulture), pendingApprovalCount == 0 ? "No pending approval rows are surfaced." : "Approval rows must clear before completion.", pendingApprovalCount == 0 ? "Ready" : "Blocked", null),
            new("calendar", "Close calendar", calendarBlockedCount.ToString(CultureInfo.InvariantCulture), calendarBlockedCount == 0 ? "Close calendar items are ready or unavailable." : "Close-calendar due tasks remain blocked or under review.", calendarBlockedCount == 0 ? "Ready" : "Blocked", null),
            new("private-capital", "Private-capital close", cockpit?.OverallStatus.ToString() ?? "Unavailable", cockpit is null ? "Private-capital close cockpit is not registered." : cockpit.IsReadyToClose ? "Private-capital close cockpit is ready." : cockpit.ReadyLaneCount + "/" + cockpit.Lanes.Count + " close lanes ready.", cockpit?.IsReadyToClose == true ? "Ready" : "Review", cockpit?.CockpitRoute),
            new("queue", "Active queue", rows.Count.ToString(CultureInfo.InvariantCulture), BuildSummary(rows.Any(static row => row.IsBlocked) ? "Blocked" : "AtRisk", rows.Count, rows.Count(static row => row.IsBlocked), rows.Count(static row => !row.IsBlocked)), rows.Any(static row => row.IsBlocked) ? "Blocked" : "Review", null)
        ];
    }

    private static string FormatReadyCount(int readyCount, int totalCount)
        => $"{readyCount.ToString(CultureInfo.InvariantCulture)}/{totalCount.ToString(CultureInfo.InvariantCulture)}";

    private static string BuildExceptionManagementMetricDetail(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<OperationsBreakCaseDto> openBreakCases)
    {
        if (workflow is null)
        {
            return "No active close workflow matched the requested exception-management scope.";
        }

        if (openBreakCases.Count == 0)
        {
            return "No open exception cases require assignment, escalation, or retained resolution evidence.";
        }

        var assignmentEscalationCount = openBreakCases.Count(static item =>
            IsBreakCaseAssignmentRequired(item) || IsBreakCaseEscalationRequired(item));
        var blockedOutputCount = openBreakCases.Count(static item => item.BlockedOutputs is { Count: > 0 });
        if (assignmentEscalationCount > 0)
        {
            return $"{assignmentEscalationCount.ToString(CultureInfo.InvariantCulture)} exception case(s) require break assignment or escalation before approval.";
        }

        if (blockedOutputCount > 0)
        {
            return $"{blockedOutputCount.ToString(CultureInfo.InvariantCulture)} exception case(s) block NAV, close-report, or evidence outputs.";
        }

        return $"{openBreakCases.Count.ToString(CultureInfo.InvariantCulture)} exception case(s) remain open for resolution evidence.";
    }

    private static string ResolveExceptionManagementMetricStatus(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<OperationsBreakCaseDto> openBreakCases)
    {
        if (workflow is null)
        {
            return "Review";
        }

        if (openBreakCases.Any(static item => IsBreakCaseBlocked(item)))
        {
            return "Blocked";
        }

        return openBreakCases.Count == 0 ? "Ready" : "Review";
    }

    private static IReadOnlyList<CoreFlowStage> BuildCoreFlowStages(OperationsContinuityWorkflowDto? workflow)
    {
        if (workflow is null)
        {
            return [];
        }

        var gatesByKey = workflow.Gates
            .GroupBy(static gate => gate.GateKey)
            .ToDictionary(static group => group.Key, static group => group.First());

        return CoreFlowGateOrder
            .Select(gate =>
            {
                gatesByKey.TryGetValue(gate, out var state);
                return new CoreFlowStage(gate, WorkflowStageLabel(gate), state?.Status ?? ResolveCoreFlowFallbackStatus(workflow, gate));
            })
            .ToArray();
    }

    private static OperationsGateStatusDto ResolveCoreFlowFallbackStatus(
        OperationsContinuityWorkflowDto workflow,
        OperationsGateKeyDto gate)
        => gate switch
        {
            OperationsGateKeyDto.BrokerIngest => workflow.BrokerIntakeState switch
            {
                OperationsBrokerIntakeStateDto.Complete => OperationsGateStatusDto.Passed,
                OperationsBrokerIntakeStateDto.Pending => OperationsGateStatusDto.NotStarted,
                _ => OperationsGateStatusDto.InProgress
            },
            OperationsGateKeyDto.SecurityMaster => workflow.SecurityMasterState switch
            {
                OperationsSecurityMasterStateDto.Complete => OperationsGateStatusDto.Passed,
                OperationsSecurityMasterStateDto.Pending => OperationsGateStatusDto.NotStarted,
                OperationsSecurityMasterStateDto.OverridesRequested => OperationsGateStatusDto.ReviewRequired,
                _ => OperationsGateStatusDto.InProgress
            },
            OperationsGateKeyDto.Reconciliation => workflow.ReconciliationState switch
            {
                OperationsReconciliationStateDto.Complete => OperationsGateStatusDto.Passed,
                OperationsReconciliationStateDto.ExceptionsOpen => OperationsGateStatusDto.Blocked,
                OperationsReconciliationStateDto.InReview => OperationsGateStatusDto.ReviewRequired,
                OperationsReconciliationStateDto.Pending => OperationsGateStatusDto.NotStarted,
                _ => OperationsGateStatusDto.InProgress
            },
            OperationsGateKeyDto.Approval => workflow.ApprovalState switch
            {
                OperationsApprovalStateDto.Approved => OperationsGateStatusDto.Passed,
                OperationsApprovalStateDto.Rejected => OperationsGateStatusDto.Blocked,
                OperationsApprovalStateDto.Pending => OperationsGateStatusDto.NotStarted,
                _ => OperationsGateStatusDto.ReviewRequired
            },
            OperationsGateKeyDto.LedgerPosting => workflow.LedgerPostingState switch
            {
                OperationsLedgerPostingStateDto.Complete => OperationsGateStatusDto.Passed,
                OperationsLedgerPostingStateDto.Pending => OperationsGateStatusDto.NotStarted,
                _ => OperationsGateStatusDto.InProgress
            },
            _ => OperationsGateStatusDto.NotStarted
        };

    private static string BuildCoreFlowMetricDetail(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<CoreFlowStage> stages)
    {
        if (workflow is null)
        {
            return "No active close workflow matched the requested core flow scope.";
        }

        if (stages.Count == 0)
        {
            return "No core flow stages are surfaced for receive, match, resolve, approve, or evidence production.";
        }

        var blockedStages = stages
            .Where(static item => item.Status is OperationsGateStatusDto.Blocked)
            .Select(static item => item.Label)
            .ToArray();
        if (blockedStages.Length > 0)
        {
            return $"Core flow is blocked at {string.Join(", ", blockedStages)}.";
        }

        var openStages = stages
            .Where(static item => item.Status is not OperationsGateStatusDto.Passed)
            .Select(static item => item.Label)
            .ToArray();
        return openStages.Length > 0
            ? $"{openStages.Length.ToString(CultureInfo.InvariantCulture)} core flow stage(s) are not yet passed: {string.Join(", ", openStages)}."
            : "Core flow has passed Receive Activity -> Match Records -> Resolve Exceptions -> Approve Results -> Produce Evidence.";
    }

    private static string ResolveCoreFlowMetricStatus(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<CoreFlowStage> stages)
    {
        if (workflow is null || stages.Count == 0)
        {
            return "Review";
        }

        if (stages.Any(static item => item.Status is OperationsGateStatusDto.Blocked))
        {
            return "Blocked";
        }

        return stages.All(static item => item.Status is OperationsGateStatusDto.Passed)
            ? "Ready"
            : "Review";
    }

    private static string BuildReconciliationMetricDetail(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<OperationsReconciliationLaneSummaryDto> lanes)
    {
        if (workflow is null)
        {
            return "No active close workflow matched the requested reconciliation scope.";
        }

        if (lanes.Count == 0)
        {
            return "No reconciliation lane summaries are surfaced for cash, position, trade, income, MBS factor, bank, or GL reconciliation.";
        }

        var blockedCount = lanes.Count(static item => item.Status is OperationsReconciliationLaneStatusDto.Blocked or OperationsReconciliationLaneStatusDto.Missing);
        if (blockedCount > 0)
        {
            return $"{blockedCount.ToString(CultureInfo.InvariantCulture)} reconciliation lane(s) block close support across cash, position, trade, income, MBS factor, bank, or GL reconciliation.";
        }

        var reviewCount = lanes.Count(static item => item.Status is OperationsReconciliationLaneStatusDto.ReviewRequired);
        return reviewCount > 0
            ? $"{reviewCount.ToString(CultureInfo.InvariantCulture)} reconciliation lane(s) require review before approval."
            : "Cash, position, trade, income, MBS factor, bank, and GL reconciliation lanes are ready.";
    }

    private static string ResolveReconciliationMetricStatus(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<OperationsReconciliationLaneSummaryDto> lanes)
    {
        if (workflow is null || lanes.Count == 0)
        {
            return "Review";
        }

        if (lanes.Any(static item => item.Status is OperationsReconciliationLaneStatusDto.Blocked or OperationsReconciliationLaneStatusDto.Missing))
        {
            return "Blocked";
        }

        return lanes.Any(static item => item.Status is OperationsReconciliationLaneStatusDto.ReviewRequired)
            ? "Review"
            : "Ready";
    }

    private static string BuildCloseChecklistMetricDetail(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<OperationsCloseChecklistTaskDto> tasks)
    {
        if (workflow is null)
        {
            return "No active close workflow matched the requested checklist scope.";
        }

        if (tasks.Count == 0)
        {
            return "No close checklist tasks are surfaced for receive, match, resolve, approve, or evidence gates.";
        }

        var blockedCount = tasks.Count(static item => IsChecklistTaskBlocked(item));
        if (blockedCount > 0)
        {
            return $"{blockedCount.ToString(CultureInfo.InvariantCulture)} close checklist task(s) block period close or evidence release.";
        }

        var openCount = tasks.Count(static item => !IsChecklistTaskComplete(item));
        return openCount > 0
            ? $"{openCount.ToString(CultureInfo.InvariantCulture)} close checklist task(s) require evidence, approval, or acknowledgement."
            : "Close checklist tasks are complete across receive, match, resolve, approve, and evidence gates.";
    }

    private static string ResolveCloseChecklistMetricStatus(
        OperationsContinuityWorkflowDto? workflow,
        IReadOnlyList<OperationsCloseChecklistTaskDto> tasks)
    {
        if (workflow is null || tasks.Count == 0)
        {
            return "Review";
        }

        if (tasks.Any(static item => IsChecklistTaskBlocked(item)))
        {
            return "Blocked";
        }

        return tasks.Any(static item => !IsChecklistTaskComplete(item))
            ? "Review"
            : "Ready";
    }

    private static bool IsWorkflowControlMetricRow(FinancialOperationsQueueRowDto row)
    {
        return row.SourceKind.Contains("workflow", StringComparison.OrdinalIgnoreCase)
            || row.SourceKind.Contains("automation", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CoreFlowStage(
        OperationsGateKeyDto Gate,
        string Label,
        OperationsGateStatusDto? Status);

    private static FinancialOperationsCloseSupportDecisionDto BuildCloseSupportDecision(
        OperationsContinuityWorkflowDto? workflow,
        OperationsCloseCalendarDto? closeCalendar,
        PrivateCapitalCloseCockpitDto? cockpit,
        IReadOnlyList<FinancialOperationsQueueRowDto> rows)
    {
        var periodState = workflow is null
            ? "No active period workflow matched the requested scope."
            : $"{workflow.Status} period {workflow.PeriodId}; close readiness {workflow.CloseReadiness?.Severity ?? "Unavailable"}.";
        var lockPosture = BuildLockReopenPosture(workflow);
        var navReportPosture = BuildNavReportDependencyPosture(workflow, cockpit);
        var unresolvedExceptionCount = rows.Count(static row =>
            row.SourceKind.Contains("break", StringComparison.OrdinalIgnoreCase)
            || row.SourceKind.Contains("reconciliation", StringComparison.OrdinalIgnoreCase));
        var pendingApprovalCount = rows.Count(static row =>
            row.SourceKind.Contains("approval", StringComparison.OrdinalIgnoreCase));
        var retainedEvidenceGapCount = rows.Count(static row =>
            row.SourceKind.Contains("evidence", StringComparison.OrdinalIgnoreCase)
            || row.SourceKind.Contains("nav-support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.SourceKind, "close-checklist", StringComparison.OrdinalIgnoreCase));
        var decisionRows = BuildCloseSupportDecisionRows(workflow, closeCalendar, cockpit, rows).ToArray();
        var hasBlockingDecision = rows.Any(static row => row.IsBlocked)
            || decisionRows.Any(static row => row.IsBlocking);
        var blockingDecisionCount = rows.Count(static row => row.IsBlocked)
            + decisionRows.Count(decision =>
                decision.IsBlocking
                && !rows.Any(row => string.Equals(row.QueueId, decision.DecisionId, StringComparison.OrdinalIgnoreCase)));
        var isReady = workflow is not null
            && (workflow.CloseReadiness?.IsReadyToClose == true || workflow.Status is OperationsWorkflowStatusDto.ReadyForClose or OperationsWorkflowStatusDto.Closed)
            && !hasBlockingDecision
            && (cockpit?.IsReadyToClose ?? true);
        var status = isReady
            ? "Ready"
            : hasBlockingDecision
                ? "Blocked"
                : "Review";
        var summary = status switch
        {
            "Ready" => "Close support is ready: period posture, NAV/report dependencies, exceptions, approvals, and retained evidence gaps are clear.",
            "Blocked" => $"{blockingDecisionCount.ToString("N0", CultureInfo.InvariantCulture)} close-support decision(s) block completion.",
            _ => "Close support requires controller review before completion."
        };

        return new FinancialOperationsCloseSupportDecisionDto(
            DecisionId: BuildCloseSupportDecisionId(workflow, cockpit),
            Status: status,
            IsReady: isReady,
            Summary: summary,
            PeriodState: periodState,
            LockReopenPosture: lockPosture,
            NavReportDependencyPosture: navReportPosture,
            UnresolvedExceptionCount: unresolvedExceptionCount,
            PendingApprovalCount: pendingApprovalCount,
            RetainedEvidenceGapCount: retainedEvidenceGapCount,
            Decisions: decisionRows);
    }

    private static IEnumerable<FinancialOperationsCloseSupportDecisionRowDto> BuildCloseSupportDecisionRows(
        OperationsContinuityWorkflowDto? workflow,
        OperationsCloseCalendarDto? closeCalendar,
        PrivateCapitalCloseCockpitDto? cockpit,
        IReadOnlyList<FinancialOperationsQueueRowDto> rows)
    {
        if (workflow is null)
        {
            yield return new FinancialOperationsCloseSupportDecisionRowDto(
                "period-state",
                "Period state",
                "Active period workflow",
                "Missing",
                true,
                "No active operations continuity workflow matched the requested close scope.",
                "Start or select a scoped close workflow before close support can pass.",
                UiApiRoutes.OperationsContinuity);
        }

        foreach (var row in rows.Take(12))
        {
            yield return new FinancialOperationsCloseSupportDecisionRowDto(
                row.QueueId,
                MapDecisionCategory(row.SourceKind),
                row.Title,
                row.StatusLabel,
                row.IsBlocked,
                row.Detail,
                row.ActionLabel,
                row.RouteHint,
                row.EvidenceLinks);
        }

        if (closeCalendar?.Items.Any(static item => !item.IsReadyToClose || item.BlockerCount > 0) == true)
        {
            yield return new FinancialOperationsCloseSupportDecisionRowDto(
                "period-lock-calendar",
                "Lock/reopen posture",
                "Period lock calendar",
                "Blocked",
                true,
                "Close calendar has blocked or incomplete period-lock items.",
                "Clear close-calendar period-lock and reopen remediation items.",
                UiApiRoutes.OperationsContinuityCloseCalendar);
        }

        if (cockpit is not null && !cockpit.IsReadyToClose)
        {
            yield return new FinancialOperationsCloseSupportDecisionRowDto(
                "nav-report-dependencies",
                "NAV/report dependencies",
                "NAV and report dependency posture",
                cockpit.OverallStatus.ToString(),
                true,
                cockpit.BlockedLaneCount > 0
                    ? $"{cockpit.BlockedLaneCount.ToString("N0", CultureInfo.InvariantCulture)} private-capital close lane(s) block NAV/report support."
                    : "Private-capital close cockpit is not ready for close.",
                "Resolve NAV support, report output approval, delivery, and private-capital close lanes.",
                cockpit.CockpitRoute);
        }
    }

    private static string BuildCloseSupportDecisionId(OperationsContinuityWorkflowDto? workflow, PrivateCapitalCloseCockpitDto? cockpit)
        => workflow is not null
            ? $"finops-close-support:{workflow.WorkflowId:D}"
            : $"finops-close-support:{cockpit?.FundProfileId ?? "unscoped"}:{cockpit?.PeriodId ?? "current"}";

    private static string BuildLockReopenPosture(OperationsContinuityWorkflowDto? workflow)
    {
        if (workflow is null)
        {
            return "Period lock posture is unavailable until a workflow is selected.";
        }

        var periodLockPackage = workflow.EvidencePackages.FirstOrDefault(static package =>
            package.PackageId.Contains("period-lock", StringComparison.OrdinalIgnoreCase)
            || package.Label.Contains("period lock", StringComparison.OrdinalIgnoreCase)
            || package.Label.Contains("reopen", StringComparison.OrdinalIgnoreCase));
        if (workflow.Status is OperationsWorkflowStatusDto.Closed && periodLockPackage?.IsReady == true)
        {
            return "Period is closed with retained period-lock evidence.";
        }

        if (workflow.ClosePackage is not null && workflow.Status is not OperationsWorkflowStatusDto.Closed)
        {
            return "Prior close package is retained, but current period is reopened or not re-locked.";
        }

        return periodLockPackage is null
            ? "Period lock or governed reopen evidence has not been retained."
            : $"{periodLockPackage.Label}: {periodLockPackage.Summary}";
    }

    private static string BuildNavReportDependencyPosture(OperationsContinuityWorkflowDto? workflow, PrivateCapitalCloseCockpitDto? cockpit)
    {
        var reportPosture = workflow?.ReportPackReadiness.IsReady == true
            ? $"Report pack {workflow.ReportPackReadiness.ReportPackId ?? "selected"} is ready."
            : workflow?.ReportPackReadiness.BlockingReason ?? "Report-pack readiness is unavailable.";
        var navPosture = cockpit is null
            ? "NAV support cockpit is unavailable."
            : cockpit.IsReadyToClose
                ? "NAV support and private-capital close lanes are ready."
                : BuildNavDependencyCountLabel(cockpit);
        return $"{reportPosture} {navPosture}";
    }

    private static string BuildNavDependencyCountLabel(PrivateCapitalCloseCockpitDto cockpit)
    {
        var dependencyCount = cockpit.Lanes.Count > 0 ? cockpit.Lanes.Count : cockpit.NavSupportPackages.Count;
        var readyCount = cockpit.Lanes.Count > 0
            ? cockpit.ReadyLaneCount
            : cockpit.NavSupportPackages.Count(static package => package.IsReady);
        return $"{readyCount.ToString("N0", CultureInfo.InvariantCulture)}/{dependencyCount.ToString("N0", CultureInfo.InvariantCulture)} private-capital close lane(s) ready.";
    }

    private static string MapDecisionCategory(string sourceKind)
    {
        var normalized = sourceKind.Trim().ToLowerInvariant();
        if (normalized.Contains("calendar"))
        {
            return "Lock/reopen posture";
        }

        if (normalized.Contains("checklist"))
        {
            return "Close checklists";
        }

        if (normalized.Contains("dashboard"))
        {
            return "Operational dashboards";
        }

        if (normalized.Contains("workflow"))
        {
            return "Workflow control";
        }

        if (normalized.Contains("automation"))
        {
            return "Workflow control";
        }

        if (normalized.Contains("fund-event-accounting"))
        {
            return "Fund-event accounting support";
        }

        if (normalized.Contains("partner-capital"))
        {
            return "Partner capital account tie-outs";
        }

        if (normalized.Contains("expense-fee-allocation") || normalized.Contains("management-company"))
        {
            return "Expense, fee, and allocation review";
        }

        if (normalized.Contains("period-lock"))
        {
            return "Lock/reopen posture";
        }

        if (normalized.Contains("approval-history"))
        {
            return "Approval history";
        }

        if (normalized.Contains("exception-management"))
        {
            return "Unresolved exceptions";
        }

        if (normalized.Contains("approval"))
        {
            return "Approvals";
        }

        if (normalized.Contains("nav") || normalized.Contains("private-capital"))
        {
            return "NAV/report dependencies";
        }

        if (normalized.Contains("break") || normalized.Contains("reconciliation"))
        {
            return "Unresolved exceptions";
        }

        if (normalized.Contains("evidence"))
        {
            return "Retained evidence gaps";
        }

        return "Close support";
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
           || breakCase.BlockedOutputs is { Count: > 0 }
           || IsBreakCaseAssignmentRequired(breakCase) && IsBreakCaseSlaBreached(breakCase)
           || IsBreakCaseEscalationRequired(breakCase);

    private static bool IsBreakCaseAssignmentRequired(OperationsBreakCaseDto breakCase)
        => string.IsNullOrWhiteSpace(breakCase.Owner);

    private static bool IsBreakCaseEscalationRequired(OperationsBreakCaseDto breakCase)
        => string.IsNullOrWhiteSpace(breakCase.EscalationLevel)
           && (string.Equals(breakCase.Severity, "Critical", StringComparison.OrdinalIgnoreCase)
               || string.Equals(breakCase.Status, "Blocked", StringComparison.OrdinalIgnoreCase)
               || IsBreakCaseSlaBreached(breakCase));

    private static bool IsBreakCaseSlaBreached(OperationsBreakCaseDto breakCase)
        => breakCase.SlaState is not null &&
           (breakCase.SlaState.Contains("breach", StringComparison.OrdinalIgnoreCase)
            || breakCase.SlaState.Contains("overdue", StringComparison.OrdinalIgnoreCase)
            || breakCase.SlaState.Contains("missed", StringComparison.OrdinalIgnoreCase));

    private static string BuildBreakCaseActionLabel(OperationsBreakCaseDto breakCase, IReadOnlyList<string> blockedOutputs)
    {
        if (blockedOutputs.Count > 0)
        {
            return $"Unblock {string.Join(", ", blockedOutputs)}.";
        }

        var assignmentRequired = IsBreakCaseAssignmentRequired(breakCase);
        var escalationRequired = IsBreakCaseEscalationRequired(breakCase);
        return (assignmentRequired, escalationRequired) switch
        {
            (true, true) => "Assign owner and escalate break before close sign-off.",
            (true, false) => "Assign break owner before close sign-off.",
            (false, true) => "Escalate break and retain decision evidence before close sign-off.",
            _ => "Resolve or assign the reconciliation break."
        };
    }

    private static string BuildBreakCaseBlockerType(OperationsBreakCaseDto breakCase, IReadOnlyList<string> blockedOutputs)
    {
        if (blockedOutputs.Count > 0)
        {
            return string.Join(", ", blockedOutputs);
        }

        var assignmentRequired = IsBreakCaseAssignmentRequired(breakCase);
        var escalationRequired = IsBreakCaseEscalationRequired(breakCase);
        return (assignmentRequired, escalationRequired) switch
        {
            (true, true) => "Assignment/escalation",
            (true, false) => "Assignment",
            (false, true) => "Escalation",
            _ => QueueValue(breakCase.Category, "Reconciliation break")
        };
    }

    private static string BuildBreakCaseCloseReportImpact(OperationsBreakCaseDto breakCase, IReadOnlyList<string> blockedOutputs)
    {
        if (blockedOutputs.Count > 0)
        {
            return $"Blocks {string.Join(", ", blockedOutputs)}";
        }

        return IsBreakCaseAssignmentRequired(breakCase) || IsBreakCaseEscalationRequired(breakCase)
            ? "Break assignment/escalation required"
            : "Close/report exception review";
    }

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

    private static string BuildAccountingRecordCategoryAction(OperationsAccountingRecordEvidenceCategoryDto category)
    {
        var required = category.RequiredEvidence?
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToArray() ?? [];
        return required.Length == 0
            ? "Retain accounting record evidence before close sign-off."
            : $"Retain {string.Join(", ", required)} before close sign-off.";
    }

    private static string BuildAccountingRecordCloseReportImpact(OperationsAccountingRecordEvidenceCategoryDto category)
        => category.Key switch
        {
            "source-records" => "Source activity must be retained before matching, posting, and evidence release.",
            "normalized-activity" => "Normalized activity must be available before record matching and reconciliation.",
            "reconciliation-case-history" => "Exception case history must clear before approval and close support.",
            "ledger-evidence" => "Journal and ledger evidence must be retained before accounting close support.",
            "approvals" => "Approval history must be retained before close completion.",
            "report-pack" => "Report-pack evidence must be retained before evidence release.",
            "exports" => "Export manifest and retained evidence hash must be retained before audit release.",
            "restatement-lineage" => "Restatement baseline must be retained before period close evidence is complete.",
            _ => "Accounting record evidence must be retained before close completion."
        };

    private static string BuildAuditPackAction(FundAuditPackReadinessDto auditPack)
    {
        if (auditPack.MissingEvidenceCategories.Count > 0)
        {
            return $"Complete audit evidence categories: {string.Join(", ", auditPack.MissingEvidenceCategories)}.";
        }

        return auditPack.Warnings.Count > 0
            ? string.Join("; ", auditPack.Warnings)
            : "Complete audit evidence package before retained evidence release.";
    }

    private static string WorkflowStageLabel(OperationsGateKeyDto? gate)
        => gate switch
        {
            OperationsGateKeyDto.BrokerIngest => "Receive Activity",
            OperationsGateKeyDto.SecurityMaster => "Match Records",
            OperationsGateKeyDto.LedgerPosting => "Produce Evidence",
            OperationsGateKeyDto.Reconciliation => "Resolve Exceptions",
            OperationsGateKeyDto.Approval => "Approve Results",
            _ => "Workflow control"
        };

    private static string WorkflowStageCloseReportImpact(OperationsGateKeyDto? gate)
        => gate switch
        {
            OperationsGateKeyDto.BrokerIngest => "Activity intake must complete before matching and close evidence.",
            OperationsGateKeyDto.SecurityMaster => "Record matching and security coverage must clear before close approval.",
            OperationsGateKeyDto.LedgerPosting => "Accounting evidence and ledger posting must be retained before close support.",
            OperationsGateKeyDto.Reconciliation => "Exceptions must be resolved before approval and evidence release.",
            OperationsGateKeyDto.Approval => "Approval results must be retained before close completion.",
            _ => "Workflow control must clear before close completion."
        };

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

    private static string BuildEvidencePackageSourceKind(string packageId)
    {
        var normalized = packageId.Trim().ToLowerInvariant();
        if (normalized.Contains("approval-history"))
        {
            return "approval-history-evidence-package";
        }

        if (normalized.Contains("period-lock-reopen") || normalized.Contains("period-lock"))
        {
            return "period-lock-evidence-package";
        }

        if (normalized.Contains("exception-management"))
        {
            return "exception-management-evidence-package";
        }

        if (normalized.Contains("reconciliation-coverage"))
        {
            return "reconciliation-coverage-evidence-package";
        }

        if (normalized.Contains("audit-support"))
        {
            return "audit-evidence-package";
        }

        if (normalized.Contains("close-package"))
        {
            return "close-package-evidence";
        }

        return "evidence-package";
    }

    private static string BuildEvidencePackageCapabilityLabel(string packageId, string label)
    {
        var normalized = packageId.Trim().ToLowerInvariant();
        return normalized switch
        {
            var value when value.Contains("approval-history") => "Approval history",
            var value when value.Contains("period-lock-reopen") || value.Contains("period-lock") => "Period close locks and reopen evidence",
            var value when value.Contains("exception-management") => "Exception management",
            var value when value.Contains("reconciliation-coverage") => "Evidence packages",
            var value when value.Contains("audit-support") => "Audit evidence package",
            var value when value.Contains("close-package") => "Close evidence package",
            _ => "Evidence package"
        };
    }

    private static string BuildEvidencePackageDetail(string summary, string capabilityLabel)
    {
        var detail = string.IsNullOrWhiteSpace(summary) ? "Evidence package requires review." : summary.Trim();
        return capabilityLabel == "Evidence package"
            ? detail
            : $"{detail} Capability: {capabilityLabel}.";
    }

    private static string BuildEvidencePackageCloseReportImpact(string packageId, string requiredActions)
    {
        var normalized = packageId.Trim().ToLowerInvariant();
        var impact = normalized switch
        {
            var value when value.Contains("approval-history") => "Approval history, reviewer decision, checklist-control approval, and retained sign-off evidence",
            var value when value.Contains("period-lock-reopen") || value.Contains("period-lock") => "Period close lock, governed reopen posture, retained manifest, and audit evidence",
            var value when value.Contains("exception-management") => "Exception inventory, break assignment, escalation, resolution, and retained case evidence",
            var value when value.Contains("reconciliation-coverage") => "Cash, position, trade, income, MBS factor, bank, and GL reconciliation evidence coverage",
            var value when value.Contains("audit-support") => "Audit evidence package release",
            var value when value.Contains("close-package") => "Close package manifest and retained evidence hash",
            _ => string.Empty
        };
        return string.IsNullOrWhiteSpace(impact)
            ? requiredActions
            : $"{impact}: {requiredActions}";
    }

    private static string BuildPrivateCapitalSourceKind(string id, string fallback)
    {
        var normalized = id.Trim().ToLowerInvariant();
        if (normalized.Contains("fund-event-accounting")
            || normalized.Contains("journal-posting")
            || normalized.Contains("capital-accounts"))
        {
            return "private-capital-fund-event-accounting";
        }

        if (normalized.Contains("partner-capital"))
        {
            return "private-capital-partner-capital-tie-out";
        }

        if (normalized.Contains("expense-fee-allocation") || normalized.Contains("management-company"))
        {
            return "private-capital-expense-fee-allocation";
        }

        if (normalized.Contains("nav-support") || normalized.Contains("valuation-evidence"))
        {
            return "private-capital-nav-support";
        }

        if (normalized.Contains("period-lock") || normalized.Contains("close-package") || normalized.Contains("close-controls"))
        {
            return "private-capital-period-lock";
        }

        if (normalized.Contains("close-approval-audit"))
        {
            return "private-capital-approval-history-evidence";
        }

        return fallback;
    }

    private static string BuildPrivateCapitalCapabilityLabel(string id, string label)
    {
        var normalized = id.Trim().ToLowerInvariant();
        return normalized switch
        {
            var value when value.Contains("fund-event-accounting")
                || value.Contains("journal-posting")
                || value.Contains("capital-accounts") => "Fund-event accounting support",
            var value when value.Contains("partner-capital") => "Partner capital account tie-outs",
            var value when value.Contains("expense-fee-allocation")
                || value.Contains("management-company") => "Expense, fee, and allocation review",
            var value when value.Contains("nav-support")
                || value.Contains("valuation-evidence") => "Shadow NAV and NAV-support packages",
            var value when value.Contains("period-lock")
                || value.Contains("close-package")
                || value.Contains("close-controls") => "Period close locks and reopen evidence",
            var value when value.Contains("close-approval-audit") => "Approval history",
            _ => string.IsNullOrWhiteSpace(label) ? "Private-capital close lane" : label.Trim()
        };
    }

    private static string BuildPrivateCapitalDetail(string summary, string capabilityLabel, string fallback)
    {
        var detail = string.IsNullOrWhiteSpace(summary) ? fallback : summary.Trim();
        return $"{detail} Capability: {capabilityLabel}.";
    }

    private static string BuildPrivateCapitalCloseReportImpact(string id, string requiredActions)
    {
        var normalized = id.Trim().ToLowerInvariant();
        var impact = normalized switch
        {
            var value when value.Contains("fund-event-accounting")
                || value.Contains("journal-posting")
                || value.Contains("capital-accounts") => "Fund-event accounting, journal posting, capital-account roll-forward, and retained source support",
            var value when value.Contains("partner-capital") => "Partner capital account statement tie-out, subledger roll-forward, ledger impact, and delivery evidence",
            var value when value.Contains("expense-fee-allocation")
                || value.Contains("management-company") => "Expense review, management-fee allocation, allocation policy, and management-company operating evidence",
            var value when value.Contains("nav-support")
                || value.Contains("valuation-evidence") => "Shadow NAV, administrator NAV, valuation evidence, and NAV-support package release",
            var value when value.Contains("period-lock")
                || value.Contains("close-package")
                || value.Contains("close-controls") => "Period close lock, governed reopen evidence, close controls, and close-package publication",
            var value when value.Contains("close-approval-audit") => "Approval history, close sign-off, and audit evidence package",
            _ => "Private-capital close support"
        };
        return $"{impact}: {requiredActions}";
    }

    private static string BuildReconciliationLaneDetail(
        OperationsReconciliationLaneSummaryDto lane,
        string capabilityLabel)
    {
        var summary = string.IsNullOrWhiteSpace(lane.Summary)
            ? "Reconciliation lane requires review."
            : lane.Summary.Trim();
        return $"{summary} Capability: {capabilityLabel}.";
    }

    private static string BuildReconciliationCapabilityLabel(OperationsReconciliationLaneSummaryDto lane)
    {
        if (!string.IsNullOrWhiteSpace(lane.Label))
        {
            return lane.Label.Trim();
        }

        return lane.LaneId switch
        {
            "cash-reconciliation" => "Cash reconciliation",
            "position-reconciliation" => "Position reconciliation",
            "trade-reconciliation" => "Trade reconciliation",
            "income-reconciliation" => "Income reconciliation",
            "mbs-factor-reconciliation" => "MBS factor reconciliation",
            "bank-reconciliation" => "Bank reconciliation",
            "gl-reconciliation" => "GL reconciliation support",
            _ => "Reconciliation lane"
        };
    }

    private static string BuildReconciliationCloseReportImpact(
        OperationsReconciliationLaneSummaryDto lane,
        string requiredActions)
    {
        var impact = lane.LaneId switch
        {
            "cash-reconciliation" => "Cash close, NAV support, bank tie-out, and report evidence",
            "position-reconciliation" => "Position close, NAV support, custody holdings, and valuation evidence",
            "trade-reconciliation" => "Trade capture, allocation, ledger posting, and settlement evidence",
            "income-reconciliation" => "Income accrual, coupon/dividend, ledger posting, and close support",
            "mbs-factor-reconciliation" => "MBS factor, paydown, principal schedule, and valuation evidence",
            "bank-reconciliation" => "Bank statement, payment, cash movement, and treasury support",
            "gl-reconciliation" => "GL tie-out, external GL reconciliation, ledger evidence, and close approval",
            _ => "Close/report reconciliation support"
        };
        return $"{impact}: {requiredActions}";
    }

    private static string QueueValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string SanitizeQueueToken(string value)
    {
        var token = string.Concat(value
            .Trim()
            .Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'))
            .Trim('-');
        return token.Length == 0 ? "action" : token;
    }
}
