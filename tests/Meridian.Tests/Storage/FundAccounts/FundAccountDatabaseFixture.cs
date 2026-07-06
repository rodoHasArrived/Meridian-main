using Meridian.Storage.FundAccounts;
using Npgsql;
using Testcontainers.PostgreSql;
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

    // Non-null only when no external connection string is supplied.
    private readonly PostgreSqlContainer? _container;

    // Stable once InitializeAsync completes.
    public FundAccountStoreOptions Options { get; private set; }

    public FundAccountDatabaseFixture()
    {
        var externalConnectionString = Environment.GetEnvironmentVariable(EnvVar);

        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            Options = new FundAccountStoreOptions
            {
                ConnectionString = externalConnectionString,
                Schema = $"fa_test_{Guid.NewGuid():N}"
            };
        }
        else
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("meridian_test")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .Build();

            // Placeholder — overwritten in InitializeAsync once the container is running.
            Options = new FundAccountStoreOptions();
        }
    }

    public async Task InitializeAsync()
    {
        if (_container is not null)
        {
            await _container.StartAsync().ConfigureAwait(false);
            Options = new FundAccountStoreOptions
            {
                ConnectionString = _container.GetConnectionString(),
                Schema = $"fa_test_{Guid.NewGuid():N}"
            };
        }

        var runner = new FundAccountMigrationRunner(Options);
        await runner.EnsureMigratedAsync().ConfigureAwait(false);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            // FundAccountMigrationRunner has no schema reset; drop the run-scoped
            // schema directly so external databases stay clean.
            await DropSchemaAsync(Options.ConnectionString, Options.Schema).ConfigureAwait(false);
        }
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
            !identifier.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException($"Unsafe schema identifier: '{identifier}'", nameof(identifier));
        }
    }
}

[CollectionDefinition(nameof(FundAccountDatabaseCollection), DisableParallelization = true)]
public sealed class FundAccountDatabaseCollection : ICollectionFixture<FundAccountDatabaseFixture>
{
}
