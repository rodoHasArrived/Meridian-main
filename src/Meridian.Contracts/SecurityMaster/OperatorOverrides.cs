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
/// Security Master record. Values are string key/value pairs staged by the operator
/// workstation: paths outside the asset-terms namespace are free-form annotations
/// (e.g. ratings, sector classification) that never amend the canonical security terms,
/// while <c>assetSpecificTerms.*</c> paths are schema-validated corrections that — once
/// approved and published through the governed revision lifecycle — are merged into the
/// canonical terms as a complete economic-definition amendment by the publish fan-out's
/// canonical-merge handler. Until publish, staged values of either kind live only in
/// this overlay.
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
/// Public API request for a reviewer's decision on a pending operator-override overlay.
/// <see cref="Decision"/> must be <see cref="SecurityOverrideApprovalStatusDto.Approved"/> or
/// <see cref="SecurityOverrideApprovalStatusDto.Rejected"/>, with an optional comment recorded on the
/// durable audit trail. The reviewer identity is intentionally absent: the API layer derives it from
/// the authenticated principal and maps this request onto the internal store command, so a caller
/// cannot attribute a decision to someone else.
/// </summary>
public sealed record OperatorOverrideDecisionRequest(
    SecurityOverrideApprovalStatusDto Decision,
    string? Comment = null);
