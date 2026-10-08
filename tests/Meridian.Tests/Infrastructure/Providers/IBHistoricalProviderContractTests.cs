using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="HistoricalDataProviderContractTests{TProvider}"/> suite to
/// <see cref="IBHistoricalDataProvider"/>.
/// <para>
/// In non-IBAPI builds the provider compiles to a no-arg stub that still satisfies the full
/// identity, metadata, capability, and disposal contracts.  This ensures IB's provider metadata
/// is always verifiable even when the optional IBAPI package is absent.
/// </para>
/// </summary>
public sealed class IBHistoricalProviderContractTests : HistoricalDataProviderContractTests<IBHistoricalDataProvider>, IDisposable
{
    private readonly EnhancedIBConnectionManager _connectionManager;

    public IBHistoricalProviderContractTests()
    {
        var publisher = new TestMarketEventPublisher();
        var router = new IBCallbackRouter(
            new MarketDepthCollector(publisher),
            new TradeDataCollector(publisher, null));
        _connectionManager = new EnhancedIBConnectionManager(router, enableHeartbeat: false);
    }

    protected override IBHistoricalDataProvider CreateProvider()
        => new(_connectionManager);

    public void Dispose() => _connectionManager.Dispose();
}
