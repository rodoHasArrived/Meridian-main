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
