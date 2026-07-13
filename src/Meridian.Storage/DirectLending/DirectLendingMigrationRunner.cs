using Meridian.Contracts.DirectLending;
using Meridian.Storage.Migrations;

namespace Meridian.Storage.DirectLending;

public sealed class DirectLendingMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public DirectLendingMigrationRunner(DirectLendingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("DirectLending", "Migrations"),
            DisplayName = "Direct lending",
            LockScopeName = "direct_lending",
            ConnectionStringSettingName = $"{nameof(DirectLendingOptions)}.{nameof(options.ConnectionString)}",
            DriftPolicy = MigrationDriftPolicy.Reapply,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);
}
