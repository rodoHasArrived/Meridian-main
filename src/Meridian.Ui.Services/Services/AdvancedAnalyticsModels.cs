using System;
using System.Collections.Generic;

namespace Meridian.Ui.Services;

// =====================================================================================
// Advanced Analytics DTOs — shared across desktop applications.
// =====================================================================================


/// <summary>
/// Selects symbols, dates, event types, and the minimum duration for detecting data gaps.
/// </summary>
public sealed class GapAnalysisOptions
{
    public string? Symbol { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public List<string>? EventTypes { get; set; }
    public int MinGapMinutes { get; set; } = 5;
}

/// <summary>
/// Reports gap-analysis success, detected intervals, total duration, and per-symbol coverage.
/// </summary>
public sealed class GapAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime AnalysisTime { get; set; }
    public int TotalGaps { get; set; }
    public TimeSpan TotalGapDuration { get; set; }
    public List<AnalyticsDataGap> Gaps { get; set; } = new();
    public List<SymbolGapSummary> SymbolSummaries { get; set; } = new();
}

/// <summary>
/// Carries the API gap-analysis timestamp, detected intervals, and symbol summaries.
/// </summary>
public sealed class GapAnalysisResponse
{
    public DateTime AnalysisTime { get; set; }
    public int TotalGaps { get; set; }
    public TimeSpan TotalGapDuration { get; set; }
    public List<AnalyticsDataGap>? Gaps { get; set; }
    public List<SymbolGapSummary>? SymbolSummaries { get; set; }
}

/// <summary>
/// Describes a missing event interval for a symbol, its possible cause, and repair eligibility.
/// </summary>
public sealed class AnalyticsDataGap
{
    public string Symbol { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string? PossibleReason { get; set; }
    public bool IsRepairable { get; set; }
}

/// <summary>
/// Summarizes a symbol's gap count, missing duration, and coverage percentage.
/// </summary>
public sealed class SymbolGapSummary
{
    public string Symbol { get; set; } = string.Empty;
    public int GapCount { get; set; }
    public TimeSpan TotalGapDuration { get; set; }
    public double CoveragePercent { get; set; }
}

/// <summary>
/// Selects gaps to repair and controls alternative-provider use and dry-run execution.
/// </summary>
public sealed class GapRepairOptions
{
    public string? Symbol { get; set; }
    public List<string>? GapIds { get; set; }
    public bool UseAlternativeProviders { get; set; } = true;
    public bool DryRun { get; set; }
}

/// <summary>
/// Reports repair outcomes, recovered records, and details for each attempted gap.
/// </summary>
public sealed class AnalyticsGapRepairResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int GapsAttempted { get; set; }
    public int GapsRepaired { get; set; }
    public int GapsFailed { get; set; }
    public long RecordsRecovered { get; set; }
    public List<GapRepairDetail> Details { get; set; } = new();
}

/// <summary>
/// Carries API repair counts, recovered records, and per-gap outcomes.
/// </summary>
public sealed class GapRepairResponse
{
    public int GapsAttempted { get; set; }
    public int GapsRepaired { get; set; }
    public int GapsFailed { get; set; }
    public long RecordsRecovered { get; set; }
    public List<GapRepairDetail>? Details { get; set; }
}

/// <summary>
/// Describes one gap-repair attempt, including its source provider and any error.
/// </summary>
public sealed class GapRepairDetail
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime GapStart { get; set; }
    public DateTime GapEnd { get; set; }
    public bool Repaired { get; set; }
    public int RecordsRecovered { get; set; }
    public string? SourceProvider { get; set; }
    public string? Error { get; set; }
}



/// <summary>
/// Selects a symbol, date, providers, and event type for a consistency comparison.
/// </summary>
public sealed class CrossProviderComparisonOptions
{
    public string Symbol { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public List<string>? Providers { get; set; }
    public string EventType { get; set; } = "trades";
}

/// <summary>
/// Reports comparison success, consistency scores, provider pairs, and discrepancies.
/// </summary>
public sealed class CrossProviderComparisonResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime ComparisonTime { get; set; }
    public double OverallConsistencyScore { get; set; }
    public List<string> ProvidersCompared { get; set; } = new();
    public List<ProviderComparison> Comparisons { get; set; } = new();
    public List<DataDiscrepancy> Discrepancies { get; set; } = new();
}

