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

    [Fact]
    public async Task ScoreFileAsync_ShouldPersistMetadataViaSingleQualityAssessmentCall()
    {
        var dir = Path.Combine(_tempDir, "AAPL", "Trade");
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "2024-01-15.jsonl");
        await File.WriteAllLinesAsync(filePath,
        [
            "{\"Symbol\":\"AAPL\",\"Price\":150.10,\"Sequence\":1}",
            "{\"Symbol\":\"AAPL\",\"Price\":150.20,\"Sequence\":2}"
        ]);

        var metadata = new RecordingMetadataTagService();
        var service = new DataQualityScoringService(_options, _loggerMock.Object, metadataService: metadata);

        var assessment = await service.ScoreFileAsync(filePath);

        assessment.CompositeScore.Should().BeGreaterThan(0.0);
        metadata.SingleAssessmentCalls.Should().Be(1);
        metadata.BatchAssessmentCalls.Should().Be(0);
        metadata.GetQualityScore(filePath).Should().Be(assessment.CompositeScore);
        metadata.GetInsight(filePath, "quality_assessment").Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateReportAsync_ShouldBatchMetadataPersistence()
    {
        var dir = Path.Combine(_tempDir, "MSFT", "Trade");
        Directory.CreateDirectory(dir);

        for (var day = 1; day <= 3; day++)
        {
            await File.WriteAllLinesAsync(
                Path.Combine(dir, $"2024-02-{day:D2}.jsonl"),
            [
                "{\"Symbol\":\"MSFT\",\"Price\":250.10,\"Sequence\":1}",
                "{\"Symbol\":\"MSFT\",\"Price\":250.20,\"Sequence\":2}"
            ]);
        }

        var metadata = new RecordingMetadataTagService();
        var service = new DataQualityScoringService(_options, _loggerMock.Object, metadataService: metadata);

        var report = await service.GenerateReportAsync();

        report.Assessments.Should().HaveCount(3);
        metadata.SingleAssessmentCalls.Should().Be(0);
        metadata.BatchAssessmentCalls.Should().Be(1);
        metadata.BatchAssessmentSizes.Should().ContainSingle().Which.Should().Be(3);
    }

    private sealed class RecordingMetadataTagService : IMetadataTagService
    {
        private readonly Dictionary<string, FileMetadataRecord> _records = new(StringComparer.OrdinalIgnoreCase);

        public int SingleAssessmentCalls { get; private set; }

        public int BatchAssessmentCalls { get; private set; }

        public List<int> BatchAssessmentSizes { get; } = [];

        public void SetTag(string filePath, string key, string value)
        {
            var record = GetOrCreate(filePath);
            record.Tags[key] = value;
        }

        public void SetTags(string filePath, IReadOnlyDictionary<string, string> tags)
        {
            var record = GetOrCreate(filePath);
            foreach (var pair in tags)
            {
                record.Tags[pair.Key] = pair.Value;
            }
        }

        public string? GetTag(string filePath, string key)
            => _records.TryGetValue(filePath, out var record) && record.Tags.TryGetValue(key, out var value)
                ? value
                : null;

        public IReadOnlyDictionary<string, string> GetAllTags(string filePath)
            => _records.TryGetValue(filePath, out var record) ? record.Tags : new Dictionary<string, string>();

        public bool RemoveTag(string filePath, string key)
            => _records.TryGetValue(filePath, out var record) && record.Tags.Remove(key);

        public void RecordLineage(string filePath, LineageEntry entry)
            => GetOrCreate(filePath).Lineage.Add(entry);

        public IReadOnlyList<LineageEntry> GetLineage(string filePath)
            => _records.TryGetValue(filePath, out var record) ? record.Lineage : Array.Empty<LineageEntry>();

        public void SetInsight(string filePath, string insightKey, DataInsight insight)
            => GetOrCreate(filePath).Insights[insightKey] = insight;

        public DataInsight? GetInsight(string filePath, string insightKey)
            => _records.TryGetValue(filePath, out var record) && record.Insights.TryGetValue(insightKey, out var insight)
                ? insight
                : null;

        public IReadOnlyDictionary<string, DataInsight> GetAllInsights(string filePath)
            => _records.TryGetValue(filePath, out var record) ? record.Insights : new Dictionary<string, DataInsight>();

        public void SetQualityScore(string filePath, double score, string? scoredBy = null)
        {
            var record = GetOrCreate(filePath);
            record.QualityScore = Math.Clamp(score, 0.0, 1.0);
            record.QualityScoredBy = scoredBy;
        }

        public double? GetQualityScore(string filePath)
            => _records.TryGetValue(filePath, out var record) ? record.QualityScore : null;

        public IReadOnlyList<string> SearchByTag(string key, string? valuePattern = null)
        {
            return _records
                .Where(pair => pair.Value.Tags.TryGetValue(key, out var value)
                    && (valuePattern is null || value.Contains(valuePattern, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => pair.Key)
                .ToArray();
        }

        public IReadOnlyList<string> SearchByQualityScore(double minScore, double maxScore = 1.0)
        {
            return _records
                .Where(pair => pair.Value.QualityScore >= minScore && pair.Value.QualityScore <= maxScore)
                .Select(pair => pair.Key)
                .ToArray();
        }

        public FileMetadataRecord? GetFullMetadata(string filePath)
            => _records.TryGetValue(filePath, out var record) ? record : null;

        public void RemoveMetadata(string filePath)
            => _records.Remove(filePath);

        public Task SetQualityAssessmentAsync(string filePath, double score, DataInsight insight, string? scoredBy = null, CancellationToken ct = default)
        {
            SingleAssessmentCalls++;
            ApplyAssessment(filePath, score, insight, scoredBy, "quality_assessment");
            return Task.CompletedTask;
        }

        public Task SetQualityAssessmentsAsync(IReadOnlyCollection<QualityAssessmentMetadataUpdate> assessments, CancellationToken ct = default)
        {
            BatchAssessmentCalls++;
            BatchAssessmentSizes.Add(assessments.Count);

            foreach (var assessment in assessments)
            {
                ApplyAssessment(assessment.FilePath, assessment.Score, assessment.Insight, assessment.ScoredBy, assessment.InsightKey);
            }

            return Task.CompletedTask;
        }

        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

        private FileMetadataRecord GetOrCreate(string filePath)
        {
            if (_records.TryGetValue(filePath, out var record))
            {
                return record;
            }

            record = new FileMetadataRecord
            {
                FilePath = filePath,
                CreatedUtc = DateTime.UtcNow,
                LastModifiedUtc = DateTime.UtcNow
            };
            _records[filePath] = record;
            return record;
        }

        private void ApplyAssessment(string filePath, double score, DataInsight insight, string? scoredBy, string insightKey)
        {
            var record = GetOrCreate(filePath);
            record.QualityScore = Math.Clamp(score, 0.0, 1.0);
            record.QualityScoredBy = scoredBy;
            record.QualityScoredAtUtc = insight.ComputedAtUtc;
            record.LastModifiedUtc = insight.ComputedAtUtc;
            record.Insights[insightKey] = insight;
        }
    }
}
