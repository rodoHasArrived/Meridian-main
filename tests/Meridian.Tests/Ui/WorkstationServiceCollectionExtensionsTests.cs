using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.DataIntegration.Credentials;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity;
using Meridian.Reporting;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;
using IReportingScheduleStore = Meridian.Reporting.IReportingScheduleStore;
#pragma warning disable CS0618
using LegacyReportingScheduleStore = Meridian.Ui.Shared.Services.IReportingScheduleStore;
#pragma warning restore CS0618

namespace Meridian.Tests.Ui;

[Collection("Sequential")]
public sealed class WorkstationServiceCollectionExtensionsTests
{
    [Fact]
    public void AddUiSharedServices_DefaultProviderCatalog_ResolvesEveryProviderAndAliasExactlyOnce()
    {
        using var quietProductionEnvironment =
            new Meridian.Tests.Application.Composition.ProductionEnvironmentQuietScope();
        using var environment = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "ASPNETCORE_ENVIRONMENT",
            "Test");
        using var inMemoryGovernance = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_USE_INMEMORY_GOVERNANCE",
            "true");
        var expectedHandlers = DefaultProviderSetupHandlers.Create();
        var services = CreateMinimalWorkstationServices();

        services.AddUiSharedServices();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IProviderSetupRegistry>();
        registry.Handlers
            .Select(static handler => handler.Descriptor.ProviderId)
            .Should()
            .Equal(expectedHandlers.Select(static handler => handler.Descriptor.ProviderId));

        foreach (var expectedHandler in expectedHandlers)
        {
            var expectedProviderId = expectedHandler.Descriptor.ProviderId;
            var supportedLookups = expectedHandler.Descriptor.Aliases
                .Prepend(expectedProviderId);

            foreach (var lookup in supportedLookups)
            {
                registry.Find(lookup)?.Descriptor.ProviderId
                    .Should()
                    .Be(expectedProviderId, $"'{lookup}' must resolve to its advertised provider setup handler");
            }
        }
    }

    [Fact]
    public void ReportingAuthoritativeSource_NonPostgresDependencies_ShouldNotClaimDurableConfiguration()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ILedgerJournalStore>());
        services.AddSingleton(Substitute.For<IFundProfileTenancyRegistry>());
        services.AddSingleton(Substitute.For<IFundStructureService>());
        using var provider = services.BuildServiceProvider();

        var source = new ServiceProviderReportingAuthoritativeSource(provider);

        source.IsConfigured.Should().BeFalse(
            "in-memory or file compatibility services are not a durable certified-reporting authority");
    }

    [Fact]
    public void AddWorkstationSharedServices_UsesConfiguredDataRootForStrategyStores()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        var configuredDataRoot = Path.Combine(root, "persistent-data");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, $$"""{"DataRoot":"{{configuredDataRoot.Replace("\\", "\\\\")}}"}""");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<PromotionRecordStoreOptions>().RootDirectory
            .Should()
            .Be(Path.Combine(configuredDataRoot, "strategies", "promotions"));
        provider.GetRequiredService<StrategyDesignStoreOptions>().RootDirectory
            .Should()
            .Be(Path.Combine(configuredDataRoot, "strategies", "designer"));
        provider.GetRequiredService<ReportingRunStoreOptions>().RootDirectory
            .Should()
            .Be(Path.Combine(configuredDataRoot, "workstation", "reporting", "runs"));
        provider.GetService<IReportPackWorkflowRecordStore>().Should().BeNull(
            "the retired report-pack workflow must not retain a second lifecycle authority");
        provider.GetService<IReportPackDeliveryRecordStore>().Should().BeNull(
            "canonical secure distribution owns delivery jobs and immutable receipts");
        provider.GetRequiredService<IRestatementCandidateResolver>().Should()
            .BeOfType<IndeterminateRestatementCandidateResolver>(
                "the retired report-pack workflow cannot authoritatively clear soft-closed restatement exposure");
        provider.GetRequiredService<ReportTemplateGovernanceStoreOptions>().SnapshotPath
            .Should()
            .Be(Path.Combine(configuredDataRoot, "workstation", "reporting", "report-templates.json"));
        provider.GetRequiredService<IReportTemplateGovernanceStore>()
            .Should().BeOfType<FileReportTemplateGovernanceStore>();
        provider.GetRequiredService<IReportingStarterKitStore>()
            .Should().BeOfType<FileReportingStarterKitStore>();
        provider.GetRequiredService<IGovernanceReportPackRepository>()
            .Should().BeOfType<FileGovernanceReportPackRepository>();
    }

    [Fact]
    public void AddWorkstationSharedServices_DoesNotRegisterRetiredQueryTokenDeliveryService()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, "{}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        provider.GetService<ReportPackDeliveryService>().Should().BeNull(
            "legacy delivery generated deterministic query-token links and is no longer production-reachable");
    }

    [Fact]
    public void AddWorkstationSharedServices_RegistersSharedLiveOrderReadinessGate()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, "{}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILiveOrderReadinessGate>()
            .Should()
            .BeOfType<TradingOperatorLiveOrderReadinessGate>();
        provider.GetRequiredService<ITradingOperatorReadinessProvider>()
            .Should()
            .BeSameAs(provider.GetRequiredService<TradingOperatorReadinessService>());
    }

    [Fact]
    public void AddWorkstationSharedServices_ResolvesAccountingEvidenceServicesThroughInterfaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, "{}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAutomatedJournalCapitalAccountReconciliationResolver>()
            .Should()
            .BeOfType<LedgerCapitalAccountReconciliationResolver>();
        provider.GetRequiredService<IAccountingPositionSnapshotCaptureService>()
            .Should()
            .BeOfType<AccountingPositionSnapshotCaptureService>();
    }

    [Fact]
    public void AddWorkstationSharedServices_AliasesAssetOperationsInterfacesToSameInMemoryStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, "{}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        var legacyStore = provider.GetRequiredService<IAssetOperationsProjectionStore>();
        legacyStore.Should().BeOfType<InMemoryAssetOperationsProjectionStore>();
        provider.GetRequiredService<IInstrumentPositionProjectionStore>()
            .Should()
            .BeSameAs(legacyStore);
    }

    [Fact]
    public void AddWorkstationSharedServices_PreservesPreRegisteredDualAssetOperationsStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, "{}");
        var customStore = Substitute.For<IAssetOperationsProjectionStore, IInstrumentPositionProjectionStore>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));
        services.AddSingleton<IAssetOperationsProjectionStore>(customStore);

        services.AddWorkstationSharedServices();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IAssetOperationsProjectionStore>()
            .Should()
            .BeSameAs(customStore);
        provider.GetRequiredService<IInstrumentPositionProjectionStore>()
            .Should()
            .BeSameAs(customStore);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(InMemoryAssetOperationsProjectionStore));
    }

    [Fact]
    public void AddWorkstationSharedServices_WhenEnvironmentIsProduction_OmitsProcessLocalOperatorInbox()
    {
        using var environment = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "ASPNETCORE_ENVIRONMENT",
            "Production");
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            "Host=127.0.0.1;Port=1;Database=meridian;Username=test;Password=test;Timeout=30");
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, "{}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));

        services.AddWorkstationSharedServices();

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IOperatorInboxService));
    }

    [Fact]
    public void AddWorkstationSharedServices_WhenProductionReportingAuthorityIsMissing_FailsClosed()
    {
        using var environment = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "ASPNETCORE_ENVIRONMENT",
            "Production");
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            null);
        using var ledger = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            null);
        var services = CreateMinimalWorkstationServices();

        Action act = () => services.AddWorkstationSharedServices();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Production reporting authority requires*");
    }

    [Fact]
    public void AddWorkstationSharedServices_WhenProductionReportingIsConfigured_OmitsFileBackedReportingAuthorities()
    {
        using var environment = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "ASPNETCORE_ENVIRONMENT",
            "Production");
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            "Host=127.0.0.1;Port=1;Database=meridian;Username=test;Password=test;Timeout=30");
        using var ledger = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            null);
        var services = CreateMinimalWorkstationServices();

        services.AddWorkstationSharedServices();

        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IReportTemplateGovernanceStore));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IReportingStarterKitStore));
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IGovernanceReportPackRepository));
        using var provider = services.BuildServiceProvider();
        provider.GetService<IReportTemplateGovernanceStore>().Should().BeNull();
        provider.GetService<IReportingStarterKitStore>().Should().BeNull();
        provider.GetService<IGovernanceReportPackRepository>().Should().BeNull();
        provider.GetRequiredService<ReportTemplateRegistryService>()
            .List(includeSuperseded: true)
            .Should().NotBeEmpty()
            .And.OnlyContain(static template => template.IsBuiltIn);
        provider.GetRequiredService<IReportingTemplateCatalog>()
            .ListTemplates()
            .Should().NotBeEmpty(
                "production canonical reporting keeps the built-in template catalog when custom mutation is unavailable");
    }

    [Fact]
    public async Task AddWorkstationSharedServices_ConfiguredReportingRegistersCancellableStartupAndHostedFallback()
    {
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            "Host=127.0.0.1;Port=1;Database=meridian;Username=test;Password=test;Timeout=30");
        using var ledger = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_LEDGER_CONNECTION_STRING",
            null);
        using var destinations = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_RECIPIENT_DESTINATIONS_JSON",
            """
            [
              {
                "tenantId": "tenant-test",
                "companyId": "tenant-test",
                "principalId": "client-1",
                "transportId": "secure-portal",
                "destination": "client-1"
              }
            ]
            """);
        var services = CreateMinimalWorkstationServices();

        services.AddWorkstationSharedServices();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(WorkstationReportingMigrationHostedService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(ReportingScheduleHostedService));
        services.Count(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(ReportingSecureDistributionHostedService))
            .Should().Be(1, "one server-owned delivery worker owns each process readiness receipt");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IReportingMigrationStartup>()
            .Should().NotBeNull(
                "the host must be able to complete reporting migration before hosted-service construction");
        provider.GetRequiredService<IReportingArtifactStore>()
            .Should().BeOfType<PostgresReportingArtifactStore>(
                "resolving a store must not synchronously open a database connection");
        provider.GetRequiredService<IReportingRunStore>()
            .Should().BeOfType<PostgresReportingRunStore>();
        provider.GetRequiredService<IReportingReleaseConsistencyGate>()
            .Should().BeOfType<PostgresReportingReleaseConsistencyGate>(
                "final release must share the PostgreSQL accounting-period fence");
        var canonicalScheduleStore = provider.GetRequiredService<IReportingScheduleStore>();
        canonicalScheduleStore
            .Should().BeOfType<PostgresReportingScheduleStore>();
