using System.Text.Json;
using Meridian.Application.DirectLending;
using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.SecurityMaster;
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

    internal static Guid TestSecurityId { get; } = Guid.Parse("d1643625-caa0-4fa5-98fb-64e202915a28");

    internal const string TestSecuritySymbol = "DL-TEST";

    private static readonly ITransactionalLedgerJournalStore _noOpLedgerJournalStore = new InMemoryNoOpLedgerJournalStore();
    private static readonly Meridian.Application.SecurityMaster.ISecurityMasterQueryService _securityMasterQueryService =
        new DeterministicSecurityMasterQueryService();

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

        Store = new PostgresDirectLendingStateStore(Options, _noOpLedgerJournalStore);
        Rebuilder = new DirectLendingEventRebuilder();
        QueryService = new PostgresDirectLendingQueryService(Store, Store, Rebuilder);
        CommandService = new PostgresDirectLendingCommandService(
            Store,
            Store,
            QueryService,
            new LoanAccountingProjector(
                _noOpLedgerJournalStore,
                new AccountingPolicyService(),
                _securityMasterQueryService),
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

    private sealed class InMemoryNoOpLedgerJournalStore : ITransactionalLedgerJournalStore
    {
        private static readonly LedgerAccountingPeriod TestAccountingPeriod = new(
            PeriodId: Guid.Parse("58190d5b-2306-4e0d-a818-a2fb5e087bbf"),
            LedgerBookId: null,
            FiscalYear: 2026,
            PeriodNo: 1,
            Label: "2026",
            StartDate: new DateOnly(2026, 1, 1),
            EndDate: new DateOnly(2026, 12, 31),
            Status: "Open",
            OpenedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ClosedAt: null,
            Version: 0);

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) => Task.CompletedTask;

        public Task AppendAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            LedgerJournalEntryWrite entry,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>([]);

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            Task.FromResult<LedgerAccountingPeriod?>(
                periodId == TestAccountingPeriod.PeriodId ? TestAccountingPeriod : null);

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerAccountingPeriod>>([TestAccountingPeriod]);

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            Task.FromResult(period);

        public Task<LedgerAccountingPeriod> SaveHardClosedPeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord closeEvent,
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

    private sealed class DeterministicSecurityMasterQueryService :
        Meridian.Application.SecurityMaster.ISecurityMasterQueryService
    {
        private static readonly SecurityDetailDto TestSecurity = new(
            SecurityId: TestSecurityId,
            AssetClass: "PrivateCredit",
            Status: SecurityStatusDto.Active,
            DisplayName: TestSecuritySymbol,
            Currency: "USD",
            CommonTerms: JsonSerializer.SerializeToElement(new { }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Identifiers: [],
            Aliases: [],
            Version: 1,
            EffectiveFrom: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null);

        public Task<SecurityDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default) =>
            Task.FromResult<SecurityDetailDto?>(securityId == TestSecurityId ? TestSecurity : null);

        public Task<SecurityDetailDto?> GetByIdAsOfAsync(
            Guid securityId,
            DateTimeOffset asOfUtc,
            CancellationToken ct = default) =>
            GetByIdAsync(securityId, ct);

        public Task<SecurityDetailDto?> GetByIdentifierAsync(
            SecurityIdentifierKind identifierKind,
            string identifierValue,
            string? provider,
            CancellationToken ct = default,
            DateTimeOffset? asOfUtc = null) =>
            Task.FromResult<SecurityDetailDto?>(
                string.Equals(identifierValue, TestSecuritySymbol, StringComparison.OrdinalIgnoreCase)
                    ? TestSecurity
                    : null);

        public Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(
            SecuritySearchRequest request,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SecuritySummaryDto>>([]);

        public Task<IReadOnlyList<SecurityMasterEventEnvelope>> GetHistoryAsync(
            SecurityHistoryRequest request,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SecurityMasterEventEnvelope>>([]);

        public Task<SecurityEconomicDefinitionRecord?> GetEconomicDefinitionByIdAsync(
            Guid securityId,
            CancellationToken ct = default) =>
            Task.FromResult<SecurityEconomicDefinitionRecord?>(null);

        public Task<TradingParametersDto?> GetTradingParametersAsync(
            Guid securityId,
            DateTimeOffset asOf,
            CancellationToken ct = default) =>
            Task.FromResult<TradingParametersDto?>(null);

        public Task<IReadOnlyList<CorporateActionDto>> GetCorporateActionsAsync(
            Guid securityId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CorporateActionDto>>([]);

        public Task<PreferredEquityTermsDto?> GetPreferredEquityTermsAsync(
            Guid securityId,
            CancellationToken ct = default) =>
            Task.FromResult<PreferredEquityTermsDto?>(null);

        public Task<ConvertibleEquityTermsDto?> GetConvertibleEquityTermsAsync(
            Guid securityId,
            CancellationToken ct = default) =>
            Task.FromResult<ConvertibleEquityTermsDto?>(null);
    }
}
