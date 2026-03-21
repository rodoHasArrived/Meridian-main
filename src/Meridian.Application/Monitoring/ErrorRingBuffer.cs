using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Thread-safe ring buffer for storing recent errors.
/// Part of the data quality framework (QW-58) to provide
/// the Last N Errors endpoint for diagnostics and monitoring.
/// </summary>
/// <remarks>
/// Uses a lock-free circular buffer implementation to minimize
/// overhead on hot paths while providing recent error visibility.
/// </remarks>
public sealed class ErrorRingBuffer
{
    private readonly ErrorEntry[] _buffer;
    private readonly int _capacity;
    private long _head;
    private long _count;

    private static readonly ILogger _log = LoggingSetup.ForContext<ErrorRingBuffer>();

    /// <summary>
    /// Default buffer capacity.
    /// </summary>
    public const int DefaultCapacity = 100;

    /// <summary>
    /// Creates a new error ring buffer with the specified capacity.
    /// </summary>
    /// <param name="capacity">Maximum number of errors to retain.</param>
    public ErrorRingBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        _capacity = capacity;
        _buffer = new ErrorEntry[capacity];
        _log.Debug("ErrorRingBuffer initialized with capacity {Capacity}", capacity);
    }

    /// <summary>
    /// Records an error in the ring buffer.
    /// </summary>
    /// <param name="error">The error entry to record.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(ErrorEntry error)
    {
        var index = Interlocked.Increment(ref _head) - 1;
        var bufferIndex = (int)(index % _capacity);
        _buffer[bufferIndex] = error;

        // Track count up to capacity
        long currentCount;
        do
        {
            currentCount = Interlocked.Read(ref _count);
            if (currentCount >= _capacity)
                break;
        }
        while (Interlocked.CompareExchange(ref _count, currentCount + 1, currentCount) != currentCount);
    }

    /// <summary>
    /// Records an error from an exception.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(Exception ex, string? context = null, string? symbol = null, string? provider = null)
    {
        Record(new ErrorEntry(
            Id: GenerateId(),
            Timestamp: DateTimeOffset.UtcNow,
            Level: ErrorLevel.Error,
            Source: ex.Source ?? "Unknown",
            Message: ex.Message,
            ExceptionType: ex.GetType().Name,
            StackTrace: ex.StackTrace,
            Context: context,
            Symbol: symbol,
            Provider: provider
        ));
    }

    /// <summary>
    /// Records a warning-level error.
    /// </summary>
    public void RecordWarning(string source, string message, string? context = null, string? symbol = null, string? provider = null)
    {
        Record(new ErrorEntry(
            Id: GenerateId(),
            Timestamp: DateTimeOffset.UtcNow,
            Level: ErrorLevel.Warning,
            Source: source,
            Message: message,
            ExceptionType: null,
            StackTrace: null,
            Context: context,
            Symbol: symbol,
            Provider: provider
        ));
    }

    /// <summary>
    /// Records a critical-level error.
    /// </summary>
    public void RecordCritical(string source, string message, Exception? ex = null, string? context = null, string? symbol = null, string? provider = null)
    {
        Record(new ErrorEntry(
            Id: GenerateId(),
            Timestamp: DateTimeOffset.UtcNow,
            Level: ErrorLevel.Critical,
            Source: source,
            Message: message,
            ExceptionType: ex?.GetType().Name,
            StackTrace: ex?.StackTrace,
            Context: context,
            Symbol: symbol,
            Provider: provider
        ));
    }

    /// <summary>
    /// Gets the most recent N errors.
    /// </summary>
    /// <param name="count">Maximum number of errors to return.</param>
    /// <returns>List of recent errors, most recent first.</returns>
    public IReadOnlyList<ErrorEntry> GetRecent(int count = 10)
    {
        if (count <= 0)
            return Array.Empty<ErrorEntry>();

        var actualCount = Math.Min(count, (int)Interlocked.Read(ref _count));
        if (actualCount == 0)
            return Array.Empty<ErrorEntry>();

        var result = new List<ErrorEntry>(actualCount);
        var head = Interlocked.Read(ref _head);

        for (var i = 0; i < actualCount; i++)
        {
            var index = (int)((head - 1 - i) % _capacity);
            if (index < 0)
                index += _capacity;

            var entry = _buffer[index];
            if (entry.Timestamp != default) // Skip uninitialized entries
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    /// Gets errors filtered by level.
    /// </summary>
    public IReadOnlyList<ErrorEntry> GetByLevel(ErrorLevel level, int count = 10)
    {
        var all = GetRecent(Math.Min(_capacity, count * 3)); // Get more to filter
        return all.Where(e => e.Level >= level).Take(count).ToList();
    }

    /// <summary>
    /// Gets errors filtered by symbol.
    /// </summary>
    public IReadOnlyList<ErrorEntry> GetBySymbol(string symbol, int count = 10)
    {
        var all = GetRecent(_capacity);
        return all.Where(e => e.Symbol?.Equals(symbol, StringComparison.OrdinalIgnoreCase) == true)
                  .Take(count).ToList();
    }

    /// <summary>
    /// Gets errors that occurred within the specified time window.
    /// </summary>
    public IReadOnlyList<ErrorEntry> GetSince(DateTimeOffset since)
    {
        var all = GetRecent(_capacity);
        return all.Where(e => e.Timestamp >= since).ToList();
    }

    /// <summary>
    /// Gets error statistics.
    /// </summary>
    public ErrorStats GetStats()
    {
        var all = GetRecent(_capacity);
        var byLevel = all.GroupBy(e => e.Level)
                         .ToDictionary(g => g.Key, g => g.Count());

        var recentMinute = all.Count(e => e.Timestamp >= DateTimeOffset.UtcNow.AddMinutes(-1));
        var recentHour = all.Count(e => e.Timestamp >= DateTimeOffset.UtcNow.AddHours(-1));

        return new ErrorStats(
            TotalErrors: (int)Interlocked.Read(ref _count),
            ErrorsInLastMinute: recentMinute,
            ErrorsInLastHour: recentHour,
            WarningCount: byLevel.GetValueOrDefault(ErrorLevel.Warning, 0),
            ErrorCount: byLevel.GetValueOrDefault(ErrorLevel.Error, 0),
            CriticalCount: byLevel.GetValueOrDefault(ErrorLevel.Critical, 0),
            LastErrorTime: all.Count > 0 ? all[0].Timestamp : DateTimeOffset.MinValue
        );
    }

    /// <summary>
    /// Clears all errors from the buffer.
    /// </summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _head, 0);
        Array.Clear(_buffer, 0, _buffer.Length);
    }

    /// <summary>
    /// Gets the current number of errors stored.
    /// </summary>
    public int Count => (int)Math.Min(Interlocked.Read(ref _count), _capacity);

    /// <summary>
    /// Gets the buffer capacity.
    /// </summary>
    public int Capacity => _capacity;

    private static string GenerateId()
    {
        return $"ERR-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..8]}";
    }
}

/// <summary>
/// Represents a recorded error entry.
/// </summary>
public readonly record struct ErrorEntry(
    string Id,
    DateTimeOffset Timestamp,
    ErrorLevel Level,
    string Source,
    string Message,
    string? ExceptionType,
    string? StackTrace,
    string? Context,
    string? Symbol,
    string? Provider
);

/// <summary>
/// Error severity level.
/// </summary>
public enum ErrorLevel : byte
{
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>
/// Statistics about recorded errors.
/// </summary>
public readonly record struct ErrorStats(
    int TotalErrors,
    int ErrorsInLastMinute,
    int ErrorsInLastHour,
    int WarningCount,
    int ErrorCount,
    int CriticalCount,
    DateTimeOffset LastErrorTime
);
