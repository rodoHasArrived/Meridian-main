namespace Meridian.Contracts.Api;

/// <summary>The authenticated server tenant owns the connection; the request cannot supply an owner.</summary>
public sealed record CreateOwnedProviderConnectionRequest(CreateProviderConnectionRequest Connection, string Environment);

public sealed record BindProviderRuntimeCredentialRequest(string ProviderId);

public sealed record ApplyProviderRuntimeCredentialRequest(string ProviderId);

/// <summary>
/// Whether the retained host binding has reached running provider instances in this host process. A
/// binding changed after host start stays pending until an in-process recycle applies it or the host
/// is deliberately restarted; nothing activates hot.
/// </summary>
public sealed record ProviderRuntimeCredentialApplicationStatus(
    string ProviderId,
    string TenantId,
    string ConnectionId,
    bool IsBound,
    bool PendingRestart,
    DateTimeOffset? BoundAt,
    DateTimeOffset? AppliedAt,
    string Detail);

/// <summary>ExpectedGeneration comes from the non-secret migration preview, never from a secret value.</summary>
public sealed record ProviderCredentialMigrationRequest(string ProviderId, string MigrationId, string? ExpectedGeneration = null);
