using Meridian.Contracts.Workstation;

namespace Meridian.Application.Workflows;

public sealed class FundWorkflowCommandHandler
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, WorkflowSnapshot> _workflows = new();
    private readonly Dictionary<Guid, HashSet<string>> _requestIds = new();

    public FundWorkflowState Handle(StartWorkflow command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        EnsureStage(s, FundWorkflowStage.BrokerImport, FundWorkflowSubStatus.InProgress);
    });

    public FundWorkflowState Handle(ImportBrokerData command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        RequireStage(s, FundWorkflowStage.BrokerImport, FundWorkflowSubStatus.InProgress);
    });

    public FundWorkflowState Handle(NormalizeBrokerTransactions command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        RequireStage(s, FundWorkflowStage.BrokerImport, FundWorkflowSubStatus.InProgress);
        s.StageStatus[FundWorkflowStage.BrokerImport] = FundWorkflowSubStatus.Completed;
        s.StageStatus[FundWorkflowStage.SecurityMaster] = FundWorkflowSubStatus.InProgress;
    });

    public FundWorkflowState Handle(ResolveSecurityMasterMappings command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        RequireStage(s, FundWorkflowStage.SecurityMaster, FundWorkflowSubStatus.InProgress);
    });

    public FundWorkflowState Handle(ApproveSecurityMasterOverrides command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        RequireStage(s, FundWorkflowStage.SecurityMaster, FundWorkflowSubStatus.InProgress);
        s.StageStatus[FundWorkflowStage.SecurityMaster] = FundWorkflowSubStatus.Completed;
        s.StageStatus[FundWorkflowStage.Ledger] = FundWorkflowSubStatus.InProgress;
    });

    public FundWorkflowState Handle(BuildLedgerDraft command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        RequireStage(s, FundWorkflowStage.Ledger, FundWorkflowSubStatus.InProgress);
    });

    public FundWorkflowState Handle(ValidateLedgerDraft command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        RequireStage(s, FundWorkflowStage.Ledger, FundWorkflowSubStatus.InProgress);
        s.PostingGateOpen = true;
    });

    public FundWorkflowState Handle(PostLedgerEntries command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        Require(s.PostingGateOpen, "Posting gate must be opened by ValidateLedgerDraft.");
        s.StageStatus[FundWorkflowStage.Ledger] = FundWorkflowSubStatus.Completed;
        s.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.InProgress;
    });

    public FundWorkflowState Handle(RunReconciliation command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        RequireStage(s, FundWorkflowStage.Reconciliation, FundWorkflowSubStatus.InProgress);
        s.ReconciliationGateOpen = true;
    });

    public FundWorkflowState Handle(ResolveBreakCase command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        Require(s.ReconciliationGateOpen, "RunReconciliation must complete before resolving breaks.");
        s.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.Completed;
        s.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.InProgress;
        s.ApprovalGateOpen = true;
    });

    public FundWorkflowState Handle(SubmitForApproval command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        Require(s.ApprovalGateOpen, "Approval gate is closed.");
    });

    public FundWorkflowState Handle(ApproveWorkflow command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        Require(s.ApprovalGateOpen, "SubmitForApproval or ResolveBreakCase is required first.");
        s.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.Completed;
    });

    public FundWorkflowState Handle(RejectWorkflow command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        Require(s.ApprovalGateOpen, "Workflow must be in approval to reject.");
        RouteRejection(s, command.ReasonCode);
    });

    public FundWorkflowState Handle(CloseWorkflow command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        Require(DeriveOverallStatus(s) == FundWorkflowOverallStatus.Approved, "Only approved workflows can be closed.");
        s.IsClosed = true;
    });

    public FundWorkflowState Handle(ReopenWorkflow command) => Apply(command.WorkflowId, command.Metadata, s =>
    {
        Require(!string.IsNullOrWhiteSpace(command.Reason), "Reopen reason is required.");
        Require(string.Equals(command.Metadata.Role, "Administrator", StringComparison.OrdinalIgnoreCase)
                || string.Equals(command.Metadata.Role, "OperationsManager", StringComparison.OrdinalIgnoreCase),
            "Reopen requires elevated role (Administrator or OperationsManager).");
        Require(!string.IsNullOrWhiteSpace(command.Metadata.IncidentTicketId), "Incident/ticket ID is required for reopen.");
        Require(s.IsClosed || DeriveOverallStatus(s) == FundWorkflowOverallStatus.Rejected, "Only closed or rejected workflows can be reopened.");
        s.IsClosed = false;
        s.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.InProgress;
        s.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.NotStarted;
        s.ApprovalGateOpen = false;
    });

    private FundWorkflowState Apply(Guid workflowId, FundWorkflowCommandMetadata metadata, Action<WorkflowSnapshot> mutate)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(metadata.RequestId))
            {
                var set = _requestIds.TryGetValue(workflowId, out var existing) ? existing : (_requestIds[workflowId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (!set.Add(metadata.RequestId))
                {
                    return ToState(GetOrCreate(workflowId));
                }
            }

            var snapshot = GetOrCreate(workflowId);
            mutate(snapshot);
            snapshot.OverallStatus = DeriveOverallStatus(snapshot);
            snapshot.LastActor = metadata.Actor;
            snapshot.UpdatedAtUtc = DateTimeOffset.UtcNow;
            return ToState(snapshot);
        }
    }

    private WorkflowSnapshot GetOrCreate(Guid id)
    {
        if (_workflows.TryGetValue(id, out var found)) return found;
        var stage = Enum.GetValues<FundWorkflowStage>().ToDictionary(x => x, _ => FundWorkflowSubStatus.NotStarted);
        var created = new WorkflowSnapshot(id, FundWorkflowOverallStatus.Draft, stage, false, false, false, null, DateTimeOffset.UtcNow, false);
        _workflows[id] = created;
        return created;
    }

    private static void EnsureStage(WorkflowSnapshot s, FundWorkflowStage stage, FundWorkflowSubStatus status) => s.StageStatus[stage] = status;
    private static void RequireStage(WorkflowSnapshot s, FundWorkflowStage stage, FundWorkflowSubStatus status) => Require(s.StageStatus[stage] == status, $"Expected {stage} to be {status}.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static void RouteRejection(WorkflowSnapshot s, FundWorkflowRejectionReasonCode reason)
    {
        s.StageStatus[FundWorkflowStage.Approval] = FundWorkflowSubStatus.Blocked;
        if (reason is FundWorkflowRejectionReasonCode.LedgerMismatch or FundWorkflowRejectionReasonCode.GovernanceFailure)
        {
            s.StageStatus[FundWorkflowStage.Ledger] = FundWorkflowSubStatus.InProgress;
            s.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.NotStarted;
        }
        else
        {
            s.StageStatus[FundWorkflowStage.Reconciliation] = FundWorkflowSubStatus.InProgress;
        }
    }

    private static FundWorkflowOverallStatus DeriveOverallStatus(WorkflowSnapshot s)
    {
        if (s.IsClosed) return FundWorkflowOverallStatus.Closed;
        if (s.StageStatus[FundWorkflowStage.Approval] == FundWorkflowSubStatus.Blocked) return FundWorkflowOverallStatus.Rejected;
        if (s.StageStatus.Values.All(x => x == FundWorkflowSubStatus.Completed)) return FundWorkflowOverallStatus.Approved;
        if (s.StageStatus[FundWorkflowStage.Approval] == FundWorkflowSubStatus.InProgress) return FundWorkflowOverallStatus.AwaitingApproval;
        if (s.StageStatus.Values.Any(x => x is FundWorkflowSubStatus.InProgress or FundWorkflowSubStatus.Completed)) return FundWorkflowOverallStatus.InProgress;
        return FundWorkflowOverallStatus.Draft;
    }

    private static FundWorkflowState ToState(WorkflowSnapshot s) => new(s.WorkflowId, s.OverallStatus, new Dictionary<FundWorkflowStage, FundWorkflowSubStatus>(s.StageStatus), s.ApprovalGateOpen, s.PostingGateOpen, s.ReconciliationGateOpen, s.LastActor, s.UpdatedAtUtc);

    private sealed class WorkflowSnapshot
    {
        public WorkflowSnapshot(Guid workflowId, FundWorkflowOverallStatus overallStatus, Dictionary<FundWorkflowStage, FundWorkflowSubStatus> stageStatus, bool approvalGateOpen, bool postingGateOpen, bool reconciliationGateOpen, string? lastActor, DateTimeOffset updatedAtUtc, bool isClosed)
        { WorkflowId = workflowId; OverallStatus = overallStatus; StageStatus = stageStatus; ApprovalGateOpen = approvalGateOpen; PostingGateOpen = postingGateOpen; ReconciliationGateOpen = reconciliationGateOpen; LastActor = lastActor; UpdatedAtUtc = updatedAtUtc; IsClosed = isClosed; }
        public Guid WorkflowId { get; }
        public FundWorkflowOverallStatus OverallStatus { get; set; }
        public Dictionary<FundWorkflowStage, FundWorkflowSubStatus> StageStatus { get; }
        public bool ApprovalGateOpen { get; set; }
        public bool PostingGateOpen { get; set; }
        public bool ReconciliationGateOpen { get; set; }
        public string? LastActor { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public bool IsClosed { get; set; }
    }
}
