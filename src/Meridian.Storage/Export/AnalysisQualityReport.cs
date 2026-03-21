using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Serialization;
using Meridian.Storage.Archival;
using Serilog;

namespace Meridian.Storage.Export;

/// <summary>
/// Generates analysis-ready data quality reports for exported datasets.
/// Focuses on metrics relevant to quantitative analysis and research.
/// </summary>
public sealed class AnalysisQualityReportGenerator
{
    private readonly ILogger _log = LoggingSetup.ForContext<AnalysisQualityReportGenerator>();

    /// <summary>
    /// Generate a comprehensive quality report for the exported data.
    /// </summary>
    public async Task<AnalysisQualityReport> GenerateReportAsync(
        ExportResult exportResult,
        ExportRequest request,
        CancellationToken ct = default)
    {
        var report = new AnalysisQualityReport
        {
            GeneratedAt = DateTime.UtcNow,
            ExportProfileId = exportResult.ProfileId,
            DateRange = exportResult.DateRange,
            Symbols = exportResult.Symbols ?? Array.Empty<string>()
        };

        // Analyze each exported file
        foreach (var file in exportResult.Files ?? Array.Empty<ExportedFile>())
        {
            var fileAnalysis = await AnalyzeFileAsync(file, ct);
            report.FileAnalyses.Add(fileAnalysis);
        }

        // Calculate aggregate statistics
        CalculateAggregateStats(report);

        // Detect potential issues
        DetectIssues(report);

        // Generate recommendations
        GenerateRecommendations(report);

        // Calculate overall quality score
        report.OverallQualityScore = CalculateQualityScore(report);
        report.QualityGrade = GetQualityGrade(report.OverallQualityScore);

        return report;
    }

    /// <summary>
    /// Export the quality report to various formats.
    /// </summary>
    public async Task ExportReportAsync(
        AnalysisQualityReport report,
        string outputDirectory,
        ReportFormat format = ReportFormat.All,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDirectory);

        if (format.HasFlag(ReportFormat.Markdown))
        {
            var mdPath = Path.Combine(outputDirectory, "quality_report.md");
            await AtomicFileWriter.WriteAsync(mdPath, GenerateMarkdownReport(report), ct);
            _log.Information("Quality report exported to {Path}", mdPath);
        }

        if (format.HasFlag(ReportFormat.Json))
        {
            var jsonPath = Path.Combine(outputDirectory, "quality_report.json");
            var json = JsonSerializer.Serialize(report, MarketDataJsonContext.PrettyPrintOptions);
            await AtomicFileWriter.WriteAsync(jsonPath, json, ct);
            _log.Information("Quality report exported to {Path}", jsonPath);
        }

