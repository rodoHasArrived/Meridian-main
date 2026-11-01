using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.DirectLending;
using Meridian.Application.FundStructure;
using Meridian.Platform.FundOperationsPersistence;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Application.UI;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Store;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Instruments.FixedIncome;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage;
using Meridian.Storage.DirectLending;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.FundAccounts;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Meridian.Tests.Application.Composition;

[Collection("Sequential")]
public sealed class StorageFeatureRegistrationTests : IDisposable
{
    private readonly string? _originalSecurityMasterConnectionString;
    private readonly string? _originalSecurityMasterSchema;
    private readonly string? _originalDirectLendingConnectionString;
    private readonly string? _originalDirectLendingSchema;
    private readonly string? _originalAssetOperationsConnectionString;
    private readonly string? _originalAssetOperationsSchema;
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
    private readonly string? _originalUnifiedDatabaseUrl;
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
        _originalAssetOperationsConnectionString = Environment.GetEnvironmentVariable(AssetOperationsStartup.ConnectionStringVariable);
        _originalAssetOperationsSchema = Environment.GetEnvironmentVariable(AssetOperationsStartup.SchemaVariable);
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
        _originalUnifiedDatabaseUrl = Environment.GetEnvironmentVariable(Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable);
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
            sd.ImplementationType == typeof(TenantPostureHostedService<DirectLendingOutboxDispatcher>));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(TenantPostureHostedService<DailyAccrualWorker>));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_ResolvesOneCanonicalSecurityMasterQueryContract(bool configured)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true",
            [SecurityMasterStartup.ConnectionStringVariable] = configured
                ? "Host=unused;Database=unused;Username=test;Password=test"
                : null
        };
        var services = new ServiceCollection();
        services.AddLogging();
        new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build()
        });
        using var provider = services.BuildServiceProvider();

        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(ISecurityMasterQueryService));
        var query = provider.GetRequiredService<ISecurityMasterQueryService>();
        provider.GetServices<ISecurityMasterQueryService>().Should().ContainSingle().Which.Should().BeSameAs(query);
        provider.GetRequiredService<ISecurityIdentityQueryService>().Should().BeSameAs(query);
        provider.GetRequiredService<ISecurityTradingParametersQueryService>().Should().BeSameAs(query);
        provider.GetRequiredService<ISecurityCorporateActionsQueryService>().Should().BeSameAs(query);
        if (configured)
        {
            query.Should().BeSameAs(provider.GetRequiredService<SecurityMasterQueryService>());
        }
        else
        {
            query.Should().BeOfType<NullSecurityMasterQueryService>();
        }
    }

    [Fact]
    public void Register_PreservesCallerSuppliedConsumerQueryContracts()
    {
        var services = new ServiceCollection();
        var identity = Substitute.For<ISecurityIdentityQueryService>();
        var trading = Substitute.For<ISecurityTradingParametersQueryService>();
        var corporateActions = Substitute.For<ISecurityCorporateActionsQueryService>();
        services.AddSingleton(identity);
        services.AddSingleton(trading);
        services.AddSingleton(corporateActions);
        services.AddLogging();
        new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Test",
                ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true"
            }).Build()
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISecurityIdentityQueryService>().Should().BeSameAs(identity);
        provider.GetRequiredService<ISecurityTradingParametersQueryService>().Should().BeSameAs(trading);
        provider.GetRequiredService<ISecurityCorporateActionsQueryService>().Should().BeSameAs(corporateActions);
    }

    [Fact]
    public async Task MinimalComposition_ResolvesAmortizationWithTheRetainedBondPolicyAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-amortization-composition-" + Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(directory, "appsettings.json");
        var dataRoot = Path.Combine(directory, "data");
        Directory.CreateDirectory(directory);
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { DataRoot = dataRoot }));
        var options = CompositionOptions.Minimal with
        {
            ConfigPath = configPath,
            EnableProcessWideHostedServices = false,
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Test",
                ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true"
            }).Build()
        };
        try
        {
            using (var provider = new ServiceCollection().AddLogging().AddMarketDataServices(options).BuildServiceProvider())
            {
                provider.GetRequiredService<ConfigStore>().ConfigPath.Should().Be(configPath);
                provider.GetRequiredService<StorageOptions>().RootPath.Should().Be(dataRoot);
                provider.GetRequiredService<CanonicalLotAmortizationService>().Should().NotBeNull();
                provider.GetRequiredService<IBondCalculationPolicy>().Should()
                    .BeSameAs(provider.GetRequiredService<FileBondCalculationPolicy>());
                (await provider.GetRequiredService<FileBondCalculationPolicy>().ReadAsync()).Should().BeNull();
            }

            // A retained enabled marker without its receipt must still block after the graph is
            // rebuilt. Composition must not substitute an in-memory or permissive policy.
            var policyDirectory = Path.Combine(dataRoot, "bonds");
            Directory.CreateDirectory(policyDirectory);
            File.WriteAllText(Path.Combine(policyDirectory, "cutover.json.enabled"), new string('a', 64));
            using var restarted = new ServiceCollection().AddLogging().AddMarketDataServices(options).BuildServiceProvider();
            restarted.GetRequiredService<CanonicalLotAmortizationService>().Should().NotBeNull();
            await restarted.GetRequiredService<FileBondCalculationPolicy>().Invoking(policy => policy.ReadAsync())
                .Should().ThrowAsync<InvalidDataException>().WithMessage("*receipt is missing*");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Register_UsesCallerSuppliedCanonicalQueryForApplicationValidation()
    {
        var services = new ServiceCollection();
        var query = Substitute.For<ISecurityMasterQueryService>();
        services.AddSingleton(query);
        services.AddSingleton(Substitute.For<ISecurityValidationSnapshotStore>());
        services.AddLogging();
        new StorageFeatureRegistration().Register(services, CompositionOptions.Minimal with
        {
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Test",
                ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true"
            }).Build()
        });
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();

        var result = await provider.GetRequiredService<ISecurityValidationGateService>()
            .ValidateSymbolAsync(" unknown ", SecurityValidationWorkflowDto.StrategyRunPreflight,
                ct: cancellation.Token);

        result.IsBlocked.Should().BeTrue();
        result.IsResolved.Should().BeFalse();
        await query.Received(1).GetByIdentifierAsync(
            SecurityIdentifierKind.Ticker, "UNKNOWN", null, cancellation.Token);
        provider.GetRequiredService<ISecurityMasterQueryService>().Should().BeSameAs(query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_ResolvesCanonicalSeederAndLookupAsTheSameSingleton(bool enableSymbolManagement)
    {
        ConfigureInMemoryGovernanceFixture();
        ClearPostgresBackedStorageEnvironment();
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable,
            "Host=unused;Database=unused;Username=test;Password=test");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(NSubstitute.Substitute.For<Meridian.Contracts.Catalog.ICanonicalSymbolRegistry>());
        new StorageFeatureRegistration().Register(services,
            enableSymbolManagement ? CompositionOptions.WebDashboard : CompositionOptions.Minimal);
        using var provider = services.BuildServiceProvider();

        var seed = provider.GetRequiredService<SecurityMasterCanonicalSymbolSeedService>();
        provider.GetRequiredService<Meridian.DataIntegration.Canonicalization.ICanonicalSecurityIdLookup>()
            .Should().BeSameAs(seed);
        provider.GetRequiredService<SecurityMasterProjectionChangeHandler>().Should().NotBeNull();
    }

    [Fact]
    public async Task MinimalComposition_ResolvesAndStartsProjectionHostedServiceWithoutCanonicalRegistry()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true",
            [SecurityMasterStartup.ConnectionStringVariable] = "Host=unused;Database=unused;Username=test;Password=test",
            ["MERIDIAN_SECURITY_MASTER_PRELOAD_CACHE"] = "false",
            ["MERIDIAN_SECURITY_MASTER_PROJECTION_NOTIFICATIONS"] = "false"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketDataServices(CompositionOptions.Minimal with { Configuration = configuration });
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(Meridian.Contracts.Catalog.ICanonicalSymbolRegistry));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(SecurityMasterCanonicalSymbolSeedService));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(Meridian.DataIntegration.Canonicalization.ICanonicalSecurityIdLookup));

        // Exercise the exact hosted registration without starting unrelated storage workers.
        var warmupRegistration = services.Single(descriptor => descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(SecurityMasterProjectionWarmupService));
        services.RemoveAll<IHostedService>();
        services.Add(warmupRegistration);
        await using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISecurityMasterService>().Should().BeOfType<SecurityMasterService>();
        provider.GetRequiredService<SecurityMasterProjectionChangeHandler>().Should().NotBeNull();
        var warmup = provider.GetServices<IHostedService>().Should().ContainSingle().Which;

        await warmup.StartAsync(CancellationToken.None);
        await warmup.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void SymbolManagementComposition_ResolvesSeederRegisteredBeforeCanonicalRegistry()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Test",
            ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true",
            [SecurityMasterStartup.ConnectionStringVariable] = "Host=unused;Database=unused;Username=test;Password=test"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketDataServices(CompositionOptions.Minimal with
        {
            Configuration = configuration,
            EnableSymbolManagement = true
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<Meridian.Contracts.Catalog.ICanonicalSymbolRegistry>().Should().BeOfType<CanonicalSymbolRegistry>();
        var seed = provider.GetRequiredService<SecurityMasterCanonicalSymbolSeedService>();
        provider.GetRequiredService<Meridian.DataIntegration.Canonicalization.ICanonicalSecurityIdLookup>()
            .Should().BeSameAs(seed);
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
    public void Register_AliasesAssetOperationsInterfacesToSameInMemoryStore_WhenDatabaseIsNotConfigured()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearPostgresBackedStorageEnvironment();
        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        using var provider = services.BuildServiceProvider();
        var legacyStore = provider.GetRequiredService<IAssetOperationsProjectionStore>();
        legacyStore.Should().BeOfType<InMemoryAssetOperationsProjectionStore>();
        provider.GetRequiredService<IInstrumentPositionProjectionStore>()
            .Should()
            .BeSameAs(legacyStore);
    }

    [Fact]
    public void Register_AliasesAssetOperationsInterfacesToSamePostgresStore_WhenDatabaseIsConfigured()
    {
        ConfigureInMemoryGovernanceFixture();
        ClearPostgresBackedStorageEnvironment();
        Environment.SetEnvironmentVariable(
            AssetOperationsStartup.ConnectionStringVariable,
            "Host=asset-operations-db;Port=5432;Database=asset_operations;Username=postgres;Password=secret");
        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        using var provider = services.BuildServiceProvider();
        var legacyStore = provider.GetRequiredService<IAssetOperationsProjectionStore>();
        legacyStore.Should().BeOfType<PostgresAssetOperationsProjectionStore>();
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(InMemoryAssetOperationsProjectionStore));
        provider.GetRequiredService<IInstrumentPositionProjectionStore>()
            .Should()
            .BeSameAs(legacyStore);
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
            .Should().BeOfType<RoutingBrokerStatementService>();
        provider.GetRequiredService<CsvBrokerStatementService>().Should().NotBeNull();
        provider.GetRequiredService<IbFlexBrokerStatementService>().Should().NotBeNull();
        provider.GetRequiredService<IStatementReconciliationCheckpointStore>()
            .Should().BeOfType<FileStatementReconciliationCheckpointStore>();
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
            sd.ImplementationType == typeof(TenantPostureHostedService<DirectLendingOutboxDispatcher>));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(TenantPostureHostedService<DailyAccrualWorker>));
    }

    [Fact]
    public void Register_Throws_WhenProductionGovernancePersistenceIsMissing()
    {
        ClearPostgresBackedStorageEnvironment();
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", null);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Production);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Production);

        var services = new ServiceCollection();

        var act = () => new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MERIDIAN_FUND_ACCOUNTS_CONNECTION_STRING*")
            .WithMessage("*MERIDIAN_FUND_STRUCTURE_CONNECTION_STRING*")
            .WithMessage("*MERIDIAN_USE_INMEMORY_GOVERNANCE=true*");
    }

    [Fact]
    public void Register_AddsPostgresGovernanceServices_WhenProductionPersistenceIsConfigured()
    {
        ClearPostgresBackedStorageEnvironment();
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", null);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Production);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Production);
        Environment.SetEnvironmentVariable(FundAccountsStartup.ConnectionStringVariable, "Host=fa-db;Port=5432;Database=fund_accounts;Username=postgres;Password=secret");
        Environment.SetEnvironmentVariable(FundAccountsStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(FundStructureStartup.ConnectionStringVariable, "Host=fs-db;Port=5432;Database=fund_structure;Username=postgres;Password=secret");
        Environment.SetEnvironmentVariable(FundStructureStartup.SchemaVariable, null);

        var services = new ServiceCollection();

        new StorageFeatureRegistration().Register(services, CompositionOptions.WebDashboard);

        services.Should().ContainSingle(sd => sd.ServiceType == typeof(FundAccountStoreOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IFundAccountStore) &&
            sd.ImplementationType == typeof(PostgresFundAccountStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(PostgresFundAccountService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IFundAccountService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(FundStructureStoreOptions));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IFundStructureStore) &&
            sd.ImplementationType == typeof(PostgresFundStructureStore));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(PostgresFundStructureService));
        services.Should().ContainSingle(sd => sd.ServiceType == typeof(IFundStructureService));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IDomainProjectionReconciliationJob));
        services.Should().NotContain(sd => sd.ServiceType == typeof(IHostedService) &&
            sd.ImplementationType == typeof(ProjectionReconciliationHostedService));
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
        Environment.SetEnvironmentVariable(Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(SecurityMasterStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(DirectLendingStartup.SchemaVariable, null);
        Environment.SetEnvironmentVariable(AssetOperationsStartup.ConnectionStringVariable, null);
        Environment.SetEnvironmentVariable(AssetOperationsStartup.SchemaVariable, null);
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
        Environment.SetEnvironmentVariable(AssetOperationsStartup.ConnectionStringVariable, _originalAssetOperationsConnectionString);
        Environment.SetEnvironmentVariable(AssetOperationsStartup.SchemaVariable, _originalAssetOperationsSchema);
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
        Environment.SetEnvironmentVariable(Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable, _originalUnifiedDatabaseUrl);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING", _originalScopedAccessConnectionString);
        Environment.SetEnvironmentVariable("MERIDIAN_SCOPED_ACCESS_SCHEMA", _originalScopedAccessSchema);
        Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", _originalUseInMemoryGovernance);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", _originalDotnetEnvironment);
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", _originalAspNetCoreEnvironment);
    }
}
