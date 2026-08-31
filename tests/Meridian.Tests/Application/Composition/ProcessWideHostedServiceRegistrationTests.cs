using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Core.Config;
using Meridian.Storage;
using Meridian.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Application.Composition;

/// <summary>
/// Guards child and one-shot host graphs from duplicating process-wide coordination and database
/// workers while preserving the graph-local storage and symbol initialization they still own.
/// </summary>
[Collection("Sequential")]
public sealed class ProcessWideHostedServiceRegistrationTests
{
    private static readonly string[] ProcessWideHostedServiceNames =
    [
        "ScopedAccessAssignmentStoreMigrationHostedService",
        "PolygonCorporateActionFetcher",
        "DirectLendingOutboxDispatcher",
        "DailyAccrualWorker",
        "ProjectionReconciliationHostedService",
        "ClusterCoordinatorService",
        "SplitBrainDetector"
    ];

    private static readonly string[] LocalInitializerHostedServiceNames =
    [
        "StorageCatalogInitializationHostedService",
        "SymbolRegistryInitializationHostedService",
        "CanonicalSymbolRegistryMigrationService"
    ];

    [Fact]
    public async Task AddMarketDataServices_ProcessWideWorkersDisabled_OmitsOnlyProcessOwnedHostedServices()
    {
        using var environment = CompositionRegistrationTestEnvironment.Enable();
        using var artifacts = TestArtifactDirectory.Create(nameof(ProcessWideHostedServiceRegistrationTests));
        var configPath = WriteConfig(artifacts.RootPath);

        var processOwnerGraph = await ResolveHostedServiceNamesAsync(
            configPath,
            enableProcessWideHostedServices: true);
        var childGraph = await ResolveHostedServiceNamesAsync(
            configPath,
            enableProcessWideHostedServices: false);

        processOwnerGraph
            .Except(childGraph, StringComparer.Ordinal)
            .Should()
            .BeEquivalentTo(
                ProcessWideHostedServiceNames,
                "the ownership flag must gate every process-wide database and coordination worker");
        childGraph
            .Except(processOwnerGraph, StringComparer.Ordinal)
            .Should()
            .BeEmpty("disabling process-wide workers must not add a divergent child-only graph");

        foreach (var processWideServiceName in ProcessWideHostedServiceNames)
        {
            processOwnerGraph.Should().Contain(processWideServiceName);
            childGraph.Should().NotContain(processWideServiceName);
        }

        foreach (var localInitializerServiceName in LocalInitializerHostedServiceNames)
        {
            processOwnerGraph.Should().Contain(localInitializerServiceName);
            childGraph.Should().Contain(
                localInitializerServiceName,
                "child graphs retain their local catalog and symbol initialization");
        }
    }

    private static async Task<HashSet<string>> ResolveHostedServiceNamesAsync(
        string configPath,
        bool enableProcessWideHostedServices)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketDataServices(
            CompositionOptions.Default with
            {
                ConfigPath = configPath,
                EnableProcessWideHostedServices = enableProcessWideHostedServices
            });

        await using var provider = services.BuildServiceProvider();

        // Resolve registrations to expose factory-backed hosted-service types (Polygon and the
        // coordinator), but deliberately never start an IHost or call any worker's StartAsync.
        return provider
            .GetServices<IHostedService>()
            .Select(service => service.GetType().Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string WriteConfig(string dataRoot)
    {
        var configPath = Path.Combine(dataRoot, "appsettings.json");
        var config = new AppConfig(
            DataRoot: dataRoot,
            Compress: false,
            Storage: new StorageConfig(),
            Backfill: new BackfillConfig(Enabled: false),
            Coordination: new CoordinationConfig(Enabled: false));
        File.WriteAllText(
            configPath,
            JsonSerializer.Serialize(config, AppConfigJsonOptions.Write));
        return configPath;
    }

    private sealed class CompositionRegistrationTestEnvironment : IDisposable
    {
        private const string ScopedAccessConnectionStringVariable =
            "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING";
        private const string ScopedAccessSchemaVariable = "MERIDIAN_SCOPED_ACCESS_SCHEMA";
        private const string DummyPostgresConnectionString =
            "Host=registration-test.invalid;Port=5432;Database=meridian;Username=test;Password=test";

        private static readonly string[] VariableNames =
        [
            "DOTNET_ENVIRONMENT",
            "ASPNETCORE_ENVIRONMENT",
            "MERIDIAN_ENVIRONMENT",
            "MERIDIAN_DEPLOYMENT_ENVIRONMENT",
            "MERIDIAN_MODE",
            "MERIDIAN_API_DEPLOYMENT_MODE",
            "MERIDIAN_USE_INMEMORY_GOVERNANCE",
            MeridianDatabaseEnvironment.UnifiedVariable,
            .. MeridianDatabaseEnvironment.PropagatedConnectionStringVariables,
            ScopedAccessConnectionStringVariable,
            DirectLendingStartup.ConnectionStringVariable,
            SecurityMasterStartup.SchemaVariable,
            AssetOperationsStartup.SchemaVariable,
            DirectLendingStartup.SchemaVariable,
            LedgerStartup.SchemaVariable,
            ScopedAccessSchemaVariable
        ];

        private readonly IReadOnlyDictionary<string, string?> _originalValues;

        private CompositionRegistrationTestEnvironment()
        {
            _originalValues = VariableNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    variableName => variableName,
                    Environment.GetEnvironmentVariable,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var variableName in _originalValues.Keys)
            {
                Environment.SetEnvironmentVariable(variableName, null);
            }

            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", Environments.Development);
            Environment.SetEnvironmentVariable("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
            Environment.SetEnvironmentVariable(
                SecurityMasterStartup.ConnectionStringVariable,
                DummyPostgresConnectionString);
            Environment.SetEnvironmentVariable(
                LedgerStartup.ConnectionStringVariable,
                DummyPostgresConnectionString);
            Environment.SetEnvironmentVariable(
                ScopedAccessConnectionStringVariable,
                DummyPostgresConnectionString);
            Environment.SetEnvironmentVariable(ScopedAccessSchemaVariable, "identity_access_test");
        }

        public static CompositionRegistrationTestEnvironment Enable() => new();

        public void Dispose()
        {
            foreach (var (variableName, value) in _originalValues)
            {
                Environment.SetEnvironmentVariable(variableName, value);
            }
        }
    }
}
