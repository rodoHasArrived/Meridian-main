namespace Meridian.FinancialOperations.Reconciliation.Connectors;

/// <summary>
/// The declarative, versioned mapping-profile document. Profiles are data, not code:
/// operators onboard a new custodian or broker format by creating a document instead of
/// waiting for a release. Documents are persisted as JSON (ADR-014 source-generated)
/// and materialized into runtime <see cref="StatementMappingProfile"/> records.
/// </summary>
public sealed record StatementMappingProfileDocument(
    int SchemaVersion,
    string ProfileId,
    string DisplayName,
    string Format,
    StatementProfileCsvOptions? Csv,
    string? Culture,
    IReadOnlyList<string>? DateFormats,
    IReadOnlyList<StatementProfileFieldMapping> Fields,
    IReadOnlyList<StatementProfileActivityCode> ActivityCodes,
    string? LastAcceptedFingerprint = null,
    bool IsBuiltIn = false,
    string? Notes = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string CsvFormat = "csv";
    public const string OfxFormat = "ofx";
}

public sealed record StatementProfileCsvOptions(
    string Delimiter = ",",
    string Quote = "\"",
    bool HasHeader = true);

/// <summary>
/// Maps one canonical field to a source column (or OFX tag). Aliases let a single profile
/// absorb the header spellings a custodian rotates through without operator edits.
/// </summary>
public sealed record StatementProfileFieldMapping(
    string CanonicalField,
    string SourceColumn,
    IReadOnlyList<string>? Aliases = null,
    bool Required = false);

public sealed record StatementProfileActivityCode(
    string SourceCode,
    string CanonicalActivityType);

/// <summary>Persisted snapshot shape for the file-backed profile store.</summary>
public sealed record StatementMappingProfileSnapshot(
    int Version,
    IReadOnlyList<StatementMappingProfileDocument> Profiles);
