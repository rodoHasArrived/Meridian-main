using System;
using System.Collections.Generic;
using Meridian.Contracts.Api.Quality;

namespace Meridian.Ui.Services.DataQuality;

public sealed class DataQualityPresentationSnapshot
{
    public bool IsAvailable { get; init; }
    public bool IsPartial { get; init; }
    public string DashboardVersion { get; init; } = string.Empty;
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
    public string ExpectedEventsText { get; init; } = "--";
    public int GapCount { get; init; }
    public int AnomalyCount { get; init; }
    public IReadOnlyList<QualityComponentResponse> Components { get; init; } = Array.Empty<QualityComponentResponse>();
    public IReadOnlyList<QualityProviderFreshnessResponse> ProviderFreshness { get; init; } = Array.Empty<QualityProviderFreshnessResponse>();
    public IReadOnlyList<string> IssueDetails { get; init; } = Array.Empty<string>();
}

public sealed class DataQualityGapPresentation
{
    public string GapId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Duration { get; init; } = string.Empty;
    public string DashboardVersion { get; init; } = string.Empty;
    public string? Provider { get; init; }
    public bool CanRepair { get; init; }
    public string? DisabledReason { get; init; }
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
