namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Field-level lineage for a single Security Master field. Where the record-level
/// <c>Provenance</c> answers "who last touched this record", a <see cref="SecurityFieldProvenance"/>
/// answers "why does <em>this field</em> have <em>this value</em>": which source authoritatively set
/// it, the authority rank and confidence that justified it, and when it was decided. This is the
/// per-field counterpart the record-level model could not express (every field shared one source).
/// </summary>
/// <param name="FieldPath">
/// Canonical dotted field path this entry describes, e.g. <c>common.currency</c> or
/// <c>assetSpecific.issuerName</c>. A bare name (no scope prefix) is treated as an asset-specific term.
/// </param>
/// <param name="Source">The source system whose value is authoritative for this field.</param>
/// <param name="Authority">
/// Authority rank of <paramref name="Source"/> (lower = higher authority), derived from the
/// configured source-precedence ladder at the time the field was decided.
/// </param>
/// <param name="Confidence">Confidence score in [0,1] carried by the winning source for this field.</param>
/// <param name="AsOf">When this field's value/authority was decided.</param>
/// <param name="Reason">Optional operator- or policy-supplied justification.</param>
/// <param name="UpdatedBy">Optional actor who applied the decision (operator id or automated policy).</param>
public sealed record SecurityFieldProvenance(
    string FieldPath,
    string Source,
    int Authority,
    decimal Confidence,
    DateTimeOffset AsOf,
    string? Reason = null,
    string? UpdatedBy = null);

/// <summary>
/// An immutable set of <see cref="SecurityFieldProvenance"/> entries keyed by canonical field path.
/// Round-trips as the reserved <c>fields</c> object embedded in a record's provenance JSON so
/// field-level lineage travels with the record without a separate storage column.
/// </summary>
public sealed record SecurityFieldProvenanceSet(IReadOnlyList<SecurityFieldProvenance> Fields)
{
    /// <summary>The reserved property name that carries field provenance inside a provenance JSON blob.</summary>
    public const string EmbeddedPropertyName = "fields";

    /// <summary>An empty set (no field-level provenance recorded).</summary>
    public static SecurityFieldProvenanceSet Empty { get; } = new(Array.Empty<SecurityFieldProvenance>());

    /// <summary>Returns the entry for <paramref name="fieldPath"/>, or <see langword="null"/> if absent.</summary>
    public SecurityFieldProvenance? Find(string fieldPath)
        => Fields.FirstOrDefault(f => string.Equals(f.FieldPath, fieldPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns a new set with <paramref name="entry"/> upserted by field path (case-insensitive):
    /// an existing entry for the same path is replaced, otherwise the entry is appended.
    /// </summary>
    public SecurityFieldProvenanceSet Upsert(SecurityFieldProvenance entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var next = Fields
            .Where(f => !string.Equals(f.FieldPath, entry.FieldPath, StringComparison.OrdinalIgnoreCase))
            .Append(entry)
            .OrderBy(f => f.FieldPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new SecurityFieldProvenanceSet(next);
    }
}
