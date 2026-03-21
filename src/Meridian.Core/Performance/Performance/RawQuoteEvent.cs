using System.Runtime.InteropServices;

namespace Meridian.Core.Performance;

/// <summary>
/// Zero-allocation struct representation of a Best-Bid/Offer quote event for the hot path.
/// Uses <see cref="LayoutKind.Sequential"/> for predictable memory layout in
/// the <see cref="SpscRingBuffer{T}"/> backing array.
/// </summary>
/// <remarks>
/// The symbol is stored as a pre-computed integer ID from <see cref="SymbolTable"/>
/// to avoid string references on the producer thread.
/// Mid-price and spread are intentionally omitted — they are derived fields that the
/// consumer can recompute from bid/ask without extra storage.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct RawQuoteEvent
{
    /// <summary>
    /// Timestamp as UTC ticks (100-nanosecond intervals since 0001-01-01 UTC,
    /// i.e. <see cref="DateTimeOffset.UtcTicks"/>).
    /// Reconstruct the wall-clock time with
    /// <c>new DateTimeOffset(TimestampTicks, TimeSpan.Zero)</c>.
    /// </summary>
    public readonly long TimestampTicks;

    /// <summary>
    /// Pre-computed symbol identifier assigned by <see cref="SymbolTable.GetOrAdd"/>.
    /// Resolve back to the symbol string with <see cref="SymbolTable.TryGetSymbol"/>.
    /// </summary>
    public readonly int SymbolHash;

    /// <summary>Best bid price.</summary>
    public readonly decimal BidPrice;

    /// <summary>Best bid size (shares or contracts).</summary>
    public readonly long BidSize;

    /// <summary>Best ask price.</summary>
    public readonly decimal AskPrice;

    /// <summary>Best ask size (shares or contracts).</summary>
    public readonly long AskSize;

    /// <summary>Provider-assigned sequence number for ordering and gap detection.</summary>
    public readonly long Sequence;

    /// <summary>
    /// Creates a new <see cref="RawQuoteEvent"/>.
    /// </summary>
    /// <param name="timestampTicks">Stopwatch timestamp ticks.</param>
    /// <param name="symbolHash">Symbol ID from <see cref="SymbolTable"/>.</param>
    /// <param name="bidPrice">Best bid price.</param>
    /// <param name="bidSize">Best bid size.</param>
    /// <param name="askPrice">Best ask price.</param>
    /// <param name="askSize">Best ask size.</param>
    /// <param name="sequence">Provider sequence number.</param>
    public RawQuoteEvent(long timestampTicks, int symbolHash, decimal bidPrice, long bidSize, decimal askPrice, long askSize, long sequence)
    {
        TimestampTicks = timestampTicks;
        SymbolHash = symbolHash;
        BidPrice = bidPrice;
        BidSize = bidSize;
        AskPrice = askPrice;
        AskSize = askSize;
        Sequence = sequence;
    }
}
