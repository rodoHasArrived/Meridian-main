namespace Meridian.Application.Monitoring;

/// <summary>
/// Represents the aggregated metrics status for all providers.
/// </summary>
public sealed record ProviderMetricsStatus(
    DateTimeOffset Timestamp,
    ProviderMetrics[] Providers,
    int TotalProviders,
    int HealthyProviders
);

/// <summary>
/// Represents metrics for a single provider.
/// </summary>
public sealed record ProviderMetrics(
    string ProviderId,
    string ProviderType,
    bool IsConnected,
    long TradesReceived,
    long DepthUpdatesReceived,
    long QuotesReceived,
    long ConnectionAttempts,
    long ConnectionFailures,
    long MessagesDropped,
    long ActiveSubscriptions,
    double AverageLatencyMs,
    double MinLatencyMs,
    double MaxLatencyMs,
    double DataQualityScore,
    double ConnectionSuccessRate,
    DateTimeOffset Timestamp
);
