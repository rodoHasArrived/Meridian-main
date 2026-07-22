using Meridian.Storage.Migrations;

namespace Meridian.Storage.Reporting;

/// <summary>
/// Applies immutable, checksummed reporting storage migrations under a schema-scoped advisory lock.
/// </summary>
public sealed class ReportingMigrationRunner
{
    private readonly PostgresMigrationRunner _runner;

    public ReportingMigrationRunner(ReportingArtifactStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("Reporting", "Migrations"),
            DisplayName = "Reporting",
            LockScopeName = "reporting",
            ConnectionStringSettingName = $"{nameof(ReportingArtifactStoreOptions)}.{nameof(options.ConnectionString)}",
            LedgerTableName = "reporting_schema_migrations",
            QuoteSchemaInScripts = true,
            ChecksumRequired = true,
            DriftPolicy = MigrationDriftPolicy.Throw
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);

    public Task ResetSchemaAsync(CancellationToken ct = default) => _runner.ResetSchemaAsync(ct);
}
