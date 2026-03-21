using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;

namespace Meridian.Application.Testing;

public static class DepthBufferSelfTests
{
    public static void Run()
    {
        Test_InsertUpdateDelete();
        Test_GapMarksStale();
    }

    private static void Test_InsertUpdateDelete()
    {
        var buf = new MarketDepthCollector.SymbolOrderBookBuffer(50);
        var ts = DateTimeOffset.UtcNow;
        var sym = "TEST";

        // insert bid0, ask0
        var r1 = buf.Apply(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 100, 10), out var snap1);
        if (r1 != DepthIntegrityKind.Ok || snap1 is null)
            throw new Exception("Insert bid failed.");

        var r2 = buf.Apply(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Insert, OrderBookSide.Ask, 101, 12), out var snap2);
        if (r2 != DepthIntegrityKind.Ok || snap2 is null)
            throw new Exception("Insert ask failed.");
        if (snap2.Bids.Count != 1 || snap2.Asks.Count != 1)
            throw new Exception("Counts incorrect.");

        // update bid0
        var r3 = buf.Apply(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Update, OrderBookSide.Bid, 100, 20), out var snap3);
        if (r3 != DepthIntegrityKind.Ok || snap3 is null)
            throw new Exception("Update bid failed.");
        if (Math.Abs(snap3.Bids[0].Size - 20) > 0.000000001m)
            throw new Exception("Update size incorrect.");

        // delete ask0
        var r4 = buf.Apply(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Delete, OrderBookSide.Ask, 0, 0), out var snap4);
        if (r4 != DepthIntegrityKind.Ok || snap4 is null)
            throw new Exception("Delete ask failed.");
        if (snap4.Asks.Count != 0)
            throw new Exception("Ask delete did not apply.");
    }

    private static void Test_GapMarksStale()
    {
        var buf = new MarketDepthCollector.SymbolOrderBookBuffer(50);
        var ts = DateTimeOffset.UtcNow;
        var sym = "TEST";

        // update without insert -> out of order -> stale
        var r = buf.Apply(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Update, OrderBookSide.Bid, 100, 10), out var snap);
        if (r == DepthIntegrityKind.Ok)
            throw new Exception("Expected integrity failure.");
        if (!buf.IsStale)
            throw new Exception("Expected stale after integrity failure.");

        // further applies should return stale
        var r2 = buf.Apply(new MarketDepthUpdate(ts, sym, 0, DepthOperation.Insert, OrderBookSide.Bid, 100, 10), out _);
        if (r2 != DepthIntegrityKind.Stale)
            throw new Exception("Expected stale on subsequent apply.");
    }
}
