using FluentAssertions;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed partial class MetadataTagServiceTests
{
    [Fact]
    public void Constructor_InvalidUtf8InsideRetainedValue_RejectsWithoutReplacingBytes()
    {
        _service.SetTag("source", "note", "marker");
        var json = File.ReadAllText(_metadataPath);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        bytes[json.IndexOf("marker", StringComparison.Ordinal)] = 0xff;
        File.WriteAllBytes(_metadataPath, bytes);
        var restart = () => new MetadataTagService(_metadataPath);
        restart.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(_metadataPath).Should().Equal(bytes);
    }

    [Fact]
    public void Constructor_NullNestedHistory_RefusesToReplaceItWithEmptyEvidence()
    {
        _service.SetTag("source", "provider", "synthetic");
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(_metadataPath))!;
        root["records"]!["source"]!["tags"] = null;
        var json = root.ToJsonString();
        File.WriteAllText(_metadataPath, json);
        var restart = () => new MetadataTagService(_metadataPath);
        restart.Should().Throw<InvalidDataException>();
        File.ReadAllText(_metadataPath).Should().Be(json);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":\"2.0.0\",\"records\":{}}")]
    [InlineData("{\"version\":\"1.0.0\",\"records\":null}")]
    [InlineData("{\"version\":\"1.0.0\",\"records\":{\"source\":{\"filePath\":\"other\"}}}")]
    public void Constructor_CorruptOrUnsupportedSnapshot_RefusesToEraseRetainedEvidence(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_metadataPath)!);
        File.WriteAllText(_metadataPath, json);

        var restart = () => new MetadataTagService(_metadataPath);

        restart.Should().Throw<InvalidDataException>();
        File.ReadAllText(_metadataPath).Should().Be(json);
    }

    [Fact]
    public async Task ConcurrentInstances_MergeRetainedTagsAndStaleSaveCannotEraseCommits()
    {
        var services = Enumerable.Range(0, 8).Select(_ => new MetadataTagService(_metadataPath)).ToArray();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = services.Select((service, index) => Task.Run(async () =>
        {
            await gate.Task;
            service.SetTag("/data/retained.jsonl", $"key{index}", $"value{index}");
        })).ToArray();
        gate.SetResult();
        await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(10));
        await _service.SaveAsync();

        var restarted = new MetadataTagService(_metadataPath);
        restarted.GetAllTags("/data/retained.jsonl").Should().HaveCount(8);
        foreach (var index in Enumerable.Range(0, 8))
            restarted.GetTag("/data/retained.jsonl", $"key{index}").Should().Be($"value{index}");
    }

    [Fact]
    public void Mutation_AfterSnapshotCorruptionOrRemoval_RetainsPublishedTagsAndRefusesWrite()
    {
        _service.SetTag("/data/retained.jsonl", "source", "synthetic");
        File.WriteAllText(_metadataPath, "{");
        var mutation = () => _service.RemoveTag("/data/retained.jsonl", "source");

        mutation.Should().Throw<InvalidDataException>();
        _service.GetTag("/data/retained.jsonl", "source").Should().Be("synthetic");
        File.ReadAllText(_metadataPath).Should().Be("{");
        File.Delete(_metadataPath);
        mutation.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public async Task Mutation_WaitsForSlowOtherInstanceAndReloadsCommittedTags()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var other = new MetadataTagService(_metadataPath, writer.Write, writer.WriteAsync);
        var block = writer.BlockNextWrite();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var first = Task.Run(() => other.SetTag("/data/retained.jsonl", "first", "one"));
        Task? second = null;
        try
        {
            await block.WaitUntilEnteredAsync(deadline.Token);
            second = _service.SetQualityAssessmentAsync("/data/retained.jsonl", 0.9,
                new DataInsight("quality", "Synthetic", 0.9, "score", DateTime.UtcNow), ct: deadline.Token);
            second.IsCompleted.Should().BeFalse();
            _service.GetQualityScore("/data/retained.jsonl").Should().BeNull();
        }
        finally
        {
            block.Release();
        }
        await first.WaitAsync(deadline.Token);
        await second!.WaitAsync(deadline.Token);
        var restarted = new MetadataTagService(_metadataPath);
        restarted.GetTag("/data/retained.jsonl", "first").Should().Be("one");
        restarted.GetQualityScore("/data/retained.jsonl").Should().Be(0.9);
    }
}
