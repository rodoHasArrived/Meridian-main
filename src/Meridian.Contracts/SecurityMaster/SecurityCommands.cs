using System.Text.Json;

namespace Meridian.Contracts.SecurityMaster;

public sealed record CreateSecurityRequest(
    Guid SecurityId,
    string AssetClass,
    JsonElement CommonTerms,
    JsonElement AssetSpecificTerms,
    IReadOnlyList<SecurityIdentifierDto> Identifiers,
    DateTimeOffset EffectiveFrom,
    string SourceSystem,
    string UpdatedBy,
    string? SourceRecordId,
    string? Reason);

public sealed record AmendSecurityTermsRequest(
    Guid SecurityId,
    long ExpectedVersion,
    JsonElement? CommonTerms,
    JsonElement? AssetSpecificTermsPatch,
    IReadOnlyList<SecurityIdentifierDto> IdentifiersToAdd,
    IReadOnlyList<SecurityIdentifierDto> IdentifiersToExpire,
    DateTimeOffset EffectiveFrom,
    string SourceSystem,
    string UpdatedBy,
    string? SourceRecordId,
    string? Reason);

public sealed record DeactivateSecurityRequest(
    Guid SecurityId,
    long ExpectedVersion,
    DateTimeOffset EffectiveTo,
    string SourceSystem,
    string UpdatedBy,
    string? SourceRecordId,
    string? Reason);

/// <summary>
/// Request for bulk importing securities via the HTTP API.
/// </summary>
public sealed record SecurityMasterImportRequest(
    string FileContent,
    string FileExtension);

public sealed record UpsertSecurityAliasRequest(
    Guid AliasId,
    Guid SecurityId,
    string AliasKind,
    string AliasValue,
    string? Provider,
    SecurityAliasScope Scope,
    string CreatedBy,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string? Reason);
