using FluentAssertions;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.DataIntegration.Monitoring.DataQuality;

public sealed class DataQualityReportExportTests : IDisposable
{
    private readonly string _outputDirectory =
        Path.Combine(Path.GetTempPath(), $"meridian-dq-export-{Guid.NewGuid():N}");
    private readonly CompletenessScoreCalculator _completeness = new();
    private readonly GapAnalyzer _gapAnalyzer = new();
    private readonly SequenceErrorTracker _sequenceTracker = new();
    private readonly AnomalyDetector _anomalyDetector = new();
    private readonly LatencyHistogram _latencyHistogram = new();
    private readonly DataQualityReportGenerator _generator;

    public DataQualityReportExportTests()
    {
        _generator = new DataQualityReportGenerator(
            _completeness,
            _gapAnalyzer,
            _sequenceTracker,
            _anomalyDetector,
            _latencyHistogram,
            outputDirectory: _outputDirectory);
    }

    [Fact]
    public async Task ExportWeeklyReportAsync_Csv_WritesCsvRowsNotJson()
    {
        var report = BuildWeekly(BuildDaily(new DateOnly(2026, 4, 6), "AAPL", "A"));

        var path = await _generator.ExportWeeklyReportAsync(report, ReportExportFormat.Csv);

        path.Should().EndWith(".csv");
        var lines = (await File.ReadAllLinesAsync(path));
        lines[0].Should().Be("Date,OverallScore,TotalEvents,TotalGaps,TotalSequenceErrors,TotalAnomalies");
        lines[1].Should().Be("2026-04-06,0.9500,1200,2,1,3");
        lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task ExportReportAsync_Csv_EscapesAndNeutralizesTextCells()
    {
        var report = BuildDaily(new DateOnly(2026, 4, 6), "=HYPERLINK(\"https://example.invalid\"),X", "A");

        var path = await _generator.ExportReportAsync(report, ReportExportFormat.Csv);

        var lines = await File.ReadAllLinesAsync(path);
        lines[1].Should().Be("\"'=HYPERLINK(\"\"https://example.invalid\"\"),X\",0.9500,0.9000,1200,2,1,3,A");
    }

    [Fact]
    public async Task ExportReportAsync_Html_EncodesTextValues()
    {
        var report = BuildDaily(
            new DateOnly(2026, 4, 6),
            "<script>alert(1)</script>",
            "A",
            recommendation: "Check <b>feed</b> & retry");

        var path = await _generator.ExportReportAsync(report, ReportExportFormat.Html);

        var html = await File.ReadAllTextAsync(path);
        html.Should().NotContain("<script>");
        html.Should().Contain("<td>&lt;script&gt;alert(1)&lt;/script&gt;</td>");
        html.Should().Contain("<li>Check &lt;b&gt;feed&lt;/b&gt; &amp; retry</li>");
    }

    public void Dispose()
    {
        _completeness.Dispose();
        _gapAnalyzer.Dispose();
        _sequenceTracker.Dispose();
        _anomalyDetector.Dispose();
        _latencyHistogram.Dispose();
        if (Directory.Exists(_outputDirectory))
            Directory.Delete(_outputDirectory, recursive: true);
    }

    private static DailyQualityReport BuildDaily(
        DateOnly date,
        string symbol,
        string grade,
        string? recommendation = null)
        => new(
            date,
            new DateTimeOffset(2026, 4, 7, 0, 5, 0, TimeSpan.Zero),
            SymbolsAnalyzed: 1,
            OverallScore: 0.95,
            CompletenessScore: 0.9,
            IntegrityScore: 0.97,
            TimelinessScore: 0.98,
            SymbolSummaries:
            [
                new SymbolQualitySummary(symbol, 0.95, 0.9, 1200, 2, TimeSpan.FromMinutes(4), 1, 3, grade, [])
            ],
            SignificantGaps: [],
            SignificantErrors: [],
            Anomalies: [],
            Recommendations: recommendation is null ? [] : [recommendation],
            Statistics: new ReportStatistics(1200, 1300, 100, 2, TimeSpan.FromMinutes(4), 1, 3, 12.5, 40));

    private static WeeklyQualityReport BuildWeekly(DailyQualityReport day)
        => new(
            day.Date,
            day.Date.AddDays(6),
            new DateTimeOffset(2026, 4, 13, 0, 5, 0, TimeSpan.Zero),
            [day],
            AverageScore: day.OverallScore,
            ScoreTrend: 0,
            TopIssues: [],
            Improvements: [],
            Recommendations: [],
            Statistics: null!);
}
