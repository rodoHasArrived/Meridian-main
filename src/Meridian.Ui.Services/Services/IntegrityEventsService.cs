using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Meridian.Ui.Services.Collections;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for tracking and managing data integrity events.
/// Provides access to recent integrity alerts for dashboard display.
/// </summary>
public sealed class IntegrityEventsService
{
    private static readonly Lazy<IntegrityEventsService> _instance = new(() => new IntegrityEventsService());
    private readonly BoundedObservableCollection<IntegrityEvent> _events;
    private readonly NotificationService _notificationService;
    private const int MaxEvents = 100;
    private const int MaxRecentEvents = 10;

    public static IntegrityEventsService Instance => _instance.Value;

    private IntegrityEventsService()
    {
        _notificationService = NotificationService.Instance;
        _events = new BoundedObservableCollection<IntegrityEvent>(MaxEvents);
    }

    /// <summary>
    /// Gets the total count of integrity events.
    /// </summary>
    public int TotalCount => _events.Count;

    /// <summary>
    /// Gets the count of critical integrity events.
    /// </summary>
    public int CriticalCount => _events.Count(e => e.Severity == IntegritySeverity.Critical);

    /// <summary>
    /// Gets the count of warning integrity events.
    /// </summary>
    public int WarningCount => _events.Count(e => e.Severity == IntegritySeverity.Warning);

    /// <summary>
    /// Gets the most recent integrity events.
    /// </summary>
    public IReadOnlyList<IntegrityEvent> GetRecentEvents(int count = MaxRecentEvents)
    {
        return _events.Take(Math.Min(count, MaxRecentEvents)).ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets all integrity events.
    /// </summary>
    public IReadOnlyList<IntegrityEvent> GetAllEvents()
    {
        return _events.AsReadOnly();
    }

    /// <summary>
    /// Gets integrity events filtered by severity.
    /// </summary>
    public IReadOnlyList<IntegrityEvent> GetEventsBySeverity(IntegritySeverity severity)
    {
        return _events.Where(e => e.Severity == severity).ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets integrity events filtered by symbol.
    /// </summary>
    public IReadOnlyList<IntegrityEvent> GetEventsBySymbol(string symbol)
    {
        return _events.Where(e => e.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .ToList().AsReadOnly();
    }

    /// <summary>
    /// Records a new integrity event.
    /// Uses efficient Prepend operation - O(1) with automatic capacity management.
    /// </summary>
    public async Task RecordEventAsync(IntegrityEvent integrityEvent, CancellationToken ct = default)
    {
        // Prepend to collection - automatically handles capacity limit
        _events.Prepend(integrityEvent);

        // Raise event
        EventRecorded?.Invoke(this, integrityEvent);

        // Send notification for critical/warning events
        if (integrityEvent.Severity == IntegritySeverity.Critical)
        {
            await _notificationService.NotifyErrorAsync(
                "Critical Integrity Alert",
                $"{integrityEvent.Symbol}: {integrityEvent.Description}");
        }
        else if (integrityEvent.Severity == IntegritySeverity.Warning)
        {
            await _notificationService.NotifyWarningAsync(
                "Integrity Warning",
                $"{integrityEvent.Symbol}: {integrityEvent.Description}");
        }
    }

    /// <summary>
    /// Records a sequence gap event.
    /// </summary>
    public async Task RecordSequenceGapAsync(string symbol, long expectedSeq, long actualSeq, DateTime timestamp, CancellationToken ct = default)
    {
        var gapSize = actualSeq - expectedSeq;
        var severity = gapSize > 100 ? IntegritySeverity.Critical :
                      gapSize > 10 ? IntegritySeverity.Warning :
                      IntegritySeverity.Info;

        await RecordEventAsync(new IntegrityEvent
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = timestamp,
            Symbol = symbol,
            EventType = IntegrityEventType.SequenceGap,
            Severity = severity,
            Description = $"Sequence gap detected: expected {expectedSeq}, got {actualSeq} ({gapSize} missing)",
            ExpectedSequence = expectedSeq,
            ActualSequence = actualSeq,
            GapSize = (int)gapSize
        });
    }

    /// <summary>
    /// Records an out-of-order event.
    /// </summary>
    public async Task RecordOutOfOrderAsync(string symbol, long expectedSeq, long actualSeq, DateTime timestamp, CancellationToken ct = default)
    {
        await RecordEventAsync(new IntegrityEvent
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = timestamp,
            Symbol = symbol,
            EventType = IntegrityEventType.OutOfOrder,
            Severity = IntegritySeverity.Warning,
            Description = $"Out-of-order event: expected {expectedSeq}, got {actualSeq}",
            ExpectedSequence = expectedSeq,
            ActualSequence = actualSeq
        });
    }

    /// <summary>
    /// Records a stale data event.
    /// </summary>
    public async Task RecordStaleDataAsync(string symbol, TimeSpan staleDuration, DateTime lastEventTime, CancellationToken ct = default)
    {
        var severity = staleDuration.TotalMinutes > 5 ? IntegritySeverity.Critical :
                      staleDuration.TotalMinutes > 1 ? IntegritySeverity.Warning :
                      IntegritySeverity.Info;

        await RecordEventAsync(new IntegrityEvent
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Symbol = symbol,
            EventType = IntegrityEventType.StaleData,
            Severity = severity,
            Description = $"No data received for {FormatDuration(staleDuration)}",
            StaleDuration = staleDuration,
            LastEventTime = lastEventTime
        });
    }

