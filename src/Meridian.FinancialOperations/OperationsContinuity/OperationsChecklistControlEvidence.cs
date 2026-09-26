using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

internal static class OperationsChecklistControlEvidence
{
    internal static IReadOnlyList<OperationsChecklistControlApprovalDto> Collect(
        IReadOnlyList<OperationsCloseChecklistTaskDto> checklist,
        IReadOnlyList<OperationsApprovalDto> approvals,
        OperationsApprovalStateDto approvalState)
    {
        var retained = checklist
            .Where(static task => task.Gate != OperationsGateKeyDto.Approval &&
                task.Status == "Done" && !string.IsNullOrWhiteSpace(task.EvidencePointer) &&
                task.AcknowledgedAtUtc is { } acknowledgedAt && acknowledgedAt != default &&
                !string.IsNullOrWhiteSpace(task.AcknowledgedBy))
            .Select(static task => new OperationsChecklistControlApprovalDto(
                task.TaskId, task.AcknowledgedBy!.Trim(), task.AcknowledgedAtUtc!.Value))
            .ToList();

        // Approval controls come from this cycle's submission and decision, never an old package.
        var currentApproval = approvals.LastOrDefault();
        if (currentApproval is null || currentApproval.Status != approvalState ||
            currentApproval.Status is not (OperationsApprovalStateDto.Submitted or
                OperationsApprovalStateDto.ReviewerAssigned or OperationsApprovalStateDto.Approved))
        {
            return retained;
        }

        var submission = currentApproval.SubmittedAtUtc.HasValue
            ? currentApproval
            : approvals.Count > 1 && approvals[^2].Status is
                OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned
                ? approvals[^2]
                : null;
        if (submission?.SubmittedAtUtc is { } submittedAt && submittedAt != default &&
            !string.IsNullOrWhiteSpace(submission.Operator))
        {
            retained.Add(new OperationsChecklistControlApprovalDto(
                "close-gate-approval", submission.Operator.Trim(), submittedAt));
        }

        if (currentApproval.Status == OperationsApprovalStateDto.Approved &&
            currentApproval.DecidedAtUtc is { } decidedAt && decidedAt != default &&
            !string.IsNullOrWhiteSpace(currentApproval.Reviewer))
        {
            retained.Add(new OperationsChecklistControlApprovalDto(
                "close-gate-approval", currentApproval.Reviewer.Trim(), decidedAt));
        }

        return retained;
    }
}
