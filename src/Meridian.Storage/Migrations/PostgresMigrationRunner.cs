using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Meridian.Storage.Migrations;

/// <summary>
/// Shared PostgreSQL SQL-file migration runner used by every feature schema in Meridian.Storage.
/// Applies <c>*.sql</c> scripts from a directory under <see cref="AppContext.BaseDirectory"/> in
/// filename order, records applied scripts (with a SHA-256 checksum) in a durable per-schema
/// migration ledger, and serializes concurrent starters with a transaction-scoped advisory lock.
/// The whole run executes inside a single transaction so a failing script never leaves the schema
/// half-migrated.
/// </summary>
public sealed class PostgresMigrationRunner
{
    private readonly PostgresMigrationRunnerOptions _options;

    public PostgresMigrationRunner(PostgresMigrationRunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        ValidateIdentifier(options.Schema, nameof(options.Schema));
        ValidateIdentifier(options.LedgerTableName, nameof(options.LedgerTableName));
        ValidateIdentifier(options.LedgerKeyColumn, nameof(options.LedgerKeyColumn));
    }

    public async Task EnsureMigratedAsync(CancellationToken ct = default)
    {
        var scripts = GetMigrationScripts();

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var migrationLock = connection.CreateCommand())
        {
            migrationLock.Transaction = transaction;
            migrationLock.CommandText =
                "select pg_advisory_xact_lock(hashtextextended(@migration_scope, 0));";
            migrationLock.Parameters.AddWithValue(
                "migration_scope",
                $"meridian.{_options.LockScopeName}.migrations.{_options.Schema}");
            await migrationLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await EnsureMigrationLedgerAsync(connection, transaction, ct).ConfigureAwait(false);

        foreach (var script in scripts)
        {
            var sql = await File.ReadAllTextAsync(script.Path, ct).ConfigureAwait(false);
            var checksum = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(sql)))
                .ToLowerInvariant();

            var applied = await GetAppliedChecksumAsync(connection, transaction, script.FileName, ct)
                .ConfigureAwait(false);

            if (applied.IsApplied)
            {
                if (applied.Checksum is null)
                {
                    // Row recorded before checksums were tracked: adopt the current content as the
                    // canonical version so future drift is detected, without re-running the script.
                    await UpdateChecksumAsync(connection, transaction, script.FileName, checksum, ct)
                        .ConfigureAwait(false);
                    continue;
                }

                if (string.Equals(applied.Checksum, checksum, StringComparison.Ordinal))
                {
                    continue;
                }

                if (_options.DriftPolicy == MigrationDriftPolicy.Throw)
                {
                    throw new InvalidOperationException(
                        $"{_options.DisplayName} migration '{script.FileName}' has changed since it was applied.");
                }

                // MigrationDriftPolicy.Reapply: schemas whose scripts were historically re-run on
                // every startup keep their edit-in-place workflow — a changed script is applied
                // again and the ledger checksum updated.
                await ExecuteScriptAsync(connection, transaction, sql, ct).ConfigureAwait(false);
                await UpdateChecksumAsync(connection, transaction, script.FileName, checksum, ct)
                    .ConfigureAwait(false);
                continue;
            }

            await ExecuteScriptAsync(connection, transaction, sql, ct).ConfigureAwait(false);
            await RecordMigrationAsync(connection, transaction, script, checksum, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"drop schema if exists {QuoteIdentifier(_options.Schema)} cascade;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private string RenderedSchema
        => _options.QuoteSchemaInScripts ? QuoteIdentifier(_options.Schema) : _options.Schema;

    private string LedgerTable => $"{RenderedSchema}.{_options.LedgerTableName}";

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"{_options.ConnectionStringSettingName} is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private async Task EnsureMigrationLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        var ordinalColumn = _options.TrackOrdinals ? "ordinal integer not null," : string.Empty;
        var checksumNullability = _options.ChecksumRequired ? "not null" : "null";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            create schema if not exists {RenderedSchema};
            create table if not exists {LedgerTable} (
                {ordinalColumn}
                {_options.LedgerKeyColumn} text primary key,
                checksum text {checksumNullability},
                applied_at timestamptz not null default now()
            );
            alter table {LedgerTable} add column if not exists checksum text;
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<(bool IsApplied, string? Checksum)> GetAppliedChecksumAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string fileName,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select checksum from {LedgerTable} where {_options.LedgerKeyColumn} = @key;";
        command.Parameters.AddWithValue("key", fileName);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return (false, null);
        }

