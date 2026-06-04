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

        foreach (var scriptPath in GetMigrationScripts())
        {
            var sql = await File.ReadAllTextAsync(scriptPath, ct).ConfigureAwait(false);
            var rendered = RenderSchema(sql, _options.Schema);

            await using var command = connection.CreateCommand();
            command.CommandText = rendered;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
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
