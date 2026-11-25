using FluentAssertions;
using Meridian.Domain.Collectors;
using Meridian.Infrastructure.Adapters.InteractiveBrokers;
using Meridian.Tests.TestHelpers;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>
/// The router used to keep every ticker it ever registered: cancelled tickers still routed late
/// TWS callbacks into the collectors (corrupting a depth book a resubscription had just reset),
/// and reconnect replay re-sent depth without clearing the book, so TWS's full-book replay
/// duplicated every level.
/// </summary>
public sealed class IBCallbackRouterTickerLifecycleTests
{
    private const int Insert = 0;
    private const int Bid = 0;

    [Fact]
    public void UnregisteredDepthTicker_DropsLateCallbacks()
    {
        var (router, depth, _) = Create();
        router.RegisterDepthTicker(7, "MSFT");
        router.UpdateMktDepth(7, 0, Insert, Bid, 400.10, 100);

        router.UnregisterDepthTicker(7);
        router.ResetDepthBook("MSFT");
        router.UpdateMktDepth(7, 0, Insert, Bid, 399.00, 50); // late callback for the cancelled ticker

        depth.GetCurrentSnapshot("MSFT").Should().BeNull("a cancelled ticker must not write into the reset book");
    }

    [Fact]
    public void ReregisteredTicker_RoutesAgain()
    {
        var (router, depth, _) = Create();
        router.RegisterDepthTicker(7, "MSFT");
        router.UnregisterDepthTicker(7);

        router.RegisterDepthTicker(8, "MSFT");
        router.UpdateMktDepth(8, 0, Insert, Bid, 400.10, 100);

        depth.GetCurrentSnapshot("MSFT").Should().NotBeNull();
    }

    [Fact]
    public void UnregisteredTradeTicker_DropsLateTrades()
    {
        var (router, _, trades) = Create();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        router.RegisterTradeTicker(11, "AAPL");
        router.OnTickByTickAllLast(11, 1, now, 190.5, 100, "NASDAQ", string.Empty);
        trades.GetRecentTrades("AAPL").Should().ContainSingle("a registered ticker routes its trades");

        router.UnregisterTradeTicker(11);
        router.OnTickByTickAllLast(11, 1, now + 1, 190.6, 100, "NASDAQ", string.Empty);

        trades.GetRecentTrades("AAPL").Should().ContainSingle("the cancelled ticker's late trade is dropped");
    }

    [Fact]
    public void ResetDepthBook_ClearsLevelsSoAReplayDoesNotDuplicateThem()
    {
        var (router, depth, _) = Create();
        router.RegisterDepthTicker(7, "MSFT");
        router.UpdateMktDepth(7, 0, Insert, Bid, 400.10, 100);

        router.ResetDepthBook("MSFT");
        router.UpdateMktDepth(7, 0, Insert, Bid, 400.10, 100); // TWS replays the book from position 0

        depth.GetCurrentSnapshot("MSFT")!.Bids.Should().ContainSingle();
    }

    [Fact]
    public void UnregisterUnknownTicker_IsANoOp()
    {
        var (router, _, _) = Create();

        var act = () =>
        {
            router.UnregisterDepthTicker(99);
            router.UnregisterTradeTicker(99);
            router.UnregisterQuoteTicker(99);
        };

        act.Should().NotThrow();
    }

    private static (IBCallbackRouter Router, MarketDepthCollector Depth, TradeDataCollector Trades) Create()
    {
        var publisher = new TestMarketEventPublisher();
        var depth = new MarketDepthCollector(publisher, requireExplicitSubscription: false);
        var trades = new TradeDataCollector(publisher, null);
        return (new IBCallbackRouter(depth, trades, new QuoteCollector(publisher)), depth, trades);
    }
}
