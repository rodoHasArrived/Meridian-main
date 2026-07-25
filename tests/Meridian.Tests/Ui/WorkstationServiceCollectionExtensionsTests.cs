using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Identity;
using Meridian.Reporting;
using Meridian.Strategies.Storage;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Ui;

[Collection("Sequential")]
public sealed class WorkstationServiceCollectionExtensionsTests
{
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
        provider.GetRequiredService<ReportPackWorkflowRecordStoreOptions>().SnapshotPath
            .Should()
            .Be(Path.Combine(configuredDataRoot, "workstation", "reporting", "report-pack-workflows.json"));
        provider.GetRequiredService<ReportTemplateGovernanceStoreOptions>().SnapshotPath
            .Should()
            .Be(Path.Combine(configuredDataRoot, "workstation", "reporting", "report-templates.json"));
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
    public async Task AddWorkstationSharedServices_ConfiguredReporting_DefersMigrationToCancellableHostedService()
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
        var services = CreateMinimalWorkstationServices();

        services.AddWorkstationSharedServices();

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) &&
            descriptor.ImplementationType == typeof(WorkstationReportingMigrationHostedService));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IReportingArtifactStore>()
            .Should().BeOfType<PostgresReportingArtifactStore>(
                "resolving a store must not synchronously open a database connection");
        var migration = ActivatorUtilities.CreateInstance<WorkstationReportingMigrationHostedService>(
            provider);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        var start = () => migration.StartAsync(canceled.Token);

        await start.Should().ThrowAsync<OperationCanceledException>(
            "the host startup token must reach reporting migrations");
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
