using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed partial class FundLedgerViewModel
{
    private StatementReconciliationWorkbenchSnapshot? _statementSnapshot;
    private bool _isStatementReconciliationLoading;

    public ObservableCollection<StatementRunWorkbenchRow> StatementRuns => ReconciliationSection.StatementRuns;

    public ObservableCollection<StatementValidationIssueRow> StatementValidationIssues => ReconciliationSection.StatementValidationIssues;

    public ObservableCollection<StatementUnresolvedBreakRow> StatementUnresolvedBreaks => ReconciliationSection.StatementUnresolvedBreaks;

    public ObservableCollection<StatementCaseActionRow> StatementCaseActions => ReconciliationSection.StatementCaseActions;

    public IAsyncRelayCommand RefreshStatementReconciliationCommand { get; private set; } = null!;

    public StatementRunWorkbenchRow? SelectedStatementRun
    {
        get => ReconciliationSection.SelectedStatementRun;
        set
        {
            if (SetReconciliationSectionProperty(ReconciliationSection.SelectedStatementRun, item => ReconciliationSection.SelectedStatementRun = item, value))
            {
                ApplySelectedStatementRunDetail();
            }
        }
    }

    public StatementUnresolvedBreakRow? SelectedStatementBreak
    {
        get => ReconciliationSection.SelectedStatementBreak;
        set
        {
            if (SetReconciliationSectionProperty(ReconciliationSection.SelectedStatementBreak, item => ReconciliationSection.SelectedStatementBreak = item, value))
            {
                ApplySelectedStatementBreakDetail();
            }
        }
    }

    public string StatementStatusText
    {
        get => ReconciliationSection.StatementStatusText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementStatusText, text => ReconciliationSection.StatementStatusText = text, value);
    }

    public string StatementRunsText
    {
        get => ReconciliationSection.StatementRunsText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementRunsText, text => ReconciliationSection.StatementRunsText = text, value);
    }

    public string StatementValidationIssuesText
    {
        get => ReconciliationSection.StatementValidationIssuesText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementValidationIssuesText, text => ReconciliationSection.StatementValidationIssuesText = text, value);
    }

    public string StatementUnresolvedBreaksText
    {
        get => ReconciliationSection.StatementUnresolvedBreaksText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementUnresolvedBreaksText, text => ReconciliationSection.StatementUnresolvedBreaksText = text, value);
    }

    public string StatementCasesText
    {
        get => ReconciliationSection.StatementCasesText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementCasesText, text => ReconciliationSection.StatementCasesText = text, value);
    }

    public string StatementLastRefreshText
    {
        get => ReconciliationSection.StatementLastRefreshText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementLastRefreshText, text => ReconciliationSection.StatementLastRefreshText = text, value);
    }

    public string StatementDetailTitle
    {
        get => ReconciliationSection.StatementDetailTitle;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementDetailTitle, text => ReconciliationSection.StatementDetailTitle = text, value);
    }

    public string StatementDetailSubtitle
    {
        get => ReconciliationSection.StatementDetailSubtitle;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementDetailSubtitle, text => ReconciliationSection.StatementDetailSubtitle = text, value);
    }

    public string StatementDisabledReasonText
    {
        get => ReconciliationSection.StatementDisabledReasonText;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementDisabledReasonText, text => ReconciliationSection.StatementDisabledReasonText = text, value);
    }

    public WorkstationStateModel StatementSignifierState
    {
        get => ReconciliationSection.StatementSignifierState;
        private set => SetReconciliationSectionProperty(ReconciliationSection.StatementSignifierState, state => ReconciliationSection.StatementSignifierState = state, value);
    }

    public bool IsStatementReconciliationLoading
    {
        get => _isStatementReconciliationLoading;
        private set => SetProperty(ref _isStatementReconciliationLoading, value);
    }

    private async Task LoadStatementReconciliationAsync(CancellationToken ct = default)
    {
        await RefreshStatementReconciliationAsync(ct);
    }

    public async Task RefreshStatementReconciliationAsync(CancellationToken ct = default)
    {
        IsStatementReconciliationLoading = true;
        StatementStatusText = "Refreshing statement reconciliation evidence from shared workstation endpoints...";

        try
        {
            _statementSnapshot = await _statementReconciliationWorkbenchService.GetSnapshotAsync(ct);
            ApplyStatementSnapshot(_statementSnapshot);
        }
        catch (OperationCanceledException)
        {
            StatementStatusText = "Statement reconciliation refresh was cancelled.";
        }
        catch (Exception ex)
        {
            StatementStatusText = $"Unable to load statement reconciliation evidence: {ex.Message}";
            ResetStatementReconciliationState();
        }
        finally
        {
            IsStatementReconciliationLoading = false;
        }
    }

    private void ApplyStatementSnapshot(StatementReconciliationWorkbenchSnapshot snapshot)
    {
        var previousRunId = SelectedStatementRun?.RunId;
        var previousBreakId = SelectedStatementBreak?.BreakId;
        var runRows = snapshot.StatementRuns
            .OrderByDescending(static run => run.CompletedAtUtc ?? run.StartedAtUtc)
            .Select(MapStatementRun)
            .ToArray();
        var issueRows = snapshot.ValidationIssues
            .OrderByDescending(static issue => ParseDateTimeOffset(issue.CreatedAtUtc) ?? DateTimeOffset.MinValue)
            .Select(MapValidationIssue)
            .ToArray();
        var breakRows = snapshot.UnresolvedBreaks
            .OrderByDescending(static item => item.LastObservedAtUtc ?? item.CreatedAtUtc ?? DateTimeOffset.MinValue)
            .Select(MapUnresolvedBreak)
            .ToArray();

        SynchronizeCollection(StatementRuns, runRows);
        SynchronizeCollection(StatementValidationIssues, issueRows);
        SynchronizeCollection(StatementUnresolvedBreaks, breakRows);

        StatementRunsText = runRows.Length.ToString("N0");
        StatementValidationIssuesText = issueRows.Length.ToString("N0");
        StatementUnresolvedBreaksText = breakRows.Length.ToString("N0");
        StatementCasesText = snapshot.OpenCases.Count.ToString("N0");
        StatementLastRefreshText = snapshot.RefreshedAt.LocalDateTime.ToString("g");
        StatementStatusText = runRows.Length == 0 && issueRows.Length == 0 && breakRows.Length == 0
            ? "No statement reconciliation evidence is currently exposed by the shared workstation endpoints."
            : $"Loaded {runRows.Length} statement run(s), {issueRows.Length} validation issue(s), and {breakRows.Length} unresolved statement break(s).";

        SelectedStatementRun = runRows.FirstOrDefault(row => string.Equals(row.RunId, previousRunId, StringComparison.OrdinalIgnoreCase))
            ?? runRows.FirstOrDefault();
        SelectedStatementBreak = breakRows.FirstOrDefault(row => string.Equals(row.BreakId, previousBreakId, StringComparison.OrdinalIgnoreCase))
            ?? breakRows.FirstOrDefault();

        if (SelectedStatementRun is null && SelectedStatementBreak is null)
        {
            ApplyEmptyStatementDetail();
        }
    }

    private void ApplySelectedStatementRunDetail()
    {
        if (SelectedStatementRun is null)
        {
            ApplyEmptyStatementDetail();
            return;
        }

        StatementDetailTitle = $"Statement run {SelectedStatementRun.RunId}";
        StatementDetailSubtitle = $"Import {SelectedStatementRun.ImportId} · {SelectedStatementRun.StatusText} · {SelectedStatementRun.OpenExceptionCount:N0} open exception(s).";
        if (SelectedStatementBreak is null)
        {
            StatementDisabledReasonText = "Select an unresolved statement break to enable case-action recovery links.";
            StatementCaseActions.Clear();
            StatementSignifierState = WorkstationStateModel.Empty(
                "Statement run selected",
                StatementDetailSubtitle,
                "Select break",
                SelectedStatementRun.RunId);
        }
    }

    private void ApplySelectedStatementBreakDetail()
    {
        if (SelectedStatementBreak is null)
        {
            ApplySelectedStatementRunDetail();
            return;
        }

        StatementDetailTitle = $"Break {SelectedStatementBreak.BreakId}";
        StatementDetailSubtitle = $"{SelectedStatementBreak.BreakTypeLabel} · {SelectedStatementBreak.StatusLabel} · {SelectedStatementBreak.DeltaText} · {SelectedStatementBreak.SlaLabel}.";
        StatementDisabledReasonText = string.IsNullOrWhiteSpace(SelectedStatementBreak.OwnerLabel) || SelectedStatementBreak.OwnerLabel == "Unassigned"
            ? "Assign an owner before close sign-off; evidence and comment actions remain available."
            : "Case actions are available from the shared reconciliation break endpoints.";
        var actions = BuildCaseActionRows(SelectedStatementBreak).ToArray();
        SynchronizeCollection(StatementCaseActions, actions);
        StatementSignifierState = BuildStatementBreakSignifier(SelectedStatementBreak, actions);
    }

    private void ApplyEmptyStatementDetail()
    {
        StatementDetailTitle = "Select a statement run";
        StatementDetailSubtitle = "Statement run detail, validation issues, unresolved breaks, and case actions appear after refresh.";
        StatementDisabledReasonText = "Select an unresolved statement break to inspect case actions.";
        StatementCaseActions.Clear();
        StatementSignifierState = WorkstationStateModel.Empty(
            "Select statement evidence",
            "Choose a statement run or unresolved break to see evidence links, recovery actions, readiness tone, and sign-off posture.",
            "Select statement",
            "Statement Reconciliation");
    }

    private void ResetStatementReconciliationState()
    {
        _statementSnapshot = null;
        StatementRuns.Clear();
        StatementValidationIssues.Clear();
        StatementUnresolvedBreaks.Clear();
        StatementCaseActions.Clear();
        ReconciliationSection.SelectedStatementRun = null;
        ReconciliationSection.SelectedStatementBreak = null;
        StatementRunsText = "0";
        StatementValidationIssuesText = "0";
        StatementUnresolvedBreaksText = "0";
        StatementCasesText = "0";
        StatementLastRefreshText = "-";
        StatementStatusText = "Statement reconciliation evidence has not been loaded yet.";
        ApplyEmptyStatementDetail();
    }

    private static StatementRunWorkbenchRow MapStatementRun(StatementRunSummaryDto run)
    {
        var open = run.OpenExceptionCount;
        return new StatementRunWorkbenchRow(
            run.RunId,
            run.ImportId,
            FormatDateTimeText(run.StartedAtUtc),
            FormatDateTimeText(run.CompletedAtUtc),
            run.PositionMatches,
            run.CashMatches,
            run.TransactionMatches,
            open,
            open > 0 ? "Review required" : "Matched",
            open > 0 ? WorkstationReadinessTone.Blocked : WorkstationReadinessTone.Ready,
            open > 0 ? WorkspaceTone.Danger : WorkspaceTone.Success);
    }

    private static StatementValidationIssueRow MapValidationIssue(StatementRunExceptionDto issue)
        => new(
            issue.BreakId,
            issue.RunId,
            issue.ImportId,
            issue.SourceReference,
            issue.ToleranceBreached ? "Error" : "Warning",
            issue.Category,
            issue.BreakCode,
            issue.Delta.ToString("N2"),
            issue.Tolerance.ToString("N2"),
            issue.Status,
            FormatDateTimeText(issue.CreatedAtUtc),
            issue.ToleranceBreached ? "Resolve before sign-off" : "Review variance evidence",
            $"/api/workstation/reconciliation/statement-exceptions#{Uri.EscapeDataString(issue.BreakId)}");

    private static StatementUnresolvedBreakRow MapUnresolvedBreak(StatementBreakDto item)
    {
        var status = string.IsNullOrWhiteSpace(item.Status) ? "Open" : item.Status!;
        var owner = string.IsNullOrWhiteSpace(item.Owner) ? "Unassigned" : item.Owner!;
        var blocked = !status.Contains("resolved", StringComparison.OrdinalIgnoreCase) && !status.Contains("dismissed", StringComparison.OrdinalIgnoreCase);
        return new StatementUnresolvedBreakRow(
            item.BreakId ?? "unknown-break",
            item.InternalReference ?? "unknown-run",
            item.StatementReference ?? "-",
            item.BreakType?.ToString() ?? "Unknown",
            item.Severity?.ToString() ?? "Warning",
            item.MatchTier?.ToString() ?? "Manual",
            FormatNullableCurrency(item.Delta, item.Currency),
            FormatNullableCurrency(item.Tolerance, item.Currency),
            status,
            owner,
            FormatDateTimeText(item.LastObservedAtUtc ?? item.CreatedAtUtc),
            string.IsNullOrWhiteSpace(item.RecommendedAction) ? "Review and resolve" : item.RecommendedAction!,
            string.IsNullOrWhiteSpace(item.EvidenceLink) ? $"/api/workstation/reconciliation/statement-breaks#{Uri.EscapeDataString(item.BreakId ?? string.Empty)}" : item.EvidenceLink!,
            BuildStatementBreakSlaLabel(item),
            string.IsNullOrWhiteSpace(item.EscalationLabel) ? "Escalation pending" : item.EscalationLabel!,
            string.IsNullOrWhiteSpace(item.EscalationReason) ? "Assign ownership and retain resolution evidence before close sign-off." : item.EscalationReason!,
            blocked ? WorkstationReadinessTone.Blocked : WorkstationReadinessTone.EvidenceLinked,
            blocked ? WorkspaceTone.Danger : WorkspaceTone.Success);
    }

    private static IEnumerable<StatementCaseActionRow> BuildCaseActionRows(StatementUnresolvedBreakRow row)
    {
        var target = $"/api/workstation/reconciliation/break-queue/{Uri.EscapeDataString(row.BreakId)}";
        var needsOwner = row.OwnerLabel == "Unassigned";
        yield return new StatementCaseActionRow("assign", "Assign owner", $"{target}/assign", row.EvidenceLink, row.OwnerLabel, string.Empty, true, WorkstationReadinessTone.Warning, WorkspaceTone.Warning);
        yield return new StatementCaseActionRow("comment", "Add evidence comment", $"{target}/comments", row.EvidenceLink, row.OwnerLabel, string.Empty, true, WorkstationReadinessTone.EvidenceLinked, WorkspaceTone.Info);
        yield return new StatementCaseActionRow("resolve", "Resolve break", $"{target}/resolve", row.EvidenceLink, row.OwnerLabel, needsOwner ? "Assign an owner before resolving the case." : string.Empty, !needsOwner, needsOwner ? WorkstationReadinessTone.Blocked : WorkstationReadinessTone.Warning, needsOwner ? WorkspaceTone.Danger : WorkspaceTone.Warning);
    }

    private WorkstationStateModel BuildStatementBreakSignifier(StatementUnresolvedBreakRow row, IReadOnlyList<StatementCaseActionRow> actions)
    {
        var primaryAction = actions.FirstOrDefault(static action => action.IsEnabled) ?? actions.First();
        var actionPosture = new WorkstationActionPostureModel(
            primaryAction.Label,
            string.IsNullOrWhiteSpace(primaryAction.DisabledReason) ? row.RecommendedAction : primaryAction.DisabledReason,
            primaryAction.TargetRoute,
            row.OwnerLabel,
            row.ReadinessTone,
            row.Tone);

        return new WorkstationStateModel(
            row.Tone == WorkspaceTone.Success ? WorkstationStateKind.Ready : WorkstationStateKind.Blocked,
            $"{row.StatusLabel} statement break",
            StatementDetailSubtitle,
            primaryAction.Label,
            primaryAction.TargetRoute,
            row.EvidenceLink,
            row.Tone == WorkspaceTone.Success ? "\uE73E" : "\uE783",
            row.Tone,
            row.ReadinessTone,
            actionPosture,
            EvidenceLinks:
            [
                new WorkstationEvidenceLinkModel("Statement break evidence", row.EvidenceLink, "shared workstation endpoint", row.LastObservedText),
                new WorkstationEvidenceLinkModel("Statement break escalation", row.EvidenceLink, row.SlaLabel, row.EscalationLabel),
                new WorkstationEvidenceLinkModel("Case actions", primaryAction.TargetRoute, "reconciliation casework", row.OwnerLabel)
            ],
            RecoveryActions: actions.Select(action => new WorkstationRecoveryActionModel(action.Label, string.IsNullOrWhiteSpace(action.DisabledReason) ? row.RecommendedAction : action.DisabledReason, action.TargetRoute)).ToArray(),
            SignoffRequirement: new WorkstationSignoffRequirementModel("Fund operations reviewer", row.OwnerLabel == "Unassigned" ? "Owner required" : row.EscalationLabel, row.EscalationReason));
    }

    private static string BuildStatementBreakSlaLabel(StatementBreakDto item)
    {
        var state = string.IsNullOrWhiteSpace(item.SlaState) ? "NotStarted" : item.SlaState!;
        if (item.SlaBreachedAtUtc.HasValue)
        {
            return $"{FormatStatementSlaState(state)} · breached {FormatDateTimeText(item.SlaBreachedAtUtc.Value)}";
        }

        if (item.SlaDueAtUtc.HasValue)
        {
            return $"{FormatStatementSlaState(state)} · due {FormatDateTimeText(item.SlaDueAtUtc.Value)}";
        }

        if (item.SlaWarningAtUtc.HasValue)
        {
            return $"{FormatStatementSlaState(state)} · warning {FormatDateTimeText(item.SlaWarningAtUtc.Value)}";
        }

        return FormatStatementSlaState(state);
    }

    private static string FormatStatementSlaState(string state)
        => state switch
        {
            "NotStarted" => "Not started",
            "OnTrack" => "On track",
            "Warning" => "Warning",
            "Breached" => "Breached",
            "Stopped" => "Stopped",
            _ => state
        };

    private static string FormatDateTimeText(string? value)
        => FormatDateTimeText(ParseDateTimeOffset(value));

    private static string FormatDateTimeText(DateTimeOffset? value)
        => value?.LocalDateTime.ToString("g") ?? "-";

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string FormatNullableCurrency(decimal? value, string? currency)
        => value is null ? "-" : string.IsNullOrWhiteSpace(currency) ? value.Value.ToString("N2") : $"{currency} {value.Value:N2}";
}
