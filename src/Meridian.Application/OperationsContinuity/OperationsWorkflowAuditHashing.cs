using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.OperationsContinuity;

internal static class OperationsWorkflowAuditHashing
{
    private static readonly JsonSerializerOptions HashJsonOptions = new(JsonSerializerDefaults.Web);

    public static OperationsWorkflowAuditDto Create(
        OperationsWorkflowAuditDraft draft,
        string? previousHash,
        DateTimeOffset occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (occurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Audit timestamps must be UTC.", nameof(occurredAtUtc));
        }

        var auditId = Guid.NewGuid();
        var currentHash = ComputeCurrentHash(
            auditId,
            occurredAtUtc,
            draft.WorkflowId,
            draft.FundAccountId,
            draft.PeriodId,
            draft.EventType,
            draft.FromState,
            draft.ToState,
            draft.Gate,
            draft.FromGateStatus,
            draft.ToGateStatus,
            draft.Actor,
            draft.Rationale,
            draft.CorrelationId,
            draft.References,
            previousHash);

        return new OperationsWorkflowAuditDto(
            auditId,
            occurredAtUtc,
            draft.WorkflowId,
            draft.FundAccountId,
            draft.PeriodId,
            draft.EventType,
            draft.FromState,
            draft.ToState,
            draft.Gate,
            draft.FromGateStatus,
            draft.ToGateStatus,
            draft.Actor,
            draft.Rationale,
            draft.CorrelationId,
            draft.References,
            previousHash,
            currentHash);
    }

    public static bool TryValidateChain(
        IReadOnlyList<OperationsWorkflowAuditDto> timeline,
        out string blockerCode,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        if (timeline.Count == 0)
        {
            blockerCode = "AUDIT_CHAIN_MISSING";
            message = "Workflow close requires at least one append-only audit event.";
            return false;
        }

        string? previousHash = null;
        DateTimeOffset? previousOccurredAtUtc = null;
        for (var index = 0; index < timeline.Count; index++)
        {
            var entry = timeline[index];
            if (entry.OccurredAtUtc.Offset != TimeSpan.Zero)
            {
                blockerCode = "AUDIT_CHAIN_INVALID";
                message = $"Audit event {entry.AuditId} timestamp is not UTC.";
                return false;
            }

            if (previousOccurredAtUtc.HasValue && entry.OccurredAtUtc < previousOccurredAtUtc.Value)
            {
                blockerCode = "AUDIT_CHAIN_INVALID";
                message = $"Audit event {entry.AuditId} is out of chronological order.";
                return false;
            }

            if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                blockerCode = "AUDIT_CHAIN_INVALID";
                message = index == 0
                    ? "First audit event must not reference a previous hash."
                    : $"Audit event {entry.AuditId} does not link to the previous event hash.";
                return false;
            }

            var expectedHash = ComputeCurrentHash(
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
                entry.References,
                entry.PreviousHash);

            if (!string.Equals(entry.CurrentHash, expectedHash, StringComparison.Ordinal))
            {
                blockerCode = "AUDIT_CHAIN_INVALID";
                message = $"Audit event {entry.AuditId} hash does not match its canonical content.";
                return false;
            }

            previousHash = entry.CurrentHash;
            previousOccurredAtUtc = entry.OccurredAtUtc;
        }

        blockerCode = string.Empty;
        message = string.Empty;
        return true;
    }

    private static string ComputeCurrentHash(
        Guid auditId,
        DateTimeOffset occurredAtUtc,
        Guid workflowId,
        Guid fundAccountId,
        string periodId,
        string eventType,
        OperationsWorkflowStatusDto fromState,
        OperationsWorkflowStatusDto toState,
        OperationsGateKeyDto? gate,
        OperationsGateStatusDto? fromGateStatus,
        OperationsGateStatusDto? toGateStatus,
        string actor,
        string? rationale,
        string? correlationId,
        IReadOnlyList<OperationsEvidenceLinkDto> references,
        string? previousHash)
    {
        var hashInput = new AuditHashInput(
            auditId,
            occurredAtUtc,
            workflowId,
            fundAccountId,
            periodId,
            eventType,
            fromState,
            toState,
            gate,
            fromGateStatus,
            toGateStatus,
            actor,
            rationale,
            correlationId,
            references,
            previousHash);
        var canonicalJson = JsonSerializer.Serialize(hashInput, HashJsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();
    }

    private sealed record AuditHashInput(
        Guid AuditId,
        DateTimeOffset OccurredAtUtc,
        Guid WorkflowId,
        Guid FundAccountId,
        string PeriodId,
        string EventType,
        OperationsWorkflowStatusDto FromState,
        OperationsWorkflowStatusDto ToState,
        OperationsGateKeyDto? Gate,
        OperationsGateStatusDto? FromGateStatus,
        OperationsGateStatusDto? ToGateStatus,
        string Actor,
        string? Rationale,
        string? CorrelationId,
        IReadOnlyList<OperationsEvidenceLinkDto> References,
        string? PreviousHash);
}
