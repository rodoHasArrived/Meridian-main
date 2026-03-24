using Meridian.Infrastructure.Adapters.AlphaVantage;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Adapters.TwelveData;
using Meridian.Infrastructure.Adapters.YahooFinance;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="HistoricalDataProviderContractTests{TProvider}"/> suite to
/// <see cref="StooqHistoricalDataProvider"/>, which requires no live API credentials.
/// </summary>
public sealed class StooqProviderContractTests : HistoricalDataProviderContractTests<StooqHistoricalDataProvider>
{
    protected override StooqHistoricalDataProvider CreateProvider()
        => new();
}

/// <summary>
/// Applies the shared <see cref="HistoricalDataProviderContractTests{TProvider}"/> suite to
/// <see cref="YahooFinanceHistoricalDataProvider"/>, which requires no live API credentials.
/// </summary>
public sealed class YahooFinanceProviderContractTests : HistoricalDataProviderContractTests<YahooFinanceHistoricalDataProvider>
{
    protected override YahooFinanceHistoricalDataProvider CreateProvider()
        => new();
}

/// <summary>
/// Applies the shared contract suite to <see cref="AlphaVantageHistoricalDataProvider"/>.
/// Instantiated with a stub key — identity and structural contracts only (no network calls).
/// </summary>
public sealed class AlphaVantageProviderContractTests : HistoricalDataProviderContractTests<AlphaVantageHistoricalDataProvider>
{
    protected override AlphaVantageHistoricalDataProvider CreateProvider()
        => new(apiKey: "stub-key");
}

/// <summary>
/// Applies the shared contract suite to <see cref="FinnhubHistoricalDataProvider"/>.
/// </summary>
public sealed class FinnhubProviderContractTests : HistoricalDataProviderContractTests<FinnhubHistoricalDataProvider>
{
    protected override FinnhubHistoricalDataProvider CreateProvider()
        => new(apiKey: "stub-key");
}

/// <summary>
/// Applies the shared contract suite to <see cref="TiingoHistoricalDataProvider"/>.
/// </summary>
public sealed class TiingoProviderContractTests : HistoricalDataProviderContractTests<TiingoHistoricalDataProvider>
{
    protected override TiingoHistoricalDataProvider CreateProvider()
        => new(apiToken: "stub-token");
}

/// <summary>
/// Applies the shared contract suite to <see cref="FredHistoricalDataProvider"/>.
/// </summary>
public sealed class FredProviderContractTests : HistoricalDataProviderContractTests<FredHistoricalDataProvider>
{
    protected override FredHistoricalDataProvider CreateProvider()
        => new(apiKey: "stub-key");
}

/// <summary>
/// Applies the shared contract suite to <see cref="TwelveDataHistoricalDataProvider"/>.
/// </summary>
public sealed class TwelveDataProviderContractTests : HistoricalDataProviderContractTests<TwelveDataHistoricalDataProvider>
{
    protected override TwelveDataHistoricalDataProvider CreateProvider()
        => new(apiKey: "stub-key");
}

/// <summary>
/// Applies the shared contract suite to <see cref="NasdaqDataLinkHistoricalDataProvider"/>.
/// IsAvailableAsync makes a real HTTP call but catches all exceptions and returns false,
/// so the contract's NotThrow assertion holds even in offline environments.
/// </summary>
public sealed class NasdaqDataLinkProviderContractTests : HistoricalDataProviderContractTests<NasdaqDataLinkHistoricalDataProvider>
{
    protected override NasdaqDataLinkHistoricalDataProvider CreateProvider()
        => new(apiKey: "stub-key");
}
