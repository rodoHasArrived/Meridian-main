using Meridian.Infrastructure.CppTrader.Protocol;

namespace Meridian.Infrastructure.CppTrader.Translation;

public sealed class CppTraderSnapshotTranslator : ICppTraderSnapshotTranslator
{
    public LOBSnapshot Translate(BookSnapshotEvent snapshotEvent)
    {
        ArgumentNullException.ThrowIfNull(snapshotEvent);

        var snapshot = snapshotEvent.Snapshot;
        var bids = snapshot.Bids
            .Select((level, index) => new OrderBookLevel(OrderBookSide.Bid, (ushort)index, level.Price, level.Size))
            .ToArray();
        var asks = snapshot.Asks
            .Select((level, index) => new OrderBookLevel(OrderBookSide.Ask, (ushort)index, level.Price, level.Size))
            .ToArray();

        return new LOBSnapshot(
            snapshot.Timestamp,
            snapshot.Symbol,
            bids,
            asks,
            snapshot.MidPrice,
            snapshot.MicroPrice,
            snapshot.Imbalance,
            MarketState.Normal,
            snapshot.SequenceNumber,
            StreamId: "CPPTRADER",
            Venue: snapshot.Venue);
    }
}
