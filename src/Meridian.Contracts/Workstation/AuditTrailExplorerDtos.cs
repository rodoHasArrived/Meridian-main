using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Query contract for cross-object audit trail exploration.
/// </summary>
public sealed record AuditTrailExplorerQueryDto(
    string? SearchText = null,
    string? RunId = null,
    string? Category = null,
    string? Action = null,
    string? Outcome = null,
    string? Actor = null,
    string? Symbol = null,
    string? CorrelationId = null,
    string? ObjectKind = null,
    string? ObjectId = null,
    string? RelatedObjectId = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = 100);

/// <summary>
/// Normalized timeline row used by shared audit explorer surfaces.
/// </summary>
public sealed record AuditTrailTimelineEntryDto(
    string AuditId,
    DateTimeOffset OccurredAt,
    string ObjectKind,
    string ObjectId,
    string Category,
    string Action,
    string Outcome,
    string? Actor = null,
    string? RunId = null,
    string? Symbol = null,
    string? CorrelationId = null,
    string? Reason = null,
    string? Scope = null,
    string? Message = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    IReadOnlyList<string>? RelatedObjectIds = null,
    string? EvidenceRoute = null);

/// <summary>
/// Result contract for cross-object audit trail search.
/// </summary>
public sealed record AuditTrailExplorerResultDto(
    DateTimeOffset AsOf,
    int TotalMatched,
    int Returned,
    IReadOnlyList<AuditTrailTimelineEntryDto> Entries);

/// <summary>
/// Stable object-kind vocabulary for audit explorer rows.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AuditTrailObjectKindDto>))]
public enum AuditTrailObjectKindDto : byte
{
    Audit,
    Order,
    ExecutionControl,
    OperatorAction,
    Promotion,
    Run,
    Reconciliation,
    Report,
    Evidence
}
