using System.Globalization;
using System.Text.Json;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Contracts.Api;
using Meridian.Contracts.Api.Quality;
using Meridian.Application.Backfill;
using Meridian.Application.DataQuality;
using Meridian.Ui.Shared.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// HTTP endpoint extensions for data quality monitoring dashboard.
/// </summary>
public static class DataQualityEndpoints
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Parses an optional date string, defaulting to today (UTC).
    /// </summary>
    private static DateOnly ParseDateOrToday(string? date, string field = "date") =>
        date is null ? DateOnly.FromDateTime(DateTime.UtcNow) : ParseDate(date, field);

    private static DateOnly ParseDate(string value, string field)
    {
        if (DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        throw new DataQualityRequestValidationException(
            field,
            $"{field} must use the yyyy-MM-dd format.");
    }

    /// <summary>
    /// Returns the result as JSON using the shared serializer options.
    /// </summary>
    private static IResult Json<T>(T value) => Results.Json(value, s_jsonOptions);

    /// <summary>
    /// Maps all data quality monitoring endpoints.
    /// </summary>
    public static void MapDataQualityEndpoints(this WebApplication app, DataQualityMonitoringService qualityService)
    {
        var guard = CreateGuard(app);
        var compositeService = app.Services.GetService(typeof(ICompositeDataQualityReadService))
            as ICompositeDataQualityReadService;
        var remediationService = app.Services.GetService(typeof(IDataQualityGapRemediationService))
            as IDataQualityGapRemediationService;

        // ==================== DASHBOARD ====================

        app.MapGet(UiApiRoutes.QualityDashboard, (CancellationToken ct) =>
            guard.HandleAsync(async () =>
            {
                var legacy = ToResponse(qualityService.GetDashboard());
                var composite = compositeService is null
                    ? null
                    : await compositeService.GetDashboardAsync(ct).ConfigureAwait(false);
                var response = legacy with { Composite = composite };
                return Results.Json(response, QualityApiJsonContext.Default.QualityDashboardResponse);
            }, ct));

        app.MapGet(UiApiRoutes.QualityMetrics, () =>
            guard.HandleSync(() => Json(qualityService.GetRealTimeMetrics())));

        // ==================== COMPLETENESS ====================

        app.MapGet(UiApiRoutes.QualityCompleteness, (string? date) =>
            guard.HandleSync(() =>
            {
                var targetDate = ParseDateOrToday(date);
                return Json(qualityService.Completeness.GetScoresForDate(targetDate));
            }));

        app.MapGet(UiApiRoutes.QualityCompletenessBySymbol, (string symbol, string? date) =>
            guard.HandleSync(() =>
            {
                if (date != null)
                {
                    var targetDate = ParseDate(date, nameof(date));
                    var score = qualityService.Completeness.GetScore(symbol, targetDate);
                    return score != null
                        ? Json(score)
                        : Results.NotFound($"No completeness data for {symbol} on {date}");
                }

                return Json(qualityService.Completeness.GetScoresForSymbol(symbol));
            }));

        app.MapGet(UiApiRoutes.QualityCompletenessSummary, () =>
            guard.HandleSync(() => Json(qualityService.Completeness.GetSummary())));

        app.MapGet(UiApiRoutes.QualityCompletenessLow, (string? date, double? threshold) =>
            guard.HandleSync(() =>
            {
                var targetDate = ParseDateOrToday(date);
                return Json(qualityService.Completeness.GetLowCompletenessSymbols(targetDate, threshold ?? 0.8));
            }));

        // ==================== GAP ANALYSIS ====================

        app.MapGet(UiApiRoutes.QualityGaps, (string? date, int? count) =>
            guard.HandleSync(() =>
            {
                if (date != null)
                {
                    var targetDate = ParseDate(date, nameof(date));
                    return Json(qualityService.GapAnalyzer.GetGapsForDate(targetDate).Select(ToResponse).ToArray());
                }

                return Json(qualityService.GapAnalyzer.GetRecentGaps(count ?? 100).Select(ToResponse).ToArray());
            }));

        app.MapGet(UiApiRoutes.QualityGapsBySymbol, (string symbol, string? date) =>
            guard.HandleSync(() =>
            {
                var targetDate = ParseDateOrToday(date);
                return Json(qualityService.GapAnalyzer.AnalyzeGaps(symbol, targetDate));
            }));

        app.MapPost(UiApiRoutes.QualityGapsBySymbol, (
            string symbol,
            QualityGapRemediationRequest request,
            CancellationToken ct) =>
            guard.HandleAsync(async () =>
            {
                if (compositeService is null || remediationService is null)
                {
                    return Results.Problem(
                        "Contextual quality-gap remediation is unavailable.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (!compositeService.TryResolveGap(symbol, request.GapId, out var target))
                {
                    return Results.NotFound($"Gap {request.GapId} was not found for {symbol}.");
                }

                if (!string.Equals(request.DashboardVersion, target.DashboardVersion, StringComparison.Ordinal))
                {
                    return Results.Conflict(new
                    {
                        message = "The data-quality snapshot changed. Refresh before remediating this gap."
                    });
                }

                if (string.IsNullOrWhiteSpace(target.Provider))
                {
                    return Results.Conflict(new
                    {
                        message = "The originating provider is unavailable. Refresh after provider-attributed quality evidence is available."
                    });
                }

                var outcome = await remediationService
                    .RequestDataQualityGapAsync(target.Gap, target.Provider, ct)
                    .ConfigureAwait(false);
                var response = new QualityGapRemediationResponse(
                    GapId: target.GapId,
                    Symbol: target.Gap.Symbol,
                    Status: outcome.Outcome.ToString(),
                    Provider: outcome.Provider,
                    From: outcome.From,
                    To: outcome.To,
                    IdempotencyKey: outcome.IdempotencyKey,
                    Message: DescribeRemediationOutcome(outcome.Outcome));

                return outcome.Outcome switch
                {
                    AutoRemediationOutcome.Completed => Results.Json(
                        response,
                        QualityApiJsonContext.Default.QualityGapRemediationResponse),
                    AutoRemediationOutcome.None => Results.Accepted(value: response),
                    AutoRemediationOutcome.Skipped => Results.Conflict(response),
                    _ => Results.Json(
                        response,
                        QualityApiJsonContext.Default.QualityGapRemediationResponse,
                        statusCode: StatusCodes.Status502BadGateway)
                };
            }, ct));

        app.MapGet(UiApiRoutes.QualityGapsTimeline, (string symbol, string? date) =>
            guard.HandleSync(() =>
            {
                var targetDate = ParseDateOrToday(date);
                var analysis = qualityService.GapAnalyzer.AnalyzeGaps(symbol, targetDate);
                return Json(new { symbol, date = targetDate, timeline = analysis.Timeline });
            }));

        app.MapGet(UiApiRoutes.QualityGapsStatistics, (string? date) =>
            guard.HandleSync(() =>
            {
                var targetDate = date != null ? ParseDate(date, nameof(date)) : (DateOnly?)null;
                return Json(qualityService.GapAnalyzer.GetStatistics(targetDate));
            }));

        // ==================== SEQUENCE ERRORS ====================

        app.MapGet(UiApiRoutes.QualityErrors, (string? date, int? count) =>
            guard.HandleSync(() =>
            {
                if (date != null)
                {
                    var targetDate = ParseDate(date, nameof(date));
                    return Json(qualityService.SequenceTracker.GetErrorsForDate(targetDate));
                }

                return Json(qualityService.SequenceTracker.GetRecentErrors(count ?? 100));
            }));

        app.MapGet(UiApiRoutes.QualityErrorsBySymbol, (string symbol, string? date, int? count) =>
            guard.HandleSync(() =>
            {
                var targetDate = date != null ? ParseDate(date, nameof(date)) : (DateOnly?)null;
                return Json(qualityService.SequenceTracker.GetSummary(symbol, targetDate));
            }));

        app.MapGet(UiApiRoutes.QualityErrorsStatistics, () =>
            guard.HandleSync(() => Json(qualityService.SequenceTracker.GetStatistics())));

        app.MapGet(UiApiRoutes.QualityErrorsTopSymbols, (int? count) =>
            guard.HandleSync(() => Json(qualityService.SequenceTracker.GetSymbolsWithMostErrors(count ?? 10))));

        // ==================== ANOMALIES ====================

        app.MapGet(UiApiRoutes.QualityAnomalies, (string? date, string? type, string? severity, int? count) =>
            guard.HandleSync(() =>
            {
                IReadOnlyList<DataAnomaly> anomalies;

                if (date != null)
                {
                    var targetDate = ParseDate(date, nameof(date));
                    anomalies = qualityService.AnomalyDetector.GetAnomaliesForDate(targetDate);
                }
                else if (type != null && Enum.TryParse<AnomalyType>(type, true, out var anomalyType))
                {
                    anomalies = qualityService.AnomalyDetector.GetAnomaliesByType(anomalyType, count ?? 100);
                }
                else if (severity != null && Enum.TryParse<AnomalySeverity>(severity, true, out var sev))
                {
                    anomalies = qualityService.AnomalyDetector.GetAnomaliesBySeverity(sev, count ?? 100);
                }
                else
                {
                    anomalies = qualityService.AnomalyDetector.GetRecentAnomalies(count ?? 100);
                }

                return Json(anomalies.Select(ToResponse).ToArray());
            }));

        app.MapGet(UiApiRoutes.QualityAnomaliesBySymbol, (string symbol, int? count) =>
            guard.HandleSync(() => Json(qualityService.AnomalyDetector.GetAnomalies(symbol, count ?? 100).Select(ToResponse).ToArray())));

        app.MapGet(UiApiRoutes.QualityAnomaliesUnacknowledged, (int? count) =>
            guard.HandleSync(() => Json(qualityService.AnomalyDetector.GetUnacknowledgedAnomalies(count ?? 100).Select(ToResponse).ToArray())));

        app.MapPost(UiApiRoutes.QualityAnomaliesAcknowledge, (string anomalyId) =>
            guard.HandleSync(() =>
            {
                var success = qualityService.AnomalyDetector.AcknowledgeAnomaly(anomalyId);
                return success
                    ? Json(new QualityAnomalyAcknowledgementResponse(Acknowledged: true))
                    : Results.NotFound($"Anomaly {anomalyId} not found");
            }));

        app.MapGet(UiApiRoutes.QualityAnomaliesStatistics, () =>
            guard.HandleSync(() => Json(qualityService.AnomalyDetector.GetStatistics())));

        app.MapGet(UiApiRoutes.QualityAnomaliesStale, () =>
            guard.HandleSync(() => Json(qualityService.AnomalyDetector.GetStaleSymbols())));

        // ==================== LATENCY ====================

        app.MapGet(UiApiRoutes.QualityLatency, () =>
            guard.HandleSync(() => Json(qualityService.LatencyHistogram.GetAllDistributions())));

        app.MapGet(UiApiRoutes.QualityLatencyBySymbol, (string symbol, string? provider) =>
            guard.HandleSync(() =>
            {
                var distribution = qualityService.LatencyHistogram.GetDistribution(symbol, provider);
                return distribution != null
                    ? Json(distribution)
                    : Results.NotFound($"No latency data for {symbol}");
            }));

        app.MapGet(UiApiRoutes.QualityLatencyHistogram, (string symbol, string? provider) =>
            guard.HandleSync(() => Json(new { symbol, provider, buckets = qualityService.LatencyHistogram.GetBuckets(symbol, provider) })));

        app.MapGet(UiApiRoutes.QualityLatencyStatistics, () =>
            guard.HandleSync(() => Json(ToResponse(qualityService.LatencyHistogram.GetStatistics()))));

        app.MapGet(UiApiRoutes.QualityLatencyHigh, (double? thresholdMs) =>
            guard.HandleSync(() => Json(qualityService.LatencyHistogram.GetHighLatencySymbols(thresholdMs ?? 100))));

        // ==================== CROSS-PROVIDER COMPARISON ====================

        app.MapGet(UiApiRoutes.QualityComparison, (string symbol, string? date, string? eventType) =>
            guard.HandleSync(() =>
            {
                var targetDate = ParseDateOrToday(date);
                return Json(ToResponse(qualityService.CrossProvider.Compare(symbol, targetDate, eventType ?? "Trade")));
            }));

        app.MapGet(UiApiRoutes.QualityComparisonDiscrepancies, (string? date, int? count) =>
            guard.HandleSync(() =>
            {
                if (date != null)
                {
                    var targetDate = ParseDate(date, nameof(date));
                    return Json(qualityService.CrossProvider.GetDiscrepanciesForDate(targetDate));
                }

                return Json(qualityService.CrossProvider.GetRecentDiscrepancies(count ?? 100));
            }));

        app.MapGet(UiApiRoutes.QualityComparisonStatistics, () =>
            guard.HandleSync(() => Json(qualityService.CrossProvider.GetStatistics())));

        // ==================== REPORTS ====================

        app.MapGet(UiApiRoutes.QualityReportsDaily, async (string? date, CancellationToken ct) =>
            await guard.HandleAsync(async () =>
            {
                var targetDate = ParseDateOrToday(date);
                var report = await qualityService.GenerateDailyReportAsync(targetDate, null, ct);
                return Json(report);
            }, ct));

        app.MapGet(UiApiRoutes.QualityReportsWeekly, async (string? weekStart, CancellationToken ct) =>
            await guard.HandleAsync(async () =>
            {
                DateOnly start;
                if (weekStart != null)
                {
                    start = ParseDate(weekStart, nameof(weekStart));
                }
                else
                {
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var dayOfWeek = (int)today.DayOfWeek;
                    start = today.AddDays(-dayOfWeek);
                }

                var report = await qualityService.GenerateWeeklyReportAsync(start, null, ct);
                return Json(report);
            }, ct));

        app.MapPost(UiApiRoutes.QualityReportsExport, async (ReportExportRequest request, CancellationToken ct) =>
            await guard.HandleAsync(async () =>
            {
                var targetDate = ParseDateOrToday(request.Date);
                var format = Enum.TryParse<ReportExportFormat>(request.Format, true, out var f)
                    ? f : ReportExportFormat.Json;

                var report = await qualityService.GenerateDailyReportAsync(targetDate, null, ct);
                var filePath = await qualityService.ExportReportAsync(report, format, ct);

                return Results.Ok(new { filePath, format = format.ToString() });
            }, ct));

        // ==================== HEALTH ====================

        app.MapGet(UiApiRoutes.QualityHealth, () =>
            guard.HandleSync(() =>
            {
                var metrics = qualityService.GetRealTimeMetrics();
                var status = metrics.OverallHealthScore switch
                {
                    >= 0.9 => "healthy",
                    >= 0.7 => "degraded",
                    _ => "unhealthy"
                };

                return Json(new
                {
                    status,
                    score = metrics.OverallHealthScore,
                    activeSymbols = metrics.ActiveSymbols,
                    symbolsWithIssues = metrics.SymbolsWithIssues,
                    gapsLast5Min = metrics.GapsLast5Minutes,
                    errorsLast5Min = metrics.SequenceErrorsLast5Minutes,
                    anomaliesLast5Min = metrics.AnomaliesLast5Minutes,
                    timestamp = metrics.Timestamp
                });
            }));

        app.MapGet(UiApiRoutes.QualityHealthBySymbol, (string symbol) =>
            guard.HandleSync(() =>
            {
                var health = qualityService.GetSymbolHealth(symbol);
                return health != null
                    ? Json(health)
                    : Results.NotFound($"No health data for {symbol}");
            }));

        app.MapGet(UiApiRoutes.QualityHealthUnhealthy, () =>
            guard.HandleSync(() => Json(qualityService.GetUnhealthySymbols())));
    }

    private static QualityDashboardResponse ToResponse(DataQualityDashboard dashboard) =>
        new(
            Timestamp: dashboard.Timestamp,
            RealTimeMetrics: ToResponse(dashboard.RealTimeMetrics),
            CompletenessStats: ToResponse(dashboard.CompletenessStats),
            GapStats: ToResponse(dashboard.GapStats),
            SequenceStats: ToResponse(dashboard.SequenceStats),
            AnomalyStats: ToResponse(dashboard.AnomalyStats),
            LatencyStats: ToResponse(dashboard.LatencyStats),
            RecentGaps: dashboard.RecentGaps.Select(ToResponse).ToArray(),
            RecentErrors: dashboard.RecentErrors.Select(ToResponse).ToArray(),
            RecentAnomalies: dashboard.RecentAnomalies.Select(ToResponse).ToArray(),
            StaleSymbols: dashboard.StaleSymbols.ToArray());

    private static string DescribeRemediationOutcome(AutoRemediationOutcome outcome) => outcome switch
    {
        AutoRemediationOutcome.Completed => "The gap backfill completed successfully.",
        AutoRemediationOutcome.Skipped => "The gap was not queued because a remediation guardrail is active.",
        AutoRemediationOutcome.FailedTransient => "The gap backfill failed with a transient provider error.",
        AutoRemediationOutcome.FailedPermanent => "The gap backfill failed and requires operator review.",
        _ => "The gap backfill request was accepted."
    };

    private static QualityRealTimeMetricsResponse ToResponse(RealTimeQualityMetrics metrics) =>
        new(
            Timestamp: metrics.Timestamp,
            ActiveSymbols: metrics.ActiveSymbols,
            OverallHealthScore: metrics.OverallHealthScore,
            EventsPerSecond: metrics.EventsPerSecond,
            GapsLast5Minutes: metrics.GapsLast5Minutes,
            SequenceErrorsLast5Minutes: metrics.SequenceErrorsLast5Minutes,
            AnomaliesLast5Minutes: metrics.AnomaliesLast5Minutes,
            AverageLatencyMs: metrics.AverageLatencyMs,
            SymbolsWithIssues: metrics.SymbolsWithIssues,
            SymbolHealth: metrics.SymbolHealth.Select(ToResponse).ToArray());

    private static QualitySymbolHealthResponse ToResponse(SymbolHealthStatus status) =>
        new(
            Symbol: status.Symbol,
            State: (byte)status.State,
            Score: status.Score,
            LastEvent: status.LastEvent,
            TimeSinceLastEvent: status.TimeSinceLastEvent,
            ActiveIssues: status.ActiveIssues);

    private static QualityCompletenessSummaryResponse ToResponse(CompletenessSummary summary) =>
        new(
            TotalSymbolDates: summary.TotalSymbolDates,
            AverageScore: summary.AverageScore,
            MinScore: summary.MinScore,
            MaxScore: summary.MaxScore,
            SymbolsTracked: summary.SymbolsTracked,
            DatesTracked: summary.DatesTracked,
            TotalEvents: summary.TotalEvents,
            TotalExpectedEvents: summary.TotalExpectedEvents,
            OverallCoverage: summary.OverallCoverage,
            GradeDistribution: new Dictionary<string, int>(summary.GradeDistribution),
            CalculatedAt: summary.CalculatedAt);

    private static QualityGapStatisticsResponse ToResponse(GapStatistics statistics) =>
        new(
            TotalGaps: statistics.TotalGaps,
            TotalGapDuration: statistics.TotalGapDuration,
            AverageGapDuration: statistics.AverageGapDuration,
            MaxGapDuration: statistics.MaxGapDuration,
            MinGapDuration: statistics.MinGapDuration,
            GapsBySeverity: statistics.GapsBySeverity.ToDictionary(entry => (byte)entry.Key, entry => entry.Value),
            SymbolsAffected: statistics.SymbolsAffected,
            MostAffectedSymbols: statistics.MostAffectedSymbols.ToArray(),
            CalculatedAt: statistics.CalculatedAt);

    private static QualitySequenceErrorStatisticsResponse ToResponse(SequenceErrorStatistics statistics) =>
        new(
            TotalEventsChecked: statistics.TotalEventsChecked,
            TotalErrors: statistics.TotalErrors,
            ErrorRate: statistics.ErrorRate,
            ErrorsByType: statistics.ErrorsByType.ToDictionary(entry => (byte)entry.Key, entry => entry.Value),
            SymbolsWithErrors: statistics.SymbolsWithErrors,
            AverageGapSize: statistics.AverageGapSize,
            MaxGapSize: statistics.MaxGapSize,
            CalculatedAt: statistics.CalculatedAt)
        {
            LifetimeTotalErrors = statistics.LifetimeTotalErrors,
            LifetimeErrorRate = statistics.LifetimeErrorRate
        };

    private static QualityAnomalyStatisticsResponse ToResponse(AnomalyStatistics statistics) =>
        new(
            TotalAnomalies: statistics.TotalAnomalies,
            AnomaliesByType: statistics.AnomaliesByType.ToDictionary(entry => (byte)entry.Key, entry => entry.Value),
            AnomaliesBySeverity: statistics.AnomaliesBySeverity.ToDictionary(entry => (byte)entry.Key, entry => entry.Value),
            SymbolsWithMostAnomalies: statistics.SymbolsWithMostAnomalies
                .Select(entry => new QualityCountBySymbolResponse(entry.Symbol, entry.Count))
                .ToArray(),
            UnacknowledgedCount: statistics.UnacknowledgedCount,
            AnomaliesLast24Hours: statistics.AnomaliesLast24Hours,
            CalculatedAt: statistics.CalculatedAt);

    private static QualityLatencyStatisticsResponse ToResponse(LatencyStatistics statistics) =>
        new(
            SymbolsTracked: statistics.SymbolsTracked,
            TotalSamples: statistics.TotalSamples,
            GlobalMeanMs: statistics.GlobalMeanMs,
            GlobalP50Ms: statistics.GlobalP50Ms,
            GlobalP90Ms: statistics.GlobalP90Ms,
            GlobalP99Ms: statistics.GlobalP99Ms,
            FastestSymbol: statistics.FastestSymbol,
            SlowestSymbol: statistics.SlowestSymbol,
            DistributionsBySymbol: new Dictionary<string, double>(statistics.DistributionsBySymbol),
            CalculatedAt: statistics.CalculatedAt);

    private static QualityGapResponse ToResponse(DataGap gap) =>
        new(
            Symbol: gap.Symbol,
            EventType: gap.EventType,
            GapStart: gap.GapStart,
            GapEnd: gap.GapEnd,
            Duration: gap.Duration,
            MissedSequenceStart: gap.MissedSequenceStart,
            MissedSequenceEnd: gap.MissedSequenceEnd,
            EstimatedMissedEvents: gap.EstimatedMissedEvents,
            Severity: (byte)gap.Severity,
            PossibleCause: gap.PossibleCause);

    private static QualitySequenceErrorResponse ToResponse(SequenceError error) =>
        new(
            Timestamp: error.Timestamp,
            Symbol: error.Symbol,
            EventType: error.EventType,
            ErrorType: (byte)error.ErrorType,
            ExpectedSequence: error.ExpectedSequence,
            ActualSequence: error.ActualSequence,
            GapSize: error.GapSize,
            StreamId: error.StreamId,
            Provider: error.Provider);

    private static QualityAnomalyResponse ToResponse(DataAnomaly anomaly) =>
        new(
            Id: anomaly.Id,
            Timestamp: anomaly.Timestamp,
            Symbol: anomaly.Symbol,
            Type: (byte)anomaly.Type,
            Severity: (byte)anomaly.Severity,
            Description: anomaly.Description,
            ExpectedValue: anomaly.ExpectedValue,
            ActualValue: anomaly.ActualValue,
            DeviationPercent: anomaly.DeviationPercent,
            ZScore: anomaly.ZScore,
            Provider: anomaly.Provider,
            IsAcknowledged: anomaly.IsAcknowledged,
            DetectedAt: anomaly.DetectedAt);

    private static QualityComparisonResponse ToResponse(CrossProviderComparison comparison) =>
        new(
            Symbol: comparison.Symbol,
            Date: comparison.Date,
            EventType: comparison.EventType,
            Providers: comparison.Providers.Select(ToResponse).ToArray(),
            Discrepancies: comparison.Discrepancies.Select(ToResponse).ToArray(),
            RecommendedProvider: comparison.RecommendedProvider,
            ComparedAt: comparison.ComparedAt);

    private static QualityProviderDataSummaryResponse ToResponse(ProviderDataSummary summary) =>
        new(
            Provider: summary.Provider,
            EventCount: summary.EventCount,
            FirstEvent: summary.FirstEvent,
            LastEvent: summary.LastEvent,
            Coverage: summary.Coverage,
            GapCount: summary.GapCount,
            CompletenessScore: summary.CompletenessScore,
            Latency: summary.Latency,
            IsRecommended: summary.IsRecommended);

    private static QualityProviderDiscrepancyResponse ToResponse(ProviderDiscrepancy discrepancy) =>
        new(
            Timestamp: discrepancy.Timestamp,
            DiscrepancyType: discrepancy.DiscrepancyType,
            Provider1: discrepancy.Provider1,
            Provider2: discrepancy.Provider2,
            Field: discrepancy.Field,
            Value1: discrepancy.Value1,
            Value2: discrepancy.Value2,
            Difference: discrepancy.Difference,
            Severity: (byte)discrepancy.Severity);

    /// <summary>
    /// Maps SLA monitoring endpoints (ADQ-4.6).
    /// </summary>
    public static void MapSlaEndpoints(this WebApplication app, DataFreshnessSlaMonitor slaMonitor)
    {
        var guard = CreateGuard(app);
        app.MapGet(UiApiRoutes.SlaStatus, () =>
            guard.HandleSync(() => Json(slaMonitor.GetSnapshot())));

        app.MapGet(UiApiRoutes.SlaStatusBySymbol, (string symbol) =>
            guard.HandleSync(() =>
            {
                var status = slaMonitor.GetSymbolStatus(symbol);
                return status != null
                    ? Json(status)
                    : Results.NotFound($"No SLA data for {symbol}");
            }));

        app.MapGet(UiApiRoutes.SlaViolations, () =>
            guard.HandleSync(() =>
            {
                var snapshot = slaMonitor.GetSnapshot();
                var violations = snapshot.SymbolStatuses
                    .Where(s => s.State == SlaState.Violation)
                    .ToList();

                return Json(new
                {
                    count = violations.Count,
                    totalViolations = snapshot.TotalViolations,
                    violations
                });
            }));

        app.MapGet(UiApiRoutes.SlaHealth, () =>
            guard.HandleSync(() =>
            {
                var snapshot = slaMonitor.GetSnapshot();
                var status = snapshot.OverallFreshnessScore switch
                {
                    >= 90 => "healthy",
                    >= 70 => "degraded",
                    _ => "unhealthy"
                };

                return Json(new
                {
                    status,
                    score = snapshot.OverallFreshnessScore,
                    totalSymbols = snapshot.TotalSymbols,
                    healthySymbols = snapshot.HealthySymbols,
                    warningSymbols = snapshot.WarningSymbols,
                    violationSymbols = snapshot.ViolationSymbols,
                    noDataSymbols = snapshot.NoDataSymbols,
                    totalViolations = snapshot.TotalViolations,
                    isMarketOpen = snapshot.IsMarketOpen,
                    timestamp = snapshot.Timestamp
                });
            }));

        app.MapGet(UiApiRoutes.SlaMetrics, () =>
            guard.HandleSync(() => Json(new
            {
                totalViolations = slaMonitor.TotalViolations,
                currentViolations = slaMonitor.CurrentViolations,
                totalRecoveries = slaMonitor.TotalRecoveries,
                isMarketOpen = slaMonitor.IsMarketOpen(),
                timestamp = DateTimeOffset.UtcNow
            })));
    }

    private static DataQualityEndpointGuard CreateGuard(WebApplication app)
        => new(app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Meridian.Ui.Shared.Endpoints.DataQualityEndpoints"));

    private sealed class DataQualityEndpointGuard(ILogger logger)
    {
        private const string SafeFailureDetail =
            "The data-quality request could not be completed. Retry or contact an administrator.";

        public IResult HandleSync(Func<IResult> handler)
        {
            try
            {
                return handler();
            }
            catch (DataQualityRequestValidationException exception)
            {
                return ApiProblemDetails.Validation(null, exception.Field, exception.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Data-quality endpoint failed.");
                return ApiProblemDetails.Internal(null, SafeFailureDetail);
            }
        }

        public async Task<IResult> HandleAsync(
            Func<Task<IResult>> handler,
            CancellationToken ct = default)
        {
            try
            {
                return await handler().ConfigureAwait(false);
            }
            catch (DataQualityRequestValidationException exception)
            {
                return ApiProblemDetails.Validation(null, exception.Field, exception.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Data-quality endpoint failed.");
                return ApiProblemDetails.Internal(null, SafeFailureDetail);
            }
        }
    }

    private sealed class DataQualityRequestValidationException(string field, string message)
        : Exception(message)
    {
        public string Field { get; } = field;
    }
}

/// <summary>
/// Request DTO for report export.
/// </summary>
public record ReportExportRequest(
    string? Date,
    string? Format
);
