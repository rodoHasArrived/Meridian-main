using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Tracks and reports sequence errors in market data streams.
/// Detects gaps, out-of-order events, duplicates, and sequence resets.
/// </summary>
public sealed class SequenceErrorTracker : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<SequenceErrorTracker>();
    private readonly ConcurrentDictionary<string, SymbolSequenceState> _symbolStates = new();
    private readonly ConcurrentDictionary<string, List<SequenceError>> _errors = new();
    private readonly SequenceErrorConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

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
    {
        _config = config ?? SequenceErrorConfig.Default;
        _cleanupTimer = new Timer(CleanupOldData, null,
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));

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
        if (_isDisposed)
            return null;

        Interlocked.Increment(ref _totalEventsChecked);

        var key = GetKey(symbol, eventType, streamId);
        var state = _symbolStates.GetOrAdd(key, _ => new SymbolSequenceState(symbol, eventType, streamId));

        var error = state.CheckSequence(sequenceNumber, timestamp, provider, _config);
        if (error != null)
        {
            RecordError(error);
        }

        return error;
    }

    /// <summary>
    /// Records a sequence error directly (for use with existing integrity events).
    /// </summary>
    public void RecordError(SequenceError error)
    {
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
        }

        var key = GetKey(error.Symbol, error.EventType, error.StreamId);
        var errorList = _errors.GetOrAdd(key, _ => new List<SequenceError>());

        lock (errorList)
        {
            errorList.Add(error);

            // Keep only recent errors to prevent memory growth
            while (errorList.Count > _config.MaxErrorsPerSymbol)
            {
                errorList.RemoveAt(0);
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
        var symbolUpper = symbol.ToUpperInvariant();
        var allErrors = _errors.Values
            .SelectMany(list => list.ToList())
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
            Date: date ?? DateOnly.FromDateTime(DateTime.UtcNow),
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
        return _errors.Values
            .SelectMany(list => list.ToList())
            .Where(e => DateOnly.FromDateTime(e.Timestamp.UtcDateTime) == date)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// Gets recent errors across all symbols.
    /// </summary>
    public IReadOnlyList<SequenceError> GetRecentErrors(int count = 100)
    {
        return _errors.Values
            .SelectMany(list => list.ToList())
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets errors for a specific symbol and event type.
    /// </summary>
    public IReadOnlyList<SequenceError> GetErrors(string symbol, string? eventType = null, int count = 100)
    {
        var symbolUpper = symbol.ToUpperInvariant();
        return _errors.Values
            .SelectMany(list => list.ToList())
            .Where(e => e.Symbol.Equals(symbolUpper, StringComparison.OrdinalIgnoreCase) &&
                        (eventType == null || e.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets symbols with the most errors.
    /// </summary>
    public IReadOnlyList<(string Symbol, int ErrorCount)> GetSymbolsWithMostErrors(int count = 10)
    {
        return _errors.Values
            .SelectMany(list => list.ToList())
            .GroupBy(e => e.Symbol)
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
        var allErrors = _errors.Values.SelectMany(list => list.ToList()).ToList();
        var totalChecked = Interlocked.Read(ref _totalEventsChecked);

        var errorsByType = new Dictionary<SequenceErrorType, long>
        {
            [SequenceErrorType.Gap] = Interlocked.Read(ref _totalGapErrors),
            [SequenceErrorType.OutOfOrder] = Interlocked.Read(ref _totalOutOfOrderErrors),
            [SequenceErrorType.Duplicate] = Interlocked.Read(ref _totalDuplicateErrors),
            [SequenceErrorType.Reset] = Interlocked.Read(ref _totalResetErrors)
        };

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
            SymbolsWithErrors: allErrors.Select(e => e.Symbol).Distinct().Count(),
            AverageGapSize: avgGapSize,
            MaxGapSize: maxGapSize,
            CalculatedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Resets the sequence state for a symbol (use after intentional reconnection).
    /// </summary>
    public void ResetSymbolState(string symbol, string? eventType = null, string? streamId = null)
    {
        var symbolUpper = symbol.ToUpperInvariant();
        var keysToReset = _symbolStates.Keys
            .Where(k => k.StartsWith(symbolUpper) &&
                        (eventType == null || k.Contains($":{eventType}:")) &&
                        (streamId == null || k.EndsWith($":{streamId}")))
            .ToList();

        foreach (var key in keysToReset)
        {
            if (_symbolStates.TryGetValue(key, out var state))
            {
                state.Reset();
            }
        }

        _log.Information("Reset sequence state for symbol: {Symbol}, EventType: {EventType}, StreamId: {StreamId}",
            symbol, eventType ?? "all", streamId ?? "all");
    }

    /// <summary>
    /// Gets the total error counts.
    /// </summary>
    public long TotalGapErrors => Interlocked.Read(ref _totalGapErrors);
    public long TotalOutOfOrderErrors => Interlocked.Read(ref _totalOutOfOrderErrors);
    public long TotalDuplicateErrors => Interlocked.Read(ref _totalDuplicateErrors);
    public long TotalResetErrors => Interlocked.Read(ref _totalResetErrors);
    public long TotalEventsChecked => Interlocked.Read(ref _totalEventsChecked);

    private static string GetKey(string symbol, string eventType, string? streamId)
    {
        return streamId != null
            ? $"{symbol.ToUpperInvariant()}:{eventType}:{streamId}"
            : $"{symbol.ToUpperInvariant()}:{eventType}";
    }

    private void CleanupOldData(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_config.RetentionDays);
            var keysToClean = new List<string>();

            foreach (var kvp in _errors)
            {
                lock (kvp.Value)
                {
                    kvp.Value.RemoveAll(e => e.Timestamp < cutoff);
                    if (kvp.Value.Count == 0)
                    {
                        keysToClean.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in keysToClean)
            {
                _errors.TryRemove(key, out _);
            }

            // Evict stale symbol state entries that haven't received events
            // within the retention window. This prevents unbounded memory growth
            // when symbols are rotated (e.g., options chains expiring).
            var staleActivityCutoff = DateTimeOffset.UtcNow.AddHours(-6);
            var staleStateKeys = _symbolStates
                .Where(kvp => kvp.Value.LastActivityTime < staleActivityCutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleStateKeys)
            {
                _symbolStates.TryRemove(key, out _);
            }

            if (keysToClean.Count > 0 || staleStateKeys.Count > 0)
            {
                _log.Debug("Sequence error tracker cleanup: removed {ErrorKeys} empty error lists and {StateKeys} stale symbol states",
                    keysToClean.Count, staleStateKeys.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during sequence error tracker cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _symbolStates.Clear();
        _errors.Clear();
    }

    /// <summary>
    /// Per-symbol sequence tracking state.
    /// </summary>
    private sealed class SymbolSequenceState
    {
        private long _lastSequence = -1;
        private long _totalEvents;
        private DateTimeOffset _lastActivityTime = DateTimeOffset.UtcNow;
        private readonly HashSet<long> _recentSequences = new();
        private readonly Queue<long> _sequenceHistory = new();
        private const int MaxHistorySize = 1000;

        public string Symbol { get; }
        public string EventType { get; }
        public string? StreamId { get; }
        public DateTimeOffset LastActivityTime => _lastActivityTime;
        public long TotalEvents => Interlocked.Read(ref _totalEvents);

        public SymbolSequenceState(string symbol, string eventType, string? streamId)
        {
            Symbol = symbol;
            EventType = eventType;
            StreamId = streamId;
        }

        public SequenceError? CheckSequence(long sequence, DateTimeOffset timestamp, string? provider, SequenceErrorConfig config)
        {
            Interlocked.Increment(ref _totalEvents);
            _lastActivityTime = DateTimeOffset.UtcNow;

            var lastSeq = Interlocked.Read(ref _lastSequence);
            SequenceError? error = null;

            // First event - just record it
            if (lastSeq == -1)
            {
                Interlocked.Exchange(ref _lastSequence, sequence);
                AddToHistory(sequence);
                return null;
            }

            // Check for duplicate
            if (_recentSequences.Contains(sequence))
            {
                error = new SequenceError(
                    Timestamp: timestamp,
                    Symbol: Symbol,
                    EventType: EventType,
                    ErrorType: SequenceErrorType.Duplicate,
                    ExpectedSequence: lastSeq + 1,
                    ActualSequence: sequence,
                    GapSize: 0,
                    StreamId: StreamId,
                    Provider: provider
                );
            }
            // Check for sequence reset (large backwards jump)
            else if (sequence < lastSeq - config.ResetThreshold)
            {
                error = new SequenceError(
                    Timestamp: timestamp,
                    Symbol: Symbol,
                    EventType: EventType,
                    ErrorType: SequenceErrorType.Reset,
                    ExpectedSequence: lastSeq + 1,
                    ActualSequence: sequence,
                    GapSize: lastSeq - sequence,
                    StreamId: StreamId,
                    Provider: provider
                );
                // Accept the reset
                Interlocked.Exchange(ref _lastSequence, sequence);
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
                    ExpectedSequence: lastSeq + 1,
                    ActualSequence: sequence,
                    GapSize: lastSeq - sequence,
                    StreamId: StreamId,
                    Provider: provider
                );
            }
            // Check for gap (skip in sequence)
            else if (sequence > lastSeq + config.GapThreshold)
            {
                error = new SequenceError(
                    Timestamp: timestamp,
                    Symbol: Symbol,
                    EventType: EventType,
                    ErrorType: SequenceErrorType.Gap,
                    ExpectedSequence: lastSeq + 1,
                    ActualSequence: sequence,
                    GapSize: sequence - lastSeq - 1,
                    StreamId: StreamId,
                    Provider: provider
                );
                Interlocked.Exchange(ref _lastSequence, sequence);
            }
            else
            {
                // Normal sequence progression
                Interlocked.Exchange(ref _lastSequence, sequence);
            }

            AddToHistory(sequence);
            return error;
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _lastSequence, -1);
            _recentSequences.Clear();
            _sequenceHistory.Clear();
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
    }
}

/// <summary>
/// Configuration for sequence error tracking.
/// </summary>
public sealed record SequenceErrorConfig
{
    /// <summary>
    /// Gap threshold - sequences more than this apart are considered a gap.
    /// </summary>
    public long GapThreshold { get; init; } = 1;

    /// <summary>
    /// Significant gap size for logging.
    /// </summary>
    public long SignificantGapSize { get; init; } = 100;

    /// <summary>
    /// Reset threshold - if sequence goes back more than this, it's a reset.
    /// </summary>
    public long ResetThreshold { get; init; } = 10000;

    /// <summary>
    /// Maximum errors to retain per symbol.
    /// </summary>
    public int MaxErrorsPerSymbol { get; init; } = 1000;

    /// <summary>
    /// Days to retain error history.
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
    DateTimeOffset CalculatedAt
);
