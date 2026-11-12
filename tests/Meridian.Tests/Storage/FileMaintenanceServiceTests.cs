using System.Text;
using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Services;
using Meridian.Storage.Policies;
using Meridian.Storage.Sinks;
using Meridian.Domain.Events;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
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
    public async Task RunHealthCheckAsync_ArchiveBeingAppended_IsCheckedOnTheNextSweepNotMisreported()
    {
        var archivePath = Path.Combine(TestDataRoot, "SPY_trade_20260110.jsonl.gz");
        await using (var gzip = new System.IO.Compression.GZipStream(File.Create(archivePath), System.IO.Compression.CompressionLevel.Fastest))
            await gzip.WriteAsync(Encoding.UTF8.GetBytes("{\"price\":450.0}\n"));
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var checksumsOnly = new HealthCheckOptions(ValidateChecksums: true, IdentifyCorruption: false);

        // An appender has retired the sidecar and holds the archive's lock while it rewrites the bytes.
        using (var appender = new FileStream(ArchiveAppendLock.PathFor(archivePath), FileMode.OpenOrCreate,
                   FileAccess.ReadWrite, FileShare.None))
        {
            var during = await service.RunHealthCheckAsync(checksumsOnly);
            during.Issues.Should().NotContain(issue => issue.Path == archivePath,
                "a sidecar retired by an in-flight append is not lost coverage");
        }

        var after = await service.RunHealthCheckAsync(checksumsOnly);
        after.Issues.Should().ContainSingle(issue => issue.Type == IssueType.MissingChecksum && issue.Path == archivePath);
    }

    [Fact]
    public async Task RunHealthCheckAsync_CompressedArchiveWithoutSidecar_ReportsMissingChecksum()
    {
        // Compression always publishes a sidecar beside the archive, so a bare archive has lost
        // its coverage and must not pass as healthy; an open plain day file never had one.
        var archivePath = Path.Combine(TestDataRoot, "SPY_trade_20260110.jsonl.gz");
        await using (var gzip = new System.IO.Compression.GZipStream(File.Create(archivePath), System.IO.Compression.CompressionLevel.Fastest))
            await gzip.WriteAsync(Encoding.UTF8.GetBytes("{\"price\":450.0}\n"));
        await File.WriteAllTextAsync(Path.Combine(TestDataRoot, "SPY_trade_20260111.jsonl"), "{\"price\":451.0}\n");

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var report = await service.RunHealthCheckAsync(new HealthCheckOptions(
            ValidateChecksums: true,
            IdentifyCorruption: false));

        report.Issues.Should().ContainSingle(i => i.Type == IssueType.MissingChecksum).Which.Path.Should().Be(archivePath);
        report.Issues.Should().NotContain(i => i.Type == IssueType.ChecksumMismatch);
    }

    [Fact]
    public async Task RepairAsync_TruncateCorrupted_SalvagesValidTailAndQuarantinesCorruptLines()
    {
        // The old repair stopped at the first invalid line and discarded the valid tail. The file
        // was published intact; corrupt lines were interleaved afterwards, so the salvaged records
        // reproduce the retained checksum.
        var dataPath = Path.Combine(TestDataRoot, "AAPL_trade_20260110.jsonl");
        await AtomicFileWriter.WriteWithChecksumAsync(dataPath, Encoding.UTF8.GetBytes("{\"seq\":1}\n{\"seq\":2}\n"));
        var content = "{\"seq\":1}\nnot-json-1\n{\"seq\":2}\nnot-json-2\n";
        await File.WriteAllTextAsync(dataPath, content);

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.TruncateCorrupted));

        result.FilesRepaired.Should().Be(1);
        result.Errors.Should().BeEmpty();
        var repairedLines = await File.ReadAllLinesAsync(dataPath);
        repairedLines.Where(l => !string.IsNullOrWhiteSpace(l)).Should().Equal("{\"seq\":1}", "{\"seq\":2}");

        var quarantinePath = result.RecoveryCopies.Should().ContainSingle(path => path.EndsWith(".corrupt-lines")).Subject;
        var quarantined = await File.ReadAllLinesAsync(quarantinePath);
        quarantined.Where(l => !string.IsNullOrWhiteSpace(l)).Should().Equal("not-json-1", "not-json-2");

        var backupPath = result.RecoveryCopies.Should().ContainSingle(path => path.StartsWith(dataPath + ".pre-repair.")).Subject;
        (await File.ReadAllTextAsync(backupPath)).Should().Be(content);
        (await AtomicFileWriter.VerifyChecksumAsync(dataPath)).Should().BeTrue(
            "the salvaged records reproduce the retained checksum");
    }

    [Fact]
    public async Task RepairAsync_TruncateCorrupted_AlteredRecordsBesideACorruptTail_LeaveTheChecksumMismatchUnresolved()
    {
        // A valid record was altered and a corrupt tail appended. Salvage removes the tail, but
        // the remaining records no longer match the retained checksum; rewriting the sidecar over
        // them would bless the alteration.
        var dataPath = Path.Combine(TestDataRoot, "AAPL_trade_20260110.jsonl");
        await AtomicFileWriter.WriteWithChecksumAsync(dataPath, Encoding.UTF8.GetBytes("{\"price\":450.0}\n"));
        var originalSidecar = await File.ReadAllTextAsync(dataPath + ".sha256");
        await File.WriteAllTextAsync(dataPath, "{\"price\":999.0}\nnot-json\n");

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.TruncateCorrupted));

        result.FilesRepaired.Should().Be(0);
        result.Errors.Should().ContainSingle(error => error.Contains(nameof(IssueType.ChecksumMismatch)));
        (await File.ReadAllLinesAsync(dataPath)).Where(l => !string.IsNullOrWhiteSpace(l)).Should().Equal("{\"price\":999.0}");
        (await File.ReadAllTextAsync(dataPath + ".sha256")).Should().Be(originalSidecar, "the retained checksum is never rewritten over altered records");
        (await AtomicFileWriter.VerifyChecksumAsync(dataPath)).Should().BeFalse();
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
    public async Task RepairAsync_RecompressOptimal_LeavesDamagedContentUnchangedAndUnsigned()
    {
        // Re-encoding cannot repair damaged content. Publishing it under a fresh archive checksum and
        // deleting the source would make the same damage read as healthy on every later check.
        var dataPath = Path.Combine(TestDataRoot, "SPY_trade_20260109.jsonl");
        var content = "{\"seq\":1}\nnot-json\n";
        await File.WriteAllTextAsync(dataPath, content);

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.RecompressOptimal));

        result.FilesRepaired.Should().Be(0, "encoding damaged content does not resolve its corruption");
        result.FilesChanged.Should().Be(0);
        result.Errors.Should().ContainSingle(error => error.Contains("recompression cannot repair"));
        (await File.ReadAllTextAsync(dataPath)).Should().Be(content);
        File.Exists(dataPath + ".gz").Should().BeFalse();
        File.Exists(dataPath + ".gz.sha256").Should().BeFalse();
    }

    [Fact]
    public async Task RepairAsync_RecompressWithExistingDestination_PreservesBothArchives()
    {
        var sourcePath = Path.Combine(TestDataRoot, "a.jsonl");
        const string original = "{\"seq\":1}\nbad-json\n";
        await File.WriteAllTextAsync(sourcePath, original);
        var compressedPath = sourcePath + ".gz";
        byte[] retainedCompressed = [0x1f, 0x8b, 0x08, 0x00, 0x01];
        await File.WriteAllBytesAsync(compressedPath, retainedCompressed);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.RecompressOptimal));

        result.FilesRepaired.Should().Be(0);
        result.Errors.Should().ContainSingle(error => error.Contains("recompression cannot repair"));
        (await File.ReadAllTextAsync(sourcePath)).Should().Be(original);
        (await File.ReadAllBytesAsync(compressedPath)).Should().Equal(retainedCompressed);
        Directory.GetFiles(TestDataRoot, "*.recompress.tmp").Should().BeEmpty();
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
        Directory.GetFiles(scopedDir, "*.corrupt-lines").Should().ContainSingle();
        Directory.GetFiles(otherDir, "*.corrupt-lines").Should().BeEmpty(
            "files outside the scoped directory must not be touched");
    }

    [Fact]
    public async Task RepairAsync_RequiredBackupFailure_LeavesEverySourceAndSidecarUntouched()
    {
        var dataPaths = new[] { Path.Combine(TestDataRoot, "a.jsonl"), Path.Combine(TestDataRoot, "b.jsonl") };
        const string original = "{\"seq\":1}\nnot-json\n";
        foreach (var path in dataPaths)
            await AtomicFileWriter.WriteWithChecksumAsync(path, Encoding.UTF8.GetBytes(original));
        var sidecars = await Task.WhenAll(dataPaths.Select(path => File.ReadAllTextAsync(path + ".sha256")));
        var blockedBackupPath = Path.Combine(TestDataRoot, "backup-destination");
        await File.WriteAllTextAsync(blockedBackupPath, "a file cannot hold the required backup directory");
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.RepairAsync(new RepairOptions(
            RepairStrategy.TruncateCorrupted, BackupPath: blockedBackupPath));

        result.FilesProcessed.Should().Be(2);
        result.FilesRepaired.Should().Be(0);
        result.FilesUnresolved.Should().Be(2);
        result.Errors.Should().HaveCount(2).And.OnlyContain(error => error.Contains("Backup failed"));
        (await Task.WhenAll(dataPaths.Select(path => File.ReadAllTextAsync(path)))).Should().OnlyContain(content => content == original);
        (await Task.WhenAll(dataPaths.Select(path => File.ReadAllTextAsync(path + ".sha256")))).Should().Equal(sidecars);
        Directory.GetFiles(TestDataRoot, "*.corrupt-lines", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task RepairAsync_SameNamedSourcesAndRepeatedRepairs_PreserveDistinctRecoveryCopies()
    {
        var dataPaths = new[] { "alpaca", "polygon" }.Select(provider =>
        {
            var directory = Path.Combine(TestDataRoot, provider);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "a.jsonl");
        }).ToArray();
        var originals = new[] { "{\"seq\":1}\nfirst-corruption\n", "{\"seq\":2}\nsecond-corruption\n" };
        for (var i = 0; i < dataPaths.Length; i++)
            await File.WriteAllTextAsync(dataPaths[i], originals[i]);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });
        var options = new RepairOptions(RepairStrategy.TruncateCorrupted, BackupPath: Path.Combine(TestDataRoot, "backups"));

        var first = await service.RepairAsync(options);
        first.FilesRepaired.Should().Be(2);
        first.RecoveryCopies.Should().HaveCount(4).And.OnlyHaveUniqueItems();
        var firstContents = await Task.WhenAll(first.RecoveryCopies.Select(path => File.ReadAllTextAsync(path)));
        var backupContents = await Task.WhenAll(first.RecoveryCopies.Where(path => path.EndsWith(".bak")).Select(path => File.ReadAllTextAsync(path)));
        backupContents.Should().BeEquivalentTo(originals);

        await File.AppendAllTextAsync(dataPaths[0], "third-corruption\n");
        var second = await service.RepairAsync(options);

        second.FilesRepaired.Should().Be(1);
        second.RecoveryCopies.Should().HaveCount(2);
        second.RecoveryCopies.Intersect(first.RecoveryCopies).Should().BeEmpty();
        (await Task.WhenAll(first.RecoveryCopies.Select(path => File.ReadAllTextAsync(path)))).Should().Equal(firstContents,
            "a later repair must never replace an earlier backup or rejected-line recovery copy");
    }

    [Fact]
    public async Task RepairAsync_ChecksumAndTruncationIssues_ProcessFileOnceAndRetainOriginalSidecar()
    {
        var path = Path.Combine(TestDataRoot, "a.jsonl");
        await AtomicFileWriter.WriteWithChecksumAsync(path, Encoding.UTF8.GetBytes("{\"seq\":1}\n"));
        var originalSidecar = await File.ReadAllTextAsync(path + ".sha256");
        await File.AppendAllTextAsync(path, "bad-json\n");
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.TruncateCorrupted));

        result.FilesProcessed.Should().Be(1);
        result.FilesRepaired.Should().Be(1);
        result.Errors.Should().BeEmpty();
        result.RecoveryCopies.Should().HaveCount(3);
        var sidecarBackup = result.RecoveryCopies.Single(copy => copy.Contains(".sha256.pre-repair."));
        (await File.ReadAllTextAsync(sidecarBackup)).Should().Be(originalSidecar);
    }

    [Theory]
    [InlineData(RepairStrategy.RebuildIndex)]
    [InlineData(RepairStrategy.MergeFragments)]
    [InlineData(RepairStrategy.RestoreFromBackup)]
    [InlineData(RepairStrategy.BackfillFromSource)]
    public async Task RepairAsync_UnsupportedStrategies_ReportFailureWithoutMutation(RepairStrategy strategy)
    {
        var path = Path.Combine(TestDataRoot, "a.jsonl");
        const string original = "{\"seq\":1}\nbad-json\n";
        await File.WriteAllTextAsync(path, original);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.RepairAsync(new RepairOptions(strategy));

        result.FilesRepaired.Should().Be(0);
        result.FilesUnresolved.Should().Be(1);
        result.Errors.Should().ContainSingle(error => error.Contains("unsupported"));
        result.RecoveryCopies.Should().BeEmpty();
        (await File.ReadAllTextAsync(path)).Should().Be(original);
        Directory.GetFiles(TestDataRoot).Should().ContainSingle();
    }

    [Fact]
    public async Task RepairAsync_ValidJsonWithWrongChecksum_ReportsUnresolvedNoOp()
    {
        var path = Path.Combine(TestDataRoot, "a.jsonl");
        await AtomicFileWriter.WriteWithChecksumAsync(path, Encoding.UTF8.GetBytes("{\"seq\":1}\n"));
        await File.WriteAllTextAsync(path, "{\"seq\":2}\n");
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.TruncateCorrupted));

        result.FilesRepaired.Should().Be(0);
        result.FilesUnresolved.Should().Be(1);
        result.Errors.Should().ContainSingle(error => error.Contains("made no repair"));
        (await AtomicFileWriter.VerifyChecksumAsync(path)).Should().BeFalse();
    }

    [Fact]
    public async Task RepairAsync_SalvageLeavesEmptyFile_ReportsUnresolvedReadback()
    {
        var path = Path.Combine(TestDataRoot, "a.jsonl");
        await File.WriteAllTextAsync(path, "bad-json\n");
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot });

        var result = await service.RepairAsync(new RepairOptions(RepairStrategy.TruncateCorrupted));

        result.FilesRepaired.Should().Be(0);
        result.Errors.Should().ContainSingle(error => error.Contains(nameof(IssueType.EmptyFile)));
        result.RecoveryCopies.Should().HaveCount(2).And.OnlyContain(copy => File.Exists(copy));
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
        var jsonlA = Path.Combine(dir, "SPY_trade_20260101.jsonl");
        var jsonlB = Path.Combine(dir, "SPY_trade_20260101.merged-11111111111111111111111111111111.jsonl");
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

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot, NamingConvention = FileNamingConvention.Flat });
        var result = await service.DefragmentAsync(new DefragOptions(MaxFileAge: TimeSpan.FromDays(1)));

        result.FilesCreated.Should().Be(1);
        File.Exists(jsonlA).Should().BeFalse();
        File.Exists(jsonlB).Should().BeFalse();
        var merged = Directory.GetFiles(dir, "*.merged-*.jsonl").Should().ContainSingle().Subject;
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
        var jsonlA = Path.Combine(dir, "SPY_trade_20260101.jsonl");
        var jsonlB = Path.Combine(dir, "SPY_trade_20260101.merged-11111111111111111111111111111111.jsonl");
        await File.WriteAllTextAsync(jsonlA, "{\"seq\":1}\n");
        await File.WriteAllTextAsync(jsonlB, "{\"seq\":2}\n");
        foreach (var f in new[] { jsonlA, jsonlB })
            File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-2));

        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot, NamingConvention = FileNamingConvention.Flat });
        var result = await service.DefragmentAsync(new DefragOptions(MaxFileAge: TimeSpan.FromDays(1), DryRun: true));

        result.FilesProcessed.Should().Be(2, "the dry run must still report the would-merge plan");
        result.FilesCreated.Should().Be(0);
        File.Exists(jsonlA).Should().BeTrue();
        File.Exists(jsonlB).Should().BeTrue();
        Directory.GetFiles(dir, "*.jsonl").Should().BeEquivalentTo(new[] { jsonlA, jsonlB },
            "a dry run must not write any merged files");
    }

    [Fact]
    public async Task DefragmentAsync_WhenMergedSourceCannotBeDeleted_ReportsFailedGroupAndSurvivingBytes()
    {
        if (!OperatingSystem.IsWindows())
            return; // Windows file-share semantics provide the deterministic undeletable source used here.

        var dir = Path.Combine(TestDataRoot, "locked-source");
        Directory.CreateDirectory(dir);
        var lockedPath = Path.Combine(dir, "SPY_trade_20260101.jsonl");
        var deletablePath = Path.Combine(dir, "SPY_trade_20260101.merged-11111111111111111111111111111111.jsonl");
        await File.WriteAllTextAsync(lockedPath, "{\"seq\":1}\n");
        await File.WriteAllTextAsync(deletablePath, "{\"seq\":2}\n");
        File.SetLastWriteTimeUtc(lockedPath, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(deletablePath, DateTime.UtcNow.AddDays(-2));
        var lockedLength = new FileInfo(lockedPath).Length;
        await using var lockHandle = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var service = new FileMaintenanceService(new StorageOptions { RootPath = TestDataRoot, NamingConvention = FileNamingConvention.Flat });

        var result = await service.DefragmentAsync(new DefragOptions(MaxFileAge: TimeSpan.FromDays(1)));

        result.MergeGroupsAttempted.Should().Be(1);
        result.MergeGroupsSucceeded.Should().Be(0);
        result.Errors.Should().ContainSingle(error => error.Contains(lockedPath, StringComparison.Ordinal));
        File.Exists(lockedPath).Should().BeTrue();
        result.FilesDeleted.Should().Be(1);
        result.BytesAfter.Should().BeGreaterThan(lockedLength,
            "the actual footprint includes both the merged file and the source that survived deletion");
    }

    [Fact]
    public void DefragOptions_DefaultQuietPeriod_IsAtLeastOneDay()
    {
        new DefragOptions().MaxFileAge.Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(1));
        new DefragOptions(MinFileSizeBytes: 256).MaxFileAge.Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(1));
    }

    [Fact]
    public async Task DefragmentAsync_CurrentPartitionWithOldTimestamps_IsUntouched()
    {
        var day = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        var paths = await WriteFragmentsAsync($"SPY_trade_{day}");
        var result = await FlatMaintenance().DefragmentAsync(new DefragOptions(MaxFileAge: TimeSpan.FromTicks(1)));

        result.FilesProcessed.Should().Be(0, "an old mtime is not proof that today's event partition is closed");
        paths.Should().OnlyContain(path => File.Exists(path));
    }

    [Fact]
    public async Task DefragmentAsync_FlatLayout_KeepsSymbolTypeDateAndProviderPartitionsSeparate()
    {
        var partitions = new[]
        {
            "SPY_trade_20260101_provider_one", "AAPL_trade_20260101_provider_one",
            "SPY_quote_20260101_provider_one", "SPY_trade_20260102_provider_one",
            "SPY_trade_20260101_provider_two"
        };
        foreach (var partition in partitions)
            await WriteFragmentsAsync(partition);
        var options = new StorageOptions { RootPath = TestDataRoot, NamingConvention = FileNamingConvention.Flat, IncludeProvider = true };

        var result = await new FileMaintenanceService(options).DefragmentAsync(new DefragOptions());

        result.Errors.Should().BeEmpty();
        result.FilesCreated.Should().Be(partitions.Length);
        var policy = new JsonlStoragePolicy(options);
        var merged = Directory.GetFiles(TestDataRoot, "*.jsonl");
        merged.Should().HaveCount(partitions.Length);
        foreach (var partition in partitions)
        {
            var path = merged.Should().ContainSingle(path => Path.GetFileName(path).StartsWith(partition + ".merged-", StringComparison.Ordinal)).Subject;
            (await File.ReadAllLinesAsync(path)).Should().HaveCount(2);
            policy.TryParsePath(path).Should().Be(policy.TryParsePath(Path.Combine(TestDataRoot, partition + ".jsonl")));
        }
    }

    [Theory]
    [InlineData(DatePartition.Daily, "yyyy-MM-dd")]
    [InlineData(DatePartition.Hourly, "yyyy-MM-dd_HH")]
    [InlineData(DatePartition.Monthly, "yyyy-MM")]
    public async Task DefragmentAsync_CurrentPartitionDateCannotBeOverriddenByOldProviderOrPrefixDate(
        DatePartition partition, string dateFormat)
    {
        var prefix = "snapshot-20000101_";
        var date = DateTimeOffset.UtcNow.ToString(dateFormat, System.Globalization.CultureInfo.InvariantCulture);
        var paths = await WriteFragmentsAsync($"{prefix}SPY_trade_{date}_provider-2000-01-01");
        var options = new StorageOptions
        {
            RootPath = TestDataRoot, NamingConvention = FileNamingConvention.Flat,
            IncludeProvider = true, FilePrefix = prefix, DatePartition = partition
        };

        var result = await new FileMaintenanceService(options).DefragmentAsync(new DefragOptions());

        result.FilesProcessed.Should().Be(0, "only the owning date segment can establish that a partition is closed");
        paths.Should().OnlyContain(path => File.Exists(path));
    }

    [Fact]
    public async Task DefragmentAsync_OpenJsonlWriter_PreservesFsyncedAndSubsequentEvents()
    {
        var options = new StorageOptions { RootPath = TestDataRoot, NamingConvention = FileNamingConvention.Flat };
        var policy = new JsonlStoragePolicy(options);
        await using var sink = new JsonlStorageSink(options, policy,
            new JsonlBatchOptions { Enabled = false, FlushInterval = TimeSpan.FromMinutes(5) });
        var at = DateTimeOffset.Parse("2026-01-01T14:30:00Z");
        MarketEvent Event(int sequence) => MarketEvent.Trade(at, "SPY",
            new Trade(at, "SPY", 100m + sequence, 10, AggressorSide.Buy, sequence), "TEST", sequence);
        await sink.AppendAsync(Event(1));
        await sink.FlushAsync();
        var source = policy.GetPath(Event(1));
        var fragment = Path.Combine(Path.GetDirectoryName(source)!, Path.GetFileNameWithoutExtension(source) + ".merged-11111111111111111111111111111111.jsonl");
        await File.WriteAllTextAsync(fragment, "{\"sequence\":0}\n");
        foreach (var path in new[] { source, fragment })
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2));

        var result = await new FileMaintenanceService(options).DefragmentAsync(new DefragOptions());

        result.FilesDeleted.Should().Be(0, "the stream is still an active append destination even after fsync");
        result.FilesCreated.Should().Be(0);
        File.Exists(source).Should().BeTrue();
        await sink.AppendAsync(Event(2));
        await sink.FlushAsync();
        (await File.ReadAllLinesAsync(source)).Should().HaveCount(2);
    }

    [Fact]
    public async Task DefragmentAsync_CrossProcessWriterLease_RefusesMergeUntilReleased()
    {
        var paths = await WriteFragmentsAsync("SPY_trade_20260101");
        using (await JsonlWriterLock.AcquireSharedAsync(paths[0], TimeSpan.FromSeconds(5), CancellationToken.None))
        {
            var blocked = await FlatMaintenance().DefragmentAsync(new DefragOptions());
            blocked.FilesDeleted.Should().Be(0);
            paths.Should().OnlyContain(path => File.Exists(path));
        }

        var allowed = await FlatMaintenance().DefragmentAsync(new DefragOptions());
        allowed.Errors.Should().BeEmpty();
        allowed.FilesDeleted.Should().Be(2);
    }

    [Fact]
    public async Task FindOrphansAsync_AncestorTempNameAndQuarantinedEvidence_ArePreserved()
    {
        var dataRoot = Path.Combine(TestDataRoot, "_temp-volume", "data");
        var livePath = Path.Combine(dataRoot, "SPY_trade_20260101.jsonl");
        var quarantinedPath = Path.Combine(dataRoot, "_temp", "_quarantine", "evidence.jsonl");
        var temporaryPath = Path.Combine(dataRoot, "_temp", "abandoned.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(quarantinedPath)!);
        await File.WriteAllTextAsync(livePath, "{}\n");
        await File.WriteAllTextAsync(quarantinedPath, "{}\n");
        await File.WriteAllTextAsync(temporaryPath, "{}\n");

        var report = await new FileMaintenanceService(new StorageOptions { RootPath = dataRoot }).FindOrphansAsync();

        report.OrphanedFiles.Should().ContainSingle().Which.Path.Should().Be(temporaryPath);
        File.Exists(quarantinedPath).Should().BeTrue();
    }

    private FileMaintenanceService FlatMaintenance() => new(new StorageOptions
    {
        RootPath = TestDataRoot, NamingConvention = FileNamingConvention.Flat
    });

    private async Task<string[]> WriteFragmentsAsync(string partition)
    {
        var paths = new[]
        {
            Path.Combine(TestDataRoot, partition + ".jsonl"),
            Path.Combine(TestDataRoot, partition + ".merged-11111111111111111111111111111111.jsonl")
        };
        for (var i = 0; i < paths.Length; i++)
        {
            await File.WriteAllTextAsync(paths[i], $"{{\"sequence\":{i}}}\n");
            File.SetLastWriteTimeUtc(paths[i], DateTime.UtcNow.AddDays(-2));
        }
        return paths;
    }
}
