using Meridian.Contracts.Configuration;

namespace Meridian.Contracts.Api;

public enum ProviderReadinessStatusDto
{
    Ready,
    Review,
    Degraded,
    Blocked,
    Unknown
}

public enum ProviderReadinessEvidenceKindDto
{
    Credential,
    Connection,
    Validation,
    Degradation,
    Plaid,
    Routing
}

public sealed record ProviderReadinessSummaryDto(
    DateTimeOffset AsOf,
    ProviderReadinessStatusDto Status,
    int TotalProviders,
    int ReadyProviders,
    int ReviewProviders,
    int DegradedProviders,
    int BlockedProviders,
    string Summary,
    string RecommendedAction,
    IReadOnlyList<ProviderReadinessRowDto> Providers);

public sealed record ProviderReadinessRowDto(
    string ProviderId,
    string DisplayName,
    ProviderConnectionCapabilityDto Capability,
    ProviderReadinessStatusDto Status,
    ProviderCredentialStateDto CredentialState,
    ProviderCredentialSourceDto CredentialSource,
    ProviderVerificationStateDto VerificationState,
    ProviderContinuityHealthDto ConnectionHealth,
    bool IsEnabled,
    bool IsConnected,
    bool FallbackActive,
    double? DegradationScore,
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
    IReadOnlyList<ProviderReadinessEvidenceDto> Evidence,
    IReadOnlyList<ProviderRecoveryActionDto> RecoveryActions);

public sealed record ProviderReadinessEvidenceDto(
    ProviderReadinessEvidenceKindDto Kind,
    string Label,
    ProviderReadinessStatusDto Status,
    string Detail,
    DateTimeOffset? ObservedAt = null,
    string? Route = null);

public sealed record ProviderRecoveryActionDto(
    string ActionId,
    string Label,
    string Target,
    bool RequiresMutation,
    string? DisabledReason = null);
