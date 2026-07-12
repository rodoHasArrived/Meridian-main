using System.Globalization;
using Meridian.Contracts.SecurityMaster;
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
    private readonly SecurityMasterOptions _options;

    public SecurityMasterMigrationRunner(SecurityMasterOptions options)
    {
        _options = options;
    }

    public async Task EnsureMigratedAsync(CancellationToken ct = default)
    {
        var schema = ValidateIdentifier(_options.Schema, nameof(_options.Schema));

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        await using (var schemaCommand = connection.CreateCommand())
        {
            schemaCommand.CommandText = $"create schema if not exists {schema};";
            await schemaCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var ledgerCommand = connection.CreateCommand())
        {
            ledgerCommand.CommandText =
                $"""
                create table if not exists {schema}.schema_migrations (
                    ordinal integer not null,
                    filename text primary key,
                    applied_at timestamptz not null default now()
                );
                """;
            await ledgerCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var script in GetMigrationScripts())
        {
            if (await IsAppliedAsync(connection, schema, script.FileName, ct).ConfigureAwait(false))
            {
                continue;
            }

            var sql = await File.ReadAllTextAsync(script.Path, ct).ConfigureAwait(false);
            var rendered = RenderSchema(sql, schema);

            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                await using (var migrationCommand = connection.CreateCommand())
                {
                    migrationCommand.Transaction = transaction;
                    migrationCommand.CommandText = rendered;
                    await migrationCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var recordCommand = connection.CreateCommand())
                {
                    recordCommand.Transaction = transaction;
                    recordCommand.CommandText =
                        $"insert into {schema}.schema_migrations (ordinal, filename) values (@ordinal, @filename);";
                    recordCommand.Parameters.AddWithValue("ordinal", script.Ordinal);
                    recordCommand.Parameters.AddWithValue("filename", script.FileName);
                    await recordCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }
    }

    public async Task ResetSchemaAsync(CancellationToken ct = default)
    {
        var schema = ValidateIdentifier(_options.Schema, nameof(_options.Schema));
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"drop schema if exists {schema} cascade;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        string schema,
        string fileName,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"select 1 from {schema}.schema_migrations where filename = @filename;";
        command.Parameters.AddWithValue("filename", fileName);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static IReadOnlyList<MigrationScript> GetMigrationScripts()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var migrationDirectory = Path.Combine(baseDirectory, "SecurityMaster", "Migrations");
        if (!Directory.Exists(migrationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Security Master migration directory was not found at '{migrationDirectory}'.");
        }

        var scripts = Directory
            .GetFiles(migrationDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new MigrationScript(path, Path.GetFileName(path), ParseOrdinal(Path.GetFileName(path))))
            .ToList();

        var seenOrdinals = new Dictionary<int, string>();
        foreach (var script in scripts)
        {
            if (!seenOrdinals.TryAdd(script.Ordinal, script.FileName))
            {
                throw new InvalidOperationException(
                    $"Duplicate Security Master migration ordinal {script.Ordinal:D3}: " +
                    $"'{seenOrdinals[script.Ordinal]}' and '{script.FileName}' share the same ordinal. " +
                    "Migration ordinals must be unique so apply order is deterministic.");
            }
        }

        return scripts;
    }

    private static int ParseOrdinal(string fileName)
    {
        var separatorIndex = fileName.IndexOf('_');
        var prefix = separatorIndex > 0 ? fileName[..separatorIndex] : fileName;
        if (!int.TryParse(prefix, NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal))
        {
            throw new InvalidOperationException(
                $"Security Master migration '{fileName}' does not begin with a numeric ordinal prefix.");
        }

        return ordinal;
    }

    private static string RenderSchema(string sql, string schema)
        => sql.Replace("__SCHEMA__", schema, StringComparison.Ordinal);

    private static string ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{parameterName} is required.");
        }

        // Unquoted Postgres identifiers must not start with a digit — the schema name is interpolated
        // unquoted into DDL, so a leading digit would be a SQL syntax error at runtime.
        if (!(char.IsAsciiLetter(value[0]) || value[0] == '_'))
        {
            throw new InvalidOperationException($"{parameterName} must start with a letter or underscore.");
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw new InvalidOperationException($"{parameterName} contains an invalid identifier character.");
            }
        }

        return value;
    }

    private readonly record struct MigrationScript(string Path, string FileName, int Ordinal);
}
