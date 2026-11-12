using Meridian.Storage.Archival;
using Meridian.Testing;

namespace Meridian.Tests.Storage;

public sealed class JsonlAppendWriterTests
{
    [Fact]
    public async Task AppendLinesAsync_AppendsAfterExistingLinesWithoutRewritingThem()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(AppendLinesAsync_AppendsAfterExistingLinesWithoutRewritingThem));
        var path = Path.Combine(artifacts.RootPath, "logs", "events.jsonl");

        await JsonlAppendWriter.AppendLinesAsync(path, ["{\"n\":1}"]);
        await JsonlAppendWriter.AppendLinesAsync(path, ["{\"n\":2}", "{\"n\":3}"]);

        Assert.Equal("{\"n\":1}\n{\"n\":2}\n{\"n\":3}\n", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp*"));
    }

    [Fact]
    public async Task AppendLinesAsync_TornFinalLine_IsTruncatedBeforeTheAppend()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(AppendLinesAsync_TornFinalLine_IsTruncatedBeforeTheAppend));
        var path = Path.Combine(artifacts.RootPath, "events.jsonl");
        // A crash part-way through an append leaves a final line with no newline.
        await File.WriteAllTextAsync(path, "{\"n\":1}\n{\"n\":2");

        await JsonlAppendWriter.AppendLinesAsync(path, ["{\"n\":3}"]);

        Assert.Equal("{\"n\":1}\n{\"n\":3}\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AppendLinesAsync_Cancelled_LeavesTheFileUnchanged()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(AppendLinesAsync_Cancelled_LeavesTheFileUnchanged));
        var path = Path.Combine(artifacts.RootPath, "events.jsonl");
        await JsonlAppendWriter.AppendLinesAsync(path, ["{\"n\":1}"]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            JsonlAppendWriter.AppendLinesAsync(path, ["{\"n\":2}"], cancellation.Token));

        Assert.Equal("{\"n\":1}\n", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AppendLinesAsync_NoLines_DoesNotCreateTheFile()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(AppendLinesAsync_NoLines_DoesNotCreateTheFile));
        var path = Path.Combine(artifacts.RootPath, "events.jsonl");

        await JsonlAppendWriter.AppendLinesAsync(path, []);

        Assert.False(File.Exists(path));
    }
}
