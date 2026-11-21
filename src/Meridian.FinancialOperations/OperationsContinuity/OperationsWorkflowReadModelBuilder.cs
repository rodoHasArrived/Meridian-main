using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>
/// Projects captured workflow state and retained audit evidence without storage access or mutation.
/// </summary>
internal sealed class OperationsWorkflowReadModelBuilder(IOperationsStatusDerivationService statusDerivation)
{
    private static readonly string[] RequiredReconciliationCoverageLaneIds =
    [
        "cash-reconciliation",
        "position-reconciliation",
        "trade-reconciliation",
        "income-reconciliation",
        "mbs-factor-reconciliation",
        "bank-reconciliation",
        "gl-reconciliation"
    ];

    internal OperationsContinuityWorkflowDto Build(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsCloseReadinessDto closeReadiness)
    {
        var gates = workflow.Gates.Select(ToGateDto).ToArray();
        var blockers = gates.SelectMany(static gate => gate.Blockers).ToArray();
        var nextActions = gates.SelectMany(static gate => gate.NextActions).ToArray();
        var evidenceLinks = timeline.SelectMany(static entry => entry.References)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var accountingRecordSummary = BuildAccountingRecordSummary(workflow, timeline, evidenceLinks);
        var evidencePackages = BuildEvidencePackages(workflow, timeline, accountingRecordSummary, evidenceLinks);
        var dashboardSummary = BuildDashboardSummary(
            workflow,
            timeline,
            closeReadiness,
            accountingRecordSummary,
            evidencePackages,
            evidenceLinks);
        var reviewedAutomation = OperationsReviewedAutomationProjection.Build(workflow, evidenceLinks);

        return new OperationsContinuityWorkflowDto(
            workflow.WorkflowId,
            workflow.FundAccountId,
            workflow.PeriodId,
            workflow.SecurityMasterSnapshotId,
            workflow.BrokerSource,
            workflow.CreatedAtUtc,
            workflow.UpdatedAtUtc,
            workflow.Version,
            statusDerivation.Derive(workflow),
            workflow.BrokerIntakeState,
            workflow.SecurityMasterState,
            workflow.LedgerPostingState,
            workflow.ReconciliationState,
            workflow.ApprovalState,
            gates,
            timeline,
            workflow.BreakCases,
            workflow.LedgerPreview,
            workflow.Approvals,
            workflow.ReportPackReadiness,
            BuildChecklist(workflow, timeline),
            evidenceLinks,
            blockers,
            nextActions,
            closeReadiness,
            workflow.ClosePackage,
            accountingRecordSummary,
            workflow.ReconciliationLanes,
            dashboardSummary,
            evidencePackages,
            reviewedAutomation,
            workflow.LedgerBookId);
    }

