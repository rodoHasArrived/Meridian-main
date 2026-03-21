using System;
using System.Collections.Generic;

namespace Meridian.Ui.Services.DataQuality;

public sealed class QualityDashboardResponse
{
    public QualityDashboardMetricsResponse? RealTimeMetrics { get; set; }
    public QualityCompletenessStatsResponse? CompletenessStats { get; set; }
    public QualityGapStatsResponse? GapStats { get; set; }
    public QualitySequenceStatsResponse? SequenceStats { get; set; }
    public QualityAnomalyStatsResponse? AnomalyStats { get; set; }
    public List<QualityAnomalyResponse> RecentAnomalies { get; set; } = new();
}

public sealed class QualityDashboardMetricsResponse
{
    public double OverallHealthScore { get; set; }
    public double AverageLatencyMs { get; set; }
    public List<QualitySymbolHealthResponse> SymbolHealth { get; set; } = new();
}

public sealed class QualitySymbolHealthResponse
{
    public string Symbol { get; set; } = string.Empty;
    public int State { get; set; }
    public double Score { get; set; }
    public DateTimeOffset LastEvent { get; set; }
    public List<string> ActiveIssues { get; set; } = new();
}

public sealed class QualityCompletenessStatsResponse
{
    public double AverageScore { get; set; }
    public Dictionary<string, int> GradeDistribution { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset CalculatedAt { get; set; }
}

public sealed class QualityGapStatsResponse
{
    public int TotalGaps { get; set; }
}

public sealed class QualitySequenceStatsResponse
{
    public long TotalErrors { get; set; }
}

public sealed class QualityAnomalyStatsResponse
{
    public int UnacknowledgedCount { get; set; }
    public long TotalAnomalies { get; set; }
    public Dictionary<string, int> AnomaliesByType { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class QualityGapResponse
{
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset GapStart { get; set; }
    public DateTimeOffset GapEnd { get; set; }
    public long EstimatedMissedEvents { get; set; }
}

public sealed class QualityAnomalyResponse
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int Type { get; set; }
    public int Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsAcknowledged { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
}

public sealed class QualityLatencyStatisticsResponse
{
    public double GlobalMeanMs { get; set; }
    public double GlobalP50Ms { get; set; }
    public double GlobalP90Ms { get; set; }
    public double GlobalP95Ms { get; set; }
    public double GlobalP99Ms { get; set; }
}

public sealed class QualityProviderComparisonResponse
{
    public string Symbol { get; set; } = string.Empty;
    public List<QualityProviderComparisonEntryResponse> Providers { get; set; } = new();
}

public sealed class QualityProviderComparisonEntryResponse
{
    public string Provider { get; set; } = string.Empty;
    public double Completeness { get; set; }
    public double AverageLatencyMs { get; set; }
    public string LastDataAge { get; set; } = string.Empty;
}

public sealed class QualityActionResponse
{
    public bool Acknowledged { get; set; }
}

public sealed class DataQualityPresentationSnapshot
{
    public double OverallScore { get; init; }
    public string OverallScoreText { get; init; } = "--";
    public string OverallGradeText { get; init; } = "--";
    public string StatusText { get; init; } = "--";
    public string ScoreTone { get; init; } = DataQualityVisualTones.Success;
    public string LastUpdateText { get; init; } = "Last updated: --";
    public string LatencyText { get; init; } = "--";
    public string CompletenessText { get; init; } = "--";
    public string HealthyFilesText { get; init; } = "--";
    public string WarningFilesText { get; init; } = "--";
    public string CriticalFilesText { get; init; } = "--";
    public string GapsCountText { get; init; } = "--";
    public string GapsTone { get; init; } = DataQualityVisualTones.Success;
    public string ErrorsCountText { get; init; } = "--";
    public string ErrorsTone { get; init; } = DataQualityVisualTones.Success;
    public string UnacknowledgedText { get; init; } = "--";
    public string TotalActiveAlertsText { get; init; } = "--";
    public string AlertCountBadgeText { get; init; } = "0";
    public bool IsAlertCountBadgeVisible { get; init; }
    public string CrossedMarketCount { get; init; } = "--";
    public string StaleDataCount { get; init; } = "--";
    public string InvalidPriceCount { get; init; } = "--";
    public string InvalidVolumeCount { get; init; } = "--";
    public string MissingDataCount { get; init; } = "--";
    public string LastCheckTimeText { get; init; } = "--";
    public string NextCheckText { get; init; } = "--";
    public double CheckProgressValue { get; init; }
    public string P50Text { get; init; } = "--";
    public string P75Text { get; init; } = "--";
    public string P90Text { get; init; } = "--";
    public string P95Text { get; init; } = "--";
    public string P99Text { get; init; } = "--";
    public IReadOnlyList<DataQualitySymbolPresentation> Symbols { get; init; } = Array.Empty<DataQualitySymbolPresentation>();
    public IReadOnlyList<DataQualityGapPresentation> Gaps { get; init; } = Array.Empty<DataQualityGapPresentation>();
    public IReadOnlyList<DataQualityAlertPresentation> Alerts { get; init; } = Array.Empty<DataQualityAlertPresentation>();
    public IReadOnlyList<DataQualityAnomalyPresentation> Anomalies { get; init; } = Array.Empty<DataQualityAnomalyPresentation>();
}

public sealed class DataQualitySymbolPresentation
{
    public string Symbol { get; init; } = string.Empty;
    public double Score { get; init; }
    public string ScoreFormatted { get; init; } = string.Empty;
    public string Grade { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Issues { get; init; } = string.Empty;
    public DateTimeOffset LastUpdate { get; init; }
    public string LastUpdateFormatted { get; init; } = string.Empty;
}

public sealed class DataQualityGapPresentation
{
    public string GapId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
}

public sealed class DataQualityAlertPresentation
{
    public string Id { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string AlertType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string SeverityTone { get; init; } = DataQualityVisualTones.Info;
}

public sealed class DataQualityAnomalyPresentation
{
    public string Symbol { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Timestamp { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string SeverityTone { get; init; } = DataQualityVisualTones.Info;
}

public sealed class DataQualityProviderComparisonPresentation
{
    public string Symbol { get; init; } = string.Empty;
    public IReadOnlyList<DataQualityProviderComparisonItem> Providers { get; init; } = Array.Empty<DataQualityProviderComparisonItem>();
}

public sealed class DataQualityProviderComparisonItem
{
    public string Name { get; init; } = string.Empty;
    public string CompletenessText { get; init; } = "--";
    public string LatencyText { get; init; } = "--";
    public string FreshnessText { get; init; } = "--";
    public string Status { get; init; } = string.Empty;
}

public sealed class DataQualitySymbolDrilldownPresentation
{
    public string HeaderText { get; init; } = string.Empty;
    public string ScoreText { get; init; } = string.Empty;
    public string ScoreTone { get; init; } = DataQualityVisualTones.Success;
    public string CompletenessText { get; init; } = "--";
    public string GapsText { get; init; } = "--";
    public string ErrorsText { get; init; } = "--";
    public string LatencyText { get; init; } = "--";
    public IReadOnlyList<DataQualityHeatmapCellPresentation> HeatmapCells { get; init; } = Array.Empty<DataQualityHeatmapCellPresentation>();
    public IReadOnlyList<DataQualityDrilldownIssuePresentation> Issues { get; init; } = Array.Empty<DataQualityDrilldownIssuePresentation>();
}

public sealed class DataQualityHeatmapCellPresentation
{
    public string Label { get; init; } = string.Empty;
    public int Score { get; init; }
    public string Tone { get; init; } = DataQualityVisualTones.Success;
    public string Tooltip { get; init; } = string.Empty;
}

public sealed class DataQualityDrilldownIssuePresentation
{
    public string Description { get; init; } = string.Empty;
    public string Timestamp { get; init; } = string.Empty;
    public string Tone { get; init; } = DataQualityVisualTones.Info;
}

public static class DataQualityVisualTones
{
    public const string Success = "Success";
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Muted = "Muted";
}
