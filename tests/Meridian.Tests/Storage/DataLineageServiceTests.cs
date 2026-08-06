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

    [Fact]
    public async Task RecordIngestion_ShouldPersistImmediately()
    {
        _service.RecordIngestion("/data/immediate.jsonl", new IngestionRecord(
            DateTime.UtcNow, "alpaca", "QQQ", "Trade", 250));

        File.Exists(_lineagePath).Should().BeTrue();
        var content = await File.ReadAllTextAsync(_lineagePath);
        content.Should().Contain("immediate.jsonl");
        content.Should().Contain("QQQ");
    }

    [Fact]
    public void RecordIngestion_WhenPersistenceWriteFails_Throws()
    {
        var brokenPath = Path.Combine(_tempDir, "broken-lineage.json");
        File.WriteAllText(brokenPath, "{}");

        var loggerMock = new Mock<ILogger<DataLineageService>>();
        var service = new DataLineageService(brokenPath, loggerMock.Object);
        File.Delete(brokenPath);
        Directory.CreateDirectory(brokenPath);

        var act = () => service.RecordIngestion("/data/broken.jsonl", new IngestionRecord(
            DateTime.UtcNow, "alpaca", "IWM", "Trade", 10));

        var exception = act.Should().Throw<Exception>().Which;
        (exception is IOException || exception is UnauthorizedAccessException).Should().BeTrue();
    }

    [Fact]
    public void RecordIngestion_WhenAtomicWriteFails_DoesNotPublishOrSmuggleFailedCandidateAfterRestart()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var logger = new Mock<ILogger<DataLineageService>>();
        var service = new DataLineageService(
            _lineagePath,
            logger.Object,
            writer.Write,
            writer.WriteAsync);
        service.RecordIngestion(
            "/data/baseline.jsonl",
            new IngestionRecord(
                new DateTime(2026, 8, 5, 13, 59, 0, DateTimeKind.Utc),
                "stooq",
                "DIA",
                "HistoricalBar",
                5));
        writer.FailNextWrite();

        var act = () => service.RecordIngestion(
            "/data/failed.jsonl",
            new IngestionRecord(
                new DateTime(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc),
                "alpaca",
                "IWM",
                "Trade",
                10));

        act.Should().Throw<IOException>();
        service.GetLineageGraph("/data/baseline.jsonl").Should().NotBeNull();
        service.GetLineageGraph("/data/failed.jsonl").Should().BeNull();

        service.RecordIngestion(
            "/data/committed.jsonl",
            new IngestionRecord(
                new DateTime(2026, 8, 5, 14, 1, 0, DateTimeKind.Utc),
                "polygon",
                "QQQ",
                "Trade",
                20));

        var restarted = new DataLineageService(_lineagePath, logger.Object);
        restarted.GetLineageGraph("/data/baseline.jsonl").Should().NotBeNull();
        restarted.GetLineageGraph("/data/failed.jsonl").Should().BeNull();
        restarted.GetLineageGraph("/data/committed.jsonl").Should().NotBeNull();
    }

    [Fact]
    public async Task RecordTransformation_WhileAtomicWriteIsBlocked_PublishesBothSidesTogetherAfterCommit()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var logger = new Mock<ILogger<DataLineageService>>();
        var service = new DataLineageService(
            _lineagePath,
            logger.Object,
            writer.Write,
            writer.WriteAsync);
        var block = writer.BlockNextWrite();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var persistence = Task.Run(() => service.RecordTransformation(
            "/data/source.jsonl",
            "/data/target.parquet",
            new TransformationRecord(
                new DateTime(2026, 8, 5, 14, 30, 0, DateTimeKind.Utc),
                "format_conversion",
                "JSONL to Parquet")));

        try
        {
            await block.WaitUntilEnteredAsync(timeout.Token);
            service.GetLineageGraph("/data/source.jsonl").Should().BeNull();
            service.GetLineageGraph("/data/target.parquet").Should().BeNull();
        }
        finally
        {
            block.Release();
        }

        await persistence.WaitAsync(timeout.Token);
        service.GetLineageGraph("/data/source.jsonl")!.Downstream
            .Should().ContainSingle("/data/target.parquet");
        service.GetLineageGraph("/data/target.parquet")!.Upstream
            .Should().ContainSingle("/data/source.jsonl");

        var restarted = new DataLineageService(_lineagePath, logger.Object);
        restarted.GetLineageGraph("/data/source.jsonl")!.Downstream
            .Should().ContainSingle("/data/target.parquet");
        restarted.GetLineageGraph("/data/target.parquet")!.Upstream
            .Should().ContainSingle("/data/source.jsonl");
    }

    [Fact]
    public void RecordIngestion_CallerAndGetterMutation_DoesNotChangePublishedOrPersistedSnapshot()
    {
        const string filePath = "/data/defensive.jsonl";
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["channel"] = "sip"
        };
        _service.RecordIngestion(filePath, new IngestionRecord(
            new DateTime(2026, 8, 5, 15, 0, 0, DateTimeKind.Utc),
            "alpaca",
            "SPY",
            "Trade",
            500,
            Parameters: parameters));

        parameters["channel"] = "mutated";
        var returned = _service.GetLineageGraph(filePath)!;
        returned.FilePath = "mutated";
        returned.Upstream.Add("leaked");
        var returnedParameters = (IDictionary<string, string>)returned.Ingestions[0].Parameters!;
        returnedParameters["channel"] = "leaked";
        returned.Ingestions.Clear();

        var retained = _service.GetLineageGraph("/DATA/DEFENSIVE.JSONL")!;
        retained.FilePath.Should().Be(filePath);
        retained.Upstream.Should().BeEmpty();
        retained.Ingestions.Should().ContainSingle();
        retained.Ingestions[0].Parameters.Should().Contain("channel", "sip");

        var logger = new Mock<ILogger<DataLineageService>>();
        var restarted = new DataLineageService(_lineagePath, logger.Object);
        restarted.GetLineageGraph(filePath)!.Ingestions[0].Parameters
            .Should().Contain("channel", "sip");
    }
}
