using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Templates;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using Meridian.Tests.TestHelpers;
using Meridian.Tests.Ui;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Meridian.Tests.Application.Composition;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class ProviderCompositionTests : IDisposable
{
    private const string TokenVariable = "MERIDIAN_COMPOSITION_TEST_TOKEN";
    private readonly string? _originalToken = Environment.GetEnvironmentVariable(TokenVariable);
    private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"provider-composition-{Guid.NewGuid():N}.json");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Application_path_resolves_every_declared_capability_through_configured_aliases(bool attributeDiscovery)
    {
        var services = CreateApplicationServices(attributeDiscovery);
        var registrationCount = services.Count;
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var discovered = provider.GetRequiredService<DataSourceRegistry>();

        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            var ids = ProviderIdentity.Aliases.Where(pair => pair.Value == descriptor.ProviderId)
                .Select(pair => pair.Key).Append(descriptor.ProviderId).ToArray();
            foreach (var registration in descriptor.Registrations())
            {
                foreach (var id in ids)
                {
                    var instance = registry.GetCapability($" {id.ToUpperInvariant()} ", registration.Contract);
                    instance.Should().NotBeNull($"{id} declares {registration.Contract.Name} in the application catalog");
                    registration.Implementation.IsInstanceOfType(instance).Should().BeTrue(
                        $"{id} must resolve {registration.Implementation.Name} through {registration.Contract.Name}");

                    if (instance is IProviderMetadata metadata)
                        ProviderIdentity.NormalizeId(metadata.ProviderId).Should().Be(descriptor.ProviderId);
                    if (instance is IBrokerageGateway brokerage)
                        ProviderIdentity.NormalizeId(brokerage.GatewayId).Should().Be(descriptor.ProviderId);
                    if (registration.Contract == typeof(IMarketDataClient) && instance is IMarketDataClient streaming)
                        await streaming.DisposeAsync();
                }
            }
            if (descriptor.CompatibilityDataSource is { } compatibility)
                provider.GetServices<IDataSource>().Should().Contain(source => compatibility.IsInstanceOfType(source));
        }

        registry.SupportedStreamingSources.Should().BeEquivalentTo(
            ProviderCapabilityDescriptorCatalog.Descriptors.Where(d => d.HasStreaming).Select(d => d.ProviderId));
        registry.GetProviderCatalog().Should().OnlyContain(entry => entry.ProviderId == ProviderIdentity.NormalizeId(entry.ProviderId));
        discovered.Sources.Should().OnlyContain(source => source.Id == ProviderIdentity.NormalizeId(source.Id));
        services.Count.Should().Be(registrationCount, "resolving services must never register additional descriptors after BuildServiceProvider");
    }

    [Theory]
    [InlineData("template-brokerage")]
    [InlineData("templates")]
    [InlineData("tradier")]
    [InlineData("tradestation")]
    public async Task Configuring_nonproduction_families_does_not_create_production_providers(string family)
    {
        var services = CreateApplicationServices(attributeDiscovery: true, extraFamily: family);
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        foreach (var contract in ProviderCapabilityDescriptorCatalog.Descriptors
                     .SelectMany(d => d.Registrations()).Select(r => r.Contract).Distinct())
            registry.GetCapability(family, contract).Should().BeNull();

        var createStreaming = () => registry.CreateStreamingClient(family);
        createStreaming.Should().Throw<InvalidOperationException>();
        provider.GetService<TemplateBrokerageGateway>().Should().BeNull();
        provider.GetServices<IBrokerageGateway>().Should().NotContain(g => g is TemplateBrokerageGateway);
        provider.GetRequiredService<DataSourceRegistry>().Sources.Should().NotContain(
            source => source.ImplementationType == typeof(TemplateBrokerageGateway) || ProviderIdentity.EqualsId(source.Id, family));
    }

    [Fact]
    public async Task Alias_disable_applies_to_all_capabilities_of_the_family()
    {
        var services = CreateApplicationServices(disabledFamily: "interactive-brokers");
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var ib = ProviderCapabilityDescriptorCatalog.Descriptors.Single(d => d.ProviderId == "ibkr");

        foreach (var capability in ib.Registrations())
            registry.GetCapability(" IB ", capability.Contract).Should().BeNull();
        registry.SupportedStreamingSources.Should().NotContain("ibkr");
        provider.GetServices<IBrokerageGateway>().Should().NotContain(g => g.GatewayId == "ibkr");
    }

    [Fact]
    public async Task Public_registration_path_uses_alias_sidecars_before_container_build()
    {
        var config = new AppConfig(ProviderModules: new ProviderModulesConfig(new()
        {
            ["alpaca-brokerage"] = new()
        }));
        var services = new ServiceCollection();
        services.AddSingleton<IMarketEventPublisher, TestMarketEventPublisher>();
        new CollectorFeatureRegistration().Register(services, CompositionOptions.WebDashboard);
        services.AddProviderServices(config, new FixedCredentialResolver(), sidecars: new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["alpaca"] = new Dictionary<string, string> { ["keyId"] = "composition-sidecar-key", ["secretKey"] = "composition-sidecar-secret" }
        });
        var handler = new CaptureCredentialsHandler();
        services.AddHttpClient("alpaca-corp-actions").ConfigurePrimaryHttpMessageHandler(() => handler);
        var count = services.Count;
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        registry.GetCapability<IBrokerageGateway>("alpaca-brokerage").Should().NotBeNull();
        var corporateActions = registry.GetCapability<ICorporateActionProvider>("alpaca-corp-actions");
        await corporateActions!.FetchAsync("AAPL", Guid.NewGuid());
        handler.Credentials.Should().HaveCount(2).And.OnlyContain(pair =>
            pair.Key == "composition-sidecar-key" && pair.Secret == "composition-sidecar-secret");
        registry.GetCapability<IOptionsChainProvider>("alpaca-options").Should().NotBeNull();
        await using var streaming = registry.CreateStreamingClient("alpaca");
        services.Count.Should().Be(count);
    }

    [Fact]
    public async Task Options_credentials_are_resolved_from_the_final_host_resolver()
    {
        var services = CreateApplicationServices();
        // Remove the module env mapping so only the resolver added after registration can
        // supply the Robinhood credential. The service shape must not depend on process env.
        var config = JsonNode.Parse(File.ReadAllText(_configPath))!;
        config["providerModules"]!["modules"]!["robinhood-live"]!["credentials"] = new JsonObject();
        File.WriteAllText(_configPath, config.ToJsonString());
        services = CreateApplicationServicesWithExistingConfig();
        await using var provider = services.BuildServiceProvider();
        provider.GetServices<IOptionsChainProvider>().Should().Contain(p => p.ProviderId == "robinhood");
        var registry = provider.GetRequiredService<ProviderRegistry>();
        registry.GetCapability<IOptionsChainProvider>("robinhood-options").Should().NotBeNull();
        await using var streaming = registry.CreateStreamingClient("robinhood-live");
        streaming.IsEnabled.Should().BeTrue("the same configured credentials reach every Robinhood capability");
    }

    [Fact]
    public async Task Empty_options_set_does_not_break_catalog_or_other_capabilities()
    {
        CreateApplicationServices();
        var config = JsonNode.Parse(File.ReadAllText(_configPath))!;
        foreach (var family in ProviderCapabilityDescriptorCatalog.Descriptors.Where(d => d.HasOptions))
            config["providerModules"]!["modules"]![family.ProviderId == "robinhood" ? "robinhood-live" : family.ProviderId] =
                new JsonObject { ["enabled"] = false };
        File.WriteAllText(_configPath, config.ToJsonString());
        await using var provider = CreateApplicationServicesWithExistingConfig().BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        provider.GetServices<IOptionsChainProvider>().Should().BeEmpty();
        ProviderCatalog.GetAll().Should().NotBeEmpty();
        registry.GetCapability<IHistoricalDataProvider>("interactive-brokers").Should().NotBeNull();
    }

    [Fact]
    public async Task Streaming_factories_are_distinct_from_search_and_runtime_alias_disable_covers_both()
    {
        await using var provider = CreateApplicationServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var search = registry.GetCapability<ISymbolSearchProvider>("synthetic");
        await using var streaming = registry.GetCapability<IMarketDataClient>("synthetic");
        streaming.Should().NotBeSameAs(search);
        var first = await provider.InitializeProvidersAsync();
        var second = await provider.InitializeProvidersAsync();
        first.TotalProviders.Should().Be(second.TotalProviders);
        registry.GetCapability<ISymbolSearchProvider>("synthetic").Should().BeSameAs(search);

        registry.Disable("interactive-brokers");
        registry.GetCapability<IBrokerageGateway>("IB").Should().BeNull();
        var create = () => registry.CreateStreamingClient("ibkr");
        create.Should().Throw<InvalidOperationException>();
        registry.Enable("ib");
        registry.GetCapability<IBrokerageGateway>("interactive-brokers").Should().NotBeNull();
    }

    private ServiceCollection CreateApplicationServices(
        bool attributeDiscovery = false, string? extraFamily = null, string? disabledFamily = null)
    {
        Environment.SetEnvironmentVariable(TokenVariable, "composition-fixture-token");
        var modules = new Dictionary<string, ProviderModuleSettings>
        {
            ["interactive-brokers"] = new(),
            ["nasdaq-data-link"] = new(),
            ["robinhood-live"] = new(Credentials: new() { ["accessToken"] = TokenVariable })
        };
        if (extraFamily is not null)
            modules[extraFamily] = new();
        if (disabledFamily is not null)
            modules[disabledFamily] = new(Enabled: false);

        var config = new AppConfig(
            IB: new IBOptions(),
            Alpaca: new AlpacaOptions(KeyId: "composition-fixture-key", SecretKey: "composition-fixture-secret"),
            Polygon: new PolygonOptions(ApiKey: "composition-fixture-key"),
            Synthetic: new SyntheticMarketDataConfig(Enabled: true),
            ProviderModules: new ProviderModulesConfig(modules),
            Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(
                Synthetic: new SyntheticMarketDataConfig(Enabled: true),
                AlphaVantage: new AlphaVantageConfig(Enabled: true),
                Fred: new FredConfig(Enabled: true),
                Robinhood: new RobinhoodConfig(Enabled: true))));
        var json = JsonSerializer.SerializeToNode(config, AppConfigJsonOptions.Write)!.AsObject();
        json["dataSource"] = " interactive-brokers ";
        json["providerRegistry"] = new JsonObject { ["useAttributeDiscovery"] = attributeDiscovery };
        File.WriteAllText(_configPath, json.ToJsonString());
        return CreateApplicationServicesWithExistingConfig();
    }

    private ServiceCollection CreateApplicationServicesWithExistingConfig()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMarketEventPublisher, TestMarketEventPublisher>();
        var options = CompositionOptions.WebDashboard with { ConfigPath = _configPath };
        new ConfigurationFeatureRegistration().Register(services, options);
        new CollectorFeatureRegistration().Register(services, options);
        new ProviderFeatureRegistration().Register(services, options);
        // Substitute only the external credential source. All provider factories and adapters
        // must come from the same feature used by the application host.
        services.AddSingleton<IProviderCredentialResolver, FixedCredentialResolver>();
        return services;
    }

    private sealed class CaptureCredentialsHandler : HttpMessageHandler
    {
        public System.Collections.Concurrent.ConcurrentBag<(string Key, string Secret)> Credentials { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Credentials.Add((request.Headers.GetValues("APCA-API-KEY-ID").Single(),
                request.Headers.GetValues("APCA-API-SECRET-KEY").Single()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }

    private sealed class FixedCredentialResolver : IProviderCredentialResolver, ICredentialContext
    {
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null) => this;
        public string Get(string name) => "composition-fixture-credential";
        public bool IsConfigured(string name) => true;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TokenVariable, _originalToken);
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;
        File.Delete(_configPath);
    }
}
