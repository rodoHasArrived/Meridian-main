using Meridian.Contracts.Tenancy;

namespace Meridian.Application.Tenancy;

/// <summary>Refuses unpartitioned local snapshots until their ownership is explicitly migrated.</summary>
public sealed class LocalTenantMigrationGate(TenantScopeEnforcementOptions options)
{
    public bool EnforcesStrictTenancy => options.IsFailClosed;

    public const string RefusalMessage =
        "Local fund records have no retained tenant attribution and cannot be accessed under strict tenant enforcement. " +
        "Existing files are retained. Use the supported durable host and reviewed tenant backfill, or explicitly select " +
        "TenantScopeEnforcement=deployment-boundary only for isolated single-company migration or demo access.";

    public Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        if (options.IsFailClosed)
            throw new LocalTenantMigrationRequiredException();
        return operation();
    }
}

/// <summary>A retained local capability is unavailable until its tenant migration is reviewed.</summary>
public sealed class LocalTenantMigrationRequiredException() : Exception(LocalTenantMigrationGate.RefusalMessage);
