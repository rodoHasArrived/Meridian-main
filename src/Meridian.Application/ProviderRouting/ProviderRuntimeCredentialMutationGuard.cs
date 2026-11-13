using System.Collections.Concurrent;
using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Application.ProviderRouting;

/// <summary>Serializes host selection/application with mutations that could invalidate a running provider's keys.</summary>
public sealed class ProviderRuntimeCredentialMutationGuard(ConfigStore configStore,
    ProviderCredentialOwnershipPolicy? ownershipPolicy = null, IProviderRuntimeCredentialApplier? applier = null,
    ProviderRuntimeCredentialApplicationTracker? tracker = null)
{
    private readonly ProviderCredentialOwnershipPolicy _policy = ownershipPolicy ?? ProviderCredentialOwnershipPolicy.ForCurrentEnvironment();
    private readonly IProviderRuntimeCredentialApplier _applier = applier ?? new NoInProcessProviderRecycleApplier();
    private readonly ProviderRuntimeCredentialApplicationTracker _tracker = tracker ?? new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Operations = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task<IDisposable> AcquireOperationAsync(string providerId, CancellationToken ct)
    {
        if (!_policy.ProhibitsLegacy(providerId))
            return new OperationLease(null);
        var key = Path.GetFullPath(configStore.ConfigPath) + "\n" + ProviderCredentialCatalog.NormalizeProviderId(providerId);
        var gate = Operations.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new OperationLease(gate);
    }

    public async Task<MutationLease> AcquireForConnectionAsync(string providerId, string connectionId, string tenantId, CancellationToken ct)
    {
        var scope = ProviderRuntimeCredentialService.RequireScope(configStore.LoadRequired(), providerId, connectionId, tenantId, requireEnabled: false);
        return await AcquireAsync(providerId, scope, ct).ConfigureAwait(false);
    }

    public async Task<MutationLease> AcquireForSelectionAsync(string providerId, string connectionId, string tenantId, CancellationToken ct)
    {
        var operation = await AcquireOperationAsync(providerId, ct).ConfigureAwait(false);
        return await AcquireCoreAsync(providerId, null, operation, selectHost: true, ct, connectionId, tenantId).ConfigureAwait(false);
    }

    /// <summary>Returns ownership and operation leases held until the vault/configuration mutation completes.</summary>
    public async Task<MutationLease> AcquireAsync(string providerId, ProviderCredentialScope? scope, CancellationToken ct)
    {
        var operation = await AcquireOperationAsync(providerId, ct).ConfigureAwait(false);
        return await AcquireCoreAsync(providerId, scope, operation, selectHost: false, ct).ConfigureAwait(false);
    }

    private async Task<MutationLease> AcquireCoreAsync(string providerId, ProviderCredentialScope? scope,
        IDisposable operation, bool selectHost, CancellationToken ct,
        string? selectionConnectionId = null, string? selectionTenantId = null)
    {
        IDisposable? ownership = null;
        try
        {
            ownership = await configStore.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
            var before = configStore.LoadRequired();
            var binding = (before.ProviderConnections?.RuntimeCredentials ?? []).SingleOrDefault(value => SameProvider(value.ProviderId, providerId));
            if (selectHost)
            {
                ProviderRuntimeCredentialService.RequireScope(before, providerId, selectionConnectionId!, selectionTenantId!);
                if (binding is not null && binding.TenantId != selectionTenantId)
                    throw new UnauthorizedAccessException("Runtime credential ownership belongs to another tenant.");
            }
            // Old unpinned selections cannot construct a provider in this binary, so rebinding
            // establishes their first usable selection rather than claiming a live instance stopped.
            if (_policy.ProhibitsLegacy(providerId) && selectHost && binding?.ExternalAccountId is not null && binding.Environment is not null && binding.SelectionId is not null)
                scope = ProviderRuntimeCredentialService.RequireScope(before, providerId, binding.ConnectionId, binding.TenantId, requireEnabled: false);
            if (scope is not null && ProviderRuntimeCredentialService.RequireScope(before, providerId, scope.ConnectionId, scope.TenantId, requireEnabled: false) != scope)
                throw new UnauthorizedAccessException("Credential ownership changed. Select the current connection again.");
            if (_policy.ProhibitsLegacy(providerId) && scope is not null && binding is not null &&
                string.Equals(binding.ConnectionId, scope.ConnectionId, StringComparison.OrdinalIgnoreCase) && binding.TenantId == scope.TenantId)
            {
                ProviderRuntimeCredentialService.RequireBoundScope(binding, scope);
                // Stop/drain must be real host authority. It runs outside the configuration lease
                // so provider shutdown can finish its own persistence and credential operations.
                ownership.Dispose();
                ownership = null;
                _tracker.MarkBound(providerId);
                if (!await _applier.StopAndDrainAsync(binding, ct).ConfigureAwait(false))
                    throw new ProviderRuntimeCredentialQuiescenceException();
                ownership = await configStore.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
                var current = configStore.LoadRequired();
                if (ProviderRuntimeCredentialService.RequireScope(current, providerId, scope.ConnectionId, scope.TenantId, requireEnabled: false) != scope ||
                    (current.ProviderConnections?.RuntimeCredentials ?? []).SingleOrDefault(value => SameProvider(value.ProviderId, providerId)) != binding)
                    throw new ProviderCredentialConflictException("Credential selection changed while the provider was stopping. Reload before retrying.");
            }
            return new MutationLease(ownership, operation);
        }
        catch
        {
            ownership?.Dispose();
            operation.Dispose();
            throw;
        }
    }

    private static bool SameProvider(string first, string second)
        => string.Equals(ProviderCredentialCatalog.NormalizeProviderId(first), ProviderCredentialCatalog.NormalizeProviderId(second), StringComparison.OrdinalIgnoreCase);

    public sealed class MutationLease(IDisposable ownership, IDisposable operation) : IDisposable
    {
        private IDisposable? _ownership = ownership;
        private IDisposable? _operation = operation;
        // Configuration writers acquire their own lease. Keep the operation gate across that
        // commit so an Apply/Bind in this host cannot resurrect an instance between drain and write.
        public void ReleaseConfigurationLease() => Interlocked.Exchange(ref _ownership, null)?.Dispose();
        public void Dispose()
        {
            ReleaseConfigurationLease();
            Interlocked.Exchange(ref _operation, null)?.Dispose();
        }
    }

    private sealed class OperationLease(SemaphoreSlim? gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

/// <summary>A bound secret cannot change until its running instances are proven stopped and drained.</summary>
public sealed class ProviderRuntimeCredentialQuiescenceException : InvalidOperationException
{
    public ProviderRuntimeCredentialQuiescenceException() : base(
        "Running provider instances cannot be stopped and drained by this host. Stop the host and perform credential maintenance offline before restarting; the requested change was refused.")
    { }
}
