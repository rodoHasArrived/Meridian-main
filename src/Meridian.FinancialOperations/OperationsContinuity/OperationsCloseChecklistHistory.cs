using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>Projects only retained snapshots. Unknown historical gate states remain explicit gaps.</summary>
public static class OperationsCloseChecklistHistory
{
    public static OperationsCloseChecklistHistoryDto Build(Guid workflowId,
        IReadOnlyList<OperationsTimelineEntryDto> timeline, DateTimeOffset asOf)
    {
        if (timeline.Count == 0)
            return new(workflowId, asOf, "Unavailable", "No retained checklist history is available.", null, null, true, [], []);
        if (timeline.Any(entry => entry.WorkflowId != workflowId || string.IsNullOrWhiteSpace(entry.CurrentHash))
            || timeline.Select(entry => entry.AuditId).Distinct().Count() != timeline.Count)
            throw new InvalidDataException("The retained checklist timeline contains foreign or ambiguous audit identities.");
        // Hash links resolve equal timestamps without inventing an ordering from GUIDs.
        var byPrevious = new Dictionary<string, OperationsTimelineEntryDto>(StringComparer.Ordinal);
        foreach (var entry in timeline)
            if (!byPrevious.TryAdd(entry.PreviousHash ?? string.Empty, entry))
                throw new InvalidDataException("The retained checklist timeline branches.");
        var ordered = new List<OperationsTimelineEntryDto>();
        var previous = string.Empty;
        while (byPrevious.Remove(previous, out var next))
        {
            ordered.Add(next);
            previous = next.CurrentHash;
        }
        if (ordered.Count != timeline.Count || !OperationsWorkflowAuditHashing.TryValidateTimeline(ordered, out _, out _))
            throw new InvalidDataException("The retained checklist timeline failed its audit integrity check.");
        var observations = new List<OperationsCloseChecklistObservationDto>();
        var missing = new List<Guid>();
        var versionGaps = new List<OperationsCloseChecklistVersionGapDto>();
        var segment = 0;
        var gap = false;
        long? priorVersion = null;
        foreach (var entry in ordered.Where(entry => entry.OccurredAtUtc <= asOf))
        {
            if (entry.CloseChecklistSnapshot is not { } snapshot)
            {
                missing.Add(entry.AuditId);
                gap = true;
                continue;
            }
            if (priorVersion is { } version && snapshot.WorkflowVersion <= version)
                throw new InvalidDataException("Retained checklist workflow versions are not increasing.");
            if (snapshot.WorkflowVersion > (priorVersion ?? 0) + 1)
            {
                versionGaps.Add(new(priorVersion ?? 0, snapshot.WorkflowVersion, entry.AuditId));
                gap = true;
            }
            if (gap)
                segment++;
            gap = false;
            observations.Add(new(entry.AuditId, entry.OccurredAtUtc, snapshot.WorkflowVersion,
                snapshot.Gates.Count, snapshot.Gates.Count(gate => gate.Status != OperationsGateStatusDto.Passed),
                snapshot.Gates.Count(gate => gate.Status == OperationsGateStatusDto.Blocked), segment));
            priorVersion = snapshot.WorkflowVersion;
        }
        var partial = missing.Count > 0 || versionGaps.Count > 0 || observations.Count == 0;
        return new OperationsCloseChecklistHistoryDto(workflowId, asOf, observations.Count == 0 ? "Unavailable" : "Ready",
            observations.Count == 0 ? "Historical audit events do not retain checklist snapshots. Coverage begins with new canonical committed transitions."
                : partial ? "History contains gaps. The chart connects only observations in the same retained coverage segment." : null,
            observations.FirstOrDefault()?.RecordedAtUtc, observations.LastOrDefault()?.RecordedAtUtc,
            partial, missing, observations)
        { VersionGaps = versionGaps };
    }
}
