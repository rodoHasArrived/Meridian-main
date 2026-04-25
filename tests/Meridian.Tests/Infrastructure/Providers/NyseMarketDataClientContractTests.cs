using System.Net.Http;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.NYSE;
using Meridian.Tests.TestHelpers;
using NSubstitute;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// Applies the shared <see cref="MarketDataClientContractTests{TClient}"/> suite to
/// <see cref="NyseMarketDataClient"/>.
/// <para>
/// The client is constructed with a stub <see cref="IHttpClientFactory"/> (no live network) and
/// default <see cref="NYSEOptions"/>.  Because <c>IsEnabled == true</c> for the NYSE client by
/// design, subscription contract tests call <c>IMarketDataClient.SubscribeTrades</c> and
/// <c>IMarketDataClient.SubscribeMarketDepth</c>; these return non-negative IDs without
/// establishing a real WebSocket connection.
/// </para>
/// </summary>
public sealed class NyseMarketDataClientContractTests : MarketDataClientContractTests<NyseMarketDataClient>
{
    protected override NyseMarketDataClient CreateClient()
    {
        var publisher = new TestMarketEventPublisher();
        var tradeCollector = new TradeDataCollector(publisher, null);
        var depthCollector = new MarketDepthCollector(publisher);
        var quoteCollector = new QuoteCollector(publisher);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient());

        return new NyseMarketDataClient(
            tradeCollector,
            depthCollector,
            quoteCollector,
            factory,
            new NYSEOptions());
    }
}