/// <summary>
/// Carries API consistency scores, compared providers, and observed discrepancies.
/// </summary>
public sealed class CrossProviderComparisonResponse
{
    public DateTime ComparisonTime { get; set; }
    public double OverallConsistencyScore { get; set; }
    public List<string>? ProvidersCompared { get; set; }
    public List<ProviderComparison>? Comparisons { get; set; }
    public List<DataDiscrepancy>? Discrepancies { get; set; }
}

/// <summary>
/// Summarizes matching, mismatching, and provider-exclusive records for a provider pair.
/// </summary>
public sealed class ProviderComparison
{
    public string Provider1 { get; set; } = string.Empty;
    public string Provider2 { get; set; } = string.Empty;
    public double ConsistencyScore { get; set; }
    public long RecordsMatched { get; set; }
    public long RecordsMismatched { get; set; }
    public long RecordsOnlyIn1 { get; set; }
    public long RecordsOnlyIn2 { get; set; }
}

/// <summary>
/// Captures differing provider values at a timestamp and the measured difference.
/// </summary>
public sealed class DataDiscrepancy
{
    public DateTime Timestamp { get; set; }
    public string DiscrepancyType { get; set; } = string.Empty;
    public string Provider1 { get; set; } = string.Empty;
    public string Provider2 { get; set; } = string.Empty;
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
    public double Difference { get; set; }
}



/// <summary>
/// Selects a provider, observation period, and bucket count for latency analysis.
/// </summary>
public sealed class LatencyHistogramOptions
{
    public string? Provider { get; set; }
    public int BucketCount { get; set; } = 20;
    public TimeSpan? Period { get; set; }
}

/// <summary>
/// Reports latency-query success and provider histograms for an observation period.
/// </summary>
public sealed class LatencyHistogramResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Period { get; set; }
    public List<ProviderLatencyData> Providers { get; set; } = new();
}

/// <summary>
/// Carries API latency histograms for the requested observation period.
/// </summary>
public sealed class LatencyHistogramResponse
{
    public string? Period { get; set; }
    public List<ProviderLatencyData>? Providers { get; set; }
}

/// <summary>
/// Groups a provider's latency buckets and median, 95th, and 99th percentiles.
/// </summary>
public sealed class ProviderLatencyData
{
    public string Provider { get; set; } = string.Empty;
    public List<LatencyBucket> Buckets { get; set; } = new();
    public double P50Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
}

/// <summary>
/// Describes a latency range in milliseconds with its observation count and percentage.
/// </summary>
public sealed class LatencyBucket
{
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public long Count { get; set; }
    public double Percentage { get; set; }
}

/// <summary>
/// Reports query success and descriptive latency statistics for providers.
/// </summary>
public sealed class LatencyStatisticsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderLatencyStatistics> Statistics { get; set; } = new();
}

/// <summary>
/// Carries API descriptive latency statistics grouped by provider.
/// </summary>
public sealed class LatencyStatisticsResponse
{
    public List<ProviderLatencyStatistics>? Statistics { get; set; }
}

/// <summary>
/// Summarizes a provider's latency distribution, percentiles, and sample count.
/// </summary>
public sealed class ProviderLatencyStatistics
{
    public string Provider { get; set; } = string.Empty;
    public double MinMs { get; set; }
    public double MaxMs { get; set; }
    public double MeanMs { get; set; }
    public double MedianMs { get; set; }
    public double StdDevMs { get; set; }
    public double P50Ms { get; set; }
    public double P90Ms { get; set; }
    public double P95Ms { get; set; }
    public double P99Ms { get; set; }
    public long SampleCount { get; set; }
}



/// <summary>
/// Selects symbols, dates, anomaly categories, and the detection sensitivity threshold.
/// </summary>
public sealed class AnomalyDetectionOptions
{
    public string? Symbol { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public List<string>? AnomalyTypes { get; set; }
    public double SensitivityThreshold { get; set; } = 0.95;
}

/// <summary>
/// Reports detection success, timestamped anomalies, and aggregate anomaly counts.
/// </summary>
public sealed class AnomalyDetectionResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime AnalysisTime { get; set; }
    public int TotalAnomalies { get; set; }
    public List<DataAnomaly> Anomalies { get; set; } = new();
    public AnomalySummary? Summary { get; set; }
}

