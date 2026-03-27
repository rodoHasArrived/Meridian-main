using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.Services;

#region DTO Classes

public sealed class DataQualitySummary
{
    public double OverallScore { get; set; }
    public int TotalFiles { get; set; }
    public int HealthyFiles { get; set; }
    public int WarningFiles { get; set; }
    public int CriticalFiles { get; set; }
    public int ActiveAlerts { get; set; }
    public int UnacknowledgedAlerts { get; set; }
    public DateTime LastChecked { get; set; }
    public List<SymbolQualitySummary> SymbolSummaries { get; set; } = new();
}

public sealed class SymbolQualitySummary
{
    public string Symbol { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public int GapCount { get; set; }
    public DateTime LastUpdate { get; set; }
}

public sealed class QualityScoreEntry
{
    public string Path { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Grade { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int MissingCount { get; set; }
    public int DuplicateCount { get; set; }
    public int AnomalyCount { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public DateTime LastAnalyzed { get; set; }
}

public sealed class SymbolQualityReport
{
    public string Symbol { get; set; } = string.Empty;
    public double OverallScore { get; set; }
    public Dictionary<string, double> ScoresByType { get; set; } = new();
    public List<QualityIssue> Issues { get; set; } = new();
    public List<QualityDataGap> Gaps { get; set; } = new();
    public QualityCompletenessReport Completeness { get; set; } = new();
}

public sealed class QualityIssue
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string AffectedPath { get; set; } = string.Empty;
}

public sealed class QualityDataGap
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public TimeSpan Duration { get; set; }
    public string DataType { get; set; } = string.Empty;
    public int MissingRecords { get; set; }
}

public sealed class QualityCompletenessReport
{
    public double OverallCompleteness { get; set; }
    public Dictionary<string, double> ByDataType { get; set; } = new();
    public Dictionary<string, double> ByDate { get; set; } = new();
}

public sealed class QualityAlert
{
    public string Id { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsAcknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
}

public sealed class AcknowledgeResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class SourceRanking
{
    public string Source { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public double QualityScore { get; set; }
    public double Latency { get; set; }
    public double Completeness { get; set; }
    public int Rank { get; set; }
}

public sealed class QualityTrendData
{
    public List<TrendDataPoint> OverallTrend { get; set; } = new();
    public Dictionary<string, List<TrendDataPoint>> BySymbol { get; set; } = new();
    public string TimeWindow { get; set; } = string.Empty;
    public double AverageScore { get; set; }
    public double TrendDirection { get; set; }
}

public sealed class TrendDataPoint
{
    public DateTime Timestamp { get; set; }
    public double Score { get; set; }
    public int EventCount { get; set; }
    public int AlertCount { get; set; }
}

public sealed class AnomalyEvent
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
}

public sealed class QualityCheckResult
{
    public bool Success { get; set; }
    public double Score { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public DateTime CheckedAt { get; set; }
}

public sealed class DataGapInfo
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int MissingBars { get; set; }
}

public sealed class IntegrityVerificationResult
{
    public bool IsValid { get; set; }
    public double Score { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public DateTime CheckedAt { get; set; }
}

#endregion

/// <summary>
/// Abstract base class for data quality monitoring shared between platforms.
/// Provides all quality query methods and DTOs.
/// Platform-specific API client access is delegated to derived classes.
/// Part of Phase 2 service extraction.
/// </summary>
public abstract class DataQualityServiceBase
{
    /// <summary>
    /// Gets a value from the API using a GET request.
    /// </summary>
    protected abstract Task<T?> GetAsync<T>(string endpoint, CancellationToken ct) where T : class;

    /// <summary>
    /// Posts a value to the API and returns the result.
    /// </summary>
    protected abstract Task<T?> PostAsync<T>(string endpoint, object? body, CancellationToken ct) where T : class;

