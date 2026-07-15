using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api.Quality;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Maps the canonical composite quality contract into desktop presentation state without seeded
/// or randomly-derived fallback telemetry.
/// </summary>
public sealed class DataQualityPresentationService : IDataQualityPresentationService
{
    private static readonly string[] AnomalySeverityNames = { "Info", "Warning", "Error", "Critical" };
    private static readonly string[] AnomalyTypeNames =
    {
        "PriceSpike", "PriceDrop", "VolumeSpike", "VolumeDrop", "SpreadWide", "StaleData",
        "RapidPriceChange", "AbnormalVolatility", "MissingData", "DuplicateData",
        "CrossedMarket", "InvalidPrice", "InvalidVolume"
    };

    private readonly IDataQualityApiClient _apiClient;

    public DataQualityPresentationService(IDataQualityApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<DataQualityPresentationSnapshot> GetSnapshotAsync(string timeRange, CancellationToken ct = default)
    {
        _ = timeRange;
        var dashboard = await _apiClient.GetDashboardAsync(ct).ConfigureAwait(false);
        return BuildSnapshot(dashboard);
    }

    public async Task<DataQualityProviderComparisonPresentation> GetProviderComparisonAsync(
        string symbol,
        CancellationToken ct = default)
    {
        var response = await _apiClient.GetProviderComparisonAsync(symbol, ct).ConfigureAwait(false);
        if (response?.Providers is not { Count: > 0 })
        {
            return new DataQualityProviderComparisonPresentation
            {
                Symbol = symbol,
                Providers = Array.Empty<DataQualityProviderComparisonItem>()
            };
        }

        return new DataQualityProviderComparisonPresentation
        {
            Symbol = response.Symbol,
            Providers = response.Providers.Select(provider => new DataQualityProviderComparisonItem
            {
                Name = provider.Provider,
                CompletenessText = $"{provider.CompletenessScore * 100:F1}%",
                LatencyText = provider.Latency > 0 ? $"{provider.Latency:F0}ms" : "--",
                FreshnessText = provider.LastEvent > DateTimeOffset.MinValue
                    ? FormatRelativeTime(provider.LastEvent.UtcDateTime)
                    : "--",
                Status = provider.EventCount == 0
                    ? "Unavailable"
                    : provider.CompletenessScore >= 0.8 ? "Good" : provider.CompletenessScore >= 0.6 ? "Fair" : "Poor"
            }).ToArray()
        };
    }

    public DataQualitySymbolDrilldownPresentation BuildSymbolDrilldown(DataQualitySymbolPresentation symbol)
    {
        var stored = FindComponent(symbol.Components, "StoredCompleteness");
        var streaming = FindComponent(symbol.Components, "StreamingFreshness");
        var heatmap = symbol.Components.Select(component => new DataQualityHeatmapCellPresentation
        {
            Label = component.Kind switch
            {
                "StreamingFreshness" => "Stream",
                "StoredCompleteness" => "Stored",
                "AdapterGapIntegrity" => "Adapter",
                _ => component.Label
            },
            Score = component.Score.HasValue ? (int)Math.Round(component.Score.Value) : 0,
            Tone = component.Score.HasValue ? ScoreToTone(component.Score.Value) : DataQualityVisualTones.Muted,
            Tooltip = $"{component.Label}: {(component.Score.HasValue ? $"{component.Score:F1}" : "unavailable")} — {component.Detail}"
        }).ToArray();
        var issues = symbol.IssueDetails.Select(issue => new DataQualityDrilldownIssuePresentation
        {
            Description = issue,
            Timestamp = "Current snapshot",
            Tone = symbol.Status switch
            {
                "Red" => DataQualityVisualTones.Error,
                "Amber" => DataQualityVisualTones.Warning,
                _ => DataQualityVisualTones.Info
            }
        }).ToArray();

        return new DataQualitySymbolDrilldownPresentation
        {
            HeaderText = $"{symbol.Symbol} — Quality Drilldown",
            ScoreText = symbol.ScoreFormatted,
            ScoreTone = ScoreToTone(symbol.Score),
            CompletenessText = FormatComponentScore(stored),
            GapsText = symbol.GapCount.ToString(),
            ErrorsText = symbol.AnomalyCount.ToString(),
            LatencyText = FormatComponentScore(streaming),
            HeatmapCells = heatmap,
            Issues = issues
        };
    }

    private static DataQualityPresentationSnapshot BuildSnapshot(QualityDashboardResponse? dashboard)
    {
        if (dashboard?.Composite is null)
        {
            return new DataQualityPresentationSnapshot
            {
                IsAvailable = false,
                IsPartial = true,
                OverallScore = 0,
                OverallScoreText = "--",
                OverallGradeText = "--",
                StatusText = "Unavailable",
                ScoreTone = DataQualityVisualTones.Muted,
                LastUpdateText = dashboard is null ? "Last updated: unavailable" : $"Last updated: {dashboard.Timestamp:HH:mm:ss}",
                Symbols = Array.Empty<DataQualitySymbolPresentation>(),
                Gaps = Array.Empty<DataQualityGapPresentation>(),
                Alerts = dashboard?.RecentAnomalies.Select(MapAlert).ToArray() ?? [],
                Anomalies = dashboard?.RecentAnomalies.Select(MapAnomaly).ToArray() ?? []
            };
        }

        var composite = dashboard.Composite;
        var overallScore = composite.CompositeScore;
        var stored = FindComponent(composite.Components, "StoredCompleteness");
        var streaming = FindComponent(composite.Components, "StreamingFreshness");
        var symbols = composite.Symbols.Select(MapSymbol).ToArray();
        var greenCount = composite.Symbols.Count(static symbol => symbol.Status == "Green");
        var amberCount = composite.Symbols.Count(static symbol => symbol.Status == "Amber" || symbol.Status == "Unavailable");
        var redCount = composite.Symbols.Count(static symbol => symbol.Status == "Red");
        var recentAnomalies = dashboard.RecentAnomalies;
        var unacknowledged = recentAnomalies.Count(static anomaly => !anomaly.IsAcknowledged);

        return new DataQualityPresentationSnapshot
        {
            IsAvailable = overallScore.HasValue,
            IsPartial = composite.IsPartial,
            DashboardVersion = composite.Version,
            OverallScore = overallScore ?? 0,
            OverallScoreText = overallScore.HasValue ? $"{overallScore.Value:F1}" : "--",
            OverallGradeText = overallScore.HasValue ? GetGrade(overallScore.Value) : "--",
            StatusText = composite.Status,
            ScoreTone = StatusToTone(composite.Status),
            LastUpdateText = $"Last updated: {composite.ObservedAt:HH:mm:ss}",
            LatencyText = dashboard.LatencyStats.GlobalMeanMs > 0 ? $"{dashboard.LatencyStats.GlobalMeanMs:F0}ms" : "--",
            CompletenessText = FormatComponentScore(stored),
            HealthyFilesText = greenCount.ToString("N0"),
            WarningFilesText = amberCount.ToString("N0"),
            CriticalFilesText = redCount.ToString("N0"),
            GapsCountText = composite.OpenGaps.Count.ToString(),
            GapsTone = composite.OpenGaps.Count == 0 ? DataQualityVisualTones.Success : DataQualityVisualTones.Warning,
            ErrorsCountText = dashboard.SequenceStats.TotalErrors.ToString("N0"),
            ErrorsTone = dashboard.SequenceStats.TotalErrors == 0 ? DataQualityVisualTones.Success : DataQualityVisualTones.Error,
            UnacknowledgedText = unacknowledged.ToString(),
            TotalActiveAlertsText = composite.AnomalyCount.ToString("N0"),
            AlertCountBadgeText = unacknowledged.ToString(),
            IsAlertCountBadgeVisible = unacknowledged > 0,
            CrossedMarketCount = CountAnomalies(recentAnomalies, 10).ToString(),
            StaleDataCount = CountAnomalies(recentAnomalies, 5).ToString(),
            InvalidPriceCount = CountAnomalies(recentAnomalies, 11).ToString(),
            InvalidVolumeCount = CountAnomalies(recentAnomalies, 12).ToString(),
            MissingDataCount = CountAnomalies(recentAnomalies, 8).ToString(),
            LastCheckTimeText = FormatRelativeTime(composite.ObservedAt.UtcDateTime),
            NextCheckText = "On refresh",
            CheckProgressValue = 0,
            P50Text = dashboard.LatencyStats.GlobalP50Ms > 0 ? $"{dashboard.LatencyStats.GlobalP50Ms:F0}ms" : "--",
            P75Text = dashboard.LatencyStats.GlobalMeanMs > 0 ? $"{dashboard.LatencyStats.GlobalMeanMs:F0}ms" : "--",
            P90Text = dashboard.LatencyStats.GlobalP90Ms > 0 ? $"{dashboard.LatencyStats.GlobalP90Ms:F0}ms" : "--",
            P95Text = "--",
            P99Text = dashboard.LatencyStats.GlobalP99Ms > 0 ? $"{dashboard.LatencyStats.GlobalP99Ms:F0}ms" : "--",
            Symbols = symbols,
            Gaps = composite.OpenGaps.Select(gap => MapGap(gap, composite.Version)).ToArray(),
            Alerts = recentAnomalies.Where(static anomaly => !anomaly.IsAcknowledged).Select(MapAlert).ToArray(),
            Anomalies = recentAnomalies.Select(MapAnomaly).ToArray()
        };
    }

    private static DataQualitySymbolPresentation MapSymbol(QualityCompositeSymbolResponse response)
    {
        var score = response.CompositeScore ?? 0;
        var lastObserved = response.Components
            .Where(static component => component.ObservedAt.HasValue)
            .Select(static component => component.ObservedAt!.Value)
            .DefaultIfEmpty()
            .Max();
        return new DataQualitySymbolPresentation
        {
            Symbol = response.Symbol,
            Score = score,
            ScoreFormatted = response.CompositeScore.HasValue ? $"{score:F1}" : "--",
            Grade = response.CompositeScore.HasValue ? GetGrade(score) : "--",
            Status = response.Status,
            Issues = response.Issues.Count == 0 ? "—" : string.Join(", ", response.Issues),
            LastUpdate = lastObserved,
            LastUpdateFormatted = lastObserved == default ? "--" : FormatRelativeTime(lastObserved.UtcDateTime),
            ExpectedEventsText = response.ExpectedEvents.HasValue && response.ObservedEvents.HasValue
                ? $"{response.ObservedEvents:N0} / {response.ExpectedEvents:N0} expected"
                : "Expected-session counts unavailable",
            GapCount = response.OpenGaps.Count,
            AnomalyCount = response.AnomalyCount,
            Components = response.Components,
            ProviderFreshness = response.ProviderFreshness,
            IssueDetails = response.Issues
        };
    }

    private static DataQualityGapPresentation MapGap(QualityCompositeGapResponse response, string dashboardVersion)
    {
        var duration = response.To - response.From;
        var durationText = duration.TotalDays >= 1 ? $"{duration.TotalDays:F0} days"
            : duration.TotalHours >= 1 ? $"{duration.TotalHours:F1} hours"
            : $"{duration.TotalMinutes:F0} mins";
        return new DataQualityGapPresentation
        {
            GapId = response.GapId,
            Symbol = response.Symbol,
            Description = $"{response.EventType}: {response.EstimatedMissingEvents:N0} estimated missing events ({response.Provider ?? "default provider"})",
            Duration = durationText,
            DashboardVersion = dashboardVersion,
            Provider = response.Provider,
            CanRepair = response.CanBackfill,
            DisabledReason = response.DisabledReason
        };
    }

    private static DataQualityAlertPresentation MapAlert(QualityAnomalyResponse response)
    {
        var severity = ReadEnumString(response.Severity, AnomalySeverityNames, "Warning");
        return new DataQualityAlertPresentation
        {
            Id = response.Id,
            Symbol = response.Symbol,
            AlertType = ReadEnumString(response.Type, AnomalyTypeNames, $"Type {response.Type}"),
            Message = response.Description,
            Severity = severity,
            SeverityTone = SeverityToTone(severity)
        };
    }

    private static DataQualityAnomalyPresentation MapAnomaly(QualityAnomalyResponse response)
    {
        var severity = ReadEnumString(response.Severity, AnomalySeverityNames, "Warning");
        return new DataQualityAnomalyPresentation
        {
            Symbol = response.Symbol,
            Description = response.Description,
            Timestamp = response.DetectedAt.ToString("MMM d HH:mm"),
            Type = ReadEnumString(response.Type, AnomalyTypeNames, $"Type {response.Type}"),
            SeverityTone = SeverityToTone(severity)
        };
    }

    private static QualityComponentResponse? FindComponent(
        IReadOnlyList<QualityComponentResponse> components,
        string kind) => components.FirstOrDefault(component => component.Kind == kind);

    private static string FormatComponentScore(QualityComponentResponse? component) =>
        component?.Score is double score ? $"{score:F1}" : "--";

    private static int CountAnomalies(IReadOnlyList<QualityAnomalyResponse> anomalies, byte type) =>
        anomalies.Count(anomaly => anomaly.Type == type);

    private static string ReadEnumString(int value, IReadOnlyList<string> names, string fallback) =>
        value >= 0 && value < names.Count ? names[value] : fallback;

    private static string FormatRelativeTime(DateTime time)
    {
        var span = DateTime.UtcNow - time;
        return span.TotalSeconds < 60 ? "Just now"
            : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes} minutes ago"
            : span.TotalHours < 24 ? $"{(int)span.TotalHours} hours ago"
            : $"{(int)span.TotalDays} days ago";
    }

    private static string SeverityToTone(string severity) => severity.ToLowerInvariant() switch
    {
        "critical" or "error" => DataQualityVisualTones.Error,
        "warning" => DataQualityVisualTones.Warning,
        _ => DataQualityVisualTones.Info
    };

    private static string StatusToTone(string status) => status switch
    {
        "Green" => DataQualityVisualTones.Success,
        "Red" => DataQualityVisualTones.Error,
        "Unavailable" => DataQualityVisualTones.Muted,
        _ => DataQualityVisualTones.Warning
    };

    private static string ScoreToTone(double score) => score switch
    {
        >= 80 => DataQualityVisualTones.Success,
        >= 60 => DataQualityVisualTones.Warning,
        _ => DataQualityVisualTones.Error
    };

    private static string GetGrade(double score) => score switch
    {
        >= 95 => "A+",
        >= 90 => "A",
        >= 85 => "A-",
        >= 80 => "B+",
        >= 75 => "B",
        >= 70 => "B-",
        >= 65 => "C+",
        >= 60 => "C",
        >= 55 => "C-",
        >= 50 => "D",
        _ => "F"
    };
}
