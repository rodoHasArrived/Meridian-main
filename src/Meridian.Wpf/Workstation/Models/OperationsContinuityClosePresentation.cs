using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Workstation.Models;

/// <summary>Displays the selected shared close decision; legacy gate totals cannot establish close readiness.</summary>
public sealed record OperationsContinuityClosePresentation(bool IsReady, string Label, string Detail)
{
    public static OperationsContinuityClosePresentation Build(
        OperationsContinuityWorkflowDto? workflow,
        FinancialOperationsCommandCenterDto? commandCenter,
        CloseReadinessScopeDto? selectedScope)
    {
        if (selectedScope is null || string.IsNullOrWhiteSpace(selectedScope.FundProfileId) ||
            selectedScope.LedgerBookId.GetValueOrDefault() == Guid.Empty || selectedScope.FundAccountId.GetValueOrDefault() == Guid.Empty ||
            string.IsNullOrWhiteSpace(selectedScope.EntityId) || string.IsNullOrWhiteSpace(selectedScope.PeriodId))
            return Blocked("Select the fund, ledger book, account, entity, and period before evaluating close readiness.");
        if (workflow is null || commandCenter?.ActiveWorkflow is not { } active ||
            active.WorkflowId != workflow.WorkflowId || active.Version != workflow.Version ||
            workflow.FundAccountId != selectedScope.FundAccountId || workflow.PeriodId != selectedScope.PeriodId ||
            workflow.LedgerBookId != selectedScope.LedgerBookId)
            return Blocked("The shared close decision does not match the selected workflow and version. Refresh the selected scope.");
        if (commandCenter.CloseReadiness is not { } decision || decision.Scope != selectedScope)
            return Blocked("Shared close readiness is unavailable for the selected scope. Refresh the close evidence.");
        if (decision is { IsComplete: true, IsReadyToClose: true, Status: "Ready", Blockers.Count: 0 })
            return new(true, "Ready to close", "The shared service confirms complete close evidence for this scope and workflow version.");
        return Blocked(decision.Blockers.Count > 0
            ? string.Join(" ", decision.Blockers.Select(blocker => $"{blocker.ContributorId}: {blocker.Message}"))
            : "The shared close evidence is incomplete or requires review. Resolve the issue and refresh readiness.");
    }

    private static OperationsContinuityClosePresentation Blocked(string detail) => new(false, "Close blocked", detail);
}
