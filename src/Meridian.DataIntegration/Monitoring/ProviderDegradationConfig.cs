namespace Meridian.DataIntegration.Monitoring;

/// <summary>
/// Configuration for provider degradation scoring.
/// </summary>
public sealed record ProviderDegradationConfig
{
    /// <summary>How often to evaluate scores (seconds).</summary>
    public int EvaluationIntervalSeconds { get; init; } = 30;

    /// <summary>Composite score at or above which a provider is considered degraded (0.0-1.0).</summary>
    public double DegradationThreshold { get; init; } = 0.6;

    /// <summary>P95 latency (ms) above which latency degradation begins.</summary>
    public double LatencyThresholdMs { get; init; } = 200;

    /// <summary>P95 latency (ms) at which latency degradation is fully maxed out.</summary>
    public double LatencyMaxMs { get; init; } = 2000;

    /// <summary>Error rate (0.0-1.0) above which error degradation begins.</summary>
    public double ErrorRateThreshold { get; init; } = 0.05;

    /// <summary>Sliding window (seconds) for error rate calculation.</summary>
    public int ErrorWindowSeconds { get; init; } = 300;

    /// <summary>Reconnects per hour at which reconnect degradation is fully maxed out.</summary>
    public double MaxReconnectsPerHour { get; init; } = 10;

    /// <summary>Number of missed heartbeats for full connection degradation score.</summary>
    public int MaxMissedHeartbeatsForFullDegradation { get; init; } = 5;

    /// <summary>Weight for connection health component.</summary>
    public double ConnectionWeight { get; init; } = 0.35;

    /// <summary>Weight for latency component.</summary>
    public double LatencyWeight { get; init; } = 0.25;

    /// <summary>Weight for error rate component.</summary>
    public double ErrorRateWeight { get; init; } = 0.25;

    /// <summary>Weight for reconnect frequency component.</summary>
    public double ReconnectWeight { get; init; } = 0.15;

    public static ProviderDegradationConfig Default => new();
}
