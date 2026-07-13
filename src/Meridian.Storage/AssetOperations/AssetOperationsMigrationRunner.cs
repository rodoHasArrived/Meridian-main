using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.AssetOperations;
using Npgsql;

namespace Meridian.Storage.AssetOperations;

public sealed class AssetOperationsMigrationRunner
{
    private readonly AssetOperationsOptions _options;

    public AssetOperationsMigrationRunner(AssetOperationsOptions options)
    {
        _options = options;
        ValidateIdentifier(_options.Schema, nameof(options.Schema));
    }

    public async Task EnsureMigratedAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var migrationLock = connection.CreateCommand())
        {
            migrationLock.Transaction = transaction;
            migrationLock.CommandText =
                "select pg_advisory_xact_lock(hashtextextended(@migration_scope, 0));";
            migrationLock.Parameters.AddWithValue(
                "migration_scope",
                $"meridian.asset_operations.migrations.{_options.Schema}");
            await migrationLock.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await EnsureMigrationLedgerAsync(connection, transaction, ct).ConfigureAwait(false);

        foreach (var scriptPath in GetMigrationScripts())
        {
            var sql = await File.ReadAllTextAsync(scriptPath, ct).ConfigureAwait(false);
            var migrationId = Path.GetFileName(scriptPath);
            var checksum = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(sql)))
                .ToLowerInvariant();
            if (await IsMigrationAppliedAsync(
                    connection,
                    transaction,
                    migrationId,
                    checksum,
                    ct)
                .ConfigureAwait(false))
            {
                continue;
            }

            var rendered = RenderSchema(sql, _options.Schema);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = rendered;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await RecordMigrationAsync(
                    connection,
                    transaction,
                    migrationId,
                    checksum,
                    ct)
                .ConfigureAwait(false);
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

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("AssetOperationsOptions.ConnectionString is not configured.");
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
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            create schema if not exists {QuoteIdentifier(_options.Schema)};
            create table if not exists {QuoteIdentifier(_options.Schema)}.asset_operations_schema_migrations (
                migration_id text primary key,
                checksum text not null,
                applied_at timestamptz not null default now()
            );
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> IsMigrationAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationId,
        string checksum,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"select checksum from {QuoteIdentifier(_options.Schema)}.asset_operations_schema_migrations where migration_id = @migration_id;";
        command.Parameters.AddWithValue("migration_id", migrationId);
        var appliedChecksum = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (appliedChecksum is null)
        {
            return false;
        }

        if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset Operations migration '{migrationId}' has changed since it was applied.");
        }

        return true;
    }

    private async Task RecordMigrationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationId,
        string checksum,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"insert into {QuoteIdentifier(_options.Schema)}.asset_operations_schema_migrations (migration_id, checksum) values (@migration_id, @checksum);";
        command.Parameters.AddWithValue("migration_id", migrationId);
        command.Parameters.AddWithValue("checksum", checksum);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> GetMigrationScripts()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var migrationDirectory = Path.Combine(baseDirectory, "AssetOperations", "Migrations");
        if (!Directory.Exists(migrationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Asset Operations migration directory was not found at '{migrationDirectory}'.");
        }

        return Directory
            .GetFiles(migrationDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string RenderSchema(string sql, string schema)
        => sql.Replace("__SCHEMA__", QuoteIdentifier(schema), StringComparison.Ordinal);

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
        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        return value.All(static character => char.IsLetterOrDigit(character) || character == '_');
    }
}
