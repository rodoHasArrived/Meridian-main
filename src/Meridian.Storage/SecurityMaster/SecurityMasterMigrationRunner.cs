using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.Migrations;
using Npgsql;

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
        // Capture the same schema the shared runner validates; later changes to the mutable
        // configuration object must not change the identifiers used by the apply check.
        var applyOptions = new SecurityMasterOptions
        {
            Schema = options.Schema,
            IdentifierHistoryMigrationWritersStopped = options.IdentifierHistoryMigrationWritersStopped,
            IdentifierHistoryMigrationLegacyWritersRetired = options.IdentifierHistoryMigrationLegacyWritersRetired
        };
        _runner = new PostgresMigrationRunner(new PostgresMigrationRunnerOptions
        {
            ConnectionString = options.ConnectionString,
            Schema = options.Schema,
            ScriptsSubdirectory = Path.Combine("SecurityMaster", "Migrations"),
            DisplayName = "Security Master",
            LockScopeName = "security_master",
            ConnectionStringSettingName = $"{nameof(SecurityMasterOptions)}.{nameof(options.ConnectionString)}",
            TrackOrdinals = true,
            BeforeScriptAsync = (fileName, connection, transaction, isUpgrade, ct) =>
                CheckIdentifierHistoryApplyConditionsAsync(applyOptions, fileName, connection, transaction, isUpgrade, ct),
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);

    public Task ResetSchemaAsync(CancellationToken ct = default) => _runner.ResetSchemaAsync(ct);

    internal static async Task CheckIdentifierHistoryApplyConditionsAsync(SecurityMasterOptions options,
        string fileName, NpgsqlConnection connection, NpgsqlTransaction transaction, bool isUpgrade,
        CancellationToken ct)
    {
        if (fileName != "062_security_master_identifier_revisions.sql")
            return;

        if (isUpgrade && (!options.IdentifierHistoryMigrationWritersStopped
                          || !options.IdentifierHistoryMigrationLegacyWritersRetired))
            throw new InvalidOperationException(
                "Security Master migration 062 requires stopped writers and retirement of every pre-062 writer. " +
                "Stop every Security Master writer, prevent pre-062 binaries from restarting, then set " +
                "SecurityMasterOptions.IdentifierHistoryMigrationWritersStopped and " +
                "SecurityMasterOptions.IdentifierHistoryMigrationLegacyWritersRetired to true for this upgrade.");

        // Lock the parent before the child, before the Baseline's foreign-key checks. NOWAIT makes
        // an active writer a visible apply refusal instead of waiting into the reviewed deadlock.
        // Both locks last through the enclosing migration commit or rollback.
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"lock table {options.Schema}.securities, {options.Schema}.security_identifiers in exclusive mode nowait;";
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new InvalidOperationException(
                "Security Master migration 062 cannot lock its source tables. Stop every Security Master writer before retrying.", exception);
        }

        command.CommandText = $"select count(*) from (select 1 from {options.Schema}.security_identifiers limit 1000001) identifiers;";
        var rowCount = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        CheckIdentifierHistoryTimeout(rowCount, command.CommandTimeout);
    }

    internal static void CheckIdentifierHistoryTimeout(long rowCount, int commandTimeoutSeconds)
    {
        // Zero means unlimited in Npgsql. A larger backfill must explicitly raise the usual
        // thirty-second driver timeout; it must never start with the reviewed unsafe default.
        if (rowCount > 1_000_000 && commandTimeoutSeconds is > 0 and <= 30)
            throw new InvalidOperationException(
                "Security Master migration 062 has more than 1,000,000 identifier rows. " +
                "Raise Command Timeout above 30 seconds in SecurityMasterOptions.ConnectionString for this upgrade (0 means unlimited).");
    }
}
