using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflow
{
    private static IReadOnlyList<OperationsReconciliationLaneSummaryDto> BuildReconciliationLanes(
        IReadOnlyList<OperationsReconciliationLaneSummaryDto>? requestedLanes,
        IReadOnlyList<OperationsBreakCaseDto> breakCases,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        bool refreshBreakDerivedPosture = false)
    {
        var requestedById = (requestedLanes ?? [])
            .Where(static lane => !string.IsNullOrWhiteSpace(lane.LaneId))
            .GroupBy(static lane => lane.LaneId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        return ReconciliationLaneDefinitions
            .Select(definition => BuildReconciliationLane(
                definition,
                requestedById,
                breakCases,
                evidenceLinks,
                refreshBreakDerivedPosture))
            .ToArray();
    }

    private static OperationsReconciliationLaneSummaryDto BuildReconciliationLane(
        ReconciliationLaneDefinition definition,
        IReadOnlyDictionary<string, OperationsReconciliationLaneSummaryDto> requestedById,
        IReadOnlyList<OperationsBreakCaseDto> breakCases,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        bool refreshBreakDerivedPosture)
    {
        requestedById.TryGetValue(definition.LaneId, out var requested);
        var laneBreaks = breakCases
            .Where(definition.Matches)
            .ToArray();
        var openBreaks = laneBreaks
            .Where(static item => !IsClosedBreakStatus(item.Status))
            .ToArray();
        var hasCriticalBreak = openBreaks.Any(static item =>
            string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var inferredStatus = hasCriticalBreak
            ? OperationsReconciliationLaneStatusDto.Blocked
            : openBreaks.Length > 0
                ? OperationsReconciliationLaneStatusDto.ReviewRequired
                : OperationsReconciliationLaneStatusDto.Ready;
        var requestedBreakCount = requested?.BreakCount ?? 0;
        var shouldRefreshBreakPosture = refreshBreakDerivedPosture &&
            requested is not null &&
            requestedBreakCount != openBreaks.Length;
        var status = shouldRefreshBreakPosture ? inferredStatus : requested?.Status ?? inferredStatus;
        if (hasCriticalBreak)
        {
            status = OperationsReconciliationLaneStatusDto.Blocked;
        }
        else if (openBreaks.Length > 0 && status == OperationsReconciliationLaneStatusDto.Ready)
        {
            status = OperationsReconciliationLaneStatusDto.ReviewRequired;
        }

        var retainedEvidence = (requested?.EvidenceLinks ?? [])
            .Concat(laneBreaks.SelectMany(static breakCase => breakCase.EvidenceLinks))
            .Concat(status == OperationsReconciliationLaneStatusDto.Ready ? evidenceLinks : [])
            .Where(static link => !string.IsNullOrWhiteSpace(link.EvidenceId))
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var breakCount = shouldRefreshBreakPosture
            ? openBreaks.Length
            : Math.Max(requestedBreakCount, openBreaks.Length);
        var isReady = status == OperationsReconciliationLaneStatusDto.Ready && breakCount == 0;

        return new OperationsReconciliationLaneSummaryDto(
            definition.LaneId,
            string.IsNullOrWhiteSpace(requested?.Label) ? definition.Label : requested.Label.Trim(),
            status,
            isReady,
            breakCount,
            shouldRefreshBreakPosture || string.IsNullOrWhiteSpace(requested?.Summary)
                ? BuildReconciliationLaneSummary(definition.Label, status, breakCount)
                : requested.Summary.Trim(),
            string.IsNullOrWhiteSpace(requested?.RouteHint) ? definition.RouteHint : requested.RouteHint.Trim(),
            retainedEvidence,
            BuildReconciliationLaneActions(
                definition.Label,
                status,
                openBreaks,
                refreshBreakDerivedPosture ? null : requested?.RequiredActions));
    }

    private void RefreshReconciliationLanePosture()
    {
        ReconciliationLanes = BuildReconciliationLanes(
            ReconciliationLanes,
            BreakCases,
            evidenceLinks: [],
            refreshBreakDerivedPosture: true).ToList();
    }

    private static string BuildReconciliationLaneSummary(
        string label,
        OperationsReconciliationLaneStatusDto status,
        int breakCount) =>
        status switch
        {
            OperationsReconciliationLaneStatusDto.Ready => $"{label} is covered by the retained reconciliation run.",
            OperationsReconciliationLaneStatusDto.Blocked => $"{label} has {breakCount} open critical break(s).",
            OperationsReconciliationLaneStatusDto.ReviewRequired => $"{label} has {breakCount} open break(s) requiring controller review.",
            _ => $"{label} evidence was not returned by the reconciliation run."
        };

    private static IReadOnlyList<string> BuildReconciliationLaneActions(
        string label,
        OperationsReconciliationLaneStatusDto status,
        IReadOnlyList<OperationsBreakCaseDto> openBreaks,
        IReadOnlyList<string>? requestedActions)
    {
        var actions = new List<string>();
        foreach (var action in requestedActions ?? [])
        {
            AddRequiredAction(actions, action);
        }

        if (status == OperationsReconciliationLaneStatusDto.Ready)
        {
            return actions;
        }

        foreach (var breakCase in openBreaks)
        {
            if (ShouldAddBreakSuggestedAction(breakCase))
            {
                AddRequiredAction(actions, breakCase.SuggestedAction);
            }
        }

        var unassignedCount = openBreaks.Count(static breakCase => string.IsNullOrWhiteSpace(breakCase.Owner));
        if (unassignedCount > 0)
        {
            AddRequiredAction(actions, $"Assign {FormatLaneBreakCount(label, unassignedCount)} to an accountable owner.");
        }

        var escalatedBreaks = openBreaks
            .Where(static breakCase => !string.IsNullOrWhiteSpace(breakCase.EscalationLevel))
            .ToArray();
        if (escalatedBreaks.Length > 0)
        {
            var firstEscalation = escalatedBreaks[0].EscalationLevel!.Trim();
            var escalationReason = string.IsNullOrWhiteSpace(escalatedBreaks[0].EscalationReason)
                ? string.Empty
                : $": {escalatedBreaks[0].EscalationReason!.Trim()}";
            AddRequiredAction(actions, $"Review {firstEscalation} escalation for {FormatLaneBreakCount(label, escalatedBreaks.Length)}{escalationReason}.");
        }

        var blockedOutputs = openBreaks
            .SelectMany(static breakCase => breakCase.BlockedOutputs ?? [])
            .Select(static output => output.Trim())
            .Where(static output => output.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (blockedOutputs.Length > 0)
        {
            var shownOutputs = blockedOutputs.Take(3).ToArray();
            var remainingCount = blockedOutputs.Length - shownOutputs.Length;
            var outputSummary = remainingCount == 0
                ? string.Join(", ", shownOutputs)
                : $"{string.Join(", ", shownOutputs)}, and {remainingCount} more";
            AddRequiredAction(actions, $"Retain resolution evidence before releasing blocked output(s): {outputSummary}.");
        }

        if (actions.Count == 0)
        {
            AddRequiredAction(actions, $"Resolve or assign {label.ToLowerInvariant()} breaks and retain evidence.");
        }

        return actions;
    }

    private static void AddRequiredAction(List<string> actions, string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        var trimmed = action.Trim();
        if (!actions.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            actions.Add(trimmed);
        }
    }

    private static bool ShouldAddBreakSuggestedAction(OperationsBreakCaseDto breakCase)
    {
        if (string.IsNullOrWhiteSpace(breakCase.SuggestedAction))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(breakCase.Owner) ||
            !breakCase.SuggestedAction.TrimStart().StartsWith("Assign ", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatLaneBreakCount(string label, int count)
    {
        var normalizedLabel = label.ToLowerInvariant();
        return count == 1
            ? $"1 {normalizedLabel} break"
            : $"{count} {normalizedLabel} breaks";
    }

    private static bool BreakContains(OperationsBreakCaseDto breakCase, params string[] terms)
    {
        var searchable = string.Join(
            ' ',
            BuildBreakSearchValues(breakCase)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim()));
        return terms.Any(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string?> BuildBreakSearchValues(OperationsBreakCaseDto breakCase)
    {
        yield return breakCase.BreakId;
        yield return breakCase.CheckId;
        yield return breakCase.Category;
        yield return breakCase.Severity;
        yield return breakCase.Status;
        yield return breakCase.Owner;
        yield return breakCase.ExpectedSource;
        yield return breakCase.ActualSource;
        yield return breakCase.SecurityId;
        yield return breakCase.Symbol;
        yield return breakCase.SuggestedAction;
        yield return breakCase.EscalationLevel;
        yield return breakCase.EscalationReason;
        yield return breakCase.SlaState;
        yield return breakCase.RootCauseCode;
        yield return breakCase.ApprovalState;

        foreach (var output in breakCase.BlockedOutputs ?? [])
        {
            yield return output;
        }

        if (breakCase.CorrelationKeys is { } keys)
        {
            yield return keys.ReconciliationCaseId;
        }

        foreach (var evidence in breakCase.EvidenceLinks)
        {
            yield return evidence.EvidenceId;
            yield return evidence.Label;
            yield return evidence.Route;
            yield return evidence.Source;
        }
    }

    private sealed record ReconciliationLaneDefinition(
        string LaneId,
        string Label,
        string RouteHint,
        Func<OperationsBreakCaseDto, bool> Matches);
}
