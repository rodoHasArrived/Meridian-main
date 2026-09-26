using Meridian.Contracts.Ledger;

namespace Meridian.Wpf.ViewModels.Accounting;

public sealed partial class AccountingCloseViewModel
{
    private long _closeWorkflowSelectionRevision;

    public void ApplyClosePlan(ClosePeriodPlanDto closePlan)
    {
        ApplyClosePlan(
            closePlan.Configuration?.WorkflowId ?? Guid.Empty,
            closePlan.WorkflowVersion,
            closePlan);
    }

    public void ApplyClosePlan(Guid workflowId, ClosePeriodPlanDto closePlan)
        => ApplyClosePlan(workflowId, closePlan.WorkflowVersion, closePlan);

    public void ApplyClosePlan(Guid workflowId, long workflowVersion, ClosePeriodPlanDto closePlan)
    {
        ArgumentNullException.ThrowIfNull(closePlan);
        ++_closeWorkflowSelectionRevision;
        _closeWorkflowId = workflowId;
        _closeWorkflowIdText = workflowId == Guid.Empty ? string.Empty : workflowId.ToString("D");
        RaisePropertyChanged(nameof(CloseWorkflowIdText));
        LoadClosePlanCommand.NotifyCanExecuteChanged();
        _closeWorkflowVersion = closePlan.WorkflowVersion > 0
            ? closePlan.WorkflowVersion
            : Math.Max(0, workflowVersion);
        _closePlan = closePlan;
        RaisePropertyChanged(nameof(CloseScopeStatusText));
        ApplyClosingEntriesGate(closePlan);
        ApplyCloseSetupDraft(closePlan);
        ApplyCloseReviewRows(closePlan);
        ClosePlanSetupStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; setup retention is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; setup changes require a governed reopen workflow."
            : $"Close plan {closePlan.PeriodId} loaded for governed setup retention.";
        ClosePeriodLockStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; period lock is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is already locked."
            : ResolveClosePeriodLockStatus(closePlan);
        CloseTaskSignOffStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; task sign-off is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; task sign-off requires a governed reopen workflow."
            : ApplyCloseTaskSignOffDraft(closePlan) is { } signOffTask
                ? $"Close task {signOffTask.TaskId} is ready for WPF sign-off evidence retention."
                : $"Close plan {closePlan.PeriodId} has no open task sign-off requirement.";
        LateAdjustmentCurrency = closePlan.MaterialityPolicy.Currency;
        LateAdjustmentRequestStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; late-adjustment requests are disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; late-adjustment requests require a governed reopen workflow."
            : ValidateLateAdjustmentDraft(closePlan) ?? $"Close plan {closePlan.PeriodId} is ready for retained late-adjustment requests.";
        LateAdjustmentReviewStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; late-adjustment review is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; late-adjustment review requires a governed reopen workflow."
            : ApplyLateAdjustmentReviewDraft(closePlan) is { } adjustment
                ? $"Late adjustment {adjustment.RequestId} is ready for WPF review."
                : $"Close plan {closePlan.PeriodId} has no submitted late adjustment to review.";
        CloseEvidenceReviewStatusText = workflowId == Guid.Empty
            ? $"Close plan {closePlan.PeriodId} loaded without workflow context; blocker/evidence review is disabled."
            : closePlan.IsPeriodLocked
            ? $"Close plan {closePlan.PeriodId} is locked; blocker/evidence review requires a governed reopen workflow."
            : ApplyCloseEvidenceReviewDraft(closePlan) is { } issue
                ? $"Close blocker {issue.Code} is ready for WPF evidence review."
                : $"Close plan {closePlan.PeriodId} has no unreviewed active blockers.";
        ApplyClosePeriodLockIssues(closePlan.ValidationIssues);
        ConfigureClosePlanCommand.NotifyCanExecuteChanged();
        SignOffCloseTaskCommand.NotifyCanExecuteChanged();
        RequestLateAdjustmentCommand.NotifyCanExecuteChanged();
        ReviewLateAdjustmentCommand.NotifyCanExecuteChanged();
        ReviewCloseEvidenceCommand.NotifyCanExecuteChanged();
        QueueClosingEntriesCommand.NotifyCanExecuteChanged();
        LockClosePeriodCommand.NotifyCanExecuteChanged();
        RefreshCloseWorkflowSteps();
    }

