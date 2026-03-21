using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Monitoring.Core;

/// <summary>
/// Represents the severity of a health status.
/// </summary>
public enum HealthSeverity : byte
{
    /// <summary>Component is healthy and fully operational.</summary>
    Healthy,

    /// <summary>Component is operational but with minor issues.</summary>
    Degraded,

    /// <summary>Component has critical issues affecting functionality.</summary>
    Unhealthy,

    /// <summary>Component status is unknown or cannot be determined.</summary>
    Unknown
}

/// <summary>
/// Represents a health check result with status and diagnostics.
/// </summary>
/// <param name="ComponentName">Name of the component being checked.</param>
/// <param name="Severity">Health severity level.</param>
/// <param name="Message">Human-readable status message.</param>
/// <param name="CheckedAt">When the check was performed.</param>
/// <param name="Duration">How long the check took.</param>
/// <param name="Details">Additional diagnostic details.</param>
/// <param name="Exception">Exception if the check failed.</param>
public sealed record HealthCheckResult(
    string ComponentName,
    HealthSeverity Severity,
    string Message,
    DateTimeOffset CheckedAt,
    TimeSpan Duration,
    IReadOnlyDictionary<string, object>? Details = null,
    Exception? Exception = null)
{
    /// <summary>
    /// Creates a healthy result.
    /// </summary>
    public static HealthCheckResult Healthy(string componentName, string message, TimeSpan duration,
        IReadOnlyDictionary<string, object>? details = null)
        => new(componentName, HealthSeverity.Healthy, message, DateTimeOffset.UtcNow, duration, details);

    /// <summary>
    /// Creates a degraded result.
    /// </summary>
    public static HealthCheckResult Degraded(string componentName, string message, TimeSpan duration,
        IReadOnlyDictionary<string, object>? details = null)
        => new(componentName, HealthSeverity.Degraded, message, DateTimeOffset.UtcNow, duration, details);

    /// <summary>
    /// Creates an unhealthy result.
    /// </summary>
    public static HealthCheckResult Unhealthy(string componentName, string message, TimeSpan duration,
        Exception? exception = null, IReadOnlyDictionary<string, object>? details = null)
        => new(componentName, HealthSeverity.Unhealthy, message, DateTimeOffset.UtcNow, duration, details, exception);

    /// <summary>
    /// Creates an unknown result.
    /// </summary>
    public static HealthCheckResult Unknown(string componentName, string message, TimeSpan duration)
        => new(componentName, HealthSeverity.Unknown, message, DateTimeOffset.UtcNow, duration);
}

/// <summary>
/// Interface for components that provide health check capabilities.
/// Implementations should be lightweight and complete quickly.
/// </summary>
/// <remarks>
/// Health checks should:
/// - Complete within 5 seconds
/// - Not throw exceptions (return unhealthy status instead)
/// - Include relevant diagnostic details
/// - Be idempotent and side-effect free
/// </remarks>
[ImplementsAdr("ADR-001", "Unified health check interface for all components")]
public interface IHealthCheckProvider
{
    /// <summary>
    /// Name of the component for identification.
    /// </summary>
    string ComponentName { get; }

    /// <summary>
    /// Performs a health check and returns the result.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Health check result with status and diagnostics.</returns>
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// Aggregated health report from multiple providers.
/// </summary>
/// <param name="OverallSeverity">Worst severity across all components.</param>
/// <param name="CheckedAt">When the report was generated.</param>
/// <param name="TotalDuration">Total time to complete all checks.</param>
/// <param name="Results">Individual component results.</param>
public sealed record AggregatedHealthReport(
    HealthSeverity OverallSeverity,
    DateTimeOffset CheckedAt,
    TimeSpan TotalDuration,
    IReadOnlyList<HealthCheckResult> Results)
{
    /// <summary>
    /// Gets all healthy components.
    /// </summary>
    public IEnumerable<HealthCheckResult> HealthyComponents =>
        Results.Where(r => r.Severity == HealthSeverity.Healthy);

    /// <summary>
    /// Gets all degraded components.
    /// </summary>
    public IEnumerable<HealthCheckResult> DegradedComponents =>
        Results.Where(r => r.Severity == HealthSeverity.Degraded);

    /// <summary>
    /// Gets all unhealthy components.
    /// </summary>
    public IEnumerable<HealthCheckResult> UnhealthyComponents =>
        Results.Where(r => r.Severity == HealthSeverity.Unhealthy);

    /// <summary>
    /// Gets whether the overall system is operational (healthy or degraded).
    /// </summary>
    public bool IsOperational =>
        OverallSeverity is HealthSeverity.Healthy or HealthSeverity.Degraded;
}

/// <summary>
/// Aggregates health checks from multiple providers into a unified report.
/// </summary>
[ImplementsAdr("ADR-001", "Centralized health check aggregation")]
public interface IHealthCheckAggregator
{
    /// <summary>
    /// Registers a health check provider.
    /// </summary>
    /// <param name="provider">Provider to register.</param>
    void Register(IHealthCheckProvider provider);

    /// <summary>
    /// Unregisters a health check provider.
    /// </summary>
    /// <param name="componentName">Name of the component to unregister.</param>
    void Unregister(string componentName);

    /// <summary>
    /// Runs all registered health checks and returns an aggregated report.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated health report.</returns>
    Task<AggregatedHealthReport> CheckAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs a specific health check by component name.
    /// </summary>
    /// <param name="componentName">Name of the component to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Health check result or null if component not found.</returns>
    Task<HealthCheckResult?> CheckAsync(string componentName, CancellationToken ct = default);
}
