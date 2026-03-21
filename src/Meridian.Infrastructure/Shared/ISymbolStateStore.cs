using System.Collections.Concurrent;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Shared;

/// <summary>
/// Generic interface for thread-safe symbol-keyed state storage.
/// Abstracts the 35+ ConcurrentDictionary usages across the codebase
/// into a reusable pattern with consistent semantics.
/// </summary>
/// <typeparam name="T">Type of state to store per symbol.</typeparam>
/// <remarks>
/// Addresses scattered concurrent collection usage by providing:
/// - Consistent thread-safe access patterns
/// - Built-in staleness detection and cleanup
/// - Snapshot capabilities for iteration
/// - Bulk operations for efficiency
/// - Observable state changes
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized symbol state management")]
public interface ISymbolStateStore<T> : IDisposable where T : class
{
    /// <summary>
    /// Gets the number of symbols with state.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets or creates state for a symbol.
    /// </summary>
    /// <param name="symbol">Symbol key.</param>
    /// <param name="factory">Factory to create state if not exists.</param>
    /// <returns>Existing or newly created state.</returns>
    T GetOrAdd(string symbol, Func<string, T> factory);

    /// <summary>
    /// Gets state for a symbol if it exists.
    /// </summary>
    /// <param name="symbol">Symbol key.</param>
    /// <param name="state">State if found.</param>
    /// <returns>True if state exists.</returns>
    bool TryGet(string symbol, out T? state);

    /// <summary>
    /// Updates state for a symbol.
    /// </summary>
    /// <param name="symbol">Symbol key.</param>
    /// <param name="state">New state.</param>
    void Set(string symbol, T state);

    /// <summary>
    /// Updates state with a transformation function.
    /// </summary>
    /// <param name="symbol">Symbol key.</param>
    /// <param name="addFactory">Function to create state if not present.</param>
    /// <param name="updateFactory">Function to update state.</param>
    /// <returns>Updated state.</returns>
    T AddOrUpdate(string symbol, Func<string, T> addFactory, Func<string, T, T> updateFactory);

    /// <summary>
    /// Removes state for a symbol.
    /// </summary>
    /// <param name="symbol">Symbol key.</param>
    /// <returns>True if removed.</returns>
    bool Remove(string symbol);

    /// <summary>
    /// Removes state for a symbol and returns it.
    /// </summary>
    /// <param name="symbol">Symbol key.</param>
    /// <param name="state">Removed state if found.</param>
    /// <returns>True if removed.</returns>
    bool TryRemove(string symbol, out T? state);

    /// <summary>
    /// Checks if state exists for a symbol.
    /// </summary>
    /// <param name="symbol">Symbol key.</param>
    /// <returns>True if state exists.</returns>
    bool Contains(string symbol);

    /// <summary>
    /// Gets all symbols with state.
    /// </summary>
    /// <returns>List of symbols.</returns>
    IReadOnlyList<string> GetSymbols();

    /// <summary>
    /// Gets a snapshot of all state (thread-safe copy).
    /// </summary>
    /// <returns>Dictionary snapshot.</returns>
    IReadOnlyDictionary<string, T> GetSnapshot();

    /// <summary>
    /// Clears all state.
    /// </summary>
    void Clear();

    /// <summary>
    /// Applies an action to all states.
    /// </summary>
    /// <param name="action">Action to apply.</param>
    void ForEach(Action<string, T> action);

    /// <summary>
    /// Removes stale entries based on a predicate.
    /// </summary>
    /// <param name="isStale">Predicate to determine staleness.</param>
    /// <returns>Number of entries removed.</returns>
    int RemoveStale(Func<string, T, bool> isStale);
}

/// <summary>
/// Default implementation of ISymbolStateStore using ConcurrentDictionary.
/// </summary>
/// <typeparam name="T">Type of state to store.</typeparam>
[ImplementsAdr("ADR-001", "ConcurrentDictionary-based symbol state store")]
public sealed class SymbolStateStore<T> : ISymbolStateStore<T> where T : class
{
    private readonly ConcurrentDictionary<string, T> _store;
    private readonly StringComparer _comparer;
    private bool _disposed;

