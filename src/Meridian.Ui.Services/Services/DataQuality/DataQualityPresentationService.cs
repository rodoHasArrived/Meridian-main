using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.DataQuality;

/// <summary>
/// Maps raw quality responses into UI-facing snapshot models while preserving demo fallbacks.
/// </summary>
public sealed class DataQualityPresentationService : IDataQualityPresentationService
{
    private static readonly string[] HealthStateNames = { "Healthy", "Degraded", "Unhealthy", "Stale", "Unknown" };
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
        var dashboardTask = _apiClient.GetDashboardAsync(ct);
        var gapsTask = _apiClient.GetGapsAsync(GetRangeCount(timeRange, 100, 250, 500, 1000), ct);
        var anomaliesTask = _apiClient.GetAnomaliesAsync(GetRangeCount(timeRange, 50, 100, 200, 400), ct);
        var latencyTask = _apiClient.GetLatencyStatisticsAsync(ct);

        await Task.WhenAll(dashboardTask, gapsTask, anomaliesTask, latencyTask).ConfigureAwait(false);

        var dashboard = await dashboardTask.ConfigureAwait(false);
        var gaps = await gapsTask.ConfigureAwait(false);
        var anomalies = await anomaliesTask.ConfigureAwait(false);
        var latency = await latencyTask.ConfigureAwait(false);

