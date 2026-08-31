using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Workstation.Models;

public sealed record OperationsContinuityWorkflowRowModel(
    Guid WorkflowId,
    string Label,
    string StatusText,
    string GatesText,
    string UpdatedText,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public sealed record OperationsContinuityPanelRowModel(
    string Id,
    string Label,
    string Value,
    string Detail,
    string Meta,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public sealed record OperationsContinuityNextActionModel(
    string Label,
    string StatusText,
    string RouteText,
    string? DisabledReason,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public sealed record OperationsContinuityQueueRollupModel(
    string StatusLabel,
    int BlockedCount,
    int ReviewCount,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

/// <summary>
/// Projects the server-owned operations continuity contracts into desktop rows using the same
/// derivations as the browser continuity screen: workflow/gate/severity/checklist/break status
/// tone maps, the gate-priority next-action pick, and the open-item operator queue (an item is
/// open when its tone is not ready). All gate and severity truth stays server-side.
/// </summary>
public static class OperationsContinuityMapper
{
    public static WorkstationReadinessTone ToTone(OperationsWorkflowStatusDto status)
        => status switch
        {
            OperationsWorkflowStatusDto.Closed or OperationsWorkflowStatusDto.ReadyForClose
                => WorkstationReadinessTone.EvidenceLinked,
            OperationsWorkflowStatusDto.Blocked => WorkstationReadinessTone.Blocked,
            OperationsWorkflowStatusDto.ApprovalPending
                or OperationsWorkflowStatusDto.ReconciliationActive
                or OperationsWorkflowStatusDto.LedgerPostingDraft
                or OperationsWorkflowStatusDto.SecurityMasterValidation
                or OperationsWorkflowStatusDto.CollectingBrokerData
                => WorkstationReadinessTone.SignoffRequired,
            _ => WorkstationReadinessTone.Neutral
        };

    public static WorkstationReadinessTone ToTone(OperationsGateStatusDto status)
        => status switch
        {
            OperationsGateStatusDto.Passed => WorkstationReadinessTone.EvidenceLinked,
            OperationsGateStatusDto.Blocked => WorkstationReadinessTone.Blocked,
            OperationsGateStatusDto.ReviewRequired or OperationsGateStatusDto.InProgress
                => WorkstationReadinessTone.SignoffRequired,
            _ => WorkstationReadinessTone.Neutral
        };

    public static WorkstationReadinessTone SeverityTone(string? severity)
        => severity?.Trim().ToLowerInvariant() switch
        {
            "critical" or "error" => WorkstationReadinessTone.Blocked,
            "warning" or "warn" => WorkstationReadinessTone.SignoffRequired,
            _ => WorkstationReadinessTone.Neutral
        };

    public static WorkstationReadinessTone ChecklistTone(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "done" or "complete" or "completed" or "acknowledged" => WorkstationReadinessTone.EvidenceLinked,
            "blocked" or "expired" => WorkstationReadinessTone.Blocked,
            "review" or "reviewrequired" or "pending" or "open" => WorkstationReadinessTone.SignoffRequired,
            _ => WorkstationReadinessTone.Neutral
        };

    public static WorkstationReadinessTone BreakTone(string? status)
        => status?.Trim().ToLowerInvariant() switch
        {
            "resolved" or "dismissed" or "matched" => WorkstationReadinessTone.EvidenceLinked,
            "open" or "inreview" or "reviewrequired" => WorkstationReadinessTone.SignoffRequired,
            "blocked" => WorkstationReadinessTone.Blocked,
            _ => WorkstationReadinessTone.Neutral
        };

    public static WorkstationReadinessTone LaneTone(OperationsReconciliationLaneStatusDto status)
        => status switch
        {
            OperationsReconciliationLaneStatusDto.Ready => WorkstationReadinessTone.EvidenceLinked,
            OperationsReconciliationLaneStatusDto.Blocked or OperationsReconciliationLaneStatusDto.Missing
                => WorkstationReadinessTone.Blocked,
            OperationsReconciliationLaneStatusDto.ReviewRequired => WorkstationReadinessTone.SignoffRequired,
            _ => WorkstationReadinessTone.Neutral
        };

    public static IReadOnlyList<OperationsContinuityWorkflowRowModel> BuildWorkflowRows(
        IReadOnlyList<OperationsContinuityWorkflowSummaryDto> workflows)
    {
        ArgumentNullException.ThrowIfNull(workflows);

        return workflows
            .OrderByDescending(static workflow => workflow.UpdatedAtUtc)
            .Select(static workflow =>
            {
                var tone = ToTone(workflow.Status);
                var passedGates = workflow.Gates.Count(static gate => gate.Status == OperationsGateStatusDto.Passed);
                var blockerCount = workflow.Gates.Sum(static gate => gate.Blockers.Count);
                return new OperationsContinuityWorkflowRowModel(
                    workflow.WorkflowId,
                    $"{workflow.PeriodId} · {workflow.BrokerSource}",
                    SettingsViewModel.FormatIdentifier(workflow.Status.ToString()),
                    $"{passedGates}/{workflow.Gates.Count} gates passed · {Pluralize(blockerCount, "blocker")}",
                    FormatTimestamp(workflow.UpdatedAtUtc),
                    tone,
                    ToWorkspaceTone(tone));
            })
            .ToArray();
    }

    public static IReadOnlyList<OperationsContinuityPanelRowModel> BuildGateRows(OperationsContinuityWorkflowDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return detail.Gates
            .Select(static gate =>
            {
                var tone = ToTone(gate.Status);
                return new OperationsContinuityPanelRowModel(
                    gate.GateKey.ToString(),
                    gate.DisplayName,
                    SettingsViewModel.FormatIdentifier(gate.Status.ToString()),
                    gate.Description,
                    $"{Pluralize(gate.Blockers.Count, "blocker")}{(gate.CompletedBy is null ? string.Empty : $" · Completed by {gate.CompletedBy}")}",
                    tone,
                    ToWorkspaceTone(tone));
            })
            .ToArray();
    }

    public static IReadOnlyList<OperationsContinuityPanelRowModel> BuildBlockerRows(OperationsContinuityWorkflowDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return detail.Blockers
            .Select(static blocker =>
            {
                var tone = SeverityTone(blocker.Severity);
                return new OperationsContinuityPanelRowModel(
                    blocker.Code,
                    blocker.Code,
                    SettingsViewModel.FormatIdentifier(blocker.Severity),
                    blocker.Message,
                    blocker.Gate is null ? "No gate" : $"Gate {blocker.Gate}",
                    tone,
                    ToWorkspaceTone(tone));
            })
            .ToArray();
    }

    public static IReadOnlyList<OperationsContinuityPanelRowModel> BuildChecklistRows(OperationsContinuityWorkflowDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return detail.CloseChecklist
            .Select(static task =>
            {
                var tone = IsChecklistTaskBlocked(task)
                    ? WorkstationReadinessTone.Blocked
                    : IsChecklistTaskReady(task)
                        ? WorkstationReadinessTone.EvidenceLinked
                        : ChecklistTone(task.Status);
                return new OperationsContinuityPanelRowModel(
                    task.TaskId,
                    task.Label,
                    SettingsViewModel.FormatIdentifier(task.Status),
                    task.BlockingReason ?? $"Owner {task.Owner}",
                    task.DueDate is null
                        ? $"Gate {task.Gate}"
                        : $"Gate {task.Gate} · Due {task.DueDate.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}",
                    tone,
                    ToWorkspaceTone(tone));
            })
            .ToArray();
    }

    /// <summary>
    /// The unified operator queue: breaks, reconciliation lanes, blockers, checklist tasks, and
    /// close-calendar rows, keeping only open items (tone is not ready) like the browser queue.
    /// </summary>
    public static IReadOnlyList<OperationsContinuityPanelRowModel> BuildQueueRows(
        OperationsContinuityWorkflowDto? detail,
        IReadOnlyList<OperationsContinuityPanelRowModel> closeCalendarRows)
    {
        ArgumentNullException.ThrowIfNull(closeCalendarRows);

        var rows = new List<OperationsContinuityPanelRowModel>();
        if (detail is not null)
        {
            rows.AddRange(detail.BreakCases.Select(static breakCase =>
            {
                var tone = BreakTone(breakCase.Status);
                return new OperationsContinuityPanelRowModel(
                    $"break:{breakCase.BreakId}",
                    $"Break {breakCase.BreakId}",
                    SettingsViewModel.FormatIdentifier(breakCase.Status),
                    breakCase.SuggestedAction ?? breakCase.Category,
                    $"{SettingsViewModel.FormatIdentifier(breakCase.Severity)}{(breakCase.Owner is null ? string.Empty : $" · {breakCase.Owner}")}",
                    tone,
                    ToWorkspaceTone(tone));
            }));

            rows.AddRange((detail.ReconciliationLanes ?? []).Select(static lane =>
            {
                var tone = LaneTone(lane.Status);
                return new OperationsContinuityPanelRowModel(
                    $"lane:{lane.LaneId}",
                    lane.Label,
                    SettingsViewModel.FormatIdentifier(lane.Status.ToString()),
                    lane.Summary,
                    Pluralize(lane.BreakCount, "break"),
                    tone,
                    ToWorkspaceTone(tone));
            }));

            rows.AddRange(BuildBlockerRows(detail).Select(static row => row with { Id = $"blocker:{row.Id}" }));
            rows.AddRange(BuildChecklistRows(detail).Select(static row => row with { Id = $"task:{row.Id}" }));
        }

        rows.AddRange(closeCalendarRows.Select(static row => row with { Id = $"calendar:{row.Id}" }));

        return rows
            .Where(static row => row.ReadinessTone != WorkstationReadinessTone.EvidenceLinked)
            .ToArray();
    }

    public static OperationsContinuityQueueRollupModel BuildQueueRollup(
        bool isLoading,
        IReadOnlyList<OperationsContinuityPanelRowModel> queueRows)
    {
        ArgumentNullException.ThrowIfNull(queueRows);

        if (isLoading)
        {
            return new OperationsContinuityQueueRollupModel(
                "Loading", 0, 0, WorkstationReadinessTone.Neutral, WorkspaceTone.Neutral);
        }

        var blockedCount = queueRows.Count(static row => row.ReadinessTone == WorkstationReadinessTone.Blocked);
        var reviewCount = queueRows.Count - blockedCount;
        var tone = blockedCount > 0
            ? WorkstationReadinessTone.Blocked
            : queueRows.Count > 0
                ? WorkstationReadinessTone.SignoffRequired
                : WorkstationReadinessTone.EvidenceLinked;
        var label = blockedCount > 0 ? "Blocked" : queueRows.Count > 0 ? "Review" : "Clear";
        return new OperationsContinuityQueueRollupModel(label, blockedCount, reviewCount, tone, ToWorkspaceTone(tone));
    }

    /// <summary>
    /// Picks the server-recommended next action: all workflow and gate next actions ordered by the
    /// owning gate's status priority (blocked outranks review outranks in-progress), first entry
    /// with a non-blank label — the browser console's exact rule.
    /// </summary>
    public static OperationsContinuityNextActionModel ResolveNextAction(
        OperationsContinuityWorkflowDto? detail,
        bool isLoading,
        string? detailError)
    {
        if (isLoading)
        {
            return Disabled("Wait for the selected workflow to finish loading before acting.");
        }

        if (!string.IsNullOrWhiteSpace(detailError))
        {
            return Disabled("Resolve the workflow detail error before acting on a next action.");
        }

        if (detail is null)
        {
            return Disabled("Start or load an operations continuity workflow to receive a next action.");
        }

        var gateStatusByKey = detail.Gates.ToDictionary(static gate => gate.GateKey, static gate => gate.Status);
        var candidates = detail.NextActions
            .Select(action => (Action: action, Priority: GatePriority(action.Gate, gateStatusByKey)))
            .Concat(detail.Gates.SelectMany(gate => gate.NextActions.Select(action =>
                (Action: action, Priority: GatePriority(gate.GateKey, gateStatusByKey)))))
            .OrderByDescending(static candidate => candidate.Priority)
            .Select(static candidate => candidate.Action)
            .FirstOrDefault(static action => !string.IsNullOrWhiteSpace(action.Label));

        if (candidates is null)
        {
            return Disabled(detail.Status == OperationsWorkflowStatusDto.Closed
                ? "This workflow is closed and locked; use the governed reopen command to make changes."
                : "No server-recommended next action is available for this workflow.");
        }

        var owningGateStatus = candidates.Gate is not null && gateStatusByKey.TryGetValue(candidates.Gate.Value, out var status)
            ? status
            : (OperationsGateStatusDto?)null;
        var tone = owningGateStatus is not null ? ToTone(owningGateStatus.Value) : ToTone(detail.Status);
        var statusText = owningGateStatus switch
        {
            OperationsGateStatusDto.Blocked => "Blocked",
            OperationsGateStatusDto.ReviewRequired => "Review required",
            OperationsGateStatusDto.InProgress => "In progress",
            OperationsGateStatusDto.NotStarted => "Pending",
            OperationsGateStatusDto.Passed => "Ready",
            _ => SettingsViewModel.FormatIdentifier(detail.Status.ToString())
        };
        return new OperationsContinuityNextActionModel(
            candidates.Label,
            statusText,
            candidates.Route ?? candidates.RouteHint ?? "No route provided",
            DisabledReason: string.IsNullOrWhiteSpace(candidates.Route ?? candidates.RouteHint)
                ? "The server did not provide a local workstation route for this action."
                : null,
            tone,
            ToWorkspaceTone(tone));

        static OperationsContinuityNextActionModel Disabled(string reason)
            => new(
                "No next action",
                "Unavailable",
                "-",
                reason,
                WorkstationReadinessTone.Neutral,
                WorkspaceTone.Neutral);
    }

    public static IReadOnlyList<OperationsContinuityPanelRowModel> BuildCloseCalendarRows(OperationsCloseCalendarDto? calendar)
    {
        if (calendar?.Items is not { Count: > 0 } items)
        {
            return [];
        }

        return items
            .OrderBy(static item => item.NextDueDate ?? DateOnly.MaxValue)
            .ThenBy(static item => item.PeriodId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.FundAccountId)
            .Select(static item =>
            {
                var row = new SettingsOperationsCloseCalendarRow(item);
                var tone = item.BlockerCount > 0
                    ? WorkstationReadinessTone.Blocked
                    : item.IsReadyToClose
                        ? WorkstationReadinessTone.EvidenceLinked
                        : WorkstationReadinessTone.SignoffRequired;
                return new OperationsContinuityPanelRowModel(
                    item.WorkflowId.ToString("D"),
                    $"{row.PeriodId} close",
                    row.StatusLabel,
                    $"{row.ReadinessLabel} · {row.BlockerLabel} · {row.ChecklistLabel}",
                    $"{row.DueLabel} · {row.OwnerLabel} · {row.ApprovalLabel}",
                    tone,
                    ToWorkspaceTone(tone));
            })
            .ToArray();
    }

    public static bool IsChecklistTaskReady(OperationsCloseChecklistTaskDto task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task.AcknowledgedAtUtc is not null
            || task.Status.Trim().ToLowerInvariant() is "done" or "complete" or "completed" or "acknowledged";
    }

    public static bool IsChecklistTaskBlocked(OperationsCloseChecklistTaskDto task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return !string.IsNullOrWhiteSpace(task.BlockingReason)
            || task.Status.Trim().ToLowerInvariant() is "blocked" or "expired";
    }

    public static string FormatTimestamp(DateTimeOffset value)
        => $"{value.UtcDateTime:yyyy-MM-dd HH:mm} UTC";

    internal static string Pluralize(int count, string singular)
        => count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    internal static string ToWorkspaceTone(WorkstationReadinessTone readinessTone)
        => readinessTone switch
        {
            WorkstationReadinessTone.Blocked => WorkspaceTone.Danger,
            WorkstationReadinessTone.SignoffRequired => WorkspaceTone.Warning,
            WorkstationReadinessTone.EvidenceLinked or WorkstationReadinessTone.Ready => WorkspaceTone.Success,
            _ => WorkspaceTone.Neutral
        };

    private static int GatePriority(
        OperationsGateKeyDto? gateKey,
        IReadOnlyDictionary<OperationsGateKeyDto, OperationsGateStatusDto> gateStatusByKey)
    {
        if (gateKey is null || !gateStatusByKey.TryGetValue(gateKey.Value, out var status))
        {
            return 0;
        }

        return status switch
        {
            OperationsGateStatusDto.Blocked => 4,
            OperationsGateStatusDto.ReviewRequired => 3,
            OperationsGateStatusDto.InProgress => 2,
            OperationsGateStatusDto.NotStarted => 1,
            _ => 0
        };
    }
}
