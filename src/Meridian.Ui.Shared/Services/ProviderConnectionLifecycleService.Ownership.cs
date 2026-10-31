using Meridian.Application.ProviderRouting;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderConnectionLifecycleService
{
    private async Task<IDisposable> AcquireOwnershipLeaseAsync(string providerId, bool enforceLegacyOwnership, CancellationToken ct)
    {
        var authority = new Meridian.Application.UI.ConfigStore(_configStore.ConfigPath);
        var lease = await authority.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ownershipScope is not null)
            {
                var current = ProviderRuntimeCredentialService.RequireScope(authority.LoadRequired(), providerId,
                    _ownershipScope.ConnectionId, _ownershipScope.TenantId);
                if (current != _ownershipScope)
                    throw new UnauthorizedAccessException("Credential ownership changed. Select the current connection again.");
            }
            else if (enforceLegacyOwnership)
            {
                // Missing authority cannot establish an entirely legacy family. In particular,
                // losing configuration after rollback must not activate the restored legacy record.
                var section = authority.LoadRequired().ProviderConnections;
                if (ProviderOwnershipRules.Evaluate(section, providerId).RequiresScopedOwnership)
                    throw new UnauthorizedAccessException("Select an explicitly owned credential connection.");
            }
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    // Network calls run outside the configuration lease. Every secret access and conditional
    // result publication rechecks the current owner; ordinary rotation remains concurrent and
    // the vault generation check decides whether a returning provider response is still valid.
    private sealed class OwnershipGuardedCredentialStore(IProviderCredentialStore store,
        Func<string, bool, CancellationToken, Task<IDisposable>> acquire) : IProviderCredentialStore
    {
        public string VaultPath => store.VaultPath;
        public Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
            => RunAsync(providerId, false, () => store.GetStatusAsync(providerId, ct), ct);
        public Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default)
            => RunAsync(providerId, true, () => store.ReadForProviderAsync(providerId, ct), ct);
        public Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
            => RunAsync(request.ProviderId, () => store.SaveAsync(request, ct), ct);
        public Task SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
            => RunAsync(request.ProviderId, () => store.SaveRotatedCredentialsAsync(request, ct), ct);
        public Task<string> SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request,
            ProviderCredentialReadResult expectedConnection, CancellationToken ct = default)
            => RunAsync(request.ProviderId, true, () => store.SaveRotatedCredentialsAsync(request, expectedConnection, ct), ct);
        public Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
            => RunAsync(providerId, () => store.DeleteAsync(providerId, actor, ct), ct);
        public Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
            => RunAsync(update.ProviderId, () => store.RecordVerificationAsync(update, ct), ct);

        private async Task<T> RunAsync<T>(string providerId, bool enforceLegacyOwnership, Func<Task<T>> action, CancellationToken ct)
        {
            using var lease = await acquire(providerId, enforceLegacyOwnership, ct).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        private async Task RunAsync(string providerId, Func<Task> action, CancellationToken ct)
        {
            using var lease = await acquire(providerId, true, ct).ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }
    }
}
