using Meridian.Application.DirectLending;
using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.DirectLending;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using Meridian.TestSupport;
using Npgsql;

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
    private static readonly ILedgerJournalStore _noOpLedgerJournalStore = new InMemoryNoOpLedgerJournalStore();

    private readonly PostgresTestServer _server;

    private DirectLendingPostgresTestDatabase(PostgresTestServer server, string schema)
    {
        _server = server;
        ConnectionString = server.ConnectionString;
        Schema = schema;
        Options = new DirectLendingOptions
        {
            ConnectionString = server.ConnectionString,
            Schema = schema,
            SnapshotIntervalVersions = 2,
            CurrentEventSchemaVersion = 1
        };

        Store = new PostgresDirectLendingStateStore(Options);
        Rebuilder = new DirectLendingEventRebuilder();
        QueryService = new PostgresDirectLendingQueryService(Store, Store, Rebuilder);
        CommandService = new PostgresDirectLendingCommandService(
            Store,
            Store,
            QueryService,
            new LoanAccountingProjector(_noOpLedgerJournalStore, new AccountingPolicyService()),
            Options);
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

        var server = await PostgresTestServer.CreateAsync(
                EnvVar,
                new PostgresTestContainerOptions { Database = "meridian_dl_test" })
            .ConfigureAwait(false);
        var schema = server.CreateSchemaName("dl");

        var database = new DirectLendingPostgresTestDatabase(server, schema);
        try
        {
            var runner = new DirectLendingMigrationRunner(database.Options);
            await runner.EnsureMigratedAsync().ConfigureAwait(false);
            return database;
        }
        catch
        {
            await database.DisposeAsync().ConfigureAwait(false);
            throw;
        }
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
        await _server.DisposeAsync().ConfigureAwait(false);
    }

    private sealed class InMemoryNoOpLedgerJournalStore : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<LedgerAccountingPeriod?>(null);

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([]);

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            Task.FromResult(period);

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) =>
            Task.FromResult<LedgerBookRecord?>(null);

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerBookRecord>>([]);

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default) =>
            Task.FromResult(book);
    }
}
