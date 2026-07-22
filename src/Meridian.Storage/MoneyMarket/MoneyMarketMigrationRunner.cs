using Meridian.Storage.Migrations;

namespace Meridian.Storage.MoneyMarket;

/// <summary>
/// Applies pending SQL migrations for the MoneyMarket schema.
/// </summary>
public sealed class MoneyMarketMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public MoneyMarketMigrationRunner(MoneyMarketStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("MoneyMarket", "Migrations"),
            DisplayName = "MoneyMarket",
            LockScopeName = "money_market",
            ConnectionStringSettingName = $"{nameof(MoneyMarketStoreOptions)}.{nameof(options.ConnectionString)}",
            ThrowWhenScriptsDirectoryMissing = false,
        });
    }

    /// <summary>
    /// Creates the schema and applies all outstanding migrations in version order.
    /// Idempotent — safe to call on every startup.
    /// </summary>
    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
