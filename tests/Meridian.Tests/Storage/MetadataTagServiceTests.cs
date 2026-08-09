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
    public void Constructor_ShouldLoadExistingData()
    {
        _service.SetTag("/data/test.jsonl", "persisted", "yes");

        // Create new instance pointing to same file
        var newService = new MetadataTagService(_metadataPath);

        newService.GetTag("/data/test.jsonl", "persisted").Should().Be("yes");
    }

    [Fact]
    public async Task SetTag_ShouldPersistImmediately()
    {
        _service.SetTag("/data/immediate.jsonl", "persisted", "true");

        File.Exists(_metadataPath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(_metadataPath);
        content.Should().Contain("persisted");
        content.Should().Contain("true");
    }

    [Fact]
    public void SetTag_WhenPersistenceWriteFails_Throws()
    {
        var brokenPath = Path.Combine(_tempDir, "broken-store.json");
        File.WriteAllText(brokenPath, "{}");

        var service = new MetadataTagService(brokenPath);
        File.Delete(brokenPath);
        Directory.CreateDirectory(brokenPath);

        var act = () => service.SetTag("/data/broken.jsonl", "source", "alpaca");

        var exception = act.Should().Throw<Exception>().Which;
        (exception is IOException || exception is UnauthorizedAccessException).Should().BeTrue();
    }

    [Fact]
    public void SetTag_WhenAtomicWriteFails_DoesNotPublishOrSmuggleFailedCandidateAfterRestart()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var service = new MetadataTagService(_metadataPath, writer.Write, writer.WriteAsync);
        service.SetTag("/data/baseline.jsonl", "source", "stooq");
        writer.FailNextWrite();

        var act = () => service.SetTag("/data/failed.jsonl", "source", "alpaca");

        act.Should().Throw<IOException>();
        service.GetTag("/data/baseline.jsonl", "source").Should().Be("stooq");
        service.GetTag("/data/failed.jsonl", "source").Should().BeNull();

        service.SetTag("/data/committed.jsonl", "source", "polygon");

        var restarted = new MetadataTagService(_metadataPath);
        restarted.GetTag("/data/baseline.jsonl", "source").Should().Be("stooq");
        restarted.GetTag("/data/failed.jsonl", "source").Should().BeNull();
        restarted.GetTag("/data/committed.jsonl", "source").Should().Be("polygon");
    }

    [Fact]
    public async Task SetQualityAssessmentsAsync_WhileAtomicWriteIsBlocked_PublishesWholeBatchAfterCommit()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var service = new MetadataTagService(_metadataPath, writer.Write, writer.WriteAsync);
        var block = writer.BlockNextWrite();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var computedAtUtc = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
        var assessments = new[]
        {
            new QualityAssessmentMetadataUpdate(
                "/data/AAPL.jsonl",
                0.98,
                new DataInsight("quality", "Complete", 0.98, "score", computedAtUtc)),
            new QualityAssessmentMetadataUpdate(
                "/data/MSFT.jsonl",
                0.94,
                new DataInsight("quality", "Complete", 0.94, "score", computedAtUtc))
        };

        var persistence = service.SetQualityAssessmentsAsync(assessments, timeout.Token);

        try
        {
            await block.WaitUntilEnteredAsync(timeout.Token);
            service.GetQualityScore("/data/AAPL.jsonl").Should().BeNull();
            service.GetQualityScore("/data/MSFT.jsonl").Should().BeNull();
        }
        finally
        {
            block.Release();
        }

        await persistence.WaitAsync(timeout.Token);
        service.GetQualityScore("/data/AAPL.jsonl").Should().Be(0.98);
        service.GetQualityScore("/data/MSFT.jsonl").Should().Be(0.94);
    }

    [Fact]
    public async Task SetQualityAssessmentAsync_WhenAtomicWriteIsCancelled_DoesNotPublishOrSmuggleCandidate()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var service = new MetadataTagService(_metadataPath, writer.Write, writer.WriteAsync);
        service.SetTag("/data/baseline-before-cancel.jsonl", "source", "stooq");
        var block = writer.BlockNextWrite();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var operationCancellation = new CancellationTokenSource();
        var insight = new DataInsight(
            "quality",
            "Cancelled assessment",
            0.75,
            "score",
            new DateTime(2026, 8, 5, 12, 30, 0, DateTimeKind.Utc));
        var persistence = service.SetQualityAssessmentAsync(
            "/data/cancelled.jsonl",
            0.75,
            insight,
            "quality-engine",
            operationCancellation.Token);

        try
        {
            await block.WaitUntilEnteredAsync(timeout.Token);
            operationCancellation.Cancel();
            var act = async () => await persistence;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            operationCancellation.Cancel();
            block.Release();
        }

        service.GetTag("/data/baseline-before-cancel.jsonl", "source").Should().Be("stooq");
        service.GetQualityScore("/data/cancelled.jsonl").Should().BeNull();
        service.SetTag("/data/committed-after-cancel.jsonl", "source", "alpaca");

        var restarted = new MetadataTagService(_metadataPath);
        restarted.GetTag("/data/baseline-before-cancel.jsonl", "source").Should().Be("stooq");
        restarted.GetQualityScore("/data/cancelled.jsonl").Should().BeNull();
        restarted.GetTag("/data/committed-after-cancel.jsonl", "source").Should().Be("alpaca");
    }

    [Fact]
    public void SetTagsAndLineage_CallerAndGetterMutation_DoesNotChangePublishedOrPersistedSnapshot()
    {
        const string filePath = "/data/defensive.jsonl";
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = "alpaca"
        };
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["channel"] = "sip"
        };
        _service.SetTags(filePath, tags);
        _service.RecordLineage(filePath, new LineageEntry(
            TimestampUtc: new DateTime(2026, 8, 5, 13, 0, 0, DateTimeKind.Utc),
            Operation: "ingest",
            SourcePath: null,
            SourceProvider: "alpaca",
            TransformationType: null,
            Description: "Session-open ingest",
            Parameters: parameters));

        tags["source"] = "mutated";
        parameters["channel"] = "mutated";
        var returnedTags = (IDictionary<string, string>)_service.GetAllTags(filePath);
        returnedTags["source"] = "leaked";
        var returnedRecord = _service.GetFullMetadata(filePath)!;
        returnedRecord.Tags["source"] = "record-leaked";
        returnedRecord.Lineage[0] = returnedRecord.Lineage[0] with { Description = "leaked" };
        var returnedParameters = (IDictionary<string, string>)returnedRecord.Lineage[0].Parameters!;
        returnedParameters["channel"] = "leaked";

        _service.GetTag(filePath, "source").Should().Be("alpaca");
        var retainedLineage = _service.GetLineage(filePath).Should().ContainSingle().Which;
        retainedLineage.Description.Should().Be("Session-open ingest");
        retainedLineage.Parameters.Should().Contain("channel", "sip");

        var restarted = new MetadataTagService(_metadataPath);
        restarted.GetTag(filePath, "source").Should().Be("alpaca");
        restarted.GetLineage(filePath).Single().Parameters.Should().Contain("channel", "sip");
    }
}
