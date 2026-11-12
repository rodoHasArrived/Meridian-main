using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Composition;
using Meridian.Application.Config.Credentials;
using Meridian.Application.Pipeline;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Coordination;
using Meridian.Contracts.Etl;
using Meridian.Core.Config;
using Meridian.DataIntegration.Canonicalization;
using Meridian.DataIntegration.Historical;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Ledger;
using Meridian.Storage;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Services;
using Meridian.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Xunit;

namespace Meridian.Tests.Application.Composition;

public sealed class ServiceCompositionRootTests
{
    // These are observable capabilities, rather than the private feature-plan implementation.
    private static readonly (string Feature, Type Service)[] OptionalCapabilities =
    [
        ("credentials", typeof(CredentialTestingService)),
        ("providers", typeof(IProviderCredentialResolver)),
        ("providers", typeof(ProviderRoutingService)),
        ("symbols", typeof(CanonicalSymbolRegistry)),
        ("backfill", typeof(BackfillCoordinator)),
        ("etl", typeof(IEtlJobDefinitionStore)),
        ("maintenance", typeof(ArchiveMaintenanceScheduleManager)),
        ("diagnostics", typeof(HistoricalDataQueryService)),
        ("pipeline", typeof(DualPathEventPipeline)),
        ("collectors", typeof(QuoteCollector)),
        ("canonicalization", typeof(CanonicalizingPublisher)),
        ("http", typeof(IAsyncPolicy<HttpResponseMessage>))
    ];

    [Theory]
    [InlineData("default")]
    [InlineData("minimal")]
    [InlineData("web")]
    [InlineData("streaming")]
    [InlineData("backfill")]
    public void Presets_RegisterExpectedCapabilitiesAndCoreServices(string preset)
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ServiceCompositionRootTests));
        var options = preset switch
        {
            "minimal" => CompositionOptions.Minimal,
            "web" => CompositionOptions.WebDashboard,
            "streaming" => CompositionOptions.Streaming,
            "backfill" => CompositionOptions.BackfillOnly,
            _ => CompositionOptions.Default
        };
        var expected = preset switch
        {
            "minimal" => Array.Empty<string>(),
            "streaming" => OptionalCapabilities.Select(capability => capability.Feature)
                .Where(feature => feature != "maintenance").ToArray(),
            "backfill" => new[] { "credentials", "providers", "symbols", "backfill", "etl", "pipeline", "http" },
            _ => OptionalCapabilities.Select(capability => capability.Feature).ToArray()
        };

        var services = Compose(options, artifacts.RootPath);

        AssertCapabilities(services, expected);
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ConfigStore));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ICoordinationStore));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(StorageOptions));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ProjectLedgerBook));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ProductionRegistrationGuardService));
        services.First().ServiceType.Should().Be(typeof(IHostedService),
            "the final-graph production guard must remain ahead of all feature workers");
    }

    [Theory]
    [InlineData("credentials")]
    [InlineData("providers")]
    [InlineData("symbols")]
    [InlineData("backfill")]
    [InlineData("etl")]
    [InlineData("maintenance")]
    [InlineData("diagnostics")]
    [InlineData("pipeline")]
    [InlineData("collectors")]
    [InlineData("canonicalization")]
    [InlineData("http")]
    public void CustomOptions_EnableOnlyRequestedCapabilityWithoutInferringDependencies(string feature)
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ServiceCompositionRootTests));
        var minimal = CompositionOptions.Minimal;
        var options = feature switch
        {
            "credentials" => minimal with { EnableCredentialServices = true },
            "providers" => minimal with { EnableProviderServices = true },
            "symbols" => minimal with { EnableSymbolManagement = true },
            "backfill" => minimal with { EnableBackfillServices = true },
            "etl" => minimal with { EnableEtlServices = true },
            "maintenance" => minimal with { EnableMaintenanceServices = true },
            "diagnostics" => minimal with { EnableDiagnosticServices = true },
            "pipeline" => minimal with { EnablePipelineServices = true },
            "collectors" => minimal with { EnableCollectorServices = true },
            "canonicalization" => minimal with { EnableCanonicalizationServices = true },
            "http" => minimal with { EnableHttpClientFactory = true },
            _ => throw new ArgumentOutOfRangeException(nameof(feature))
        };

        var services = Compose(options, artifacts.RootPath);

        AssertCapabilities(services, [feature]);
    }

    [Fact]
    public void Default_RegistersPrerequisitesBeforeConsumersAndCanonicalizationAfterPipeline()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(ServiceCompositionRootTests));
        var services = Compose(CompositionOptions.Default, artifacts.RootPath).ToList();
        Type[] dependencyOrder =
        [
            typeof(ConfigStore), typeof(ICoordinationStore), typeof(StorageOptions),
            typeof(ProjectLedgerBook), typeof(CredentialTestingService),
            typeof(IProviderCredentialResolver), typeof(ProviderRoutingService),
            typeof(CanonicalSymbolRegistry), typeof(BackfillCoordinator),
            typeof(DualPathEventPipeline), typeof(CanonicalizingPublisher)
        ];

        var positions = dependencyOrder.Select(serviceType =>
            services.FindIndex(descriptor => descriptor.ServiceType == serviceType)).ToArray();

        positions.Should().OnlyContain(position => position >= 0);
        positions.Should().BeInAscendingOrder();
        positions.Should().OnlyHaveUniqueItems();
    }

    private static IServiceCollection Compose(CompositionOptions options, string root)
    {
        var configPath = Path.Combine(root, "appsettings.json");
        var config = new AppConfig(
            DataRoot: root,
            Compress: false,
            Storage: new StorageConfig(),
            Backfill: new BackfillConfig(Enabled: false),
            Coordination: new CoordinationConfig(Enabled: false));
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, AppConfigJsonOptions.Write));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DOTNET_ENVIRONMENT"] = "Test",
                ["MERIDIAN_USE_INMEMORY_GOVERNANCE"] = "true"
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMarketDataServices(options with
        {
            ConfigPath = configPath,
            DataRoot = root,
            Configuration = configuration
        });
        return services;
    }

    private static void AssertCapabilities(IServiceCollection services, string[] expected)
    {
        foreach (var (feature, serviceType) in OptionalCapabilities)
        {
            services.Any(descriptor => descriptor.ServiceType == serviceType).Should()
                .Be(expected.Contains(feature), $"the {feature} capability must follow its activation option");
        }
    }
}
