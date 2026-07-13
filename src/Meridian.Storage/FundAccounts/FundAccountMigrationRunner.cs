using Meridian.Storage.Migrations;

namespace Meridian.Storage.FundAccounts;

public sealed class FundAccountMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public FundAccountMigrationRunner(FundAccountStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("FundAccounts", "Migrations"),
            DisplayName = "Fund accounts",
            LockScopeName = "fund_accounts",
            ConnectionStringSettingName = $"{nameof(FundAccountStoreOptions)}.{nameof(options.ConnectionString)}",
            DriftPolicy = MigrationDriftPolicy.Reapply,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
