using System.Text;
using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Generates comprehensive daily and weekly data quality reports with export functionality.
/// </summary>
public sealed class DataQualityReportGenerator
{
    private readonly ILogger _log = LoggingSetup.ForContext<DataQualityReportGenerator>();
    private readonly CompletenessScoreCalculator _completeness;
    private readonly GapAnalyzer _gapAnalyzer;
    private readonly SequenceErrorTracker _sequenceTracker;
    private readonly AnomalyDetector _anomalyDetector;
    private readonly LatencyHistogram _latencyHistogram;
    private readonly CrossProviderComparisonService? _crossProvider;
    private readonly string _outputDirectory;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public DataQualityReportGenerator(
        CompletenessScoreCalculator completeness,
        GapAnalyzer gapAnalyzer,
        SequenceErrorTracker sequenceTracker,
        AnomalyDetector anomalyDetector,
        LatencyHistogram latencyHistogram,
        CrossProviderComparisonService? crossProvider = null,
        string? outputDirectory = null)
    {
        _completeness = completeness;
        _gapAnalyzer = gapAnalyzer;
        _sequenceTracker = sequenceTracker;
        _anomalyDetector = anomalyDetector;
        _latencyHistogram = latencyHistogram;
        _crossProvider = crossProvider;
        _outputDirectory = outputDirectory ?? Path.Combine("data", "reports");

        Directory.CreateDirectory(_outputDirectory);
        _log.Information("DataQualityReportGenerator initialized with output directory: {OutputDir}", _outputDirectory);
    }

    /// <summary>
    /// Generates a daily quality report.
    /// </summary>
    public Task<DailyQualityReport> GenerateDailyReportAsync(
        DateOnly date,
        ReportGenerationOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= ReportGenerationOptions.Default;
        _log.Information("Generating daily quality report for {Date}", date);

        var completenessScores = _completeness.GetScoresForDate(date);
        var gaps = _gapAnalyzer.GetGapsForDate(date);
        var sequenceErrors = _sequenceTracker.GetErrorsForDate(date);
        var anomalies = _anomalyDetector.GetAnomaliesForDate(date);
        var latencyStats = _latencyHistogram.GetStatistics();

        // Filter by symbols if specified
        if (options.Symbols.Length > 0 && !options.IncludeAllSymbols)
        {
            var symbolSet = new HashSet<string>(options.Symbols, StringComparer.OrdinalIgnoreCase);
            completenessScores = completenessScores.Where(s => symbolSet.Contains(s.Symbol)).ToList();
            gaps = gaps.Where(g => symbolSet.Contains(g.Symbol)).ToList();
            sequenceErrors = sequenceErrors.Where(e => symbolSet.Contains(e.Symbol)).ToList();
            anomalies = anomalies.Where(a => symbolSet.Contains(a.Symbol)).ToList();
        }

        // Calculate overall scores
        var overallScore = completenessScores.Count > 0
            ? completenessScores.Average(s => s.Score)
            : 0;

        var integrityScore = sequenceErrors.Count > 0
            ? Math.Max(0, 1.0 - (sequenceErrors.Count / 1000.0))
            : 1.0;

        var timelinessScore = latencyStats.GlobalP99Ms switch
        {
            < 100 => 1.0,
            < 500 => 0.9,
            < 1000 => 0.8,
            < 5000 => 0.6,
            _ => 0.4
        };

        // Build symbol summaries
        var symbolSummaries = BuildSymbolSummaries(completenessScores, gaps, sequenceErrors, anomalies);

        // Filter by score threshold
        if (options.MinScoreThreshold > 0)
        {
            symbolSummaries = symbolSummaries
                .Where(s => s.OverallScore < options.MinScoreThreshold)
                .ToList();
        }

        // Limit results
        var significantGaps = gaps
            .Where(g => g.Severity >= GapSeverity.Significant)
            .OrderByDescending(g => g.Duration)
            .Take(options.MaxGapsPerSymbol)
            .ToList();

        var significantErrors = sequenceErrors
            .Where(e => e.GapSize > 10)
            .OrderByDescending(e => e.GapSize)
            .Take(options.MaxErrorsPerSymbol)
            .ToList();

        var limitedAnomalies = anomalies
            .Where(a => a.Severity >= AnomalySeverity.Warning)
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.Timestamp)
            .Take(options.MaxAnomaliesPerSymbol)
            .ToList();

