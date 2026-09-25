using Meridian.Contracts.Configuration;

namespace Meridian.DataIntegration.Credentials;

public interface IProviderCredentialStore
{
    string VaultPath { get; }

    Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default);

    Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default);

    Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default);

    Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default);

    Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default);
}

/// <summary>Atomic, insert-only import of a complete historical credential sidecar.</summary>
public interface ILegacyProviderCredentialImporter
{
    Task ImportLegacyAsync(IReadOnlyList<ProviderCredentialSaveRequest> requests, CancellationToken ct = default);
}

public sealed record ProviderCredentialSaveRequest(
    string ProviderId,
    IReadOnlyDictionary<string, string?> Credentials,
    string? Environment = null,
    string? Actor = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed class ProviderCredentialValidationException : Exception
{
    public ProviderCredentialValidationException(string providerId, IReadOnlyList<string> unknownFields)
        : base(BuildMessage(providerId, unknownFields))
    {
        ProviderId = providerId;
        UnknownFields = unknownFields;
    }

    public string ProviderId { get; }

    public IReadOnlyList<string> UnknownFields { get; }

    private static string BuildMessage(string providerId, IReadOnlyList<string> unknownFields)
        => unknownFields.Count == 0
            ? $"Credential fields are invalid for provider '{providerId}'."
            : $"Credential fields are not recognized for provider '{providerId}': {string.Join(", ", unknownFields)}.";
}

public sealed record ProviderCredentialVerificationUpdate(
    string ProviderId,
    bool Success,
    string? ErrorMessage = null,
    string? ExternalAccountId = null,
    DateTimeOffset? VerifiedAt = null,
    string? Actor = null);

public sealed record ProviderCredentialStoreStatus(
    string ProviderId,
    string DisplayName,
    ProviderCredentialStateDto CredentialState,
    ProviderCredentialSourceDto CredentialSource,
    ProviderVerificationStateDto VerificationState,
    DateTimeOffset? SavedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastSuccessfulAt,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    string? MaskedKeyPreview,
    string? Environment,
    string? ExternalAccountId,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> PresentFields,
    IReadOnlyDictionary<string, string> AuditMetadata);

public sealed record ProviderCredentialReadResult(
    string ProviderId,
    ProviderCredentialSourceDto Source,
    IReadOnlyDictionary<string, string> Credentials,
    string? Environment,
    string? ExternalAccountId,
    DateTimeOffset? SavedAt,
    DateTimeOffset? LastVerifiedAt,
    DateTimeOffset? LastSuccessfulAt,
    DateTimeOffset? LastFailureAt,
    string? LastError,
    IReadOnlyDictionary<string, string> AuditMetadata)
{
    public string? Get(string fieldName)
        => Credentials.TryGetValue(fieldName, out var value) ? value : null;
}


/// <summary>Explicit ownership boundary for provider secrets; values must come from trusted routing context.</summary>
public sealed record ProviderCredentialScope
{
    public ProviderCredentialScope(string tenantId, string connectionId, string externalAccountId, string environment)
    {
        TenantId = RequireIdentity(tenantId, nameof(tenantId));
        ConnectionId = RequireIdentity(connectionId, nameof(connectionId));
        ExternalAccountId = RequireIdentity(externalAccountId, nameof(externalAccountId));
        Environment = RequireIdentity(environment, nameof(environment)).ToLowerInvariant();
    }
    public string TenantId { get; }
    public string ConnectionId { get; }
    public string ExternalAccountId { get; }
    public string Environment { get; }

    internal string StorageKey(string providerId)
    {
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new[]
            { providerId, TenantId, ConnectionId, ExternalAccountId, Environment });
        // Preserve the original storage-key encoding so existing scoped OAuth keys remain readable.
        // This is a vault identity, not a canonical digest field in an evidence contract.
        return providerId + "@scope:" + Meridian.Contracts.Integrity.Sha256Digest.Compute(payload).ToUpperInvariant();
    }

    private static string RequireIdentity(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        var normalized = value.Trim();
        if (normalized.Length > 512 || normalized.Any(char.IsControl))
            throw new ArgumentException("Credential scope identity is invalid.", name);
        return normalized;
    }
}

/// <summary>Scope-bound access never falls back to an unscoped record or process environment.</summary>
public interface IScopedProviderCredentialStore : IProviderCredentialStore
{
    Task<ProviderCredentialStoreStatus> GetScopedStatusAsync(string providerId, ProviderCredentialScope scope, CancellationToken ct = default);
    Task<ProviderCredentialReadResult?> ReadScopedAsync(string providerId, ProviderCredentialScope scope, CancellationToken ct = default);
    Task SaveScopedAsync(ProviderCredentialSaveRequest request, ProviderCredentialScope scope, CancellationToken ct = default);
    Task DeleteScopedAsync(string providerId, ProviderCredentialScope scope, string? actor = null, CancellationToken ct = default);
    Task RecordScopedVerificationAsync(ProviderCredentialVerificationUpdate update, ProviderCredentialScope scope, CancellationToken ct = default);
}
