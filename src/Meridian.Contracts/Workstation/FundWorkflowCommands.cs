namespace Meridian.Contracts.Workstation;

public enum FundWorkflowOverallStatus : byte { Draft=0, InProgress=1, AwaitingApproval=2, Approved=3, Rejected=4, Closed=5 }
public enum FundWorkflowStage : byte { BrokerImport=0, SecurityMaster=1, Ledger=2, Reconciliation=3, Approval=4 }
public enum FundWorkflowSubStatus : byte { NotStarted=0, InProgress=1, Completed=2, Blocked=3 }
public enum FundWorkflowRejectionReasonCode : byte { DataQuality=0, MappingDefect=1, LedgerMismatch=2, ReconciliationBreak=3, GovernanceFailure=4 }

public sealed record FundWorkflowCommandMetadata(string Actor,string Role,string? RequestId=null,string? CorrelationId=null,string? IncidentTicketId=null);
public sealed record StartWorkflow(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record ImportBrokerData(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record NormalizeBrokerTransactions(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record ResolveSecurityMasterMappings(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record ApproveSecurityMasterOverrides(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record BuildLedgerDraft(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record ValidateLedgerDraft(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record PostLedgerEntries(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record RunReconciliation(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record ResolveBreakCase(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record SubmitForApproval(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record ApproveWorkflow(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record RejectWorkflow(Guid WorkflowId, FundWorkflowRejectionReasonCode ReasonCode, FundWorkflowCommandMetadata Metadata);
public sealed record CloseWorkflow(Guid WorkflowId, FundWorkflowCommandMetadata Metadata);
public sealed record ReopenWorkflow(Guid WorkflowId, string Reason, FundWorkflowCommandMetadata Metadata);

public sealed record FundWorkflowState(
    Guid WorkflowId,
    FundWorkflowOverallStatus OverallStatus,
    IReadOnlyDictionary<FundWorkflowStage, FundWorkflowSubStatus> StageStatus,
    bool ApprovalGateOpen,
    bool PostingGateOpen,
    bool ReconciliationGateOpen,
    string? LastActor,
    DateTimeOffset UpdatedAtUtc);
