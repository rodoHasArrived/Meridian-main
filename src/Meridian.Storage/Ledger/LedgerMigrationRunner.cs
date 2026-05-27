using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed class LedgerMigrationRunner
{
    private readonly LedgerJournalStoreOptions _options;

    public LedgerMigrationRunner(LedgerJournalStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task EnsureMigratedAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        foreach (var scriptPath in GetMigrationScripts())
        {
            var sql = await File.ReadAllTextAsync(scriptPath, ct).ConfigureAwait(false);
            var rendered = RenderSchema(sql, _options.SchemaName);

            await using var command = connection.CreateCommand();
            command.CommandText = rendered;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("LedgerJournalStoreOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static IEnumerable<string> GetMigrationScripts()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var migrationDirectory = Path.Combine(baseDirectory, "Ledger", "Migrations");
        if (!Directory.Exists(migrationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Ledger migration directory was not found at '{migrationDirectory}'.");
        }

        return Directory
            .GetFiles(migrationDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string RenderSchema(string sql, string schema)
        => sql.Replace("__SCHEMA__", ValidateIdentifier(schema, nameof(schema)), StringComparison.Ordinal);

    private static string ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{parameterName} is required.");
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw new InvalidOperationException($"{parameterName} contains an invalid identifier character.");
            }
        }

        return value;
    }
}
