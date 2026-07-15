using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityOverrideApprovalStatusDto>))]
public enum SecurityOverrideApprovalStatusDto
{
    NotRequested = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public sealed record SecurityOverrideAuditEntryDto(
    string EventType,
    string Actor,
    DateTimeOffset OccurredAt,
    SecurityOverrideApprovalStatusDto ApprovalStatus,
    string? ReasonCode = null,
    string? Comment = null,
    string? Reviewer = null,
    DateTimeOffset? ReviewedAt = null);

/// <summary>
/// Operator-supplied per-security override values that supplement the authoritative
/// Security Master record. Values are free-form string key/value pairs used by the
/// operator workstation to record annotations and corrections (e.g. ratings, sector
/// classification, factor adjustments) without amending the canonical security terms.
/// </summary>
public sealed record OperatorOverridesDto(
    Guid SecurityId,
    IReadOnlyDictionary<string, string> Values,
    string UpdatedBy,
    DateTimeOffset UpdatedAt)
{
    public SecurityOverrideApprovalStatusDto ApprovalStatus { get; init; } = SecurityOverrideApprovalStatusDto.NotRequested;

    public string? ReasonCode { get; init; }

    public string? ReviewedBy { get; init; }

    public DateTimeOffset? ReviewedAt { get; init; }

    public IReadOnlyList<SecurityOverrideAuditEntryDto> AuditTrail { get; init; } = [];
}

/// <summary>
/// Partial update request for operator overrides. <see cref="SetValues"/> upserts the
/// listed keys; <see cref="RemoveKeys"/> deletes the listed keys. Both collections are
/// optional and may be empty (no-op patch).
/// </summary>
public sealed record OperatorOverridesPatchRequest(
    IReadOnlyDictionary<string, string>? SetValues,
    IReadOnlyList<string>? RemoveKeys)
{
    public string? ReasonCode { get; init; }
}

/// <summary>
<<<<<<< Updated upstream
/// Records a reviewer's approval decision for a security's pending operator overrides. The
/// <see cref="Decision"/> must be either <see cref="SecurityOverrideApprovalStatusDto.Approved"/> or
/// <see cref="SecurityOverrideApprovalStatusDto.Rejected"/>; the acting reviewer is server-derived
/// from the authenticated principal, not supplied by the caller.
/// </summary>
public sealed record OperatorOverrideApprovalDecisionRequest(
    SecurityOverrideApprovalStatusDto Decision,
    string? ReasonCode = null,
=======
/// A reviewer's decision on a pending operator-override overlay. <see cref="Decision"/> must be
/// <see cref="SecurityOverrideApprovalStatusDto.Approved"/> or
/// <see cref="SecurityOverrideApprovalStatusDto.Rejected"/>; the reviewer identity and an optional
/// comment are stamped onto the durable audit trail.
/// </summary>
public sealed record OperatorOverrideDecisionRequest(
    SecurityOverrideApprovalStatusDto Decision,
    string Reviewer,
>>>>>>> Stashed changes
    string? Comment = null);
