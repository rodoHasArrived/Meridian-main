using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflow
{
    public List<OperationsApprovalCommentDto> ApprovalComments { get; set; } = [];
    public List<OperationsApprovalCollaborationReceipt> ApprovalCollaborationReceipts { get; set; } = [];

    public OperationsWorkflowBlockerDto? ApprovalCollaborationBlocker(string approvalId)
    {
        if (IsClosed || ApprovalState is not (OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned)
            || Approvals.LastOrDefault()?.ApprovalId != approvalId)
            return new("APPROVAL_SESSION_CHANGED", "Refresh the current pending approval before adding discussion or changing its reviewer.", OperationsGateKeyDto.Approval, "Error", []);
        return null;
    }

    public OperationsWorkflowBlockerDto? ReassignApprovalReviewer(string approvalId, string reviewer, string rationale)
    {
        if (ApprovalCollaborationBlocker(approvalId) is { } blocker) return blocker;
        var submission = Approvals.Last();
        if (string.IsNullOrWhiteSpace(reviewer) || string.IsNullOrWhiteSpace(rationale))
            return new("APPROVAL_REASSIGN_METADATA_REQUIRED", "A reviewer and a reason are required.", OperationsGateKeyDto.Approval, "Error", []);
        if (string.Equals(submission.Operator?.Trim(), reviewer.Trim(), StringComparison.OrdinalIgnoreCase))
            return new("APPROVAL_INDEPENDENT_REVIEWER_REQUIRED", "The reviewer must differ from the original submitting operator.", OperationsGateKeyDto.Approval, "Error", []);
        Approvals.Add(submission with {
            ApprovalId = Guid.NewGuid().ToString("N"), Status = OperationsApprovalStateDto.ReviewerAssigned,
            Reviewer = reviewer.Trim(), Rationale = rationale.Trim(), DecidedAtUtc = null
        });
        ApprovalState = OperationsApprovalStateDto.ReviewerAssigned;
        // Submission identity/evidence and the in-progress gate are preserved. This never approves.
        return null;
    }
}
