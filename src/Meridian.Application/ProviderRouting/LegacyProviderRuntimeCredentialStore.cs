using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// Contains runtimes that do not yet carry authenticated connection ownership. Every vault access
/// rechecks that the provider family is entirely legacy while holding the configuration lease.
/// </summary>
public sealed class LegacyProviderRuntimeCredentialStore(ConfigStore configStore, IProviderCredentialStore credentialStore,
    ProviderCredentialOwnershipPolicy? ownershipPolicy = null)
    : IProviderCredentialStore
{
    public string VaultPath => credentialStore.VaultPath;

    public Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
        => RunAsync(providerId, () => credentialStore.GetStatusAsync(providerId, ct), ct);

    public Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default)
        => RunAsync(providerId, () => credentialStore.ReadForProviderAsync(providerId, ct), ct);

    public Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
        => RunAsync(request.ProviderId, () => credentialStore.SaveAsync(request, ct), ct);

    public Task SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
        => RunAsync(request.ProviderId, () => credentialStore.SaveRotatedCredentialsAsync(request, ct), ct);

    public Task<string> SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request,
        ProviderCredentialReadResult expectedConnection, CancellationToken ct = default)
        => RunAsync(request.ProviderId, () => credentialStore.SaveRotatedCredentialsAsync(request, expectedConnection, ct), ct);

    public Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
        => RunAsync(providerId, () => credentialStore.DeleteAsync(providerId, actor, ct), ct);

    public Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
        => RunAsync(update.ProviderId, () => credentialStore.RecordVerificationAsync(update, ct), ct);

    private async Task<T> RunAsync<T>(string providerId, Func<Task<T>> action, CancellationToken ct)
    {
        using var lease = await configStore.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
        RequireLegacy(providerId);
        return await action().ConfigureAwait(false);
    }

    private async Task RunAsync(string providerId, Func<Task> action, CancellationToken ct)
    {
        using var lease = await configStore.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
        RequireLegacy(providerId);
        await action().ConfigureAwait(false);
    }

    private void RequireLegacy(string providerId)
    {
        // Missing or corrupt configuration remains an error, never evidence of legacy ownership.
        var config = configStore.LoadRequired();
        RequireLegacy(config, providerId);
    }

    private void RequireLegacy(AppConfig config, string providerId)
    {
        if ((ownershipPolicy ?? ProviderCredentialOwnershipPolicy.ForCurrentEnvironment()).ProhibitsLegacy(providerId))
            throw new ProviderCredentialOwnershipRequiredException();
        ProviderCredentialScope? scope;
        try
        {
            scope = ProviderRuntimeCredentialService.ResolveScope(config, providerId);
        }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new ProviderCredentialOwnershipRequiredException();
        }
        if (scope is not null)
            throw new ProviderCredentialOwnershipRequiredException();
    }
}
