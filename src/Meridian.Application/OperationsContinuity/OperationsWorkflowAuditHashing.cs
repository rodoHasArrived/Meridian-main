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
        var hashInput = new AuditHashInput(
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
        var canonicalJson = JsonSerializer.Serialize(hashInput, HashJsonOptions);
        var currentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson))).ToLowerInvariant();

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
