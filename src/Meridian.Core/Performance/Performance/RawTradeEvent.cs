using System.Runtime.InteropServices;

namespace Meridian.Core.Performance;

/// <summary>
/// Zero-allocation struct representation of a trade event for the hot path.
/// Uses <see cref="LayoutKind.Sequential"/> for predictable memory layout in
/// the <see cref="SpscRingBuffer{T}"/> backing array.
/// </summary>
/// <remarks>
/// The symbol is stored as a pre-computed integer ID from <see cref="SymbolTable"/>
/// to avoid string references on the producer thread.
/// Consumers resolve the ID back to a symbol string via the same table.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct RawTradeEvent
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

    /// <summary>Execution price of the trade.</summary>
    public readonly decimal Price;

    /// <summary>Number of shares or contracts traded.</summary>
    public readonly long Size;

    /// <summary>
    /// Aggressor side encoded as the underlying byte value of
    /// <see cref="Meridian.Contracts.Domain.Enums.AggressorSide"/>:
    /// 0 = Unknown, 1 = Buy, 2 = Sell.
    /// </summary>
    public readonly byte Aggressor;

    /// <summary>Provider-assigned sequence number for ordering and gap detection.</summary>
    public readonly long Sequence;

    /// <summary>
    /// Creates a new <see cref="RawTradeEvent"/>.
    /// </summary>
    /// <param name="timestampTicks">Stopwatch timestamp ticks.</param>
    /// <param name="symbolHash">Symbol ID from <see cref="SymbolTable"/>.</param>
    /// <param name="price">Execution price (must be positive).</param>
    /// <param name="size">Trade size (must be non-negative).</param>
    /// <param name="aggressor">Aggressor side byte value.</param>
    /// <param name="sequence">Provider sequence number.</param>
    public RawTradeEvent(long timestampTicks, int symbolHash, decimal price, long size, byte aggressor, long sequence)
    {
        TimestampTicks = timestampTicks;
        SymbolHash = symbolHash;
        Price = price;
        Size = size;
        Aggressor = aggressor;
        Sequence = sequence;
    }
}
