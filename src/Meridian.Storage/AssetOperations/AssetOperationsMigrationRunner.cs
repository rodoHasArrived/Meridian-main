using Meridian.Contracts.AssetOperations;
using Meridian.Storage.Migrations;

namespace Meridian.Storage.AssetOperations;

public sealed class AssetOperationsMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public AssetOperationsMigrationRunner(AssetOperationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("AssetOperations", "Migrations"),
            DisplayName = "Asset Operations",
            LockScopeName = "asset_operations",
            ConnectionStringSettingName = $"{nameof(AssetOperationsOptions)}.{nameof(options.ConnectionString)}",
            LedgerTableName = "asset_operations_schema_migrations",
            LedgerKeyColumn = "migration_id",
            QuoteSchemaInScripts = true,
            ChecksumRequired = true,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);

    public Task ResetSchemaAsync(CancellationToken ct = default) => _runner.ResetSchemaAsync(ct);
}
