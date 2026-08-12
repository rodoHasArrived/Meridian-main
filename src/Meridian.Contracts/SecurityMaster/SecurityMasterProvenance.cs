using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// Canonical conflict-kind identifiers for <see cref="SecurityMasterConflict.ConflictKind"/>.
/// Detection produces these; the workbench and the authority policy route on them. Keep them here
/// so producers and consumers cannot drift on spelling.
/// </summary>
public static class SecurityMasterConflictKinds
{
    /// <summary>An identifier that multiple distinct securities claim from different providers.</summary>
    public const string IdentifierAmbiguity = "IdentifierAmbiguity";

    /// <summary>Two source systems disagree on an economic term (maturity, coupon, day count, …) of the same security.</summary>
    public const string EconomicTermMismatch = "EconomicTermMismatch";

    /// <summary>Two source systems disagree on a common term (currency, country of risk, …) of the same security.</summary>
    public const string CommonTermMismatch = "CommonTermMismatch";
}

/// <summary>
/// Typed record-level provenance read from the Security Master provenance document — who supplied
/// the record, when it was true, and who applied it.
/// </summary>
public sealed record SecurityRecordProvenance(
    string SourceSystem,
    string? SourceRecordId,
    DateTimeOffset? AsOf,
    string? UpdatedBy,
    string? Reason)
{
    /// <summary>
    /// Projects this record-level provenance onto one field — the per-field (vendor, asOf,
    /// confidence) attribution consumed by field-conflict assessment and display.
    /// </summary>
    public SecurityFieldProvenance ForField(string fieldPath, decimal? confidence = null)
        => new(fieldPath, SourceSystem, AsOf, UpdatedBy, confidence);
}

/// <summary>
/// Field-level provenance: which source system asserted the value at <paramref name="FieldPath"/>,
/// as of when, and with what confidence (null when the source carries no confidence score — never
/// fabricated).
/// </summary>
public sealed record SecurityFieldProvenance(
    string FieldPath,
    string SourceSystem,
    DateTimeOffset? AsOf,
    string? UpdatedBy,
    decimal? Confidence);

/// <summary>
/// Canonical origins for persisted field-level provenance rows. The origin distinguishes canonical
/// golden-record attribution from overlay annotations, so an operator edit can never masquerade as
/// (or clobber) a conflict-resolution winner.
/// </summary>
public static class SecurityFieldProvenanceOrigins
{
    /// <summary>The attribution written when a golden-record conflict resolves to a winning source.</summary>
    public const string ConflictResolution = "ConflictResolution";

    /// <summary>The attribution written when an operator stages an overlay field edit.</summary>
    public const string OperatorFieldEdit = "OperatorFieldEdit";
}

/// <summary>
/// A persisted field-level provenance row: the durable form of <see cref="SecurityFieldProvenance"/>,
/// keyed by (security, field path, origin). <see cref="OriginReference"/> points back at the artifact
/// that asserted the attribution — the conflict id for conflict resolutions, the revision id for
/// operator field edits — so lineage is traceable to its governing record.
/// </summary>
public sealed record SecurityFieldProvenanceRecord(
    Guid SecurityId,
    string FieldPath,
    string SourceSystem,
    DateTimeOffset? AsOf,
    string? UpdatedBy,
    decimal? Confidence,
    string Origin,
    string? OriginReference,
    DateTimeOffset RecordedAt)
{
    /// <summary>Projects the durable row onto the display-level field-provenance shape.</summary>
    public SecurityFieldProvenance ToFieldProvenance()
        => new(FieldPath, SourceSystem, AsOf, UpdatedBy, Confidence);
}

/// <summary>
/// Typed reader for the Security Master provenance JSON
/// (<c>{ sourceSystem, sourceRecordId, asOf, updatedBy, reason }</c> — the shape the F# snapshot
/// serializer writes). Reads are tolerant: a missing or malformed document yields
/// <see cref="UnknownSource"/> rather than failing the row.
/// </summary>
public static class SecurityMasterProvenanceReader
{
    public const string UnknownSource = "Unknown";

    public static SecurityRecordProvenance Read(JsonElement provenance)
    {
        if (provenance.ValueKind != JsonValueKind.Object)
        {
            return new SecurityRecordProvenance(UnknownSource, null, null, null, null);
        }

        var sourceSystem = ReadString(provenance, "sourceSystem");
        return new SecurityRecordProvenance(
            string.IsNullOrWhiteSpace(sourceSystem) ? UnknownSource : sourceSystem.Trim(),
            ReadString(provenance, "sourceRecordId"),
            ReadTimestamp(provenance, "asOf"),
            ReadString(provenance, "updatedBy"),
            ReadString(provenance, "reason"));
    }

    private static string? ReadString(JsonElement source, string propertyName)
        => SecurityTermReader.TryGetProperty(source, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadTimestamp(JsonElement source, string propertyName)
        => SecurityTermReader.TryGetProperty(source, propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
           && value.TryGetDateTimeOffset(out var timestamp)
            ? timestamp
            : null;
}
