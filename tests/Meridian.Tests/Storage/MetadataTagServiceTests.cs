using FluentAssertions;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class MetadataTagServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _metadataPath;
    private readonly MetadataTagService _service;

    public MetadataTagServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _metadataPath = Path.Combine(_tempDir, "_catalog", "metadata.json");
        _service = new MetadataTagService(_metadataPath);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best effort cleanup */ }
    }

    [Fact]
    public void SetTag_ShouldStoreAndRetrieveTag()
    {
        _service.SetTag("/data/AAPL/Trade/2024-01-15.jsonl", "source", "alpaca");

        var result = _service.GetTag("/data/AAPL/Trade/2024-01-15.jsonl", "source");

        result.Should().Be("alpaca");
    }

    [Fact]
    public void SetTags_ShouldStoreMultipleTags()
    {
        var tags = new Dictionary<string, string>
        {
            ["source"] = "alpaca",
            ["quality"] = "high",
            ["asset_class"] = "equity"
        };

        _service.SetTags("/data/AAPL/Trade/2024-01-15.jsonl", tags);

        var allTags = _service.GetAllTags("/data/AAPL/Trade/2024-01-15.jsonl");
        allTags.Should().HaveCount(3);
        allTags["source"].Should().Be("alpaca");
        allTags["quality"].Should().Be("high");
    }

    [Fact]
    public void GetTag_ShouldReturnNullForMissingKey()
    {
        var result = _service.GetTag("/data/missing.jsonl", "nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void RemoveTag_ShouldRemoveExistingTag()
    {
        _service.SetTag("/data/test.jsonl", "key", "value");

        var removed = _service.RemoveTag("/data/test.jsonl", "key");

        removed.Should().BeTrue();
        _service.GetTag("/data/test.jsonl", "key").Should().BeNull();
    }

    [Fact]
    public void RemoveTag_ShouldReturnFalseForMissingTag()
    {
        var removed = _service.RemoveTag("/data/test.jsonl", "nonexistent");

        removed.Should().BeFalse();
    }

    [Fact]
    public void RecordLineage_ShouldTrackProvenance()
    {
        var entry = new LineageEntry(
            TimestampUtc: DateTime.UtcNow,
            Operation: "ingest",
            SourcePath: null,
            SourceProvider: "alpaca",
            TransformationType: null,
            Description: "Ingested from Alpaca WebSocket stream");

        _service.RecordLineage("/data/AAPL/Trade/2024-01-15.jsonl", entry);

        var lineage = _service.GetLineage("/data/AAPL/Trade/2024-01-15.jsonl");
        lineage.Should().HaveCount(1);
        lineage[0].Operation.Should().Be("ingest");
        lineage[0].SourceProvider.Should().Be("alpaca");
    }

    [Fact]
    public void SetInsight_ShouldStoreAndRetrieveInsight()
    {
        var insight = new DataInsight(
            Category: "quality",
            Description: "High completeness score",
            NumericValue: 0.95,
            Unit: "score",
            ComputedAtUtc: DateTime.UtcNow);

        _service.SetInsight("/data/test.jsonl", "completeness", insight);

        var result = _service.GetInsight("/data/test.jsonl", "completeness");
        result.Should().NotBeNull();
        result!.NumericValue.Should().Be(0.95);
        result.Category.Should().Be("quality");
    }

    [Fact]
    public void SetQualityScore_ShouldClampToValidRange()
    {
        _service.SetQualityScore("/data/test.jsonl", 1.5);
        _service.GetQualityScore("/data/test.jsonl").Should().Be(1.0);

        _service.SetQualityScore("/data/test.jsonl", -0.5);
        _service.GetQualityScore("/data/test.jsonl").Should().Be(0.0);

        _service.SetQualityScore("/data/test.jsonl", 0.85);
        _service.GetQualityScore("/data/test.jsonl").Should().Be(0.85);
    }

    [Fact]
    public void SearchByTag_ShouldFindMatchingFiles()
    {
        _service.SetTag("/data/AAPL/trade.jsonl", "source", "alpaca");
        _service.SetTag("/data/MSFT/trade.jsonl", "source", "polygon");
        _service.SetTag("/data/GOOG/trade.jsonl", "source", "alpaca");

        var results = _service.SearchByTag("source", "alpaca");

        results.Should().HaveCount(2);
        results.Should().Contain("/data/AAPL/trade.jsonl");
        results.Should().Contain("/data/GOOG/trade.jsonl");
    }

    [Fact]
    public void SearchByQualityScore_ShouldFilterByRange()
    {
        _service.SetQualityScore("/data/high.jsonl", 0.95);
        _service.SetQualityScore("/data/medium.jsonl", 0.65);
        _service.SetQualityScore("/data/low.jsonl", 0.30);

        var highQuality = _service.SearchByQualityScore(0.8);
        highQuality.Should().HaveCount(1);
        highQuality.Should().Contain("/data/high.jsonl");

        var mediumAndAbove = _service.SearchByQualityScore(0.5);
        mediumAndAbove.Should().HaveCount(2);
    }

    [Fact]
    public void GetFullMetadata_ShouldReturnCompleteRecord()
    {
        _service.SetTag("/data/test.jsonl", "source", "alpaca");
        _service.SetQualityScore("/data/test.jsonl", 0.9, "test-scorer");

        var metadata = _service.GetFullMetadata("/data/test.jsonl");

        metadata.Should().NotBeNull();
        metadata!.Tags.Should().ContainKey("source");
        metadata.QualityScore.Should().Be(0.9);
        metadata.QualityScoredBy.Should().Be("test-scorer");
    }

    [Fact]
    public void RemoveMetadata_ShouldDeleteAllMetadataForFile()
    {
        _service.SetTag("/data/test.jsonl", "key", "value");
        _service.SetQualityScore("/data/test.jsonl", 0.9);

        _service.RemoveMetadata("/data/test.jsonl");

        _service.GetFullMetadata("/data/test.jsonl").Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_ShouldPersistToDisk()
    {
        _service.SetTag("/data/test.jsonl", "key", "value");

        await _service.SaveAsync();

        File.Exists(_metadataPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(_metadataPath);
        content.Should().Contain("key");
        content.Should().Contain("value");
    }

    [Fact]
    public async Task Constructor_ShouldLoadExistingData()
    {
        _service.SetTag("/data/test.jsonl", "persisted", "yes");
        await _service.SaveAsync();

        // Create new instance pointing to same file
        var newService = new MetadataTagService(_metadataPath);

        newService.GetTag("/data/test.jsonl", "persisted").Should().Be("yes");
    }
}