        if (format.HasFlag(ReportFormat.Csv))
        {
            // Export issues as CSV
            var issuesPath = Path.Combine(outputDirectory, "quality_issues.csv");
            await ExportIssuesToCsvAsync(report, issuesPath, ct);

            // Export outliers as CSV
            if (report.FileAnalyses.Any(f => f.Outliers.Count > 0))
            {
                var outliersPath = Path.Combine(outputDirectory, "outliers.csv");
                await ExportOutliersToCsvAsync(report, outliersPath, ct);
            }

            // Export gaps as CSV
            if (report.FileAnalyses.Any(f => f.Gaps.Count > 0))
            {
                var gapsPath = Path.Combine(outputDirectory, "gaps.csv");
                await ExportGapsToCsvAsync(report, gapsPath, ct);
            }
        }
    }

    private async Task<FileQualityAnalysis> AnalyzeFileAsync(ExportedFile file, CancellationToken ct)
    {
        var analysis = new FileQualityAnalysis
        {
            FilePath = file.Path,
            Symbol = file.Symbol,
            EventType = file.EventType,
            RecordCount = file.RecordCount,
            SizeBytes = file.SizeBytes
        };

        try
        {
            // Read and analyze records
            var prices = new List<double>();
            var volumes = new List<long>();
            var timestamps = new List<DateTime>();

            await foreach (var record in ReadRecordsAsync(file.Path, ct))
            {
                if (record.TryGetValue("Timestamp", out var ts) && ts is string tsStr)
                {
                    if (DateTime.TryParse(tsStr, out var dt))
                    {
                        timestamps.Add(dt);
                    }
                }

                if (record.TryGetValue("Price", out var price) && price is double p)
                {
                    prices.Add(p);
                }

                if (record.TryGetValue("Size", out var size))
                {
                    if (size is double sz)
                        volumes.Add((long)sz);
                    else if (size is long szl)
                        volumes.Add(szl);
                }
            }

            // Calculate statistics
            if (prices.Count > 0)
            {
                analysis.PriceStats = CalculateStats(prices);
                analysis.Outliers = DetectOutliers(prices, timestamps, "Price");
            }

            if (volumes.Count > 0)
            {
                analysis.VolumeStats = CalculateStats(volumes.Select(v => (double)v).ToList());
            }

            if (timestamps.Count > 1)
            {
                analysis.TimeStats = CalculateTimeStats(timestamps);
                analysis.Gaps = DetectGaps(timestamps);
            }

            // Check data completeness
            analysis.CompletenessScore = CalculateCompleteness(timestamps);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to analyze file {Path}", file.Path);
            analysis.AnalysisErrors.Add(ex.Message);
        }

        return analysis;
    }

    private async IAsyncEnumerable<Dictionary<string, object?>> ReadRecordsAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!File.Exists(path))
            yield break;

        Stream stream = File.OpenRead(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            stream = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        }

        await using (stream)
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Dictionary<string, object?>? record = null;
                try
                {
                    var doc = JsonDocument.Parse(line);
                    record = new Dictionary<string, object?>();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        record[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => prop.Value.GetRawText()
                        };
                    }
                }
                catch
                {
                    // Skip malformed records
                }

                if (record != null)
                    yield return record;
            }
        }
    }

    private static DescriptiveStats CalculateStats(List<double> values)
    {
        if (values.Count == 0)
            return new DescriptiveStats();

        var sorted = values.OrderBy(v => v).ToList();
        var n = values.Count;

        // Calculate median correctly for both odd and even-sized lists
        var median = n % 2 == 1
            ? sorted[n / 2]
            : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;

        return new DescriptiveStats
        {
            Count = n,
            Mean = values.Average(),
            Median = median,
            Min = sorted[0],
            Max = sorted[n - 1],
            StdDev = CalculateStdDev(values),
            Percentile25 = sorted[Math.Min((int)(n * 0.25), n - 1)],
            Percentile75 = sorted[Math.Min((int)(n * 0.75), n - 1)],
            Percentile95 = sorted[Math.Min((int)(n * 0.95), n - 1)],
            Percentile99 = sorted[Math.Min((int)(n * 0.99), n - 1)]
        };
    }

    private static double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2)
            return 0;
        var mean = values.Average();
        var sumSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumSquares / (values.Count - 1));
    }

    private static TimeStats CalculateTimeStats(List<DateTime> timestamps)
    {
        var sorted = timestamps.OrderBy(t => t).ToList();
        var gaps = new List<double>();

        for (var i = 1; i < sorted.Count; i++)
        {
            gaps.Add((sorted[i] - sorted[i - 1]).TotalSeconds);
        }

        return new TimeStats
        {
            FirstTimestamp = sorted[0],
            LastTimestamp = sorted[^1],
            TotalDuration = sorted[^1] - sorted[0],
            AverageGapSeconds = gaps.Count > 0 ? gaps.Average() : 0,
            MaxGapSeconds = gaps.Count > 0 ? gaps.Max() : 0,
            MedianGapSeconds = gaps.Count > 0 ? gaps.OrderBy(g => g).ToList()[gaps.Count / 2] : 0
        };
    }

    private static List<DataOutlier> DetectOutliers(
        List<double> values,
        List<DateTime> timestamps,
        string fieldName)
    {
        var outliers = new List<DataOutlier>();
        if (values.Count < 10)
            return outliers;

        var mean = values.Average();
        var stdDev = CalculateStdDev(values);
        var threshold = 4.0; // 4 standard deviations

        for (var i = 0; i < values.Count; i++)
        {
            var zScore = Math.Abs((values[i] - mean) / stdDev);
            if (zScore > threshold)
            {
                outliers.Add(new DataOutlier
                {
                    Index = i,
                    Timestamp = i < timestamps.Count ? timestamps[i] : null,
                    FieldName = fieldName,
                    Value = values[i],
                    ZScore = zScore,
                    ExpectedRange = $"{mean - threshold * stdDev:F4} to {mean + threshold * stdDev:F4}"
                });
            }
        }

        return outliers;
    }

    private static List<DataGap> DetectGaps(List<DateTime> timestamps)
    {
        var gaps = new List<DataGap>();
        if (timestamps.Count < 2)
            return gaps;

        var sorted = timestamps.OrderBy(t => t).ToList();

        // Calculate typical gap (median)
        var gapDurations = new List<double>();
        for (var i = 1; i < sorted.Count; i++)
        {
            gapDurations.Add((sorted[i] - sorted[i - 1]).TotalSeconds);
        }

        var medianGap = gapDurations.OrderBy(g => g).ToList()[gapDurations.Count / 2];
        var gapThreshold = Math.Max(medianGap * 10, 300); // 10x median or 5 minutes

        for (var i = 1; i < sorted.Count; i++)
        {
            var gapSeconds = (sorted[i] - sorted[i - 1]).TotalSeconds;
            if (gapSeconds > gapThreshold)
            {
                // Check if it's a market closure (weekend/holiday)
                var isWeekend = sorted[i - 1].DayOfWeek == DayOfWeek.Friday &&
                               sorted[i].DayOfWeek == DayOfWeek.Monday;
                var isOvernight = sorted[i - 1].Hour >= 16 && sorted[i].Hour <= 9;

                gaps.Add(new DataGap
                {
                    StartTime = sorted[i - 1],
                    EndTime = sorted[i],
                    Duration = TimeSpan.FromSeconds(gapSeconds),
                    GapType = isWeekend ? GapType.Weekend :
                              isOvernight ? GapType.Overnight :
                              GapType.Unexpected,
                    EstimatedMissingRecords = (int)(gapSeconds / medianGap)
                });
            }
        }

        return gaps;
    }

    private static double CalculateCompleteness(List<DateTime> timestamps)
    {
        if (timestamps.Count < 2)
            return 100.0;

        var sorted = timestamps.OrderBy(t => t).ToList();
        var totalTradingMinutes = 0.0;
        var coveredMinutes = 0.0;

        // Group by date
        var byDate = sorted.GroupBy(t => t.Date);
        foreach (var day in byDate)
        {
            if (day.Key.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            // Assume 6.5 hours trading day
            totalTradingMinutes += 390;

            var first = day.Min();
            var last = day.Max();
            coveredMinutes += (last - first).TotalMinutes;
        }

        return totalTradingMinutes > 0 ? (coveredMinutes / totalTradingMinutes) * 100 : 100.0;
    }

    private void CalculateAggregateStats(AnalysisQualityReport report)
    {
        report.TotalRecords = report.FileAnalyses.Sum(f => f.RecordCount);
        report.TotalBytes = report.FileAnalyses.Sum(f => f.SizeBytes);
        report.TotalOutliers = report.FileAnalyses.Sum(f => f.Outliers.Count);
        report.TotalGaps = report.FileAnalyses.Sum(f => f.Gaps.Count);
        report.UnexpectedGaps = report.FileAnalyses.Sum(f =>
            f.Gaps.Count(g => g.GapType == GapType.Unexpected));

        // Calculate average completeness
        var completenessScores = report.FileAnalyses
            .Where(f => f.CompletenessScore > 0)
            .Select(f => f.CompletenessScore)
            .ToList();

        report.AverageCompleteness = completenessScores.Count > 0
            ? completenessScores.Average()
            : 100.0;

        // First/last timestamps
        var allTimestamps = report.FileAnalyses
            .Where(f => f.TimeStats != null)
            .Select(f => f.TimeStats!)
            .ToList();

        if (allTimestamps.Count > 0)
        {
            report.FirstTimestamp = allTimestamps.Min(t => t.FirstTimestamp);
            report.LastTimestamp = allTimestamps.Max(t => t.LastTimestamp);
        }
    }

    private void DetectIssues(AnalysisQualityReport report)
    {
        // Check for high outlier count
        if (report.TotalOutliers > 0)
        {
            var outlierRate = (double)report.TotalOutliers / report.TotalRecords * 100;
            if (outlierRate > 0.1)
            {
                report.Issues.Add(new QualityIssue
                {
                    Severity = outlierRate > 1 ? IssueSeverity.Warning : IssueSeverity.Info,
                    Category = "Outliers",
                    Description = $"{report.TotalOutliers} price outliers detected ({outlierRate:F2}%)",
                    Impact = "May affect statistical analysis and ML model training",
                    Resolution = "Consider winsorizing or removing extreme values"
                });
            }
        }

        // Check for unexpected gaps
        if (report.UnexpectedGaps > 0)
        {
            report.Issues.Add(new QualityIssue
            {
                Severity = report.UnexpectedGaps > 5 ? IssueSeverity.Warning : IssueSeverity.Info,
                Category = "Data Gaps",
                Description = $"{report.UnexpectedGaps} unexpected data gaps detected",
                Impact = "Missing data may affect time series analysis",
                Resolution = "Consider backfilling missing data or marking gaps in analysis"
            });
        }

        // Check for low completeness
        if (report.AverageCompleteness < 95)
        {
            report.Issues.Add(new QualityIssue
            {
                Severity = report.AverageCompleteness < 80 ? IssueSeverity.Error : IssueSeverity.Warning,
                Category = "Completeness",
                Description = $"Average data completeness is {report.AverageCompleteness:F1}%",
                Impact = "Incomplete data may bias backtesting results",
                Resolution = "Review data collection and consider additional data sources"
            });
        }

        // Check for analysis errors
        var errorCount = report.FileAnalyses.Sum(f => f.AnalysisErrors.Count);
        if (errorCount > 0)
        {
            report.Issues.Add(new QualityIssue
            {
                Severity = IssueSeverity.Warning,
                Category = "Parse Errors",
                Description = $"{errorCount} files had analysis errors",
                Impact = "Some statistics may be incomplete",
                Resolution = "Check file format and data integrity"
            });
        }
    }

    private void GenerateRecommendations(AnalysisQualityReport report)
    {
        // Suitability for different use cases
        report.Recommendations.Add(new AnalysisRecommendation
        {
            UseCase = "Backtesting",
            Suitable = report.AverageCompleteness > 95 && report.UnexpectedGaps < 3,
            Notes = report.AverageCompleteness > 95
                ? "Data is suitable for backtesting"
                : "Consider gap handling strategy before backtesting"
        });

        report.Recommendations.Add(new AnalysisRecommendation
        {
            UseCase = "ML Training",
            Suitable = report.TotalOutliers < report.TotalRecords * 0.001,
            Notes = report.TotalOutliers > 0
                ? $"Consider preprocessing {report.TotalOutliers} outliers"
                : "Data is suitable for ML training"
        });

        report.Recommendations.Add(new AnalysisRecommendation
        {
            UseCase = "Statistical Research",
            Suitable = report.OverallQualityScore > 90,
            Notes = report.OverallQualityScore > 90
                ? "High quality data suitable for research"
                : "Review data quality issues before analysis"
        });
    }

    private static double CalculateQualityScore(AnalysisQualityReport report)
    {
        // Weighted quality score
        var scores = new List<(double Score, double Weight)>
        {
            (report.AverageCompleteness, 0.30),
            (100 - Math.Min(report.TotalOutliers / (double)Math.Max(report.TotalRecords, 1) * 1000, 100), 0.25),
            (100 - Math.Min(report.UnexpectedGaps * 5, 100), 0.20),
            (report.FileAnalyses.Count(f => f.AnalysisErrors.Count == 0) /
             (double)Math.Max(report.FileAnalyses.Count, 1) * 100, 0.15),
            (report.Issues.Count(i => i.Severity == IssueSeverity.Error) == 0 ? 100 : 50, 0.10)
        };

        return scores.Sum(s => s.Score * s.Weight);
    }

    private static string GetQualityGrade(double score) => score switch
    {
        >= 95 => "A+",
        >= 90 => "A",
        >= 85 => "B+",
        >= 80 => "B",
        >= 75 => "C+",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };

    private static string GenerateMarkdownReport(AnalysisQualityReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Data Quality Report for External Analysis");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Export Profile:** {report.ExportProfileId}");
        sb.AppendLine($"**Quality Grade:** {report.QualityGrade} ({report.OverallQualityScore:F1}%)");
        sb.AppendLine();

        // Summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Total Records:** {report.TotalRecords:N0}");
        sb.AppendLine($"- **Total Size:** {report.TotalBytes / (1024.0 * 1024.0):F2} MB");
        sb.AppendLine($"- **Symbols:** {string.Join(", ", report.Symbols)}");
        if (report.DateRange != null)
        {
            sb.AppendLine($"- **Date Range:** {report.DateRange.Start:yyyy-MM-dd} to {report.DateRange.End:yyyy-MM-dd}");
            sb.AppendLine($"- **Trading Days:** {report.DateRange.TradingDays}");
        }
        sb.AppendLine();

        // Completeness
        sb.AppendLine("## Data Completeness");
        sb.AppendLine();
        sb.AppendLine($"- **Average Completeness:** {report.AverageCompleteness:F1}%");
        sb.AppendLine($"- **Data Gaps:** {report.TotalGaps} total ({report.UnexpectedGaps} unexpected)");
        sb.AppendLine();

        // Outliers
        if (report.TotalOutliers > 0)
        {
            sb.AppendLine("## Outliers Detected");
            sb.AppendLine();
            sb.AppendLine($"⚠️ **{report.TotalOutliers} outliers detected** (>4σ from mean)");
            sb.AppendLine();
            sb.AppendLine("See `outliers.csv` for details.");
            sb.AppendLine();
        }

        // Issues
        if (report.Issues.Count > 0)
        {
            sb.AppendLine("## Quality Issues");
            sb.AppendLine();
            foreach (var issue in report.Issues.OrderByDescending(i => i.Severity))
            {
                var icon = issue.Severity switch
                {
                    IssueSeverity.Error => "❌",
                    IssueSeverity.Warning => "⚠️",
                    _ => "ℹ️"
                };
                sb.AppendLine($"- {icon} **{issue.Category}:** {issue.Description}");
                sb.AppendLine($"  - *Impact:* {issue.Impact}");
                sb.AppendLine($"  - *Resolution:* {issue.Resolution}");
            }
            sb.AppendLine();
        }

        // Recommendations
        sb.AppendLine("## Analysis Recommendations");
        sb.AppendLine();
        foreach (var rec in report.Recommendations)
        {
            var icon = rec.Suitable ? "✅" : "⚠️";
            sb.AppendLine($"### {rec.UseCase}");
            sb.AppendLine($"{icon} {rec.Notes}");
            sb.AppendLine();
        }

        // Per-file statistics
        sb.AppendLine("## Per-File Statistics");
        sb.AppendLine();
        sb.AppendLine("| File | Records | Completeness | Outliers | Gaps |");
        sb.AppendLine("|------|---------|--------------|----------|------|");
        foreach (var file in report.FileAnalyses)
        {
            var fileName = Path.GetFileName(file.FilePath);
            sb.AppendLine($"| {fileName} | {file.RecordCount:N0} | {file.CompletenessScore:F1}% | {file.Outliers.Count} | {file.Gaps.Count} |");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static async Task ExportIssuesToCsvAsync(
        AnalysisQualityReport report,
        string path,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Severity,Category,Description,Impact,Resolution");
        foreach (var issue in report.Issues)
        {
            sb.AppendLine($"{issue.Severity},{EscapeCsv(issue.Category)}," +
                         $"{EscapeCsv(issue.Description)},{EscapeCsv(issue.Impact)}," +
                         $"{EscapeCsv(issue.Resolution)}");
        }
        await AtomicFileWriter.WriteAsync(path, sb.ToString(), ct);
    }

    private static async Task ExportOutliersToCsvAsync(
        AnalysisQualityReport report,
        string path,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("File,Symbol,Timestamp,Field,Value,ZScore,ExpectedRange");
        foreach (var file in report.FileAnalyses)
        {
            foreach (var outlier in file.Outliers)
            {
                sb.AppendLine($"{Path.GetFileName(file.FilePath)},{file.Symbol}," +
                             $"{outlier.Timestamp:O},{outlier.FieldName},{outlier.Value:F4}," +
                             $"{outlier.ZScore:F2},{outlier.ExpectedRange}");
            }
        }
        await AtomicFileWriter.WriteAsync(path, sb.ToString(), ct);
    }

    private static async Task ExportGapsToCsvAsync(
        AnalysisQualityReport report,
        string path,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("File,Symbol,StartTime,EndTime,Duration,GapType,EstimatedMissing");
        foreach (var file in report.FileAnalyses)
        {
            foreach (var gap in file.Gaps)
            {
                sb.AppendLine($"{Path.GetFileName(file.FilePath)},{file.Symbol}," +
                             $"{gap.StartTime:O},{gap.EndTime:O}," +
                             $"{gap.Duration.TotalMinutes:F1},{gap.GapType}," +
                             $"{gap.EstimatedMissingRecords}");
            }
        }
        await AtomicFileWriter.WriteAsync(path, sb.ToString(), ct);
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

/// <summary>
/// Comprehensive quality report for analysis purposes.
/// </summary>
public sealed class AnalysisQualityReport
{
    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; }

    [JsonPropertyName("exportProfileId")]
    public string ExportProfileId { get; set; } = string.Empty;

    [JsonPropertyName("dateRange")]
    public ExportDateRange? DateRange { get; set; }

    [JsonPropertyName("symbols")]
    public string[] Symbols { get; set; } = Array.Empty<string>();

    [JsonPropertyName("totalRecords")]
    public long TotalRecords { get; set; }

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("firstTimestamp")]
    public DateTime? FirstTimestamp { get; set; }

    [JsonPropertyName("lastTimestamp")]
    public DateTime? LastTimestamp { get; set; }

    [JsonPropertyName("averageCompleteness")]
    public double AverageCompleteness { get; set; }

    [JsonPropertyName("totalOutliers")]
    public int TotalOutliers { get; set; }

    [JsonPropertyName("totalGaps")]
    public int TotalGaps { get; set; }

    [JsonPropertyName("unexpectedGaps")]
    public int UnexpectedGaps { get; set; }

    [JsonPropertyName("overallQualityScore")]
    public double OverallQualityScore { get; set; }

    [JsonPropertyName("qualityGrade")]
    public string QualityGrade { get; set; } = "A";

    [JsonPropertyName("fileAnalyses")]
    public List<FileQualityAnalysis> FileAnalyses { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<QualityIssue> Issues { get; set; } = new();

    [JsonPropertyName("recommendations")]
    public List<AnalysisRecommendation> Recommendations { get; set; } = new();
}

/// <summary>
/// Quality analysis for a single file.
/// </summary>
public sealed class FileQualityAnalysis
{
    public string FilePath { get; set; } = string.Empty;
    public string? Symbol { get; set; }
    public string? EventType { get; set; }
    public long RecordCount { get; set; }
    public long SizeBytes { get; set; }
    public double CompletenessScore { get; set; }
    public DescriptiveStats? PriceStats { get; set; }
    public DescriptiveStats? VolumeStats { get; set; }
    public TimeStats? TimeStats { get; set; }
    public List<DataOutlier> Outliers { get; set; } = new();
    public List<DataGap> Gaps { get; set; } = new();
    public List<string> AnalysisErrors { get; set; } = new();
}

/// <summary>
/// Descriptive statistics for a numeric field.
/// </summary>
public sealed class DescriptiveStats
{
    public long Count { get; set; }
    public double Mean { get; set; }
    public double Median { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double StdDev { get; set; }
    public double Percentile25 { get; set; }
    public double Percentile75 { get; set; }
    public double Percentile95 { get; set; }
    public double Percentile99 { get; set; }
}

/// <summary>
/// Time-related statistics.
/// </summary>
public sealed class TimeStats
{
    public DateTime FirstTimestamp { get; set; }
    public DateTime LastTimestamp { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public double AverageGapSeconds { get; set; }
    public double MaxGapSeconds { get; set; }
    public double MedianGapSeconds { get; set; }
}

/// <summary>
/// Detected data outlier.
/// </summary>
public sealed class DataOutlier
{
    public int Index { get; set; }
    public DateTime? Timestamp { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public double Value { get; set; }
    public double ZScore { get; set; }
    public string ExpectedRange { get; set; } = string.Empty;
}

/// <summary>
/// Detected data gap.
/// </summary>
public sealed class DataGap
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public GapType GapType { get; set; }
    public int EstimatedMissingRecords { get; set; }
}

/// <summary>
/// Type of data gap.
/// </summary>
public enum GapType : byte
{
    Weekend,
    Overnight,
    Holiday,
    Unexpected
}

/// <summary>
/// Quality issue detected in the data.
/// </summary>
public sealed class QualityIssue
{
    public IssueSeverity Severity { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
}

/// <summary>
/// Issue severity level.
/// </summary>
public enum IssueSeverity : byte
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Recommendation for using the data.
/// </summary>
public sealed class AnalysisRecommendation
{
    public string UseCase { get; set; } = string.Empty;
    public bool Suitable { get; set; }
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Report output formats.
/// </summary>
[Flags]
public enum ReportFormat : byte
{
    Markdown = 1,
    Json = 2,
    Csv = 4,
    All = Markdown | Json | Csv
}
