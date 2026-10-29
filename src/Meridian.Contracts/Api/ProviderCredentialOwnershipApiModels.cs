namespace Meridian.Contracts.Api;

/// <summary>The authenticated server tenant owns the connection; the request cannot supply an owner.</summary>
public sealed record CreateOwnedProviderConnectionRequest(CreateProviderConnectionRequest Connection, string Environment);

public sealed record BindProviderRuntimeCredentialRequest(string ProviderId);

/// <summary>ExpectedGeneration comes from the non-secret migration preview, never from a secret value.</summary>
public sealed record ProviderCredentialMigrationRequest(string ProviderId, string MigrationId, string? ExpectedGeneration = null);
