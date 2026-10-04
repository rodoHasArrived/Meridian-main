using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.Domain.Events;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Tests.TestHelpers;
using Meridian.Tests.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Application.Composition;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class ProviderCatalogCompositionTests : IDisposable
{
    [Fact]
    public async Task Public_registration_catalog_reports_all_six_factories_from_the_same_descriptors()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        _ = provider.GetRequiredService<ProviderRegistry>();

        var catalog = ProviderCatalog.GetAll();
        catalog.Select(entry => entry.ProviderId).Should().OnlyHaveUniqueItems();
        catalog.Should().OnlyContain(entry => entry.ProviderId == ProviderIdentity.NormalizeId(entry.ProviderId));

        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            var entry = catalog.Should().ContainSingle(candidate => candidate.ProviderId == descriptor.ProviderId).Which;
            var capabilities = entry.Capabilities;
            capabilities.SupportsStreaming.Should().Be(descriptor.HasStreaming, descriptor.ProviderId);
            capabilities.SupportsBackfill.Should().Be(descriptor.HasHistorical, descriptor.ProviderId);
            capabilities.SupportsSymbolSearch.Should().Be(descriptor.HasSearch, descriptor.ProviderId);
            capabilities.SupportsCorporateActions.Should().Be(descriptor.HasCorporateActions, descriptor.ProviderId);
            capabilities.SupportsOptionsChain.Should().Be(descriptor.HasOptions, descriptor.ProviderId);
            capabilities.SupportsBrokerage.Should().Be(descriptor.HasBrokerage, descriptor.ProviderId);

            var dictionary = capabilities.ToDictionary();
            dictionary.ContainsKey(nameof(CapabilityInfo.SupportsBackfill)).Should().Be(descriptor.HasHistorical);
            dictionary.ContainsKey(nameof(CapabilityInfo.SupportsSymbolSearch)).Should().Be(descriptor.HasSearch);
            dictionary.ContainsKey(nameof(CapabilityInfo.SupportsCorporateActions)).Should().Be(descriptor.HasCorporateActions);

            var expectedType = (descriptor.HasStreaming, descriptor.HasHistorical) switch
            {
                (true, true) => ProviderTypeKind.Hybrid,
                (true, false) => ProviderTypeKind.Streaming,
                (false, true) => ProviderTypeKind.Backfill,
                _ => ProviderTypeKind.SymbolSearch
            };
            entry.ProviderType.Should().Be(expectedType);
        }

        catalog.Should().NotContain(entry => entry.ProviderId == "templates"
            || entry.ProviderId == "tradier" || entry.ProviderId == "tradestation");
    }

    [Fact]
    public async Task Catalog_aliases_share_canonical_family_entries()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        _ = provider.GetRequiredService<ProviderRegistry>();

        foreach (var alias in ProviderIdentity.Aliases.Where(pair =>
                     ProviderCapabilityDescriptorCatalog.Descriptors.Any(descriptor => descriptor.ProviderId == pair.Value)))
        {
            var entry = ProviderCatalog.Get($" {alias.Key.ToUpperInvariant()} ");
            entry.Should().NotBeNull(alias.Key);
            entry!.ProviderId.Should().Be(alias.Value);
        }
    }

    [Fact]
    public async Task Merging_inventory_preserves_product_entitlement_feed_and_quality_metadata()
    {
        await using var provider = CreateServices().BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var historical = registry.GetCapability<IHistoricalDataProvider>("interactive-brokers");
        historical.Should().NotBeNull();
        var historicalEntry = ProviderTemplateFactory.ToCatalogEntry(historical!);
        historicalEntry.Capabilities.SupportsBackfill.Should().BeTrue();
        historicalEntry.Capabilities.MarketDataCapabilities.Should().NotBeEmpty();

        var merged = ProviderCatalog.Get(" IB ");
        merged.Should().NotBeNull();
        merged!.Capabilities.SupportsStreaming.Should().BeTrue("the family declares a streaming factory");
        merged.Capabilities.SupportsBrokerage.Should().BeTrue("the family declares a brokerage factory");
        merged.Capabilities.MarketDataCapabilities.Should().BeEquivalentTo(
            historicalEntry.Capabilities.MarketDataCapabilities,
            "factory inventory must preserve feed, pacing, entitlement, timestamp, and quality declarations");
        merged.Warnings.Should().Contain(historicalEntry.Warnings);
    }

    [Fact]
    public async Task Disabled_family_retains_factory_inventory_without_claiming_runtime_products()
    {
        await using var provider = CreateServices(disabledFamily: "twelve-data").BuildServiceProvider();
        var registry = provider.GetRequiredService<ProviderRegistry>();
        var descriptor = ProviderCapabilityDescriptorCatalog.Descriptors.Single(d => d.ProviderId == "twelvedata");
        foreach (var capability in descriptor.Registrations())
            registry.GetCapability("twelve-data", capability.Contract).Should().BeNull();

        var entry = ProviderCatalog.Get("twelve-data");
        entry.Should().NotBeNull();
        entry!.ProviderId.Should().Be("twelvedata");
        entry.Capabilities.SupportsBackfill.Should().BeTrue();
        entry.Capabilities.SupportsSymbolSearch.Should().BeTrue();
        entry.Capabilities.SupportsCorporateActions.Should().BeTrue();
        entry.Capabilities.MarketDataCapabilities.Should().BeEmpty(
            "an unconstructed descriptor is inventory, not runtime availability or entitlement evidence");
        entry.RequiresCredentials.Should().BeTrue();
        entry.CredentialFields.Should().Contain(field => field.Name == "TWELVEDATA_API_KEY" && field.Required);
    }

    private static ServiceCollection CreateServices(string? disabledFamily = null)
    {
        var modules = new Dictionary<string, ProviderModuleSettings>
        {
            ["interactive-brokers"] = new(),
            ["nasdaq-data-link"] = new()
        };
        if (disabledFamily is not null)
            modules[disabledFamily] = new(Enabled: false);

        var config = new AppConfig(
            IB: new IBOptions(),
            Synthetic: new SyntheticMarketDataConfig(Enabled: true),
            ProviderModules: new ProviderModulesConfig(modules),
            Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(
                Synthetic: new SyntheticMarketDataConfig(Enabled: true))));
        var services = new ServiceCollection();
        services.AddSingleton<IMarketEventPublisher, TestMarketEventPublisher>();
        new CollectorFeatureRegistration().Register(services, CompositionOptions.WebDashboard);
        // The public application registration owns every adapter and factory. Only the
        // external credential source is replaced with a deterministic fixture.
        services.AddSingleton<Meridian.Core.IO.IAtomicFileWriter, Meridian.Storage.Archival.AtomicFileWriterAdapter>();
        services.AddProviderServices(config, new FixedCredentialResolver());
        return services;
    }

    private sealed class FixedCredentialResolver : IProviderCredentialResolver, ICredentialContext
    {
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null) => this;
        public string Get(string name) => "catalog-composition-fixture-credential";
        public bool IsConfigured(string name) => true;
    }

    public void Dispose()
    {
        ProviderCatalog.RuntimeCatalogProvider = null;
        ProviderCatalog.RuntimeCatalogEntryProvider = null;
    }
}
