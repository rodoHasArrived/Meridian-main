using System;
using System.Collections.Generic;

namespace Meridian.Application.OperationsContinuity;

public sealed class OperationsContinuityWorkflow
{
    private readonly List<OperationsContinuityAuditEntry> _auditTrail = new();

    public OperationsContinuityWorkflow(
        Guid workflowId,
        Guid fundAccountId,
        string periodId,
        Guid securityMasterSnapshotId,
        string brokerSource,
        DateTimeOffset createdAtUtc,
        long version)
    {
        WorkflowId = workflowId;
        FundAccountId = fundAccountId;
        PeriodId = RequireNonEmpty(periodId, nameof(periodId));
        SecurityMasterSnapshotId = securityMasterSnapshotId;
        BrokerSource = RequireNonEmpty(brokerSource, nameof(brokerSource));
        CreatedAtUtc = EnsureUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = CreatedAtUtc;
        Version = version;

        BrokerIngestGate = ContinuityGate.Pending;
        SecurityMasterGate = ContinuityGate.Pending;
        LedgerPostingGate = ContinuityGate.Pending;
        ReconciliationGate = ContinuityGate.Pending;
        ApprovalGate = ContinuityGate.Pending;
    }

    public Guid WorkflowId { get; }
    public Guid FundAccountId { get; }
    public string PeriodId { get; }
    public Guid SecurityMasterSnapshotId { get; }
    public string BrokerSource { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public ContinuityGate BrokerIngestGate { get; private set; }
    public ContinuityGate SecurityMasterGate { get; private set; }
    public ContinuityGate LedgerPostingGate { get; private set; }
    public ContinuityGate ReconciliationGate { get; private set; }
    public ContinuityGate ApprovalGate { get; private set; }

    public BrokerSubState BrokerState { get; private set; }
    public SecuritySubState SecurityState { get; private set; }
    public LedgerSubState LedgerState { get; private set; }
    public ReconciliationSubState ReconciliationState { get; private set; }
    public ApprovalSubState ApprovalState { get; private set; }

    public bool CloseSucceeded { get; private set; }
    public int CriticalBreakCountOpen { get; private set; }

    public IReadOnlyList<OperationsContinuityAuditEntry> AuditTrail => _auditTrail;

    public ContinuityOverallStatus DeriveOverallStatus()
    {
        if (IsBlocked())
        {
            return ContinuityOverallStatus.Blocked;
        }

        if (CloseSucceeded)
        {
            return ContinuityOverallStatus.Closed;
        }

        if (AllGatesPassed() && ApprovalState == ApprovalSubState.Approved)
        {
            return ContinuityOverallStatus.ReadyForClose;
        }

        // Reconciliation-primary override for review-required workflows.
        if (ReconciliationGate == ContinuityGate.ReviewRequired || ReconciliationState == ReconciliationSubState.ReviewRequired)
        {
            return ContinuityOverallStatus.ReviewRequired;
        }

        if (IsAnyReviewRequired())
        {
            return ContinuityOverallStatus.ReviewRequired;
        }

        return HighestActiveStage();
    }

    public void TransitionBroker(
        BrokerSubState newState,
        ContinuityGate gate,
        OperationsContinuityAuditEntry auditEntry)
    {
        BrokerState = newState;
        BrokerIngestGate = gate;
        AppendAuditAndBump(auditEntry);
    }

    public void TransitionSecurity(
        SecuritySubState newState,
        ContinuityGate gate,
        OperationsContinuityAuditEntry auditEntry)
    {
        SecurityState = newState;
        SecurityMasterGate = gate;
        AppendAuditAndBump(auditEntry);
    }

    public void TransitionLedger(
        LedgerSubState newState,
        ContinuityGate gate,
        OperationsContinuityAuditEntry auditEntry)
    {
        if (!IsSecurityResolved())
        {
            throw OperationsContinuityWorkflowException.WithCode(
                OperationsContinuityWorkflowErrorCode.LedgerPostBeforeSecurityResolution,
                "Cannot transition ledger state before the security master stage is resolved.");
        }

        LedgerState = newState;
        LedgerPostingGate = gate;
        AppendAuditAndBump(auditEntry);
    }

    public void TransitionReconciliation(
        ReconciliationSubState newState,
        ContinuityGate gate,
        int criticalBreakCountOpen,
        OperationsContinuityAuditEntry auditEntry)
    {
        if (criticalBreakCountOpen < 0)
        {
            throw OperationsContinuityWorkflowException.WithCode(
                OperationsContinuityWorkflowErrorCode.InvalidCriticalBreakCount,
                "Critical break count cannot be negative.");
        }

        ReconciliationState = newState;
        ReconciliationGate = gate;
        CriticalBreakCountOpen = criticalBreakCountOpen;
        AppendAuditAndBump(auditEntry);
    }

    public void TransitionApproval(
        ApprovalSubState newState,
        ContinuityGate gate,
        string? approvedByOperator,
        string? rationale,
        string? evidenceReference,
        OperationsContinuityAuditEntry auditEntry)
    {
        if (newState == ApprovalSubState.Approved &&
            (string.IsNullOrWhiteSpace(approvedByOperator) || string.IsNullOrWhiteSpace(rationale) || string.IsNullOrWhiteSpace(evidenceReference)))
        {
            throw OperationsContinuityWorkflowException.WithCode(
                OperationsContinuityWorkflowErrorCode.ApprovalRequiresOperatorRationaleEvidence,
                "Approval requires operator, rationale, and evidence reference.");
        }

        ApprovalState = newState;
        ApprovalGate = gate;
        AppendAuditAndBump(auditEntry);
    }

    public void MarkClosed(OperationsContinuityAuditEntry auditEntry)
    {
        if (CriticalBreakCountOpen > 0)
        {
            throw OperationsContinuityWorkflowException.WithCode(
                OperationsContinuityWorkflowErrorCode.CloseBlockedByCriticalBreaks,
                "Cannot close workflow while critical reconciliation breaks are open.");
        }

        if (DeriveOverallStatus() != ContinuityOverallStatus.ReadyForClose)
        {
            throw OperationsContinuityWorkflowException.WithCode(
                OperationsContinuityWorkflowErrorCode.CloseRequiresReadyForCloseStatus,
                "Workflow must be ReadyForClose before closure.");
        }

        CloseSucceeded = true;
        AppendAuditAndBump(auditEntry);
    }

    private void AppendAuditAndBump(OperationsContinuityAuditEntry auditEntry)
    {
        if (auditEntry is null)
        {
            throw OperationsContinuityWorkflowException.WithCode(
                OperationsContinuityWorkflowErrorCode.AuditAppendRequired,
                "State transitions require a non-null audit entry.");
        }

        if (auditEntry.ChangedAtUtc.Offset != TimeSpan.Zero)
        {
            throw OperationsContinuityWorkflowException.WithCode(
                OperationsContinuityWorkflowErrorCode.AuditTimestampMustBeUtc,
                "Audit entry timestamp must be UTC.");
        }

        _auditTrail.Add(auditEntry);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
        Version++;
    }

    private bool IsBlocked() =>
        BrokerIngestGate == ContinuityGate.Blocked ||
        SecurityMasterGate == ContinuityGate.Blocked ||
        LedgerPostingGate == ContinuityGate.Blocked ||
        ReconciliationGate == ContinuityGate.Blocked ||
        ApprovalGate == ContinuityGate.Blocked;

    private bool IsAnyReviewRequired() =>
        BrokerIngestGate == ContinuityGate.ReviewRequired ||
        SecurityMasterGate == ContinuityGate.ReviewRequired ||
        LedgerPostingGate == ContinuityGate.ReviewRequired ||
        ApprovalGate == ContinuityGate.ReviewRequired;

    private bool IsSecurityResolved() =>
        SecurityMasterGate == ContinuityGate.Passed &&
        (SecurityState == SecuritySubState.Resolved || SecurityState == SecuritySubState.Completed);

    private bool AllGatesPassed() =>
        BrokerIngestGate == ContinuityGate.Passed &&
        SecurityMasterGate == ContinuityGate.Passed &&
        LedgerPostingGate == ContinuityGate.Passed &&
        ReconciliationGate == ContinuityGate.Passed &&
        ApprovalGate == ContinuityGate.Passed;

    private ContinuityOverallStatus HighestActiveStage()
    {
        if (ApprovalState is ApprovalSubState.Pending or ApprovalSubState.InReview)
        {
            return ContinuityOverallStatus.Approval;
        }

        if (ReconciliationState is ReconciliationSubState.Pending or ReconciliationSubState.Reconciling)
        {
            return ContinuityOverallStatus.Reconciliation;
        }

        if (LedgerState is LedgerSubState.Pending or LedgerSubState.Posting)
        {
            return ContinuityOverallStatus.LedgerPosting;
        }

        if (SecurityState is SecuritySubState.Pending or SecuritySubState.Validating)
        {
            return ContinuityOverallStatus.SecurityMaster;
        }

        return ContinuityOverallStatus.BrokerIngest;
    }

    private static string RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return value.Trim();
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be UTC.", parameterName);
        }

