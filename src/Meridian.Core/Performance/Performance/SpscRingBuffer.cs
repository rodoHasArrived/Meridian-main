using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meridian.Core.Performance;

// Each padded long occupies exactly 128 bytes (two typical 64-byte cache lines),
// ensuring head and tail never share a cache line regardless of surrounding allocations.
// Must be a non-nested type because the CLR forbids explicit-layout structs inside generic types.
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct PaddedLong
{
    [FieldOffset(64)]
    public long Value;
}

/// <summary>
/// Lock-free, single-producer/single-consumer ring buffer backed by a pre-allocated array.
/// </summary>
/// <remarks>
/// <para>
/// <b>No allocation occurs on <see cref="TryWrite"/> or <see cref="TryRead"/> calls</b> —
/// the backing array is allocated once in the constructor. This makes the buffer suitable
/// for the zero-allocation hot path that handles high-frequency trade and quote events.
/// </para>
/// <para>
/// The buffer capacity is rounded up to the nearest power of two so index masking
/// (<c>index &amp; _mask</c>) replaces the modulo operation on every slot access.
/// </para>
/// <para>
/// <b>Thread-safety contract</b>: exactly one producer thread may call
/// <see cref="TryWrite"/>, and exactly one consumer thread may call
/// <see cref="TryRead"/> / <see cref="DrainTo"/>. Calling either method
/// from more than one thread simultaneously violates this contract.
/// </para>
/// <para>
/// Head and tail counters are placed in separate cache-line-padded structs to
/// prevent false sharing between the producer and consumer CPU cores.
/// </para>
/// </remarks>
/// <typeparam name="T">Value type stored in the ring buffer. Must be a struct.</typeparam>
public sealed class SpscRingBuffer<T> where T : struct
{
    private readonly T[] _buffer;
    private readonly int _mask;

    // Written by the producer, read by the consumer.
    private PaddedLong _head;

    // Written by the consumer, read by the producer.
    private PaddedLong _tail;

    /// <summary>
    /// Creates a new ring buffer with a capacity rounded up to the next power of two.
    /// </summary>
    /// <param name="capacity">Desired minimum capacity. Must be ≥ 2.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="capacity"/> is less than 2.
    /// </exception>
    public SpscRingBuffer(int capacity)
    {
        if (capacity < 2)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 2.");

        var size = NextPowerOfTwo(capacity);
        _buffer = new T[size];
        _mask = size - 1;
    }

    /// <summary>
    /// Gets the actual buffer capacity (the next power of two ≥ the requested capacity).
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Gets an approximate count of items currently available to read.
    /// The value may be stale by the time the caller acts on it.
    /// </summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)(Volatile.Read(ref _head.Value) - Volatile.Read(ref _tail.Value));
    }

    /// <summary>Gets whether the buffer currently has no items to read.</summary>
    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref _head.Value) == Volatile.Read(ref _tail.Value);
    }

    /// <summary>Gets whether the buffer is currently full.</summary>
    public bool IsFull
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Count >= _buffer.Length;
    }

    /// <summary>
    /// Tries to write <paramref name="item"/> into the buffer.
    /// </summary>
    /// <remarks>
    /// Must be called from the <b>producer</b> thread only.
    /// This method never allocates and performs no locking.
    /// </remarks>
    /// <param name="item">Item to write.</param>
    /// <returns>
    /// <see langword="true"/> when the item was written;
    /// <see langword="false"/> when the buffer is full.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWrite(in T item)
    {
        var head = Volatile.Read(ref _head.Value);
        var tail = Volatile.Read(ref _tail.Value);

        if (head - tail >= _buffer.Length)
            return false; // Buffer full

        _buffer[(int)(head & _mask)] = item;
        Volatile.Write(ref _head.Value, head + 1);
        return true;
    }

    /// <summary>
    /// Tries to read the next item from the buffer.
    /// </summary>
    /// <remarks>
    /// Must be called from the <b>consumer</b> thread only.
    /// This method never allocates and performs no locking.
    /// </remarks>
    /// <param name="item">
    /// Set to the dequeued item on success; set to <see langword="default"/> on failure.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when an item was dequeued;
    /// <see langword="false"/> when the buffer is empty.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead(out T item)
    {
        var tail = Volatile.Read(ref _tail.Value);
        var head = Volatile.Read(ref _head.Value);

        if (head == tail)
        {
            item = default;
            return false; // Buffer empty
        }

        item = _buffer[tail & _mask];
        Volatile.Write(ref _tail.Value, tail + 1);
        return true;
    }

    /// <summary>
    /// Drains up to <paramref name="maxItems"/> items into <paramref name="destination"/>.
    /// </summary>
    /// <remarks>Must be called from the <b>consumer</b> thread only.</remarks>
    /// <param name="destination">Span to write items into. Capacity must be ≥ <paramref name="maxItems"/>.</param>
    /// <param name="maxItems">Maximum items to drain. Defaults to the full destination length.</param>
    /// <returns>The number of items written to <paramref name="destination"/>.</returns>
    public int DrainTo(Span<T> destination, int maxItems = int.MaxValue)
    {
        var count = 0;
        var limit = Math.Min(destination.Length, maxItems);
        while (count < limit && TryRead(out destination[count]))
            count++;
        return count;
    }

    // Rounds v up to the next power of two (v ≥ 2 guaranteed by constructor).
    private static int NextPowerOfTwo(int v)
    {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}
