using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Meridian.DirectLending.Tests;

/// <summary>
/// Provides an isolated PostgreSQL database for Direct Lending integration tests.
/// When <c>MERIDIAN_DIRECT_LENDING_CONNECTION_STRING</c> is set the fixture connects
/// to that external database; otherwise a Docker container is started automatically
/// via Testcontainers.  Set <c>MERIDIAN_DISABLE_DOCKER_TESTS=true</c> to skip the
/// entire suite on runners that have no Docker daemon.
/// </summary>
internal sealed class DirectLendingPostgresTestDatabase : IAsyncDisposable
{
    private const string EnvVar = "MERIDIAN_DIRECT_LENDING_CONNECTION_STRING";
    private const string DisableDockerEnvVar = "MERIDIAN_DISABLE_DOCKER_TESTS";

    private readonly PostgreSqlContainer? _container;

    private DirectLendingPostgresTestDatabase(string connectionString, string schema, PostgreSqlContainer? container)
    {
        _container = container;
        ConnectionString = connectionString;
        Schema = schema;
        Options = new DirectLendingOptions
        {
            ConnectionString = connectionString,
            Schema = schema,
            SnapshotIntervalVersions = 2,
            CurrentEventSchemaVersion = 1
        };

        Store = new PostgresDirectLendingStateStore(Options);
        Rebuilder = new DirectLendingEventRebuilder();
        QueryService = new PostgresDirectLendingQueryService(Store, Store, Rebuilder);
        CommandService = new PostgresDirectLendingCommandService(Store, Store, QueryService, Options);
        Service = new PostgresDirectLendingService(CommandService, QueryService);
    }

    public string ConnectionString { get; }

    public string Schema { get; }

    public DirectLendingOptions Options { get; }

    public PostgresDirectLendingStateStore Store { get; }

    public DirectLendingEventRebuilder Rebuilder { get; }

    public PostgresDirectLendingQueryService QueryService { get; }

    public PostgresDirectLendingCommandService CommandService { get; }

    public PostgresDirectLendingService Service { get; }

    /// <summary>
    /// Creates and migrates a test database.  Returns <see langword="null"/> only when
    /// <c>MERIDIAN_DISABLE_DOCKER_TESTS=true</c> is set, allowing the caller to skip
    /// the test.  In all other cases a real database (container or external) is used.
    /// </summary>
    public static async Task<DirectLendingPostgresTestDatabase?> CreateOrSkipAsync()
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable(DisableDockerEnvVar),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                $"Skipping Direct Lending PostgreSQL integration tests because {DisableDockerEnvVar}=true.");
            return null;
        }

        var schema = $"dl_test_{Guid.NewGuid():N}";

        var externalConnectionString = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            var database = new DirectLendingPostgresTestDatabase(externalConnectionString, schema, container: null);
            var runner = new DirectLendingMigrationRunner(database.Options);
            await runner.EnsureMigratedAsync().ConfigureAwait(false);
            return database;
        }

        // No external connection string — spin up a container.
        var container = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("meridian_dl_test")
            .WithUsername("testuser")
            .WithPassword("testpass")
            .Build();

        await container.StartAsync().ConfigureAwait(false);

        var containerDatabase = new DirectLendingPostgresTestDatabase(
            container.GetConnectionString(), schema, container);

        var migrationRunner = new DirectLendingMigrationRunner(containerDatabase.Options);
        await migrationRunner.EnsureMigratedAsync().ConfigureAwait(false);
        return containerDatabase;
    }

    public async Task<long> CountSnapshotsAsync(Guid loanId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from {Schema}.loan_snapshot where loan_id = @loan_id;";
        command.Parameters.AddWithValue("loan_id", loanId);
        return (long)(await command.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
    }

    public async Task DeleteLiveStateAsync(Guid loanId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"delete from {Schema}.loan_state where loan_id = @loan_id;";
        command.Parameters.AddWithValue("loan_id", loanId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"drop schema if exists {Schema} cascade;";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
