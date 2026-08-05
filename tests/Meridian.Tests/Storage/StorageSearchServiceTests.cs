using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Tests.Infrastructure;

namespace Meridian.Tests.Storage;

public sealed class StorageSearchServiceTests : TempDirectoryAsyncTestBase
{
    [Fact]
    public async Task RebuildIndexAsync_ReturnsMatchingCanonicalReadbackProof()
    {
        var firstPath = Path.Combine(TestDataRoot, "AAPL_trade_20260110.jsonl");
        await File.WriteAllTextAsync(firstPath, "{\"seq\":1}\n");
        var service = new StorageSearchService(new StorageOptions { RootPath = TestDataRoot });

        var first = await service.RebuildIndexAsync([TestDataRoot], new RebuildOptions());

        first.Before.IndexedFileCount.Should().Be(0);
        first.After.IndexedFileCount.Should().Be(1);
        first.DiscoveredFileCount.Should().Be(1);
        first.AllDiscoveredFilesIndexed.Should().BeTrue();
        first.ReadbackVerified.Should().BeTrue();
        first.IsVerified.Should().BeTrue();
        first.After.DigestSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        first.Readback.Should().BeEquivalentTo(first.After, options => options.Excluding(item => item.CapturedAtUtc));

        var secondPath = Path.Combine(TestDataRoot, "MSFT_trade_20260110.jsonl");
        await File.WriteAllTextAsync(secondPath, "{\"seq\":2}\n");
        var second = await service.RebuildIndexAsync([TestDataRoot], new RebuildOptions());

        second.Before.IndexedFileCount.Should().Be(1);
        second.Before.DigestSha256.Should().Be(first.After.DigestSha256);
        second.After.IndexedFileCount.Should().Be(2);
        second.After.DigestSha256.Should().NotBe(second.Before.DigestSha256);
        second.ReadbackVerified.Should().BeTrue();
    }

    [Fact]
    public async Task RebuildIndexAsync_WhenRequestedScopeIsEmpty_PreservesLiveIndex()
    {
        var dataPath = Path.Combine(TestDataRoot, "AAPL_trade_20260110.jsonl");
        await File.WriteAllTextAsync(dataPath, "{\"seq\":1}\n");
        var emptyScope = Path.Combine(TestDataRoot, "empty");
        Directory.CreateDirectory(emptyScope);
        var service = new StorageSearchService(new StorageOptions { RootPath = TestDataRoot });
        await service.RebuildIndexAsync([TestDataRoot], new RebuildOptions());
        GetMetadataIndex(service).Should().ContainKey(Path.GetFullPath(dataPath));

        var act = () => service.RebuildIndexAsync([emptyScope], new RebuildOptions());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*live index was preserved*");
        GetMetadataIndex(service).Should().ContainKey(Path.GetFullPath(dataPath));
    }

    [Fact]
    public async Task RebuildIndexAsync_WithoutExplicitNonemptyPath_RejectsRequestAndPreservesLiveIndex()
    {
        var dataPath = Path.Combine(TestDataRoot, "MSFT_trade_20260110.jsonl");
        await File.WriteAllTextAsync(dataPath, "{\"seq\":1}\n");
        var service = new StorageSearchService(new StorageOptions { RootPath = TestDataRoot });
        await service.RebuildIndexAsync([TestDataRoot], new RebuildOptions());

        var act = () => service.RebuildIndexAsync(["", "   "], new RebuildOptions());

        await act.Should().ThrowAsync<ArgumentException>();
        GetMetadataIndex(service).Should().ContainKey(Path.GetFullPath(dataPath));
    }

    private static ConcurrentDictionary<string, FileMetadata> GetMetadataIndex(StorageSearchService service) =>
        (ConcurrentDictionary<string, FileMetadata>)typeof(StorageSearchService)
            .GetField("_fileMetadata", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;
}
