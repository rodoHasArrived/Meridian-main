using Meridian.Contracts.Domain.Enums;
using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Edgar;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Robinhood;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Adapters.NYSE;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Canonical capability descriptors used to keep provider metadata, implemented interfaces,
/// and registration paths aligned.
/// </summary>
public static class ProviderCapabilityDescriptorCatalog
{
    public static IReadOnlyList<ProviderCapabilityDescriptor> Descriptors { get; } =
    [
        new("alpaca", typeof(AlpacaMarketDataClient), typeof(AlpacaHistoricalDataProvider), typeof(AlpacaSymbolSearchProvider), typeof(AlpacaCorporateActionProvider), typeof(AlpacaOptionsChainProvider), typeof(AlpacaBrokerageGateway),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.Crypto],
            StreamingAssetClasses: [MarketDataAssetClass.Equities, MarketDataAssetClass.Options, MarketDataAssetClass.Crypto, MarketDataAssetClass.News]),
        new("synthetic", Streaming: typeof(SyntheticMarketDataClient), Historical: typeof(SyntheticHistoricalDataProvider), Search: typeof(SyntheticMarketDataClient), Options: typeof(SyntheticOptionsChainProvider),
            Exclusions:
            [
                new(nameof(ICorporateActionProvider), "SyntheticHistoricalDataProvider emits historical corporate-action evidence through ICorporateActionSource; it is not an on-demand ICorporateActionProvider.")
            ],
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption],
            StreamingInstrumentTypes: [InstrumentType.Equity]),
        new("ibkr", Streaming: typeof(IBMarketDataClient), Historical: typeof(IBHistoricalDataProvider), Brokerage: typeof(IBBrokerageGateway),
            ExecutionMode: IBProviderCapabilityExecutionMode.SimulationWhenVendorSdkUnavailable,
            InstrumentTypes:
            [
                InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption,
                InstrumentType.Future, InstrumentType.FuturesOption, InstrumentType.Forex,
                InstrumentType.Bond, InstrumentType.CFD, InstrumentType.Warrant, InstrumentType.Index
            ]),
        new("yahoo", Historical: typeof(YahooFinanceHistoricalDataProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Index, InstrumentType.Forex, InstrumentType.Crypto]),
        new("polygon", Streaming: typeof(PolygonMarketDataClient), Historical: typeof(PolygonHistoricalDataProvider), Search: typeof(PolygonSymbolSearchProvider), Options: typeof(PolygonOptionsChainProvider),
            Exclusions:
            [
                new(nameof(ICorporateActionProvider), "PolygonCorporateActionFetcher is a hosted Security Master ingestion workflow; it does not implement the on-demand ICorporateActionProvider contract.")
            ],
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.Forex, InstrumentType.Crypto, InstrumentType.Index],
            StreamingInstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption]),
        new("nyse", Streaming: typeof(NyseMarketDataClient), CompatibilityDataSource: typeof(NYSEDataSource),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Index]),
        new(
            "robinhood",
            typeof(RobinhoodMarketDataClient),
            typeof(RobinhoodHistoricalDataProvider),
            typeof(RobinhoodSymbolSearchProvider),
            Options: typeof(RobinhoodOptionsChainProvider),
            Brokerage: typeof(RobinhoodBrokerageGateway),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.Crypto]),
        new("edgar", Search: typeof(EdgarSymbolSearchProvider),
            InstrumentTypes: [InstrumentType.Equity]),
        new("tiingo", Historical: typeof(TiingoHistoricalDataProvider), Search: typeof(TiingoSymbolSearchProvider), CorporateActions: typeof(TiingoCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Crypto]),
        new("twelvedata", Historical: typeof(TwelveDataHistoricalDataProvider), Search: typeof(TwelveDataSymbolSearchProvider), CorporateActions: typeof(TwelveDataCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Forex, InstrumentType.Crypto, InstrumentType.Index]),
        new("finnhub", Historical: typeof(FinnhubHistoricalDataProvider), Search: typeof(FinnhubSymbolSearchProvider), CorporateActions: typeof(FinnhubCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Forex, InstrumentType.Crypto]),
        new("stooq", Historical: typeof(StooqHistoricalDataProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Index]),
        new("alphavantage", Historical: typeof(AlphaVantageHistoricalDataProvider), Search: typeof(AlphaVantageSymbolSearchProvider), CorporateActions: typeof(AlphaVantageCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Forex, InstrumentType.Crypto]),
        new("fred", Historical: typeof(FredHistoricalDataProvider), Search: typeof(FredSymbolSearchProvider),
            InstrumentTypes: [InstrumentType.Index]),
        new("nasdaq", Historical: typeof(NasdaqDataLinkHistoricalDataProvider), Search: typeof(NasdaqDataLinkSymbolSearchProvider), CorporateActions: typeof(NasdaqDataLinkCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Commodity, InstrumentType.Index])
    ];

    /// <summary>
    /// Direct adapter folders that intentionally do not advertise provider capabilities. Keeping
    /// these exclusions beside the descriptors makes the folder-level inventory reviewable.
    /// </summary>
    public static IReadOnlyList<ProviderAdapterFamilyExclusion> ExcludedAdapterFamilies { get; } =
    [
        new("Core", "Shared provider primitives and orchestration, not a vendor adapter family."),
        new("Failover", "Composite streaming orchestration over catalogued providers, not an independent provider family."),
        new("OpenFigi", "Symbol resolution remains available through ISymbolResolver; the operator capability matrix does not expose a symbol-resolution surface."),
        new("Plaid", "Runtime financial-connectivity adapters implement Plaid-specific Contracts ports, not market-data provider capabilities."),
        new("Templates", "Copy-only provider and brokerage scaffolds are not runtime registrations."),
        new("TradeStation", "Mapper-only brokerage assets; no concrete shared-contract runtime adapter exists."),
        new("Tradier", "Mapper-only brokerage assets; no concrete shared-contract runtime adapter exists.")
    ];
}

