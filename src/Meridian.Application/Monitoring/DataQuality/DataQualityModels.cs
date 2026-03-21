using System.Text.Json.Serialization;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Completeness score for a specific symbol on a specific date.
/// </summary>
public sealed record CompletenessScore(
    string Symbol,
    DateOnly Date,
    double Score,
    long ExpectedEvents,
    long ActualEvents,
    long MissingEvents,
    TimeSpan TradingDuration,
    TimeSpan CoveredDuration,
    double CoveragePercent,
    DateTimeOffset CalculatedAt
)
{
    public string Grade => Score switch
    {
        >= 0.95 => "A",
        >= 0.85 => "B",
        >= 0.70 => "C",
        >= 0.50 => "D",
        _ => "F"
    };
}

/// <summary>
/// Represents a gap in the data stream.
/// </summary>
public sealed record DataGap(
    string Symbol,
    string EventType,
    DateTimeOffset GapStart,
    DateTimeOffset GapEnd,
    TimeSpan Duration,
    long MissedSequenceStart,
    long MissedSequenceEnd,
    long EstimatedMissedEvents,
    GapSeverity Severity,
    string? PossibleCause
);

/// <summary>
/// Gap severity classification.
/// </summary>
public enum GapSeverity : byte
{
    Minor,      // < 1 minute
    Moderate,   // 1-5 minutes
    Significant,// 5-30 minutes
    Major,      // 30-60 minutes
    Critical    // > 60 minutes
}

/// <summary>
/// Timeline entry for visualizing data availability.
/// </summary>
public sealed record TimelineEntry(
    DateTimeOffset Start,
    DateTimeOffset End,
    TimelineEntryType Type,
    long EventCount,
    string? Details
);

/// <summary>
/// Type of timeline entry.
/// </summary>
public enum TimelineEntryType : byte
{
    DataPresent,
    Gap,
    MarketClosed,
    PreMarket,
    AfterHours
}

/// <summary>
/// Gap analysis result with visual timeline data.
/// </summary>
public sealed record GapAnalysisResult(
    string Symbol,
    DateOnly Date,
    int TotalGaps,
    TimeSpan TotalGapDuration,
    double DataAvailabilityPercent,
    IReadOnlyList<DataGap> Gaps,
    IReadOnlyList<TimelineEntry> Timeline,
    DateTimeOffset AnalyzedAt
);

/// <summary>
/// Sequence error event.
/// </summary>
public sealed record SequenceError(
    DateTimeOffset Timestamp,
    string Symbol,
    string EventType,
    SequenceErrorType ErrorType,
    long ExpectedSequence,
    long ActualSequence,
    long GapSize,
    string? StreamId,
    string? Provider
);

/// <summary>
/// Type of sequence error.
/// </summary>
public enum SequenceErrorType : byte
{
    Gap,
    OutOfOrder,
    Duplicate,
    Reset
}

/// <summary>
/// Sequence error summary for a symbol.
/// </summary>
public sealed record SequenceErrorSummary(
    string Symbol,
    DateOnly Date,
    long TotalErrors,
    long GapErrors,
    long OutOfOrderErrors,
    long DuplicateErrors,
    long ResetErrors,
    double ErrorRate,
    IReadOnlyList<SequenceError> RecentErrors
);

/// <summary>
/// Cross-provider comparison result for a symbol.
/// </summary>
public sealed record CrossProviderComparison(
    string Symbol,
    DateOnly Date,
    string EventType,
    IReadOnlyList<ProviderDataSummary> Providers,
    IReadOnlyList<ProviderDiscrepancy> Discrepancies,
    string RecommendedProvider,
    DateTimeOffset ComparedAt
);

/// <summary>
/// Summary of data from a single provider.
/// </summary>
public sealed record ProviderDataSummary(
    string Provider,
    long EventCount,
    DateTimeOffset FirstEvent,
    DateTimeOffset LastEvent,
    TimeSpan Coverage,
    int GapCount,
    double CompletenessScore,
    double Latency,
    bool IsRecommended
);

/// <summary>
/// Discrepancy found between providers.
/// </summary>
public sealed record ProviderDiscrepancy(
    DateTimeOffset Timestamp,
    string DiscrepancyType,
    string Provider1,
    string Provider2,
    string Field,
    string Value1,
    string Value2,
    double Difference,
    DiscrepancySeverity Severity
);

/// <summary>
/// Discrepancy severity.
/// </summary>
public enum DiscrepancySeverity : byte
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Histogram bucket for latency distribution.
/// </summary>
public sealed record HistogramBucket(
    double LowerBound,
    double UpperBound,
    long Count,
    double Percentage
);

/// <summary>
/// Latency distribution histogram.
/// </summary>
public sealed record LatencyDistribution(
    string Symbol,
    string? Provider,
    DateTimeOffset From,
    DateTimeOffset To,
    long SampleCount,
    double MinLatencyMs,
    double MaxLatencyMs,
    double MeanLatencyMs,
    double MedianLatencyMs,
    double P50LatencyMs,
    double P90LatencyMs,
    double P95LatencyMs,
    double P99LatencyMs,
    double StandardDeviation,
    IReadOnlyList<HistogramBucket> Buckets,
    DateTimeOffset CalculatedAt
);

/// <summary>
/// Detected anomaly in market data.
/// </summary>
public sealed record DataAnomaly(
    string Id,
    DateTimeOffset Timestamp,
    string Symbol,
    AnomalyType Type,
    AnomalySeverity Severity,
    string Description,
    double ExpectedValue,
    double ActualValue,
    double DeviationPercent,
    double ZScore,
    string? Provider,
    bool IsAcknowledged,
    DateTimeOffset DetectedAt
);

