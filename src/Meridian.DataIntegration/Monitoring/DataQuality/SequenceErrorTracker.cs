using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Core.Logging;
using Serilog;

namespace Meridian.DataIntegration.Monitoring.DataQuality;

/// <summary>
/// Tracks and reports sequence errors in market data streams.
/// Detects gaps, out-of-order events, duplicates, and sequence resets.
/// </summary>
public sealed class SequenceErrorTracker : IDisposable
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StateInactivityWindow = TimeSpan.FromHours(6);

    private readonly ILogger _log = LoggingSetup.ForContext<SequenceErrorTracker>();
    private readonly ConcurrentDictionary<SequenceStreamKey, SymbolSequenceState> _symbolStates = new();
    private readonly ConcurrentDictionary<SequenceStreamKey, SequenceErrorBuffer> _errors = new();
    private readonly SequenceErrorConfig _config;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _cleanupTimer;
    private readonly SequenceErrorTrackerTestHooks? _testHooks;
    private readonly object _lifecycleSync = new();
    // Callback reentrancy is stack/thread-local; it must not flow into unrelated child tasks.
    private readonly ThreadLocal<int> _operationDepth = new();
    private int _activeOperations;
    private bool _disposeRequested;
    private bool _timerStopped;
    private bool _disposeCompleted;

    // Global counters
    private long _totalEventsChecked;
    private long _totalGapErrors;
    private long _totalOutOfOrderErrors;
    private long _totalDuplicateErrors;
    private long _totalResetErrors;

    /// <summary>
    /// Event raised when a sequence error is detected.
    /// </summary>
    public event Action<SequenceError>? OnSequenceError;

    public SequenceErrorTracker(SequenceErrorConfig? config = null)
        : this(config, TimeProvider.System, testHooks: null)
    {
    }

    internal SequenceErrorTracker(
        SequenceErrorConfig? config,
        TimeProvider timeProvider,
        SequenceErrorTrackerTestHooks? testHooks = null)
    {
        _config = ValidateConfig(config ?? SequenceErrorConfig.Default);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _testHooks = testHooks;
        _cleanupTimer = _timeProvider.CreateTimer(
            static state => ((SequenceErrorTracker)state!).RunCleanup(),
            this,
            CleanupInterval,
            CleanupInterval);

        _log.Information("SequenceErrorTracker initialized with gap threshold: {GapThreshold}",
            _config.GapThreshold);
    }

    /// <summary>
    /// Checks a sequence number for errors.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SequenceError? CheckSequence(
        string symbol,
        string eventType,
        long sequenceNumber,
        DateTimeOffset timestamp,
        string? streamId = null,
        string? provider = null)
    {
        if (!TryEnterOperation())
            return null;

        try
        {
            var key = GetKey(symbol, eventType, streamId, provider);
            ArgumentOutOfRangeException.ThrowIfNegative(sequenceNumber);
            var observedAt = _timeProvider.GetUtcNow();
            Interlocked.Increment(ref _totalEventsChecked);

            SequenceError? error;
            while (true)
            {
                var state = _symbolStates.GetOrAdd(
                    key,
                    _ => new SymbolSequenceState(
                        symbol.Trim(),
                        eventType.Trim(),
                        streamId,
                        observedAt));
                if (state.TryCheckSequence(
                        sequenceNumber,
                        timestamp,
                        observedAt,
                        provider,
                        _config,
                        out error))
                {
                    break;
                }

                TryRemoveExact(_symbolStates, key, state);
            }

            if (error != null)
            {
                RecordErrorCore(error);
            }

            return error;
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Records a sequence error directly (for use with existing integrity events).
    /// </summary>
    public void RecordError(SequenceError error)
    {
        if (!TryEnterOperation())
            return;

        try
        {
            ArgumentNullException.ThrowIfNull(error);
            RecordErrorCore(error);
        }
        finally
        {
            ExitOperation();
        }
    }

    private void RecordErrorCore(SequenceError error)
    {
        var key = GetKey(error.Symbol, error.EventType, error.StreamId, error.Provider);
        switch (error.ErrorType)
        {
            case SequenceErrorType.Gap:
                Interlocked.Increment(ref _totalGapErrors);
                break;
            case SequenceErrorType.OutOfOrder:
                Interlocked.Increment(ref _totalOutOfOrderErrors);
                break;
            case SequenceErrorType.Duplicate:
                Interlocked.Increment(ref _totalDuplicateErrors);
                break;
            case SequenceErrorType.Reset:
                Interlocked.Increment(ref _totalResetErrors);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(error),
                    error.ErrorType,
                    "Sequence error type is not supported.");
        }

        if (_config.MaxErrorsPerSymbol > 0)
        {
            while (true)
            {
                var errorBuffer = _errors.GetOrAdd(key, static _ => new SequenceErrorBuffer());
                if (errorBuffer.TryAdd(error, _config.MaxErrorsPerSymbol))
                    break;

                TryRemoveExact(_errors, key, errorBuffer);
            }
        }

        // Log significant errors
        if (error.GapSize > _config.SignificantGapSize || error.ErrorType == SequenceErrorType.Reset)
        {
            _log.Warning("Sequence error detected: {Symbol}:{EventType} - Type: {ErrorType}, " +
                "Expected: {Expected}, Actual: {Actual}, Gap: {Gap}",
                error.Symbol, error.EventType, error.ErrorType,
                error.ExpectedSequence, error.ActualSequence, error.GapSize);
        }

        try
        {
            OnSequenceError?.Invoke(error);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in sequence error event handler");
        }
    }

    /// <summary>
    /// Gets the error summary for a symbol.
    /// </summary>
    public SequenceErrorSummary GetSummary(string symbol, DateOnly? date = null)
    {
        var symbolUpper = NormalizeRequiredIdentity(symbol, nameof(symbol));
        var allErrors = SnapshotErrors()
            .Where(e => e.Symbol.Equals(symbolUpper, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (date.HasValue)
        {
            allErrors = allErrors
                .Where(e => DateOnly.FromDateTime(e.Timestamp.UtcDateTime) == date.Value)
                .ToList();
        }

        var totalEvents = _symbolStates.Values
            .Where(s => s.Symbol.Equals(symbolUpper, StringComparison.OrdinalIgnoreCase))
            .Sum(s => s.TotalEvents);

        var errorRate = totalEvents > 0 ? (double)allErrors.Count / totalEvents * 100 : 0;

        return new SequenceErrorSummary(
            Symbol: symbol,
            Date: date ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
            TotalErrors: allErrors.Count,
            GapErrors: allErrors.Count(e => e.ErrorType == SequenceErrorType.Gap),
            OutOfOrderErrors: allErrors.Count(e => e.ErrorType == SequenceErrorType.OutOfOrder),
            DuplicateErrors: allErrors.Count(e => e.ErrorType == SequenceErrorType.Duplicate),
            ResetErrors: allErrors.Count(e => e.ErrorType == SequenceErrorType.Reset),
            ErrorRate: Math.Round(errorRate, 4),
            RecentErrors: allErrors.OrderByDescending(e => e.Timestamp).Take(20).ToList()
        );
    }

    /// <summary>
    /// Gets all errors for a specific date.
    /// </summary>
    public IReadOnlyList<SequenceError> GetErrorsForDate(DateOnly date)
    {
        return SnapshotErrors()
            .Where(e => DateOnly.FromDateTime(e.Timestamp.UtcDateTime) == date)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Gets recent errors across all symbols.
    /// </summary>
    public IReadOnlyList<SequenceError> GetRecentErrors(int count = 100)
    {
        return SnapshotErrors()
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets errors for a specific symbol and event type.
    /// </summary>
    public IReadOnlyList<SequenceError> GetErrors(string symbol, string? eventType = null, int count = 100)
    {
        var symbolUpper = NormalizeRequiredIdentity(symbol, nameof(symbol));
        var eventTypeUpper = eventType is null
            ? null
            : NormalizeRequiredIdentity(eventType, nameof(eventType));
        return SnapshotErrors()
            .Where(e => e.Symbol.Equals(symbolUpper, StringComparison.OrdinalIgnoreCase) &&
                        (eventTypeUpper == null ||
                         e.EventType.Equals(eventTypeUpper, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets symbols with the most errors.
    /// </summary>
    public IReadOnlyList<(string Symbol, int ErrorCount)> GetSymbolsWithMostErrors(int count = 10)
    {
        return SnapshotErrors()
            .GroupBy(e => e.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Symbol: g.Key, ErrorCount: g.Count()))
            .OrderByDescending(x => x.ErrorCount)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets overall statistics.
    /// </summary>
    public SequenceErrorStatistics GetStatistics()
    {
        var allErrors = SnapshotErrors();
        var totalChecked = Interlocked.Read(ref _totalEventsChecked);

        var errorsByType = new Dictionary<SequenceErrorType, long>
        {
            [SequenceErrorType.Gap] = Interlocked.Read(ref _totalGapErrors),
            [SequenceErrorType.OutOfOrder] = Interlocked.Read(ref _totalOutOfOrderErrors),
            [SequenceErrorType.Duplicate] = Interlocked.Read(ref _totalDuplicateErrors),
            [SequenceErrorType.Reset] = Interlocked.Read(ref _totalResetErrors)
        };
        var lifetimeTotalErrors = errorsByType.Values.Sum();

        var avgGapSize = allErrors.Count > 0
            ? allErrors.Average(e => e.GapSize)
            : 0;

        var maxGapSize = allErrors.Count > 0
            ? allErrors.Max(e => e.GapSize)
            : 0;

        return new SequenceErrorStatistics(
            TotalEventsChecked: totalChecked,
            TotalErrors: allErrors.Count,
            ErrorRate: totalChecked > 0 ? (double)allErrors.Count / totalChecked * 100 : 0,
            ErrorsByType: errorsByType,
            SymbolsWithErrors: allErrors.Select(e => e.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            AverageGapSize: avgGapSize,
            MaxGapSize: maxGapSize,
            CalculatedAt: _timeProvider.GetUtcNow())
        {
            LifetimeTotalErrors = lifetimeTotalErrors,
            LifetimeErrorRate = totalChecked > 0
                ? (double)lifetimeTotalErrors / totalChecked * 100
                : 0
        };
    }

    /// <summary>
    /// Resets the sequence state for a symbol (use after intentional reconnection).
    /// </summary>
    public void ResetSymbolState(string symbol, string? eventType = null, string? streamId = null)
        => ResetSymbolStateCore(symbol, eventType, streamId, provider: null, providerScoped: false);

    /// <summary>
    /// Resets sequence state for one provider while preserving matching streams for other providers.
    /// </summary>
    public void ResetSymbolState(
        string symbol,
        string? eventType,
        string? streamId,
        string provider)
        => ResetSymbolStateCore(symbol, eventType, streamId, provider, providerScoped: true);

    private void ResetSymbolStateCore(
        string symbol,
        string? eventType,
        string? streamId,
        string? provider,
        bool providerScoped)
    {
        if (!TryEnterOperation())
            return;

        try
        {
            var normalizedSymbol = NormalizeRequiredIdentity(symbol, nameof(symbol));
            var normalizedEventType = eventType is null
                ? null
                : NormalizeRequiredIdentity(eventType, nameof(eventType));
            var normalizedProvider = providerScoped
                ? NormalizeRequiredIdentity(provider!, nameof(provider))
                : null;
            var statesToReset = _symbolStates
                .Where(kvp => kvp.Key.Symbol.Equals(normalizedSymbol, StringComparison.Ordinal) &&
                              (normalizedEventType is null ||
                               kvp.Key.EventType.Equals(normalizedEventType, StringComparison.Ordinal)) &&
                              (streamId == null ||
                               string.Equals(kvp.Key.StreamId, streamId, StringComparison.Ordinal)) &&
                              (!providerScoped ||
                               string.Equals(kvp.Key.Provider, normalizedProvider, StringComparison.Ordinal)))
                .Select(static kvp => kvp.Value)
                .ToList();

            foreach (var state in statesToReset)
            {
                state.TryReset();
            }

            _log.Information(
                "Reset sequence state for symbol: {Symbol}, EventType: {EventType}, StreamId: {StreamId}, Provider: {Provider}",
                symbol,
                eventType ?? "all",
                streamId ?? "all",
                providerScoped ? provider : "all");
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Gets the total error counts.
    /// </summary>
    public long TotalGapErrors => Interlocked.Read(ref _totalGapErrors);
    public long TotalOutOfOrderErrors => Interlocked.Read(ref _totalOutOfOrderErrors);
    public long TotalDuplicateErrors => Interlocked.Read(ref _totalDuplicateErrors);
    public long TotalResetErrors => Interlocked.Read(ref _totalResetErrors);
    public long TotalEventsChecked => Interlocked.Read(ref _totalEventsChecked);

    private static SequenceStreamKey GetKey(
        string symbol,
        string eventType,
        string? streamId,
        string? provider)
    {
        return new SequenceStreamKey(
            NormalizeRequiredIdentity(symbol, nameof(symbol)),
            NormalizeRequiredIdentity(eventType, nameof(eventType)),
            streamId,
            NormalizeOptionalIdentity(provider, nameof(provider)));
    }

    private static string NormalizeRequiredIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalIdentity(string? value, string parameterName)
        => value is null ? null : NormalizeRequiredIdentity(value, parameterName);

    private List<SequenceError> SnapshotErrors()
    {
        var snapshot = new List<SequenceError>();
        foreach (var errorBuffer in _errors.Values)
        {
            errorBuffer.CopyTo(snapshot);
        }

        return snapshot;
    }

    private static bool TryRemoveExact<TValue>(
        ConcurrentDictionary<SequenceStreamKey, TValue> dictionary,
        SequenceStreamKey key,
        TValue value)
        where TValue : class
    {
        return ((ICollection<KeyValuePair<SequenceStreamKey, TValue>>)dictionary)
            .Remove(new KeyValuePair<SequenceStreamKey, TValue>(key, value));
    }

    internal void RunCleanup()
    {
        if (!TryEnterOperation())
            return;

        try
        {
            var now = _timeProvider.GetUtcNow();
            var cutoff = SubtractDaysClamped(now, _config.RetentionDays);
            var removedErrorKeys = 0;

            foreach (var kvp in _errors)
            {
                if (!kvp.Value.RetireIfEmptyAfterRemovingOlderThan(cutoff))
                    continue;

                _testHooks?.ErrorBufferRetiredBeforeRemoval?.Invoke();
                if (TryRemoveExact(_errors, kvp.Key, kvp.Value))
                {
                    removedErrorKeys++;
                }
            }

            // Evict stale symbol state entries that haven't received events
            // within the inactivity window. This prevents unbounded memory growth
            // when symbols are rotated (e.g., options chains expiring).
            var staleActivityCutoff = SubtractClamped(now, StateInactivityWindow);
            var removedStateKeys = 0;

            foreach (var kvp in _symbolStates)
            {
                if (!kvp.Value.RetireIfInactive(staleActivityCutoff))
                    continue;

                _testHooks?.StateRetiredBeforeRemoval?.Invoke();
                if (TryRemoveExact(_symbolStates, kvp.Key, kvp.Value))
                {
                    removedStateKeys++;
                }
            }

            if (removedErrorKeys > 0 || removedStateKeys > 0)
            {
                _log.Debug("Sequence error tracker cleanup: removed {ErrorKeys} empty error lists and {StateKeys} stale symbol states",
                    removedErrorKeys, removedStateKeys);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during sequence error tracker cleanup");
        }
        finally
        {
            ExitOperation();
        }
    }

    public void Dispose()
    {
        var stopTimer = false;
        lock (_lifecycleSync)
        {
            if (!_disposeRequested)
            {
                _disposeRequested = true;
                stopTimer = true;
            }
        }

        if (stopTimer)
        {
            _testHooks?.DisposeRequested?.Invoke();
            _cleanupTimer.Dispose();
            lock (_lifecycleSync)
            {
                _timerStopped = true;
                CompleteDisposeIfReadyUnderLock();
                Monitor.PulseAll(_lifecycleSync);
            }
        }

        // A callback disposing its own tracker cannot wait for itself. It requests shutdown;
        // the outer operation performs final cleanup when it releases the last lease.
        if (_operationDepth.Value > 0)
            return;

        lock (_lifecycleSync)
        {
            while (!_disposeCompleted)
            {
                Monitor.Wait(_lifecycleSync);
            }
        }
    }

    private readonly record struct SequenceStreamKey(
        string Symbol,
        string EventType,
        string? StreamId,
        string? Provider);

    private bool TryEnterOperation()
    {
        lock (_lifecycleSync)
        {
            if (_disposeRequested)
                return false;

            _activeOperations++;
            _operationDepth.Value++;
            return true;
        }
    }

    private void ExitOperation()
    {
        _operationDepth.Value--;
        lock (_lifecycleSync)
        {
            _activeOperations--;
            CompleteDisposeIfReadyUnderLock();
            Monitor.PulseAll(_lifecycleSync);
        }
    }

    private void CompleteDisposeIfReadyUnderLock()
    {
        if (_disposeCompleted || !_disposeRequested || !_timerStopped || _activeOperations != 0)
            return;

        _symbolStates.Clear();
        _errors.Clear();
        _disposeCompleted = true;
    }

    private static SequenceErrorConfig ValidateConfig(SequenceErrorConfig config)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            config.GapThreshold,
            1L,
            nameof(SequenceErrorConfig.GapThreshold));
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.SignificantGapSize,
            nameof(SequenceErrorConfig.SignificantGapSize));
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.ResetThreshold,
            nameof(SequenceErrorConfig.ResetThreshold));
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.MaxErrorsPerSymbol,
            nameof(SequenceErrorConfig.MaxErrorsPerSymbol));
        ArgumentOutOfRangeException.ThrowIfNegative(
            config.RetentionDays,
            nameof(SequenceErrorConfig.RetentionDays));
        return config;
    }

    private static DateTimeOffset SubtractDaysClamped(DateTimeOffset value, int days)
    {
        var availableDays = (value - DateTimeOffset.MinValue).TotalDays;
        return days >= availableDays ? DateTimeOffset.MinValue : value.AddDays(-days);
    }

    private static DateTimeOffset SubtractClamped(DateTimeOffset value, TimeSpan duration)
        => duration >= value - DateTimeOffset.MinValue
            ? DateTimeOffset.MinValue
            : value - duration;

    private sealed class SequenceErrorBuffer
    {
        private readonly object _sync = new();
        private readonly List<SequenceError> _errors = new();
        private bool _retired;

        public bool TryAdd(SequenceError error, int maximumCount)
        {
            lock (_sync)
            {
                if (_retired)
                    return false;

                _errors.Add(error);
                while (_errors.Count > maximumCount)
                {
                    _errors.RemoveAt(0);
                }

                return true;
            }
        }

        public void CopyTo(List<SequenceError> destination)
        {
            lock (_sync)
            {
                destination.AddRange(_errors);
            }
        }

        public bool RetireIfEmptyAfterRemovingOlderThan(DateTimeOffset cutoff)
        {
            lock (_sync)
            {
                if (_retired)
                    return true;

                _errors.RemoveAll(error => error.Timestamp < cutoff);
                if (_errors.Count > 0)
                    return false;

                _retired = true;
                return true;
            }
        }
    }

    /// <summary>
    /// Per-symbol sequence tracking state.
    /// </summary>
    private sealed class SymbolSequenceState
    {
        private readonly object _sync = new();
        private long _lastSequence = -1;
        private long _totalEvents;
        private DateTimeOffset _lastActivityTime = DateTimeOffset.MinValue;
        private readonly HashSet<long> _recentSequences = new();
        private readonly Queue<long> _sequenceHistory = new();
        private bool _retired;
        private const int MaxHistorySize = 1000;

        public string Symbol { get; }
        public string EventType { get; }
        public string? StreamId { get; }
        public long TotalEvents
        {
            get
            {
                lock (_sync)
                {
                    return _totalEvents;
                }
            }
        }

        public SymbolSequenceState(
            string symbol,
            string eventType,
            string? streamId,
            DateTimeOffset createdAt)
        {
            Symbol = symbol;
            EventType = eventType;
            StreamId = streamId;
            _lastActivityTime = createdAt;
        }

        public bool TryCheckSequence(
            long sequence,
            DateTimeOffset timestamp,
            DateTimeOffset observedAt,
            string? provider,
            SequenceErrorConfig config,
            out SequenceError? error)
        {
            lock (_sync)
            {
                if (_retired)
                {
                    error = null;
                    return false;
                }

                _totalEvents++;
                _lastActivityTime = observedAt;

                var lastSeq = _lastSequence;
                error = null;

                // First event - just record it
                if (lastSeq == -1)
                {
                    _lastSequence = sequence;
                    AddToHistory(sequence);
                    return true;
                }

                // Check for duplicate
                if (_recentSequences.Contains(sequence))
                {
                    error = new SequenceError(
                        Timestamp: timestamp,
                        Symbol: Symbol,
                        EventType: EventType,
                        ErrorType: SequenceErrorType.Duplicate,
                        ExpectedSequence: SaturatingIncrement(lastSeq),
                        ActualSequence: sequence,
                        GapSize: 0,
                        StreamId: StreamId,
                        Provider: provider
                    );
                }
                // Check for sequence reset (large backwards jump)
                else if (sequence < lastSeq &&
                         UnsignedDistance(lastSeq, sequence) > (ulong)config.ResetThreshold)
                {
                    error = new SequenceError(
                        Timestamp: timestamp,
                        Symbol: Symbol,
                        EventType: EventType,
                        ErrorType: SequenceErrorType.Reset,
                        ExpectedSequence: SaturatingIncrement(lastSeq),
                        ActualSequence: sequence,
                        GapSize: SaturatingGapSize(UnsignedDistance(lastSeq, sequence)),
                        StreamId: StreamId,
                        Provider: provider
                    );
                    // Accept the reset
                    _lastSequence = sequence;
                    _recentSequences.Clear();
                    _sequenceHistory.Clear();
                }
                // Check for out-of-order (small backwards)
                else if (sequence < lastSeq)
                {
                    error = new SequenceError(
                        Timestamp: timestamp,
                        Symbol: Symbol,
                        EventType: EventType,
                        ErrorType: SequenceErrorType.OutOfOrder,
                        ExpectedSequence: SaturatingIncrement(lastSeq),
                        ActualSequence: sequence,
                        GapSize: SaturatingGapSize(UnsignedDistance(lastSeq, sequence)),
                        StreamId: StreamId,
                        Provider: provider
                    );
                }
                // Check for gap (skip in sequence)
                else if (sequence > lastSeq &&
                         UnsignedDistance(sequence, lastSeq) > (ulong)config.GapThreshold)
                {
                    error = new SequenceError(
                        Timestamp: timestamp,
                        Symbol: Symbol,
                        EventType: EventType,
                        ErrorType: SequenceErrorType.Gap,
                        ExpectedSequence: SaturatingIncrement(lastSeq),
                        ActualSequence: sequence,
                        GapSize: SaturatingGapSize(UnsignedDistance(sequence, lastSeq) - 1),
                        StreamId: StreamId,
                        Provider: provider
                    );
                    _lastSequence = sequence;
                }
                else
                {
                    // Normal sequence progression
                    _lastSequence = sequence;
                }

                AddToHistory(sequence);
                return true;
            }
        }

        public bool TryReset()
        {
            lock (_sync)
            {
                if (_retired)
                    return false;

                _lastSequence = -1;
                _recentSequences.Clear();
                _sequenceHistory.Clear();
                return true;
            }
        }

        public bool RetireIfInactive(DateTimeOffset cutoff)
        {
            lock (_sync)
            {
                if (_retired)
                    return true;
                if (_lastActivityTime >= cutoff)
                    return false;

                _retired = true;
                return true;
            }
        }

        private void AddToHistory(long sequence)
        {
            _recentSequences.Add(sequence);
            _sequenceHistory.Enqueue(sequence);

            while (_sequenceHistory.Count > MaxHistorySize)
            {
                var old = _sequenceHistory.Dequeue();
                _recentSequences.Remove(old);
            }
        }

        private static ulong UnsignedDistance(long higher, long lower)
            => unchecked((ulong)(higher - lower));

        private static long SaturatingGapSize(ulong distance)
            => distance > long.MaxValue ? long.MaxValue : (long)distance;

        private static long SaturatingIncrement(long value)
            => value == long.MaxValue ? long.MaxValue : value + 1;
    }
}

internal sealed class SequenceErrorTrackerTestHooks
{
    public Action? ErrorBufferRetiredBeforeRemoval { get; set; }
    public Action? StateRetiredBeforeRemoval { get; set; }
    public Action? DisposeRequested { get; set; }
}

/// <summary>
/// Configuration for sequence error tracking.
/// </summary>
public sealed record SequenceErrorConfig
{
    /// <summary>
    /// Gap threshold - sequences more than this apart are considered a gap. Must be at least 1.
    /// </summary>
    public long GapThreshold { get; init; } = 1;

    /// <summary>
    /// Significant gap size for logging. Must be non-negative.
    /// </summary>
    public long SignificantGapSize { get; init; } = 100;

    /// <summary>
    /// Reset threshold - if sequence goes back more than this, it's a reset. Must be non-negative.
    /// </summary>
    public long ResetThreshold { get; init; } = 10000;

    /// <summary>
    /// Maximum errors to retain per stream identity. Must be non-negative.
    /// </summary>
    public int MaxErrorsPerSymbol { get; init; } = 1000;

    /// <summary>
    /// Days to retain error history. Must be non-negative.
    /// </summary>
    public int RetentionDays { get; init; } = 7;

    public static SequenceErrorConfig Default => new();
}

/// <summary>
/// Sequence error statistics.
/// </summary>
public sealed record SequenceErrorStatistics(
    long TotalEventsChecked,
    long TotalErrors,
    double ErrorRate,
    Dictionary<SequenceErrorType, long> ErrorsByType,
    int SymbolsWithErrors,
    double AverageGapSize,
    long MaxGapSize,
    DateTimeOffset CalculatedAt)
{
    /// <summary>
    /// Number of error records currently retained after per-stream caps and retention cleanup.
    /// This is an explicit compatibility alias for <see cref="TotalErrors"/>.
    /// </summary>
    public long RetainedTotalErrors => TotalErrors;

    /// <summary>
    /// Error rate calculated from retained records. This is an explicit compatibility alias for
    /// <see cref="ErrorRate"/>.
    /// </summary>
    public double RetainedErrorRate => ErrorRate;

    /// <summary>Total errors detected over the lifetime of this tracker instance.</summary>
    public long LifetimeTotalErrors { get; init; }

    /// <summary>Lifetime errors as a percentage of all checked events.</summary>
    public double LifetimeErrorRate { get; init; }
}