public sealed record ProviderCapabilityDescriptor(
    string ProviderId,
    Type? Streaming = null,
    Type? Historical = null,
    Type? Search = null,
    Type? CorporateActions = null,
    Type? Options = null,
    Type? Brokerage = null,
    Type? SymbolResolver = null,
    Type? CompatibilityDataSource = null,
    IBProviderCapabilityExecutionMode ExecutionMode = IBProviderCapabilityExecutionMode.NotApplicable,
    IReadOnlyList<InstrumentType>? InstrumentTypes = null,
    IReadOnlyList<MarketDataAssetClass>? StreamingAssetClasses = null,
    IReadOnlyList<ProviderCapabilityExclusion>? Exclusions = null,
    IReadOnlyList<InstrumentType>? StreamingInstrumentTypes = null)
{
    /// <summary>
    /// Instrument types this provider is declared to cover. Declared here, next to the adapter
    /// type wiring, so capability assertions stay close to the code that implements them.
    /// Defaults to equities when a provider has not declared broader coverage.
    /// </summary>
    public IReadOnlyList<InstrumentType> SupportedInstrumentTypes { get; } = InstrumentTypes ?? [InstrumentType.Equity];

    public IReadOnlyList<MarketDataAssetClass> SupportedStreamingAssetClasses { get; } = StreamingAssetClasses ?? [];

    /// <summary>Streaming coverage can be narrower than historical or reference-data coverage.</summary>
    public IReadOnlyList<InstrumentType> SupportedStreamingInstrumentTypes { get; } = StreamingInstrumentTypes ?? InstrumentTypes ?? [InstrumentType.Equity];

    public IReadOnlyList<ProviderCapabilityExclusion> ExplicitExclusions { get; } = Exclusions ?? [];

    public bool HasStreaming => Streaming is not null;
    public bool HasHistorical => Historical is not null;
    public bool HasSearch => Search is not null;
    public bool HasCorporateActions => CorporateActions is not null;
    public bool HasOptions => Options is not null;
    public bool HasBrokerage => Brokerage is not null;
    public bool HasSymbolResolver => SymbolResolver is not null;
    public bool HasCompatibilityDataSource => CompatibilityDataSource is not null;

    public IEnumerable<Type> Implementations()
    {
        if (Streaming is not null)
            yield return Streaming;
        if (Historical is not null)
            yield return Historical;
        if (Search is not null)
            yield return Search;
        if (CorporateActions is not null)
            yield return CorporateActions;
        if (Options is not null)
            yield return Options;
        if (Brokerage is not null)
            yield return Brokerage;
        if (SymbolResolver is not null)
            yield return SymbolResolver;
        if (CompatibilityDataSource is not null)
            yield return CompatibilityDataSource;
    }
}

public sealed record ProviderCapabilityExclusion(string Capability, string Reason);

public sealed record ProviderAdapterFamilyExclusion(string FolderName, string Reason);

/// <summary>Catalog-level readiness signal; inventory cannot promote a guidance build to live routing.</summary>
public enum IBProviderCapabilityExecutionMode
{
    NotApplicable,
    SimulationWhenVendorSdkUnavailable,
    VendorRuntimeRequiredForLive
}
