using Meridian.Application.Config;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.StockSharp;
using Meridian.Tests.TestHelpers;

namespace Meridian.Tests.Infrastructure.Providers;

// ─────────────────────────────────────────────────────────────────────────────
//  Streaming contract
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="StockSharpMarketDataClient"/>.
/// <para>
/// The client is constructed with a disabled default config (<c>Enabled = false</c>) so that
/// the contract's identity / metadata / lifecycle tests run without requiring the StockSharp.Algo
/// runtime package to be present.  Subscription tests are automatically skipped when
/// <c>IsEnabled == false</c> per the base-class guard.
/// </para>
/// </summary>
public sealed class StockSharpMarketDataClientContractTests : MarketDataClientContractTests<StockSharpMarketDataClient>
{
    protected override StockSharpMarketDataClient CreateClient()
    {
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher, null);
        var depthCollector = new MarketDepthCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);
        // Enabled: false → no connector instantiation, safe without StockSharp.Algo.
        return new StockSharpMarketDataClient(
            tradeCollector,
            depthCollector,
            quoteCollector,
            new StockSharpConfig(Enabled: false));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Historical-provider contract
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Applies the shared <see cref="HistoricalDataProviderContractTests{TProvider}"/> suite to
/// <see cref="StockSharpHistoricalDataProvider"/>.
/// <para>
/// The provider is constructed with a default <see cref="StockSharpConfig"/>.  All contract
/// tests probe only identity, metadata, capability properties, and disposal — none of which
/// require a live StockSharp connector.
/// </para>
/// </summary>
public sealed class StockSharpHistoricalProviderContractTests : HistoricalDataProviderContractTests<StockSharpHistoricalDataProvider>
{
    protected override StockSharpHistoricalDataProvider CreateProvider()
        => new(new StockSharpConfig());
}
