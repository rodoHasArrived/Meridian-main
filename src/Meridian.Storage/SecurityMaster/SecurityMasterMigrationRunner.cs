using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Migrations;

namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Applies the Security Master schema migrations in ordinal order. Applied scripts are recorded in a
/// durable <c>schema_migrations</c> ledger so each migration runs exactly once, and the runner refuses
/// to start when two scripts collide on the same numeric ordinal — the ordering across scripts is then
/// well-defined rather than decided by the rest of the filename.
/// </summary>
public sealed class SecurityMasterMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public SecurityMasterMigrationRunner(SecurityMasterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("SecurityMaster", "Migrations"),
            DisplayName = "Security Master",
            LockScopeName = "security_master",
            ConnectionStringSettingName = $"{nameof(SecurityMasterOptions)}.{nameof(options.ConnectionString)}",
            TrackOrdinals = true,
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);

    public Task ResetSchemaAsync(CancellationToken ct = default) => _runner.ResetSchemaAsync(ct);
}
