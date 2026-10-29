using System.Text.Json;
using System.Text.Json.Nodes;
using Meridian.Contracts.Configuration;

namespace Meridian.DataIntegration.Credentials;

public sealed partial class FileProviderCredentialStore
{
    public Task<ProviderCredentialMigrationReceipt> MigrateToScopeAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string expectedGeneration, string actor, CancellationToken ct = default)
        => TransferOwnershipAsync(providerId, scope, migrationId, expectedGeneration, actor, rollback: false, ct);

    public Task<ProviderCredentialMigrationReceipt> RollbackMigrationAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string actor, CancellationToken ct = default)
        => TransferOwnershipAsync(providerId, scope, migrationId, null, actor, rollback: true, ct);

    private async Task<ProviderCredentialMigrationReceipt> TransferOwnershipAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string? expectedGeneration, string actor, bool rollback, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if (migrationId.Length > 128 || migrationId.Any(char.IsControl))
            throw new ArgumentException("Migration identity is invalid.", nameof(migrationId));
        var descriptor = RequireDescriptor(providerId);
        if (descriptor.NormalizeEnvironment(scope.Environment) != scope.Environment)
            throw new InvalidDataException("Migration requires a canonical credential environment.");
        if (!rollback)
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedGeneration);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            vault.OwnershipMigrations.TryGetValue(migrationId, out var receipt);
            if (receipt is not null)
            {
                if (receipt.ProviderId != descriptor.ProviderId || receipt.Scope != scope ||
                    (!rollback && receipt.CredentialGeneration != expectedGeneration))
                    throw new ProviderCredentialConflictException();
                // A completed command is an immutable receipt, including after subsequent rotation.
                // Retrying migration after rollback never re-applies an old transfer.
                if (!rollback || receipt.RolledBack)
                    return receipt;
            }
            else if (rollback)
                throw new ProviderCredentialConflictException();

            var scopedKey = scope.StorageKey(descriptor.ProviderId);
            var sourceKey = rollback ? scopedKey : descriptor.ProviderId;
            var targetKey = rollback ? descriptor.ProviderId : scopedKey;
            if (vault.Providers.ContainsKey(targetKey) || !vault.Providers.TryGetValue(sourceKey, out var record))
                throw new ProviderCredentialConflictException();
            ValidateRecordScope(record, rollback ? scope : null);
            var generation = record.Metadata.GetValueOrDefault("credentialGeneration");
            if (string.IsNullOrWhiteSpace(generation) || generation != (rollback ? receipt!.CredentialGeneration : expectedGeneration))
                throw new ProviderCredentialConflictException();
            if (descriptor.NormalizeEnvironment(record.Environment) != scope.Environment ||
                (!string.IsNullOrWhiteSpace(record.ExternalAccountId) && record.ExternalAccountId != scope.ExternalAccountId))
                throw new InvalidDataException("Legacy credentials do not match the reviewed connection account and environment.");
            // Separate OAuth tokens require their own provider-specific transfer; never silently
            // leave a refreshable provider-wide token active alongside a newly owned record.
            if (vault.OAuthTokens.Keys.Any(key => ProviderCredentialCatalog.NormalizeProviderId(key) == descriptor.ProviderId))
                throw new InvalidOperationException("Remove or migrate the separate legacy OAuth token before transferring ownership.");

            receipt = rollback
                ? receipt! with { RolledBack = true, RolledBackBy = actor, RolledBackAt = DateTimeOffset.UtcNow }
                : new ProviderCredentialMigrationReceipt(migrationId, descriptor.ProviderId, scope, generation, actor, DateTimeOffset.UtcNow);
            record.Scope = rollback ? null : scope;
            record.Environment = scope.Environment;
            // A transferred secret is unverified under its new ownership. Never carry a previous
            // account verification into the newly selected connection or its rollback.
            record.ExternalAccountId = rollback ? null : scope.ExternalAccountId;
            record.LastVerifiedAt = null;
            record.LastSuccessfulAt = null;
            record.Metadata["verificationRequired"] = "true";
            vault.Providers.Remove(sourceKey);
            vault.Providers[targetKey] = record;
            vault.LegacyImportedProviderIds.Add(descriptor.ProviderId);
            vault.OwnershipMigrations[migrationId] = receipt;
            var action = rollback ? "ownership-rollback" : "ownership-migrate";
            var audit = JsonNode.Parse(BuildAuditLine(descriptor, action, actor,
                BuildStatus(descriptor, ToReadResult(descriptor, record, ProviderCredentialSourceDto.LocalEncryptedStore)),
                record.Fields.Keys.ToArray(), scope))!.AsObject();
            audit["migrationId"] = migrationId;
            await CommitWithAuditAsync(vault, audit.ToJsonString(JsonOptions),
                // Both recovery generations must contain the transfer, never the old unscoped copy.
                () => WriteVaultAsync(vault, ct, discardPreviousGeneration: true), descriptor.ProviderId, action,
                new AuditCommitCheck("provider-save", targetKey, generation, null), ct).ConfigureAwait(false);
            return receipt;
        }
        finally
        {
            _gate.Release();
        }
    }
}
