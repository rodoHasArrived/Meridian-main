using Meridian.Storage.Migrations;

namespace Meridian.Storage.Ledger;

public sealed class LedgerMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public LedgerMigrationRunner(LedgerJournalStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.SchemaName,
            ScriptsSubdirectory = Path.Combine("Ledger", "Migrations"),
            DisplayName = "Ledger",
            LockScopeName = "ledger",
            ConnectionStringSettingName = $"{nameof(LedgerJournalStoreOptions)}.{nameof(options.ConnectionString)}",
            // Feature-prefixed ledger so a shared schema can never collide with another
            // feature's migration table layout.
            LedgerTableName = "ledger_journal_schema_migrations",
            DriftPolicy = MigrationDriftPolicy.Reapply,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
