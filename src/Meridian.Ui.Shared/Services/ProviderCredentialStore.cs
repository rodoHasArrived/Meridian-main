using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compatibility adapter from provider-module setup to the Data Integration-owned encrypted vault.
/// A plaintext sidecar retained from earlier releases is never read or imported; it is only kept
/// owner-only. Unknown provider ids fail on writes and never create a sidecar.
/// </summary>
public sealed class ProviderCredentialStore : Meridian.Core.Contracts.IProviderCredentialStore
{
    private readonly IProviderCredentialStore _vault;
    private readonly string _retainedSidecarPath;
    private volatile bool _sidecarRestricted;

    public ProviderCredentialStore(string dataRoot)
        : this(new FileProviderCredentialStore(dataRoot), dataRoot)
    {
    }

    public ProviderCredentialStore(IProviderCredentialStore vault, string dataRoot)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _retainedSidecarPath = Path.Combine(Path.GetFullPath(dataRoot), "provider-credentials.json");
    }

    public async Task SaveCredentialsAsync(
        string moduleId,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(values);
        RestrictRetainedSidecar();
        RequireKnownProvider(moduleId);
        await _vault.SaveAsync(
            new ProviderCredentialSaveRequest(
                moduleId,
                values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase),
                Actor: "provider-module-setup",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["credentialOwner"] = "Meridian.DataIntegration",
                    ["compatibilitySource"] = "provider-module-setup"
                }),
            ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCredentialsAsync(
        string moduleId,
        CancellationToken ct = default)
        => await ReadCredentialRecordAsync(moduleId, ct).ConfigureAwait(false)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyDictionary<string, string>?> ReadCredentialRecordAsync(
        string moduleId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        RestrictRetainedSidecar();
        if (ProviderCredentialCatalog.Find(moduleId) is null)
        {
            return null;
        }

        var result = await _vault.ReadForProviderAsync(moduleId, ct).ConfigureAwait(false);
        return result?.Credentials;
    }

    public async Task<IReadOnlySet<string>> GetStoredKeyNamesAsync(
        string moduleId,
        CancellationToken ct = default)
    {
        var credentials = await GetCredentialsAsync(moduleId, ct).ConfigureAwait(false);
        return credentials
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task DeleteCredentialsAsync(string moduleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        RestrictRetainedSidecar();
        if (ProviderCredentialCatalog.Find(moduleId) is not null)
        {
            await _vault.DeleteAsync(moduleId, "provider-module-setup", ct).ConfigureAwait(false);
        }
    }

    private void RestrictRetainedSidecar()
    {
        if (_sidecarRestricted)
        {
            return;
        }

        // Idempotent, so concurrent first callers are harmless. A failure propagates and is retried.
        RetainedCredentialFileAccess.RestrictAccess(_retainedSidecarPath);
        _sidecarRestricted = true;
    }

    private static void RequireKnownProvider(string providerId)
    {
        if (ProviderCredentialCatalog.Find(providerId) is null)
        {
            throw new InvalidOperationException(
                $"Provider '{providerId}' has no encrypted credential-vault descriptor; plaintext fallback is prohibited.");
        }
    }

}
