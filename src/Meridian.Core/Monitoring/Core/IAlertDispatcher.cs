using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Monitoring.Core;

/// <summary>
/// Severity levels for monitoring alerts.
/// </summary>
public enum AlertSeverity : byte
{
    /// <summary>Informational message, no action required.</summary>
    Info,

    /// <summary>Warning that may require attention.</summary>
    Warning,

    /// <summary>Error that requires investigation.</summary>
    Error,

    /// <summary>Critical issue requiring immediate attention.</summary>
    Critical
}

/// <summary>
/// Categories for monitoring alerts.
/// </summary>
public enum AlertCategory : byte
{
    /// <summary>Connection-related alerts (WebSocket, API connectivity).</summary>
    Connection,

    /// <summary>Data quality alerts (gaps, anomalies, validation failures).</summary>
    DataQuality,

    /// <summary>Performance alerts (latency, throughput, backpressure).</summary>
    Performance,

    /// <summary>System resource alerts (memory, CPU, disk).</summary>
    SystemResources,

    /// <summary>Provider-specific alerts.</summary>
    Provider,

    /// <summary>Storage-related alerts (disk space, write failures).</summary>
    Storage,

    /// <summary>Configuration alerts (invalid settings, missing credentials).</summary>
    Configuration,

    /// <summary>Security alerts (authentication failures, rate limiting).</summary>
    Security
}

/// <summary>
/// Represents a monitoring alert from any system component.
/// </summary>
/// <param name="Id">Unique alert identifier.</param>
/// <param name="Severity">Alert severity level.</param>
/// <param name="Category">Alert category for filtering and routing.</param>
/// <param name="Source">Component that generated the alert.</param>
/// <param name="Title">Short summary of the alert.</param>
/// <param name="Message">Detailed alert message.</param>
/// <param name="Timestamp">When the alert was generated.</param>
/// <param name="Context">Additional context data.</param>
/// <param name="Exception">Related exception if applicable.</param>
public sealed record MonitoringAlert(
    string Id,
    AlertSeverity Severity,
    AlertCategory Category,
    string Source,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object>? Context = null,
    Exception? Exception = null)
{
    /// <summary>
    /// Creates an info alert.
    /// </summary>
    public static MonitoringAlert Info(string source, AlertCategory category, string title, string message,
        IReadOnlyDictionary<string, object>? context = null)
        => new(Guid.NewGuid().ToString("N"), AlertSeverity.Info, category, source, title, message,
            DateTimeOffset.UtcNow, context);

    /// <summary>
    /// Creates a warning alert.
    /// </summary>
    public static MonitoringAlert Warning(string source, AlertCategory category, string title, string message,
        IReadOnlyDictionary<string, object>? context = null)
        => new(Guid.NewGuid().ToString("N"), AlertSeverity.Warning, category, source, title, message,
            DateTimeOffset.UtcNow, context);

    /// <summary>
    /// Creates an error alert.
    /// </summary>
    public static MonitoringAlert Error(string source, AlertCategory category, string title, string message,
        Exception? exception = null, IReadOnlyDictionary<string, object>? context = null)
        => new(Guid.NewGuid().ToString("N"), AlertSeverity.Error, category, source, title, message,
            DateTimeOffset.UtcNow, context, exception);

    /// <summary>
    /// Creates a critical alert.
    /// </summary>
    public static MonitoringAlert Critical(string source, AlertCategory category, string title, string message,
        Exception? exception = null, IReadOnlyDictionary<string, object>? context = null)
        => new(Guid.NewGuid().ToString("N"), AlertSeverity.Critical, category, source, title, message,
            DateTimeOffset.UtcNow, context, exception);
}

