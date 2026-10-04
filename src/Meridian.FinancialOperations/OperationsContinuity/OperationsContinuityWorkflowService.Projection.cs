using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

public sealed partial class OperationsContinuityWorkflowService
{
    private static IReadOnlyList<OperationsEvidenceLinkDto> EvidenceForGate(
        IReadOnlyList<OperationsTimelineEntryDto> timeline,
        OperationsGateKeyDto gate) =>
        timeline
            .Where(entry => entry.Gate == gate)
            .SelectMany(static entry => entry.References)
            .DistinctBy(static link => link.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<OperationsCloseChecklistTaskDto> BuildChecklist(
        OperationsContinuityWorkflow workflow,
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        var dueBase = DateOnly.FromDateTime(workflow.CreatedAtUtc.UtcDateTime).AddDays(2);
        var orderedTimeline = OrderChecklistTimeline(timeline);
        var acknowledgments = new Dictionary<OperationsGateKeyDto, OperationsTimelineEntryDto>();
        var gateEvidence = new Dictionary<OperationsGateKeyDto, OperationsEvidenceLinkDto>();
        foreach (var entry in orderedTimeline)
        {
            if (!IsSuccessfulChecklistEvent(entry))
            {
                continue;
            }

            if (entry.EventType == "checklist-task-acknowledged")
            {
                if (entry.Gate is { } acknowledgedGate &&
                    entry.ToGateStatus == OperationsGateStatusDto.Passed &&
                    !string.IsNullOrWhiteSpace(entry.Actor) &&
                    gateEvidence.TryGetValue(acknowledgedGate, out var reviewedEvidence) &&
                    entry.References.Any(link => link == reviewedEvidence))
                {
                    acknowledgments[acknowledgedGate] = entry;
                }

                continue;
            }

            if (entry.EventType is "workflow-reopened" or "approval-rejected" or "gate-posture-refreshed")
            {
                // A posture refresh has no per-gate mutation payload in the retained audit.
                acknowledgments.Clear();
            }
            else if (entry.Gate is { } changedGate)
            {
                if (entry.EventType is "security-master-resolved" or "security-master-override-approved")
                {
                    changedGate = OperationsGateKeyDto.BrokerIngest;
                }

                foreach (var dependentGate in acknowledgments.Keys.Where(key => (byte)key >= (byte)changedGate).ToArray())
                {
                    acknowledgments.Remove(dependentGate);
                }
            }

            if (entry.Gate is { } evidenceGate && entry.ToGateStatus == OperationsGateStatusDto.Passed)
            {
                gateEvidence[evidenceGate] = ChecklistCompletionEvidence(entry, evidenceGate);
                if ((entry.EventType is "security-master-resolved" or "security-master-override-approved") &&
                    workflow.BrokerIngestGate.Status == OperationsGateStatusDto.Passed)
                {
                    // Override approval retains unresolved-identity blockers, so Passed also proves intake resolution.
                    gateEvidence[OperationsGateKeyDto.BrokerIngest] = ChecklistCompletionEvidence(entry, OperationsGateKeyDto.BrokerIngest);
                }
            }
        }

        return workflow.Gates.Select((gate, index) =>
        {
            gateEvidence.TryGetValue(gate.GateKey, out var evidence);
            var acknowledgment = gate.Status == OperationsGateStatusDto.Passed
                ? acknowledgments.GetValueOrDefault(gate.GateKey)
                : null;
            var status = gate.Status switch
            {
                OperationsGateStatusDto.Passed => "Done",
                OperationsGateStatusDto.Blocked => "Blocked",
                OperationsGateStatusDto.InProgress => "InProgress",
                _ => "Pending"
            };

            return new OperationsCloseChecklistTaskDto(
                CloseChecklistTaskId(gate.GateKey),
                gate.GateKey,
                $"{DisplayName(gate.GateKey)} close gate",
                gate.CompletedBy ?? "accounting-operator",
                RequiredEvidence: "Evidence link and gate completion audit",
                RequiredApprovalCount: gate.GateKey == OperationsGateKeyDto.Approval ? 2 : 1,
                ExpiresOn: dueBase.AddDays(index + 5),
                dueBase.AddDays(index),
                status,
                gate.Blockers.FirstOrDefault()?.Message,
                evidence?.EvidenceId,
                gate.NextActions.FirstOrDefault()?.Route,
                CanAcknowledge: !workflow.IsClosed && gate.Status == OperationsGateStatusDto.Passed &&
                    evidence is not null && acknowledgment is null,
                acknowledgment?.OccurredAtUtc,
                acknowledgment?.Actor);
        }).ToArray();
    }

    private static OperationsEvidenceLinkDto ChecklistCompletionEvidence(OperationsTimelineEntryDto entry, OperationsGateKeyDto gate) =>
        (entry.Gate == gate ? entry.References.FirstOrDefault() : null) ?? new OperationsEvidenceLinkDto(
            $"operations-audit:{entry.AuditId:D}:{gate.ToString().ToLowerInvariant()}",
            $"{DisplayName(gate)} completion audit {entry.CurrentHash}",
            $"/api/workstation/operations/continuity/{entry.WorkflowId:D}/timeline",
            gate.ToString(),
            entry.OccurredAtUtc);

    private static bool CompletesChecklistGate(OperationsTimelineEntryDto entry, OperationsGateKeyDto gate) =>
        entry.EventType != "checklist-task-acknowledged" &&
        entry.ToGateStatus == OperationsGateStatusDto.Passed && IsSuccessfulChecklistEvent(entry) &&
        (entry.Gate == gate || (gate == OperationsGateKeyDto.BrokerIngest &&
            entry.EventType is "security-master-resolved" or "security-master-override-approved"));

    private static bool IsSuccessfulChecklistEvent(OperationsTimelineEntryDto entry) =>
        entry.Outcome is { } outcome
            ? outcome.State is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings
            : entry.EventType is not ("workflow-transition-blocked" or "workflow-transition-failed" or "ledger-posting-blocked");

    private static IReadOnlyList<OperationsTimelineEntryDto> OrderChecklistTimeline(
        IReadOnlyList<OperationsTimelineEntryDto> timeline)
    {
        // Store reads may sort equal timestamps differently. Retained hash links define append order.
        var successors = new Dictionary<string, OperationsTimelineEntryDto>(StringComparer.Ordinal);
        foreach (var entry in timeline)
        {
            if (string.IsNullOrWhiteSpace(entry.CurrentHash) ||
                !successors.TryAdd(entry.PreviousHash ?? string.Empty, entry))
            {
                return [];
            }
        }

        var ordered = new List<OperationsTimelineEntryDto>(timeline.Count);
        var previousHash = string.Empty;
        while (successors.Remove(previousHash, out var next))
        {
            ordered.Add(next);
            previousHash = next.CurrentHash;
        }

        return ordered.Count == timeline.Count ? ordered : [];
    }

    private static string CloseChecklistTaskId(OperationsGateKeyDto gate) =>
        $"close-gate-{gate}".ToLowerInvariant();

    private static OperationsGateDto ToGateDto(OperationsGateState gate) =>
        new(
            gate.GateKey,
            DisplayName(gate.GateKey),
            gate.Status,
            IsRequired: true,
            Description(gate.GateKey),
            gate.Blockers,
            gate.NextActions,
            gate.CompletedAtUtc,
            gate.CompletedBy);

    private static OperationsTimelineEntryDto ToTimelineEntry(OperationsWorkflowAuditDto entry) =>
        new(
            entry.AuditId,
            entry.OccurredAtUtc,
            entry.WorkflowId,
            entry.FundAccountId,
            entry.PeriodId,
            entry.EventType,
            entry.FromState,
            entry.ToState,
            entry.Gate,
            entry.FromGateStatus,
            entry.ToGateStatus,
            entry.Actor,
            entry.Rationale,
            entry.CorrelationId,
            entry.CorrelationKeys,
            entry.References,
            entry.PreviousHash,
            entry.CurrentHash,
            entry.Outcome);

    private static string DisplayName(OperationsGateKeyDto gateKey) => gateKey switch
    {
        OperationsGateKeyDto.BrokerIngest => "Broker, custodian, and bank intake",
        OperationsGateKeyDto.SecurityMaster => "Security Master resolution",
        OperationsGateKeyDto.LedgerPosting => "Ledger draft and posting",
        OperationsGateKeyDto.Reconciliation => "Reconciliation",
        OperationsGateKeyDto.Approval => "Approval and close readiness",
        _ => gateKey.ToString()
    };

    private static string Description(OperationsGateKeyDto gateKey) => gateKey switch
    {
        OperationsGateKeyDto.BrokerIngest => "Imports and normalizes external account activity before accounting use.",
        OperationsGateKeyDto.SecurityMaster => "Requires authoritative instrument identity, provenance, and accounting classifications.",
        OperationsGateKeyDto.LedgerPosting => "Controls journal preview, validation, idempotency, and posting readiness.",
        OperationsGateKeyDto.Reconciliation => "Connects expected Security Master events, actual activity, and ledger postings.",
        OperationsGateKeyDto.Approval => "Requires operator, reviewer, rationale, and linked evidence before close.",
        _ => gateKey.ToString()
    };
}