    /// <summary>
    /// Creates a new symbol state store.
    /// </summary>
    /// <param name="ignoreCase">Whether to ignore case in symbol keys (default: true).</param>
    public SymbolStateStore(bool ignoreCase = true)
    {
        _comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _store = new ConcurrentDictionary<string, T>(_comparer);
    }

    /// <inheritdoc/>
    public int Count => _store.Count;

    /// <inheritdoc/>
    public T GetOrAdd(string symbol, Func<string, T> factory)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(factory);
        return _store.GetOrAdd(symbol, factory);
    }

    /// <inheritdoc/>
    public bool TryGet(string symbol, out T? state)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(symbol))
        {
            state = null;
            return false;
        }
        return _store.TryGetValue(symbol, out state);
    }

    /// <inheritdoc/>
    public void Set(string symbol, T state)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(state);
        _store[symbol] = state;
    }

    /// <inheritdoc/>
    public T AddOrUpdate(string symbol, Func<string, T> addFactory, Func<string, T, T> updateFactory)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(symbol);
        return _store.AddOrUpdate(symbol, addFactory, updateFactory);
    }

    /// <inheritdoc/>
    public bool Remove(string symbol)
    {
        ThrowIfDisposed();
        return _store.TryRemove(symbol, out _);
    }

    /// <inheritdoc/>
    public bool TryRemove(string symbol, out T? state)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(symbol))
        {
            state = null;
            return false;
        }
        return _store.TryRemove(symbol, out state);
    }

    /// <inheritdoc/>
    public bool Contains(string symbol)
    {
        ThrowIfDisposed();
        return !string.IsNullOrEmpty(symbol) && _store.ContainsKey(symbol);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetSymbols()
    {
        ThrowIfDisposed();
        return _store.Keys.ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, T> GetSnapshot()
    {
        ThrowIfDisposed();
        return new Dictionary<string, T>(_store, _comparer);
    }

    /// <inheritdoc/>
    public void Clear()
    {
        ThrowIfDisposed();
        _store.Clear();
    }

    /// <inheritdoc/>
    public void ForEach(Action<string, T> action)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(action);

        foreach (var kvp in _store)
        {
            action(kvp.Key, kvp.Value);
        }
    }

    /// <inheritdoc/>
    public int RemoveStale(Func<string, T, bool> isStale)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(isStale);

        var staleKeys = _store
            .Where(kvp => isStale(kvp.Key, kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        var removed = 0;
        foreach (var key in staleKeys)
        {
            if (_store.TryRemove(key, out _))
                removed++;
        }

        return removed;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Dispose any disposable state
        foreach (var value in _store.Values)
        {
            if (value is IDisposable disposable)
            {
                try
                { disposable.Dispose(); }
                catch (ObjectDisposedException) { /* already disposed */ }
            }
        }

        _store.Clear();
    }
}

/// <summary>
/// Symbol state store with automatic expiration based on last access time.
/// </summary>
/// <typeparam name="T">Type of state to store.</typeparam>
[ImplementsAdr("ADR-001", "Time-based expiring symbol state store")]
public sealed class ExpiringSymbolStateStore<T> : ISymbolStateStore<T> where T : class
{
    private readonly SymbolStateStore<TimestampedState<T>> _inner;
    private readonly TimeSpan _expirationTime;
    private readonly Timer? _cleanupTimer;
    private bool _disposed;

    /// <summary>
    /// Creates a new expiring symbol state store.
    /// </summary>
    /// <param name="expirationTime">Time after which state expires.</param>
    /// <param name="cleanupInterval">Interval for automatic cleanup (null = no auto cleanup).</param>
    /// <param name="ignoreCase">Whether to ignore case in symbol keys.</param>
    public ExpiringSymbolStateStore(
        TimeSpan expirationTime,
        TimeSpan? cleanupInterval = null,
        bool ignoreCase = true)
    {
        _inner = new SymbolStateStore<TimestampedState<T>>(ignoreCase);
        _expirationTime = expirationTime;

        if (cleanupInterval.HasValue)
        {
            _cleanupTimer = new Timer(
                _ => RemoveExpired(),
                null,
                cleanupInterval.Value,
                cleanupInterval.Value);
        }
    }

    /// <inheritdoc/>
    public int Count => _inner.Count;

    /// <inheritdoc/>
    public T GetOrAdd(string symbol, Func<string, T> factory)
    {
        var timestamped = _inner.GetOrAdd(symbol, s => new TimestampedState<T>(factory(s)));
        timestamped.Touch();
        return timestamped.Value;
    }

    /// <inheritdoc/>
    public bool TryGet(string symbol, out T? state)
    {
        if (_inner.TryGet(symbol, out var timestamped) && timestamped != null)
        {
            if (!IsExpired(timestamped))
            {
                timestamped.Touch();
                state = timestamped.Value;
                return true;
            }
            _inner.Remove(symbol);
        }

        state = null;
        return false;
    }

    /// <inheritdoc/>
    public void Set(string symbol, T state)
    {
        _inner.Set(symbol, new TimestampedState<T>(state));
    }

    /// <inheritdoc/>
    public T AddOrUpdate(string symbol, Func<string, T> addFactory, Func<string, T, T> updateFactory)
    {
        var result = _inner.AddOrUpdate(
            symbol,
            s => new TimestampedState<T>(addFactory(s)),
            (s, existing) =>
            {
                existing.Touch();
                return new TimestampedState<T>(updateFactory(s, existing.Value));
            });
        return result.Value;
    }

    /// <inheritdoc/>
    public bool Remove(string symbol) => _inner.Remove(symbol);

    /// <inheritdoc/>
    public bool TryRemove(string symbol, out T? state)
    {
        if (_inner.TryRemove(symbol, out var timestamped) && timestamped != null)
        {
            state = timestamped.Value;
            return true;
        }
        state = null;
        return false;
    }

    /// <inheritdoc/>
    public bool Contains(string symbol)
    {
        if (_inner.TryGet(symbol, out var timestamped) && timestamped != null)
        {
            return !IsExpired(timestamped);
        }
        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetSymbols() => _inner.GetSymbols();

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, T> GetSnapshot()
    {
        return _inner.GetSnapshot()
            .Where(kvp => !IsExpired(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Value);
    }

    /// <inheritdoc/>
    public void Clear() => _inner.Clear();

    /// <inheritdoc/>
    public void ForEach(Action<string, T> action)
    {
        _inner.ForEach((symbol, timestamped) =>
        {
            if (!IsExpired(timestamped))
            {
                action(symbol, timestamped.Value);
            }
        });
    }

    /// <inheritdoc/>
    public int RemoveStale(Func<string, T, bool> isStale)
    {
        return _inner.RemoveStale((symbol, timestamped) =>
            IsExpired(timestamped) || isStale(symbol, timestamped.Value));
    }

    /// <summary>
    /// Removes all expired entries.
    /// </summary>
    /// <returns>Number of entries removed.</returns>
    public int RemoveExpired()
    {
        return _inner.RemoveStale((_, timestamped) => IsExpired(timestamped));
    }

    private bool IsExpired(TimestampedState<T> state)
    {
        return DateTimeOffset.UtcNow - state.LastAccessedAt > _expirationTime;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _cleanupTimer?.Dispose();
        _inner.Dispose();
    }
}

/// <summary>
/// State wrapper with timestamp tracking.
/// </summary>
internal sealed class TimestampedState<T>
{
    public T Value { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastAccessedAt { get; private set; }

    public TimestampedState(T value)
    {
        Value = value;
        CreatedAt = DateTimeOffset.UtcNow;
        LastAccessedAt = CreatedAt;
    }

    public void Touch()
    {
        LastAccessedAt = DateTimeOffset.UtcNow;
    }
}
