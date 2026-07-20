using System.Text;
using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Services;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for <see cref="FileMaintenanceService"/> checksum validation: the health check
/// must parse the sha256sum-format sidecars written by <see cref="AtomicFileWriter"/>
/// ("{checksum}  {filename}"), so healthy files are not flagged as corrupt while genuine
/// tampering still is.
/// </summary>
public sealed class FileMaintenanceServiceTests : TempDirectoryAsyncTestBase
{
    [Fact]
    public async Task RunHealthCheckAsync_WithHealthySidecarProtectedFile_ReportsNoChecksumMismatch()
    {
        var dataPath = Path.Combine(TestDataRoot, "SPY_trade_20260110.jsonl");
        await AtomicFileWriter.WriteWithChecksumAsync(dataPath, Encoding.UTF8.GetBytes("{\"price\":450.0}\n"));

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var report = await service.RunHealthCheckAsync(new HealthCheckOptions(
            ValidateChecksums: true,
            IdentifyCorruption: false));

        report.Issues.Should().NotContain(i => i.Type == IssueType.ChecksumMismatch,
            "an untouched file whose sidecar was written by AtomicFileWriter is healthy");
    }

    [Fact]
    public async Task RunHealthCheckAsync_WithTamperedSidecarProtectedFile_FlagsChecksumMismatch()
    {
        var dataPath = Path.Combine(TestDataRoot, "SPY_trade_20260110.jsonl");
        await AtomicFileWriter.WriteWithChecksumAsync(dataPath, Encoding.UTF8.GetBytes("{\"price\":450.0}\n"));
        await File.WriteAllTextAsync(dataPath, "{\"price\":999.0}\n");

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var report = await service.RunHealthCheckAsync(new HealthCheckOptions(
            ValidateChecksums: true,
            IdentifyCorruption: false));

        report.Issues.Should().ContainSingle(i => i.Type == IssueType.ChecksumMismatch,
            "content that no longer matches its sidecar checksum must still be detected");
    }