/// <summary>
/// Alert subscription filter for selective alert reception.
/// </summary>
/// <param name="MinSeverity">Minimum severity to receive (null = all).</param>
/// <param name="Categories">Categories to receive (null = all).</param>
/// <param name="Sources">Sources to receive (null = all).</param>
public sealed record AlertFilter(
    AlertSeverity? MinSeverity = null,
    IReadOnlyList<AlertCategory>? Categories = null,
    IReadOnlyList<string>? Sources = null)
{
    /// <summary>
    /// Filter that matches all alerts.
    /// </summary>
    public static readonly AlertFilter All = new();

    /// <summary>
    /// Filter for critical alerts only.
    /// </summary>
    public static readonly AlertFilter CriticalOnly = new(MinSeverity: AlertSeverity.Critical);

    /// <summary>
    /// Filter for errors and above.
    /// </summary>
    public static readonly AlertFilter ErrorsAndAbove = new(MinSeverity: AlertSeverity.Error);

    /// <summary>
    /// Checks if an alert matches this filter.
    /// </summary>
    public bool Matches(MonitoringAlert alert)
    {
        if (MinSeverity.HasValue && alert.Severity < MinSeverity.Value)
            return false;

        if (Categories is { Count: > 0 } && !Categories.Contains(alert.Category))
            return false;

        if (Sources is { Count: > 0 } && !Sources.Contains(alert.Source))
            return false;

        return true;
    }
}

/// <summary>
/// Centralized alert dispatcher for publishing and subscribing to monitoring alerts.
/// </summary>
/// <remarks>
/// The alert dispatcher provides:
/// - Centralized alert publishing from any component
/// - Subscription with filtering capabilities
/// - Alert history with configurable retention
/// - Thread-safe operations
/// </remarks>
[ImplementsAdr("ADR-001", "Centralized monitoring alert dispatcher")]
public interface IAlertDispatcher
{
    /// <summary>
    /// Publishes an alert to all subscribers.
    /// </summary>
    /// <param name="alert">Alert to publish.</param>
    void Publish(MonitoringAlert alert);

    /// <summary>
    /// Publishes an alert asynchronously.
    /// </summary>
    /// <param name="alert">Alert to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(MonitoringAlert alert, CancellationToken ct = default);

    /// <summary>
    /// Subscribes to alerts with the specified filter.
    /// </summary>
    /// <param name="handler">Handler to receive alerts.</param>
    /// <param name="filter">Filter for alerts to receive.</param>
    /// <returns>Subscription handle (dispose to unsubscribe).</returns>
    IDisposable Subscribe(Action<MonitoringAlert> handler, AlertFilter? filter = null);

    /// <summary>
    /// Subscribes to alerts with an async handler.
    /// </summary>
    /// <param name="handler">Async handler to receive alerts.</param>
    /// <param name="filter">Filter for alerts to receive.</param>
    /// <returns>Subscription handle (dispose to unsubscribe).</returns>
    IDisposable Subscribe(Func<MonitoringAlert, Task> handler, AlertFilter? filter = null);

    /// <summary>
    /// Gets recent alerts matching the filter.
    /// </summary>
    /// <param name="count">Maximum number of alerts to return.</param>
    /// <param name="filter">Filter for alerts.</param>
    /// <returns>Recent alerts matching the filter.</returns>
    IReadOnlyList<MonitoringAlert> GetRecentAlerts(int count = 100, AlertFilter? filter = null);

    /// <summary>
    /// Gets alert statistics.
    /// </summary>
    /// <returns>Statistics about published alerts.</returns>
    AlertStatistics GetStatistics();
}

/// <summary>
/// Statistics about published alerts.
/// </summary>
/// <param name="TotalAlerts">Total number of alerts published.</param>
/// <param name="AlertsBySeverity">Count of alerts by severity.</param>
/// <param name="AlertsByCategory">Count of alerts by category.</param>
/// <param name="AlertsBySource">Count of alerts by source.</param>
/// <param name="Since">When statistics started being collected.</param>
public sealed record AlertStatistics(
    long TotalAlerts,
    IReadOnlyDictionary<AlertSeverity, long> AlertsBySeverity,
    IReadOnlyDictionary<AlertCategory, long> AlertsByCategory,
    IReadOnlyDictionary<string, long> AlertsBySource,
    DateTimeOffset Since);