        // Generate recommendations
        var recommendations = options.IncludeRecommendations
            ? GenerateRecommendations(symbolSummaries, significantGaps, significantErrors, limitedAnomalies)
            : new List<string>();

        // Calculate statistics
        var statistics = new ReportStatistics(
            TotalEvents: completenessScores.Sum(s => s.ActualEvents),
            ExpectedEvents: completenessScores.Sum(s => s.ExpectedEvents),
            MissingEvents: completenessScores.Sum(s => s.MissingEvents),
            TotalGaps: gaps.Count,
            TotalGapDuration: gaps.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Duration),
            TotalSequenceErrors: sequenceErrors.Count,
            TotalAnomalies: anomalies.Count,
            AverageLatencyMs: latencyStats.GlobalMeanMs,
            P99LatencyMs: latencyStats.GlobalP99Ms
        );

        var report = new DailyQualityReport(
            Date: date,
            GeneratedAt: DateTimeOffset.UtcNow,
            SymbolsAnalyzed: completenessScores.Select(s => s.Symbol).Distinct().Count(),
            OverallScore: Math.Round(overallScore, 4),
            CompletenessScore: Math.Round(overallScore, 4),
            IntegrityScore: Math.Round(integrityScore, 4),
            TimelinessScore: Math.Round(timelinessScore, 4),
            SymbolSummaries: symbolSummaries,
            SignificantGaps: significantGaps,
            SignificantErrors: significantErrors,
            Anomalies: limitedAnomalies,
            Recommendations: recommendations,
            Statistics: statistics
        );

        _log.Information("Daily report generated: {SymbolCount} symbols, score: {Score:F2}",
            report.SymbolsAnalyzed, report.OverallScore);

        return Task.FromResult(report);
    }

    /// <summary>
    /// Generates a weekly quality report.
    /// </summary>
    public async Task<WeeklyQualityReport> GenerateWeeklyReportAsync(
        DateOnly weekStart,
        ReportGenerationOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= ReportGenerationOptions.Default;
        var weekEnd = weekStart.AddDays(6);

        _log.Information("Generating weekly quality report for {WeekStart} to {WeekEnd}", weekStart, weekEnd);

        var dailyReports = new List<DailyQualityReport>();
        var currentDate = weekStart;

        while (currentDate <= weekEnd)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var dailyReport = await GenerateDailyReportAsync(currentDate, options, ct);
                dailyReports.Add(dailyReport);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to generate daily report for {Date}", currentDate);
            }
            currentDate = currentDate.AddDays(1);
        }

        if (dailyReports.Count == 0)
        {
            return new WeeklyQualityReport(
                WeekStart: weekStart,
                WeekEnd: weekEnd,
                GeneratedAt: DateTimeOffset.UtcNow,
                DailyReports: dailyReports,
                AverageScore: 0,
                ScoreTrend: 0,
                TopIssues: Array.Empty<string>(),
                Improvements: Array.Empty<string>(),
                Recommendations: Array.Empty<string>(),
                Statistics: new WeeklyStatistics(0, 0, 0, 0, 0, 0, 0, 0, weekStart, weekStart)
            );
        }

        var avgScore = dailyReports.Average(r => r.OverallScore);
        var scoreTrend = CalculateScoreTrend(dailyReports);
        var topIssues = IdentifyTopIssues(dailyReports);
        var improvements = IdentifyImprovements(dailyReports);
        var recommendations = GenerateWeeklyRecommendations(dailyReports, topIssues);

        var bestDay = dailyReports.OrderByDescending(r => r.OverallScore).First();
        var worstDay = dailyReports.OrderBy(r => r.OverallScore).First();

        var statistics = new WeeklyStatistics(
            TotalEvents: dailyReports.Sum(r => r.Statistics.TotalEvents),
            EventsPerDay: (long)dailyReports.Average(r => r.Statistics.TotalEvents),
            TotalGaps: dailyReports.Sum(r => r.Statistics.TotalGaps),
            GapsPerDay: (int)dailyReports.Average(r => r.Statistics.TotalGaps),
            TotalAnomalies: dailyReports.Sum(r => r.Statistics.TotalAnomalies),
            AverageScore: avgScore,
            BestDayScore: bestDay.OverallScore,
            WorstDayScore: worstDay.OverallScore,
            BestDay: bestDay.Date,
            WorstDay: worstDay.Date
        );

        var report = new WeeklyQualityReport(
            WeekStart: weekStart,
            WeekEnd: weekEnd,
            GeneratedAt: DateTimeOffset.UtcNow,
            DailyReports: dailyReports,
            AverageScore: Math.Round(avgScore, 4),
            ScoreTrend: Math.Round(scoreTrend, 4),
            TopIssues: topIssues,
            Improvements: improvements,
            Recommendations: recommendations,
            Statistics: statistics
        );

        _log.Information("Weekly report generated: {DayCount} days, avg score: {Score:F2}, trend: {Trend:+0.00;-0.00;0}",
            dailyReports.Count, report.AverageScore, report.ScoreTrend);

        return report;
    }

    /// <summary>
    /// Exports a report to the specified format.
    /// </summary>
    public async Task<string> ExportReportAsync(
        DailyQualityReport report,
        ReportExportFormat format,
        CancellationToken ct = default)
    {
        var fileName = $"quality_report_{report.Date:yyyy-MM-dd}";
        var filePath = Path.Combine(_outputDirectory, $"{fileName}.{GetExtension(format)}");

        var content = format switch
        {
            ReportExportFormat.Json => ExportToJson(report),
            ReportExportFormat.Csv => ExportToCsv(report),
            ReportExportFormat.Html => ExportToHtml(report),
            ReportExportFormat.Markdown => ExportToMarkdown(report),
            _ => ExportToJson(report)
        };

        await AtomicFileWriter.WriteAsync(filePath, content, ct);
        _log.Information("Report exported to {FilePath}", filePath);

        return filePath;
    }

    /// <summary>
    /// Exports a weekly report to the specified format.
    /// </summary>
    public async Task<string> ExportWeeklyReportAsync(
        WeeklyQualityReport report,
        ReportExportFormat format,
        CancellationToken ct = default)
    {
        var fileName = $"weekly_quality_report_{report.WeekStart:yyyy-MM-dd}";
        var filePath = Path.Combine(_outputDirectory, $"{fileName}.{GetExtension(format)}");

        var content = format switch
        {
            ReportExportFormat.Json => ExportWeeklyToJson(report),
            ReportExportFormat.Html => ExportWeeklyToHtml(report),
            ReportExportFormat.Markdown => ExportWeeklyToMarkdown(report),
            _ => ExportWeeklyToJson(report)
        };

        await AtomicFileWriter.WriteAsync(filePath, content, ct);
        _log.Information("Weekly report exported to {FilePath}", filePath);

        return filePath;
    }

    private List<SymbolQualitySummary> BuildSymbolSummaries(
        IReadOnlyList<CompletenessScore> completeness,
        IReadOnlyList<DataGap> gaps,
        IReadOnlyList<SequenceError> errors,
        IReadOnlyList<DataAnomaly> anomalies)
    {
        var symbols = completeness.Select(c => c.Symbol).Distinct().ToList();
        var summaries = new List<SymbolQualitySummary>();

        foreach (var symbol in symbols)
        {
            var score = completeness.FirstOrDefault(c => c.Symbol == symbol);
            var symbolGaps = gaps.Where(g => g.Symbol == symbol).ToList();
            var symbolErrors = errors.Where(e => e.Symbol == symbol).ToList();
            var symbolAnomalies = anomalies.Where(a => a.Symbol == symbol).ToList();

            var overallScore = score?.Score ?? 0;
            var issues = new List<string>();

            if (symbolGaps.Count > 0)
                issues.Add($"{symbolGaps.Count} gaps ({symbolGaps.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Duration).TotalMinutes:F1} min total)");
            if (symbolErrors.Count > 0)
                issues.Add($"{symbolErrors.Count} sequence errors");
            if (symbolAnomalies.Count > 0)
                issues.Add($"{symbolAnomalies.Count} anomalies");

            summaries.Add(new SymbolQualitySummary(
                Symbol: symbol,
                OverallScore: Math.Round(overallScore, 4),
                CompletenessScore: score?.Score ?? 0,
                TotalEvents: score?.ActualEvents ?? 0,
                GapCount: symbolGaps.Count,
                TotalGapDuration: symbolGaps.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Duration),
                SequenceErrors: symbolErrors.Count,
                Anomalies: symbolAnomalies.Count,
                Grade: score?.Grade ?? "F",
                Issues: issues.ToArray()
            ));
        }

        return summaries.OrderBy(s => s.OverallScore).ToList();
    }

    private List<string> GenerateRecommendations(
        List<SymbolQualitySummary> summaries,
        List<DataGap> gaps,
        List<SequenceError> errors,
        List<DataAnomaly> anomalies)
    {
        var recommendations = new List<string>();

        var lowScoreSymbols = summaries.Where(s => s.OverallScore < 0.8).ToList();
        if (lowScoreSymbols.Count > 0)
        {
            recommendations.Add($"Investigate {lowScoreSymbols.Count} symbols with low quality scores (<80%)");
        }

        var symbolsWithManyGaps = summaries.Where(s => s.GapCount > 5).ToList();
        if (symbolsWithManyGaps.Count > 0)
        {
            recommendations.Add($"Check provider connectivity for {symbolsWithManyGaps.Count} symbols with frequent gaps");
        }

        var criticalAnomalies = anomalies.Where(a => a.Severity == AnomalySeverity.Critical).ToList();
        if (criticalAnomalies.Count > 0)
        {
            recommendations.Add($"Review {criticalAnomalies.Count} critical anomalies requiring immediate attention");
        }

        var largeGaps = gaps.Where(g => g.Duration > TimeSpan.FromMinutes(30)).ToList();
        if (largeGaps.Count > 0)
        {
            recommendations.Add($"Investigate {largeGaps.Count} significant gaps (>30 min) - consider backfill");
        }

        if (errors.Count > 100)
        {
            recommendations.Add("High sequence error rate detected - verify provider data stream integrity");
        }

        return recommendations;
    }

    private double CalculateScoreTrend(List<DailyQualityReport> reports)
    {
        if (reports.Count < 2)
            return 0;

        var ordered = reports.OrderBy(r => r.Date).ToList();
        var firstHalf = ordered.Take(ordered.Count / 2).Average(r => r.OverallScore);
        var secondHalf = ordered.Skip(ordered.Count / 2).Average(r => r.OverallScore);

        return secondHalf - firstHalf;
    }

    private List<string> IdentifyTopIssues(List<DailyQualityReport> reports)
    {
        var issues = new List<string>();

        var totalGaps = reports.Sum(r => r.SignificantGaps.Count);
        if (totalGaps > 0)
        {
            issues.Add($"Data gaps: {totalGaps} significant gaps across the week");
        }

        var totalErrors = reports.Sum(r => r.SignificantErrors.Count);
        if (totalErrors > 0)
        {
            issues.Add($"Sequence errors: {totalErrors} significant errors detected");
        }

        var totalAnomalies = reports.Sum(r => r.Anomalies.Count);
        if (totalAnomalies > 0)
        {
            issues.Add($"Anomalies: {totalAnomalies} data anomalies identified");
        }

        return issues;
    }

    private List<string> IdentifyImprovements(List<DailyQualityReport> reports)
    {
        var improvements = new List<string>();

        if (reports.Count < 2)
            return improvements;

        var ordered = reports.OrderBy(r => r.Date).ToList();
        var lastDay = ordered.Last();
        var firstDay = ordered.First();

        if (lastDay.OverallScore > firstDay.OverallScore)
        {
            improvements.Add($"Quality score improved from {firstDay.OverallScore:P0} to {lastDay.OverallScore:P0}");
        }

        if (lastDay.Statistics.TotalGaps < firstDay.Statistics.TotalGaps)
        {
            improvements.Add($"Gap count reduced from {firstDay.Statistics.TotalGaps} to {lastDay.Statistics.TotalGaps}");
        }

        return improvements;
    }

    private List<string> GenerateWeeklyRecommendations(List<DailyQualityReport> reports, List<string> issues)
    {
        var recommendations = new List<string>();

        if (reports.Average(r => r.OverallScore) < 0.9)
        {
            recommendations.Add("Overall quality below 90% - consider provider diversity or backfill strategies");
        }

        if (issues.Any(i => i.Contains("gaps")))
        {
            recommendations.Add("Implement automated gap detection alerts for faster response");
        }

        if (issues.Any(i => i.Contains("anomalies")))
        {
            recommendations.Add("Review anomaly detection thresholds for false positive reduction");
        }

        return recommendations;
    }

    private static string GetExtension(ReportExportFormat format) => format switch
    {
        ReportExportFormat.Json => "json",
        ReportExportFormat.Csv => "csv",
        ReportExportFormat.Html => "html",
        ReportExportFormat.Markdown => "md",
        _ => "json"
    };

    private string ExportToJson(DailyQualityReport report)
    {
        return JsonSerializer.Serialize(report, s_jsonOptions);
    }

    private string ExportToCsv(DailyQualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Symbol,OverallScore,CompletenessScore,TotalEvents,GapCount,SequenceErrors,Anomalies,Grade");

        foreach (var summary in report.SymbolSummaries)
        {
            sb.AppendLine($"{summary.Symbol},{summary.OverallScore:F4},{summary.CompletenessScore:F4}," +
                $"{summary.TotalEvents},{summary.GapCount},{summary.SequenceErrors},{summary.Anomalies},{summary.Grade}");
        }

        return sb.ToString();
    }

    private string ExportToHtml(DailyQualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><title>Data Quality Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        sb.AppendLine("th { background-color: #4CAF50; color: white; }");
        sb.AppendLine(".good { color: green; } .warning { color: orange; } .error { color: red; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>Data Quality Report - {report.Date:yyyy-MM-dd}</h1>");
        sb.AppendLine($"<p>Generated: {report.GeneratedAt:O}</p>");

        sb.AppendLine("<h2>Summary</h2>");
        sb.AppendLine($"<p>Overall Score: <strong class='{GetScoreClass(report.OverallScore)}'>{report.OverallScore:P1}</strong></p>");
        sb.AppendLine($"<p>Symbols Analyzed: {report.SymbolsAnalyzed}</p>");
        sb.AppendLine($"<p>Total Events: {report.Statistics.TotalEvents:N0}</p>");

        sb.AppendLine("<h2>Symbol Quality</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Symbol</th><th>Score</th><th>Events</th><th>Gaps</th><th>Errors</th><th>Grade</th></tr>");

        foreach (var s in report.SymbolSummaries.Take(50))
        {
            sb.AppendLine($"<tr><td>{s.Symbol}</td><td class='{GetScoreClass(s.OverallScore)}'>{s.OverallScore:P1}</td>" +
                $"<td>{s.TotalEvents:N0}</td><td>{s.GapCount}</td><td>{s.SequenceErrors}</td><td>{s.Grade}</td></tr>");
        }

        sb.AppendLine("</table>");

        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine("<h2>Recommendations</h2><ul>");
            foreach (var rec in report.Recommendations)
            {
                sb.AppendLine($"<li>{rec}</li>");
            }
            sb.AppendLine("</ul>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private string ExportToMarkdown(DailyQualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Data Quality Report - {report.Date:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {report.GeneratedAt:O}");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine($"- **Overall Score:** {report.OverallScore:P1}");
        sb.AppendLine($"- **Completeness:** {report.CompletenessScore:P1}");
        sb.AppendLine($"- **Integrity:** {report.IntegrityScore:P1}");
        sb.AppendLine($"- **Timeliness:** {report.TimelinessScore:P1}");
        sb.AppendLine($"- **Symbols Analyzed:** {report.SymbolsAnalyzed}");
        sb.AppendLine();

        sb.AppendLine("## Statistics");
        sb.AppendLine($"- Total Events: {report.Statistics.TotalEvents:N0}");
        sb.AppendLine($"- Total Gaps: {report.Statistics.TotalGaps}");
        sb.AppendLine($"- Sequence Errors: {report.Statistics.TotalSequenceErrors}");
        sb.AppendLine($"- Anomalies: {report.Statistics.TotalAnomalies}");
        sb.AppendLine();

        sb.AppendLine("## Symbol Quality");
        sb.AppendLine("| Symbol | Score | Events | Gaps | Errors | Grade |");
        sb.AppendLine("|--------|-------|--------|------|--------|-------|");

        foreach (var s in report.SymbolSummaries.Take(20))
        {
            sb.AppendLine($"| {s.Symbol} | {s.OverallScore:P1} | {s.TotalEvents:N0} | {s.GapCount} | {s.SequenceErrors} | {s.Grade} |");
        }

        sb.AppendLine();

        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine("## Recommendations");
            foreach (var rec in report.Recommendations)
            {
                sb.AppendLine($"- {rec}");
            }
        }

        return sb.ToString();
    }

    private string ExportWeeklyToJson(WeeklyQualityReport report)
    {
        return JsonSerializer.Serialize(report, s_jsonOptions);
    }

    private string ExportWeeklyToHtml(WeeklyQualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><title>Weekly Data Quality Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-bottom: 20px; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        sb.AppendLine("th { background-color: #2196F3; color: white; }");
        sb.AppendLine(".trend-up { color: green; } .trend-down { color: red; } .trend-flat { color: gray; }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>Weekly Data Quality Report</h1>");
        sb.AppendLine($"<p>Week: {report.WeekStart:yyyy-MM-dd} to {report.WeekEnd:yyyy-MM-dd}</p>");
        sb.AppendLine($"<p>Generated: {report.GeneratedAt:O}</p>");

        var trendClass = report.ScoreTrend > 0 ? "trend-up" : (report.ScoreTrend < 0 ? "trend-down" : "trend-flat");
        var trendSymbol = report.ScoreTrend > 0 ? "▲" : (report.ScoreTrend < 0 ? "▼" : "→");

        sb.AppendLine($"<h2>Average Score: {report.AverageScore:P1} <span class='{trendClass}'>{trendSymbol} {Math.Abs(report.ScoreTrend):P1}</span></h2>");

        sb.AppendLine("<h3>Daily Breakdown</h3>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Date</th><th>Score</th><th>Events</th><th>Gaps</th><th>Anomalies</th></tr>");

        foreach (var day in report.DailyReports)
        {
            sb.AppendLine($"<tr><td>{day.Date:yyyy-MM-dd}</td><td>{day.OverallScore:P1}</td>" +
                $"<td>{day.Statistics.TotalEvents:N0}</td><td>{day.Statistics.TotalGaps}</td>" +
                $"<td>{day.Statistics.TotalAnomalies}</td></tr>");
        }

        sb.AppendLine("</table>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private string ExportWeeklyToMarkdown(WeeklyQualityReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Weekly Data Quality Report");
        sb.AppendLine();
        sb.AppendLine($"**Week:** {report.WeekStart:yyyy-MM-dd} to {report.WeekEnd:yyyy-MM-dd}");
        sb.AppendLine($"**Generated:** {report.GeneratedAt:O}");
        sb.AppendLine();

        var trendSymbol = report.ScoreTrend > 0 ? "📈" : (report.ScoreTrend < 0 ? "📉" : "➡️");
        sb.AppendLine($"## Average Score: {report.AverageScore:P1} {trendSymbol} ({report.ScoreTrend:+0.0%;-0.0%;0%})");
        sb.AppendLine();

        sb.AppendLine("## Daily Breakdown");
        sb.AppendLine("| Date | Score | Events | Gaps | Anomalies |");
        sb.AppendLine("|------|-------|--------|------|-----------|");

        foreach (var day in report.DailyReports)
        {
            sb.AppendLine($"| {day.Date:yyyy-MM-dd} | {day.OverallScore:P1} | {day.Statistics.TotalEvents:N0} | " +
                $"{day.Statistics.TotalGaps} | {day.Statistics.TotalAnomalies} |");
        }

        sb.AppendLine();

        if (report.TopIssues.Count > 0)
        {
            sb.AppendLine("## Top Issues");
            foreach (var issue in report.TopIssues)
            {
                sb.AppendLine($"- ⚠️ {issue}");
            }
            sb.AppendLine();
        }

        if (report.Improvements.Count > 0)
        {
            sb.AppendLine("## Improvements");
            foreach (var imp in report.Improvements)
            {
                sb.AppendLine($"- ✅ {imp}");
            }
            sb.AppendLine();
        }

        if (report.Recommendations.Count > 0)
        {
            sb.AppendLine("## Recommendations");
            foreach (var rec in report.Recommendations)
            {
                sb.AppendLine($"- 💡 {rec}");
            }
        }

        return sb.ToString();
    }

    private static string GetScoreClass(double score) => score switch
    {
        >= 0.9 => "good",
        >= 0.7 => "warning",
        _ => "error"
    };
}
