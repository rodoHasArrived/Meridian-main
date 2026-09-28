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

        new("synthetic", Streaming: typeof(SyntheticMarketDataClient), Historical: typeof(SyntheticHistoricalDataProvider), Search: typeof(SyntheticMarketDataClient), Options: typeof(SyntheticOptionsChainProvider),
            Exclusions:
            [
                new(nameof(ICorporateActionProvider), "SyntheticHistoricalDataProvider emits historical corporate-action evidence through ICorporateActionSource; it is not an on-demand ICorporateActionProvider.")
            ],
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption],
            StreamingInstrumentTypes: [InstrumentType.Equity],
            SearchInstrumentTypes: [InstrumentType.Equity],
            StreamingFactory: static f => f.CreateSyntheticStreamingClient(),
            HistoricalFactory: static f => f.CreateSyntheticBackfillProvider(f.Config.Backfill?.Providers?.Synthetic),
            SearchFactory: static f => f.CreateSyntheticSearchProvider(f.Config.Backfill?.Providers?.Synthetic)),
        new("ibkr", Streaming: typeof(IBMarketDataClient), Historical: typeof(IBHistoricalDataProvider), Brokerage: typeof(IBBrokerageGateway),
            ExecutionMode: IBProviderCapabilityExecutionMode.SimulationWhenVendorSdkUnavailable,
            InstrumentTypes:
            [
                InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption,
                InstrumentType.Future, InstrumentType.FuturesOption, InstrumentType.Forex,
                InstrumentType.Bond, InstrumentType.CFD, InstrumentType.Warrant, InstrumentType.Index
            ],
            StreamingFactory: static f => f.CreateIbStreamingClient(),
            HistoricalFactory: static f => f.CreateIbBackfillProvider(f.Config.IB),
            BrokerageFactory: static f => f.CreateIbBrokerageGateway()),
        new("alpaca", typeof(AlpacaMarketDataClient), typeof(AlpacaHistoricalDataProvider), typeof(AlpacaSymbolSearchProvider), typeof(AlpacaCorporateActionProvider), typeof(AlpacaOptionsChainProvider), typeof(AlpacaBrokerageGateway),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.Crypto],
            StreamingAssetClasses: [MarketDataAssetClass.Equities, MarketDataAssetClass.Options, MarketDataAssetClass.Crypto, MarketDataAssetClass.News],
            StreamingFactory: static f => f.CreateAlpacaStreamingClient(),
            HistoricalFactory: static f => f.CreateAlpacaBackfillProvider(f.Config.Backfill?.Providers?.Alpaca),
            SearchFactory: static f => f.CreateAlpacaSearchProvider(f.Config.Backfill?.Providers?.Alpaca),
            CorporateActionsFactory: static f => f.CreateCorporateActionProvider<AlpacaCorporateActionProvider, AlpacaHistoricalDataProvider>(
                ("ALPACA_KEY_ID", f.Config.Alpaca?.KeyId ?? f.Config.Backfill?.Providers?.Alpaca?.KeyId),
                ("ALPACA_SECRET_KEY", f.Config.Alpaca?.SecretKey ?? f.Config.Backfill?.Providers?.Alpaca?.SecretKey)),
            OptionsFactory: static f => f.CreateAlpacaOptionsProvider(),
            BrokerageFactory: static f => f.CreateAlpacaBrokerageGateway()),
        new("yahoo", Historical: typeof(YahooFinanceHistoricalDataProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Index, InstrumentType.Forex, InstrumentType.Crypto],
            HistoricalFactory: static f => f.CreateYahooBackfillProvider(f.Config.Backfill?.Providers?.Yahoo)),
        new("tiingo", Historical: typeof(TiingoHistoricalDataProvider), Search: typeof(TiingoSymbolSearchProvider), CorporateActions: typeof(TiingoCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Crypto],
            HistoricalFactory: static f => f.CreateTiingoBackfillProvider(f.Config.Backfill?.Providers?.Tiingo),
            SearchFactory: static f => f.CreateTiingoSearchProvider(f.Config.Backfill?.Providers?.Tiingo),
            CorporateActionsFactory: static f => f.CreateCorporateActionProvider<TiingoCorporateActionProvider, TiingoHistoricalDataProvider>(
                ("TIINGO_API_TOKEN", f.Config.Backfill?.Providers?.Tiingo?.ApiToken))),
        new("polygon", Streaming: typeof(PolygonMarketDataClient), Historical: typeof(PolygonHistoricalDataProvider), Search: typeof(PolygonSymbolSearchProvider), Options: typeof(PolygonOptionsChainProvider),
            Exclusions:
            [
                new(nameof(ICorporateActionProvider), "PolygonCorporateActionFetcher is a hosted Security Master ingestion workflow; it does not implement the on-demand ICorporateActionProvider contract.")
            ],
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.Forex, InstrumentType.Crypto, InstrumentType.Index],
            StreamingInstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption],
            StreamingFactory: static f => f.CreatePolygonStreamingClient(),
            HistoricalFactory: static f => f.CreatePolygonBackfillProvider(f.Config.Backfill?.Providers?.Polygon),
            SearchFactory: static f => f.CreatePolygonSearchProvider(f.Config.Backfill?.Providers?.Polygon),
            OptionsFactory: static f => f.CreatePolygonOptionsProvider()),
        new("twelvedata", Historical: typeof(TwelveDataHistoricalDataProvider), Search: typeof(TwelveDataSymbolSearchProvider), CorporateActions: typeof(TwelveDataCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Forex, InstrumentType.Crypto, InstrumentType.Index],
            HistoricalFactory: static f => f.CreateTwelveDataBackfillProvider(),
            SearchFactory: static f => f.CreateTwelveDataSearchProvider(),
            CorporateActionsFactory: static f => f.CreateCorporateActionProvider<TwelveDataCorporateActionProvider, TwelveDataHistoricalDataProvider>(
                ("TWELVEDATA_API_KEY", null))),
        new("finnhub", Historical: typeof(FinnhubHistoricalDataProvider), Search: typeof(FinnhubSymbolSearchProvider), CorporateActions: typeof(FinnhubCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Forex, InstrumentType.Crypto],
            HistoricalFactory: static f => f.CreateFinnhubBackfillProvider(f.Config.Backfill?.Providers?.Finnhub),
            SearchFactory: static f => f.CreateFinnhubSearchProvider(f.Config.Backfill?.Providers?.Finnhub),
            CorporateActionsFactory: static f => f.CreateCorporateActionProvider<FinnhubCorporateActionProvider, FinnhubHistoricalDataProvider>(
                ("FINNHUB_API_KEY", f.Config.Backfill?.Providers?.Finnhub?.ApiKey))),
        new("stooq", Historical: typeof(StooqHistoricalDataProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Index],
            HistoricalFactory: static f => f.CreateStooqBackfillProvider(f.Config.Backfill?.Providers?.Stooq)),
        new("alphavantage", Historical: typeof(AlphaVantageHistoricalDataProvider), Search: typeof(AlphaVantageSymbolSearchProvider), CorporateActions: typeof(AlphaVantageCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Forex, InstrumentType.Crypto],
            HistoricalFactory: static f => f.CreateAlphaVantageBackfillProvider(f.Config.Backfill?.Providers?.AlphaVantage),
            SearchFactory: static f => f.CreateAlphaVantageSearchProvider(f.Config.Backfill?.Providers?.AlphaVantage),
            CorporateActionsFactory: static f => f.CreateCorporateActionProvider<AlphaVantageCorporateActionProvider, AlphaVantageHistoricalDataProvider>(
                ("ALPHA_VANTAGE_API_KEY", f.Config.Backfill?.Providers?.AlphaVantage?.ApiKey))),
        new("fred", Historical: typeof(FredHistoricalDataProvider), Search: typeof(FredSymbolSearchProvider),
            InstrumentTypes: [InstrumentType.Index],
            HistoricalFactory: static f => f.CreateFredBackfillProvider(f.Config.Backfill?.Providers?.Fred),
            SearchFactory: static f => f.CreateFredSearchProvider(f.Config.Backfill?.Providers?.Fred)),
        new("nasdaq", Historical: typeof(NasdaqDataLinkHistoricalDataProvider), Search: typeof(NasdaqDataLinkSymbolSearchProvider), CorporateActions: typeof(NasdaqDataLinkCorporateActionProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Commodity, InstrumentType.Index],
            HistoricalFactory: static f => f.CreateNasdaqBackfillProvider(f.Config.Backfill?.Providers?.Nasdaq),
            SearchFactory: static f => f.CreateNasdaqSearchProvider(f.Config.Backfill?.Providers?.Nasdaq),
            CorporateActionsFactory: static f => f.CreateCorporateActionProvider<NasdaqDataLinkCorporateActionProvider, NasdaqDataLinkHistoricalDataProvider>(
                ("NASDAQ_DATA_LINK_API_KEY", f.Config.Backfill?.Providers?.Nasdaq?.ApiKey))),
        new(
            "robinhood",
            typeof(RobinhoodMarketDataClient),
            typeof(RobinhoodHistoricalDataProvider),
            typeof(RobinhoodSymbolSearchProvider),
            Options: typeof(RobinhoodOptionsChainProvider),
            Brokerage: typeof(RobinhoodBrokerageGateway),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.Crypto],
            StreamingFactory: static f => f.CreateRobinhoodStreamingClient(),
            HistoricalFactory: static f => f.CreateRobinhoodBackfillProvider(f.Config.Backfill?.Providers?.Robinhood),
            SearchFactory: static f => f.CreateRobinhoodSearchProvider(),
            OptionsFactory: static f => f.CreateRobinhoodOptionsProvider(),
            BrokerageFactory: static f => f.CreateRobinhoodBrokerageGateway(),
            OptionsEnabled: static f => f.HasRobinhoodOptionsCredentials),
        new("edgar", Search: typeof(EdgarSymbolSearchProvider),
            InstrumentTypes: [InstrumentType.Equity],
            SearchFactory: static _ => new EdgarSymbolSearchProvider()),
        new("nyse", Streaming: typeof(NyseMarketDataClient), CompatibilityDataSource: typeof(NYSEDataSource),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.Index],
            StreamingFactory: static f => f.CreateNyseStreamingClient())
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
    IReadOnlyList<InstrumentType>? StreamingInstrumentTypes = null,
    IReadOnlyList<InstrumentType>? SearchInstrumentTypes = null,
    Func<ProviderFactory, IMarketDataClient?>? StreamingFactory = null,
    Func<ProviderFactory, IHistoricalDataProvider?>? HistoricalFactory = null,
    Func<ProviderFactory, ISymbolSearchProvider?>? SearchFactory = null,
    Func<ProviderFactory, ICorporateActionProvider?>? CorporateActionsFactory = null,
    Func<ProviderFactory, IOptionsChainProvider?>? OptionsFactory = null,
    Func<ProviderFactory, IBrokerageGateway?>? BrokerageFactory = null,
    Func<ProviderFactory, bool>? OptionsEnabled = null)
{
    /// <summary>
    /// The same slots that advertise capabilities supply their construction functions.
    /// A null result means configuration or credentials disabled that capability; it never
    /// permits substitution with another provider family.
    /// </summary>
    public IEnumerable<ProviderCapabilityRegistration> Registrations()
    {
        if (Streaming is not null)
            yield return Create<IMarketDataClient>(Streaming, StreamingFactory);
        if (Historical is not null)
            yield return Create<IHistoricalDataProvider>(Historical, HistoricalFactory);
        if (Search is not null)
            yield return Create<ISymbolSearchProvider>(Search, SearchFactory);
        if (CorporateActions is not null)
            yield return Create<ICorporateActionProvider>(CorporateActions, CorporateActionsFactory);
        if (Options is not null)
            yield return Create<IOptionsChainProvider>(Options, OptionsFactory) with
            {
                IsEnabled = OptionsEnabled ?? (static _ => true)
            };
        if (Brokerage is not null)
            yield return Create<IBrokerageGateway>(Brokerage, BrokerageFactory);
    }

    private ProviderCapabilityRegistration Create<T>(Type implementation, Func<ProviderFactory, T?>? factory)
        where T : class
        => new(ProviderId, typeof(T), implementation,
            context => factory is null ? context.CreateAdapter(implementation) : factory(context));

    /// <summary>
    /// Instrument types this provider is declared to cover. Declared here, next to the adapter
    /// type wiring, so capability assertions stay close to the code that implements them.
    /// Defaults to equities when a provider has not declared broader coverage.
    /// </summary>
    public IReadOnlyList<InstrumentType> SupportedInstrumentTypes { get; } = InstrumentTypes ?? [InstrumentType.Equity];

    public IReadOnlyList<MarketDataAssetClass> SupportedStreamingAssetClasses { get; } = StreamingAssetClasses ?? [];

    /// <summary>Streaming coverage can be narrower than historical or reference-data coverage.</summary>
    public IReadOnlyList<InstrumentType> SupportedStreamingInstrumentTypes { get; } = StreamingInstrumentTypes ?? InstrumentTypes ?? [InstrumentType.Equity];

    /// <summary>Search coverage follows the reference catalog, independently of option-chain coverage.</summary>
    public IReadOnlyList<InstrumentType> SupportedSearchInstrumentTypes { get; } = SearchInstrumentTypes ?? InstrumentTypes ?? [InstrumentType.Equity];

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

/// <summary>A constructible capability belonging to one canonical provider family.</summary>
public sealed record ProviderCapabilityRegistration(
    string ProviderId,
    Type Contract,
    Type Implementation,
    Func<ProviderFactory, object?> Factory)
{
    public Func<ProviderFactory, bool> IsEnabled { get; init; } = static _ => true;
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
