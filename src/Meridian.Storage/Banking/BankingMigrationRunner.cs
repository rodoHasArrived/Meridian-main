using System.Reflection;
using Npgsql;

namespace Meridian.Storage.Banking;

/// <summary>
/// Applies pending SQL migrations for the Banking schema.
/// </summary>
public sealed class BankingMigrationRunner
{
    private readonly BankingStoreOptions _options;

    public BankingMigrationRunner(BankingStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Creates the schema and applies all outstanding migrations in version order.
    /// Idempotent — safe to call on every startup.
    /// </summary>
    public async Task EnsureMigratedAsync(CancellationToken ct = default)
    {
        var migrationsDir = Path.Combine(
            AppContext.BaseDirectory,
            "Banking",
            "Migrations");

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);

        // Ensure schema exists
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"CREATE SCHEMA IF NOT EXISTS {_options.Schema};";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Ensure migration tracking table exists
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {_options.Schema}.schema_migrations (
                    filename TEXT PRIMARY KEY,
                    applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """;
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (!Directory.Exists(migrationsDir))
        {
            return;
        }

        var sqlFiles = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(static f => f);

        foreach (var sqlFile in sqlFiles)
        {
            var filename = Path.GetFileName(sqlFile);

            // Check if already applied
            bool alreadyApplied;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"SELECT 1 FROM {_options.Schema}.schema_migrations WHERE filename = @f;";
                cmd.Parameters.AddWithValue("f", filename);
                alreadyApplied = await cmd.ExecuteScalarAsync(ct) is not null;
            }

            if (alreadyApplied) continue;

            var sql = (await File.ReadAllTextAsync(sqlFile, ct))
                .Replace("__SCHEMA__", _options.Schema, StringComparison.Ordinal);

            await using var tx = await connection.BeginTransactionAsync(ct);
            try
            {
                await using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = $"INSERT INTO {_options.Schema}.schema_migrations (filename) VALUES (@f);";
                    cmd.Parameters.AddWithValue("f", filename);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }
    }
}
