using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Meridian.Core.Performance;

/// <summary>
/// Thread-safe symbol intern table that maps market symbol strings to compact integer IDs.
/// </summary>
/// <remarks>
/// The hot-path producer stores only the integer ID in <see cref="RawTradeEvent.SymbolHash"/>
/// and <see cref="RawQuoteEvent.SymbolHash"/> to avoid string references on the
/// critical publish path.  The consumer resolves the ID back to a symbol string for
/// serialization via <see cref="TryGetSymbol"/>.
/// <para>
/// IDs start at 1. ID 0 is reserved to represent an unregistered symbol.
/// A single global <see cref="SymbolTable"/> instance should be shared between
/// the producer and consumer so both sides see the same ID→symbol mapping.
/// </para>
/// </remarks>
public sealed class SymbolTable
{
    private readonly ConcurrentDictionary<string, int> _symbolToId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, string> _idToSymbol = new();
    private int _nextId;

    /// <summary>
    /// Returns the integer ID for <paramref name="symbol"/>, registering it if not already present.
    /// This call is allocation-free for symbols that have already been registered.
    /// </summary>
    /// <param name="symbol">Market symbol (e.g. "SPY").</param>
    /// <returns>Stable non-zero integer ID for the symbol.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetOrAdd(string symbol)
    {
        if (_symbolToId.TryGetValue(symbol, out var id))
        {
            _idToSymbol.TryAdd(id, symbol);
            return id;
        }

        return AddSlow(symbol);
    }

    /// <summary>
    /// Resolves an integer ID back to its symbol string.
    /// </summary>
    /// <param name="id">Symbol ID previously returned by <see cref="GetOrAdd"/>.</param>
    /// <returns>
    /// The symbol string, or <see langword="null"/> if <paramref name="id"/> was
    /// never registered in this table.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string? TryGetSymbol(int id)
    {
        _idToSymbol.TryGetValue(id, out var symbol);
        return symbol;
    }

    /// <summary>Gets the total number of symbols registered in this table.</summary>
    public int Count => _symbolToId.Count;

    // Slow path: first registration of a new symbol.
    // Atomic increment ensures no two threads ever receive the same ID.
    private int AddSlow(string symbol)
    {
        var candidate = Interlocked.Increment(ref _nextId);
        // GetOrAdd handles the race where two threads both miss the fast-path TryGetValue.
        // The "loser" thread's candidate ID is simply discarded (a minor gap in IDs, not a bug).
        var id = _symbolToId.GetOrAdd(symbol, candidate);
        _idToSymbol.TryAdd(id, symbol);
        return id;
    }
}
