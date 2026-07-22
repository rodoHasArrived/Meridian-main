using Meridian.Storage.Migrations;

namespace Meridian.Storage.Banking;

/// <summary>
/// Applies pending SQL migrations for the Banking schema.
/// </summary>
public sealed class BankingMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public BankingMigrationRunner(BankingStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("Banking", "Migrations"),
            DisplayName = "Banking",
            LockScopeName = "banking",
            ConnectionStringSettingName = $"{nameof(BankingStoreOptions)}.{nameof(options.ConnectionString)}",
            ThrowWhenScriptsDirectoryMissing = false,
        });
    }

    /// <summary>
    /// Creates the schema and applies all outstanding migrations in version order.
    /// Idempotent — safe to call on every startup.
    /// </summary>
    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
