using System.Collections.Concurrent;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Thread-safe in-memory counters for the F# validation pipeline stage.
/// Accumulates pass/reject statistics that are periodically exported to Prometheus
/// via <see cref="PrometheusMetrics.UpdateValidationMetrics"/>.
/// </summary>
public static class ValidationMetrics
{
    private static long _totalValidated;
    private static long _totalRejected;

    // Rejection counts keyed by error-type label (e.g. "invalid_price").
    private static readonly ConcurrentDictionary<string, long> _rejectedByErrorType =
        new(StringComparer.OrdinalIgnoreCase);

    // Rejection counts keyed by event-type label (e.g. "trade", "quote").
    private static readonly ConcurrentDictionary<string, long> _rejectedByEventType =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Total events that entered the validation stage (pass or fail).</summary>
    public static long TotalValidated => Interlocked.Read(ref _totalValidated);

    /// <summary>Total events that failed validation and were sent to the dead-letter sink.</summary>
    public static long TotalRejected => Interlocked.Read(ref _totalRejected);

    /// <summary>
    /// Pass rate as a percentage (0–100). Returns 100 when no events have been validated yet.
    /// </summary>
    public static double PassRatePercent
    {
        get
        {
            var validated = TotalValidated;
            if (validated == 0)
                return 100.0;
            var rejected = TotalRejected;
            return Math.Round(100.0 * (validated - rejected) / validated, 2);
        }
    }

    /// <summary>
    /// Records a single event that entered the validation stage (regardless of outcome).
    /// Call once per validated event, before determining pass/fail.
    /// </summary>
    /// <param name="eventType">Lowercase event-type label, e.g. "trade" or "quote".</param>
    public static void RecordValidated(string eventType)
    {
        Interlocked.Increment(ref _totalValidated);
    }

    /// <summary>
    /// Records a rejection and distributes the count across all error types found in the event.
    /// </summary>
    /// <param name="eventType">Lowercase event-type label, e.g. "trade" or "quote".</param>
    /// <param name="errorDescriptions">Error description strings returned by the F# validator.</param>
    public static void RecordRejection(string eventType, IReadOnlyList<string> errorDescriptions)
    {
        Interlocked.Increment(ref _totalRejected);

        _rejectedByEventType.AddOrUpdate(eventType, 1L, (_, c) => c + 1L);

        foreach (var description in errorDescriptions)
        {
            var errorType = ClassifyError(description);
            _rejectedByErrorType.AddOrUpdate(errorType, 1L, (_, c) => c + 1L);
        }
    }

    /// <summary>
    /// Gets a snapshot of current counters for Prometheus export.
    /// </summary>
    public static ValidationMetricsSnapshot GetSnapshot() =>
        new(
            TotalValidated: TotalValidated,
            TotalRejected: TotalRejected,
            PassRatePercent: PassRatePercent,
            RejectedByErrorType: new Dictionary<string, long>(_rejectedByErrorType),
            RejectedByEventType: new Dictionary<string, long>(_rejectedByEventType));

    /// <summary>Resets all counters. Intended for testing only.</summary>
    public static void Reset()
    {
        Interlocked.Exchange(ref _totalValidated, 0);
        Interlocked.Exchange(ref _totalRejected, 0);
        _rejectedByErrorType.Clear();
        _rejectedByEventType.Clear();
    }

    // Maps F# validation error description prefixes to short Prometheus label values.
    internal static string ClassifyError(string description)
    {
        if (description.StartsWith("Invalid price", StringComparison.OrdinalIgnoreCase))
            return "invalid_price";
        if (description.StartsWith("Invalid quantity", StringComparison.OrdinalIgnoreCase))
            return "invalid_quantity";
        if (description.StartsWith("Invalid symbol", StringComparison.OrdinalIgnoreCase))
            return "invalid_symbol";
        if (description.StartsWith("Stale timestamp", StringComparison.OrdinalIgnoreCase))
            return "stale_timestamp";
        if (description.StartsWith("Future timestamp", StringComparison.OrdinalIgnoreCase))
            return "future_timestamp";
        if (description.StartsWith("Invalid sequence", StringComparison.OrdinalIgnoreCase))
            return "invalid_sequence";
        if (description.StartsWith("Invalid spread", StringComparison.OrdinalIgnoreCase))
            return "invalid_spread";
        return "custom";
    }
}

/// <summary>
/// Immutable snapshot of validation metrics at a point in time.
/// </summary>
public sealed record ValidationMetricsSnapshot(
    long TotalValidated,
    long TotalRejected,
    double PassRatePercent,
    IReadOnlyDictionary<string, long> RejectedByErrorType,
    IReadOnlyDictionary<string, long> RejectedByEventType);