    /// <summary>
    /// Posts a value and returns an operation result with success indicator.
    /// </summary>
    protected abstract Task<(bool Success, T? Data)> PostWithResponseAsync<T>(string endpoint, object? body, CancellationToken ct) where T : class;

    public async Task<DataQualitySummary?> GetQualitySummaryAsync(CancellationToken ct = default)
        => await GetAsync<DataQualitySummary>("/api/storage/quality/summary", ct);

    public async Task<List<QualityScoreEntry>?> GetQualityScoresAsync(double? minScore = null, CancellationToken ct = default)
    {
        var endpoint = minScore.HasValue
            ? $"/api/storage/quality/scores?minScore={minScore}"
            : "/api/storage/quality/scores";
        return await GetAsync<List<QualityScoreEntry>>(endpoint, ct);
    }

    public async Task<SymbolQualityReport?> GetSymbolQualityAsync(string symbol, CancellationToken ct = default)
        => await GetAsync<SymbolQualityReport>($"/api/storage/quality/symbol/{symbol}", ct);

    public async Task<List<QualityAlert>?> GetQualityAlertsAsync(string? severity = null, CancellationToken ct = default)
    {
        var endpoint = !string.IsNullOrEmpty(severity)
            ? $"/api/storage/quality/alerts?severity={severity}"
            : "/api/storage/quality/alerts";
        return await GetAsync<List<QualityAlert>>(endpoint, ct);
    }

    public async Task<bool> AcknowledgeAlertAsync(string alertId, CancellationToken ct = default)
    {
        var (success, _) = await PostWithResponseAsync<AcknowledgeResponse>(
            $"/api/storage/quality/alerts/{alertId}/acknowledge", null, ct);
        return success;
    }

    public async Task<List<SourceRanking>?> GetSourceRankingsAsync(string symbol, CancellationToken ct = default)
        => await GetAsync<List<SourceRanking>>($"/api/storage/quality/rankings/{symbol}", ct);

    public async Task<QualityTrendData?> GetQualityTrendsAsync(string? timeWindow = "7d", CancellationToken ct = default)
        => await GetAsync<QualityTrendData>($"/api/storage/quality/trends?window={timeWindow}", ct);

    public async Task<List<AnomalyEvent>?> GetAnomaliesAsync(string? type = null, CancellationToken ct = default)
    {
        var endpoint = !string.IsNullOrEmpty(type)
            ? $"/api/storage/quality/anomalies?type={type}"
            : "/api/storage/quality/anomalies";
        return await GetAsync<List<AnomalyEvent>>(endpoint, ct);
    }

    public async Task<QualityCheckResult?> RunQualityCheckAsync(string path, CancellationToken ct = default)
        => await PostAsync<QualityCheckResult>("/api/storage/quality/check", new { path }, ct);

    public async Task<List<DataGapInfo>> GetDataGapsAsync(string symbol, CancellationToken ct = default)
    {
        var report = await GetSymbolQualityAsync(symbol, ct);
        if (report?.Gaps == null)
            return new List<DataGapInfo>();

        var gaps = new List<DataGapInfo>();
        foreach (var gap in report.Gaps)
        {
            gaps.Add(new DataGapInfo
            {
                StartDate = gap.Start,
                EndDate = gap.End,
                MissingBars = gap.MissingRecords
            });
        }
        return gaps;
    }

    public async Task<IntegrityVerificationResult> VerifySymbolIntegrityAsync(string symbol, CancellationToken ct = default)
    {
        var checkResult = await RunQualityCheckAsync(symbol, ct);

        if (checkResult == null)
        {
            return new IntegrityVerificationResult
            {
                IsValid = false,
                Issues = new List<string> { "Failed to run integrity check" }
            };
        }

        return new IntegrityVerificationResult
        {
            IsValid = checkResult.Score >= 95.0 && checkResult.Issues.Count == 0,
            Score = checkResult.Score,
            Issues = checkResult.Issues,
            Recommendations = checkResult.Recommendations,
            CheckedAt = checkResult.CheckedAt
        };
    }
}
