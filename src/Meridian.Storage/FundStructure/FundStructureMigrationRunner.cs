using Meridian.Storage.Migrations;

namespace Meridian.Storage.FundStructure;

public sealed class FundStructureMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public FundStructureMigrationRunner(FundStructureStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("FundStructure", "Migrations"),
            DisplayName = "Fund structure",
            LockScopeName = "fund_structure",
            ConnectionStringSettingName = $"{nameof(FundStructureStoreOptions)}.{nameof(options.ConnectionString)}",
            // Feature-prefixed ledger so a shared schema can never collide with another
            // feature's migration table layout.
            LedgerTableName = "fund_structure_schema_migrations",
            DriftPolicy = MigrationDriftPolicy.Reapply,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
