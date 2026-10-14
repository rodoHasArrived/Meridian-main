using System.Globalization;
using Meridian.Storage.Archival;
using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;
using Meridian.FinancialOperations.Reconciliation.Connectors.Camt;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ledger;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Reporting;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.Ledger;
using Meridian.Storage.Reporting;
using Meridian.Strategies.Services;
using Meridian.TestSupport;
using Meridian.Tests.Reconciliation.Connectors;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Meridian.Tests.Integration;

public sealed partial class StatementLedgerReconciliationPostgresTests
{
    private const string TenantId = "statement-ledger-tenant";
    private const string CompanyId = "statement-ledger-company";
    private const string Institution = "May Bank";
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);
    private static readonly ReconciliationBreakQueueScope AccessScope = new(TenantId, CompanyId);
    private static readonly BankStatementScenario Bai2Scenario = new(
        "bai2-sample.bai", Bai2StatementConnector.ConnectorId, "0975312468", "USD",
        "CUSTREF01", "CUSTREF02", PeriodEnd, PeriodEnd, PeriodEnd, PeriodEnd);
    private static readonly BankStatementScenario Camt053Scenario = new(
        "camt053-sample.xml", Camt053StatementConnector.ConnectorId, "DE89370400440532013000", "EUR",
        "ACCTSVCR-001", "ACCTSVCR-002", new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 11),
        new DateOnly(2026, 5, 20), new DateOnly(2026, 5, 20));

    private sealed record BankStatementScenario(
        string FixtureName,
        string ConnectorId,
        string ExternalAccountId,
        string Currency,
        string SplitExternalId,
        string PairExternalId,
        DateOnly SplitTradeDate,
        DateOnly SplitSettlementDate,
        DateOnly PairTradeDate,
        DateOnly PairSettlementDate);

    private sealed class ScenarioDatabase : IAsyncDisposable
    {
        private readonly PostgresTestServer _server;

        private ScenarioDatabase(PostgresTestServer server, BankStatementScenario scenario)
        {
            _server = server;
            Scenario = scenario;
            Root = StatementConnectorTestData.CreateTempRoot("statement-ledger-postgres");
            LedgerOptions = new LedgerJournalStoreOptions
            {
                ConnectionString = server.ConnectionString,
                SchemaName = server.CreateSchemaName("statement_ledger"),
                RequireGovernedPostingCommand = true,
                RequireExpectedVersion = true
            };
            AccountOptions = new FundAccountStoreOptions
            {
                ConnectionString = server.ConnectionString,
                Schema = server.CreateSchemaName("statement_accounts")
            };
            ReportingOptions = new ReportingArtifactStoreOptions
            {
                ConnectionString = server.ConnectionString,
                Schema = server.CreateSchemaName("statement_reporting")
            };
        }

        public string Root { get; }
        public BankStatementScenario Scenario { get; }
        public Guid AccountId { get; } = Guid.NewGuid();
        public Guid FundId { get; } = Guid.NewGuid();
        public LedgerJournalStoreOptions LedgerOptions { get; }
        public FundAccountStoreOptions AccountOptions { get; }
        public ReportingArtifactStoreOptions ReportingOptions { get; }
        public LedgerBookDto Book { get; private set; } = null!;
        public LedgerPeriodDto Period { get; private set; } = null!;
        public LedgerBookDto OtherBook { get; private set; } = null!;
        public LedgerPeriodDto OtherPeriod { get; private set; } = null!;
        public StatementAccountingScope Scope => new(FundId.ToString("D"), Book.LedgerBookId, Period.PeriodId, PeriodEnd);

        public static async Task<ScenarioDatabase> CreateAsync(CancellationToken ct,
            BankStatementScenario? scenario = null)
        {
            var server = await PostgresTestServer.CreateAsync("MERIDIAN_REPORTING_CONNECTION_STRING", ct: ct);
            var database = new ScenarioDatabase(server, scenario ?? Bai2Scenario);
            try
            {
                await new LedgerMigrationRunner(database.LedgerOptions).EnsureMigratedAsync(ct);
                await new FundAccountMigrationRunner(database.AccountOptions).EnsureMigratedAsync(ct);
                await new ReportingMigrationRunner(database.ReportingOptions).EnsureMigratedAsync(ct);
                await database.SeedScopeAsync(ct);
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        private async Task SeedScopeAsync(CancellationToken ct)
        {
            var accounts = new PostgresFundAccountService(new PostgresFundAccountStore(AccountOptions));
            await accounts.CreateAccountAsync(new CreateAccountRequest(AccountId, AccountTypeDto.Bank,
                Scenario.ExternalAccountId, "May operating account", Scenario.Currency,
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), "bank-operations",
                FundId: FundId, Institution: Institution), ct);
            await new PostgresFundProfileTenancyRegistry(LedgerOptions)
                .BindAsync(FundId.ToString("D"), TenantId, CompanyId, ct);
            var books = new PostgresLedgerBookService(new PostgresLedgerJournalStore(LedgerOptions));
            Book = await books.CreateBookAsync(new CreateLedgerBookRequest(FundId.ToString("D"),
                FundId, FundStructureNodeKindDto.Fund, "Primary bank book", Scenario.Currency), ct);
            Period = await books.CreatePeriodAsync(new CreateLedgerPeriodRequest(Book.LedgerBookId,
                2026, 5, "May 2026", PeriodStart, PeriodEnd)
            { CreatedBy = "controller" }, ct);
            OtherBook = await books.CreateBookAsync(new CreateLedgerBookRequest(FundId.ToString("D"),
                FundId, FundStructureNodeKindDto.Fund, "GAAP comparison book", Scenario.Currency,
                AccountingBasis: AccountingBasisKindDto.Gaap), ct);
            OtherPeriod = await books.CreatePeriodAsync(new CreateLedgerPeriodRequest(OtherBook.LedgerBookId,
                2026, 5, "May 2026", PeriodStart, PeriodEnd)
            { CreatedBy = "controller" }, ct);
        }

        public Services CreateServices()
        {
            var journals = new PostgresLedgerJournalStore(LedgerOptions);
            var accounts = new PostgresFundAccountService(new PostgresFundAccountStore(AccountOptions));
            var tenancy = new PostgresFundProfileTenancyRegistry(LedgerOptions);
            var canonical = new JsonCanonicalStatementStore(Root, new AtomicFileWriterAdapter());
            var artifacts = new FileStatementRunMatchArtifactStore(Root, new AtomicFileWriterAdapter());
            var population = new RetainedInternalReconciliationPopulationProvider(accounts,
                ledgerTransactionSource: new LedgerJournalInternalTransactionSource(journals));
            var runs = new StatementRunWorkflowService(canonical,
                new JsonReconciliationCaseStore(Root, new AtomicFileWriterAdapter()), new JsonReconciliationBreakStore(Root, new AtomicFileWriterAdapter()),
                new CsvBrokerStatementService(canonical),
                new StatementReconciliationContextAdapter(new StatementReconciliationService()),
                population, IdentityReconciliationFxRateProvider.Instance,
                new InMemoryStatementToleranceProfileProvider(), new FileStatementRunRecoveryRepository(Root),
                artifacts, caseworkCommitStore: new FileStatementCaseworkCommitStore(Root, new AtomicFileWriterAdapter()));
            var catalog = new StatementMappingProfileCatalog(new FileStatementMappingProfileStore(Root));
            var imports = new StatementImportService(new StatementConnectorRegistry(
                [new Bai2StatementConnector(), new Camt053StatementConnector()]),
                catalog, runs, Root);
            var feed = new ReconciliationApiService(runs, accounts, tenancy);
            var queue = new FileReconciliationBreakQueueRepository(Path.Combine(Root, "workstation"),
                NullLogger<FileReconciliationBreakQueueRepository>.Instance);
            var status = new OperationsStatusDerivationService();
            var operationsStore = new PostgresOperationsContinuityStore(LedgerOptions, journals, status);
            var operations = new OperationsContinuityWorkflowService(operationsStore, operationsStore, status,
                journals, operationsStore);
            var intake = new StatementReconciliationIntakeAuthority(accounts, tenancy,
                new PostgresLedgerBookService(journals), operations, runs, feed, queue, canonical, artifacts);
            var authority = new PostgresStatementReconciliationReportAuthorityStore(ReportingOptions);
            var coordinator = new StatementReconciliationReportWorkflowService(imports,
                new ReportingStatementImportEvidenceRetainer(authority, Root), runs, Root,
                authority, logger: null, queue, intake);
            return new Services(journals, imports, runs, artifacts, feed, queue, operations, authority, coordinator);
        }

        public StatementReconciliationReportStartCommand Command() => new(
            new StatementImportCommitRequest(
                new StatementSourceDocument(Scenario.FixtureName, StatementConnectorTestData.ReadFixture(Scenario.FixtureName)),
                Scenario.ConnectorId, "custodian", Institution, AccountId.ToString("D"), Scenario.ExternalAccountId,
                PeriodStart, PeriodEnd, null, "bank-operations"), TenantId, CompanyId);

        public LedgerJournalEntryWrite Journal(decimal amount, string? externalId = null, bool otherBook = false,
            DateOnly? tradeDate = null, DateOnly? settlementDate = null)
        {
            var book = otherBook ? OtherBook : Book;
            var period = otherBook ? OtherPeriod : Period;
            var id = Guid.NewGuid();
            var at = new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero);
            var effectiveDate = tradeDate ?? PeriodEnd;
            const string description = "Reviewed bank movement";
            var cash = new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: AccountId.ToString("D"));
            var contra = new LedgerAccount("Bank settlement clearing", LedgerAccountType.Liability,
                FinancialAccountId: AccountId.ToString("D"));
            var entry = new JournalEntry(id, at, description,
            [
                new LedgerEntry(Guid.NewGuid(), id, at, cash, Math.Max(amount, 0), Math.Max(-amount, 0), description),
                new LedgerEntry(Guid.NewGuid(), id, at, contra, Math.Max(-amount, 0), Math.Max(amount, 0), description)
            ], new JournalEntryMetadata(ActivityType: "transaction", FinancialAccountId: AccountId.ToString("D"),
                EffectiveDate: effectiveDate, SettlementReference: externalId,
                Tags: settlementDate is { } settlement
                    ? new Dictionary<string, string>
                    {
                        ["settlementDate"] = settlement.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    }
                    : null));
            var command = new AccountingPostingCommandDto(Guid.NewGuid(), book.LedgerBookId, period.PeriodId,
                effectiveDate, at, $"statement-test:{id:D}", SourceEventId: Guid.NewGuid(), ExpectedVersion: period.Version,
                ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: $"controller:{id:D}",
                OperatorRationale: "Independently reviewed balanced bank movement retained for reconciliation.",
                LedgerBookId: book.LedgerBookId)
            {
                Actor = "controller",
                BookContext = new AccountingBookContextDto(book.LedgerBookId, book.FundProfileId,
                    book.FundStructureNodeId, book.FundStructureNodeKind, book.DisplayName, book.BaseCurrency,
                    book.AccountingBasis, book.AccountingPolicyId, book.AccountingPolicyVersion, period.PeriodId)
            };
            return new LedgerJournalEntryWrite(entry, book.LedgerBookId, period.PeriodId,
                AccountingBasis: book.AccountingBasis, AccountingPolicyId: book.AccountingPolicyId,
                AccountingPolicyVersion: book.AccountingPolicyVersion, PostingCommand: command, LedgerBookId: book.LedgerBookId);
        }

        public async Task<long> ReportingAuthorityRowCountAsync(CancellationToken ct)
        {
            await using var connection = new NpgsqlConnection(ReportingOptions.ConnectionString);
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                select
                    (select count(*) from "{ReportingOptions.Schema}".reporting_statement_reconciliation_documents)
                    + (select count(*) from "{ReportingOptions.Schema}".reporting_statement_reconciliation_document_revisions)
                    + (select count(*) from "{ReportingOptions.Schema}".reporting_artifact_blobs)
                """;
            return (long)(await command.ExecuteScalarAsync(ct))!;
        }

        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private sealed record Services(
        PostgresLedgerJournalStore Journals,
        StatementImportService Imports,
        StatementRunWorkflowService Runs,
        FileStatementRunMatchArtifactStore Artifacts,
        ReconciliationApiService Feed,
        FileReconciliationBreakQueueRepository Queue,
        OperationsContinuityWorkflowService Operations,
        PostgresStatementReconciliationReportAuthorityStore Authority,
        StatementReconciliationReportWorkflowService Coordinator);
}
