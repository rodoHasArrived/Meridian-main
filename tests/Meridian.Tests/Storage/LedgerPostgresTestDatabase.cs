using Meridian.Application.OperationsContinuity;
using Meridian.Storage.Ledger;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Meridian.Tests.Storage;

internal sealed class LedgerPostgresTestDatabase : IAsyncDisposable
{
    private const string ConnectionStringVariable = "MERIDIAN_LEDGER_CONNECTION_STRING";
    private readonly PostgreSqlContainer? _container;

    private LedgerPostgresTestDatabase(
        PostgreSqlContainer? container,
        LedgerJournalStoreOptions options)
    {
        _container = container;
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
        var externalConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        PostgreSqlContainer? container = null;
        LedgerJournalStoreOptions options;

        if (!string.IsNullOrWhiteSpace(externalConnectionString))
        {
            options = new LedgerJournalStoreOptions
            {
                ConnectionString = externalConnectionString,
                SchemaName = NewSchemaName()
            };
        }
        else
        {
            container = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase("meridian_test")
                .WithUsername("testuser")
                .WithPassword("testpass")
                .Build();

            await container.StartAsync(ct).ConfigureAwait(false);
            options = new LedgerJournalStoreOptions
            {
                ConnectionString = container.GetConnectionString(),
                SchemaName = NewSchemaName()
            };
        }

        var database = new LedgerPostgresTestDatabase(container, options);
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
        if (_container is null)
        {
            await DropSchemaAsync().ConfigureAwait(false);
            return;
        }

        await _container.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(Options.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"drop schema if exists {ValidateIdentifier(Options.SchemaName)} cascade;";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string NewSchemaName() => $"ledger_test_{Guid.NewGuid():N}";

    private static string ValidateIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Schema name is required.");
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw new InvalidOperationException("Schema name contains an invalid identifier character.");
            }
        }

        return value;
    }
}
