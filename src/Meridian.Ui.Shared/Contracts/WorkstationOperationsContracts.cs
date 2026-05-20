namespace Meridian.Ui.Shared.Contracts;

/// <summary>
/// Represents the derived lifecycle status for the end-to-end operations continuity workflow.
/// This value is server-derived and should not be authored directly by clients.
/// </summary>
public enum OperationsWorkflowStatus
{
    NotStarted,
    InProgress,
    Blocked,
    AwaitingApproval,
    Completed
}

/// <summary>
/// Represents the current gate posture for a workflow checkpoint.
/// Gate status is a server-side projection based on prerequisite evidence.
/// </summary>
public enum OperationsGateStatus
{
    NotReady,
    Ready,
    Blocked,
    Skipped,
    Completed
}

/// <summary>
/// Captures broker intake readiness for the workflow.
/// </summary>
public enum BrokerIntakeState
{
    NotStarted,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// Captures security master validation posture.
/// </summary>
public enum SecurityMasterValidationState
{
    NotStarted,
    InProgress,
    Passed,
    Failed
}

/// <summary>
/// Captures ledger posting execution state.
/// </summary>
public enum LedgerPostingState
{
    NotStarted,
    InProgress,
    Posted,
    Failed
}

/// <summary>
/// Captures reconciliation lifecycle state.
/// </summary>
public enum ReconciliationState
{
    NotStarted,
    InProgress,
    Matched,
    BreaksDetected,
    Resolved
}

/// <summary>
/// Captures approval workflow posture.
/// Approvals should only advance when all required gates are satisfied.
/// </summary>
public enum ApprovalState
{
    NotRequired,
    Pending,
    Approved,
    Rejected
}

/// <summary>
/// Aggregated continuity workflow snapshot for workstation payloads.
/// Most fields are server-derived projections and treated as read models.
/// </summary>
public sealed record OperationsContinuityWorkflowDto(
    string WorkflowId,
    string? FundAccountId,
    OperationsWorkflowStatus Status,
    BrokerIntakeState BrokerIntakeState,
    SecurityMasterValidationState SecurityMasterValidationState,
    LedgerPostingState LedgerPostingState,
    ReconciliationState ReconciliationState,
    ApprovalState ApprovalState,
    IReadOnlyList<OperationsGateDto> Gates,
    IReadOnlyList<OperationsTimelineEntryDto> Timeline,
    IReadOnlyList<OperationsBreakCaseDto> BreakCases,
    IReadOnlyList<OperationsApprovalDto> Approvals,
    string? LastUpdatedAtUtc,
    string? LastUpdatedBy);

/// <summary>
/// Read-model representation for a workflow gate.
/// Gate status is computed server-side from evidence and prerequisite results.
/// </summary>
public sealed record OperationsGateDto(
    string GateId,
    string GateKey,
    string DisplayName,
    OperationsGateStatus Status,
    bool IsRequired,
    string? Description,
    string? BlockingReason,
    string? CompletedAtUtc,
    string? CompletedBy);

/// <summary>
/// Client write request to transition workflow/gate state.
/// Version must match the current server version for optimistic concurrency.
/// </summary>
public sealed record OperationsTransitionRequestDto(
    string WorkflowId,
    string? GateId,
    string RequestedAction,
    string RequestedBy,
    string? Reason,
    int Version);

/// <summary>
/// Immutable timeline entry for workflow state changes.
/// Timeline rows are server-generated and append-only.
/// </summary>
public sealed record OperationsTimelineEntryDto(
    string EntryId,
    string TimestampUtc,
    string EventType,
    string Message,
    string? Actor,
    string? GateId,
    string? CorrelationId);

/// <summary>
/// Workflow break case surfaced for operator triage and follow-up.
/// Version is included for optimistic concurrency during updates.
/// </summary>
public sealed record OperationsBreakCaseDto(
    string BreakCaseId,
    string WorkflowId,
    string Category,
    string Status,
    string Summary,
    string? Detail,
    string? AssignedTo,
    string? OpenedAtUtc,
    string? ResolvedAtUtc,
    int Version);

/// <summary>
/// Approval projection and write surface for workflow sign-off.
/// Approval requires prerequisite gates to be satisfied before approval can be recorded.
/// </summary>
public sealed record OperationsApprovalDto(
    string ApprovalId,
    string WorkflowId,
    string ApprovalType,
    ApprovalState Status,
    string? Approver,
    string? DecisionAtUtc,
    string? Notes,
    int Version);