        return (true, reader.IsDBNull(0) ? null : reader.GetString(0));
    }

    private async Task ExecuteScriptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken ct)
    {
        var rendered = sql.Replace("__SCHEMA__", RenderedSchema, StringComparison.Ordinal);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = rendered;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task RecordMigrationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        MigrationScript script,
        string checksum,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (_options.TrackOrdinals)
        {
            command.CommandText =
                $"insert into {LedgerTable} (ordinal, {_options.LedgerKeyColumn}, checksum) values (@ordinal, @key, @checksum);";
            command.Parameters.AddWithValue("ordinal", script.Ordinal);
        }
        else
        {
            command.CommandText =
                $"insert into {LedgerTable} ({_options.LedgerKeyColumn}, checksum) values (@key, @checksum);";
        }

        command.Parameters.AddWithValue("key", script.FileName);
        command.Parameters.AddWithValue("checksum", checksum);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpdateChecksumAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string fileName,
        string checksum,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"update {LedgerTable} set checksum = @checksum where {_options.LedgerKeyColumn} = @key;";
        command.Parameters.AddWithValue("key", fileName);
        command.Parameters.AddWithValue("checksum", checksum);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private IReadOnlyList<MigrationScript> GetMigrationScripts()
    {
        var migrationDirectory = Path.Combine(AppContext.BaseDirectory, _options.ScriptsSubdirectory);
        if (!Directory.Exists(migrationDirectory))
        {
            if (_options.ThrowWhenScriptsDirectoryMissing)
            {
                throw new DirectoryNotFoundException(
                    $"{_options.DisplayName} migration directory was not found at '{migrationDirectory}'.");
            }

            return [];
        }

        var scripts = Directory
            .GetFiles(migrationDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new MigrationScript(
                path,
                Path.GetFileName(path),
                _options.TrackOrdinals ? ParseOrdinal(Path.GetFileName(path)) : 0))
            .ToList();

        if (_options.TrackOrdinals)
        {
            var seenOrdinals = new Dictionary<int, string>();
            foreach (var script in scripts)
            {
                if (!seenOrdinals.TryAdd(script.Ordinal, script.FileName))
                {
                    throw new InvalidOperationException(
                        $"Duplicate {_options.DisplayName} migration ordinal {script.Ordinal:D3}: " +
                        $"'{seenOrdinals[script.Ordinal]}' and '{script.FileName}' share the same ordinal. " +
                        "Migration ordinals must be unique so apply order is deterministic.");
                }
            }
        }

        return scripts;
    }

    private int ParseOrdinal(string fileName)
    {
        var separatorIndex = fileName.IndexOf('_');
        var prefix = separatorIndex > 0 ? fileName[..separatorIndex] : fileName;
        if (!int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal))
        {
            throw new InvalidOperationException(
                $"{_options.DisplayName} migration '{fileName}' does not begin with a numeric ordinal prefix.");
        }

        return ordinal;
    }

    private static string QuoteIdentifier(string value) => $"\"{value}\"";

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("PostgreSQL identifiers cannot be empty.", parameterName);
        }

        if (!IsValidIdentifier(value))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and underscores, and start with a letter or underscore.",
                parameterName);
        }
    }

    private static bool IsValidIdentifier(string value)
    {
        if (!(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private readonly record struct MigrationScript(string Path, string FileName, int Ordinal);
}
