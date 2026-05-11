using System.Collections.Generic;

namespace Meridian.Contracts.SecurityMaster;

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
    DateTimeOffset UpdatedAt);

/// <summary>
/// Partial update request for operator overrides. <see cref="SetValues"/> upserts the
/// listed keys; <see cref="RemoveKeys"/> deletes the listed keys. Both collections are
/// optional and may be empty (no-op patch).
/// </summary>
public sealed record OperatorOverridesPatchRequest(
    IReadOnlyDictionary<string, string>? SetValues,
    IReadOnlyList<string>? RemoveKeys);
