using System.Text.Json.Serialization;

namespace Meridian.Contracts.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderConnectionCapabilityDto
{
    Data,
    Brokerage,
    DataAndBrokerage,
    AccountingSystem
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderCredentialStateDto
{
    NotRequired,
    Missing,
    Partial,
    Configured,
    Verified,
    Invalid
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderCredentialSourceDto
{
    None,
    LocalEncryptedStore,
    Environment,
    ExternalVaultReference,
    NotRequired
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderVerificationStateDto
{
    NotRequired,
    NotVerified,
    Verified,
    Failed,
    Stale
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderContinuityHealthDto
{
    Unknown,
    Healthy,
    Warning,
    Degraded,
    Blocked
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderCredentialInputKindDto
{
    Text,
    Password,
    Url
}

public sealed record ProviderCredentialFieldMetadataDto(
    string Name,
    string Label,
    bool Required,
    ProviderCredentialInputKindDto InputKind,
    string? Placeholder = null,
    string? HelpText = null);

public sealed record ProviderEnvironmentOptionDto(
    string Value,
    string Label,
    bool IsDefault = false,
    string? HelpText = null);

public sealed record ProviderConnectionRowDto(
    string ProviderId,
    string DisplayName,
    ProviderConnectionCapabilityDto Capability,
    ProviderCredentialStateDto CredentialState,
    ProviderCredentialSourceDto CredentialSource,
    ProviderVerificationStateDto VerificationState,
    ProviderContinuityHealthDto Health,
    bool FallbackActive,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastSuccessfulAt,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    string? MaskedKeyPreview,
    string? Environment,
    string? ExternalAccountId,
    IReadOnlyList<string> AffectedWorkflows,
    string RecommendedAction,
    string ActionHref,
    IReadOnlyList<ProviderCredentialFieldMetadataDto>? CredentialFields = null,
    IReadOnlyList<ProviderEnvironmentOptionDto>? EnvironmentOptions = null);

public sealed record ProviderCredentialUpsertRequestDto(
    IReadOnlyDictionary<string, string?>? Credentials,
    string? Environment = null,
    string? RequestedBy = null);

public sealed record ProviderCredentialMutationResultDto(
    string ProviderId,
    ProviderCredentialStateDto CredentialState,
    ProviderCredentialSourceDto CredentialSource,
    ProviderVerificationStateDto VerificationState,
    ProviderContinuityHealthDto Health,
    string? MaskedKeyPreview,
    string? Environment,
    IReadOnlyList<string> Warnings);

public sealed record ProviderCredentialVerificationResultDto(
    string ProviderId,
    bool Success,
    ProviderVerificationStateDto VerificationState,
    ProviderContinuityHealthDto Health,
    DateTimeOffset? LastVerifiedAt,
    string? LastError,
    string? ExternalAccountId,
    IReadOnlyList<string> Warnings);
