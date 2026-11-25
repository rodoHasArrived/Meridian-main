using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderConnectionLifecycleService
{
    public ProviderConnectionLifecycleService ForTemplateSetup(Guid setupId, string tenantId, string actor, string providerId)
    {
        var authority = _ownershipAuthority ??= new Meridian.Application.UI.ConfigStore(_configStore.ConfigPath);
        var scope = ProviderConnectionTemplateService.RequireDraftScope(authority.LoadRequired(), setupId, tenantId, actor, providerId);
        return new ProviderConnectionLifecycleService(this, scope, (setupId, actor));
    }

    public async Task<ProviderTemplateSetupTestResult> TestTemplateSetupAsync(string providerId, string actor, CancellationToken ct)
    {
        if (providerId is "tiingo" or "polygon" or "openfigi")
            return await ProbeTemplateDataProviderAsync(providerId, ct).ConfigureAwait(false);

        var result = await VerifyAsync(providerId, ct, actor).ConfigureAwait(false);
        return result.VerificationState switch
        {
            ProviderVerificationStateDto.Verified when result.Success => new(true,
                "The credential verifier confirmed the connection. Review the checklist for data capability and entitlement checks."),
            ProviderVerificationStateDto.NotRequired when result.Success => new(true,
                "The setup check passed. This provider requires no credentials. Use the checklist to verify the data feed."),
            ProviderVerificationStateDto.NotVerified when result.LastError == ScopedVerificationUnavailable => new(true,
                "The setup check confirmed the capability configuration and vault fields. Use the checklist to verify provider connectivity and data access."),
            _ => new(false, "The connection test failed. Review the account, environment, and credentials, then try again.")
        };
    }

    /// <summary>Application configuration authority over the same path as the workstation store; created once per service.</summary>
    private Meridian.Application.UI.ConfigStore? _ownershipAuthority;

    private async Task<IDisposable> AcquireOwnershipLeaseAsync(string providerId, bool enforceLegacyOwnership, CancellationToken ct)
    {
        // The configuration lease is the cross-process .lock sidecar beside the config file, so it
        // is exclusive for readers and writers alike; there is no shared-read variant to request.
        var authority = _ownershipAuthority ??= new Meridian.Application.UI.ConfigStore(_configStore.ConfigPath);
        var lease = await authority.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ownershipScope is not null)
            {
                // Owned setup can save or remove credentials before a connection is enabled.
                // Its status receipt must remain readable; secret reads and verification still
                // require an enabled connection, as does runtime credential activation.
                var cfg = authority.LoadRequired();
                var current = _templateSetup is { } draft
                    ? ProviderConnectionTemplateService.RequireDraftScope(cfg, draft.SetupId, _ownershipScope.TenantId, draft.Actor, providerId)
                    : ProviderRuntimeCredentialService.RequireScope(cfg, providerId, _ownershipScope.ConnectionId,
                        _ownershipScope.TenantId, requireEnabled: enforceLegacyOwnership);
                if (current != _ownershipScope)
                    throw new UnauthorizedAccessException("Credential ownership changed. Select the current connection again.");
            }
            else if (enforceLegacyOwnership)
            {
                // Missing authority cannot establish an entirely legacy family. In particular,
                // losing configuration after rollback must not activate the restored legacy record.
                var section = authority.LoadRequired().ProviderConnections;
                if (_ownershipPolicy.ProhibitsLegacy(providerId) ||
                    ProviderOwnershipRules.Evaluate(section, providerId).RequiresScopedOwnership)
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

    private async Task<IDisposable> AcquireMutationOwnershipLeaseAsync(string providerId, CancellationToken ct)
    {
        // Draft credentials cannot affect any running provider. The configuration lease still
        // rechecks draft ownership and serializes vault changes with publication and discard.
        if (_templateSetup is not null)
            return await AcquireOwnershipLeaseAsync(providerId, true, ct).ConfigureAwait(false);
        if (_ownershipScope is null && _ownershipPolicy.ProhibitsLegacy(providerId))
            throw new UnauthorizedAccessException("Select an explicitly owned credential connection.");
        var lease = await _mutationGuard.AcquireAsync(providerId, _ownershipScope, ct).ConfigureAwait(false);
        try
        {
            var authority = _ownershipAuthority ??= new Meridian.Application.UI.ConfigStore(_configStore.ConfigPath);
            if (_ownershipScope is null && ProviderOwnershipRules.Evaluate(authority.LoadRequired().ProviderConnections, providerId).RequiresScopedOwnership)
                throw new UnauthorizedAccessException("Select an explicitly owned credential connection.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    // Network calls run outside the configuration lease. Every secret access and conditional
    // result publication rechecks the current owner; ordinary rotation remains concurrent and
    // the vault generation check decides whether a returning provider response is still valid.
    private sealed class OwnershipGuardedCredentialStore(IProviderCredentialStore store,
        Func<string, bool, CancellationToken, Task<IDisposable>> acquire,
        Func<string, CancellationToken, Task<IDisposable>> acquireMutation,
        Func<string, bool> prohibitsLegacy) : IProviderCredentialStore
    {
        public string VaultPath => store.VaultPath;
        public Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
            => prohibitsLegacy(providerId)
                ? Task.FromResult(UnownedStatus(providerId))
                : RunAsync(providerId, false, () => store.GetStatusAsync(providerId, ct), ct);
        public Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default)
            => RunAsync(providerId, true, () => store.ReadForProviderAsync(providerId, ct), ct);
        public Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
            => RunAsync(request.ProviderId, () => store.SaveAsync(request, ct), ct);
        public Task SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
            => RunAsync(request.ProviderId, () => store.SaveRotatedCredentialsAsync(request, ct), ct);
        public Task<string> SaveRotatedCredentialsAsync(ProviderCredentialSaveRequest request,
            ProviderCredentialReadResult expectedConnection, CancellationToken ct = default)
            // A provider-owned OAuth exchange must durably finish while its request drains.
            // The current scope and exact credential generation remain authoritative; taking the
            // operator-mutation gate here would ask an in-flight refresh to drain itself.
            => RunAsync(request.ProviderId, true, () => store.SaveRotatedCredentialsAsync(request, expectedConnection, ct), ct);
        public Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
            => RunAsync(providerId, () => store.DeleteAsync(providerId, actor, ct), ct);
        public Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
            => RunMetadataAsync(update.ProviderId, () => store.RecordVerificationAsync(update, ct), ct);

        private static ProviderCredentialStoreStatus UnownedStatus(string providerId)
        {
            var descriptor = ProviderCredentialCatalog.Find(providerId)!;
            return new(descriptor.ProviderId, descriptor.DisplayName, ProviderCredentialStateDto.Missing,
                ProviderCredentialSourceDto.None, ProviderVerificationStateDto.NotVerified,
                null, null, null, null, null, "Select an explicitly owned credential connection.",
                null, null, null, descriptor.RequiredFields.Select(field => field.Name).ToArray(), [],
                new Dictionary<string, string>());
        }

        private async Task<T> RunAsync<T>(string providerId, bool enforceLegacyOwnership, Func<Task<T>> action, CancellationToken ct)
        {
            using var lease = await acquire(providerId, enforceLegacyOwnership, ct).ConfigureAwait(false);
            return await action().ConfigureAwait(false);
        }

        private async Task RunAsync(string providerId, Func<Task> action, CancellationToken ct)
        {
            using var lease = await acquireMutation(providerId, ct).ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }

        private async Task RunMetadataAsync(string providerId, Func<Task> action, CancellationToken ct)
        {
            using var lease = await acquire(providerId, true, ct).ConfigureAwait(false);
            await action().ConfigureAwait(false);
        }
    }
}
