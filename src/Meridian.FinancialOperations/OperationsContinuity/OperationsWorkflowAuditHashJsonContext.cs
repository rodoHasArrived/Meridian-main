using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.FinancialOperations.OperationsContinuity;

internal sealed record OperationsWorkflowAuditLegacyHashInput(
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
    OperationsContinuityCorrelationKeysDto? CorrelationKeys,
    IReadOnlyList<OperationsEvidenceLinkDto> References,
    string? PreviousHash);

internal sealed record OperationsWorkflowAuditOutcomeHashInput(
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
    OperationsContinuityCorrelationKeysDto? CorrelationKeys,
    IReadOnlyList<OperationsEvidenceLinkDto> References,
    VerifiedOperationOutcome Outcome,
    string? PreviousHash);

/// <summary>
/// ADR-014 source-generated metadata for the canonical Operations Continuity audit hash payloads.
/// Web defaults intentionally preserve the historical camel-cased canonical JSON byte sequence.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = false)]
[JsonSerializable(
    typeof(OperationsWorkflowAuditLegacyHashInput),
    TypeInfoPropertyName = "LegacyAuditHashInput")]
[JsonSerializable(
    typeof(OperationsWorkflowAuditOutcomeHashInput),
    TypeInfoPropertyName = "OutcomeAuditHashInput")]
internal sealed partial class OperationsWorkflowAuditHashJsonContext : JsonSerializerContext;
