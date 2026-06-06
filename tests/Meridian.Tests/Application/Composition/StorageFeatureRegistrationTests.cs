using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.DirectLending;
using Meridian.Platform.FundOperationsPersistence;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Application.UI;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Store;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class StorageFeatureRegistrationTests : IDisposable
{
    private readonly string? _originalSecurityMasterConnectionString;
    private readonly string? _originalSecurityMasterSchema;
    private readonly string? _originalDirectLendingConnectionString;
    private readonly string? _originalDirectLendingSchema;
    private readonly string? _originalLedgerConnectionString;
    private readonly string? _originalLedgerSchema;
    private readonly string? _originalFundAccountsConnectionString;
    private readonly string? _originalFundAccountsSchema;
    private readonly string? _originalFundStructureConnectionString;
    private readonly string? _originalFundStructureSchema;
    private readonly string? _originalBankingConnectionString;
    private readonly string? _originalBankingSchema;
    private readonly string? _originalMoneyMarketConnectionString;
    private readonly string? _originalMoneyMarketSchema;
    private readonly string? _originalScopedAccessConnectionString;
    private readonly string? _originalScopedAccessSchema;
    private readonly string? _originalUseInMemoryGovernance;
    private readonly string? _originalDotnetEnvironment;
    private readonly string? _originalAspNetCoreEnvironment;

    public StorageFeatureRegistrationTests()
    {
        _originalSecurityMasterConnectionString = Environment.GetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable);
        _originalSecurityMasterSchema = Environment.GetEnvironmentVariable(SecurityMasterStartup.SchemaVariable);
        _originalDirectLendingConnectionString = Environment.GetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable);
        _originalDirectLendingSchema = Environment.GetEnvironmentVariable(DirectLendingStartup.SchemaVariable);
        _originalLedgerConnectionString = Environment.GetEnvironmentVariable(LedgerStartup.ConnectionStringVariable);
        _originalLedgerSchema = Environment.GetEnvironmentVariable(LedgerStartup.SchemaVariable);
        _originalFundAccountsConnectionString = Environment.GetEnvironmentVariable(FundAccountsStartup.ConnectionStringVariable);
        _originalFundAccountsSchema = Environment.GetEnvironmentVariable(FundAccountsStartup.SchemaVariable);
        _originalFundStructureConnectionString = Environment.GetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable);
        _originalFundStructureSchema = Environment.GetEnvironmentVariable(FundStructureStartup.SchemaVariable);
        _originalBankingConnectionString = Environment.GetEnvironmentVariable(BankingStartup.ConnectionStringVariable);
        _originalBankingSchema = Environment.GetEnvironmentVariable(BankingStartup.SchemaVariable);
        _originalMoneyMarketConnectionString = Environment.GetEnvironmentVariable(MoneyMarketStartup.ConnectionStringVariable);
        _originalMoneyMarketSchema = Environment.GetEnvironmentVariable(MoneyMarketStartup.SchemaVariable);
        _originalScopedAccessConnectionString = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING");
        _originalScopedAccessSchema = Environment.GetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA");
        _originalUseInMemoryGovernance = Environment.GetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE");
        _originalDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        _originalAspNetCoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    }

    [Fact]
    public void Register_SkipsPostgresBackedServices_WhenConnectionStringsAreMissing()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearPostgresBackedStorageEnvironment();

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().NotContain(sd => sd.ServiceType == typeof(SecurityMasterOptions));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IValidateOptions<SecurityMasterOptions>));
        services.Should().NotContain(sd => sd.ServiceType == typeof(ISecurityMasterStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ISecurityMasterIngestStatusService));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IPolygonCorporateActionFetcher));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IUflProjectionRebuilder) && sd.ImplementationType == typeof(NullUflProjectionRebuilder));
        services.Should().NotContain(sd => sd.ServiceType == typeof(LedgerJournalStoreOptions));
        services.Should().NotContain(sd => sd.ServiceType == typeof(ILedgerJournalStore));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IOperationsContinuityTransactionalCommitStore));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IDirectLendingStateStore));
        services.Should().NotContain(sd =>
            sd.ServiceType == typeof(IDirectLendingService) &&
            sd.ImplementationType == typeof(PostgresDirectLendingService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDomainProjectionReconciliationJob));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(ProjectionReconciliationHostedService));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(SecurityMasterProjectionWarmupService));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DirectLendingOutboxDispatcher));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DailyAccrualWorker));
    }

    [Fact]
    public void Register_AddsStorageCatalogService_ForEtlAndCatalogConsumers()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearPostgresBackedStorageEnvironment();

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(StorageCatalogService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IStorageCatalogService));
    }

    [Fact]
    public void Register_AddsStatementReconciliationContextServices()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearPostgresBackedStorageEnvironment();

        var services = new ServiceCollection();
        services.AddSingleton(new ConfigStore(Path.Combine(Path.GetTempPath(), $"meridian-storage-feature-{Guid.NewGuid():N}", "appsettings.json")));

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<StatementReconciliationContextAdapter>().Should().NotBeNull();
        provider.GetRequiredService<IStatementReconciliationValidationService>()
            .Should().BeSameAs(provider.GetRequiredService<StatementReconciliationContextAdapter>());
        provider.GetRequiredService<IDataIntegrationIngestionService>()
            .Should().BeSameAs(provider.GetRequiredService<StatementReconciliationContextAdapter>());
        provider.GetRequiredService<IReconciliationCaseIntakeService>()
            .Should().BeSameAs(provider.GetRequiredService<StatementReconciliationContextAdapter>());
        provider.GetRequiredService<IStatementRunWorkflowService>()
            .Should().BeOfType<StatementRunWorkflowService>();
        provider.GetRequiredService<IBrokerStatementService>()
            .Should().BeOfType<CsvBrokerStatementService>();
        provider.GetRequiredService<IStatementReconciliationCheckpointStore>()
            .Should().BeOfType<InMemoryStatementReconciliationCheckpointStore>();
        provider.GetRequiredService<StatementReconciliationOrchestrator>().Should().NotBeNull();
    }

    [Fact]
    public void Register_AddsPostgresBackedServices_WhenConnectionStringsAreConfigured()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearGovernancePersistenceEnvironment();
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, "Host=sm-db;Port=5432;Database=security;Username=postgres;Password=secret");
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, "Host=dl-db;Port=5432;Database=loans;Username=postgres;Password=secret");
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.SchemaVariable, null);

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(SecurityMasterOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IValidateOptions<SecurityMasterOptions>));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ISecurityMasterStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ISecurityMasterIngestStatusService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IPolygonCorporateActionFetcher));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IUflProjectionRebuilder) && sd.ImplementationType == typeof(UflProjectionRebuilder));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(DirectLendingOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDirectLendingStateStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDirectLendingService));
        services.Should().Contain(sd => sd.ServiceType == typeof(IDomainProjectionReconciliationJob));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(ProjectionReconciliationHostedService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(SecurityMasterProjectionWarmupService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DirectLendingOutboxDispatcher));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(DailyAccrualWorker));
    }

    [Fact]
    public void Register_AddsDirectLendingServicesInsideSecurityMasterStorage_WhenOnlySecurityMasterIsConfigured()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearGovernancePersistenceEnvironment();
        const string securityMasterConnectionString = "Host=sm-db;Port=5432;Database=security;Username=postgres;Password=secret";
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, securityMasterConnectionString);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, "security_master_ops");
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.SchemaVariable, null);

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(DirectLendingOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDirectLendingStateStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(DirectLendingMigrationRunner));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IDirectLendingService));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DirectLendingOptions>();
        options.ConnectionString.Should().Be(securityMasterConnectionString);
        options.Schema.Should().Be("security_master_ops");
    }

    [Fact]
    public void Register_ResolvesProjectionReconciliationJobs_ForEachFundOperationsDomain()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearPostgresBackedStorageEnvironment();

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        using var provider = services.BuildServiceProvider();
        var jobs = provider.GetRequiredService<IEnumerable<IDomainProjectionReconciliationJob>>();

        jobs.Select(job => job.Domain).Should().BeEquivalentTo(Enum.GetValues<FundOperationsDomain>());
    }

    [Fact]
    public void Register_AddsLedgerBackedOperationsContinuityServices_WhenLedgerConnectionStringIsConfigured()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearGovernancePersistenceEnvironment();
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.ConnectionStringVariable, "Host=ledger-db;Port=5432;Database=ledger;Username=postgres;Password=secret");
        Environment.SetEnvironmentVariable(LedgerStartup.SchemaVariable, null);

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(LedgerJournalStoreOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(PostgresLedgerJournalStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ILedgerJournalStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ITransactionalLedgerJournalStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(LedgerMigrationRunner));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(ILedgerBookService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(PostgresOperationsContinuityStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IOperationsContinuityRepository));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IOperationsWorkflowAuditStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IOperationsContinuityTransactionalCommitStore));
    }

    private static void ConfigureInMemoryGovernanceFixture()
    {
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
    }

    private static void ClearPostgresBackedStorageEnvironment()
    {
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(LedgerStartup.SchemaVariable, null);
        ClearGovernancePersistenceEnvironment();
    }

    private static void ClearGovernancePersistenceEnvironment()
    {
        Environment.SetEnvironmentVariable(FundAccountsStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(FundAccountsStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(FundStructureStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(BankingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(BankingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(MoneyMarketStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(MoneyMarketStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING", null);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, _originalSecurityMasterConnectionString);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, _originalSecurityMasterSchema);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, _originalDirectLendingConnectionString);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, _originalDirectLendingSchema);
        Environment.SetEnvironmentVariable(LedgerStartup.ConnectionStringVariable, _originalLedgerConnectionString);
        Environment.SetEnvironmentVariable(LedgerStartup.SchemaVariable, _originalLedgerSchema);
        Environment.SetEnvironmentVariable(FundAccountsStartup.ConnectionStringVariable, _originalFundAccountsConnectionString);
        Environment.SetEnvironmentVariable(FundAccountsStartup.SchemaVariable, _originalFundAccountsSchema);
        Environment.SetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable, _originalFundStructureConnectionString);
        Environment.SetEnvironmentVariable(FundStructureStartup.SchemaVariable, _originalFundStructureSchema);
        Environment.SetEnvironmentVariable(BankingStartup.ConnectionStringVariable, _originalBankingConnectionString);
        Environment.SetEnvironmentVariable(BankingStartup.SchemaVariable, _originalBankingSchema);
        Environment.SetEnvironmentVariable(MoneyMarketStartup.ConnectionStringVariable, _originalMoneyMarketConnectionString);
        Environment.SetEnvironmentVariable(MoneyMarketStartup.SchemaVariable, _originalMoneyMarketSchema);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING", _originalScopedAccessConnectionString);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA", _originalScopedAccessSchema);
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", _originalUseInMemoryGovernance);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnvironment);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspNetCoreEnvironment);
    }
}
