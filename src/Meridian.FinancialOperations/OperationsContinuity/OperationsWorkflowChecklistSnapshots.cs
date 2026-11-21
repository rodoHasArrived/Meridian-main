using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public static class OperationsWorkflowChecklistSnapshots
{
    /// <summary>Called only at a boundary committing this exact workflow state and audit together.</summary>
    public static OperationsWorkflowAuditDraft ForCommit(OperationsWorkflowAuditDraft draft,
        OperationsContinuityWorkflow workflow)
    {
        if (draft.WorkflowId != workflow.WorkflowId || draft.FundAccountId != workflow.FundAccountId || draft.PeriodId != workflow.PeriodId)
            throw new InvalidDataException("Checklist snapshot scope does not match its committed workflow audit.");
        return draft with
        {
            CloseChecklistSnapshot = new(1, checked(workflow.Version + 1), workflow.Gates.Select(gate =>
                new OperationsCloseChecklistGateSnapshotDto($"close-gate-{gate.GateKey}".ToLowerInvariant(), gate.GateKey, gate.Status)).ToArray())
        };
    }

    public static void Validate(OperationsCloseChecklistSnapshotDto snapshot)
    {
        var gates = Enum.GetValues<OperationsGateKeyDto>();
        if (snapshot.SchemaVersion != 1 || snapshot.WorkflowVersion < 1 || snapshot.Gates is null
            || snapshot.Gates.Count != gates.Length || snapshot.Gates.Any(gate => gate is null)
            || snapshot.Gates.Select(gate => gate.Gate).Distinct().Count() != gates.Length
            || snapshot.Gates.Any(gate => !Enum.IsDefined(gate.Gate) || !Enum.IsDefined(gate.Status)
                || gate.TaskId != $"close-gate-{gate.Gate}".ToLowerInvariant()))
            throw new InvalidDataException("The retained checklist snapshot has an unknown version or incomplete task population.");
    }
}
