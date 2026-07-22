using Meridian.Contracts.Domain.Enums;
using Meridian.Execution.Sdk;
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
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.Crypto]),
        new("synthetic", Historical: typeof(SyntheticHistoricalDataProvider),
            InstrumentTypes: [InstrumentType.Equity]),
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
        new("polygon", Historical: typeof(PolygonHistoricalDataProvider), Search: typeof(PolygonSymbolSearchProvider),
            InstrumentTypes: [InstrumentType.Equity, InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.Forex, InstrumentType.Crypto, InstrumentType.Index]),
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
}

public sealed record ProviderCapabilityDescriptor(
    string ProviderId,
    Type? Streaming = null,
    Type? Historical = null,
    Type? Search = null,
    Type? CorporateActions = null,
    Type? Options = null,
    Type? Brokerage = null,
    IBProviderCapabilityExecutionMode ExecutionMode = IBProviderCapabilityExecutionMode.NotApplicable,
    IReadOnlyList<InstrumentType>? InstrumentTypes = null)
{
    /// <summary>
    /// Instrument types this provider is declared to cover. Declared here, next to the adapter
    /// type wiring, so capability assertions stay close to the code that implements them.
    /// Defaults to equities when a provider has not declared broader coverage.
    /// </summary>
    public IReadOnlyList<InstrumentType> SupportedInstrumentTypes { get; } = InstrumentTypes ?? [InstrumentType.Equity];

    public bool HasStreaming => Streaming is not null;
    public bool HasHistorical => Historical is not null;
    public bool HasSearch => Search is not null;
    public bool HasCorporateActions => CorporateActions is not null;
    public bool HasOptions => Options is not null;
    public bool HasBrokerage => Brokerage is not null;

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
    }
}

/// <summary>Catalog-level readiness signal; inventory cannot promote a guidance build to live routing.</summary>
public enum IBProviderCapabilityExecutionMode
{
    NotApplicable,
    SimulationWhenVendorSdkUnavailable,
    VendorRuntimeRequiredForLive
}
