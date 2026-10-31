namespace Meridian.DataIntegration.Credentials;

/// <summary>Explicit, generation-bound transfer of a retained provider-wide record into an owned connection.</summary>
public interface IProviderCredentialOwnershipMigration
{
    Task<ProviderCredentialMigrationReceipt> MigrateToScopeAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string expectedGeneration, string actor, CancellationToken ct = default);
    Task<ProviderCredentialMigrationReceipt> RollbackMigrationAsync(string providerId, ProviderCredentialScope scope,
        string migrationId, string actor, CancellationToken ct = default);
}

/// <summary>Non-secret authority for retry and rollback. Rotation invalidates rollback to the old ownership.</summary>
public sealed record ProviderCredentialMigrationReceipt(string MigrationId, string ProviderId,
    ProviderCredentialScope Scope, string CredentialGeneration, string Actor, DateTimeOffset MigratedAt,
    bool RolledBack = false, string? RolledBackBy = null, DateTimeOffset? RolledBackAt = null);
