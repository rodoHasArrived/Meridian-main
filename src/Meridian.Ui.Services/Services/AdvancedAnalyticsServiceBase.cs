using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Base class for advanced analytics services, providing shared API delegation logic
/// for gap analysis, cross-provider comparison, latency, quality reports, and rate limits.
/// WPF implementations inherit and add platform-specific capabilities.
/// </summary>
public class AdvancedAnalyticsServiceBase
{
    #region Gap Analysis

    public virtual async Task<GapAnalysisResult> AnalyzeGapsAsync(
        GapAnalysisOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<GapAnalysisResponse>(
            "/api/analytics/gaps", options, ct);

        if (response.Success && response.Data != null)
        {
            return new GapAnalysisResult
            {
                Success = true,
                AnalysisTime = response.Data.AnalysisTime,
                TotalGaps = response.Data.TotalGaps,
                TotalGapDuration = response.Data.TotalGapDuration,
                Gaps = response.Data.Gaps?.ToList() ?? new List<AnalyticsDataGap>(),
                SymbolSummaries = response.Data.SymbolSummaries?.ToList() ?? new List<SymbolGapSummary>()
            };
        }

        return new GapAnalysisResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Gap analysis failed"
        };
    }

    public virtual async Task<AnalyticsGapRepairResult> RepairGapsAsync(
        GapRepairOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<GapRepairResponse>(
            "/api/analytics/gaps/repair", options, ct);

        if (response.Success && response.Data != null)
        {
            return new AnalyticsGapRepairResult
            {
                Success = true,
                GapsAttempted = response.Data.GapsAttempted,
                GapsRepaired = response.Data.GapsRepaired,
                GapsFailed = response.Data.GapsFailed,
                RecordsRecovered = response.Data.RecordsRecovered,
                Details = response.Data.Details?.ToList() ?? new List<GapRepairDetail>()
            };
        }

        return new AnalyticsGapRepairResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Gap repair failed"
        };
    }

    #endregion

    #region Cross-Provider Comparison

    public virtual async Task<CrossProviderComparisonResult> CompareProvidersAsync(
        CrossProviderComparisonOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<CrossProviderComparisonResponse>(
            "/api/analytics/compare", options, ct);

        if (response.Success && response.Data != null)
        {
            return new CrossProviderComparisonResult
            {
                Success = true,
                ComparisonTime = response.Data.ComparisonTime,
                OverallConsistencyScore = response.Data.OverallConsistencyScore,
                ProvidersCompared = response.Data.ProvidersCompared?.ToList() ?? new List<string>(),
                Comparisons = response.Data.Comparisons?.ToList() ?? new List<ProviderComparison>(),
                Discrepancies = response.Data.Discrepancies?.ToList() ?? new List<DataDiscrepancy>()
            };
        }

        return new CrossProviderComparisonResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Provider comparison failed"
        };
    }

    #endregion

    #region Latency Analysis

    public virtual async Task<LatencyHistogramResult> GetLatencyHistogramAsync(
        LatencyHistogramOptions? options = null,
        CancellationToken ct = default)
    {
        var url = "/api/analytics/latency";
        if (options?.Provider != null)
        {
            url = $"/api/analytics/latency?provider={Uri.EscapeDataString(options.Provider)}";
        }

        var response = await ApiClientService.Instance.GetWithResponseAsync<LatencyHistogramResponse>(url, ct);

        if (response.Success && response.Data != null)
        {
            return new LatencyHistogramResult
            {
                Success = true,
                Period = response.Data.Period,
                Providers = response.Data.Providers?.ToList() ?? new List<ProviderLatencyData>()
            };
        }

        return new LatencyHistogramResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to get latency data"
        };
    }

    public virtual async Task<LatencyStatisticsResult> GetLatencyStatisticsAsync(
        string? provider = null,
        CancellationToken ct = default)
    {
        var url = "/api/analytics/latency/stats";
        if (provider != null)
        {
            url = $"{url}?provider={Uri.EscapeDataString(provider)}";
        }

        var response = await ApiClientService.Instance.GetWithResponseAsync<LatencyStatisticsResponse>(url, ct);

        if (response.Success && response.Data != null)
        {
            return new LatencyStatisticsResult
            {
                Success = true,
                Statistics = response.Data.Statistics?.ToList() ?? new List<ProviderLatencyStatistics>()
            };
        }

        return new LatencyStatisticsResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    #endregion

    #region Anomaly Detection

    public virtual async Task<AnomalyDetectionResult> DetectAnomaliesAsync(
        AnomalyDetectionOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<AnomalyDetectionResponse>(
            "/api/analytics/anomalies", options, ct);

        if (response.Success && response.Data != null)
        {
            return new AnomalyDetectionResult
            {
                Success = true,
                AnalysisTime = response.Data.AnalysisTime,
                TotalAnomalies = response.Data.TotalAnomalies,
                Anomalies = response.Data.Anomalies?.ToList() ?? new List<DataAnomaly>(),
                Summary = response.Data.Summary
            };
        }

        return new AnomalyDetectionResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Anomaly detection failed"
        };
    }

    #endregion

    #region Quality Reports

    public virtual async Task<DataQualityReportResult> GetQualityReportAsync(
        DataQualityReportOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<DataQualityReportResponse>(
            "/api/analytics/quality-report", options, ct);

        if (response.Success && response.Data != null)
        {
            return new DataQualityReportResult
            {
                Success = true,
                ReportTime = response.Data.ReportTime,
                OverallScore = response.Data.OverallScore,
                Grade = response.Data.Grade,
                Metrics = response.Data.Metrics,
                SymbolReports = response.Data.SymbolReports?.ToList() ?? new List<AnalyticsSymbolQualityReport>(),
                Recommendations = response.Data.Recommendations?.ToList() ?? new List<string>()
            };
        }

        return new DataQualityReportResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Failed to generate quality report"
        };
    }

    public virtual async Task<CompletenessAnalysisResult> AnalyzeCompletenessAsync(
        CompletenessAnalysisOptions options,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<CompletenessAnalysisResponse>(
            "/api/analytics/completeness", options, ct);

        if (response.Success && response.Data != null)
        {
            return new CompletenessAnalysisResult
            {
                Success = true,
                OverallCompleteness = response.Data.OverallCompleteness,
                TradingDaysCovered = response.Data.TradingDaysCovered,
                TradingDaysExpected = response.Data.TradingDaysExpected,
                SymbolCompleteness = response.Data.SymbolCompleteness?.ToList() ?? new List<SymbolCompleteness>()
            };
        }

        return new CompletenessAnalysisResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    #endregion

    #region Throughput Analysis

    public virtual async Task<ThroughputAnalysisResult> GetThroughputAnalysisAsync(
        ThroughputAnalysisOptions? options = null,
        CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.PostWithResponseAsync<ThroughputAnalysisResponse>(
            "/api/analytics/throughput",
            options ?? new ThroughputAnalysisOptions(),
            ct);

        if (response.Success && response.Data != null)
        {
            return new ThroughputAnalysisResult
            {
                Success = true,
                CurrentThroughput = response.Data.CurrentThroughput,
                AverageThroughput = response.Data.AverageThroughput,
                PeakThroughput = response.Data.PeakThroughput,
                TotalEventsProcessed = response.Data.TotalEventsProcessed,
                Timeline = response.Data.Timeline?.ToList() ?? new List<ThroughputDataPoint>()
            };
        }

        return new ThroughputAnalysisResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    #endregion

    #region Rate Limits

    public virtual async Task<RateLimitStatusResult> GetRateLimitStatusAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<RateLimitStatusResponse>(
            "/api/analytics/rate-limits", ct);

        if (response.Success && response.Data != null)
        {
            return new RateLimitStatusResult
            {
                Success = true,
                Providers = response.Data.Providers?.ToList() ?? new List<ProviderRateLimitStatus>()
            };
        }

        return new RateLimitStatusResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    #endregion

    #region Symbols

    public virtual async Task<AnalyticsSymbolsResult> GetAllSymbolsAsync(CancellationToken ct = default)
    {
        var response = await ApiClientService.Instance.GetWithResponseAsync<AnalyticsSymbolsResponse>(
            "/api/symbols", ct);

        if (response.Success && response.Data != null)
        {
            return new AnalyticsSymbolsResult
            {
                Success = true,
                Symbols = response.Data.Symbols?.ToList() ?? new List<AnalyticsSymbolInfo>()
            };
        }

        return new AnalyticsSymbolsResult
        {
            Success = false,
            Error = response.ErrorMessage
        };
    }

    #endregion
}
