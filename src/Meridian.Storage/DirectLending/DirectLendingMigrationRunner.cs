using Meridian.Contracts.DirectLending;
using Npgsql;

namespace Meridian.Storage.DirectLending;

public sealed class DirectLendingMigrationRunner
{
    private readonly DirectLendingOptions _options;

    public DirectLendingMigrationRunner(DirectLendingOptions options)
    {
        _options = options;
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

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("DirectLendingOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static IEnumerable<string> GetMigrationScripts()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var migrationDirectory = Path.Combine(baseDirectory, "DirectLending", "Migrations");
        if (!Directory.Exists(migrationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Direct lending migration directory was not found at '{migrationDirectory}'.");
        }

        return Directory
            .GetFiles(migrationDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string RenderSchema(string sql, string schema)
        => sql.Replace("__SCHEMA__", schema, StringComparison.Ordinal);
}