#pragma warning disable CS0618
        var legacyScheduleStore = provider.GetRequiredService<LegacyReportingScheduleStore>();
#pragma warning restore CS0618
        legacyScheduleStore.Should().NotBeSameAs(canonicalScheduleStore);
        legacyScheduleStore.Should().BeAssignableTo<IReportingScheduleStore>();
        provider.GetService<IReportPackWorkflowRecordStore>().Should().BeNull();
        provider.GetService<IReportPackDeliveryRecordStore>().Should().BeNull();
        var capability = provider.GetRequiredService<IReportingDeploymentReadinessService>()
            .Evaluate();
        capability.IsReady
            .Should()
            .BeFalse(
                "registered PostgreSQL adapters are not deployment proof until migrations complete and the authority is reachable");
        capability.Components
            .Single(static component => component.ComponentId == "reconciliation-casework")
            .IsReady.Should().BeFalse(
                "registration alone is not proof that the canonical queue passed its startup integrity check");
        provider.GetRequiredService<IReconciliationBreakQueueRepository>()
            .Should().BeAssignableTo<IReconciliationBreakQueueAuthorityProbe>();
        capability.Components
            .Single(static component => component.ComponentId == "scheduling-worker")
            .IsReady.Should().BeFalse(
                "durable schedules are not operational until the configured server-owned worker starts");
        provider.GetRequiredService<ReportingScheduleWorkerOptions>()
            .PollInterval.Should().BeGreaterThan(TimeSpan.Zero);
        capability.Components
            .Single(static component => component.ComponentId == "delivery-worker")
            .IsReady.Should().BeFalse(
                "durable delivery is not operational until the configured server-owned worker starts");
        var distributionOptions =
            provider.GetRequiredService<SecureReportingDistributionOptions>();
        distributionOptions.WorkerId.Should().NotBeNullOrWhiteSpace();
        distributionOptions.WorkerPollInterval
            .Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(250));
        distributionOptions.WorkerPollInterval
            .Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));
        var migration = ActivatorUtilities.CreateInstance<WorkstationReportingMigrationHostedService>(
            provider);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var start = () => migration.StartAsync(canceled.Token);

        await start.Should().ThrowAsync<OperationCanceledException>(
            "the host startup token must reach reporting migrations");
    }

    [Fact]
    public void ReportingDeploymentReadiness_MissingCaseworkAuthority_FailsClosed()
    {
        using var environment = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "ASPNETCORE_ENVIRONMENT",
            "Production");
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var reporting = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_REPORTING_CONNECTION_STRING",
            "Host=127.0.0.1;Port=1;Database=meridian;Username=test;Password=test;Timeout=1");
        var services = CreateMinimalWorkstationServices();
        services.AddWorkstationSharedServices();
        services.RemoveAll<IReconciliationBreakQueueRepository>();
        using var provider = services.BuildServiceProvider();

        var capability = provider.GetRequiredService<IReportingDeploymentReadinessService>()
            .Evaluate();

        capability.IsReady.Should().BeFalse();
        capability.Components
            .Single(static component => component.ComponentId == "reconciliation-casework")
            .IsReady.Should().BeFalse();
        capability.BlockingReasons.Should().Contain(reason =>
            reason.Contains("casework authority", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddWorkstationSharedServices_ConfiguredScopedAccess_DefersMigrationToCancellableHostedService()
    {
        using var unified = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            Meridian.Storage.MeridianDatabaseEnvironment.UnifiedVariable,
            null);
        using var scopedAccess = new Meridian.Tests.Application.Composition.EnvironmentVariableScope(
            "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING",
            "Host=127.0.0.1;Port=1;Database=meridian;Username=test;Password=test;Timeout=30");
        var services = CreateMinimalWorkstationServices();

        services.AddWorkstationSharedServices();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(WorkstationScopedAccessMigrationHostedService));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IScopedAccessAssignmentStore>()
            .Should().BeOfType<PostgresScopedAccessAssignmentStore>(
                "resolving a store must not synchronously open a database connection");
        var migration = ActivatorUtilities.CreateInstance<WorkstationScopedAccessMigrationHostedService>(
            provider);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var start = () => migration.StartAsync(canceled.Token);

        await start.Should().ThrowAsync<OperationCanceledException>(
            "the host startup token must reach scoped-access migrations");
    }

    [Fact]
    public void ResolvePersistentDataRoot_UsesConfiguredDataRootFromConfigFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, """{"DataRoot":"../portable-data"}""");

        UiServer.ResolvePersistentDataRoot(configPath)
            .Should()
            .Be(Path.GetFullPath(Path.Combine(root, "..", "portable-data")));
    }

    private static ServiceCollection CreateMinimalWorkstationServices()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "Meridian.Tests",
            Guid.NewGuid().ToString("N"));
        var configPath = Path.Combine(root, "appsettings.json");
        Directory.CreateDirectory(root);
        File.WriteAllText(configPath, "{}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new CoreConfigStore(configPath));
        return services;
    }
}
