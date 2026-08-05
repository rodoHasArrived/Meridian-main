using FluentAssertions;
using Meridian.Storage.Export;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class AnalysisQualityReportCsvTests : IDisposable
{
    private readonly string _outputDirectory =
        Path.Combine(Path.GetTempPath(), $"meridian-quality-csv-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportReportAsync_Csv_EscapesEveryFieldAndNeutralizesDelimitedFormulas()
    {
        var report = new AnalysisQualityReport
        {
            Issues =
            [
                new QualityIssue
                {
                    Severity = IssueSeverity.Warning,
                    Category = "reviewed;=SUM(A1:A2)",
                    Description = "line one\rline two",
                    Impact = "+execute",
                    Resolution = "quote \"and\" neutralize"
                }
            ],
            FileAnalyses =
            [
                new FileQualityAnalysis
                {
                    FilePath = "report,unsafe.jsonl",
                    Symbol = "AAPL;=HYPERLINK(\"https://example.invalid\")",
                    Outliers =
                    [
                        new DataOutlier
                        {
                            Timestamp = new DateTime(2026, 4, 11, 16, 0, 0, DateTimeKind.Utc),
                            FieldName = "@formula",
                            Value = 12.5,
                            ZScore = 3.25,
                            ExpectedRange = "10,\"safe\"\n20"
                        }
                    ],
                    Gaps =
                    [
                        new DataGap
                        {
                            StartTime = new DateTime(2026, 4, 11, 16, 0, 0, DateTimeKind.Utc),
                            EndTime = new DateTime(2026, 4, 11, 16, 5, 0, DateTimeKind.Utc),
                            Duration = TimeSpan.FromMinutes(5),
                            GapType = GapType.Unexpected,
                            EstimatedMissingRecords = 5
                        }
                    ]
                }
            ]
        };
        var generator = new AnalysisQualityReportGenerator();

        await generator.ExportReportAsync(report, _outputDirectory, ReportFormat.Csv);

        var issues = await File.ReadAllTextAsync(Path.Combine(_outputDirectory, "quality_issues.csv"));
        issues.Should().Contain("\"reviewed;'=SUM(A1:A2)\"");
        issues.Should().Contain("\"line one\rline two\"");
        issues.Should().Contain(",'+execute,");
        issues.Should().Contain("\"quote \"\"and\"\" neutralize\"");

        var outliers = await File.ReadAllTextAsync(Path.Combine(_outputDirectory, "outliers.csv"));
        outliers.Should().Contain("\"report,unsafe.jsonl\"");
        outliers.Should().Contain("\"AAPL;'=HYPERLINK(\"\"https://example.invalid\"\")\"");
        outliers.Should().Contain(",'@formula,");
        outliers.Should().Contain("\"10,\"\"safe\"\"\n20\"");

        var gaps = await File.ReadAllTextAsync(Path.Combine(_outputDirectory, "gaps.csv"));
        gaps.Should().Contain("\"report,unsafe.jsonl\"");
        gaps.Should().Contain("\"AAPL;'=HYPERLINK(\"\"https://example.invalid\"\")\"");
        gaps.Should().Contain(",5.0,Unexpected,5");
    }

    [Fact]
    public async Task ExportReportAsync_CanceledBeforeWrite_DoesNotPublishCsv()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var generator = new AnalysisQualityReportGenerator();

        var act = () => generator.ExportReportAsync(
            new AnalysisQualityReport(),
            _outputDirectory,
            ReportFormat.Csv,
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFiles(_outputDirectory, "*.csv", SearchOption.TopDirectoryOnly)
            .Should()
            .BeEmpty();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_outputDirectory))
            {
                Directory.Delete(_outputDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory must not fail an otherwise passing test run.
        }
    }
}
