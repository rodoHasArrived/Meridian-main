using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Integrity;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;

namespace Meridian.Tests.Storage;

public sealed partial class StorageCatalogServiceTests
{
    private StorageCatalogService NewCatalog(Func<string, string, CancellationToken, Task>? writer = null)
    {
        var options = new StorageOptions { RootPath = _testDirectory };
        return writer is null ? new StorageCatalogService(_testDirectory, options)
            : new StorageCatalogService(_testDirectory, options, writer);
    }

    [Fact]
    public async Task CurrentPublicationFails_RestartUsesCompleteLastGoodSnapshotAndPreservesCandidateHistory()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var failCurrent = false;
        var service = NewCatalog((path, json, ct) =>
        {
            if (failCurrent && Path.GetFileName(path) == "manifest.json")
                throw new IOException("Synthetic current-publication failure.");
            return writer.WriteAsync(path, json, ct);
        });
        await service.InitializeAsync();
        await service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/retained.jsonl", Symbol = "S", EventCount = 1 });
        var current = Path.Combine(_testDirectory, "_catalog", "manifest.json");
        var retained = await File.ReadAllTextAsync(current);
        failCurrent = true;

        await Assert.ThrowsAsync<IOException>(() => service.UpdateFileEntryAsync(new IndexedFileEntry
        { RelativePath = "S/candidate.jsonl", Symbol = "S", EventCount = 2 }));

