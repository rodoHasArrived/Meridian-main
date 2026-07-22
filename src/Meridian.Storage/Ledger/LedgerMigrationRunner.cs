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
            RepeatableMigrationFileNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "V_ledger_020__fund_scope_tenant_columns.sql",
                "V_ledger_021__operations_continuity_tenant_column.sql",
            },
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