    /// <summary>
    /// Records a data validation failure.
    /// </summary>
    public async Task RecordValidationFailureAsync(string symbol, string field, string reason, DateTime timestamp, CancellationToken ct = default)
    {
        await RecordEventAsync(new IntegrityEvent
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = timestamp,
            Symbol = symbol,
            EventType = IntegrityEventType.ValidationFailure,
            Severity = IntegritySeverity.Warning,
            Description = $"Validation failed for {field}: {reason}",
            AffectedField = field,
            ValidationReason = reason
        });
    }

    /// <summary>
    /// Records a duplicate event detection.
    /// </summary>
    public async Task RecordDuplicateAsync(string symbol, long sequence, DateTime timestamp, CancellationToken ct = default)
    {
        await RecordEventAsync(new IntegrityEvent
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = timestamp,
            Symbol = symbol,
            EventType = IntegrityEventType.Duplicate,
            Severity = IntegritySeverity.Info,
            Description = $"Duplicate event detected at sequence {sequence}",
            ActualSequence = sequence
        });
    }

    /// <summary>
    /// Records a provider switchover event.
    /// </summary>
    public async Task RecordProviderSwitchAsync(string symbol, string fromProvider, string toProvider, string reason, CancellationToken ct = default)
    {
        await RecordEventAsync(new IntegrityEvent
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Symbol = symbol,
            EventType = IntegrityEventType.ProviderSwitch,
            Severity = IntegritySeverity.Info,
            Description = $"Provider switched from {fromProvider} to {toProvider}: {reason}",
            FromProvider = fromProvider,
            ToProvider = toProvider
        });
    }

    /// <summary>
    /// Acknowledges an integrity event.
    /// </summary>
    public void AcknowledgeEvent(string eventId)
    {
        var evt = _events.FirstOrDefault(e => e.Id == eventId);
        if (evt != null)
        {
            evt.IsAcknowledged = true;
            evt.AcknowledgedAt = DateTime.UtcNow;
            EventAcknowledged?.Invoke(this, evt);
        }
    }

    /// <summary>
    /// Clears all integrity events.
    /// </summary>
    public void ClearEvents()
    {
        _events.Clear();
        EventsCleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets summary statistics for integrity events.
    /// </summary>
    public IntegritySummary GetSummary()
    {
        var now = DateTime.UtcNow;
        var last24Hours = _events.Where(e => (now - e.Timestamp).TotalHours <= 24).ToList();
        var lastHour = _events.Where(e => (now - e.Timestamp).TotalHours <= 1).ToList();

        return new IntegritySummary
        {
            TotalEvents = _events.Count,
            CriticalCount = _events.Count(e => e.Severity == IntegritySeverity.Critical),
            WarningCount = _events.Count(e => e.Severity == IntegritySeverity.Warning),
            InfoCount = _events.Count(e => e.Severity == IntegritySeverity.Info),
            EventsLast24Hours = last24Hours.Count,
            EventsLastHour = lastHour.Count,
            UnacknowledgedCount = _events.Count(e => !e.IsAcknowledged),
            MostAffectedSymbol = _events
                .GroupBy(e => e.Symbol)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "N/A",
            LastEventTime = _events.FirstOrDefault()?.Timestamp
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalSeconds}s";
    }

    /// <summary>
    /// Event raised when a new integrity event is recorded.
    /// </summary>
    public event EventHandler<IntegrityEvent>? EventRecorded;

    /// <summary>
    /// Event raised when an integrity event is acknowledged.
    /// </summary>
    public event EventHandler<IntegrityEvent>? EventAcknowledged;

    /// <summary>
    /// Event raised when all events are cleared.
    /// </summary>
    public event EventHandler? EventsCleared;
}