        service.GetFilesForSymbol("S").Should().ContainSingle().Which.EventCount.Should().Be(1);
        (await File.ReadAllTextAsync(current)).Should().Be(retained);
        // A loose compatibility index can already name the failed candidate. It is not authority.
        (await File.ReadAllTextAsync(Path.Combine(_testDirectory, "S", "_index.json"))).Should().Contain("candidate.jsonl");
        var restarted = NewCatalog();
        await restarted.InitializeAsync();
        restarted.GetFilesForSymbol("S").Should().ContainSingle().Which.EventCount.Should().Be(1);
        Directory.GetFiles(Path.Combine(_testDirectory, "_catalog", "revisions"), "*.json").Should().HaveCount(3);
        failCurrent = false;
        await service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/retry.jsonl", Symbol = "S", EventCount = 3 });
        var afterRetry = NewCatalog();
        await afterRetry.InitializeAsync();
        afterRetry.GetFilesForSymbol("S").Select(file => file.EventCount).Should().BeEquivalentTo([1L, 3L]);
    }

    [Fact]
    public async Task StaleCatalogVersion_CannotOverwriteCurrentOrItsDirectorySnapshot()
    {
        await _service.InitializeAsync();
        var other = NewCatalog();
        await other.InitializeAsync();
        await _service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/first.jsonl", Symbol = "S", EventCount = 1 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => other.UpdateFileEntryAsync(new IndexedFileEntry
        { RelativePath = "S/stale.jsonl", Symbol = "S", EventCount = 99 }));
        other.GetFilesForSymbol("S").Should().BeEmpty();
        var restarted = NewCatalog();
        await restarted.InitializeAsync();
        restarted.GetFilesForSymbol("S").Should().ContainSingle().Which.EventCount.Should().Be(1);
        await other.InitializeAsync();
        await other.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/second.jsonl", Symbol = "S", EventCount = 2 });
        await restarted.InitializeAsync();
        restarted.GetFilesForSymbol("S").Select(file => file.EventCount).Should().BeEquivalentTo([1L, 2L]);
    }

    [Fact]
    public async Task SlowCurrentPublication_ReadersAndRestartRetainPreviousGenerationUntilCommit()
    {
        using var writer = new AtomicSnapshotTestWriter();
        var service = NewCatalog(writer.WriteAsync);
        await service.InitializeAsync();
        var block = writer.BlockNextWrite();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var updating = service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/new.jsonl", Symbol = "S", EventCount = 1 });
        try
        {
            await block.WaitUntilEnteredAsync(timeout.Token);
            service.GetFilesForSymbol("S").Should().BeEmpty();
            var restarted = NewCatalog();
            await restarted.InitializeAsync(timeout.Token);
            restarted.GetFilesForSymbol("S").Should().BeEmpty();
        }
        finally
        {
            block.Release();
        }
        await updating.WaitAsync(timeout.Token);
        service.GetFilesForSymbol("S").Should().ContainSingle();
    }

    [Fact]
    public async Task LegacyMigration_RetainsOnlySurvivingCompleteDirectorySnapshot()
    {
        var catalogDirectory = Path.Combine(_testDirectory, "_catalog");
        Directory.CreateDirectory(catalogDirectory);
        Directory.CreateDirectory(Path.Combine(_testDirectory, "S"));
        var legacy = new StorageCatalog { CatalogId = "legacy", DirectoryIndexes = ["S"] };
        await File.WriteAllTextAsync(Path.Combine(catalogDirectory, "manifest.json"), JsonSerializer.Serialize(legacy));
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "S", "_index.json"), JsonSerializer.Serialize(new DirectoryIndex
        { RelativePath = "S", Files = [new IndexedFileEntry { RelativePath = "S/old.jsonl", Symbol = "S", EventCount = 4 }] }));

        await _service.InitializeAsync();
        File.Delete(Path.Combine(_testDirectory, "S", "_index.json"));
        var restarted = NewCatalog();
        await restarted.InitializeAsync();

        restarted.GetCatalog().CatalogId.Should().Be("legacy");
        restarted.GetFilesForSymbol("S").Should().ContainSingle().Which.EventCount.Should().Be(4);
        Directory.GetFiles(Path.Combine(catalogDirectory, "revisions"), "*.json").Should().ContainSingle();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task CorruptCurrent_InitializationAndMutationFailWithoutReplacingRetainedHistory(string json)
    {
        await _service.InitializeAsync();
        var manifest = Path.Combine(_testDirectory, "_catalog", "manifest.json");
        await File.WriteAllTextAsync(manifest, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => NewCatalog().InitializeAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.SaveCatalogAsync());
        (await File.ReadAllTextAsync(manifest)).Should().Be(json);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptReferencedRevision_FailsClosedAndNeverReplacesIt(bool prefixBom)
    {
        await _service.InitializeAsync();
        var manifest = Path.Combine(_testDirectory, "_catalog", "manifest.json");
        var pointer = JsonNode.Parse(await File.ReadAllTextAsync(manifest))!;
        var digest = pointer["snapshotContentDigest"]!.GetValue<string>();
        var revision = Path.Combine(_testDirectory, "_catalog", "revisions", digest + ".json");
        var original = await File.ReadAllBytesAsync(revision);
        byte[] corrupt = prefixBom ? [0xef, 0xbb, 0xbf, .. original] : System.Text.Encoding.UTF8.GetBytes("{}");
        await File.WriteAllBytesAsync(revision, corrupt);

        await Assert.ThrowsAsync<InvalidDataException>(() => NewCatalog().InitializeAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => _service.SaveCatalogAsync());
        (await File.ReadAllBytesAsync(revision)).Should().Equal(corrupt);
    }

    [Fact]
    public async Task SavedRevision_HasExactDigestAndStableContentOnRetry_GettersCannotMutateAuthority()
    {
        await _service.InitializeAsync();
        await _service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/retained.jsonl", Symbol = "S", EventCount = 1 });
        var manifest = Path.Combine(_testDirectory, "_catalog", "manifest.json");
        var before = await File.ReadAllTextAsync(manifest);
        var pointer = JsonNode.Parse(before)!;
        var digest = pointer["snapshotContentDigest"]!.GetValue<string>();
        var snapshot = await File.ReadAllTextAsync(Path.Combine(_testDirectory, "_catalog", "revisions", digest + ".json"));
        Sha256Digest.ComputeUtf8(snapshot).Should().Be(digest);
        var catalog = _service.GetCatalog();
        catalog.Symbols.Clear();
        _service.GetFilesForSymbol("S").Single().EventCount = 99;
        (await _service.GetDirectoryIndexAsync("S"))!.Files.Clear();
        _service.GetStatistics().TotalFiles = 0;
        await _service.SaveCatalogAsync();

        (await File.ReadAllTextAsync(manifest)).Should().Be(before);
        var restarted = NewCatalog();
        await restarted.InitializeAsync();
        restarted.GetFilesForSymbol("S").Should().ContainSingle().Which.EventCount.Should().Be(1);
        restarted.GetStatistics().TotalFiles.Should().Be(1);
    }
    [Fact]
    public async Task RemoveLastSymbolFile_ClearsDerivedScopeAndVerificationAcrossRestart()
    {
        await _service.InitializeAsync();
        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        { RelativePath = "S/dated.jsonl", Symbol = "S", EventCount = 3, Date = new DateTime(2026, 11, 13) });
        _service.GetCatalog().DateRange.Should().NotBeNull();
        await _service.RemoveFileEntryAsync("S/dated.jsonl");
        var restarted = NewCatalog();
        await restarted.InitializeAsync();

        restarted.GetCatalog().Symbols.Should().BeEmpty();
        restarted.GetCatalog().DateRange.Should().BeNull();
        (await restarted.GetDirectoryIndexAsync("S"))!.DateRange.Should().BeNull();
        restarted.GetStatistics().UniqueSymbols.Should().Be(0);
        restarted.GetStatistics().TotalFiles.Should().Be(0);
        restarted.GetStatistics().TotalEvents.Should().Be(0);
    }

    [Fact]
    public async Task IntegrityVerification_ConcurrentGenerationChange_RefusesToCertifyUnverifiedNewFiles()
    {
        await _service.InitializeAsync();
        await _service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/first.jsonl", Symbol = "S" });
        var mutated = false;
        var progress = new InlineVerificationProgress(_ =>
        {
            if (mutated)
                return;
            mutated = true;
            _service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/second.jsonl", Symbol = "S" }).GetAwaiter().GetResult();
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.VerifyIntegrityAsync(
            new CatalogVerificationOptions { VerifyFileExists = false, VerifyChecksums = false }, progress));
        var restarted = NewCatalog();
        await restarted.InitializeAsync();
        restarted.GetCatalog().Integrity.Status.Should().NotBe("Verified");
        restarted.GetCatalog().Integrity.FilesVerified.Should().Be(0);
        restarted.GetFilesForSymbol("S").Should().HaveCount(2);
    }

    [Fact]
    public async Task MissingCurrentWithRetainedHistory_RestartRefusesToInventAnEmptyCatalog()
    {
        await _service.InitializeAsync();
        await _service.UpdateFileEntryAsync(new IndexedFileEntry { RelativePath = "S/retained.jsonl", Symbol = "S" });
        var manifest = Path.Combine(_testDirectory, "_catalog", "manifest.json");
        var revisions = Directory.GetFiles(Path.Combine(_testDirectory, "_catalog", "revisions"), "*.json");
        File.Delete(manifest);

        await Assert.ThrowsAsync<FileNotFoundException>(() => NewCatalog().InitializeAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveCatalogAsync());
        File.Exists(manifest).Should().BeFalse();
        revisions.Should().OnlyContain(path => File.Exists(path));
    }

    [Fact]
    public async Task ConcurrentPublishers_ExactlyOneGenerationBecomesCurrent()
    {
        var publishers = new[] { NewCatalog(), NewCatalog() };
        await publishers[0].InitializeAsync();
        await publishers[1].InitializeAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = publishers.Select((publisher, index) => Task.Run(async () =>
        {
            await gate.Task;
            return await Record.ExceptionAsync(() => publisher.UpdateFileEntryAsync(new IndexedFileEntry
            { RelativePath = $"S/{index}.jsonl", Symbol = "S", EventCount = index + 1 }));
        })).ToArray();
        gate.SetResult();
        var outcomes = await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(10));

        outcomes.Should().ContainSingle(exception => exception == null);
        outcomes.Should().ContainSingle(exception => exception is InvalidOperationException);
        var restarted = NewCatalog();
        await restarted.InitializeAsync();
        restarted.GetFilesForSymbol("S").Should().ContainSingle();
        restarted.GetStatistics().TotalFiles.Should().Be(1);
    }

    private sealed class InlineVerificationProgress(Action<CatalogVerificationProgress> report) : IProgress<CatalogVerificationProgress>
    {
        public void Report(CatalogVerificationProgress value) => report(value);
    }

}