    private static IReadOnlyList<OperationsEvidencePackageSummaryDto> BuildEvidencePackages(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsAccountingRecordSummaryDto accountingRecordSummary,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var accountingEvidence = accountingRecordSummary.EvidenceCategories
            .SelectMany(static category => category.EvidenceLinks)
            .Concat(accountingRecordSummary.EvidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reportEvidence = workflow.ReportPackReadiness.EvidenceLinks
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var closeEvidence = (workflow.ClosePackage?.EvidenceLinks ?? [])
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exceptionManagementPackage = BuildExceptionManagementEvidencePackage(workflow, timeline);
        var approvalHistoryPackage = BuildApprovalHistoryEvidencePackage(workflow, timeline, closeEvidence);
        var auditEvidence = accountingEvidence
            .Concat(reportEvidence)
            .Concat(closeEvidence)
            .Concat(evidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var reopenTimeline = timeline
            .Where(static entry => string.Equals(entry.EventType, "workflow-reopened", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var reopenEvidence = reopenTimeline
            .SelectMany(static entry => entry.References)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var periodLockEvidence = closeEvidence
            .Concat(reopenEvidence)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var auditPack = accountingRecordSummary.AuditPackReadiness;
        // Full audit readiness includes the artifacts created by close itself. Their absence
        // cannot waive a missing source, reconciliation, ledger, approval, or report prerequisite.
        var accountingInputsMissing = accountingRecordSummary.EvidenceCategories.Any(static category =>
            category.Key is not ("exports" or "restatement-lineage") && !category.IsComplete);
        var periodLocked = workflow.IsClosed && workflow.ClosePackage is not null;
        var reopenPostureComplete = reopenTimeline.Length == 0 || reopenEvidence.Length > 0;
        var periodLockCategoryCount = (periodLocked ? 1 : 0) + (reopenPostureComplete ? 1 : 0);

        return
        [
            new OperationsEvidencePackageSummaryDto(
                accountingRecordSummary.RecordId,
                "Accounting record evidence",
                accountingRecordSummary.IsAuditReady
                    ? EvidenceStatusDto.Ready
                    : accountingRecordSummary.CompleteCategoryCount == 0
                        ? EvidenceStatusDto.Missing
                        : EvidenceStatusDto.ReviewRequired,
                accountingRecordSummary.IsAuditReady,
                accountingRecordSummary.Summary,
                "/workstation/accounting/operations-continuity",
                accountingRecordSummary.CompleteCategoryCount,
                accountingRecordSummary.RequiredCategoryCount,
                accountingEvidence.Length,
                accountingEvidence,
                accountingRecordSummary.IsAuditReady
                    ? []
                    : ["Complete all accounting-record evidence categories before publishing the evidence package."],
                RequiredForClose: workflow.IsClosed || accountingInputsMissing),
            BuildReconciliationCoverageEvidencePackage(workflow, timeline),
            exceptionManagementPackage,
            new OperationsEvidencePackageSummaryDto(
                string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? $"report-pack:{workflow.FundAccountId:D}:{workflow.PeriodId}"
                    : workflow.ReportPackReadiness.ReportPackId,
                "Report pack evidence",
                workflow.ReportPackReadiness.IsReady &&
                !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? EvidenceStatusDto.Ready
                    : string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                        ? EvidenceStatusDto.Missing
                        : EvidenceStatusDto.ReviewRequired,
                workflow.ReportPackReadiness.IsReady &&
                !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId),
                string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? workflow.ReportPackReadiness.BlockingReason ?? "Report-pack evidence has not been linked."
                    : $"Report pack {workflow.ReportPackReadiness.ReportPackId} is linked for retained close evidence.",
                "/workstation/reporting/report-packs",
                workflow.ReportPackReadiness.IsReady ? 1 : 0,
                1,
                reportEvidence.Length,
                reportEvidence,
                workflow.ReportPackReadiness.IsReady &&
                !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? []
                    : ["Link ready report-pack evidence before close publication."]),
            new OperationsEvidencePackageSummaryDto(
                workflow.ClosePackage?.ClosePackageId ?? $"close-package:{workflow.FundAccountId:D}:{workflow.PeriodId}",
                "Close package manifest",
                workflow.ClosePackage is not null
                    ? EvidenceStatusDto.Ready
                    : workflow.ReportPackReadiness.IsReady
                        ? EvidenceStatusDto.ReviewRequired
                        : EvidenceStatusDto.Missing,
                workflow.ClosePackage is not null,
                workflow.ClosePackage is null
                    ? "Close package manifest and retained evidence hash have not been published."
                    : $"Close package {workflow.ClosePackage.ClosePackageId} retained manifest {workflow.ClosePackage.RetainedManifestId} and evidence hash.",
                workflow.ClosePackage?.RetainedManifestRoute ?? "/workstation/accounting/operations-continuity",
                workflow.ClosePackage is null ? 0 : 1,
                1,
                closeEvidence.Length,
                closeEvidence,
                workflow.ClosePackage is not null
                    ? []
                    : ["Publish the close package manifest and retain the evidence hash."],
                RequiredForClose: workflow.IsClosed),
            new OperationsEvidencePackageSummaryDto(
                $"audit-support:{workflow.FundAccountId:D}:{workflow.PeriodId}",
                "Audit support package",
                auditPack?.IsComplete == true
                    ? EvidenceStatusDto.Ready
                    : auditPack is null || accountingRecordSummary.CompleteCategoryCount == 0
                        ? EvidenceStatusDto.Missing
                        : EvidenceStatusDto.ReviewRequired,
                auditPack?.IsComplete == true,
                auditPack?.IsComplete == true
                    ? "Audit support package includes all required accounting-record evidence categories."
                    : auditPack is null
                        ? "Audit support package readiness has not been calculated."
                        : $"{auditPack.MissingEvidenceCategories.Count} audit evidence categor{(auditPack.MissingEvidenceCategories.Count == 1 ? "y is" : "ies are")} missing.",
                "/workstation/reporting/evidence",
                auditPack?.EvidenceCategorySummaries.Count(static category => category.IsComplete) ?? 0,
                auditPack?.EvidenceCategorySummaries.Count ?? accountingRecordSummary.RequiredCategoryCount,
                auditEvidence.Length,
                auditEvidence,
                auditPack?.IsComplete == true
                    ? []
                    : ["Complete missing audit evidence categories before releasing the package."],
                RequiredForClose: workflow.IsClosed || accountingInputsMissing),
            new OperationsEvidencePackageSummaryDto(
                $"period-lock-reopen:{workflow.FundAccountId:D}:{workflow.PeriodId}",
                "Period lock and reopen evidence",
                ResolvePeriodLockReopenStatus(periodLocked, reopenPostureComplete, workflow.ClosePackage, reopenEvidence),
                periodLocked && reopenPostureComplete,
                BuildPeriodLockReopenSummary(workflow, periodLocked, reopenTimeline.Length, reopenEvidence.Length),
                "/workstation/accounting/operations-continuity",
                periodLockCategoryCount,
                2,
                periodLockEvidence.Length,
                periodLockEvidence,
                BuildPeriodLockReopenRequiredActions(workflow, periodLocked, reopenPostureComplete),
                RequiredForClose: workflow.IsClosed),
            approvalHistoryPackage
        ];
    }

    private static OperationsEvidencePackageSummaryDto BuildReconciliationCoverageEvidencePackage(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        var reconciliationRunEvidence = timeline
            .Where(static entry => string.Equals(entry.EventType, "reconciliation-run", StringComparison.OrdinalIgnoreCase))
            .SelectMany(static entry => entry.References)
            .ToArray();
        var lanesById = workflow.ReconciliationLanes
            .Where(lane => RequiredReconciliationCoverageLaneIds.Any(id => string.Equals(id, lane.LaneId, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(static lane => lane.LaneId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static lane => lane.LaneId, static lane => lane, StringComparer.OrdinalIgnoreCase);
        var packageLanes = RequiredReconciliationCoverageLaneIds
            .Select(laneId => lanesById.TryGetValue(laneId, out var lane) ? lane : null)
            .Where(static lane => lane is not null)
            .Cast<OperationsReconciliationLaneSummaryDto>()
            .ToArray();
        var hasReconciliationRun = packageLanes.Length > 0 &&
            (reconciliationRunEvidence.Length > 0 ||
             workflow.ReconciliationState is not OperationsReconciliationStateDto.Pending);
        var completeCategoryCount = hasReconciliationRun
            ? packageLanes.Count(static lane => lane.IsReady)
            : 0;
        var requiredCategoryCount = RequiredReconciliationCoverageLaneIds.Length;
        var evidence = packageLanes
            .SelectMany(static lane => lane.EvidenceLinks)
            .Concat(reconciliationRunEvidence)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var blockedLaneCount = hasReconciliationRun
            ? packageLanes.Count(static lane => lane.Status == OperationsReconciliationLaneStatusDto.Blocked)
            : 0;
        var missingLaneCount = RequiredReconciliationCoverageLaneIds.Length - packageLanes.Length;
        var status = !hasReconciliationRun
            ? EvidenceStatusDto.Missing
            : blockedLaneCount > 0
                ? EvidenceStatusDto.Blocked
                : completeCategoryCount == requiredCategoryCount
                    ? EvidenceStatusDto.Ready
                    : EvidenceStatusDto.ReviewRequired;
        var actions = new List<string>();
        if (!hasReconciliationRun)
        {
            actions.Add("Run reconciliation and retain cash, position, trade, income, MBS factor, bank, and GL coverage before evidence release.");
        }

        if (missingLaneCount > 0)
        {
            actions.Add("Retain all Financial Operations reconciliation lane summaries before audit release.");
        }

        actions.AddRange(packageLanes
            .Where(static lane => !lane.IsReady)
            .SelectMany(static lane => lane.RequiredActions ?? [])
            .Where(static action => !string.IsNullOrWhiteSpace(action)));

        return new OperationsEvidencePackageSummaryDto(
            $"reconciliation-coverage:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            "Reconciliation coverage evidence",
            status,
            status == EvidenceStatusDto.Ready,
            BuildReconciliationCoverageSummary(hasReconciliationRun, completeCategoryCount, requiredCategoryCount, blockedLaneCount, evidence.Length),
            "/workstation/accounting/reconciliation",
            completeCategoryCount,
            requiredCategoryCount,
            evidence.Length,
            evidence,
            actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string BuildReconciliationCoverageSummary(
        bool hasReconciliationRun,
        int completeCategoryCount,
        int requiredCategoryCount,
        int blockedLaneCount,
        int evidenceLinkCount)
    {
        if (!hasReconciliationRun)
        {
            return "Reconciliation coverage has not been retained by a Financial Operations reconciliation run.";
        }

        if (completeCategoryCount == requiredCategoryCount)
        {
            return "Reconciliation coverage evidence confirms cash, position, trade, income, MBS factor, bank, and GL support lanes are ready.";
        }

        if (blockedLaneCount > 0)
        {
            return $"{blockedLaneCount} reconciliation lane(s) are blocked; {completeCategoryCount}/{requiredCategoryCount} coverage categories are ready across {evidenceLinkCount:N0} retained evidence link(s).";
        }

        return $"Reconciliation coverage evidence is incomplete; {completeCategoryCount}/{requiredCategoryCount} coverage categories are ready across {evidenceLinkCount:N0} retained evidence link(s).";
    }

    private static OperationsEvidencePackageSummaryDto BuildExceptionManagementEvidencePackage(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        var exceptionTimeline = timeline
            .Where(static entry =>
                string.Equals(entry.EventType, "reconciliation-run", StringComparison.OrdinalIgnoreCase) ||
                entry.EventType.StartsWith("reconciliation-break-", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var unresolvedBreaks = workflow.BreakCases
            .Where(static breakCase => !IsClosedBreakStatus(breakCase.Status))
            .ToArray();
        var exceptionEvidence = exceptionTimeline
            .SelectMany(static entry => entry.References)
            .Concat(workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasCaseInventory = exceptionTimeline.Any(static entry =>
            string.Equals(entry.EventType, "reconciliation-run", StringComparison.OrdinalIgnoreCase)) ||
            workflow.ReconciliationState == OperationsReconciliationStateDto.Complete;
        var allExceptionsResolved = hasCaseInventory && unresolvedBreaks.Length == 0;
        var retainedCaseEvidence = hasCaseInventory &&
            (workflow.BreakCases.Count == 0 || exceptionEvidence.Length > 0);
        var completeCategoryCount = (hasCaseInventory ? 1 : 0) +
            (allExceptionsResolved ? 1 : 0) +
            (retainedCaseEvidence ? 1 : 0);
        const int requiredCategoryCount = 3;
        var hasCriticalException = unresolvedBreaks.Any(static breakCase =>
            string.Equals(breakCase.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(breakCase.Severity, "High", StringComparison.OrdinalIgnoreCase));
        var status = completeCategoryCount == requiredCategoryCount
            ? EvidenceStatusDto.Ready
            : !hasCaseInventory
                ? EvidenceStatusDto.Missing
                : hasCriticalException
                    ? EvidenceStatusDto.Blocked
                    : EvidenceStatusDto.ReviewRequired;
        var actions = new List<string>();
        if (!hasCaseInventory)
        {
            actions.Add("Run reconciliation and retain exception case inventory before evidence release.");
        }

        if (unresolvedBreaks.Length > 0)
        {
            actions.AddRange(BuildResolveExceptionsRequiredActions(unresolvedBreaks));
        }

        if (!retainedCaseEvidence)
        {
            actions.Add("Retain exception assignment, escalation, or resolution evidence before audit release.");
        }

        return new OperationsEvidencePackageSummaryDto(
            $"exception-management:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            "Exception management evidence",
            status,
            status == EvidenceStatusDto.Ready,
            BuildExceptionManagementSummary(workflow, hasCaseInventory, unresolvedBreaks.Length, exceptionEvidence.Length),
            "/workstation/accounting/reconciliation",
            completeCategoryCount,
            requiredCategoryCount,
            exceptionEvidence.Length,
            exceptionEvidence,
            actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string BuildExceptionManagementSummary(
        OperationsContinuityWorkflow workflow,
        bool hasCaseInventory,
        int unresolvedBreakCount,
        int evidenceLinkCount)
    {
        if (!hasCaseInventory)
        {
            return "Exception case inventory has not been retained by a reconciliation run.";
        }

        if (unresolvedBreakCount == 0)
        {
            return workflow.BreakCases.Count == 0
                ? "Reconciliation run retained no open exception casework for this workflow."
                : $"Exception management evidence confirms {workflow.BreakCases.Count} break case(s) are closed with {evidenceLinkCount:N0} retained evidence link(s).";
        }

        return $"{unresolvedBreakCount} exception case(s) remain open; assignment, escalation, or resolution evidence is required before audit release.";
    }

    private static OperationsEvidencePackageSummaryDto BuildApprovalHistoryEvidencePackage(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        IReadOnlyList<OperationsEvidenceLinkDto> closeEvidence)
    {
        var approvalTimeline = timeline
            .Where(static entry => entry.EventType.StartsWith("approval-", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.EventType, "workflow-closed", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var approvalEvidence = approvalTimeline
            .SelectMany(static entry => entry.References)
            .Concat(workflow.Approvals.SelectMany(static approval => approval.EvidenceLinks))
            .Concat(closeEvidence)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasSubmission = workflow.Approvals.Any(static approval =>
            approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned);
        var hasApprovedDecision = workflow.ApprovalState == OperationsApprovalStateDto.Approved &&
            workflow.Approvals.Any(static approval => approval.Status == OperationsApprovalStateDto.Approved);
        var retainedChecklistApprovalCount = workflow.ClosePackage?.ChecklistControlApprovals.Count ?? 0;
        var hasChecklistApprovals = retainedChecklistApprovalCount > 0;
        var completeCategoryCount = (hasSubmission ? 1 : 0) +
            (hasApprovedDecision ? 1 : 0) +
            (hasChecklistApprovals ? 1 : 0);
        var requiredCategoryCount = workflow.ClosePackage is null ? 2 : 3;
        var status = completeCategoryCount == requiredCategoryCount
            ? EvidenceStatusDto.Ready
            : completeCategoryCount == 0
                ? EvidenceStatusDto.Missing
                : EvidenceStatusDto.ReviewRequired;
        var actions = new List<string>();
        if (!hasSubmission)
        {
            actions.Add("Submit workflow approval with reviewer, rationale, and report-pack evidence.");
        }

        if (!hasApprovedDecision)
        {
            actions.Add(workflow.ApprovalState == OperationsApprovalStateDto.Rejected
                ? "Resolve rejected approval and retain an approved reviewer decision before audit release."
                : "Record reviewer approval decision with retained rationale and report-pack evidence.");
        }

        if (workflow.ClosePackage is not null && !hasChecklistApprovals)
        {
            actions.Add("Publish close package with retained checklist-control approvals before audit release.");
        }

        return new OperationsEvidencePackageSummaryDto(
            $"approval-history:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            "Approval history evidence",
            status,
            status == EvidenceStatusDto.Ready,
            status == EvidenceStatusDto.Ready
                ? workflow.ClosePackage is null
                    ? "Approval submission and reviewer decision are retained; publication will preserve checklist-control approvals."
                    : $"Approval history includes submission, reviewer decision, and {retainedChecklistApprovalCount} retained checklist-control approval(s)."
                : $"Approval history has {completeCategoryCount} of {requiredCategoryCount} required evidence categories complete.",
            "/workstation/accounting/approvals",
            completeCategoryCount,
            requiredCategoryCount,
            approvalEvidence.Length,
            approvalEvidence,
            actions);
    }

    private static EvidenceStatusDto ResolvePeriodLockReopenStatus(
        bool periodLocked,
        bool reopenPostureComplete,
        OperationsClosePackagePublicationDto? closePackage,
        IReadOnlyList<OperationsEvidenceLinkDto> reopenEvidence)
    {
        if (periodLocked && reopenPostureComplete)
        {
            return EvidenceStatusDto.Ready;
        }

        return closePackage is not null || reopenEvidence.Count > 0
            ? EvidenceStatusDto.ReviewRequired
            : EvidenceStatusDto.Missing;
    }

    private static string BuildPeriodLockReopenSummary(
        OperationsContinuityWorkflow workflow,
        bool periodLocked,
        int reopenEventCount,
        int reopenEvidenceCount)
    {
        if (periodLocked)
        {
            return reopenEventCount == 0
                ? $"Period {workflow.PeriodId} is locked by close package {workflow.ClosePackage!.ClosePackageId}; no governed reopen incident is active."
                : $"Period {workflow.PeriodId} is locked by close package {workflow.ClosePackage!.ClosePackageId} with {reopenEvidenceCount:N0} retained reopen incident evidence link(s).";
        }

        if (workflow.ClosePackage is not null)
        {
            return reopenEventCount == 0
                ? $"Close package {workflow.ClosePackage.ClosePackageId} exists, but period {workflow.PeriodId} is not currently locked."
                : $"Workflow was reopened after close package {workflow.ClosePackage.ClosePackageId}; {reopenEvidenceCount:N0} incident evidence link(s) are retained and the period must be locked again after remediation.";
        }

        return $"Period {workflow.PeriodId} has not been locked by a close package; governed reopen evidence will be required if a closed workflow is reopened.";
    }

    private static IReadOnlyList<string> BuildPeriodLockReopenRequiredActions(
        OperationsContinuityWorkflow workflow,
        bool periodLocked,
        bool reopenPostureComplete)
    {
        if (periodLocked && reopenPostureComplete)
        {
            return [];
        }

        var actions = new List<string>();
        if (workflow.ClosePackage is null)
        {
            actions.Add("Close the workflow and retain the period-lock package before evidence release.");
        }
        else if (!workflow.IsClosed)
        {
            actions.Add("Complete reopened incident remediation and close the period again with retained evidence.");
        }

        if (!reopenPostureComplete)
        {
            actions.Add("Attach governed incident evidence before reopening a closed workflow.");
        }

        return actions;
    }

    private static OperationsDashboardSummaryDto BuildDashboardSummary(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsCloseReadinessDto? closeReadiness,
        OperationsAccountingRecordSummaryDto? accountingRecordSummary,
        IReadOnlyList<OperationsEvidencePackageSummaryDto> evidencePackages,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var unresolvedBreaks = workflow.BreakCases
            .Where(static breakCase => !IsClosedBreakStatus(breakCase.Status))
            .ToArray();
        var criticalBreaks = unresolvedBreaks.Count(static breakCase =>
            string.Equals(breakCase.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(breakCase.Severity, "High", StringComparison.OrdinalIgnoreCase));
        var readyLaneCount = workflow.ReconciliationLanes.Count(static lane => lane.IsReady);
        var reconciliationEvidence = workflow.ReconciliationLanes
            .SelectMany(static lane => lane.EvidenceLinks)
            .Concat(workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var approvalEvidence = workflow.Approvals
            .SelectMany(static approval => approval.EvidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var packageEvidence = evidencePackages
            .SelectMany(static package => package.EvidenceLinks)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var closePackage = workflow.ClosePackage;
        var periodLocked = workflow.IsClosed && closePackage is not null;

        var metrics = new[]
        {
            new OperationsDashboardMetricDto(
                "receive-activity",
                "Receive Activity",
                workflow.BrokerIntakeState.ToString(),
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete
                    ? EvidenceStatusDto.Ready
                    : EvidenceStatusDto.ReviewRequired,
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete
                    ? "Broker, custodian, or bank activity has been received and normalized for this account-period workflow."
                    : "Activity intake is still pending before matching and accounting controls can finish.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.BrokerIngest),
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete
                    ? []
                    : ["Receive and normalize account activity before matching records."]),
            new OperationsDashboardMetricDto(
                "match-records",
                "Match Records",
                $"{readyLaneCount}/{workflow.ReconciliationLanes.Count} lanes ready",
                ResolveLaneMetricStatus(workflow.ReconciliationLanes),
                workflow.ReconciliationLanes.Count == 0
                    ? "No reconciliation lane coverage has been projected for this workflow."
                    : "Cash, position, trade, income, MBS factor, bank, and GL reconciliation lanes are tracked from the shared workflow detail.",
                "/workstation/accounting/reconciliation",
                reconciliationEvidence,
                BuildMatchRecordsRequiredActions(workflow.ReconciliationLanes)),
            new OperationsDashboardMetricDto(
                "resolve-exceptions",
                "Resolve Exceptions",
                $"{unresolvedBreaks.Length} open",
                unresolvedBreaks.Length == 0
                    ? EvidenceStatusDto.Ready
                    : criticalBreaks > 0
                        ? EvidenceStatusDto.Blocked
                        : EvidenceStatusDto.ReviewRequired,
                unresolvedBreaks.Length == 0
                    ? "No unresolved reconciliation breaks remain for this workflow."
                    : $"{unresolvedBreaks.Length} reconciliation break(s) require assignment, escalation, or resolution evidence.",
                "/workstation/accounting/reconciliation",
                workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks)
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                unresolvedBreaks.Length == 0
                    ? []
                    : BuildResolveExceptionsRequiredActions(unresolvedBreaks)),
            new OperationsDashboardMetricDto(
                "approve-results",
                "Approve Results",
                workflow.ApprovalState.ToString(),
                workflow.ApprovalState == OperationsApprovalStateDto.Approved
                    ? EvidenceStatusDto.Ready
                    : EvidenceStatusDto.ReviewRequired,
                workflow.ApprovalState == OperationsApprovalStateDto.Approved
                    ? "Approval history is complete for this workflow."
                    : "Approval history is not complete for this workflow.",
                "/workstation/accounting/approvals",
                approvalEvidence,
                workflow.ApprovalState == OperationsApprovalStateDto.Approved
                    ? []
                    : BuildApproveResultsRequiredActions(workflow)),
            new OperationsDashboardMetricDto(
                "produce-evidence",
                "Produce Evidence",
                periodLocked
                    ? "Close package retained"
                    : closePackage is not null
                        ? "Reopened period lock pending"
                    : workflow.ReportPackReadiness.IsReady
                        ? "Report pack ready"
                        : "Evidence package pending",
                periodLocked
                    ? EvidenceStatusDto.Ready
                    : workflow.ReportPackReadiness.IsReady
                        ? EvidenceStatusDto.ReviewRequired
                        : EvidenceStatusDto.Missing,
                periodLocked
                    ? $"Close package {closePackage!.ClosePackageId} retained manifest {closePackage.RetainedManifestId}."
                    : closePackage is not null
                        ? $"Close package {closePackage.ClosePackageId} remains retained, but reopened incident remediation must be closed again before evidence release."
                    : workflow.ReportPackReadiness.BlockingReason ?? "Report-pack and close-package evidence still need publication.",
                "/workstation/reporting/report-packs",
                packageEvidence,
                periodLocked
                    ? []
                    : BuildProduceEvidenceRequiredActions(evidencePackages, periodLocked)),
            new OperationsDashboardMetricDto(
                "close-support",
                "Close Support",
                closeReadiness is null
                    ? "Close readiness pending"
                    : closeReadiness.IsReadyToClose
                        ? "Ready to close"
                        : $"{closeReadiness.Score}% ready",
                ResolveCloseSupportMetricStatus(closeReadiness, periodLocked),
                closeReadiness?.Blockers.FirstOrDefault()?.Message ??
                    "Close checklist, period lock, and reopen evidence are governed by the shared workflow.",
                "/workstation/accounting/operations-continuity",
                periodLocked
                    ? closePackage!.EvidenceLinks
                    : evidenceLinks,
                closeReadiness is { IsReadyToClose: true } || periodLocked
                    ? []
                    : BuildCloseSupportRequiredActions(closeReadiness, periodLocked))
        };
        var status = ResolveDashboardStatus(metrics);
        var readyCount = metrics.Count(static metric => metric.Status == EvidenceStatusDto.Ready);
        var stage = ResolveDashboardStage(workflow, unresolvedBreaks.Length);
        var dashboardEvidence = evidenceLinks
            .Concat(metrics.SelectMany(static metric => metric.EvidenceLinks))
            .Concat(accountingRecordSummary?.EvidenceLinks ?? [])
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OperationsDashboardSummaryDto(
            $"operations-dashboard:{workflow.FundAccountId:D}:{workflow.PeriodId}",
            stage,
            status,
            status == EvidenceStatusDto.Ready,
            readyCount,
            metrics.Length,
            status == EvidenceStatusDto.Ready
                ? $"Financial Operations dashboard is ready to produce evidence for period {workflow.PeriodId}."
                : $"Financial Operations dashboard is in {stage} with {metrics.Length - readyCount} metric(s) requiring review.",
            metrics,
            dashboardEvidence,
            metrics.SelectMany(static metric => metric.RequiredActions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static IReadOnlyList<string> BuildMatchRecordsRequiredActions(
        IReadOnlyList<OperationsReconciliationLaneSummaryDto> lanes)
    {
        if (lanes.Count > 0 && lanes.All(static lane => lane.IsReady))
        {
            return [];
        }

        var laneActions = lanes
            .Where(static lane => !lane.IsReady)
            .SelectMany(static lane => lane.RequiredActions ?? [])
            .ToArray();

        return BuildCappedDashboardRequiredActions(
            laneActions,
            "Complete source-backed reconciliation lanes before approval.",
            "reconciliation lane action");
    }

    private static IReadOnlyList<string> BuildResolveExceptionsRequiredActions(
        IReadOnlyList<OperationsBreakCaseDto> unresolvedBreaks)
    {
        if (unresolvedBreaks.Count == 0)
        {
            return [];
        }

        var actions = new List<string>();
        foreach (var breakCase in unresolvedBreaks)
        {
            AddDashboardRequiredAction(actions, breakCase.SuggestedAction);
        }

        var unassignedCount = unresolvedBreaks.Count(static breakCase => string.IsNullOrWhiteSpace(breakCase.Owner));
        if (unassignedCount > 0)
        {
            AddDashboardRequiredAction(
                actions,
                $"Assign {FormatOpenExceptionCount(unassignedCount)} to an accountable owner.");
        }

        var escalatedBreaks = unresolvedBreaks
            .Where(static breakCase => !string.IsNullOrWhiteSpace(breakCase.EscalationLevel))
            .ToArray();
        if (escalatedBreaks.Length > 0)
        {
            var firstEscalation = escalatedBreaks[0].EscalationLevel!.Trim();
            var escalationReason = string.IsNullOrWhiteSpace(escalatedBreaks[0].EscalationReason)
                ? string.Empty
                : $": {escalatedBreaks[0].EscalationReason!.Trim()}";
            AddDashboardRequiredAction(
                actions,
                $"Review {firstEscalation} escalation for {FormatOpenExceptionCount(escalatedBreaks.Length)}{escalationReason}.");
        }

        var blockedOutputs = unresolvedBreaks
            .SelectMany(static breakCase => breakCase.BlockedOutputs ?? [])
            .Where(static output => !string.IsNullOrWhiteSpace(output))
            .Select(static output => output.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (blockedOutputs.Length > 0)
        {
            var outputSummary = string.Join(", ", blockedOutputs.Take(3));
            if (blockedOutputs.Length > 3)
            {
                outputSummary = $"{outputSummary}, and {blockedOutputs.Length - 3} more";
            }

            AddDashboardRequiredAction(
                actions,
                $"Retain resolution evidence before releasing blocked output(s): {outputSummary}.");
        }

        return BuildCappedDashboardRequiredActions(
            actions,
            "Assign, escalate, or resolve open exceptions and retain resolution evidence.",
            "exception action");
    }

    private static IReadOnlyList<string> BuildApproveResultsRequiredActions(
        OperationsContinuityWorkflow workflow)
    {
        if (workflow.ApprovalState == OperationsApprovalStateDto.Approved)
        {
            return [];
        }

        var actions = new List<string>();
        switch (workflow.ApprovalState)
        {
            case OperationsApprovalStateDto.Pending:
                if (workflow.ReportPackReadiness.IsReady &&
                    !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId))
                {
                    AddDashboardRequiredAction(
                        actions,
                        $"Submit workflow approval for report pack {workflow.ReportPackReadiness.ReportPackId.Trim()} with reviewer and rationale.");
                }
                else
                {
                    AddDashboardRequiredAction(actions, workflow.ReportPackReadiness.BlockingReason);
                    AddDashboardRequiredAction(actions, "Link a ready report pack before approval submission.");
                }

                AddChecklistControlApprovalActions(actions, workflow, includeApprovalGate: false, transitionLabel: "approval submission");
                break;

            case OperationsApprovalStateDto.Submitted:
            case OperationsApprovalStateDto.ReviewerAssigned:
                var reviewer = GetAssignedApprovalReviewer(workflow) ?? "the assigned reviewer";
                AddDashboardRequiredAction(
                    actions,
                    $"Record approval decision from {reviewer} with retained rationale{ReadyReportPackSuffix(workflow)}");
                AddChecklistControlApprovalActions(actions, workflow, includeApprovalGate: true, transitionLabel: "approval decision");
                break;

            case OperationsApprovalStateDto.Rejected:
                var rejectionReviewer = GetLatestApprovalReviewer(workflow, OperationsApprovalStateDto.Rejected) ?? "the rejecting reviewer";
                AddDashboardRequiredAction(
                    actions,
                    $"Resolve rejected approval from {rejectionReviewer} and resubmit with retained remediation evidence.");
                if (!workflow.ReportPackReadiness.IsReady)
                {
                    AddDashboardRequiredAction(actions, workflow.ReportPackReadiness.BlockingReason);
                    AddDashboardRequiredAction(actions, "Link a ready report pack before resubmission.");
                }

                AddChecklistControlApprovalActions(actions, workflow, includeApprovalGate: false, transitionLabel: "approval resubmission");
                break;

            default:
                AddDashboardRequiredAction(actions, "Complete workflow approval and checklist-control approvals.");
                break;
        }

        return BuildCappedDashboardRequiredActions(
            actions,
            "Complete workflow approval and checklist-control approvals.",
            "approval action",
            "approval workspace");
    }

    private static void AddChecklistControlApprovalActions(
        List<string> actions,
        OperationsContinuityWorkflow workflow,
        bool includeApprovalGate,
        string transitionLabel)
    {
        var requiredGates = workflow.Gates
            .Where(gate => gate.Status == OperationsGateStatusDto.Passed ||
                (includeApprovalGate && gate.GateKey == OperationsGateKeyDto.Approval))
            .Select(static gate => gate.GateKey)
            .Distinct()
            .ToArray();

        foreach (var gate in requiredGates)
        {
            var requiredApprovalCount = gate == OperationsGateKeyDto.Approval ? 2 : 1;
            AddDashboardRequiredAction(
                actions,
                $"Retain {FormatChecklistApprovalCount(requiredApprovalCount)} for {DisplayName(gate)} close gate ({CloseChecklistTaskId(gate)}) before {transitionLabel}.");
        }
    }

    private static string FormatChecklistApprovalCount(int requiredApprovalCount) =>
        requiredApprovalCount == 1
            ? "1 checklist-control approval"
            : $"{requiredApprovalCount} checklist-control approvals";

    private static string? GetAssignedApprovalReviewer(OperationsContinuityWorkflow workflow) =>
        workflow.Approvals
            .LastOrDefault(approval =>
                (approval.Status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned) &&
                !string.IsNullOrWhiteSpace(approval.Reviewer))
            ?.Reviewer?.Trim();

    private static string? GetLatestApprovalReviewer(
        OperationsContinuityWorkflow workflow,
        OperationsApprovalStateDto approvalState) =>
        workflow.Approvals
            .LastOrDefault(approval => approval.Status == approvalState && !string.IsNullOrWhiteSpace(approval.Reviewer))
            ?.Reviewer?.Trim();

    private static string ReadyReportPackSuffix(OperationsContinuityWorkflow workflow) =>
        workflow.ReportPackReadiness.IsReady &&
        !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
            ? $" for report pack {workflow.ReportPackReadiness.ReportPackId.Trim()}."
            : ".";

    private static IReadOnlyList<string> BuildCloseSupportRequiredActions(
        OperationsCloseReadinessDto? closeReadiness,
        bool periodLocked)
    {
        if (periodLocked || closeReadiness is { IsReadyToClose: true })
        {
            return [];
        }

        var actions = closeReadiness?.Blockers
            .Select(static blocker => string.IsNullOrWhiteSpace(blocker.Category)
                ? blocker.Message
                : $"Resolve {blocker.Category}: {blocker.Message}")
            .ToArray() ?? [];

        return BuildCappedDashboardRequiredActions(
            actions,
            "Clear close readiness blockers and retain period-lock or reopen evidence.",
            "close-support action",
            "operations continuity workspace");
    }

    private static IReadOnlyList<string> BuildProduceEvidenceRequiredActions(
        IReadOnlyList<OperationsEvidencePackageSummaryDto> evidencePackages,
        bool periodLocked)
    {
        if (periodLocked ||
            evidencePackages.Count > 0 && evidencePackages.All(static package => package.IsReady))
        {
            return [];
        }

        var actions = evidencePackages
            .Where(static package => !package.IsReady)
            .SelectMany(static package => package.RequiredActions ?? [])
            .ToArray();

        return BuildCappedDashboardRequiredActions(
            actions,
            "Publish and retain the evidence package before period close.",
            "evidence-package action",
            "reporting evidence workspace");
    }

    private static IReadOnlyList<string> BuildCappedDashboardRequiredActions(
        IEnumerable<string?> requiredActions,
        string fallbackAction,
        string actionLabel,
        string overflowWorkspace = "reconciliation workspace")
    {
        var actions = requiredActions
            .Where(static action => !string.IsNullOrWhiteSpace(action))
            .Select(static action => action!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (actions.Length == 0)
        {
            return [fallbackAction];
        }

        const int maxDashboardRequiredActions = 6;
        if (actions.Length <= maxDashboardRequiredActions)
        {
            return actions;
        }

        return actions
            .Take(maxDashboardRequiredActions)
            .Append($"Review {actions.Length - maxDashboardRequiredActions} additional {actionLabel}(s) in the {overflowWorkspace}.")
            .ToArray();
    }

    private static void AddDashboardRequiredAction(List<string> actions, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        actions.Add(action.Trim());
    }

    private static string FormatOpenExceptionCount(int count) =>
        count == 1 ? "1 open exception" : $"{count} open exceptions";

    private static EvidenceStatusDto ResolveLaneMetricStatus(IReadOnlyList<OperationsReconciliationLaneSummaryDto> lanes)
    {
        if (lanes.Count == 0)
        {
            return EvidenceStatusDto.Missing;
        }

        if (lanes.Any(static lane => lane.Status == OperationsReconciliationLaneStatusDto.Blocked))
        {
            return EvidenceStatusDto.Blocked;
        }

        return lanes.All(static lane => lane.IsReady)
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto ResolveCloseSupportMetricStatus(
        OperationsCloseReadinessDto? closeReadiness,
        bool periodLocked)
    {
        if (periodLocked || closeReadiness is { IsReadyToClose: true })
        {
            return EvidenceStatusDto.Ready;
        }

        if (closeReadiness is null)
        {
            return EvidenceStatusDto.Missing;
        }

        return string.Equals(closeReadiness.Severity, "Critical", StringComparison.OrdinalIgnoreCase) ||
               closeReadiness.Blockers.Any(static blocker =>
                   string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase))
            ? EvidenceStatusDto.Blocked
            : EvidenceStatusDto.ReviewRequired;
    }

    private static EvidenceStatusDto ResolveDashboardStatus(IReadOnlyList<OperationsDashboardMetricDto> metrics)
    {
        if (metrics.Any(static metric => metric.Status == EvidenceStatusDto.Blocked))
        {
            return EvidenceStatusDto.Blocked;
        }

        if (metrics.Any(static metric => metric.Status == EvidenceStatusDto.Missing))
        {
            return EvidenceStatusDto.Missing;
        }

        return metrics.All(static metric => metric.Status == EvidenceStatusDto.Ready)
            ? EvidenceStatusDto.Ready
            : EvidenceStatusDto.ReviewRequired;
    }

    private static string ResolveDashboardStage(OperationsContinuityWorkflow workflow, int unresolvedBreakCount)
    {
        if (workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Pending or OperationsBrokerIntakeStateDto.Imported or OperationsBrokerIntakeStateDto.Normalized)
        {
            return "Receive Activity";
        }

        if (workflow.ReconciliationState is OperationsReconciliationStateDto.Pending or OperationsReconciliationStateDto.AutoMatched)
        {
            return "Match Records";
        }

        if (unresolvedBreakCount > 0 ||
            workflow.ReconciliationState is OperationsReconciliationStateDto.ExceptionsOpen or OperationsReconciliationStateDto.InReview)
        {
            return "Resolve Exceptions";
        }

        if (workflow.ApprovalState != OperationsApprovalStateDto.Approved)
        {
            return "Approve Results";
        }

        return "Produce Evidence";
    }

    internal static bool IsClosedBreakStatus(string? status)
        => string.Equals(status, "closed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "waived", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "superseded", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "dismissed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "matched", StringComparison.OrdinalIgnoreCase);

    private static OperationsAccountingRecordSummaryDto BuildAccountingRecordSummary(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks)
    {
        var categories = new[]
        {
            BuildAccountingRecordCategory(
                "source-records",
                "Retained source data",
                workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete,
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Pending
                    ? "Broker, custodian, or bank source data has not been imported."
                    : "Provider and account source data is retained for the close lane.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.BrokerIngest),
                ["provider statement", "custodian activity file", "bank or account source record"]),
            BuildAccountingRecordCategory(
                "normalized-activity",
                "Normalized transactions and positions",
                workflow.BrokerIntakeState == OperationsBrokerIntakeStateDto.Complete,
                workflow.BrokerIntakeState is OperationsBrokerIntakeStateDto.Normalized or OperationsBrokerIntakeStateDto.Complete
                    ? "Imported activity has been normalized for accounting review."
                    : "Imported activity still needs normalization.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.BrokerIngest),
                ["normalized transactions", "normalized positions", "balance or cash activity projection"]),
            BuildAccountingRecordCategory(
                "reconciliation-case-history",
                "Reconciliation case history",
                workflow.ReconciliationGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.ReconciliationState == OperationsReconciliationStateDto.Complete,
                workflow.BreakCases.Count == 0
                    ? "No unresolved reconciliation casework remains for this record."
                    : $"{workflow.BreakCases.Count} reconciliation case(s) are linked to this record.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.Reconciliation)
                    .Concat(workflow.BreakCases.SelectMany(static breakCase => breakCase.EvidenceLinks))
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["reconciliation run", "break-case decision history", "resolved exception evidence"]),
            BuildAccountingRecordCategory(
                "ledger-evidence",
                "Journal and ledger evidence",
                workflow.LedgerPostingGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.LedgerPostingState is OperationsLedgerPostingStateDto.Posted or OperationsLedgerPostingStateDto.Complete,
                workflow.LedgerPreview is null
                    ? "Ledger preview or posting evidence has not been linked."
                    : "Ledger preview and posting evidence are linked.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.LedgerPosting)
                    .Concat(workflow.LedgerPreview?.EvidenceLinks ?? [])
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["journal preview", "posted ledger batch", "trial-balance support"]),
            BuildAccountingRecordCategory(
                "approvals",
                "Approval history",
                workflow.ApprovalGate.Status == OperationsGateStatusDto.Passed &&
                    workflow.ApprovalState == OperationsApprovalStateDto.Approved,
                workflow.Approvals.Count == 0
                    ? "Approval history has not been submitted."
                    : $"{workflow.Approvals.Count} approval record(s) are linked.",
                "/workstation/accounting",
                EvidenceForGate(timeline, OperationsGateKeyDto.Approval)
                    .Concat(workflow.Approvals.SelectMany(static approval => approval.EvidenceLinks))
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["approval submission", "reviewer decision", "checklist control approvals"]),
            BuildAccountingRecordCategory(
                "report-pack",
                "Report pack",
                workflow.ReportPackReadiness.IsReady &&
                    !string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId),
                string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId)
                    ? "Report-pack readiness evidence has not been linked."
                    : $"Report pack {workflow.ReportPackReadiness.ReportPackId} is linked for the accounting record.",
                "/workstation/reporting/report-packs",
                workflow.ReportPackReadiness.EvidenceLinks,
                ["report-pack manifest", "report-pack provenance", "report-pack validation"]),
            BuildAccountingRecordCategory(
                "exports",
                "Exports and retained evidence",
                workflow.ClosePackage is not null &&
                    !string.IsNullOrWhiteSpace(workflow.ClosePackage.RetainedManifestId) &&
                    !string.IsNullOrWhiteSpace(workflow.ClosePackage.EvidenceHash),
                workflow.ClosePackage is null
                    ? "Export manifest and retained evidence hash still need close-package publication."
                    : $"Close package {workflow.ClosePackage.ClosePackageId} retains manifest {workflow.ClosePackage.RetainedManifestId} and evidence hash.",
                "/workstation/reporting/report-packs",
                workflow.ReportPackReadiness.EvidenceLinks
                    .Concat(workflow.ClosePackage?.EvidenceLinks ?? [])
                    .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ["export manifest", "retained evidence hash", "close-package publication"]),
            BuildAccountingRecordCategory(
                "restatement-lineage",
                "Restatement lineage",
                workflow.ClosePackage is not null,
                workflow.ClosePackage is null
                    ? "Restatement baseline is pending until the close package is published."
                    : "Closed package establishes the retained baseline for future restatements.",
                "/workstation/reporting/report-packs",
                workflow.ClosePackage?.EvidenceLinks ?? [],
                ["published baseline", "prior-version pointer when restated", "changed-line evidence"])
        };

        var completeCount = categories.Count(static category => category.IsComplete);
        var requiredCount = categories.Length;
        var recordId = $"accounting-record-{workflow.FundAccountId:N}-{workflow.PeriodId}";
        var summary = completeCount == requiredCount
            ? "Accounting record links retained source data, normalized activity, reconciliation case history, ledger evidence, approvals, report pack, exports, and restatement lineage."
            : $"Accounting record has {completeCount} of {requiredCount} required evidence categories complete.";
        var auditPackReadiness = BuildAuditPackReadiness(categories, workflow, completeCount, requiredCount);

        return new OperationsAccountingRecordSummaryDto(
            recordId,
            completeCount == requiredCount,
            completeCount,
            requiredCount,
            summary,
            categories,
            evidenceLinks,
            auditPackReadiness);
    }

    private static OperationsAccountingRecordEvidenceCategoryDto BuildAccountingRecordCategory(
        string key,
        string label,
        bool isComplete,
        string status,
        string routeHint,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> requiredEvidence) =>
        new(key, label, isComplete, status, routeHint, evidenceLinks, requiredEvidence);

    private static FundAuditPackReadinessDto BuildAuditPackReadiness(
        IReadOnlyList<OperationsAccountingRecordEvidenceCategoryDto> categories,
        OperationsContinuityWorkflow workflow,
        int completeCount,
        int requiredCount)
    {
        const int slaTargetSeconds = 60;
        var generatedInSeconds = workflow.ClosePackage is null
            ? 0d
            : Math.Max(0d, (workflow.ClosePackage.PublishedAtUtc - workflow.UpdatedAtUtc).Duration().TotalSeconds);
        var summaries = categories
            .Select(category => new FundAuditEvidenceCategorySummaryDto(
                MapAuditCategoryKey(category.Key),
                category.Label,
                category.IsComplete,
                category.Status,
                category.EvidenceLinks.Count,
                category.EvidenceLinks
                    .Select(static link => link.EvidenceId)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                category.RouteHint))
            .ToArray();
        var missing = summaries
            .Where(static category => !category.IsComplete)
            .Select(static category => category.Key)
            .ToArray();

        return new FundAuditPackReadinessDto(
            IsComplete: completeCount == requiredCount,
            GeneratedInSeconds: Math.Round(generatedInSeconds, 3, MidpointRounding.AwayFromZero),
            SlaTargetSeconds: slaTargetSeconds,
            SlaMet: generatedInSeconds <= slaTargetSeconds,
            MissingEvidenceCategories: missing,
            Warnings: categories
                .Where(static category => !category.IsComplete)
                .Select(static category => category.Status)
                .Where(static status => !string.IsNullOrWhiteSpace(status))
                .ToArray(),
            EvidenceCategorySummaries: summaries);
    }

    private static FundAuditEvidenceCategoryKeyDto MapAuditCategoryKey(string key)
        => key switch
        {
            "source-records" => FundAuditEvidenceCategoryKeyDto.SourceRecords,
            "normalized-activity" => FundAuditEvidenceCategoryKeyDto.NormalizedActivity,
            "reconciliation-case-history" => FundAuditEvidenceCategoryKeyDto.ReconciliationCases,
            "ledger-evidence" => FundAuditEvidenceCategoryKeyDto.LedgerEvidence,
            "approvals" => FundAuditEvidenceCategoryKeyDto.Approvals,
            "report-pack" => FundAuditEvidenceCategoryKeyDto.ReportPack,
            "exports" => FundAuditEvidenceCategoryKeyDto.Exports,
            "restatement-lineage" => FundAuditEvidenceCategoryKeyDto.RestatementLineage,
            _ => FundAuditEvidenceCategoryKeyDto.SourceRecords
        };

    private static string BuildReportPackLineageStatus(OperationsContinuityWorkflow workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow.ReportPackReadiness.ReportPackId))
        {
            return "Report-pack publication, export, retained document, and restatement lineage have not been linked.";
        }

        if (workflow.ClosePackage is null)
        {
            return $"Report pack {workflow.ReportPackReadiness.ReportPackId} is linked; close-package publication, export manifest, retained document evidence, and restatement lineage still need retained evidence.";
        }

        return $"Report pack {workflow.ClosePackage.ReportPackId} is linked with retained manifest {workflow.ClosePackage.RetainedManifestId}, publication/export evidence hash, retained document evidence, and close-package restatement lineage.";
    }

    private static IReadOnlyList<OperationsEvidenceLinkDto> EvidenceForGate(
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsGateKeyDto gate) =>
        timeline
            .Where(entry => entry.Gate == gate)
            .SelectMany(static entry => entry.References)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static IReadOnlyList<OperationsCloseChecklistTaskDto> BuildChecklist(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        var dueBase = DateOnly.FromDateTime(workflow.CreatedAtUtc.UtcDateTime).AddDays(2);
        var orderedTimeline = OrderChecklistTimeline(timeline);
        var acknowledgments = new Dictionary<OperationsGateKeyDto, OperationsTimelineEntryDto>();
        var gateEvidence = new Dictionary<OperationsGateKeyDto, OperationsEvidenceLinkDto>();
        foreach (var entry in orderedTimeline)
        {
            if (!IsSuccessfulChecklistEvent(entry))
            {
                continue;
            }

            if (entry.EventType == "checklist-task-acknowledged")
            {
                if (entry.Gate is { } acknowledgedGate &&
                    entry.ToGateStatus == OperationsGateStatusDto.Passed &&
                    !string.IsNullOrWhiteSpace(entry.Actor) &&
                    gateEvidence.TryGetValue(acknowledgedGate, out var reviewedEvidence) &&
                    entry.References.Any(link => link == reviewedEvidence))
                {
                    acknowledgments[acknowledgedGate] = entry;
                }

                continue;
            }

            if (entry.EventType is "workflow-reopened" or "approval-rejected" or "gate-posture-refreshed")
            {
                // A posture refresh has no per-gate mutation payload in the retained audit.
                acknowledgments.Clear();
            }
            else if (entry.Gate is { } changedGate)
            {
                if (entry.EventType is "security-master-resolved" or "security-master-override-approved")
                {
                    changedGate = OperationsGateKeyDto.BrokerIngest;
                }

                foreach (var dependentGate in acknowledgments.Keys.Where(key => (byte)key >= (byte)changedGate).ToArray())
                {
                    acknowledgments.Remove(dependentGate);
                }
            }

            if (entry.Gate is { } evidenceGate && entry.ToGateStatus == OperationsGateStatusDto.Passed)
            {
                gateEvidence[evidenceGate] = ChecklistCompletionEvidence(entry, evidenceGate);
                if ((entry.EventType is "security-master-resolved" or "security-master-override-approved") &&
                    workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed)
                {
                    // Override approval retains unresolved-identity blockers, so Passed also proves intake resolution.
                    gateEvidence[OperationsGateKeyDto.BrokerIngest] = ChecklistCompletionEvidence(entry, OperationsGateKeyDto.BrokerIngest);
                }
            }
        }

        return workflow.Gates.Select((gate, index) =>
        {
            gateEvidence.TryGetValue(gate.GateKey, out var evidence);
            var acknowledgment = gate.Status == OperationsGateStatusDto.Passed
                ? acknowledgments.GetValueOrDefault(gate.GateKey)
                : null;
            var status = gate.Status switch
            {
                OperationsGateStatusDto.Passed => "Done",
                OperationsGateStatusDto.Blocked => "Blocked",
                OperationsGateStatusDto.InProgress => "InProgress",
                _ => "Pending"
            };

            return new OperationsCloseChecklistTaskDto(
                CloseChecklistTaskId(gate.GateKey),
                gate.GateKey,
                $"{DisplayName(gate.GateKey)} close gate",
                gate.CompletedBy ?? "accounting-operator",
                RequiredEvidence: "Evidence link and gate completion audit",
                RequiredApprovalCount: gate.GateKey == OperationsGateKeyDto.Approval ? 2 : 1,
                ExpiresOn: dueBase.AddDays(index + 5),
                dueBase.AddDays(index),
                status,
                gate.Blockers.FirstOrDefault()?.Message,
                evidence?.EvidenceId,
                gate.NextActions.FirstOrDefault()?.Route,
                CanAcknowledge: !workflow.IsClosed && gate.Status == OperationsGateStatusDto.Passed &&
                    evidence is not null && acknowledgment is null,
                acknowledgment?.OccurredAtUtc,
                acknowledgment?.Actor);
        }).ToArray();
    }

    internal static OperationsEvidenceLinkDto ChecklistCompletionEvidence(OperationsTimelineEntryDto entry, OperationsGateKeyDto gate) =>
        (entry.Gate == gate ? entry.References.FirstOrDefault() : null) ?? new OperationsEvidenceLinkDto(
            $"operations-audit:{entry.AuditId:D}:{gate.ToString().ToLowerInvariant()}",
            $"{DisplayName(gate)} completion audit {entry.CurrentHash}",
            $"/api/workstation/operations/continuity/{entry.WorkflowId:D}/timeline",
            gate.ToString(),
            entry.OccurredAtUtc);

    internal static bool CompletesChecklistGate(OperationsTimelineEntryDto entry, OperationsGateKeyDto gate) =>
        entry.EventType != "checklist-task-acknowledged" &&
        entry.ToGateStatus == OperationsGateStatusDto.Passed && IsSuccessfulChecklistEvent(entry) &&
        (entry.Gate == gate || (gate == OperationsGateKeyDto.BrokerIngest &&
            entry.EventType is "security-master-resolved" or "security-master-override-approved"));

    private static bool IsSuccessfulChecklistEvent(OperationsTimelineEntryDto entry) =>
        entry.Outcome is { } outcome
            ? outcome.State is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
            : entry.EventType is not ("workflow-transition-blocked" or "workflow-transition-failed" or "ledger-posting-blocked");

    internal static IReadOnlyList<OperationsTimelineEntryDto> OrderChecklistTimeline(
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        // Store reads may sort equal timestamps differently. Retained hash links define append order.
        var successors = new Dictionary<string, OperationsTimelineEntryDto>(StringComparer.Ordinal);
        foreach (var entry in timeline)
        {
            if (string.IsNullOrWhiteSpace(entry.CurrentHash) ||
                !successors.TryAdd(entry.PreviousHash ?? string.Empty, entry))
            {
                return [];
            }
        }

        var ordered = new List<OperationsTimelineEntryDto>(timeline.Count);
        var previousHash = string.Empty;
        while (successors.Remove(previousHash, out var next))
        {
            ordered.Add(next);
            previousHash = next.CurrentHash;
        }

        return ordered.Count == timeline.Count ? ordered : [];
    }

    private static string CloseChecklistTaskId(OperationsGateKeyDto gate) =>
        $"close-gate-{gate}".ToLowerInvariant();

    internal static OperationsGateDto ToGateDto(OperationsGateState gate) =>
        new(
            gate.GateKey,
            DisplayName(gate.GateKey),
            gate.Status,
            IsRequired: true,
            Description(gate.GateKey),
            gate.Blockers,
            gate.NextActions,
            gate.CompletedAtUtc,
            gate.CompletedBy);

    private static string DisplayName(OperationsGateKeyDto gateKey) => gateKey switch
    {
        OperationsGateKeyDto.BrokerIngest => "Broker, custodian, and bank intake",
        OperationsGateKeyDto.SecurityMaster => "Security Master resolution",
        OperationsGateKeyDto.LedgerPosting => "Ledger draft and posting",
        OperationsGateKeyDto.Reconciliation => "Reconciliation",
        OperationsGateKeyDto.Approval => "Approval and close readiness",
        _ => gateKey.ToString()
    };

    private static string Description(OperationsGateKeyDto gateKey) => gateKey switch
    {
        OperationsGateKeyDto.BrokerIngest => "Imports and normalizes external account activity before accounting use.",
        OperationsGateKeyDto.SecurityMaster => "Requires authoritative instrument identity, provenance, and accounting classifications.",
        OperationsGateKeyDto.LedgerPosting => "Controls journal preview, validation, idempotency, and posting readiness.",
        OperationsGateKeyDto.Reconciliation => "Connects expected Security Master events, actual activity, and ledger postings.",
        OperationsGateKeyDto.Approval => "Requires operator, reviewer, rationale, and linked evidence before close.",
        _ => gateKey.ToString()
    };
}
