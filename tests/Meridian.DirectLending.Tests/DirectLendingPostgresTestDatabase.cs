using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;
using Meridian.Storage.DirectLending;
using Npgsql;

namespace Meridian.DirectLending.Tests;

internal sealed class DirectLendingPostgresTestDatabase : IAsyncDisposable
{
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=secret";

    private DirectLendingPostgresTestDatabase(string connectionString, string schema)
    {
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

    public static async Task<DirectLendingPostgresTestDatabase?> CreateOrSkipAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MERIDIAN_DIRECT_LENDING_CONNECTION_STRING") ?? DefaultConnectionString;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Skipping direct-lending PostgreSQL integration test because the database is unavailable: {ex.Message}");
            return null;
        }

        var schema = $"direct_lending_test_{Guid.NewGuid():N}".Substring(0, 33);
        var database = new DirectLendingPostgresTestDatabase(connectionString, schema);
        var runner = new DirectLendingMigrationRunner(database.Options);
        await runner.EnsureMigratedAsync().ConfigureAwait(false);
        return database;
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
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"drop schema if exists {Schema} cascade;";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
