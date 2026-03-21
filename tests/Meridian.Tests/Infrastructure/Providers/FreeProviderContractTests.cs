using Meridian.Infrastructure.Adapters.Stooq;
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
