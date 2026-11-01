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

    public async Task<ProviderCredentialMigrationReceipt> FinalizeMigrationAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        var descriptor = RequireDescriptor(providerId);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var vaultLock = await AcquireVaultLockAsync(ct).ConfigureAwait(false);
            var vault = await LoadVaultAsync(ct).ConfigureAwait(false);
            if (!vault.OwnershipMigrations.TryGetValue(migrationId, out var receipt)
                || receipt.ProviderId != descriptor.ProviderId || receipt.Scope != scope)
                throw new ProviderCredentialConflictException();
            if (receipt.RolledBack)
                throw new InvalidOperationException("A rolled-back migration has nothing to finalize.");
            if (receipt.FinalizedAt is not null)
            {
                // The first attempt may have stopped after publishing the primary generation; a retry
                // restores the recovery copy from current bytes without another finalization or audit event.
                await RepairCurrentRecoveryGenerationAsync(ct).ConfigureAwait(false);
                return receipt;
            }
            if (!vault.Providers.TryGetValue(scope.StorageKey(descriptor.ProviderId), out var record) || record.Scope != scope)
                throw new ProviderCredentialConflictException();
            // Finalization needs proof the secret works under its new ownership: a successful verification
            // recorded after the migration, and no later failure or save that re-flagged verification.
            if (record.LastSuccessfulAt is not { } verified || verified < receipt.MigratedAt
                || record.Metadata.GetValueOrDefault("verificationRequired") == "true")
                throw new InvalidOperationException("Finalize requires a successful verification of the owned record after the migration; verify the selected connection first.");
            if (receipt.OAuthTokenTransferred && !vault.ScopedOAuthTokens.Values.Any(token => token.Scope == scope
                    && ProviderCredentialCatalog.NormalizeProviderId(token.ProviderName) == descriptor.ProviderId))
                throw new InvalidOperationException("The transferred OAuth token is no longer held under the owned scope; finalize is refused.");
            receipt = receipt with { FinalizedBy = actor, FinalizedAt = DateTimeOffset.UtcNow };
            vault.OwnershipMigrations[migrationId] = receipt;
            var audit = JsonNode.Parse(BuildAuditLine(descriptor, "ownership-finalize", actor,
                BuildStatus(descriptor, ToReadResult(descriptor, record, ProviderCredentialSourceDto.LocalEncryptedStore)),
                record.Fields.Keys.ToArray(), scope))!.AsObject();
            audit["migrationId"] = migrationId;
            await CommitWithAuditAsync(vault, audit.ToJsonString(JsonOptions),
                // Both recovery generations must hold the finalized receipt; a backup still carrying the
                // active receipt would let primary recovery reopen rollback of a finalized migration.
                () => WriteVaultAsync(vault, ct, discardPreviousGeneration: true), descriptor.ProviderId, "ownership-finalize",
                new AuditCommitCheck("ownership-finalize", migrationId, null, null), ct).ConfigureAwait(false);
            return receipt;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Moves the provider-wide OAuth token under <paramref name="scope"/> (or back on rollback). Returns whether a
    /// token moved. Two provider-wide tokens for one provider, or a token already held by the target, are refused.
    /// </summary>
    private static bool TransferOAuthToken(ProviderCredentialVault vault, string providerId, ProviderCredentialScope scope, bool rollback)
    {
        if (!rollback)
        {
            var keys = vault.OAuthTokens.Keys.Where(key => ProviderCredentialCatalog.NormalizeProviderId(key) == providerId).ToArray();
            if (keys.Length == 0)
                return false;
            if (keys.Length > 1)
                throw new InvalidOperationException("More than one provider-wide OAuth token names this provider; resolve the duplicate before transferring ownership.");
            // The target is occupied when the owned scope already holds a token for this provider under
            // any spelling; two tokens for one provider under one owner would make rollback refuse.
            if (vault.ScopedOAuthTokens.Values.Any(token => token.Scope == scope
                    && ProviderCredentialCatalog.NormalizeProviderId(token.ProviderName) == providerId))
                throw new ProviderCredentialConflictException();
            var scopedKey = scope.StorageKey(keys[0]);
            vault.ScopedOAuthTokens[scopedKey] = new ScopedOAuthVaultRecord { ProviderName = keys[0], Scope = scope, Token = vault.OAuthTokens[keys[0]] };
            vault.OAuthTokens.Remove(keys[0]);
            return true;
        }
        var owned = vault.ScopedOAuthTokens.Where(pair => pair.Value.Scope == scope
            && ProviderCredentialCatalog.NormalizeProviderId(pair.Value.ProviderName) == providerId).ToArray();
        if (owned.Length != 1)
            throw new InvalidOperationException("The transferred OAuth token is no longer held under the owned scope; rollback is refused.");
        var (key, record) = (owned[0].Key, owned[0].Value);
        // The provider-wide destination is occupied by any token naming this provider, however spelled.
        if (record.Token is null || vault.OAuthTokens.Keys.Any(name => ProviderCredentialCatalog.NormalizeProviderId(name) == providerId))
            throw new ProviderCredentialConflictException();
        vault.OAuthTokens[record.ProviderName] = record.Token;
        vault.ScopedOAuthTokens.Remove(key);
        return true;
    }

    /// <summary>
    /// True while a provider-wide OAuth token for <paramref name="providerName"/> has been transferred to an owned
    /// connection and not rolled back: the owned scope holds the token, and no provider-wide token for the provider
    /// may be saved or imported beside it, whatever runtime still caches the transferred value.
    /// </summary>
    private static bool IsOAuthTokenTransferred(ProviderCredentialVault vault, string providerName)
    {
        var providerId = ProviderCredentialCatalog.NormalizeProviderId(providerName);
        return vault.OwnershipMigrations.Values.Any(migration => migration.ProviderId == providerId && migration.OAuthTokenTransferred && !migration.RolledBack)
            && vault.ScopedOAuthTokens.Values.Any(token => ProviderCredentialCatalog.NormalizeProviderId(token.ProviderName) == providerId);
    }

    private static ProviderCredentialConflictException TransferredOAuthTokenConflict(string providerName)
        => new($"The provider-wide OAuth token for {providerName} was transferred to an owned connection; provider-wide token saves are refused so a cached runtime cannot republish it. Roll back the migration to restore provider-wide ownership.");

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
                {
                    // The first attempt may have stopped after publishing the primary receipt.
                    // Repair from current bytes, including later replacements, never the old secret.
                    await RepairCurrentRecoveryGenerationAsync(ct).ConfigureAwait(false);
                    return receipt;
                }
            }
            else if (rollback)
                throw new ProviderCredentialConflictException();
            if (rollback && receipt!.FinalizedAt is not null)
                throw new InvalidOperationException("A finalized migration cannot be rolled back; the provider-wide record was released when it was finalized.");

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
            // A separate provider-wide OAuth token (access and refresh token) moves under the same scope
            // with the record, and back on rollback, so a refreshable provider-wide token is never left
            // active alongside an owned record. The moved token is unverified like the record itself.
            var oauthTransferred = rollback ? receipt!.OAuthTokenTransferred : TransferOAuthToken(vault, descriptor.ProviderId, scope, rollback: false);
            if (rollback && oauthTransferred)
                TransferOAuthToken(vault, descriptor.ProviderId, scope, rollback: true);

            receipt = rollback
                ? receipt! with { RolledBack = true, RolledBackBy = actor, RolledBackAt = DateTimeOffset.UtcNow }
                : new ProviderCredentialMigrationReceipt(migrationId, descriptor.ProviderId, scope, generation, actor, DateTimeOffset.UtcNow,
                    OAuthTokenTransferred: oauthTransferred);
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