        return value;
    }
}

public sealed record OperationsContinuityAuditEntry(
    string Transition,
    string Actor,
    string Rationale,
    string EvidenceReference,
    DateTimeOffset ChangedAtUtc);

public sealed class OperationsContinuityWorkflowException : Exception
{
    public OperationsContinuityWorkflowException(OperationsContinuityWorkflowErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public OperationsContinuityWorkflowErrorCode Code { get; }

    public static OperationsContinuityWorkflowException WithCode(OperationsContinuityWorkflowErrorCode code, string message) =>
        new(code, message);
}

public enum OperationsContinuityWorkflowErrorCode
{
    LedgerPostBeforeSecurityResolution = 1,
    CloseBlockedByCriticalBreaks = 2,
    ApprovalRequiresOperatorRationaleEvidence = 3,
    AuditAppendRequired = 4,
    AuditTimestampMustBeUtc = 5,
    InvalidCriticalBreakCount = 6,
    CloseRequiresReadyForCloseStatus = 7
}

public enum ContinuityGate
{
    Pending = 0,
    Passed = 1,
    ReviewRequired = 2,
    Blocked = 3
}

public enum ContinuityOverallStatus
{
    BrokerIngest = 0,
    SecurityMaster = 1,
    LedgerPosting = 2,
    Reconciliation = 3,
    Approval = 4,
    ReviewRequired = 5,
    ReadyForClose = 6,
    Closed = 7,
    Blocked = 8
}

public enum BrokerSubState
{
    Pending = 0,
    Ingesting = 1,
    Completed = 2,
    Failed = 3
}

public enum SecuritySubState
{
    Pending = 0,
    Validating = 1,
    Resolved = 2,
    Completed = 3,
    Failed = 4
}

public enum LedgerSubState
{
    Pending = 0,
    Posting = 1,
    Posted = 2,
    Failed = 3
}

public enum ReconciliationSubState
{
    Pending = 0,
    Reconciling = 1,
    ReviewRequired = 2,
    Cleared = 3,
    Failed = 4
}

public enum ApprovalSubState
{
    Pending = 0,
    InReview = 1,
    Approved = 2,
    Rejected = 3
}
