using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class DataQualityScoringServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageOptions _options;
    private readonly Mock<ILogger<DataQualityScoringService>> _loggerMock;
    private readonly DataQualityScoringService _service;

    public DataQualityScoringServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _options = new StorageOptions
        {
            RootPath = _tempDir,
            NamingConvention = FileNamingConvention.BySymbol
        };

        _loggerMock = new Mock<ILogger<DataQualityScoringService>>();
        _service = new DataQualityScoringService(_options, _loggerMock.Object);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best effort cleanup */ }
    }

    [Fact]
    public async Task ScoreFileAsync_ShouldReturnEmptyForNonExistentFile()
    {
        var result = await _service.ScoreFileAsync("/nonexistent/file.jsonl");

        result.CompositeScore.Should().Be(0.0);
        result.Symbol.Should().Be("Unknown");
    }

    [Fact]
    public async Task ScoreFileAsync_ShouldScoreValidJsonlFile()
    {
        var dir = Path.Combine(_tempDir, "AAPL", "Trade");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "2024-01-15.jsonl");

        var lines = Enumerable.Range(1, 100)
            .Select(i => $"{{\"Timestamp\":\"2024-01-15T10:{i:D2}:00Z\",\"Symbol\":\"AAPL\",\"Price\":150.{i:D2},\"Size\":100,\"Sequence\":{i}}}")
            .ToList();
        await File.WriteAllLinesAsync(filePath, lines);

        var result = await _service.ScoreFileAsync(filePath);

        result.CompositeScore.Should().BeGreaterThan(0.0);
        result.Dimensions.Should().ContainKey("completeness");
        result.Dimensions.Should().ContainKey("sequence_integrity");
        result.Dimensions.Should().ContainKey("format_quality");
        result.Dimensions["completeness"].Should().Be(1.0);
        result.Dimensions["format_quality"].Should().Be(1.0);
    }

    [Fact]
    public async Task ScoreFileAsync_ShouldPenalizeEmptyFile()
    {
        var dir = Path.Combine(_tempDir, "AAPL", "Trade");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "empty.jsonl");
        await File.WriteAllTextAsync(filePath, "");

        var result = await _service.ScoreFileAsync(filePath);

        result.Dimensions["completeness"].Should().Be(0.0);
    }

    [Fact]
    public async Task ScoreFileAsync_ShouldPenalizeCorruptJson()
    {
        var dir = Path.Combine(_tempDir, "AAPL", "Trade");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "corrupt.jsonl");
        var lines = new[] { "{\"valid\":true}", "not json", "{\"valid\":true}", "also not json" };
        await File.WriteAllLinesAsync(filePath, lines);

        var result = await _service.ScoreFileAsync(filePath);

        result.Dimensions["format_quality"].Should().BeLessThan(1.0);
    }

    [Fact]
    public async Task SelectBestSourceAsync_ShouldReturnBestCandidate()
    {
        // Create files from two sources
        var alpacaDir = Path.Combine(_tempDir, "alpaca", "AAPL", "Trade");
        var polygonDir = Path.Combine(_tempDir, "polygon", "AAPL", "Trade");
        Directory.CreateDirectory(alpacaDir);
        Directory.CreateDirectory(polygonDir);

        // Use BySource naming convention for this test
        var options = new StorageOptions
        {
            RootPath = _tempDir,
            NamingConvention = FileNamingConvention.BySource
        };
        var service = new DataQualityScoringService(options, _loggerMock.Object);

        // Alpaca: 100 valid lines
        var alpacaLines = Enumerable.Range(1, 100)
            .Select(i => $"{{\"Symbol\":\"AAPL\",\"Price\":150.{i:D2},\"Sequence\":{i}}}")
            .ToList();
        await File.WriteAllLinesAsync(Path.Combine(alpacaDir, "2024-01-15.jsonl"), alpacaLines);

        // Polygon: 10 valid lines (less complete)
        var polygonLines = Enumerable.Range(1, 10)
            .Select(i => $"{{\"Symbol\":\"AAPL\",\"Price\":150.{i:D2},\"Sequence\":{i}}}")
            .ToList();
        await File.WriteAllLinesAsync(Path.Combine(polygonDir, "2024-01-15.jsonl"), polygonLines);

        var result = await service.SelectBestSourceAsync("AAPL", "Trade");

        result.Candidates.Should().HaveCount(2);
        result.Best.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateReportAsync_ShouldSummarizeAllFiles()
    {
        var dir = Path.Combine(_tempDir, "AAPL", "Trade");
        Directory.CreateDirectory(dir);

        for (int day = 1; day <= 3; day++)
        {
            var lines = Enumerable.Range(1, 50)
                .Select(i => $"{{\"Symbol\":\"AAPL\",\"Price\":150.{i:D2},\"Sequence\":{i}}}")
                .ToList();
            await File.WriteAllLinesAsync(Path.Combine(dir, $"2024-01-{day:D2}.jsonl"), lines);
        }

        var report = await _service.GenerateReportAsync();

        report.Summary.TotalFiles.Should().Be(3);
        report.Summary.AverageScore.Should().BeGreaterThan(0);
        report.Assessments.Should().HaveCount(3);
    }
}
