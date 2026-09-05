using Meridian.Contracts.Workstation;
using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class FinancialOperationsCommandCenterReadService
{
    private async Task<CloseInputs> LoadCloseInputsAsync(CloseReadinessScopeDto scope,
        string? tenantId, string? companyId, CancellationToken ct)
    {
        var evaluatedAt = DateTimeOffset.UtcNow;
        var projection = new CloseReadinessProjection(scope, evaluatedAt);
        if (!projection.HasCompleteScope)
        {
            foreach (var id in new[] { "book-scope", "workflow", "calendar", "close-plan", "private-capital" })
                projection.Contribute(id, "Controller", "ScopeRequired", null, []);
            return new(null, null, null, projection);
        }

        var book = await ReadAsync(() => _ledgerBookService?.GetBookAsync(scope.LedgerBookId!.Value, ct), ct);
        var bound = book is not null && book.LedgerBookId == scope.LedgerBookId
            && string.Equals(book.FundProfileId, scope.FundProfileId, StringComparison.Ordinal);
        projection.Contribute("book-scope", "Accounting", bound ? "Ready" : "ScopeMismatch", evaluatedAt,
            [scope.LedgerBookId!.Value.ToString()], "The selected book must belong to the selected fund profile.");
        if (!bound)
        {
            foreach (var id in new[] { "workflow", "calendar", "close-plan", "private-capital" })
                projection.Contribute(id, "Controller", "Unavailable", null, [], "Resolve the book/fund scope before loading close evidence.");
            return new(null, null, null, projection);
        }

        var summaries = await ReadAsync(() => _workflowService.ListAsync(scope.FundAccountId,
            scope.PeriodId, status: null, ct, ledgerBookId: scope.LedgerBookId), ct);
        var workflows = new List<OperationsContinuityWorkflowDto>();
        var workflowFailure = summaries is null;
        foreach (var summary in summaries is { Count: 1 } ? summaries : [])
        {
            var item = await ReadAsync(() => _workflowService.GetAsync(summary.WorkflowId, ct), ct);
            if (item is null || item.WorkflowId != summary.WorkflowId
                || item.FundAccountId != scope.FundAccountId || item.LedgerBookId != scope.LedgerBookId
                || !string.Equals(item.PeriodId, scope.PeriodId, StringComparison.Ordinal))
            {
                workflowFailure = true;
                continue;
            }
            workflows.Add(item);
        }

        // Two workflows for the same declared subject require an explicit resolution, never a newest-row guess.
        var workflow = !workflowFailure && workflows.Count == 1 ? workflows[0] : null;
        projection.Contribute("workflow", "Operations", workflow is null ? "Incomplete"
            : workflow.CloseReadiness?.IsReadyToClose == true ? "Ready" : "Blocked",
            evaluatedAt, workflows.Select(w => w.WorkflowId.ToString()).ToArray(),
            workflow is null ? "Exactly one matching book/account/period workflow must be available."
                : "Resolve the workflow's close requirements.");

        var calendar = await ReadAsync(() => _closeCalendarService?.GetCalendarAsync(scope.FundAccountId, scope.PeriodId, ct), ct);
        var calendarRows = calendar?.Items.Where(i => i.FundAccountId == scope.FundAccountId
            && i.PeriodId == scope.PeriodId && workflows.Any(w => w.WorkflowId == i.WorkflowId)).ToArray();
        // Calendar transport has no book dimension; bind every record through the checked workflow identity.
        var calendarBound = calendar is not null && workflow is not null && calendarRows?.Length == 1;
        projection.Contribute("calendar", "Close calendar", !calendarBound ? "Incomplete"
            : calendarRows!.All(i => i.IsReadyToClose && i.BlockerCount == 0 && i.OpenChecklistCount == 0
                && i.CompletedApprovalCount >= i.RequiredApprovalCount) ? "Ready" : "Blocked",
            calendar?.GeneratedAtUtc, calendarRows?.Select(i => i.WorkflowId.ToString()).ToArray() ?? [],
            "A current calendar evaluation of the selected workflow must clear its checklist, blockers, and approvals.");
        calendar = calendarBound ? calendar! with { Items = calendarRows! } : null;

        var plan = workflow is null ? null : await ReadAsync(() => _closeManagementService?
            .GetPeriodPlanScopedAsync(workflow.WorkflowId, tenantId, companyId, ct), ct);
        var planBound = plan is not null && plan.FundProfileId == scope.FundProfileId
            && plan.LedgerBookId == scope.LedgerBookId && plan.PeriodId == scope.PeriodId
            && plan.WorkflowVersion == workflow!.Version;
        var planReady = planBound && plan!.ValidationIssues.Count == 0
            && plan.Tasks.All(t => t.Status == CloseTaskStatusDto.SignedOff && string.IsNullOrWhiteSpace(t.BlockerReason))
            && plan.LateAdjustments.All(a => a.ApprovalState is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Rejected);
        projection.Contribute("close-plan", "Controller", !planBound ? "Incomplete" : planReady ? "Ready" : "Blocked",
            evaluatedAt, planBound ? [plan!.ClosePlanId] : [],
            "The version-matched close plan must clear validation, task sign-offs, and late-adjustment review.");

        var cockpit = await ReadAsync(() => _privateCapitalCloseCockpitService?.GetCockpitAsync(
            scope.FundProfileId, scope.LedgerBookId, scope.FundAccountId, scope.PeriodId, scope.EntityId,
            ct, tenantId, companyId), ct);
        var cockpitBound = cockpit is not null && cockpit.FundProfileId == scope.FundProfileId
            && cockpit.LedgerBookId == scope.LedgerBookId && cockpit.FundAccountId == scope.FundAccountId
            && cockpit.PeriodId == scope.PeriodId && cockpit.EntityId == scope.EntityId
            && workflow is not null && cockpit.Workflows.Count == 1
            && cockpit.Workflows[0].WorkflowId == workflow.WorkflowId
            && cockpit.Workflows[0].FundAccountId == scope.FundAccountId
            && cockpit.Workflows[0].PeriodId == scope.PeriodId;
        projection.Contribute("private-capital", "Fund accounting", !cockpitBound ? "ScopeMismatch"
            : cockpit!.IsReadyToClose && cockpit.Blockers.Count == 0 ? "Ready" : "Blocked",
            cockpit?.ProjectedAtUtc, cockpitBound ? [workflow!.WorkflowId.ToString()] : [],
            "Fund accounting must provide current evidence for the exact fund, book, account, entity, period, and workflow.");
        return new(workflow, calendar, cockpitBound ? cockpit : null, projection);
    }

    private static async Task<T?> ReadAsync<T>(Func<Task<T>?> read, CancellationToken ct) where T : class
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var pending = read();
            return pending is null ? null : await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // A failed contributor is unavailable, never an empty successful result. Do not expose
            // storage/connection exception text in the operator payload.
            return null;
        }
    }

    private sealed record CloseInputs(OperationsContinuityWorkflowDto? Workflow,
        OperationsCloseCalendarDto? Calendar, PrivateCapitalCloseCockpitDto? Cockpit,
        CloseReadinessProjection Projection);
}
