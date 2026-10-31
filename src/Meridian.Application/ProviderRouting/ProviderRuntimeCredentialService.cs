using Meridian.Application.UI;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Application.ProviderRouting;

/// <summary>Explicit host credential selection and ownership-checked migration commands.</summary>
public sealed class ProviderRuntimeCredentialService(ConfigStore configStore, IProviderCredentialStore credentialStore,
    ILogger<ProviderRuntimeCredentialService>? logger = null)
{
    private readonly ILogger<ProviderRuntimeCredentialService> _logger = logger ?? NullLogger<ProviderRuntimeCredentialService>.Instance;

    public async Task<ProviderCredentialStoreStatus> GetMigrationPreviewAsync(string providerId, string connectionId,
        string tenantId, CancellationToken ct = default)
    {
        // The preview is a read, but ownership must be checked against configuration that cannot
        // change underneath the vault status read. The configuration lease is an exclusive
        // cross-process file lock with no shared-read mode, so the write lease is the only option.
        using var lease = await configStore.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
        var provider = RequireProvider(providerId);
        RequireScope(configStore.LoadRequired(), provider, connectionId, tenantId);
        return await credentialStore.GetStatusAsync(provider, ct).ConfigureAwait(false);
    }

    public async Task<ProviderRuntimeCredentialBinding> BindAsync(string providerId, string connectionId, string tenantId,
        CancellationToken ct = default, string? actor = null)
    {
        var bound = await configStore.UpdateRequiredAsync(config =>
        {
            var provider = RequireProvider(providerId);
            RequireScope(config, provider, connectionId, tenantId);
            var section = config.ProviderConnections!;
            var bindings = section.RuntimeCredentials ?? [];
            var existing = bindings.Where(binding => SameProvider(binding.ProviderId, provider)).ToArray();
            if (existing.Length > 1 || existing.Any(binding => binding.TenantId != tenantId))
                throw new UnauthorizedAccessException("Runtime credential ownership is ambiguous or belongs to another tenant.");
            var next = new ProviderRuntimeCredentialBinding(provider, tenantId, connectionId);
            // ResolveScope applies the same identity and enabled-state checks used by host construction.
            var updated = config with
            {
                ProviderConnections = section with
                {
                    RuntimeCredentials = bindings.Where(binding => !SameProvider(binding.ProviderId, provider)).Append(next).ToArray()
                }
            };
            ResolveScope(updated, provider);
            return (updated, next);
        }, ct).ConfigureAwait(false);
        _logger.LogInformation("Bound host runtime credentials for provider {ProviderId} to connection {ConnectionId} in tenant {TenantId} by {Actor}.",
            bound.ProviderId, bound.ConnectionId, bound.TenantId, actor ?? "unattributed");
        return bound;
    }

    public async Task<ProviderCredentialMigrationReceipt> TransferAsync(string providerId, string connectionId, string tenantId,
        string migrationId, string? expectedGeneration, string actor, bool rollback, CancellationToken ct = default)
    {
        using var lease = await configStore.AcquireWriteLeaseAsync(ct).ConfigureAwait(false);
        var provider = RequireProvider(providerId);
        var scope = RequireScope(configStore.LoadRequired(), provider, connectionId, tenantId);
        var migration = credentialStore as IProviderCredentialOwnershipMigration
            ?? throw new NotSupportedException("Credential vault does not support explicit ownership migration.");
        if (rollback)
        {
            var rolledBack = await migration.RollbackMigrationAsync(provider, scope, migrationId, actor, ct).ConfigureAwait(false);
            _logger.LogInformation("Rolled back credential ownership migration {MigrationId} for provider {ProviderId} from connection {ConnectionId} in tenant {TenantId} by {Actor}.",
                migrationId, provider, connectionId, tenantId, actor);
            return rolledBack;
        }
        if (string.IsNullOrWhiteSpace(expectedGeneration))
            throw new ArgumentException("Migration requires the expected credential generation of the record being transferred.", nameof(expectedGeneration));
        var migrated = await migration.MigrateToScopeAsync(provider, scope, migrationId, expectedGeneration, actor, ct).ConfigureAwait(false);
        _logger.LogInformation("Migrated credential ownership {MigrationId} for provider {ProviderId} to connection {ConnectionId} in tenant {TenantId} at generation {CredentialGeneration} by {Actor}.",
            migrationId, provider, connectionId, tenantId, expectedGeneration, actor);
        return migrated;
    }

    /// <summary>Null is permitted only for an entirely legacy provider family, never a partial owner or dangling default.</summary>
    public static ProviderCredentialScope? ResolveScope(AppConfig config, string providerId)
    {
        var provider = RequireProvider(providerId);
        var section = config.ProviderConnections;
        var ownership = ProviderOwnershipRules.Evaluate(section, provider);
        if (ownership.RuntimeBindingCount == 1)
        {
            var binding = (section?.RuntimeCredentials ?? []).Single(binding => SameProvider(binding.ProviderId, provider));
            return RequireScope(config, provider, binding.ConnectionId, binding.TenantId);
        }
        if (ownership.RequiresScopedOwnership)
            throw new InvalidOperationException("An explicit, unambiguous runtime credential connection is required.");
        return null;
    }

    public static ProviderCredentialScope RequireScope(AppConfig config, string providerId, string connectionId, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        var provider = RequireProvider(providerId);
        var matches = (config.ProviderConnections?.Connections ?? []).Where(connection =>
            string.Equals(connection.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var connection = matches.Length == 1 ? matches[0] : null;
        if (connection is null || !connection.Enabled || connection.TenantId != tenantId ||
            !SameProvider(connection.ProviderFamilyId, provider) || string.IsNullOrWhiteSpace(connection.ExternalAccountId) ||
            string.IsNullOrWhiteSpace(connection.CredentialEnvironment))
            throw new UnauthorizedAccessException("Credential connection ownership could not be established.");
        var scope = new ProviderCredentialScope(tenantId, connection.ConnectionId, connection.ExternalAccountId, connection.CredentialEnvironment);
        if (ProviderCredentialCatalog.Find(provider)!.NormalizeEnvironment(scope.Environment) != scope.Environment)
            throw new InvalidDataException("Retained credential environment is not canonical.");
        return scope;
    }

    private static string RequireProvider(string providerId)
        => ProviderCredentialCatalog.Find(providerId)?.ProviderId ?? throw new ArgumentException("Provider is not credential-managed.", nameof(providerId));

    private static bool SameProvider(string candidate, string provider)
        => string.Equals(ProviderCredentialCatalog.NormalizeProviderId(candidate), provider, StringComparison.OrdinalIgnoreCase);
}
