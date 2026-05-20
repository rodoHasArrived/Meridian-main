using Meridian.Execution.Sdk;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Infrastructure.Adapters.Synthetic;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Canonical capability descriptors used to keep provider metadata, implemented interfaces,
/// and registration paths aligned.
/// </summary>
public static class ProviderCapabilityDescriptorCatalog
{
    public static IReadOnlyList<ProviderCapabilityDescriptor> Descriptors { get; } =
    [
        new("alpaca", typeof(AlpacaMarketDataClient), typeof(AlpacaHistoricalDataProvider), typeof(AlpacaSymbolSearchProviderRefactored), typeof(AlpacaCorporateActionProvider), typeof(AlpacaOptionsChainProvider), typeof(AlpacaBrokerageAdapter)),
        new("synthetic", historical: typeof(SyntheticHistoricalDataProvider)),
        new("ib", historical: typeof(IBHistoricalDataProvider)),
        new("yahoo", historical: typeof(YahooFinanceHistoricalDataProvider)),
        new("polygon", historical: typeof(PolygonHistoricalDataProvider), search: typeof(PolygonSymbolSearchProvider), corporateActions: typeof(PolygonCorporateActionFetcher)),
        new("tiingo", historical: typeof(TiingoHistoricalDataProvider)),
        new("finnhub", historical: typeof(FinnhubHistoricalDataProvider), search: typeof(FinnhubSymbolSearchProviderRefactored)),
        new("stooq", historical: typeof(StooqHistoricalDataProvider)),
        new("alphavantage", historical: typeof(AlphaVantageHistoricalDataProvider)),
        new("fred", historical: typeof(FredHistoricalDataProvider)),
        new("nasdaq", historical: typeof(NasdaqDataLinkHistoricalDataProvider))
    ];
}

public sealed record ProviderCapabilityDescriptor(
    string ProviderId,
    Type? Streaming = null,
    Type? Historical = null,
    Type? Search = null,
    Type? CorporateActions = null,
    Type? Options = null,
    Type? Brokerage = null)
{
    public bool HasStreaming => Streaming is not null;
    public bool HasHistorical => Historical is not null;
    public bool HasSearch => Search is not null;
    public bool HasCorporateActions => CorporateActions is not null;
    public bool HasOptions => Options is not null;
    public bool HasBrokerage => Brokerage is not null;

    public IEnumerable<Type> Implementations()
    {
        if (Streaming is not null) yield return Streaming;
        if (Historical is not null) yield return Historical;
        if (Search is not null) yield return Search;
        if (CorporateActions is not null) yield return CorporateActions;
        if (Options is not null) yield return Options;
        if (Brokerage is not null) yield return Brokerage;
    }
}
