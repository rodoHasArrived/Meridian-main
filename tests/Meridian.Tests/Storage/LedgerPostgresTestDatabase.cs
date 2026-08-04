using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.TestSupport;
using Npgsql;

namespace Meridian.Tests.Storage;

internal sealed class LedgerPostgresTestDatabase : IAsyncDisposable
{
    private const string ConnectionStringVariable = "MERIDIAN_LEDGER_CONNECTION_STRING";
    private readonly PostgresTestServer _server;

    private LedgerPostgresTestDatabase(
        PostgresTestServer server,
        LedgerJournalStoreOptions options)
    {
        _server = server;
        Options = options;
        StatusDerivation = new OperationsStatusDerivationService();
        JournalStore = new PostgresLedgerJournalStore(options);
        AccountingConfigurationStore = new PostgresAccountingConfigurationStore(options);
        OperationsStore = new PostgresOperationsContinuityStore(
            options,
            JournalStore,
            StatusDerivation);
    }

    public LedgerJournalStoreOptions Options { get; }

    public OperationsStatusDerivationService StatusDerivation { get; }

    public PostgresLedgerJournalStore JournalStore { get; }

    public PostgresAccountingConfigurationStore AccountingConfigurationStore { get; }

    public PostgresOperationsContinuityStore OperationsStore { get; }

    public static async Task<LedgerPostgresTestDatabase> CreateAsync(CancellationToken ct = default)
    {
        var server = await PostgresTestServer.CreateAsync(ConnectionStringVariable, ct: ct)
            .ConfigureAwait(false);
        var options = new LedgerJournalStoreOptions
        {
            ConnectionString = server.ConnectionString,
            SchemaName = server.CreateSchemaName("ledger")
        };

        var database = new LedgerPostgresTestDatabase(server, options);
        try
        {
            var runner = new LedgerMigrationRunner(options);
            await runner.EnsureMigratedAsync(ct).ConfigureAwait(false);
            return database;
        }
        catch
        {
            await database.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<LedgerAccountingPeriod> SavePeriodAsync(
        Guid periodId,
        string status,
        CancellationToken ct = default)
    {
        var openedAt = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var closedAt = string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase)
            ? (DateTimeOffset?)null
            : DateTimeOffset.Parse("2026-05-31T23:59:59Z");

        var period = new LedgerAccountingPeriod(
            periodId,
            LedgerBookId: null,
            FiscalYear: 2026,
            PeriodNo: 5,
            Label: $"2026-05-{periodId:N}"[..15],
            StartDate: new DateOnly(2026, 5, 1),
            EndDate: new DateOnly(2026, 5, 31),
            Status: status,
            OpenedAt: openedAt,
            ClosedAt: closedAt,
            Version: 0);

        return await JournalStore.SavePeriodAsync(period, expectedVersion: 0, ct: ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlySet<string>> GetTableNamesAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(Options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select table_name
            from information_schema.tables
            where table_schema = @schema_name
            order by table_name;
            """;
        command.Parameters.AddWithValue("schema_name", Options.SchemaName);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync().ConfigureAwait(false);
    }
}
