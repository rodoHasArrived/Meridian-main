using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Domain.Events;
using Meridian.Domain.Models;

namespace Meridian.DataIntegration.Testing;

public static class DepthBufferSelfTests
{
    public static void Run()
    {
        Test_InsertUpdateDelete();
        Test_GapMarksStale();
    }

    private static void Test_InsertUpdateDelete()
    {
        var collector = new MarketDepthCollector(NullMarketEventPublisher.Instance, requireExplicitSubscription: false);
        var ts = DateTimeOffset.UtcNow;
        var sym = "TEST";

        collector.OnDepth(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 100, 10, Source: "SELFTEST"));
        var snap1 = collector.GetCurrentSnapshot(sym);
        if (snap1 is null || snap1.Bids.Count != 1)
            throw new InvalidOperationException("Insert bid failed.");

        collector.OnDepth(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Insert, OrderBookSide.Ask, 101, 12, Source: "SELFTEST"));
        var snap2 = collector.GetCurrentSnapshot(sym);
        if (snap2 is null)
            throw new InvalidOperationException("Insert ask failed.");
        if (snap2.Bids.Count != 1 || snap2.Asks.Count != 1)
            throw new InvalidOperationException("Counts incorrect.");

        collector.OnDepth(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Update, OrderBookSide.Bid, 100, 20, Source: "SELFTEST"));
        var snap3 = collector.GetCurrentSnapshot(sym);
        if (snap3 is null)
            throw new InvalidOperationException("Update bid failed.");
        if (Math.Abs(snap3.Bids[0].Size - 20) > 0.000000001m)
            throw new InvalidOperationException("Update size incorrect.");

        collector.OnDepth(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Delete, OrderBookSide.Ask, 0, 0, Source: "SELFTEST"));
        var snap4 = collector.GetCurrentSnapshot(sym);
        if (snap4 is null)
            throw new InvalidOperationException("Delete ask failed.");
        if (snap4.Asks.Count != 0)
            throw new InvalidOperationException("Ask delete did not apply.");
    }

    private static void Test_GapMarksStale()
    {
        var collector = new MarketDepthCollector(NullMarketEventPublisher.Instance, requireExplicitSubscription: false);
        var ts = DateTimeOffset.UtcNow;
        var sym = "TEST";

        collector.OnDepth(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Update, OrderBookSide.Bid, 100, 10, Source: "SELFTEST"));
        if (!collector.IsSymbolStreamStale(sym))
            throw new InvalidOperationException("Expected stale after integrity failure.");
        if (collector.GetRecentIntegrityEvents(1).FirstOrDefault()?.Kind != DepthIntegrityKind.OutOfOrder)
            throw new InvalidOperationException("Expected out-of-order integrity event.");

        collector.OnDepth(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 100, 10, Source: "SELFTEST"));
        if (collector.GetRecentIntegrityEvents(1).FirstOrDefault()?.Kind != DepthIntegrityKind.Stale)
            throw new InvalidOperationException("Expected stale on subsequent apply.");
    }

    private sealed class NullMarketEventPublisher : IMarketEventPublisher
    {
        public static readonly NullMarketEventPublisher Instance = new();

        private NullMarketEventPublisher()
        {
        }

        public bool TryPublish(in MarketEvent evt) => true;
    }
}
