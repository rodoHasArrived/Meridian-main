using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityInstrumentTermsStatus>))]
public enum SecurityInstrumentTermsStatus { Ready, ReviewRequired, Unsupported }

[JsonConverter(typeof(JsonStringEnumConverter<SecurityInstrumentTermState>))]
public enum SecurityInstrumentTermState { Present, Missing, Null, Invalid, Ambiguous }

[JsonConverter(typeof(JsonStringEnumConverter<SecurityInstrumentTermUnitStatus>))]
public enum SecurityInstrumentTermUnitStatus { Explicit, Unspecified, NotApplicable }

[JsonConverter(typeof(JsonStringEnumConverter<SecurityInstrumentTermsSourceStatus>))]
public enum SecurityInstrumentTermsSourceStatus { Matched, Unavailable, VersionMismatch }

/// <summary>
/// A read-only view of the exact canonical terms at one stream version. JSON is transported as text
/// so clients cannot round retained decimal tokens while deserializing this response. The fingerprint
/// identifies the canonical term inputs, not a retained receipt, historical-visibility proof or edit authority.
/// </summary>
public sealed record SecurityInstrumentTermsDto(
    Guid SecurityId,
    string AssetClass,
    string Currency,
    long Version,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset RetrievedAtUtc,
    SecurityInstrumentTermsStatus Status,
    string Fingerprint,
    string TermsFormat,
    int AssetClassTermsVersion,
    SecurityInstrumentTermsSchemaDto Schema,
    SecurityInstrumentTermsSourceDto Source,
    string? CommonTermsJson,
    string? AssetSpecificTermsJson,
    IReadOnlyList<SecurityInstrumentTermDto> Fields,
    IReadOnlyList<string> Diagnostics);

public sealed record SecurityInstrumentTermsSchemaDto(
    string AssetClass,
    int WriteVersion,
    int ReadVersion,
    string? LotUnit,
    string? CashFlowBehavior,
    string? ProfileId,
    int? ProfileVersion,
    string? ProfileStatus);

public sealed record SecurityInstrumentTermsSourceDto(
    SecurityInstrumentTermsSourceStatus Status,
    string Summary,
    string? ProvenanceJson);

/// <summary>
/// A source property or a missing declared field. Path is an RFC 6901 pointer into one of the two
/// original term blocks. RawJson and numeric ValueText retain source lexemes without conversion.
/// IsDeclared describes registry coverage; Present establishes shape only, never economic acceptance.
/// A missing unit remains unspecified: currency, percent and quote conventions are never inferred.
/// </summary>
public sealed record SecurityInstrumentTermDto(
    string Path,
    string Key,
    string Label,
    string Group,
    string? DeclaredType,
    string JsonType,
    SecurityInstrumentTermState State,
    bool IsDeclared,
    bool Required,
    string? Role,
    string? ValueText,
    string? RawJson,
    string? Unit,
    SecurityInstrumentTermUnitStatus UnitStatus,
    string? UnitPath,
    string? ValidationMessage,
    IReadOnlyList<SecurityInstrumentTermDto> Children);
