using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

internal sealed record FundReconciliationReadPresentation(
    string StatusText,
    string EmptyStateText,
    string BreakQueueEmptyStateText,
    WorkstationStateModel State,
    bool ShowState,
    string OpenBreaksText,
    string ReconciliationRunsText,
    string SecurityIssuesText,
    string InReviewBreaksText);

public sealed partial class FundLedgerViewModel
{
    private static string BuildOverviewStatus(
        FundWorkspaceSummary summary,
        FundReconciliationWorkbenchSnapshot reconciliationSnapshot,
        bool isEmptyWorkspace)
    {
        var reconciliationClause = BuildReconciliationOverviewClause(reconciliationSnapshot);
        var status = isEmptyWorkspace
            ? $"The Accounting shell is ready. Link accounts or import positions to populate fund operations; {reconciliationClause}."
            : $"{summary.FundDisplayName} is loaded with {summary.TotalAccounts} account(s), {summary.JournalEntryCount} journal entries, and {reconciliationClause}.";

        if (summary.SecurityMissingCount > 0)
        {
            status += $" {summary.SecurityMissingCount} unresolved security mapping(s) still need Security Master coverage.";
        }

        if (summary.SecurityCoverageIssues > 0
            && reconciliationSnapshot.ReadAvailability != FundReconciliationReadAvailability.Unavailable)
        {
            var qualifier = reconciliationSnapshot.ReadAvailability == FundReconciliationReadAvailability.Degraded
                ? "At least "
                : string.Empty;
            status += $" {qualifier}{summary.SecurityCoverageIssues} reconciliation security coverage issue(s) remain open.";
        }

        return status;
    }

