using Meridian.Contracts.Workstation;

namespace Meridian.Wpf.Workstation.Models;

/// <summary>Displays the selected shared close decision; legacy gate totals cannot establish close readiness.</summary>
public sealed record OperationsContinuityClosePresentation(bool IsReady, string Label, string Detail)
{
    public IReadOnlyList<CloseReadinessBlockerDto> Blockers { get; init; } = [];
    public IReadOnlyList<OperationsContinuityCloseBlockerPresentation> BlockerRows
        => Blockers.Select(static blocker => new OperationsContinuityCloseBlockerPresentation(blocker)).ToArray();

    public static OperationsContinuityClosePresentation Build(
        OperationsContinuityWorkflowDto? workflow,
        FinancialOperationsCommandCenterDto? commandCenter,
        CloseReadinessScopeDto? selectedScope)
    {
        if (selectedScope is null || string.IsNullOrWhiteSpace(selectedScope.FundProfileId) ||
            selectedScope.LedgerBookId is not { } bookId || bookId == Guid.Empty ||
            selectedScope.FundAccountId is not { } accountId || accountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(selectedScope.EntityId) || string.IsNullOrWhiteSpace(selectedScope.PeriodId))
            return Blocked("Select the fund, ledger book, account, entity, and period before evaluating close readiness.");
        if (workflow is null || commandCenter?.ActiveWorkflow is not { } active ||
            active.WorkflowId != workflow.WorkflowId || active.Version != workflow.Version ||
            active.FundAccountId != workflow.FundAccountId || active.PeriodId != workflow.PeriodId ||
            active.LedgerBookId != workflow.LedgerBookId ||
            workflow.FundAccountId != selectedScope.FundAccountId || workflow.PeriodId != selectedScope.PeriodId ||
            workflow.LedgerBookId != selectedScope.LedgerBookId)
            return Blocked("The shared close decision does not match the selected workflow and version. Refresh the selected scope.");
        if (commandCenter.CloseReadiness is not { } decision || decision.Scope != selectedScope)
            return Blocked("Shared close readiness is unavailable for the selected scope. Refresh the close evidence.");
        if (!MatchesPublicationEvidence(workflow, active))
            return Blocked("The shared close decision and selected workflow contain different report-pack or publication evidence. Refresh the selected workflow and close evidence.");
        if (decision is { IsComplete: true, IsReadyToClose: true, Status: "Ready", Blockers.Count: 0 })
            return new(true, "Ready to close", "The shared service confirms complete close evidence for this scope and workflow version.");
        return Blocked(decision.Blockers.Count > 0
            ? string.Join(" ", decision.Blockers.Select(blocker => $"{blocker.ContributorId}: {blocker.Message}"))
            : "The shared close evidence is incomplete or requires review. Resolve the issue and refresh readiness.") with
        {
            Blockers = decision.Blockers
        };
    }

    private static OperationsContinuityClosePresentation Blocked(string detail) => new(false, "Close blocked", detail);

    private static bool MatchesPublicationEvidence(OperationsContinuityWorkflowDto selected, OperationsContinuityWorkflowDto active)
        => selected.ReportPackReadiness.ReportPackId == active.ReportPackReadiness.ReportPackId &&
           selected.ReportPackReadiness.IsReady == active.ReportPackReadiness.IsReady &&
           EvidenceIdentitiesMatch(selected.ReportPackReadiness.EvidenceLinks, active.ReportPackReadiness.EvidenceLinks) &&
           EvidenceIdentitiesMatch(PublicationEvidence(selected), PublicationEvidence(active)) &&
           ChecklistApprovalIdentitiesMatch(selected, active);

    private static bool ChecklistApprovalIdentitiesMatch(OperationsContinuityWorkflowDto selected, OperationsContinuityWorkflowDto active)
    {
        var selectedIdentities = PublicationChecklistApprovals(selected);
        var activeIdentities = PublicationChecklistApprovals(active);
        return selectedIdentities.Count == activeIdentities.Count && selectedIdentities.All(pair =>
            activeIdentities.TryGetValue(pair.Key, out var approvedAtUtc) && pair.Value == approvedAtUtc);
    }

