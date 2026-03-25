using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Meridian.Ui.Services.Collections;

/// <summary>
/// A fixed-size circular buffer (ring buffer) that provides O(1) operations
/// for adding elements and O(n) operations for computing statistics. When the buffer is full,
/// new elements overwrite the oldest elements.
/// </summary>
/// <typeparam name="T">The type of elements in the buffer.</typeparam>
public sealed class CircularBuffer<T> : IEnumerable<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    /// <summary>
    /// Initializes a new circular buffer with the specified capacity.
    /// </summary>
    /// <param name="capacity">The maximum number of elements the buffer can hold.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when capacity is less than 1.</exception>
    public CircularBuffer(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        }

        _buffer = new T[capacity];
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Gets the maximum capacity of the buffer.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Gets the current number of elements in the buffer.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets a value indicating whether the buffer is empty.
    /// </summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// Gets a value indicating whether the buffer is full.
    /// </summary>
    public bool IsFull => _count == _buffer.Length;

    /// <summary>
    /// Adds an element to the buffer. If the buffer is full,
    /// the oldest element is overwritten.
    /// </summary>
    /// <param name="item">The element to add.</param>
    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
        {
            _count++;
        }
    }

    /// <summary>
    /// Gets the element at the specified index, where index 0 is the oldest
    /// element and index Count-1 is the newest element.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>The element at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            // Calculate actual index in the circular buffer
            var actualIndex = (_head - _count + index + _buffer.Length) % _buffer.Length;
            return _buffer[actualIndex];
        }
    }

    /// <summary>
    /// Gets the most recently added element.
    /// </summary>
    /// <returns>The newest element.</returns>
    /// <exception cref="InvalidOperationException">Thrown when buffer is empty.</exception>
    public T GetNewest()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("Buffer is empty.");
        }

        var index = (_head - 1 + _buffer.Length) % _buffer.Length;
        return _buffer[index];
    }

    /// <summary>
    /// Gets the oldest element in the buffer.
    /// </summary>
    /// <returns>The oldest element.</returns>
    /// <exception cref="InvalidOperationException">Thrown when buffer is empty.</exception>
    public T GetOldest()
    {
        if (_count == 0)
        {
            throw new InvalidOperationException("Buffer is empty.");
        }

        var index = (_head - _count + _buffer.Length) % _buffer.Length;
        return _buffer[index];
    }

    /// <summary>
    /// Tries to get the most recently added element.
    /// </summary>
    /// <param name="value">When this method returns, contains the newest element if successful.</param>
    /// <returns>True if the buffer is not empty; otherwise, false.</returns>
    public bool TryGetNewest(out T? value)
    {
        if (_count == 0)
        {
            value = default;
            return false;
        }

        var index = (_head - 1 + _buffer.Length) % _buffer.Length;
        value = _buffer[index];
        return true;
    }

    /// <summary>
    /// Tries to get the element at the specified offset from the newest element.
    /// Offset 0 returns the newest, offset 1 returns the second newest, etc.
    /// </summary>
    /// <param name="offsetFromNewest">Offset from the newest element (0 = newest).</param>
    /// <param name="value">When this method returns, contains the element if successful; otherwise, the default value for the type.</param>
    /// <returns>True if the element exists at the specified offset; otherwise, false.</returns>
    public bool TryGetFromNewest(int offsetFromNewest, [MaybeNullWhen(false)] out T value)
    {
        if (offsetFromNewest < 0 || offsetFromNewest >= _count)
        {
            value = default!;
            return false;
        }

        var index = (_head - 1 - offsetFromNewest + _buffer.Length) % _buffer.Length;
        value = _buffer[index];
        return true;
    }

    /// <summary>
    /// Clears all elements from the buffer.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Copies all elements to an array, ordered from oldest to newest.
    /// </summary>
    /// <returns>An array containing all elements.</returns>
    public T[] ToArray()
    {
        var result = new T[_count];
        for (var i = 0; i < _count; i++)
        {
            result[i] = this[i];
        }
        return result;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the buffer from oldest to newest.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Extension methods for CircularBuffer with numeric types.
/// </summary>
public static class CircularBufferExtensions
{
    /// <summary>
    /// Calculates the sum of all elements in the buffer.
    /// </summary>
    public static double Sum(this CircularBuffer<double> buffer)
    {
        var sum = 0.0;
        for (var i = 0; i < buffer.Count; i++)
        {
            sum += buffer[i];
        }
        return sum;
    }

    /// <summary>
    /// Calculates the average of all elements in the buffer.
    /// </summary>
    public static double Average(this CircularBuffer<double> buffer)
    {
        if (buffer.Count == 0)
            return 0.0;
        return buffer.Sum() / buffer.Count;
    }

    /// <summary>
    /// Finds the maximum value in the buffer.
    /// </summary>
    public static double Max(this CircularBuffer<double> buffer)
    {
        if (buffer.Count == 0)
            return 0.0;

        var max = double.MinValue;
        for (var i = 0; i < buffer.Count; i++)
        {
            if (buffer[i] > max)
                max = buffer[i];
        }
        return max;
    }

    /// <summary>
    /// Finds the minimum value in the buffer.
    /// </summary>
    public static double Min(this CircularBuffer<double> buffer)
    {
        if (buffer.Count == 0)
            return 0.0;

        var min = double.MaxValue;
        for (var i = 0; i < buffer.Count; i++)
        {
            if (buffer[i] < min)
                min = buffer[i];
        }
        return min;
    }

    /// <summary>
    /// Calculates the rate of change (delta) between the two most recent values.
    /// </summary>
    /// <param name="buffer">The buffer to calculate rate for.</param>
    /// <param name="intervalSeconds">The time interval between samples in seconds.</param>
    /// <returns>The rate of change per second, or 0 if insufficient data.</returns>
    public static double CalculateRate(this CircularBuffer<double> buffer, double intervalSeconds = 1.0)
    {
        if (buffer.Count < 2 || intervalSeconds <= 0)
            return 0.0;

        var newest = buffer[buffer.Count - 1];
        var previous = buffer[buffer.Count - 2];
        return (newest - previous) / intervalSeconds;
    }

    /// <summary>
    /// Calculates the percentage change between two values at specified offsets from the newest element.
    /// </summary>
    /// <param name="buffer">The buffer to calculate percentage change for.</param>
    /// <param name="fromOffset">Offset of the base value from newest (1 = second newest).</param>
    /// <param name="toOffset">Offset of the comparison value from newest (0 = newest).</param>
    /// <returns>The percentage change, or null if division by zero or insufficient data.</returns>
    /// <remarks>
    /// Formula: ((toValue - fromValue) / fromValue) * 100
    /// Returns null if fromValue is zero to avoid division by zero.
    /// </remarks>
    public static double? CalculatePercentageChange(this CircularBuffer<double> buffer, int fromOffset, int toOffset)
    {
        // Using standard Try pattern with out var - no nullable inference issues
        if (!buffer.TryGetFromNewest(fromOffset, out var fromValue) ||
            !buffer.TryGetFromNewest(toOffset, out var toValue))
        {
            return null;
        }

        // Avoid division by zero
        if (Math.Abs(fromValue) < double.Epsilon)
        {
            return null;
        }

        return ((toValue - fromValue) / fromValue) * 100.0;
    }
}