        return BuildSnapshot(dashboard, gaps, anomalies, latency);
    }

    public async Task<DataQualityProviderComparisonPresentation> GetProviderComparisonAsync(string symbol, CancellationToken ct = default)
    {
        var response = await _apiClient.GetProviderComparisonAsync(symbol, ct).ConfigureAwait(false);
        if (response?.Providers is { Count: > 0 })
        {
            return new DataQualityProviderComparisonPresentation
            {
                Symbol = response.Symbol,
                Providers = response.Providers
                    .Select(provider => new DataQualityProviderComparisonItem
                    {
                        Name = provider.Provider,
                        CompletenessText = $"{provider.Completeness * 100:F1}%",
                        LatencyText = provider.AverageLatencyMs > 0 ? $"{provider.AverageLatencyMs:F0}ms" : "--",
                        FreshnessText = string.IsNullOrWhiteSpace(provider.LastDataAge) ? "--" : provider.LastDataAge,
                        Status = provider.Completeness >= 0.95 ? "Good" : provider.Completeness >= 0.80 ? "Fair" : "Poor"
                    })
                    .ToArray()
            };
        }

        return new DataQualityProviderComparisonPresentation
        {
            Symbol = symbol,
            Providers = new[]
            {
                new DataQualityProviderComparisonItem { Name = "Alpaca", CompletenessText = "99.2%", LatencyText = "8ms", FreshnessText = "2s ago", Status = "Good" },
                new DataQualityProviderComparisonItem { Name = "Polygon", CompletenessText = "97.8%", LatencyText = "12ms", FreshnessText = "5s ago", Status = "Good" },
                new DataQualityProviderComparisonItem { Name = "Tiingo", CompletenessText = "94.5%", LatencyText = "45ms", FreshnessText = "1m ago", Status = "Fair" },
                new DataQualityProviderComparisonItem { Name = "Yahoo Finance", CompletenessText = "88.2%", LatencyText = "120ms", FreshnessText = "15m ago", Status = "Fair" }
            }
        };
    }

    public DataQualitySymbolDrilldownPresentation BuildSymbolDrilldown(DataQualitySymbolPresentation symbol)
    {
        var random = new Random(symbol.Symbol.GetHashCode(StringComparison.Ordinal));

        var heatmap = Enumerable.Range(0, 7)
            .Select(index =>
            {
                var day = DateTime.Today.AddDays(-6 + index);
                var dayScore = random.Next(60, 100);
                return new DataQualityHeatmapCellPresentation
                {
                    Label = day.ToString("ddd"),
                    Score = dayScore,
                    Tone = dayScore >= 95 ? DataQualityVisualTones.Success
                        : dayScore >= 85 ? DataQualityVisualTones.Info
                        : dayScore >= 70 ? DataQualityVisualTones.Warning
                        : DataQualityVisualTones.Error,
                    Tooltip = $"{day:MMM dd}: Score {dayScore}%"
                };
            })
            .ToArray();

        var issueTypes = new[]
        {
            "Sequence gap detected",
            "Stale data (>5s delay)",
            "Price spike anomaly",
            "Missing quotes window",
            "Volume irregularity"
        };

        var issues = Enumerable.Range(0, random.Next(0, 4))
            .Select(_ =>
            {
                var severity = random.Next(0, 3);
                return new DataQualityDrilldownIssuePresentation
                {
                    Description = issueTypes[random.Next(issueTypes.Length)],
                    Timestamp = DateTime.Now.AddMinutes(-random.Next(10, 2880)).ToString("MMM dd HH:mm"),
                    Tone = severity switch
                    {
                        0 => DataQualityVisualTones.Error,
                        1 => DataQualityVisualTones.Warning,
                        _ => DataQualityVisualTones.Info
                    }
                };
            })
            .ToArray();

        return new DataQualitySymbolDrilldownPresentation
        {
            HeaderText = $"{symbol.Symbol} — Quality Drilldown",
            ScoreText = symbol.ScoreFormatted,
            ScoreTone = ScoreToTone(symbol.Score),
            CompletenessText = $"{random.Next(85, 100)}%",
            GapsText = random.Next(0, 5).ToString(),
            ErrorsText = random.Next(0, 3).ToString(),
            LatencyText = $"{random.Next(5, 120)}ms",
            HeatmapCells = heatmap,
            Issues = issues
        };
    }

    private static DataQualityPresentationSnapshot BuildSnapshot(
        QualityDashboardResponse? dashboard,
        IReadOnlyList<QualityGapResponse> gaps,
        IReadOnlyList<QualityAnomalyResponse> anomalies,
        QualityLatencyStatisticsResponse? latency)
    {
        if (dashboard?.RealTimeMetrics == null)
        {
            return BuildDemoSnapshot();
        }

        var metrics = dashboard.RealTimeMetrics;
        var overallScore = Math.Clamp(metrics.OverallHealthScore * 100, 0, 100);
        var completenessAverage = dashboard.CompletenessStats?.AverageScore ?? 0;
        var gradeDistribution = dashboard.CompletenessStats?.GradeDistribution ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalGaps = dashboard.GapStats?.TotalGaps ?? 0;
        var totalErrors = dashboard.SequenceStats?.TotalErrors ?? 0;
        var anomalyStats = dashboard.AnomalyStats;
        var calculatedAt = dashboard.CompletenessStats?.CalculatedAt;

        return new DataQualityPresentationSnapshot
        {
            OverallScore = overallScore,
            OverallScoreText = overallScore > 0 ? $"{overallScore:F1}" : "--",
            OverallGradeText = GetGrade(overallScore),
            StatusText = GetStatus(overallScore),
            ScoreTone = ScoreToTone(overallScore),
            LastUpdateText = $"Last updated: {DateTime.Now:HH:mm:ss}",
            LatencyText = metrics.AverageLatencyMs > 0 ? $"{metrics.AverageLatencyMs:F0}ms" : "--",
            CompletenessText = completenessAverage > 0 ? $"{completenessAverage * 100:F1}%" : "--",
            HealthyFilesText = (GetGradeCount(gradeDistribution, "A") + GetGradeCount(gradeDistribution, "B")).ToString("N0"),
            WarningFilesText = GetGradeCount(gradeDistribution, "C").ToString("N0"),
            CriticalFilesText = (GetGradeCount(gradeDistribution, "D") + GetGradeCount(gradeDistribution, "F")).ToString("N0"),
            GapsCountText = totalGaps.ToString(),
            GapsTone = totalGaps == 0 ? DataQualityVisualTones.Success : totalGaps <= 5 ? DataQualityVisualTones.Warning : DataQualityVisualTones.Error,
            ErrorsCountText = totalErrors.ToString("N0"),
            ErrorsTone = totalErrors == 0 ? DataQualityVisualTones.Success : DataQualityVisualTones.Error,
            UnacknowledgedText = (anomalyStats?.UnacknowledgedCount ?? 0).ToString(),
            TotalActiveAlertsText = (anomalyStats?.TotalAnomalies ?? 0).ToString("N0"),
            AlertCountBadgeText = (anomalyStats?.UnacknowledgedCount ?? 0).ToString(),
            IsAlertCountBadgeVisible = (anomalyStats?.UnacknowledgedCount ?? 0) > 0,
            CrossedMarketCount = GetAnomalyCount(anomalyStats?.AnomaliesByType, "CrossedMarket").ToString(),
            StaleDataCount = GetAnomalyCount(anomalyStats?.AnomaliesByType, "StaleData").ToString(),
            InvalidPriceCount = GetAnomalyCount(anomalyStats?.AnomaliesByType, "InvalidPrice").ToString(),
            InvalidVolumeCount = GetAnomalyCount(anomalyStats?.AnomaliesByType, "InvalidVolume").ToString(),
            MissingDataCount = GetAnomalyCount(anomalyStats?.AnomaliesByType, "MissingData").ToString(),
            LastCheckTimeText = calculatedAt.HasValue ? FormatRelativeTime(calculatedAt.Value.UtcDateTime) : "--",
            NextCheckText = calculatedAt.HasValue ? "In 30 minutes" : "--",
            CheckProgressValue = calculatedAt.HasValue
                ? Math.Min(100, (DateTimeOffset.UtcNow - calculatedAt.Value).TotalMinutes / 30 * 100)
                : 0,
            P50Text = latency?.GlobalP50Ms > 0 ? $"{latency.GlobalP50Ms:F0}ms" : "--",
            P75Text = latency?.GlobalMeanMs > 0 ? $"{latency.GlobalMeanMs:F0}ms" : "--",
            P90Text = latency?.GlobalP90Ms > 0 ? $"{latency.GlobalP90Ms:F0}ms" : "--",
            P95Text = latency?.GlobalP95Ms > 0 ? $"{latency.GlobalP95Ms:F0}ms" : "--",
            P99Text = latency?.GlobalP99Ms > 0 ? $"{latency.GlobalP99Ms:F0}ms" : "--",
            Symbols = metrics.SymbolHealth.Select(MapSymbol).ToArray(),
            Gaps = gaps.Count > 0 ? gaps.Select(MapGap).ToArray() : BuildDemoGaps(),
            Alerts = dashboard.RecentAnomalies.Count > 0 ? dashboard.RecentAnomalies.Select(MapAlert).ToArray() : BuildDemoAlerts(),
            Anomalies = anomalies.Count > 0 ? anomalies.Select(MapAnomaly).ToArray() : Array.Empty<DataQualityAnomalyPresentation>()
        };
    }

    private static DataQualityPresentationSnapshot BuildDemoSnapshot()
    {
        return new DataQualityPresentationSnapshot
        {
            OverallScore = 98.5,
            OverallScoreText = "98.5",
            OverallGradeText = "A+",
            StatusText = "Excellent",
            ScoreTone = DataQualityVisualTones.Success,
            LastUpdateText = $"Last updated: {DateTime.Now:HH:mm:ss}",
            LatencyText = "12ms",
            CompletenessText = "98.5%",
            HealthyFilesText = "1,234",
            WarningFilesText = "12",
            CriticalFilesText = "0",
            GapsCountText = "3",
            GapsTone = DataQualityVisualTones.Warning,
            ErrorsCountText = "0",
            ErrorsTone = DataQualityVisualTones.Success,
            UnacknowledgedText = "2",
            TotalActiveAlertsText = "5",
            AlertCountBadgeText = "2",
            IsAlertCountBadgeVisible = true,
            CrossedMarketCount = "1",
            StaleDataCount = "2",
            InvalidPriceCount = "0",
            InvalidVolumeCount = "0",
            MissingDataCount = "2",
            LastCheckTimeText = "2 minutes ago",
            NextCheckText = "In 28 minutes",
            CheckProgressValue = 6,
            P50Text = "8ms",
            P75Text = "12ms",
            P90Text = "18ms",
            P95Text = "25ms",
            P99Text = "45ms",
            Symbols = new[]
            {
                CreateDemoSymbolQuality("SPY", 99.8, "Healthy"),
                CreateDemoSymbolQuality("AAPL", 98.2, "Healthy"),
                CreateDemoSymbolQuality("MSFT", 97.5, "Healthy"),
                CreateDemoSymbolQuality("GOOGL", 94.8, "Degraded"),
                CreateDemoSymbolQuality("AMZN", 96.1, "Healthy")
            },
            Gaps = BuildDemoGaps(),
            Alerts = BuildDemoAlerts(),
            Anomalies = Array.Empty<DataQualityAnomalyPresentation>()
        };
    }

    private static DataQualitySymbolPresentation MapSymbol(QualitySymbolHealthResponse response)
    {
        var score = Math.Clamp(response.Score * 100, 0, 100);
        var issues = response.ActiveIssues.Where(issue => !string.IsNullOrWhiteSpace(issue)).ToArray();

        return new DataQualitySymbolPresentation
        {
            Symbol = response.Symbol,
            Score = score,
            ScoreFormatted = $"{score:F1}%",
            Grade = GetGrade(score),
            Status = ReadEnumString(response.State, HealthStateNames, "Unknown"),
            Issues = issues.Length == 0 ? "—" : string.Join(", ", issues),
            LastUpdate = response.LastEvent,
            LastUpdateFormatted = FormatRelativeTime(response.LastEvent.UtcDateTime)
        };
    }

    private static DataQualityGapPresentation MapGap(QualityGapResponse response)
    {
        var duration = response.GapEnd - response.GapStart;
        var durationText = duration.TotalDays >= 1 ? $"{duration.TotalDays:F0} days"
            : duration.TotalHours >= 1 ? $"{duration.TotalHours:F0} hours"
            : $"{duration.TotalMinutes:F0} mins";

        return new DataQualityGapPresentation
        {
            GapId = response.GapStart != default ? response.GapStart.ToString("O") : Guid.NewGuid().ToString(),
            Symbol = response.Symbol,
            Description = $"Missing {response.EstimatedMissedEvents} events between {response.GapStart:yyyy-MM-dd HH:mm} and {response.GapEnd:yyyy-MM-dd HH:mm}",
            Duration = durationText
        };
    }

    private static DataQualityAlertPresentation MapAlert(QualityAnomalyResponse response)
    {
        var severity = ReadEnumString(response.Severity, AnomalySeverityNames, "Warning");
        return new DataQualityAlertPresentation
        {
            Id = string.IsNullOrWhiteSpace(response.Id) ? Guid.NewGuid().ToString() : response.Id,
            Symbol = response.Symbol,
            AlertType = ReadEnumString(response.Type, AnomalyTypeNames, string.Empty),
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
            Type = ReadEnumString(response.Type, AnomalyTypeNames, string.Empty),
            SeverityTone = SeverityToTone(severity)
        };
    }

    private static DataQualityGapPresentation[] BuildDemoGaps()
    {
        return new[]
        {
            new DataQualityGapPresentation { GapId = "gap-1", Symbol = "AAPL", Description = "Missing 156 events between 2024-01-15 09:30 and 2024-01-17 16:00", Duration = "2 days" },
            new DataQualityGapPresentation { GapId = "gap-2", Symbol = "GOOGL", Description = "Missing 45 events between 2024-01-20 14:00 and 2024-01-20 15:30", Duration = "1.5 hours" },
            new DataQualityGapPresentation { GapId = "gap-3", Symbol = "MSFT", Description = "Missing 12 events between 2024-01-22 10:00 and 2024-01-22 10:15", Duration = "15 mins" }
        };
    }

    private static DataQualityAlertPresentation[] BuildDemoAlerts()
    {
        return new[]
        {
            new DataQualityAlertPresentation
            {
                Id = "alert-1",
                Symbol = "AAPL",
                AlertType = "StaleData",
                Message = "No trades received in the last 3 minutes",
                Severity = "Warning",
                SeverityTone = DataQualityVisualTones.Warning
            },
            new DataQualityAlertPresentation
            {
                Id = "alert-2",
                Symbol = "GOOGL",
                AlertType = "CrossedMarket",
                Message = "Bid price exceeded ask for 2 ticks",
                Severity = "Critical",
                SeverityTone = DataQualityVisualTones.Error
            }
        };
    }

    private static DataQualitySymbolPresentation CreateDemoSymbolQuality(string symbol, double score, string status)
    {
        return new DataQualitySymbolPresentation
        {
            Symbol = symbol,
            Score = score,
            ScoreFormatted = $"{score:F1}%",
            Grade = GetGrade(score),
            Status = status,
            Issues = status == "Healthy" ? "—" : "Recent gaps",
            LastUpdate = DateTimeOffset.UtcNow.AddMinutes(-3),
            LastUpdateFormatted = "3m ago"
        };
    }

    private static string ReadEnumString(int value, IReadOnlyList<string> names, string fallback)
        => value >= 0 && value < names.Count ? names[value] : fallback;

    private static int GetGradeCount(IReadOnlyDictionary<string, int> gradeDistribution, string grade)
        => gradeDistribution.TryGetValue(grade, out var value) ? value : 0;

    private static int GetAnomalyCount(IReadOnlyDictionary<string, int>? counts, string type)
        => counts != null && counts.TryGetValue(type, out var value) ? value : 0;

    private static int GetRangeCount(string range, int oneDay, int sevenDay, int thirtyDay, int ninetyDay)
        => range switch
        {
            "1d" => oneDay,
            "7d" => sevenDay,
            "30d" => thirtyDay,
            "90d" => ninetyDay,
            _ => sevenDay
        };

    private static string FormatRelativeTime(DateTime time)
    {
        var span = DateTime.UtcNow - time;
        return span.TotalSeconds < 60 ? "Just now"
            : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes} minutes ago"
            : span.TotalHours < 24 ? $"{(int)span.TotalHours} hours ago"
            : $"{(int)span.TotalDays} days ago";
    }

    private static string SeverityToTone(string severity)
        => severity.ToLowerInvariant() switch
        {
            "critical" or "error" => DataQualityVisualTones.Error,
            "warning" => DataQualityVisualTones.Warning,
            _ => DataQualityVisualTones.Info
        };

    private static string ScoreToTone(double score) => score switch
    {
        >= 90 => DataQualityVisualTones.Success,
        >= 75 => DataQualityVisualTones.Info,
        >= 50 => DataQualityVisualTones.Warning,
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

    private static string GetStatus(double score) => score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Healthy",
        >= 50 => "Warning",
        _ => "Critical"
    };
}