    private static Dictionary<(string TaskId, string ApprovedBy), DateTimeOffset> PublicationChecklistApprovals(
        OperationsContinuityWorkflowDto workflow)
    {
        var identities = new Dictionary<(string TaskId, string ApprovedBy), DateTimeOffset>();
        foreach (var task in workflow.CloseChecklist)
        {
            var status = task.Status.Trim().ToLowerInvariant();
            var ready = task.AcknowledgedAtUtc is not null || status is "done" or "complete" or "completed" or "acknowledged";
            if (task.Gate != OperationsGateKeyDto.Approval && ready && status is not ("blocked" or "expired") &&
                string.IsNullOrWhiteSpace(task.BlockingReason) && !string.IsNullOrWhiteSpace(task.EvidencePointer))
                Add(task.TaskId, task.AcknowledgedBy, task.AcknowledgedAtUtc);
        }

        if (workflow.ApprovalState == OperationsApprovalStateDto.Approved &&
            workflow.Approvals.LastOrDefault() is { Status: OperationsApprovalStateDto.Approved } decision)
        {
            var taskId = workflow.CloseChecklist.FirstOrDefault(static task => task.Gate == OperationsGateKeyDto.Approval)?.TaskId
                ?? "close-gate-approval";
            if (decision.SubmittedAtUtc is not null)
                Add(taskId, decision.Operator, decision.SubmittedAtUtc);
            else if (workflow.Approvals.Count > 1 && workflow.Approvals[^2] is
                     { Status: OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned } submission)
                Add(taskId, submission.Operator, submission.SubmittedAtUtc);
            Add(taskId, decision.Reviewer, decision.DecidedAtUtc);
        }

        return identities;

        void Add(string? taskId, string? approvedBy, DateTimeOffset? approvedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(approvedBy) || approvedAtUtc is null)
                return;
            // Publication forwards the first trimmed task/actor pair, compared case-insensitively by the shared guard.
            identities.TryAdd((taskId.Trim().ToLowerInvariant(), approvedBy.Trim().ToLowerInvariant()), approvedAtUtc.Value);
        }
    }

    private static IEnumerable<OperationsEvidenceLinkDto> PublicationEvidence(OperationsContinuityWorkflowDto workflow)
        => workflow.ReportPackReadiness.EvidenceLinks
            .Concat(workflow.EvidenceLinks)
            .Concat(workflow.EvidencePackages.SelectMany(static package => package.EvidenceLinks))
            .Concat(workflow.Approvals.SelectMany(static approval => approval.EvidenceLinks));

    private static bool EvidenceIdentitiesMatch(IEnumerable<OperationsEvidenceLinkDto> selected, IEnumerable<OperationsEvidenceLinkDto> active)
    {
        var selectedIdentities = EvidenceIdentities(selected);
        var activeIdentities = EvidenceIdentities(active);
        return selectedIdentities.Count == activeIdentities.Count && selectedIdentities.All(pair =>
            activeIdentities.TryGetValue(pair.Key, out var identity) && pair.Value == identity);
    }

    private static Dictionary<string, (string? Source, string? Route, DateTimeOffset? CapturedAtUtc)> EvidenceIdentities(
        IEnumerable<OperationsEvidenceLinkDto> evidence)
    {
        var identities = new Dictionary<string, (string? Source, string? Route, DateTimeOffset? CapturedAtUtc)>(StringComparer.Ordinal);
        // Match shared publication's first-link-per-evidence-ID precedence; labels and list order do not identify retained evidence.
        foreach (var link in evidence)
            identities.TryAdd(link.EvidenceId, (link.Source, link.Route, link.CapturedAtUtc));
        return identities;
    }
}

public sealed record OperationsContinuityCloseBlockerPresentation(CloseReadinessBlockerDto Blocker)
{
    public string OwnerLabel => string.IsNullOrWhiteSpace(Blocker.Owner) ? "Owner unavailable" : Blocker.Owner;

    public string RepairInstruction => string.IsNullOrWhiteSpace(Blocker.Owner)
        ? "Repair: identify the owning lane, resolve this contributor and its causing records through that workflow, then select Evaluate close to refresh the shared decision."
        : $"Repair: ask {Blocker.Owner} to resolve this contributor and its causing records through the owning workflow, then select Evaluate close to refresh the shared decision.";
}
