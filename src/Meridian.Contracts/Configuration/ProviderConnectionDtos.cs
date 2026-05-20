using System.Text.Json.Serialization;

namespace Meridian.Contracts.Configuration;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderConnectionCapabilityDto
{
    Data,
    Brokerage,
    DataAndBrokerage
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
    string ActionHref);

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
