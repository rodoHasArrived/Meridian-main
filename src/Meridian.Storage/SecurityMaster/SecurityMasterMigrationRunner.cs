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
            IdentifierHistoryMigrationLegacyWritersRetired = options.IdentifierHistoryMigrationLegacyWritersRetired,
            TermsFormatBackfillLegacyWritersRetired = options.TermsFormatBackfillLegacyWritersRetired
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
                CheckApplyConditionsAsync(applyOptions, fileName, connection, transaction, isUpgrade, ct),
        });
    }

    public Task EnsureMigratedAsync(CancellationToken ct = default) => _runner.EnsureMigratedAsync(ct);

    public Task ResetSchemaAsync(CancellationToken ct = default) => _runner.ResetSchemaAsync(ct);

    /// <summary>The apply gates of every Security Master migration that has one (062 and 068).</summary>
    internal static async Task CheckApplyConditionsAsync(SecurityMasterOptions options,
        string fileName, NpgsqlConnection connection, NpgsqlTransaction transaction, bool isUpgrade,
        CancellationToken ct)
    {
        await CheckIdentifierHistoryApplyConditionsAsync(options, fileName, connection, transaction, isUpgrade, ct)
            .ConfigureAwait(false);
        await CheckTermsFormatBackfillApplyConditionsAsync(options, fileName, connection, transaction, isUpgrade, ct)
            .ConfigureAwait(false);
    }

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
        command.CommandText = $"lock table {options.Schema}.securities, {options.Schema}.security_identifiers in access exclusive mode nowait;";
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

    /// <summary>
    /// Migration 068 rewrites <c>securities.asset_specific_terms</c> to declare <c>termsFormat</c> and
    /// then adds a constraint that refuses an untagged row (ADR-024 amendment 2026-11-13). An upgrade
    /// therefore requires the attestation that no writer built before the stamping change can write
    /// after the commit, and an <c>ACCESS EXCLUSIVE NOWAIT</c> lock on <c>securities</c>, so an active writer
    /// is a visible refusal rather than a row written untagged between the backfill and the
    /// constraint. The lock is the one the script's <c>ALTER TABLE ... ADD CONSTRAINT</c> needs: taking
    /// it up front with <c>NOWAIT</c> means an open reader is also refused immediately, instead of the
    /// later lock upgrade waiting behind that reader while every new read of <c>securities</c> queues
    /// behind the migration. A fresh installation has no rows and needs no attestation.
    /// </summary>
    internal static async Task CheckTermsFormatBackfillApplyConditionsAsync(SecurityMasterOptions options,
        string fileName, NpgsqlConnection connection, NpgsqlTransaction transaction, bool isUpgrade,
        CancellationToken ct)
    {
        if (fileName != "068_security_master_terms_format_backfill.sql")
            return;

        if (isUpgrade && !options.TermsFormatBackfillLegacyWritersRetired)
            throw new InvalidOperationException(
                "Security Master migration 068 requires retirement of every writer that persists untagged terms. " +
                "Prevent every Security Master binary built before the termsFormat stamping change from restarting, then set " +
                "SecurityMasterOptions.TermsFormatBackfillLegacyWritersRetired to true for this upgrade.");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"lock table {options.Schema}.securities in access exclusive mode nowait;";
        try
        {
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new InvalidOperationException(
                "Security Master migration 068 cannot lock the securities table. Stop every Security Master writer and close every open securities reader before retrying.", exception);
        }

        command.CommandText = $"select count(*) from (select 1 from {options.Schema}.securities limit 1000001) securities;";
        var rowCount = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        CheckTermsFormatBackfillTimeout(rowCount, command.CommandTimeout);
    }

    internal static void CheckTermsFormatBackfillTimeout(long rowCount, int commandTimeoutSeconds)
    {
        // The backfill and the constraint validation each scan every securities row; above a
        // million rows the usual thirty-second driver timeout must be raised explicitly.
        if (rowCount > 1_000_000 && commandTimeoutSeconds is > 0 and <= 30)
            throw new InvalidOperationException(
                "Security Master migration 068 has more than 1,000,000 securities rows. " +
                "Raise Command Timeout above 30 seconds in SecurityMasterOptions.ConnectionString for this upgrade (0 means unlimited).");
    }
}
