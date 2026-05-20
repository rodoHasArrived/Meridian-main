using Meridian.Infrastructure.Adapters.Synthetic;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class SyntheticHistoricalProviderContractTests : HistoricalDataProviderContractTests<SyntheticHistoricalDataProvider>
{
    protected override SyntheticHistoricalDataProvider CreateProvider()
        => SyntheticProviderTestHarness.CreateHistorical();
}