/// <summary>
/// Type of anomaly detected.
/// </summary>
public enum AnomalyType : byte
{
    PriceSpike,
    PriceDrop,
    VolumeSpike,
    VolumeDrop,
    SpreadWide,
    StaleData,
    RapidPriceChange,
    AbnormalVolatility,
    MissingData,
    DuplicateData,
    CrossedMarket,
    InvalidPrice,
    InvalidVolume
}

/// <summary>
/// Anomaly severity.
/// </summary>
public enum AnomalySeverity : byte
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Anomaly detection configuration.
/// </summary>
public sealed record AnomalyDetectionConfig
{
    public double PriceSpikeThresholdPercent { get; init; } = 5.0;
    public double VolumeSpikeThresholdMultiplier { get; init; } = 10.0;
    public double VolumeDropThresholdMultiplier { get; init; } = 0.1;
    public double SpreadThresholdPercent { get; init; } = 2.0;
    public int StaleDataThresholdSeconds { get; init; } = 60;
    public double RapidChangeThresholdPercent { get; init; } = 1.0;
    public int RapidChangeWindowSeconds { get; init; } = 5;
    public double ZScoreThreshold { get; init; } = 3.0;
    public int MinSamplesForStatistics { get; init; } = 100;
    public bool EnablePriceAnomalies { get; init; } = true;
    public bool EnableVolumeAnomalies { get; init; } = true;
    public bool EnableSpreadAnomalies { get; init; } = true;
    public bool EnableStaleDataDetection { get; init; } = true;
    public int AlertCooldownSeconds { get; init; } = 60;

    public static AnomalyDetectionConfig Default => new();
}

/// <summary>
/// Daily data quality report.
/// </summary>
public sealed record DailyQualityReport(
    DateOnly Date,
    DateTimeOffset GeneratedAt,
    int SymbolsAnalyzed,
    double OverallScore,
    double CompletenessScore,
    double IntegrityScore,
    double TimelinessScore,
    IReadOnlyList<SymbolQualitySummary> SymbolSummaries,
    IReadOnlyList<DataGap> SignificantGaps,
    IReadOnlyList<SequenceError> SignificantErrors,
    IReadOnlyList<DataAnomaly> Anomalies,
    IReadOnlyList<string> Recommendations,
    ReportStatistics Statistics
);

/// <summary>
/// Weekly data quality report.
/// </summary>
public sealed record WeeklyQualityReport(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DailyQualityReport> DailyReports,
    double AverageScore,
    double ScoreTrend,
    IReadOnlyList<string> TopIssues,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<string> Recommendations,
    WeeklyStatistics Statistics
);

/// <summary>
/// Symbol-level quality summary.
/// </summary>
public sealed record SymbolQualitySummary(
    string Symbol,
    double OverallScore,
    double CompletenessScore,
    long TotalEvents,
    int GapCount,
    TimeSpan TotalGapDuration,
    int SequenceErrors,
    int Anomalies,
    string Grade,
    string[] Issues
);

/// <summary>
/// Report statistics.
/// </summary>
public sealed record ReportStatistics(
    long TotalEvents,
    long ExpectedEvents,
    long MissingEvents,
    int TotalGaps,
    TimeSpan TotalGapDuration,
    int TotalSequenceErrors,
    int TotalAnomalies,
    double AverageLatencyMs,
    double P99LatencyMs
);

/// <summary>
/// Weekly statistics.
/// </summary>
public sealed record WeeklyStatistics(
    long TotalEvents,
    long EventsPerDay,
    int TotalGaps,
    int GapsPerDay,
    int TotalAnomalies,
    double AverageScore,
    double BestDayScore,
    double WorstDayScore,
    DateOnly BestDay,
    DateOnly WorstDay
);

/// <summary>
/// Export format for reports.
/// </summary>
public enum ReportExportFormat : byte
{
    Json,
    Csv,
    Html,
    Markdown
}

/// <summary>
/// Report generation options.
/// </summary>
public sealed record ReportGenerationOptions
{
    public string[] Symbols { get; init; } = Array.Empty<string>();
    public bool IncludeAllSymbols { get; init; } = true;
    public bool IncludeGaps { get; init; } = true;
    public bool IncludeSequenceErrors { get; init; } = true;
    public bool IncludeAnomalies { get; init; } = true;
    public bool IncludeRecommendations { get; init; } = true;
    public bool IncludeTimeline { get; init; } = false;
    public int MaxGapsPerSymbol { get; init; } = 50;
    public int MaxErrorsPerSymbol { get; init; } = 50;
    public int MaxAnomaliesPerSymbol { get; init; } = 50;
    public double MinScoreThreshold { get; init; } = 0.0;
    public ReportExportFormat ExportFormat { get; init; } = ReportExportFormat.Json;

    public static ReportGenerationOptions Default => new();
}

/// <summary>
/// Real-time quality metrics snapshot.
/// </summary>
public sealed record RealTimeQualityMetrics(
    DateTimeOffset Timestamp,
    int ActiveSymbols,
    double OverallHealthScore,
    long EventsPerSecond,
    long GapsLast5Minutes,
    long SequenceErrorsLast5Minutes,
    long AnomaliesLast5Minutes,
    double AverageLatencyMs,
    int SymbolsWithIssues,
    IReadOnlyList<SymbolHealthStatus> SymbolHealth
);

/// <summary>
/// Health status for a single symbol.
/// </summary>
public sealed record SymbolHealthStatus(
    string Symbol,
    HealthState State,
    double Score,
    DateTimeOffset LastEvent,
    TimeSpan TimeSinceLastEvent,
    string[] ActiveIssues
);

/// <summary>
/// Health state enumeration.
/// </summary>
public enum HealthState : byte
{
    Healthy,
    Degraded,
    Unhealthy,
    Stale,
    Unknown
}