    private async Task LoadClosePlanAsync()
    {
        if (_closeManagementService is null)
        {
            ClosePlanSetupStatusText = "Close management service is not registered for this desktop session.";
            return;
        }

        if (!Guid.TryParse(CloseWorkflowIdText, out var workflowId) || workflowId == Guid.Empty)
        {
            ClosePlanSetupStatusText = "Enter a close workflow id before loading governed close setup.";
            return;
        }

        InvalidateCloseWorkflowSelection();
        var selectionRevision = _closeWorkflowSelectionRevision;
        ClosePlanSetupStatusText = $"Loading close workflow {workflowId:D}.";
        ClosePeriodPlanDto? closePlan;
        try
        {
            closePlan = await _closeManagementService.GetPeriodPlanAsync(workflowId).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            if (IsCurrentCloseWorkflowSelection(selectionRevision, workflowId))
            {
                ClosePlanSetupStatusText = $"Close plan could not be loaded: {ex.Message}";
                ClosePeriodLockStatusText = "Refresh the selected workflow after resolving its server evidence or access issue.";
            }
            return;
        }
        if (!IsCurrentCloseWorkflowSelection(selectionRevision, workflowId))
        {
            return;
        }

        if (closePlan is null)
        {
            ClosePlanSetupStatusText = $"Close workflow {workflowId:D} was not found.";
            return;
        }

        ApplyClosePlan(workflowId, closePlan);
        ClosePlanSetupStatusText = $"Loaded close plan {closePlan.PeriodId} for governed setup retention.";
    }

    private void InvalidateCloseWorkflowSelection()
    {
        ++_closeWorkflowSelectionRevision;
        _closeWorkflowId = Guid.Empty;
        _closeWorkflowVersion = 0;
        _closePlan = null;
        ClosingEntriesGate = null;
        CloseMaterialityRows.Clear();
        CloseTaskRows.Clear();
        CloseDependencyRows.Clear();
        CloseSignOffMatrixRows.Clear();
        CloseLateAdjustmentRows.Clear();
        CloseEvidenceReviewRows.Clear();
        ClosePeriodLockIssueRows.Clear();
        CloseOperatingCoverageRows.Clear();
        ClosingEntryBalanceRows.Clear();
        CloseSetupTaskOptions.Clear();
        ClosePlanSetupStatusText = "Load the selected workflow before retaining close setup.";
        ClosePeriodLockStatusText = "Load the selected workflow before locking the accounting period.";
        CloseTaskSignOffStatusText = "Load the selected workflow before retaining sign-off evidence.";
        LateAdjustmentRequestStatusText = "Load the selected workflow before requesting late adjustments.";
        LateAdjustmentReviewStatusText = "Load the selected workflow before reviewing late adjustments.";
        CloseEvidenceReviewStatusText = "Load the selected workflow before retaining evidence review.";
        RaisePropertyChanged(nameof(CloseScopeStatusText));
        ConfigureClosePlanCommand.NotifyCanExecuteChanged();
        SignOffCloseTaskCommand.NotifyCanExecuteChanged();
        RequestLateAdjustmentCommand.NotifyCanExecuteChanged();
        ReviewLateAdjustmentCommand.NotifyCanExecuteChanged();
        ReviewCloseEvidenceCommand.NotifyCanExecuteChanged();
        QueueClosingEntriesCommand.NotifyCanExecuteChanged();
        LockClosePeriodCommand.NotifyCanExecuteChanged();
        RefreshCloseWorkflowSteps();
    }

    private void InvalidatePendingCloseScopeResponses()
    {
        if (LoadClosePlanCommand.IsRunning || ConfigureClosePlanCommand.IsRunning ||
            SignOffCloseTaskCommand.IsRunning || RequestLateAdjustmentCommand.IsRunning ||
            ReviewLateAdjustmentCommand.IsRunning || ReviewCloseEvidenceCommand.IsRunning ||
            QueueClosingEntriesCommand.IsRunning || LockClosePeriodCommand.IsRunning)
        {
            // Keep the operator's workflow text and explicit scope for a current reload.
            InvalidateCloseWorkflowSelection();
            ClosePlanSetupStatusText = "Close scope changed while a request was running. Reload the selected workflow before retaining another close command.";
            return;
        }

        ++_closeWorkflowSelectionRevision;
    }

    private bool IsCurrentCloseWorkflowSelection(long revision, Guid workflowId)
        => revision == _closeWorkflowSelectionRevision &&
           Guid.TryParse(CloseWorkflowIdText, out var selectedWorkflowId) && selectedWorkflowId == workflowId;
}
