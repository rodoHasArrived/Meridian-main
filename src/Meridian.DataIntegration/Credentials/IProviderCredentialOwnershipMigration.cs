namespace Meridian.DataIntegration.Credentials;

/// <summary>Explicit, generation-bound transfer of a retained provider-wide record into an owned connection.</summary>
public interface IProviderCredentialOwnershipMigration
{
    Task<ProviderCredentialMigrationReceipt> MigrateToScopeAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string expectedGeneration, string actor, CancellationToken ct = default);
    Task<ProviderCredentialMigrationReceipt> RollbackMigrationAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string actor, CancellationToken ct = default);
    /// <summary>
    /// Retires a completed migration after the owned record has been verified under its new ownership:
    /// the receipt stays in the audit trail, rollback is no longer available, and the provider-wide record
    /// is no longer blocked by the migration. Idempotent for an already finalized receipt.
    /// </summary>
    Task<ProviderCredentialMigrationReceipt> FinalizeMigrationAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string actor, CancellationToken ct = default);
}

/// <summary>
/// Non-secret authority for retry, rollback and finalization. Rotation invalidates rollback to the old
/// ownership; finalization retires rollback. <see cref="OAuthTokenTransferred"/> records that a separate
/// provider-wide OAuth token moved under the scope with the record and must move back on rollback.
/// </summary>
public sealed record ProviderCredentialMigrationReceipt(string MigrationId, string ProviderId,
    ProviderCredentialScope Scope, string CredentialGeneration, string Actor, DateTimeOffset MigratedAt,
    bool RolledBack = false, string? RolledBackBy = null, DateTimeOffset? RolledBackAt = null,
    bool OAuthTokenTransferred = false, string? FinalizedBy = null, DateTimeOffset? FinalizedAt = null)
{
    /// <summary>True while the migration still governs the provider-wide record: not rolled back and not finalized.</summary>
    public bool IsActive => !RolledBack && FinalizedAt is null;
}
