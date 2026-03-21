using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="IBSimulationClient"/>, which runs fully in-process without a live IB connection.
/// </summary>
public sealed class IBSimulationClientContractTests : MarketDataClientContractTests<IBSimulationClient>
{
    protected override IBSimulationClient CreateClient()
        => new(new TestMarketEventPublisher(), enableAutoTicks: false);
}
