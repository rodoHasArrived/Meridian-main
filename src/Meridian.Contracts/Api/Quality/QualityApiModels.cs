namespace Meridian.Contracts.Api.Quality;

/// <summary>
/// Request contract for provider comparison queries used by desktop clients.
/// </summary>
public sealed record QualityComparisonRequest(
    string Symbol,
    DateOnly? Date,
    string? EventType);

/// <summary>
/// Request contract for anomaly acknowledgement routes used by desktop clients.
/// </summary>
public sealed record QualityAnomalyAcknowledgeRequest(
    string AnomalyId);

/// <summary>
/// Dashboard payload returned by <c>/api/quality/dashboard</c>.
/// </summary>
public sealed record QualityDashboardResponse(
    DateTimeOffset Timestamp,
    QualityRealTimeMetricsResponse RealTimeMetrics,
    QualityCompletenessSummaryResponse CompletenessStats,
    QualityGapStatisticsResponse GapStats,
    QualitySequenceErrorStatisticsResponse SequenceStats,
    QualityAnomalyStatisticsResponse AnomalyStats,
    QualityLatencyStatisticsResponse LatencyStats,
    IReadOnlyList<QualityGapResponse> RecentGaps,
    IReadOnlyList<QualitySequenceErrorResponse> RecentErrors,
    IReadOnlyList<QualityAnomalyResponse> RecentAnomalies,
    IReadOnlyList<string> StaleSymbols);

/// <summary>
/// Real-time quality metrics payload.
/// </summary>
public sealed record QualityRealTimeMetricsResponse(
    DateTimeOffset Timestamp,
    int ActiveSymbols,
    double OverallHealthScore,
    long EventsPerSecond,
    long GapsLast5Minutes,
    long SequenceErrorsLast5Minutes,
    long AnomaliesLast5Minutes,
    double AverageLatencyMs,
    int SymbolsWithIssues,
    IReadOnlyList<QualitySymbolHealthResponse> SymbolHealth);

/// <summary>
/// Symbol-level health entry returned inside dashboard metrics.
/// </summary>
public sealed record QualitySymbolHealthResponse(
    string Symbol,
    byte State,
    double Score,
    DateTimeOffset LastEvent,
    TimeSpan TimeSinceLastEvent,
    IReadOnlyList<string> ActiveIssues);

/// <summary>
/// Completeness summary returned inside the dashboard.
/// </summary>
public sealed record QualityCompletenessSummaryResponse(
    int TotalSymbolDates,
    double AverageScore,
    double MinScore,
    double MaxScore,
    int SymbolsTracked,
    int DatesTracked,
    long TotalEvents,
    long TotalExpectedEvents,
    double OverallCoverage,
    IReadOnlyDictionary<string, int> GradeDistribution,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Gap statistics payload returned by the dashboard.
/// </summary>
public sealed record QualityGapStatisticsResponse(
    int TotalGaps,
    TimeSpan TotalGapDuration,
    TimeSpan AverageGapDuration,
    TimeSpan MaxGapDuration,
    TimeSpan MinGapDuration,
    IReadOnlyDictionary<byte, int> GapsBySeverity,
    int SymbolsAffected,
    IReadOnlyList<string> MostAffectedSymbols,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Sequence-error statistics payload returned by the dashboard.
/// </summary>
public sealed record QualitySequenceErrorStatisticsResponse(
    long TotalEventsChecked,
    long TotalErrors,
    double ErrorRate,
    IReadOnlyDictionary<byte, long> ErrorsByType,
    int SymbolsWithErrors,
    double AverageGapSize,
    long MaxGapSize,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Anomaly statistics payload returned by the dashboard.
/// </summary>
public sealed record QualityAnomalyStatisticsResponse(
    long TotalAnomalies,
    IReadOnlyDictionary<byte, int> AnomaliesByType,
    IReadOnlyDictionary<byte, int> AnomaliesBySeverity,
    IReadOnlyList<QualityCountBySymbolResponse> SymbolsWithMostAnomalies,
    int UnacknowledgedCount,
    int AnomaliesLast24Hours,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Latency statistics payload returned by the dashboard and latency statistics route.
/// </summary>
public sealed record QualityLatencyStatisticsResponse(
    int SymbolsTracked,
    long TotalSamples,
    double GlobalMeanMs,
    double GlobalP50Ms,
    double GlobalP90Ms,
    double GlobalP99Ms,
    string? FastestSymbol,
    string? SlowestSymbol,
    IReadOnlyDictionary<string, double> DistributionsBySymbol,
    DateTimeOffset CalculatedAt);

/// <summary>
/// Gap row returned by the quality gaps routes.
/// </summary>
public sealed record QualityGapResponse(
    string Symbol,
    string EventType,
    DateTimeOffset GapStart,
    DateTimeOffset GapEnd,
    TimeSpan Duration,
    long MissedSequenceStart,
    long MissedSequenceEnd,
    long EstimatedMissedEvents,
    byte Severity,
    string? PossibleCause);

/// <summary>
/// Sequence-error row returned in dashboard payloads.
/// </summary>
public sealed record QualitySequenceErrorResponse(
    DateTimeOffset Timestamp,
    string Symbol,
    string EventType,
    byte ErrorType,
    long ExpectedSequence,
    long ActualSequence,
    long GapSize,
    string? StreamId,
    string? Provider);

/// <summary>
/// Anomaly row returned by anomaly routes and dashboard payloads.
/// </summary>
public sealed record QualityAnomalyResponse(
    string Id,
    DateTimeOffset Timestamp,
    string Symbol,
    byte Type,
    byte Severity,
    string Description,
    double ExpectedValue,
    double ActualValue,
    double DeviationPercent,
    double ZScore,
    string? Provider,
    bool IsAcknowledged,
    DateTimeOffset DetectedAt);

/// <summary>
/// Symbol/count pair used in anomaly statistics.
/// </summary>
public sealed record QualityCountBySymbolResponse(
    string Symbol,
    int Count);

/// <summary>
/// Cross-provider comparison payload returned by <c>/api/quality/comparison/{symbol}</c>.
/// </summary>
public sealed record QualityComparisonResponse(
    string Symbol,
    DateOnly Date,
    string EventType,
    IReadOnlyList<QualityProviderDataSummaryResponse> Providers,
    IReadOnlyList<QualityProviderDiscrepancyResponse> Discrepancies,
    string RecommendedProvider,
    DateTimeOffset ComparedAt);

/// <summary>
/// Provider summary entry inside the comparison payload.
/// </summary>
public sealed record QualityProviderDataSummaryResponse(
    string Provider,
    long EventCount,
    DateTimeOffset FirstEvent,
    DateTimeOffset LastEvent,
    TimeSpan Coverage,
    int GapCount,
    double CompletenessScore,
    double Latency,
    bool IsRecommended);

/// <summary>
/// Discrepancy row inside the comparison payload.
/// </summary>
public sealed record QualityProviderDiscrepancyResponse(
    DateTimeOffset Timestamp,
    string DiscrepancyType,
    string Provider1,
    string Provider2,
    string Field,
    string Value1,
    string Value2,
    double Difference,
    byte Severity);

/// <summary>
/// Acknowledgement response returned by anomaly acknowledgement endpoints.
/// </summary>
public sealed record QualityAnomalyAcknowledgementResponse(
    bool Acknowledged);
