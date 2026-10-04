using System.Text.Json;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Compatibility adapter from provider-module setup to the Data Integration-owned encrypted vault.
/// On first access it migrates the historical plaintext sidecar atomically into the vault and then
/// removes the plaintext source. Unknown provider ids fail on writes and never create a sidecar.
/// </summary>
public sealed class ProviderCredentialStore : Meridian.Core.Contracts.IProviderCredentialStore
{
    private static readonly JsonSerializerOptions LegacyJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IProviderCredentialStore _vault;
    private readonly string _legacyPath;
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private bool _migrationChecked;

    public ProviderCredentialStore(string dataRoot)
        : this(new FileProviderCredentialStore(dataRoot), dataRoot)
    {
    }

    public ProviderCredentialStore(IProviderCredentialStore vault, string dataRoot)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _legacyPath = Path.Combine(Path.GetFullPath(dataRoot), "provider-credentials.json");
    }

    public async Task SaveCredentialsAsync(
        string moduleId,
        IReadOnlyDictionary<string, string> values,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(values);
        await EnsureLegacyMigratedAsync(ct).ConfigureAwait(false);
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        await EnsureLegacyMigratedAsync(ct).ConfigureAwait(false);
        if (ProviderCredentialCatalog.Find(moduleId) is null)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = await _vault.ReadForProviderAsync(moduleId, ct).ConfigureAwait(false);
        return result?.Credentials ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
        await EnsureLegacyMigratedAsync(ct).ConfigureAwait(false);
        if (ProviderCredentialCatalog.Find(moduleId) is not null)
        {
            await _vault.DeleteAsync(moduleId, "provider-module-setup", ct).ConfigureAwait(false);
        }
    }

    private async Task EnsureLegacyMigratedAsync(CancellationToken ct)
    {
        if (_migrationChecked)
        {
            return;
        }

        await _migrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_migrationChecked)
            {
                return;
            }

            await LegacyCredentialFileMigration.MigrateAsync(_legacyPath, async (json, migrationToken) =>
            {
                var legacy = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                    json, LegacyJsonOptions) ?? [];
                if (_vault is not ILegacyProviderCredentialImporter importer)
                    throw new InvalidOperationException("Credential vault does not support atomic legacy migration.");

                var requests = legacy.Select(pair => new ProviderCredentialSaveRequest(
                    pair.Key,
                    pair.Value.ToDictionary(field => field.Key, field => (string?)field.Value, StringComparer.OrdinalIgnoreCase),
                    Actor: "credential-vault-migration",
                    Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["migratedFrom"] = "provider-credentials.json",
                        ["credentialOwner"] = "Meridian.DataIntegration"
                    })).ToArray();
                await importer.ImportLegacyAsync(requests, migrationToken).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            _migrationChecked = true;
        }
        finally
        {
            _migrationGate.Release();
        }
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
