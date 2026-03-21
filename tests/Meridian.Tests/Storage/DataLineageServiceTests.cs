using FluentAssertions;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class DataLineageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _lineagePath;
    private readonly DataLineageService _service;

    public DataLineageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _lineagePath = Path.Combine(_tempDir, "_catalog", "lineage.json");
        var loggerMock = new Mock<ILogger<DataLineageService>>();
        _service = new DataLineageService(_lineagePath, loggerMock.Object);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best effort cleanup */ }
    }

    [Fact]
    public void RecordIngestion_ShouldTrackDataSource()
    {
        var record = new IngestionRecord(
            TimestampUtc: DateTime.UtcNow,
            Provider: "alpaca",
            Symbol: "AAPL",
            EventType: "Trade",
            EventCount: 5000);

        _service.RecordIngestion("/data/AAPL/Trade/2024-01-15.jsonl", record);

        var graph = _service.GetLineageGraph("/data/AAPL/Trade/2024-01-15.jsonl");
        graph.Should().NotBeNull();
        graph!.Ingestions.Should().HaveCount(1);
        graph.Ingestions[0].Provider.Should().Be("alpaca");
        graph.Ingestions[0].EventCount.Should().Be(5000);
    }

    [Fact]
    public void RecordTransformation_ShouldLinkSourceToTarget()
    {
        var transform = new TransformationRecord(
            TimestampUtc: DateTime.UtcNow,
            Type: "compression",
            Description: "Compressed from JSONL to JSONL.GZ");

        _service.RecordTransformation(
            "/data/hot/AAPL/Trade/2024-01-15.jsonl",
            "/data/warm/AAPL/Trade/2024-01-15.jsonl.gz",
            transform);

        var sourceGraph = _service.GetLineageGraph("/data/hot/AAPL/Trade/2024-01-15.jsonl");
        sourceGraph!.Downstream.Should().Contain("/data/warm/AAPL/Trade/2024-01-15.jsonl.gz");

        var targetGraph = _service.GetLineageGraph("/data/warm/AAPL/Trade/2024-01-15.jsonl.gz");
        targetGraph!.Upstream.Should().Contain("/data/hot/AAPL/Trade/2024-01-15.jsonl");
        targetGraph.Transformations.Should().HaveCount(1);
    }

    [Fact]
    public void RecordMigration_ShouldTrackTierChanges()
    {
        var migration = new MigrationRecord(
            TimestampUtc: DateTime.UtcNow,
            SourceTier: "hot",
            TargetTier: "warm",
            CompressionChange: "none -> gzip",
            BytesBefore: 10_000,
            BytesAfter: 3_000);

        _service.RecordMigration(
            "/data/hot/trade.jsonl",
            "/data/warm/trade.jsonl.gz",
            migration);

        var sourceGraph = _service.GetLineageGraph("/data/hot/trade.jsonl");
        sourceGraph!.Migrations.Should().HaveCount(1);
        sourceGraph.Migrations[0].SourceTier.Should().Be("hot");
        sourceGraph.Migrations[0].BytesBefore.Should().Be(10_000);
    }

    [Fact]
    public void RecordDeletion_ShouldMarkAsDeleted()
    {
        _service.RecordIngestion("/data/old.jsonl", new IngestionRecord(
            DateTime.UtcNow, "alpaca", "AAPL", "Trade", 100));

        _service.RecordDeletion("/data/old.jsonl", "retention_expired");

        var graph = _service.GetLineageGraph("/data/old.jsonl");
        graph!.DeletedAtUtc.Should().NotBeNull();
        graph.DeletionReason.Should().Be("retention_expired");
    }

    [Fact]
    public void GetUpstream_ShouldTraverseChain()
    {
        // Create a chain: A -> B -> C
        _service.RecordTransformation("A", "B", new TransformationRecord(
            DateTime.UtcNow, "transform", "A to B"));
        _service.RecordTransformation("B", "C", new TransformationRecord(
            DateTime.UtcNow, "transform", "B to C"));

        var upstream = _service.GetUpstream("C");

        upstream.Should().Contain("C"); // Includes self via traversal
        upstream.Should().Contain("B");
        upstream.Should().Contain("A");
    }

    [Fact]
    public void GetDownstream_ShouldTraverseChain()
    {
        _service.RecordTransformation("A", "B", new TransformationRecord(
            DateTime.UtcNow, "transform", "A to B"));
        _service.RecordTransformation("A", "C", new TransformationRecord(
            DateTime.UtcNow, "transform", "A to C"));

        var downstream = _service.GetDownstream("A");

        downstream.Should().Contain("A");
        downstream.Should().Contain("B");
        downstream.Should().Contain("C");
    }

    [Fact]
    public void GenerateReport_ShouldSummarizeAllLineage()
    {
        _service.RecordIngestion("/data/file1.jsonl", new IngestionRecord(
            DateTime.UtcNow, "alpaca", "AAPL", "Trade", 100));
        _service.RecordIngestion("/data/file2.jsonl", new IngestionRecord(
            DateTime.UtcNow, "polygon", "MSFT", "Trade", 200));
        _service.RecordTransformation("/data/file1.jsonl", "/data/file1.parquet",
            new TransformationRecord(DateTime.UtcNow, "format_conversion", "JSONL to Parquet"));

        var report = _service.GenerateReport();

        report.TotalTrackedFiles.Should().Be(3);
        report.ActiveFiles.Should().Be(3);
        report.TotalIngestions.Should().Be(2);
        report.TotalTransformations.Should().Be(1);
        report.SourceDistribution.Should().ContainKey("alpaca");
        report.SourceDistribution.Should().ContainKey("polygon");
    }

    [Fact]
    public async Task SaveAsync_ShouldPersistAndReload()
    {
        _service.RecordIngestion("/data/test.jsonl", new IngestionRecord(
            DateTime.UtcNow, "alpaca", "SPY", "Trade", 500));

        await _service.SaveAsync();

        File.Exists(_lineagePath).Should().BeTrue();

        // Reload
        var loggerMock = new Mock<ILogger<DataLineageService>>();
        var newService = new DataLineageService(_lineagePath, loggerMock.Object);
        var graph = newService.GetLineageGraph("/data/test.jsonl");

        graph.Should().NotBeNull();
        graph!.Ingestions.Should().HaveCount(1);
        graph.Ingestions[0].Provider.Should().Be("alpaca");
    }
}
