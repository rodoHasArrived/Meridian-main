using Meridian.Contracts.Configuration;

namespace Meridian.Contracts.Api;

/// <summary>
/// Request to configure a provider through the legacy provider setup route.
/// Secrets in this payload must be persisted only through the provider credential store.
/// </summary>
public sealed record ProviderSetupRequest(
    string? Kind,
    string? DisplayName,
    string? ApiKey,
    string? ApiSecret,
    string? Endpoint,
    string[]? Capabilities,
    string? Environment = null);

/// <summary>
/// Safe provider setup result. This response never includes raw provider credentials.
/// </summary>
public sealed record ProviderSetupResult(
    bool Success,
    string? ProviderId,
    string ProviderName,
    string Message,
    string? Error,
    string? ConnectionId = null,
    string[]? BindingIds = null,
    ProviderCredentialStateDto? CredentialState = null,
    ProviderCredentialSourceDto? CredentialSource = null,
    string? CredentialReference = null,
    string? Environment = null,
    string[]? Warnings = null);
