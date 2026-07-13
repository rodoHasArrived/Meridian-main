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
            DriftPolicy = MigrationDriftPolicy.Reapply,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