/// <summary>
/// Represents a data integrity event.
/// </summary>
public sealed class IntegrityEvent
{
    public string Id { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public IntegrityEventType EventType { get; set; }
    public IntegritySeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;

    // Sequence-related properties
    public long? ExpectedSequence { get; set; }
    public long? ActualSequence { get; set; }
    public int? GapSize { get; set; }

    // Stale data properties
    public TimeSpan? StaleDuration { get; set; }
    public DateTime? LastEventTime { get; set; }

    // Validation properties
    public string? AffectedField { get; set; }
    public string? ValidationReason { get; set; }

    // Provider switch properties
    public string? FromProvider { get; set; }
    public string? ToProvider { get; set; }

    // Acknowledgment
    public bool IsAcknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>
    /// Gets a relative time string for display.
    /// </summary>
    public string RelativeTime
    {
        get
        {
            var elapsed = DateTime.UtcNow - Timestamp;
            if (elapsed.TotalSeconds < 60)
                return "Just now";
            if (elapsed.TotalMinutes < 60)
                return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalHours < 24)
                return $"{(int)elapsed.TotalHours}h ago";
            return $"{(int)elapsed.TotalDays}d ago";
        }
    }

    /// <summary>
    /// Gets the icon glyph for the event type.
    /// </summary>
    public string IconGlyph => EventType switch
    {
        IntegrityEventType.SequenceGap => "\uE783",      // Warning
        IntegrityEventType.OutOfOrder => "\uE7BA",       // Sort
        IntegrityEventType.StaleData => "\uE916",        // Clock
        IntegrityEventType.ValidationFailure => "\uE814", // Shield
        IntegrityEventType.Duplicate => "\uE8C8",        // Copy
        IntegrityEventType.ProviderSwitch => "\uE8AB",   // Switch
        _ => "\uE946"                                     // Info
    };

    /// <summary>
    /// Gets the color for the severity level.
    /// </summary>
    public string SeverityColor => Severity switch
    {
        IntegritySeverity.Critical => "#F85149",  // Red
        IntegritySeverity.Warning => "#D29922",   // Yellow/Orange
        IntegritySeverity.Info => "#58A6FF",      // Blue
        _ => "#A0AEC0"                             // Gray
    };
}

/// <summary>
/// Types of integrity events.
/// </summary>
public enum IntegrityEventType : byte
{
    SequenceGap,
    OutOfOrder,
    StaleData,
    ValidationFailure,
    Duplicate,
    ProviderSwitch,
    Other
}

/// <summary>
/// Severity levels for integrity events.
/// </summary>
public enum IntegritySeverity : byte
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Summary statistics for integrity events.
/// </summary>
public sealed class IntegritySummary
{
    public int TotalEvents { get; set; }
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public int EventsLast24Hours { get; set; }
    public int EventsLastHour { get; set; }
    public int UnacknowledgedCount { get; set; }
    public string MostAffectedSymbol { get; set; } = string.Empty;
    public DateTime? LastEventTime { get; set; }
}
