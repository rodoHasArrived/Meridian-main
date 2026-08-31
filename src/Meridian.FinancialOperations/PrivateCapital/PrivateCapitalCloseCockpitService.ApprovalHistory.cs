using System.Globalization;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitService
{
    private static IReadOnlyList<PrivateCapitalCloseCockpitApprovalDto> BuildApprovalHistory(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IReadOnlyList<PrivateCapitalFundEventLedgerRecordDto> records,
        IReadOnlyList<PrivateCapitalReportOutputDto> reportOutputs)
        => workflows
            .SelectMany(BuildWorkflowApprovalHistory)
            .Concat(workflows.SelectMany(BuildWorkflowReopenApprovalHistory))
            .Concat(BuildFundEventApprovalHistory(workflows, records))
            .Concat(BuildReportOutputApprovalHistory(workflows, reportOutputs))
            .OrderByDescending(static approval =>
                approval.DecidedAtUtc ??
                approval.SubmittedAtUtc ??
                DateTimeOffset.MinValue)
            .ThenBy(static approval => approval.ApprovalId, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(static approval => approval.ApprovalId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<PrivateCapitalCloseCockpitApprovalDto> BuildFundEventApprovalHistory(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IEnumerable<PrivateCapitalFundEventLedgerRecordDto> records)
    {
        foreach (var record in records.Where(static record =>
                     !string.IsNullOrWhiteSpace(record.ApprovalId) ||
                     record.ApprovalState != ManualJournalEntryStatusDto.Draft))
        {
            var workflow = ResolveApprovalWorkflow(workflows, record.EffectiveDate);
            if (workflow is null)
            {
                continue;
            }

            var status = MapApprovalState(record.ApprovalState);
            var evidence = RecordEvidence([record], "Fund-event approval evidence");
            var submittedAt = SourceApprovalSubmittedAt(status, record.FundEvent.UpdatedAtUtc);
            var decidedAt = SourceApprovalDecidedAt(status, record.FundEvent.UpdatedAtUtc);
            yield return new PrivateCapitalCloseCockpitApprovalDto(
                NormalizeOptional(record.ApprovalId) ?? $"fund-event-approval:{record.FundEventId}",
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                status,
                null,
                null,
                $"Fund-event approval retained for {record.FundEventType}.",
                submittedAt,
                decidedAt,
                NormalizeOptional(record.ApprovalRoute) ?? NormalizeOptional(record.ActivityRoute) ?? BuildWorkflowRoute(workflow.WorkflowId),
                evidence.Count,
                evidence);
        }
    }

    private static IEnumerable<PrivateCapitalCloseCockpitApprovalDto> BuildReportOutputApprovalHistory(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        IEnumerable<PrivateCapitalReportOutputDto> reportOutputs)
    {
        foreach (var output in reportOutputs.Where(static output =>
                     output.ApprovalState != ManualJournalEntryStatusDto.Draft ||
                     output.IsPublished ||
                     !string.IsNullOrWhiteSpace(output.ReportPackId)))
        {
            var workflow = ResolveApprovalWorkflow(workflows, output.EffectiveDate);
            if (workflow is null)
            {
                continue;
            }

            var status = MapApprovalState(output.ApprovalState);
            var evidence = ReportEvidence([output]);
            var submittedAt = SourceApprovalSubmittedAt(status, output.PublishedAtUtc);
            var decidedAt = SourceApprovalDecidedAt(status, output.PublishedAtUtc);
            yield return new PrivateCapitalCloseCockpitApprovalDto(
                $"report-output-approval:{output.ReportOutputId}",
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                status,
                null,
                NormalizeOptional(output.PublishedBy),
                output.IsPublished
                    ? $"Report output publication retained for {output.DisplayName}."
                    : $"Report output approval retained for {output.DisplayName}.",
                submittedAt,
                decidedAt,
                NormalizeOptional(output.ApprovalRoute) ??
                NormalizeOptional(output.ReportOutputRoute) ??
                NormalizeOptional(output.EvidenceRoute) ??
                NormalizeOptional(output.ReportRoute) ??
                BuildWorkflowRoute(workflow.WorkflowId),
                evidence.Count,
                evidence);
        }
    }

    private static OperationsContinuityWorkflowDto? ResolveApprovalWorkflow(
        IReadOnlyList<OperationsContinuityWorkflowDto> workflows,
        DateOnly effectiveDate)
    {
        var periodId = effectiveDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return workflows.FirstOrDefault(workflow =>
                   string.Equals(workflow.PeriodId, periodId, StringComparison.OrdinalIgnoreCase)) ??
               workflows.FirstOrDefault();
    }

    private static OperationsApprovalStateDto MapApprovalState(ManualJournalEntryStatusDto status)
        => status switch
        {
            ManualJournalEntryStatusDto.Approved => OperationsApprovalStateDto.Approved,
            ManualJournalEntryStatusDto.Rejected => OperationsApprovalStateDto.Rejected,
            ManualJournalEntryStatusDto.Submitted => OperationsApprovalStateDto.Submitted,
            _ => OperationsApprovalStateDto.Pending
        };

    private static DateTimeOffset? SourceApprovalSubmittedAt(
        OperationsApprovalStateDto status,
        DateTimeOffset? timestamp)
        => status is OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.Approved or OperationsApprovalStateDto.Rejected
            ? timestamp
            : null;

    private static DateTimeOffset? SourceApprovalDecidedAt(
        OperationsApprovalStateDto status,
        DateTimeOffset? timestamp)
        => status is OperationsApprovalStateDto.Approved or OperationsApprovalStateDto.Rejected
            ? timestamp
            : null;

    private static IEnumerable<PrivateCapitalCloseCockpitApprovalDto> BuildWorkflowApprovalHistory(
        OperationsContinuityWorkflowDto workflow)
    {
        var workflowRoute = BuildWorkflowRoute(workflow.WorkflowId);
        foreach (var approval in workflow.Approvals.Select((approval, index) => (Approval: approval, Index: index)))
        {
            var evidence = approval.Approval.EvidenceLinks ?? [];
            yield return new PrivateCapitalCloseCockpitApprovalDto(
                NormalizeOptional(approval.Approval.ApprovalId) ?? $"approval:{workflow.WorkflowId:D}:{approval.Index + 1}",
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                approval.Approval.Status,
                NormalizeOptional(approval.Approval.Operator),
                NormalizeOptional(approval.Approval.Reviewer),
                NormalizeOptional(approval.Approval.Rationale),
                approval.Approval.SubmittedAtUtc,
                approval.Approval.DecidedAtUtc,
                workflowRoute,
                evidence.Count,
                evidence);
        }

        var package = workflow.ClosePackage;
        if (package is null)
        {
            yield break;
        }

        var packageEvidence = package.EvidenceLinks ?? [];
        foreach (var approval in package.ChecklistControlApprovals)
        {
            var approvalId = $"checklist-control:{workflow.WorkflowId:D}:{NormalizeOptional(approval.TaskId) ?? "task"}:{approval.ApprovedAtUtc:yyyyMMddHHmmss}";
            yield return new PrivateCapitalCloseCockpitApprovalDto(
                approvalId,
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                OperationsApprovalStateDto.Approved,
                null,
                NormalizeOptional(approval.ApprovedBy),
                $"Checklist control approval retained for {approval.TaskId}.",
                approval.ApprovedAtUtc,
                approval.ApprovedAtUtc,
                workflowRoute,
                packageEvidence.Count,
                packageEvidence);
        }
    }

    private static IEnumerable<PrivateCapitalCloseCockpitApprovalDto> BuildWorkflowReopenApprovalHistory(
        OperationsContinuityWorkflowDto workflow)
    {
        var workflowRoute = BuildWorkflowRoute(workflow.WorkflowId);
        foreach (var entry in workflow.Timeline.Where(static entry =>
                     string.Equals(entry.EventType, "workflow-reopened", StringComparison.OrdinalIgnoreCase)))
        {
            var evidence = entry.References ?? [];
            yield return new PrivateCapitalCloseCockpitApprovalDto(
                $"workflow-reopened:{entry.AuditId:D}",
                workflow.WorkflowId,
                workflow.FundAccountId,
                workflow.PeriodId,
                OperationsApprovalStateDto.Approved,
                NormalizeOptional(entry.Actor),
                NormalizeOptional(entry.Actor),
                NormalizeOptional(entry.Rationale) ?? "Governed period reopen approval retained.",
                entry.OccurredAtUtc,
                entry.OccurredAtUtc,
                workflowRoute,
                evidence.Count,
                evidence);
        }
    }
}
