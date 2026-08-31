using Meridian.Storage.FundAccounts;
using Meridian.TestSupport;
using Npgsql;
using Xunit;

namespace Meridian.Tests.Storage.FundAccounts;

/// <summary>
/// Provides a PostgreSQL database for fund-account persistence tests: either an
/// external instance via MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING or a disposable
/// postgres:16-alpine Testcontainer, with a unique schema per run migrated by the
/// real <see cref="FundAccountMigrationRunner"/>.
/// </summary>
public sealed class FundAccountDatabaseFixture : IAsyncLifetime
{
    private const string EnvVar = "MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING";

    // Resolved in InitializeAsync; null only before that runs or if it failed to start.
    private PostgresTestServer? _server;

    // Stable once InitializeAsync completes.
    public FundAccountStoreOptions Options { get; private set; } = new();

    public async Task InitializeAsync()
    {
        _server = await PostgresTestServer.CreateAsync(EnvVar).ConfigureAwait(false);
        try
        {
            Options = new FundAccountStoreOptions
            {
                ConnectionString = _server.ConnectionString,
                Schema = _server.CreateSchemaName("fa")
            };

            var runner = new FundAccountMigrationRunner(Options);
            await runner.EnsureMigratedAsync().ConfigureAwait(false);
        }
        catch
        {
            await _server.DisposeAsync().ConfigureAwait(false);
            _server = null;
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        await _server.DisposeAsync().ConfigureAwait(false);
    }

    internal static async Task DropSchemaAsync(string connectionString, string schema)
    {
        ValidateIdentifier(schema);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static void ValidateIdentifier(string identifier)
    {
        if (identifier.Length == 0 ||
            !identifier.All(static c =>
                char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException(
                $"Unsafe schema identifier: '{identifier}'",
                nameof(identifier));
        }
    }
}