    internal static FundReconciliationReadPresentation BuildReconciliationReadPresentation(
        FundReconciliationWorkbenchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var openBreaksText = snapshot.ReadAvailability switch
        {
            FundReconciliationReadAvailability.Unavailable => "-",
            FundReconciliationReadAvailability.Degraded => $"{snapshot.Summary.OpenBreakCount:N0}+",
            _ => snapshot.Summary.OpenBreakCount.ToString("N0")
        };
        var reconciliationRunsText = snapshot.ReadAvailability switch
        {
            FundReconciliationReadAvailability.Unavailable => $"0/{snapshot.KnownRunCount:N0} loaded",
            FundReconciliationReadAvailability.Degraded => $"{snapshot.Summary.RunCount:N0}/{snapshot.KnownRunCount:N0} loaded",
            _ => snapshot.Summary.RunCount.ToString("N0")
        };
        var securityIssuesText = snapshot.ReadAvailability switch
        {
            FundReconciliationReadAvailability.Unavailable => "-",
            FundReconciliationReadAvailability.Degraded => $"{snapshot.Summary.SecurityCoverageIssueCount:N0}+",
            _ => snapshot.Summary.SecurityCoverageIssueCount.ToString("N0")
        };
        var inReviewBreaksText = snapshot.BreakQueueReadAvailable
            ? snapshot.InReviewBreakCount.ToString("N0")
            : "-";
        var breakQueueEmptyStateText = snapshot.BreakQueueReadAvailable
            ? "No strategy-run breaks are queued for this fund."
            : "Break queue data is unavailable. Refresh after the workstation API recovers; do not treat this empty list as a verified zero-break queue.";

        FundReconciliationReadPresentation Present(
            string statusText,
            string emptyStateText,
            WorkstationStateModel state,
            bool showState) => new(
                statusText,
                emptyStateText,
                breakQueueEmptyStateText,
                state,
                showState,
                openBreaksText,
                reconciliationRunsText,
                securityIssuesText,
                inReviewBreaksText);

        if (snapshot.ReadAvailability == FundReconciliationReadAvailability.Unavailable)
        {
            var detail = $"Detail reads failed for all {snapshot.KnownRunCount:N0} known strategy run(s). " +
                         "Reconciliation absence and break posture cannot be verified until the workstation API recovers.";
            return Present(
                statusText: $"Reconciliation data is unavailable. {detail}",
                emptyStateText: "Reconciliation run details are unavailable. Refresh after the workstation API recovers; " +
                                "do not treat the empty list as verified absence.",
                state: WorkstationStateModel.Error(
                    "Reconciliation service unavailable",
                    detail,
                    "Refresh reconciliation",
                    "Reconciliation"),
                showState: true);
        }

        if (snapshot.ReadAvailability == FundReconciliationReadAvailability.Degraded
            || !snapshot.BreakQueueReadAvailable
            || !snapshot.CalibrationReadAvailable)
        {
            var unavailableParts = new List<string>();
            if (snapshot.UnavailableRunCount > 0)
            {
                unavailableParts.Add($"{snapshot.UnavailableRunCount:N0} run detail read(s) failed");
            }

            if (!snapshot.BreakQueueReadAvailable)
            {
                unavailableParts.Add("the break queue read failed");
            }

            if (!snapshot.CalibrationReadAvailable)
            {
                unavailableParts.Add("the calibration read failed");
            }

            var detail = $"Loaded {snapshot.Summary.RunCount:N0} of {snapshot.KnownRunCount:N0} known run reconciliation record(s); " +
                         $"{string.Join(", ", unavailableParts)}. " +
                         $"{snapshot.MissingRunCount:N0} run(s) had no reconciliation record.";
            return Present(
                statusText: $"Reconciliation data is degraded. {detail}",
                emptyStateText: "Reconciliation results are incomplete because one or more workstation reads failed. " +
                                "Refresh before relying on the current list or calibration posture.",
                state: WorkstationStateModel.Recovery(
                    "Reconciliation data is degraded",
                    detail,
                    new WorkstationActionPostureModel(
                        "Refresh reconciliation",
                        "Retry unavailable detail reads before relying on break or sign-off posture.",
                        "Reconciliation",
                        "Accounting operator",
                        WorkstationReadinessTone.Neutral,
                        WorkspaceTone.Warning)),
                showState: true);
        }

        if (snapshot.MissingRunCount > 0)
        {
            var detail = $"{snapshot.MissingRunCount:N0} known strategy run(s) do not yet have a reconciliation record; " +
                         $"{snapshot.Summary.RunCount:N0} reconciliation record(s) loaded successfully.";
            return Present(
                statusText: detail,
                emptyStateText: $"{snapshot.MissingRunCount:N0} known strategy run(s) do not yet have a reconciliation record.",
                state: WorkstationStateModel.Empty(
                    "Reconciliation details not recorded",
                    detail,
                    "Review strategy runs",
                    "Strategy"),
                showState: true);
        }

        if (snapshot.KnownRunCount == 0)
        {
            return Present(
                statusText: "No strategy runs are recorded for this fund yet.",
                emptyStateText: "No reconciliation runs are available for this fund.",
                state: WorkstationStateModel.Empty(
                    "No strategy runs recorded",
                    "Record a fund-scoped strategy run before reconciliation detail can be produced."),
                showState: false);
        }

        return Present(
            statusText: $"{snapshot.BreakQueueItems.Count:N0} break queue item(s) and {snapshot.RunRows.Count:N0} run(s) are ready for review.",
            emptyStateText: "No reconciliation runs are available for this fund.",
            state: WorkstationStateModel.Ready(
                "Reconciliation data available",
                $"All {snapshot.KnownRunCount:N0} known strategy run detail read(s) completed."),
            showState: false);
    }

    internal static string BuildReconciliationOverviewClause(
        FundReconciliationWorkbenchSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var parts = new List<string>
        {
            snapshot.ReadAvailability switch
            {
                FundReconciliationReadAvailability.Unavailable =>
                    $"reconciliation run counts unavailable for {snapshot.KnownRunCount:N0} known strategy run(s)",
                FundReconciliationReadAvailability.Degraded =>
                    $"{snapshot.Summary.RunCount:N0} of {snapshot.KnownRunCount:N0} known reconciliation run(s) loaded",
                _ => $"{snapshot.Summary.RunCount:N0} reconciliation run(s)"
            }
        };

        if (!snapshot.BreakQueueReadAvailable)
        {
            parts.Add("break queue unavailable");
        }

        if (!snapshot.CalibrationReadAvailable)
        {
            parts.Add("calibration posture unavailable");
        }

        return string.Join("; ", parts);
    }
}
