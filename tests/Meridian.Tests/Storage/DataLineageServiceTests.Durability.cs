using FluentAssertions;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Storage;

public sealed partial class DataLineageServiceTests
{
    [Fact]
    public void Constructor_InvalidUtf8InsideRetainedValue_RejectsWithoutReplacingBytes()
    {
        _service.RecordIngestion("source", new IngestionRecord(DateTime.UtcNow, "synthetic", "marker", "Trade", 1));
        var json = File.ReadAllText(_lineagePath);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        bytes[json.IndexOf("marker", StringComparison.Ordinal)] = 0xff;
        File.WriteAllBytes(_lineagePath, bytes);
        var restart = () => new DataLineageService(_lineagePath, NullLogger<DataLineageService>.Instance);
        restart.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(_lineagePath).Should().Equal(bytes);
    }

    [Fact]
    public void Constructor_NullNestedHistory_RefusesToReplaceItWithEmptyEvidence()
    {
        _service.RecordIngestion("source", new IngestionRecord(DateTime.UtcNow, "synthetic", "S", "Trade", 1));
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(_lineagePath))!;
        root["graphs"]!["source"]!["ingestions"] = null;
        var json = root.ToJsonString();
        File.WriteAllText(_lineagePath, json);
        var restart = () => new DataLineageService(_lineagePath, NullLogger<DataLineageService>.Instance);
        restart.Should().Throw<InvalidDataException>();
        File.ReadAllText(_lineagePath).Should().Be(json);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":\"2.0.0\",\"graphs\":{}}")]
    [InlineData("{\"version\":\"1.0.0\",\"graphs\":null}")]
    [InlineData("{\"version\":\"1.0.0\",\"graphs\":{\"source\":{\"filePath\":\"other\"}}}")]
    public void Constructor_CorruptOrUnsupportedSnapshot_RefusesToEraseRetainedEvidence(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_lineagePath)!);
        File.WriteAllText(_lineagePath, json);

        var restart = () => new DataLineageService(_lineagePath, NullLogger<DataLineageService>.Instance);

        restart.Should().Throw<InvalidDataException>();
        File.ReadAllText(_lineagePath).Should().Be(json);
    }

    [Fact]
    public async Task ConcurrentInstances_MergeRetainedHistoryAndStaleSaveCannotEraseCommits()
    {
        var services = Enumerable.Range(0, 8)
            .Select(_ => new DataLineageService(_lineagePath, NullLogger<DataLineageService>.Instance)).ToArray();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = services.Select((service, index) => Task.Run(async () =>
        {
            await gate.Task;
            service.RecordIngestion($"/data/{index}.jsonl", new IngestionRecord(
                DateTime.UtcNow, "synthetic", $"S{index}", "Trade", index + 1));
        })).ToArray();
        gate.SetResult();
        await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(10));
        await _service.SaveAsync(); // Created before every writer: must reload rather than erase.

        var restarted = new DataLineageService(_lineagePath, NullLogger<DataLineageService>.Instance);
        restarted.GenerateReport().TotalIngestions.Should().Be(8);
        foreach (var index in Enumerable.Range(0, 8))
            restarted.GetLineageGraph($"/data/{index}.jsonl")!.Ingestions.Single().EventCount.Should().Be(index + 1);
    }

    [Fact]
    public void Mutation_AfterSnapshotCorruptionOrRemoval_RetainsPublishedHistoryAndRefusesWrite()
    {
        _service.RecordIngestion("/data/retained.jsonl", new IngestionRecord(DateTime.UtcNow, "synthetic", "S", "Trade", 1));
        File.WriteAllText(_lineagePath, "{");
        var mutation = () => _service.RecordDeletion("/data/retained.jsonl", "removed");

        mutation.Should().Throw<InvalidDataException>();
        _service.GetLineageGraph("/data/retained.jsonl")!.DeletedAtUtc.Should().BeNull();
        File.ReadAllText(_lineagePath).Should().Be("{");
        File.Delete(_lineagePath);
        mutation.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task SaveAsync_CancellationWhileAnotherInstanceWrites_DoesNotEraseItsCommit()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var other = new DataLineageService(_lineagePath, NullLogger<DataLineageService>.Instance, writer.Write, writer.WriteAsync);
        var block = writer.BlockNextWrite();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var writing = Task.Run(() => other.RecordIngestion("/data/retained.jsonl", new IngestionRecord(
            DateTime.UtcNow, "synthetic", "S", "Trade", 1)));
        try
        {
            await block.WaitUntilEnteredAsync(deadline.Token);
            using var cancellation = new CancellationTokenSource();
            var saving = _service.SaveAsync(cancellation.Token);
            saving.IsCompleted.Should().BeFalse();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
        }
        finally
        {
            block.Release();
        }
        await writing.WaitAsync(deadline.Token);
        await _service.SaveAsync(deadline.Token);
        new DataLineageService(_lineagePath, NullLogger<DataLineageService>.Instance)
            .GetLineageGraph("/data/retained.jsonl").Should().NotBeNull();
    }
}
