using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Synthetic;
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

    public static IEnumerable<object[]> ConfiguredAliases()
    {
        var runtimeFamilies = ProviderCapabilityDescriptorCatalog.Descriptors
            .Where(descriptor => descriptor.Registrations().Any())
            .Select(descriptor => descriptor.ProviderId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (alias, family) in ProviderIdentity.Aliases.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!runtimeFamilies.Contains(family))
                continue;

            yield return [alias, family, true];
            yield return [alias, family, false];
        }
    }

    [Theory]
    [MemberData(nameof(ConfiguredAliases))]
    public async Task Every_configured_alias_controls_all_of_its_declared_capabilities(
        string alias, string family, bool enabled)
    {
        var configuredAlias = $" {alias.ToUpperInvariant()} ";
        var services = CreateApplicationServices(configuredFamily: configuredAlias, configuredFamilyEnabled: enabled);
        var registrationCount = services.Count;
        await using var provider = services.BuildServiceProvider();
        var config = provider.GetRequiredService<ConfigStore>().Load();
        config.ProviderModules!.Modules.Should().ContainKey(configuredAlias,
            "the alias must come from the host's JSON configuration, not just a registry lookup");
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var descriptor = ProviderCapabilityDescriptorCatalog.Descriptors.Single(candidate => candidate.ProviderId == family);

        foreach (var registration in descriptor.Registrations())
        {
            var instance = registry.GetCapability(configuredAlias, registration.Contract);
            if (enabled)
            {
                instance.Should().NotBeNull($"configured alias '{alias}' declares {registration.Contract.Name}");
                registration.Implementation.IsInstanceOfType(instance).Should().BeTrue();
                if (instance is IProviderMetadata metadata)
                    ProviderIdentity.NormalizeId(metadata.ProviderId).Should().Be(family);
                if (instance is IBrokerageGateway brokerage)
                    ProviderIdentity.NormalizeId(brokerage.GatewayId).Should().Be(family);
                if (registration.Contract == typeof(IMarketDataClient) && instance is IMarketDataClient streaming)
                    await streaming.DisposeAsync();
                else
                    registry.GetCapability(family, registration.Contract).Should().BeSameAs(instance);
            }
            else
            {
                instance.Should().BeNull($"disabling configured alias '{alias}' disables its whole family");
                registry.GetCapability(family, registration.Contract).Should().BeNull();
                provider.GetService(registration.Implementation).Should().BeNull(
                    "concrete DI resolution must not bypass a disabled family's configuration");
            }
        }

        if (descriptor.HasStreaming)
            registry.SupportedStreamingSources.Contains(family).Should().Be(enabled);
        services.Count.Should().Be(registrationCount, "provider resolution must not mutate the built service graph");
    }

    public static IEnumerable<object[]> ConfiguredAliasVariants()
    {
        // Rotate each family's accepted names together so every alias is read from a
        // configuration file without requiring a separate application host per alias.
        var variants = ProviderCapabilityDescriptorCatalog.Descriptors
            .Max(descriptor => AcceptedNames(descriptor.ProviderId).Length);
        for (var variant = 0; variant < variants; variant++)
        {
            yield return [false, variant];
            yield return [true, variant];
        }
    }

    [Theory]
    [MemberData(nameof(ConfiguredAliasVariants))]
    public async Task Application_path_resolves_every_declared_capability_through_configured_aliases(
        bool attributeDiscovery, int aliasVariant)
    {
        var services = CreateApplicationServices(attributeDiscovery, aliasVariant: aliasVariant);
        var registrationCount = services.Count;
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var discovered = provider.GetRequiredService<DataSourceRegistry>();

        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            var ids = AcceptedNames(descriptor.ProviderId);
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
    [InlineData("template-brokerage", false)]
    [InlineData("template-brokerage", true)]
    [InlineData("template", false)]
    [InlineData("template", true)]
    [InlineData("templates", false)]
    [InlineData("templates", true)]
    [InlineData("tradier", false)]
    [InlineData("tradier", true)]
    [InlineData("tradestation", false)]
    [InlineData("tradestation", true)]
    public async Task Configuring_nonproduction_families_does_not_create_production_providers(
        string family, bool attributeDiscovery)
    {
        var services = CreateApplicationServices(attributeDiscovery, extraFamily: $" {family.ToUpperInvariant()} ");
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        var contracts = ProviderCapabilityDescriptorCatalog.Descriptors
            .SelectMany(d => d.Registrations()).Select(r => r.Contract).Distinct().ToArray();
        foreach (var contract in contracts)
        {
            registry.GetCapability(family, contract).Should().BeNull();
            provider.GetServices(contract).Should().NotContain(instance =>
                instance is IProviderMetadata && ProviderIdentity.EqualsId(((IProviderMetadata)instance).ProviderId, family)
                || instance is IBrokerageGateway && ProviderIdentity.EqualsId(((IBrokerageGateway)instance).GatewayId, family));
        }

        var excludedNamespaces = new[]
        {
            "Meridian.Infrastructure.Adapters.Templates",
            "Meridian.Infrastructure.Adapters.Tradier",
            "Meridian.Infrastructure.Adapters.TradeStation"
        };
        var excludedTypes = typeof(ProviderFactory).Assembly.GetTypes()
            .Where(type => type.Namespace is not null && excludedNamespaces.Contains(type.Namespace)
                && contracts.Any(contract => contract.IsAssignableFrom(type)));
        foreach (var type in excludedTypes)
            provider.GetService(type).Should().BeNull($"{type.Name} belongs to a template-only or mapper-only family");

        var createStreaming = () => registry.CreateStreamingClient(family);
        createStreaming.Should().Throw<InvalidOperationException>();
        provider.GetService<TemplateBrokerageGateway>().Should().BeNull();
        provider.GetServices<IBrokerageGateway>().Should().NotContain(g => g is TemplateBrokerageGateway);
        registry.GetAllProviderMetadata().Should().NotContain(metadata => ProviderIdentity.EqualsId(metadata.ProviderId, family));
        ProviderCatalog.Get(family).Should().BeNull("configuration cannot promote scaffolds or mapper assets into runtime inventory");
        provider.GetRequiredService<DataSourceRegistry>().Sources.Should().NotContain(
            source => source.ImplementationType == typeof(TemplateBrokerageGateway) || ProviderIdentity.EqualsId(source.Id, family));
    }

    [Theory]
    [MemberData(nameof(ConfiguredAliasVariants))]
    public async Task Configured_alias_disable_applies_to_every_declared_capability(
        bool attributeDiscovery, int aliasVariant)
    {
        var services = CreateApplicationServices(attributeDiscovery, aliasVariant: aliasVariant, disableAllFamilies: true);
        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();

        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            foreach (var capability in descriptor.Registrations())
            {
                foreach (var name in AcceptedNames(descriptor.ProviderId))
                    registry.GetCapability(name, capability.Contract).Should().BeNull(
                        $"disabling configured alias {ConfiguredName(descriptor.ProviderId, aliasVariant)} disables {name}");
            }
        }

        registry.SupportedStreamingSources.Should().BeEmpty();
        registry.GetAllProviderMetadata().Should().BeEmpty();
        provider.GetServices<IBrokerageGateway>().Should().BeEmpty();
        provider.GetServices<ICorporateActionProvider>().Should().BeEmpty();
        provider.GetServices<IOptionsChainProvider>().Should().BeEmpty();
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
        services.AddSingleton<Meridian.Core.IO.IAtomicFileWriter, Meridian.Storage.Archival.AtomicFileWriterAdapter>();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Streaming_factories_are_distinct_from_search_and_runtime_alias_disable_covers_both(bool resolveConcreteFirst)
    {
        await using var provider = CreateApplicationServices().BuildServiceProvider();
        var concrete = resolveConcreteFirst ? provider.GetRequiredService<SyntheticMarketDataClient>() : null;
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var search = registry.GetCapability<ISymbolSearchProvider>("synthetic");
        concrete ??= provider.GetRequiredService<SyntheticMarketDataClient>();
        concrete.Should().BeSameAs(search,
            "the shared implementation's concrete service must reuse the registry-owned search singleton");
        provider.GetRequiredService<SyntheticMarketDataClient>().Should().BeSameAs(concrete);
        await using var streaming = registry.GetCapability<IMarketDataClient>("synthetic");
        await using var anotherStreaming = registry.CreateStreamingClient("synthetic");
        streaming.Should().NotBeSameAs(search);
        anotherStreaming.Should().NotBeSameAs(streaming).And.NotBeSameAs(search);
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
        bool attributeDiscovery = false, string? extraFamily = null,
        int? aliasVariant = null, bool disableAllFamilies = false,
        string? configuredFamily = null, bool configuredFamilyEnabled = true)
    {
        Environment.SetEnvironmentVariable(TokenVariable, "composition-fixture-token");
        var modules = new Dictionary<string, ProviderModuleSettings>
        {
            ["interactive-brokers"] = new(),
            ["nasdaq-data-link"] = new(),
            ["robinhood-live"] = new(Credentials: new() { ["accessToken"] = TokenVariable })
        };
        if (aliasVariant is { } variant)
        {
            modules = ProviderCapabilityDescriptorCatalog.Descriptors.ToDictionary(
                descriptor => $" {ConfiguredName(descriptor.ProviderId, variant).ToUpperInvariant()} ",
                descriptor => new ProviderModuleSettings(
                    Enabled: !disableAllFamilies,
                    Credentials: descriptor.ProviderId == "robinhood"
                        ? new() { ["accessToken"] = TokenVariable }
                        : null));
        }
        if (extraFamily is not null)
            modules[extraFamily] = new();
        if (configuredFamily is not null)
        {
            var existing = modules.Keys.SingleOrDefault(key => ProviderIdentity.EqualsId(key, configuredFamily));
            var settings = existing is null ? new ProviderModuleSettings() : modules[existing];
            if (existing is not null)
                modules.Remove(existing);
            modules[configuredFamily] = settings with { Enabled = configuredFamilyEnabled };
        }

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

    private static string[] AcceptedNames(string providerId) => ProviderIdentity.Aliases
        .Where(pair => pair.Value == providerId)
        .Select(pair => pair.Key)
        .Append(providerId)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    private static string ConfiguredName(string providerId, int variant)
    {
        var names = AcceptedNames(providerId);
        return names[variant % names.Length];
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