    [Fact]
    public async Task RepairAsync_TruncateCorrupted_SalvagesValidTailAndQuarantinesCorruptLines()
    {
        // The old repair stopped at the first invalid line and discarded the valid tail, and
        // never refreshed the sidecar (so a repaired file reported ChecksumMismatch forever).
        var dataPath = Path.Combine(TestDataRoot, "AAPL_trade_20260110.jsonl");
        var content = "{\"seq\":1}\nnot-json-1\n{\"seq\":2}\nnot-json-2\n";
        await AtomicFileWriter.WriteWithChecksumAsync(dataPath, Encoding.UTF8.GetBytes(content));

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.TruncateCorrupted));

        result.FilesRepaired.Should().BeGreaterThanOrEqualTo(1);
        var repairedLines = await File.ReadAllLinesAsync(dataPath);
        repairedLines.Where(l => !string.IsNullOrWhiteSpace(l)).Should().Equal("{\"seq\":1}", "{\"seq\":2}");

        var quarantined = await File.ReadAllLinesAsync(dataPath + ".corrupt-lines");
        quarantined.Where(l => !string.IsNullOrWhiteSpace(l)).Should().Equal("not-json-1", "not-json-2");

        File.Exists(dataPath + ".pre-repair.bak").Should().BeTrue(
            "repairs without a configured BackupPath must back up next to the file");
        (await AtomicFileWriter.VerifyChecksumAsync(dataPath)).Should().BeTrue(
            "the sha256 sidecar must be refreshed to match the salvaged content");
    }

    [Fact]
    public async Task RepairAsync_TruncateCorrupted_RefusesCompressedFilesInsteadOfEmptyingThem()
    {
        // On a compressed file every "line" fails JSON parsing, so the old repair atomically
        // rewrote the file as empty — destroying the data it was meant to protect.
        var dataPath = Path.Combine(TestDataRoot, "MSFT_trade_20260110.jsonl.gz");
        await using (var fileStream = File.Create(dataPath))
        await using (var gzip = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionLevel.Optimal))
        {
            var payload = Encoding.UTF8.GetBytes("{\"price\":300.0}\n");
            await gzip.WriteAsync(payload);
        }
        var originalBytes = await File.ReadAllBytesAsync(dataPath);
        await File.WriteAllTextAsync(dataPath + ".sha256", $"{new string('0', 64)}  {Path.GetFileName(dataPath)}");

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.TruncateCorrupted));

        result.Errors.Should().ContainSingle().Which.Should().Contain("only supports plain .jsonl");
        (await File.ReadAllBytesAsync(dataPath)).Should().Equal(originalBytes,
            "a refused repair must leave the compressed file byte-identical");
    }

    [Fact]
    public async Task RepairAsync_RecompressOptimal_LeavesSingleCanonicalCompressedCopy()
    {
        // The old recompress left both the .jsonl and the .gz on disk, so replay double-counted
        // every event; it also wrote the .gz non-atomically.
        var dataPath = Path.Combine(TestDataRoot, "SPY_trade_20260109.jsonl");
        var content = "{\"seq\":1}\nnot-json\n";
        await File.WriteAllTextAsync(dataPath, content);

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.RecompressOptimal));

        result.FilesRepaired.Should().Be(1);
        File.Exists(dataPath).Should().BeFalse("the plain file must be removed after recompression");
        var compressedPath = dataPath + ".gz";
        File.Exists(compressedPath).Should().BeTrue();
        (await ReadGzipTextAsync(compressedPath)).Should().Be(content,
            "the compressed copy must round-trip the original bytes");
    }

    [Fact]
    public async Task RepairAsync_DirectoryScope_RepairsOnlyFilesUnderTarget()
    {
        var scopedDir = Path.Combine(TestDataRoot, "alpaca");
        var otherDir = Path.Combine(TestDataRoot, "polygon");
        Directory.CreateDirectory(scopedDir);
        Directory.CreateDirectory(otherDir);
        var scopedFile = Path.Combine(scopedDir, "AAPL_20260110.jsonl");
        var otherFile = Path.Combine(otherDir, "SPY_20260110.jsonl");
        await File.WriteAllTextAsync(scopedFile, "{\"seq\":1}\nnot-json\n");
        await File.WriteAllTextAsync(otherFile, "{\"seq\":1}\nnot-json\n");

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.RepairAsync(new RepairOptions(
            RepairStrategy.TruncateCorrupted,
            Scope: RepairScope.Directory,
            ScopeTarget: scopedDir));

        result.FilesProcessed.Should().Be(1);
        File.Exists(scopedFile + ".corrupt-lines").Should().BeTrue();
        File.Exists(otherFile + ".corrupt-lines").Should().BeFalse(
            "files outside the scoped directory must not be touched");
    }

    [Fact]
    public async Task RepairAsync_SymbolScope_ThrowsInsteadOfMatchingEverything()
    {
        // The old MatchesScope returned true for every scope value, so a narrow-sounding scope
        // silently repaired the entire store.
        var dataPath = Path.Combine(TestDataRoot, "AAPL_20260110.jsonl");
        await File.WriteAllTextAsync(dataPath, "{\"seq\":1}\nnot-json\n");

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var act = () => service.RepairAsync(new RepairOptions(
            RepairStrategy.TruncateCorrupted,
            Scope: RepairScope.Symbol,
            ScopeTarget: "AAPL"));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task DefragmentAsync_MergesOnlyPlainJsonlAndSkipsCompressedFiles()
    {
        var dir = Path.Combine(TestDataRoot, "alpaca");
        Directory.CreateDirectory(dir);
        var jsonlA = Path.Combine(dir, "a_20260101.jsonl");
        var jsonlB = Path.Combine(dir, "b_20260102.jsonl");
        var gzA = Path.Combine(dir, "c_20260103.jsonl.gz");
        var gzB = Path.Combine(dir, "d_20260104.jsonl.gz");
        await File.WriteAllTextAsync(jsonlA, "{\"seq\":1}\n");
        await File.WriteAllTextAsync(jsonlB, "{\"seq\":2}\n");
        await File.WriteAllBytesAsync(gzA, [0x1f, 0x8b, 0x08, 0x00, 0x01]);
        await File.WriteAllBytesAsync(gzB, [0x1f, 0x8b, 0x08, 0x00, 0x02]);
        foreach (var f in new[] { jsonlA, jsonlB, gzA, gzB })
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-2));
        var gzABytes = await File.ReadAllBytesAsync(gzA);
        var gzBBytes = await File.ReadAllBytesAsync(gzB);

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.DefragmentAsync(new DefragOptions(MaxFileAge: TimeSpan.FromDays(1)));

        result.FilesCreated.Should().Be(1);
        File.Exists(jsonlA).Should().BeFalse();
        File.Exists(jsonlB).Should().BeFalse();
        var merged = Directory.GetFiles(dir, "merged_*.jsonl").Should().ContainSingle().Subject;
        var mergedContent = await File.ReadAllTextAsync(merged);
        mergedContent.Should().Contain("{\"seq\":1}").And.Contain("{\"seq\":2}");

        // Compressed files were previously text-concatenated (irreversibly mangling their bytes)
        // and then deleted; they must now be left completely untouched.
        (await File.ReadAllBytesAsync(gzA)).Should().Equal(gzABytes);
        (await File.ReadAllBytesAsync(gzB)).Should().Equal(gzBBytes);
    }

    [Fact]
    public async Task DefragmentAsync_DryRun_PerformsNoFilesystemMutations()
    {
        // The scheduler previously mapped DryRun onto PreserveOriginals, so a "dry run" still
        // wrote real merged_* files that duplicated events on replay.
        var dir = Path.Combine(TestDataRoot, "polygon");
        Directory.CreateDirectory(dir);
        var jsonlA = Path.Combine(dir, "a_20260101.jsonl");
        var jsonlB = Path.Combine(dir, "b_20260102.jsonl");
        await File.WriteAllTextAsync(jsonlA, "{\"seq\":1}\n");
        await File.WriteAllTextAsync(jsonlB, "{\"seq\":2}\n");
        foreach (var f in new[] { jsonlA, jsonlB })
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-2));

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.DefragmentAsync(new DefragOptions(MaxFileAge: TimeSpan.FromDays(1), DryRun: true));

        result.FilesProcessed.Should().Be(2, "the dry run must still report the would-merge plan");
        result.FilesCreated.Should().Be(0);
        File.Exists(jsonlA).Should().BeTrue();
        File.Exists(jsonlB).Should().BeTrue();
        Directory.GetFiles(dir, "merged_*", SearchOption.AllDirectories).Should().BeEmpty(
            "a dry run must not write any merged files");
    }

    [Fact]
    public async Task DefragmentAsync_WhenMergedSourceCannotBeDeleted_ReportsFailedGroupAndSurvivingBytes()
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows file-share semantics provide the deterministic undeletable source used here.

        var dir = Path.Combine(TestDataRoot, "locked-source");
        Directory.CreateDirectory(dir);
        var lockedPath = Path.Combine(dir, "a_20260101.jsonl");
        var deletablePath = Path.Combine(dir, "b_20260102.jsonl");
        await File.WriteAllTextAsync(lockedPath, "{\"seq\":1}\n");
        await File.WriteAllTextAsync(deletablePath, "{\"seq\":2}\n");
        File.SetLastWriteTimeUtc(lockedPath, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(deletablePath, DateTime.UtcNow.AddDays(-2));
        var lockedLength = new FileInfo(lockedPath).Length;
        await using var lockHandle = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.DefragmentAsync(new DefragOptions(MaxFileAge: TimeSpan.FromDays(1)));

        result.MergeGroupsAttempted.Should().Be(1);
        result.MergeGroupsSucceeded.Should().Be(0);
        result.Errors.Should().ContainSingle(error => error.Contains(lockedPath, StringComparison.Ordinal));
        File.Exists(lockedPath).Should().BeTrue();
        result.FilesDeleted.Should().Be(1);
        result.BytesAfter.Should().BeGreaterThan(lockedLength,
            "the actual footprint includes both the merged file and the source that survived deletion");
    }

    private static async Task<string> ReadGzipTextAsync(string path)
    {
        await using var fileStream = File.OpenRead(path);
        await using var gzip = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync();
    }
}
