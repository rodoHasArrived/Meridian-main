using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Infrastructure.Adapters.OpenFigi;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.DataSources;
using Meridian.ProviderSdk;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProviderCapabilityDescriptor = Meridian.Infrastructure.Adapters.Core.ProviderCapabilityDescriptor;

namespace Meridian.Tests.Providers;

/// <summary>
/// Guards the complete runtime provider inventory, implemented shared contracts, and reasoned exclusions.
/// </summary>
public sealed class ProviderCapabilityDescriptorCatalogTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedProviderCapabilities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alpaca"] = "streaming,historical,search,corporate-actions,options,brokerage",
            ["alphavantage"] = "historical,search,corporate-actions",
            ["edgar"] = "search",
            ["finnhub"] = "historical,search,corporate-actions",
            ["fred"] = "historical,search",
            ["ibkr"] = "streaming,historical,brokerage",
            ["nasdaq"] = "historical,search,corporate-actions",
            ["nyse"] = "streaming,data-source-compatibility",
            ["openfigi"] = "symbol-resolution",
            ["polygon"] = "streaming,historical,search,options",
            ["robinhood"] = "streaming,historical,search,options,brokerage",
            ["stooq"] = "historical",
            ["synthetic"] = "streaming,historical,search,options",
            ["tiingo"] = "historical,search,corporate-actions",
            ["twelvedata"] = "historical,search,corporate-actions",
            ["yahoo"] = "historical"
        };

    // A new folder must be audited explicitly; these reasons must survive catalog changes.
    private static readonly IReadOnlyDictionary<string, string> ExpectedExcludedFamilies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Core"] = "Shared provider primitives",
            ["Failover"] = "Composite streaming orchestration",
            ["Plaid"] = "Plaid-specific Contracts ports",
            ["Templates"] = "scaffolds",
            ["TradeStation"] = "Mapper-only",
            ["Tradier"] = "Mapper-only"
        };

    [Fact]
    public void Descriptors_match_expected_provider_inventory_and_capabilities()
    {
        var providerIds = ProviderCapabilityDescriptorCatalog.Descriptors
            .Select(static descriptor => descriptor.ProviderId)
            .ToArray();
        providerIds.Should().OnlyHaveUniqueItems(static providerId => providerId.ToUpperInvariant(),
            "provider identifiers are case-insensitive");
        providerIds.Should().BeEquivalentTo(ExpectedProviderCapabilities.Keys,
            "missing providers must fail even when their folders are moved to the exclusion list");

        foreach (var (providerId, capabilities) in ExpectedProviderCapabilities)
        {
            var descriptor = ProviderCapabilityDescriptorCatalog.Descriptors
                .Single(descriptor => descriptor.ProviderId == providerId);
            GetCapabilityInventory(descriptor).Should().Be(capabilities,
                "provider '{0}' must expose its complete audited shared-contract inventory", providerId);
        }
    }

    [Fact]
    public void Descriptors_match_implemented_interfaces()
    {
        foreach (var descriptor in ProviderCapabilityDescriptorCatalog.Descriptors)
        {
            if (descriptor.Streaming is not null)
            {
                descriptor.Streaming.Should().BeAssignableTo<IMarketDataClient>();
            }

            if (descriptor.Historical is not null)
            {
                descriptor.Historical.Should().BeAssignableTo<IHistoricalDataProvider>();
            }

            if (descriptor.Search is not null)
            {
                descriptor.Search.Should().BeAssignableTo<ISymbolSearchProvider>();
            }

            if (descriptor.CorporateActions is not null)
            {
                descriptor.CorporateActions.Should().BeAssignableTo<ICorporateActionProvider>();
            }

            if (descriptor.Options is not null)
            {
                descriptor.Options.Should().BeAssignableTo<IOptionsChainProvider>();
            }

            if (descriptor.Brokerage is not null)
            {
                descriptor.Brokerage.Should().BeAssignableTo<IBrokerageGateway>();
            }

            if (descriptor.SymbolResolver is not null)
            {
                descriptor.SymbolResolver.Should().BeAssignableTo<ISymbolResolver>();
            }

            if (descriptor.CompatibilityDataSource is not null)
            {
                descriptor.CompatibilityDataSource.Should().BeAssignableTo<IDataSource>();
            }
        }
    }

    [Fact]
    public void Descriptors_and_exclusions_cover_every_direct_adapter_folder()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, "src", "Meridian.Infrastructure", "Adapters")))
            repository = repository.Parent;
        repository.Should().NotBeNull("source inventory validation requires the repository checkout");
        var adapterRoot = Path.Combine(repository!.FullName, "src", "Meridian.Infrastructure", "Adapters");
        var actualFolders = Directory.GetDirectories(adapterRoot).Select(Path.GetFileName).ToArray();
        var declaredFolders = ProviderCapabilityDescriptorCatalog.Descriptors
            .SelectMany(static descriptor => descriptor.Implementations())
            .Select(GetAdapterFolder)
            .Distinct(StringComparer.Ordinal)
            .Concat(ProviderCapabilityDescriptorCatalog.ExcludedAdapterFamilies.Select(static exclusion => exclusion.FolderName))
            .ToArray();
        declaredFolders.Should().OnlyHaveUniqueItems("a family must be either catalogued or explicitly excluded");
        declaredFolders.Should().BeEquivalentTo(actualFolders,
            "adding or removing a source adapter family must update the audited inventory");
        ProviderCapabilityDescriptorCatalog.ExcludedAdapterFamilies.Should().OnlyContain(
            static exclusion => !string.IsNullOrWhiteSpace(exclusion.Reason),
            "every non-provider adapter folder must retain an explicit exclusion reason");

        ProviderCapabilityDescriptorCatalog.ExcludedAdapterFamilies
            .Select(static exclusion => exclusion.FolderName)
            .Should().BeEquivalentTo(ExpectedExcludedFamilies.Keys);
        foreach (var exclusion in ProviderCapabilityDescriptorCatalog.ExcludedAdapterFamilies)
        {
            exclusion.Reason.Should().Contain(ExpectedExcludedFamilies[exclusion.FolderName]);
        }
    }

    [Fact]
    public void Descriptors_cover_shared_contracts_implemented_by_every_runtime_family()
    {
        (Type Contract, Func<ProviderCapabilityDescriptor, Type?> Implementation)[] capabilities =
        [
            (typeof(IMarketDataClient), static descriptor => descriptor.Streaming),
            (typeof(IHistoricalDataProvider), static descriptor => descriptor.Historical),
            (typeof(ISymbolSearchProvider), static descriptor => descriptor.Search),
            (typeof(ICorporateActionProvider), static descriptor => descriptor.CorporateActions),
            (typeof(IOptionsChainProvider), static descriptor => descriptor.Options),
            (typeof(IBrokerageGateway), static descriptor => descriptor.Brokerage),
            (typeof(ISymbolResolver), static descriptor => descriptor.SymbolResolver),
            (typeof(IDataSource), static descriptor => descriptor.CompatibilityDataSource)
        ];
        var runtimeFamilies = typeof(ProviderCapabilityDescriptorCatalog).Assembly.GetTypes()
            .Where(static type => type.IsClass && !type.IsAbstract && type.IsPublic &&
                type.Namespace?.StartsWith("Meridian.Infrastructure.Adapters.", StringComparison.Ordinal) == true)
            .Where(type => !ExpectedExcludedFamilies.ContainsKey(GetAdapterFolder(type)))
            .GroupBy(GetAdapterFolder);

        foreach (var family in runtimeFamilies)
        {
            var descriptor = ProviderCapabilityDescriptorCatalog.Descriptors.Should().ContainSingle(
                descriptor => descriptor.Implementations().Any(type => GetAdapterFolder(type) == family.Key),
                "runtime adapter family '{0}' must have a descriptor", family.Key).Which;

            foreach (var (contract, implementation) in capabilities)
            {
                var implementingTypes = family.Where(contract.IsAssignableFrom).ToArray();
                if (implementingTypes.Length == 0)
                    continue;

                implementation(descriptor).Should().NotBeNull(
                    "family '{0}' implements {1} through {2}", family.Key, contract.Name,
                    string.Join(", ", implementingTypes.Select(static type => type.Name)));
                implementingTypes.Should().Contain(implementation(descriptor),
                    "the descriptor must select an implementation from its own adapter family");
            }
        }
    }

    [Fact]
    public void Newly_catalogued_provider_families_expose_every_implemented_shared_capability()
    {
        var descriptors = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);

        var synthetic = descriptors["synthetic"];
        synthetic.Streaming.Should().Be(typeof(SyntheticMarketDataClient));
        synthetic.Historical.Should().Be(typeof(SyntheticHistoricalDataProvider));
        synthetic.Search.Should().Be(typeof(SyntheticMarketDataClient));
        synthetic.Options.Should().Be(typeof(SyntheticOptionsChainProvider));
        synthetic.CorporateActions.Should().BeNull(
            "SyntheticHistoricalDataProvider exposes historical corporate-action evidence, not ICorporateActionProvider");
        synthetic.Brokerage.Should().BeNull();

        var polygon = descriptors["polygon"];
        polygon.Streaming.Should().Be(typeof(PolygonMarketDataClient));
        polygon.Historical.Should().Be(typeof(PolygonHistoricalDataProvider));
        polygon.Search.Should().Be(typeof(PolygonSymbolSearchProvider));
        polygon.Options.Should().Be(typeof(PolygonOptionsChainProvider));
        polygon.CorporateActions.Should().BeNull();
        polygon.ExplicitExclusions.Should().ContainSingle(exclusion =>
            exclusion.Capability == nameof(ICorporateActionProvider) &&
            exclusion.Reason.Contains(nameof(PolygonCorporateActionFetcher), StringComparison.Ordinal));
        typeof(PolygonCorporateActionFetcher).Should().BeAssignableTo<IHostedService>();
        typeof(PolygonCorporateActionFetcher).Should().NotBeAssignableTo<ICorporateActionProvider>();

        var nyse = descriptors["nyse"];
        nyse.Streaming.Should().Be(typeof(NyseMarketDataClient));
        nyse.CompatibilityDataSource.Should().Be(typeof(NYSEDataSource));
        nyse.Historical.Should().BeNull(
            "NYSE historical access remains on its IDataSource compatibility adapter rather than IHistoricalDataProvider");
        nyse.CompatibilityDataSource.Should().BeAssignableTo<IHistoricalDataSource>();
        nyse.CompatibilityDataSource.Should().BeAssignableTo<IRealtimeDataSource>();
        nyse.ExplicitExclusions.Should().ContainSingle(exclusion =>
            exclusion.Capability == nameof(IHistoricalDataProvider) &&
            exclusion.Reason.Contains(nameof(IHistoricalDataSource), StringComparison.Ordinal));

        var openFigi = descriptors["openfigi"];
        openFigi.SymbolResolver.Should().Be(typeof(OpenFigiSymbolResolver));
        openFigi.Search.Should().BeNull("ISymbolResolver.SearchAsync is not the ISymbolSearchProvider contract");
        openFigi.ExplicitExclusions.Should().ContainSingle(exclusion =>
            exclusion.Capability == nameof(ISymbolSearchProvider) &&
            exclusion.Reason.Contains(nameof(ISymbolResolver), StringComparison.Ordinal));
        openFigi.Implementations().Should().Equal(typeof(OpenFigiSymbolResolver));
    }

    [Fact]
    public void Scenario_RuntimeCapabilityDrift_SupportedRuntimeProvidersExposeImplementedDescriptors()
    {
        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);

        descriptorsById.Keys.Should().Contain("robinhood");
        var robinhood = descriptorsById["robinhood"];
        robinhood.Streaming.Should().Be(typeof(RobinhoodMarketDataClient));
        robinhood.Historical.Should().Be(typeof(RobinhoodHistoricalDataProvider));
        robinhood.Search.Should().Be(typeof(RobinhoodSymbolSearchProvider));
        robinhood.Options.Should().Be(typeof(RobinhoodOptionsChainProvider));
        robinhood.Brokerage.Should().Be(typeof(RobinhoodBrokerageGateway));
        robinhood.CorporateActions.Should().BeNull(
            "Robinhood has no dedicated ICorporateActionProvider implementation in the runtime catalog");

        var ibkr = descriptorsById["ibkr"];
        ibkr.Streaming.Should().Be(typeof(IBMarketDataClient));
        ibkr.Historical.Should().Be(typeof(IBHistoricalDataProvider));
        ibkr.Brokerage.Should().Be(typeof(IBBrokerageGateway));
        ibkr.ExecutionMode.Should().Be(IBProviderCapabilityExecutionMode.SimulationWhenVendorSdkUnavailable);

        descriptorsById.Keys.Should().Contain("edgar");
        var edgar = descriptorsById["edgar"];
        edgar.Search.Should().Be(typeof(EdgarSymbolSearchProvider));
        edgar.Streaming.Should().BeNull();
        edgar.Historical.Should().BeNull();
        edgar.Options.Should().BeNull();
        edgar.Brokerage.Should().BeNull();
        edgar.CorporateActions.Should().BeNull(
            "EDGAR corporate-action support is routed through Security Master/reference-data workflows, not an ICorporateActionProvider implementation");
        edgar.ExplicitExclusions.Should().ContainSingle(exclusion =>
            exclusion.Capability == nameof(ICorporateActionProvider) &&
            exclusion.Reason.Contains(nameof(EdgarSecurityMasterIngestProvider), StringComparison.Ordinal));

        descriptorsById.Keys.Should().NotContain("ib",
            "the IB family uses one provider identifier across streaming, historical, and brokerage capabilities");
    }

    [Fact]
    public void Scenario_BrokerageExperimentGate_MapperOnlyBrokersDoNotAdvertiseRuntimeDescriptors()
    {
        string[] mapperOnlyBrokerageProviderIds =
        [
            "tradier",
            "tradestation"
        ];

        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);
        var descriptorImplementations = ProviderCapabilityDescriptorCatalog.Descriptors
            .SelectMany(static descriptor => descriptor.Implementations())
            .Select(static implementation => implementation.FullName ?? implementation.Name)
            .ToArray();

        descriptorsById.Keys.Should().NotContain(mapperOnlyBrokerageProviderIds,
            "mapper-only brokerage assets must not become runtime provider capabilities without concrete adapters and lifecycle evidence");
        descriptorImplementations.Should().NotContain(
            implementation => implementation.Contains(".Adapters.Tradier.", StringComparison.OrdinalIgnoreCase) ||
                              implementation.Contains(".Adapters.TradeStation.", StringComparison.OrdinalIgnoreCase),
            "mapper-only brokerage assets must not enter the capability descriptor catalog until they implement shared runtime provider contracts");
    }

    [Fact]
    public void Scenario_RuntimeCapabilityDrift_FreeTierBackfillProvidersRemainFailClosed()
    {
        string[] inventoryOnlyBackfillProviders =
        [
            "fred",
            "stooq",
            "yahoo"
        ];

        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);

        descriptorsById.Keys.Should().Contain(inventoryOnlyBackfillProviders);

        foreach (var providerId in inventoryOnlyBackfillProviders)
        {
            descriptorsById[providerId].Streaming.Should().BeNull(
                "inventory/backfill provider '{0}' must not advertise streaming readiness without a dedicated provider implementation and evidence",
                providerId);
            descriptorsById[providerId].CorporateActions.Should().BeNull(
                "inventory/backfill provider '{0}' must not advertise corporate-action readiness without a dedicated provider implementation and evidence",
                providerId);
            descriptorsById[providerId].Brokerage.Should().BeNull(
                "inventory/backfill provider '{0}' must not advertise brokerage readiness without a dedicated provider implementation and evidence",
                providerId);
        }

        var finnhub = descriptorsById["finnhub"];
        finnhub.Historical.Should().Be(typeof(FinnhubHistoricalDataProvider));
        finnhub.Search.Should().Be(typeof(FinnhubSymbolSearchProvider));
        finnhub.CorporateActions.Should().Be(typeof(FinnhubCorporateActionProvider));
        finnhub.Streaming.Should().BeNull(
            "Finnhub has no dedicated streaming provider implementation in the runtime catalog");
        finnhub.Brokerage.Should().BeNull(
            "Finnhub is a data provider and must not advertise brokerage readiness");

        var tiingo = descriptorsById["tiingo"];
        tiingo.Historical.Should().Be(typeof(TiingoHistoricalDataProvider));
        tiingo.Search.Should().Be(typeof(TiingoSymbolSearchProvider));
        tiingo.CorporateActions.Should().Be(typeof(TiingoCorporateActionProvider));
        tiingo.Streaming.Should().BeNull(
            "Tiingo has no dedicated streaming provider implementation in the runtime catalog");
        tiingo.Brokerage.Should().BeNull(
            "Tiingo is a data provider and must not advertise brokerage readiness");

        var alphaVantage = descriptorsById["alphavantage"];
        alphaVantage.Historical.Should().Be(typeof(AlphaVantageHistoricalDataProvider));
        alphaVantage.Search.Should().Be(typeof(AlphaVantageSymbolSearchProvider));
        alphaVantage.CorporateActions.Should().Be(typeof(AlphaVantageCorporateActionProvider));
        alphaVantage.Streaming.Should().BeNull(
            "Alpha Vantage has no dedicated streaming provider implementation in the runtime catalog");
        alphaVantage.Brokerage.Should().BeNull(
            "Alpha Vantage is a data provider and must not advertise brokerage readiness");

        var nasdaq = descriptorsById["nasdaq"];
        nasdaq.Historical.Should().Be(typeof(NasdaqDataLinkHistoricalDataProvider));
        nasdaq.Search.Should().Be(typeof(NasdaqDataLinkSymbolSearchProvider));
        nasdaq.CorporateActions.Should().Be(typeof(NasdaqDataLinkCorporateActionProvider));
        nasdaq.Streaming.Should().BeNull(
            "Nasdaq Data Link has no dedicated streaming provider implementation in the runtime catalog");
        nasdaq.Brokerage.Should().BeNull(
            "Nasdaq Data Link is a data provider and must not advertise brokerage readiness");

        var fred = descriptorsById["fred"];
        fred.Historical.Should().Be(typeof(FredHistoricalDataProvider));
        fred.Search.Should().Be(typeof(FredSymbolSearchProvider));
        fred.Streaming.Should().BeNull(
            "FRED has no dedicated streaming provider implementation in the runtime catalog");
        fred.CorporateActions.Should().BeNull(
            "FRED economic-series search is reference discovery, not corporate-action readiness");
        fred.Brokerage.Should().BeNull(
            "FRED is a research data provider and must not advertise brokerage readiness");

        var twelveData = descriptorsById["twelvedata"];
        twelveData.Historical.Should().Be(typeof(TwelveDataHistoricalDataProvider));
        twelveData.Search.Should().Be(typeof(TwelveDataSymbolSearchProvider));
        twelveData.CorporateActions.Should().Be(typeof(TwelveDataCorporateActionProvider));
        twelveData.Streaming.Should().BeNull(
            "Twelve Data has no dedicated streaming provider implementation in the runtime catalog");
        twelveData.Brokerage.Should().BeNull(
            "Twelve Data is a data provider and must not advertise brokerage readiness");
    }

    [Fact]
    public void Scenario_UnsupportedYahooStreaming_RuntimeCatalogKeepsHistoricalFallbackOnly()
    {
        var descriptorsById = ProviderCapabilityDescriptorCatalog.Descriptors
            .ToDictionary(static descriptor => descriptor.ProviderId, StringComparer.OrdinalIgnoreCase);

        descriptorsById.Keys.Should().Contain("yahoo");
        var yahoo = descriptorsById["yahoo"];
        yahoo.Historical.Should().Be(typeof(YahooFinanceHistoricalDataProvider));
        yahoo.Streaming.Should().BeNull(
            "Yahoo Finance has no dedicated IMarketDataClient implementation or reconnect/runtime evidence");
        yahoo.Search.Should().BeNull(
            "Yahoo Finance is not a symbol-search provider in the runtime catalog");
        yahoo.CorporateActions.Should().BeNull(
            "Yahoo adjusted-bar event extraction is historical evidence, not a registered ICorporateActionProvider");
        yahoo.Brokerage.Should().BeNull(
            "Yahoo Finance is a data fallback and must never advertise brokerage readiness");
    }

    private static string GetAdapterFolder(Type implementation)
        => implementation.Namespace!["Meridian.Infrastructure.Adapters.".Length..].Split('.')[0];

    private static string GetCapabilityInventory(ProviderCapabilityDescriptor descriptor)
    {
        var capabilities = new List<string>();
        if (descriptor.HasStreaming)
            capabilities.Add("streaming");
        if (descriptor.HasHistorical)
            capabilities.Add("historical");
        if (descriptor.HasSearch)
            capabilities.Add("search");
        if (descriptor.HasCorporateActions)
            capabilities.Add("corporate-actions");
        if (descriptor.HasOptions)
            capabilities.Add("options");
        if (descriptor.HasBrokerage)
            capabilities.Add("brokerage");
        if (descriptor.HasSymbolResolver)
            capabilities.Add("symbol-resolution");
        if (descriptor.HasCompatibilityDataSource)
            capabilities.Add("data-source-compatibility");
        return string.Join(',', capabilities);
    }

}
