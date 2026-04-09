using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="IBMarketDataClient"/>, the top-level IB streaming facade.
/// <para>
/// In non-IBAPI builds the facade delegates to <see cref="IBSimulationClient"/>.
/// This test validates the <em>outer</em> facade — its metadata, subscription wiring,
/// and disposal contract — regardless of whether the IBAPI package is present.
/// The separate <see cref="IBSimulationClientContractTests"/> covers the inner
/// simulation client in isolation.
/// </para>
/// </summary>
public sealed class IBMarketDataClientContractTests : MarketDataClientContractTests<IBMarketDataClient>
{
    protected override IBMarketDataClient CreateClient()
    {
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher, null);
        var depthCollector = new MarketDepthCollector(publisher);
        return new IBMarketDataClient(publisher, tradeCollector, depthCollector);
    }
}