/// <summary>
/// Carries API anomaly-analysis time, detected anomalies, and their summary.
/// </summary>
public sealed class AnomalyDetectionResponse
{
    public DateTime AnalysisTime { get; set; }
    public int TotalAnomalies { get; set; }
    public List<DataAnomaly>? Anomalies { get; set; }
    public AnomalySummary? Summary { get; set; }
}

/// <summary>
/// Describes an anomalous symbol observation with severity, expected value, and confidence.
/// </summary>
public sealed class DataAnomaly
{
    public string Symbol { get; set; } = string.Empty;
    public string AnomalyType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public double ConfidenceScore { get; set; }
}

/// <summary>
/// Counts detected price, volume, timestamp, sequence, and stale-quote anomalies.
/// </summary>
public sealed class AnomalySummary
{
    public int NegativePrices { get; set; }
    public int FutureTimestamps { get; set; }
    public int SequenceGaps { get; set; }
    public int OutOfOrder { get; set; }
    public int PriceSpikes { get; set; }
    public int VolumeSpikes { get; set; }
    public int StaleQuotes { get; set; }
}



/// <summary>
/// Selects the symbol, reporting period, and inclusion of detailed quality findings.
/// </summary>
public sealed class DataQualityReportOptions
{
    public string? Symbol { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public bool IncludeDetails { get; set; } = true;
}

/// <summary>
/// Reports quality-analysis success, overall grade, metrics, symbol findings, and recommendations.
/// </summary>
public sealed class DataQualityReportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime ReportTime { get; set; }
    public double OverallScore { get; set; }
    public string Grade { get; set; } = string.Empty;
    public AnalyticsQualityMetrics? Metrics { get; set; }
    public List<AnalyticsSymbolQualityReport> SymbolReports { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Carries API quality scores, grades, symbol reports, and recommendations.
/// </summary>
public sealed class DataQualityReportResponse
{
    public DateTime ReportTime { get; set; }
    public double OverallScore { get; set; }
    public string Grade { get; set; } = string.Empty;
    public AnalyticsQualityMetrics? Metrics { get; set; }
    public List<AnalyticsSymbolQualityReport>? SymbolReports { get; set; }
    public List<string>? Recommendations { get; set; }
}

/// <summary>
/// Summarizes quality dimensions and the numbers of valid and invalid records.
/// </summary>
public sealed class AnalyticsQualityMetrics
{
    public double CompletenessScore { get; set; }
    public double IntegrityScore { get; set; }
    public double TimelinessScore { get; set; }
    public double AccuracyScore { get; set; }
    public double ConsistencyScore { get; set; }
    public long TotalRecords { get; set; }
    public long ValidRecords { get; set; }
    public long InvalidRecords { get; set; }
}

/// <summary>
/// Summarizes a symbol's quality grade, completeness, integrity, gaps, and issues.
/// </summary>
public sealed class AnalyticsSymbolQualityReport
{
    public string Symbol { get; set; } = string.Empty;
    public double OverallScore { get; set; }
    public string Grade { get; set; } = string.Empty;
    public double CompletenessScore { get; set; }
    public double IntegrityScore { get; set; }
    public int SequenceGaps { get; set; }
    public int Anomalies { get; set; }
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Selects the symbol and date range for trading-day completeness analysis.
/// </summary>
public sealed class CompletenessAnalysisOptions
{
    public string? Symbol { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

/// <summary>
/// Reports completeness-query success, covered trading days, and per-symbol coverage.
/// </summary>
public sealed class CompletenessAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public double OverallCompleteness { get; set; }
    public int TradingDaysCovered { get; set; }
    public int TradingDaysExpected { get; set; }
    public List<SymbolCompleteness> SymbolCompleteness { get; set; } = new();
}

/// <summary>
/// Carries API overall trading-day coverage and per-symbol completeness.
/// </summary>
public sealed class CompletenessAnalysisResponse
{
    public double OverallCompleteness { get; set; }
    public int TradingDaysCovered { get; set; }
    public int TradingDaysExpected { get; set; }
    public List<SymbolCompleteness>? SymbolCompleteness { get; set; }
}

/// <summary>
/// Describes a symbol's expected and covered days, missing dates, and daily event counts.
/// </summary>
public sealed class SymbolCompleteness
{
    public string Symbol { get; set; } = string.Empty;
    public double CompletenessPercent { get; set; }
    public int DaysCovered { get; set; }
    public int DaysExpected { get; set; }
    public List<DateOnly> MissingDays { get; set; } = new();

    // Alias properties for compatibility
    public double Score
    {
        get => CompletenessPercent;
        set => CompletenessPercent = value;
    }

    public int DaysWithData
    {
        get => DaysCovered;
        set => DaysCovered = value;
    }

    public int ExpectedDays
    {
        get => DaysExpected;
        set => DaysExpected = value;
    }

    // Additional properties
    public long RecordCount { get; set; }
    public long TotalEvents { get; set; }

    /// <summary>
    /// Per-day event breakdown showing trade, quote, depth, and bar event counts for each date.
    /// See DayEventCount class in DataCompletenessService for structure.
    /// </summary>
    public List<DayEventCount> DayDetails { get; set; } = new();
}



/// <summary>
/// Selects the provider, period, and time-bucket interval for throughput analysis.
/// </summary>
public sealed class ThroughputAnalysisOptions
{
    public string? Provider { get; set; }
    public TimeSpan? Period { get; set; }
    public int IntervalSeconds { get; set; } = 60;
}

/// <summary>
/// Reports throughput-query success, current and peak rates, and the event timeline.
/// </summary>
public sealed class ThroughputAnalysisResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public double CurrentThroughput { get; set; }
    public double AverageThroughput { get; set; }
    public double PeakThroughput { get; set; }
    public long TotalEventsProcessed { get; set; }
    public List<ThroughputDataPoint> Timeline { get; set; } = new();
}

/// <summary>
/// Carries API throughput rates, total processed events, and time-bucket measurements.
/// </summary>
public sealed class ThroughputAnalysisResponse
{
    public double CurrentThroughput { get; set; }
    public double AverageThroughput { get; set; }
    public double PeakThroughput { get; set; }
    public long TotalEventsProcessed { get; set; }
    public List<ThroughputDataPoint>? Timeline { get; set; }
}

/// <summary>
/// Records the event rate and total events at one point in a throughput timeline.
/// </summary>
public sealed class ThroughputDataPoint
{
    public DateTime Timestamp { get; set; }
    public double EventsPerSecond { get; set; }
    public long TotalEvents { get; set; }
}



/// <summary>
/// Reports query success and provider request-budget status.
/// </summary>
public sealed class RateLimitStatusResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderRateLimitStatus> Providers { get; set; } = new();
}

/// <summary>
/// Carries API request-budget status grouped by provider.
/// </summary>
public sealed class RateLimitStatusResponse
{
    public List<ProviderRateLimitStatus>? Providers { get; set; }
}

/// <summary>
/// Describes a provider's minute request usage, remaining capacity, reset time, and throttling state.
/// </summary>
public sealed class ProviderRateLimitStatus
{
    public string Provider { get; set; } = string.Empty;
    public int RequestsPerMinute { get; set; }
    public int RequestsUsed { get; set; }
    public int RequestsRemaining { get; set; }
    public DateTime? ResetTime { get; set; }
    public double UsagePercent { get; set; }
    public bool IsThrottled { get; set; }
    public string Status { get; set; } = string.Empty;
}



/// <summary>
/// Reports symbol-query success and the symbols available for analytics.
/// </summary>
public sealed class AnalyticsSymbolsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<AnalyticsSymbolInfo> Symbols { get; set; } = new();
}

/// <summary>
/// Carries API symbols available for analytics.
/// </summary>
public sealed class AnalyticsSymbolsResponse
{
    public List<AnalyticsSymbolInfo>? Symbols { get; set; }
}

/// <summary>
/// Identifies an analytics symbol and its optional display name.
/// </summary>
public sealed class AnalyticsSymbolInfo
{
    public string Symbol { get; set; } = string.Empty;
    public string? Name { get; set; }
}

