using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Archival;
using Meridian.Storage.Maintenance;
using Meridian.Storage.Services;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage.Maintenance;

public sealed partial class ScheduledArchiveMaintenanceServiceTests
{
    [Fact]
    public async Task ExecuteMaintenanceAsync_Compression_VerifiesLosslessScopedChangeWithoutClaimingRepair()
    {
        var scopedDirectory = Path.Combine(_storageRoot, "selected");
        Directory.CreateDirectory(scopedDirectory);
        var sourcePath = Path.Combine(scopedDirectory, "a.jsonl");
        var sourceBytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{\"symbol\":\"SPY\",\"label\":\"café\",\"price\":450.25}\r\n", 60)));
        await AtomicFileWriter.WriteWithChecksumAsync(sourcePath, sourceBytes);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow - ScheduledArchiveMaintenanceService.ScheduledCompressionQuietPeriod - TimeSpan.FromMinutes(1));
        var checksumBytes = await File.ReadAllBytesAsync(sourcePath + ".sha256");
        var outsidePath = WriteQuietFile("outside/b.jsonl", "{\"price\":10}\n");
        var sut = CreateSut(fileMaintenance: RealFileMaintenance());

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Compression,
            new MaintenanceTaskOptions { BackupPath = Path.Combine(_tempRoot, "backups") },
            targetPaths: [scopedDirectory]);

        execution.Status.Should().Be(MaintenanceExecutionStatus.Completed);
        execution.Result!.Success.Should().BeTrue();
        execution.Result.Summary.Should().Contain("compressed 1/1");
        execution.Result.FilesFailed.Should().Be(0);
        execution.Result.IssuesFound.Should().Be(0);
        execution.Result.IssuesResolved.Should().Be(0, "an encoding change is not a corruption repair");
        execution.Result.TotalBytesScanned.Should().Be(sourceBytes.Length);
        execution.Result.BytesSaved.Should().BeGreaterThan(0);
        File.Exists(sourcePath).Should().BeFalse();
        File.Exists(sourcePath + ".sha256").Should().BeFalse();
        (await DecompressBytesAsync(sourcePath + ".gz")).Should().Equal(sourceBytes);
        File.Exists(outsidePath).Should().BeTrue();
        File.Exists(outsidePath + ".gz").Should().BeFalse();
        var recoveryPaths = RecoveryPaths(execution);
        recoveryPaths.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        var backupBytes = await Task.WhenAll(recoveryPaths.Select(path => File.ReadAllBytesAsync(path)));
        backupBytes.Should().Contain(bytes => bytes.SequenceEqual(sourceBytes));
        backupBytes.Should().Contain(bytes => bytes.SequenceEqual(checksumBytes));
        var retained = _scheduleManager.ExecutionHistory.GetExecution(execution.ExecutionId)!;
        retained.Status.Should().Be(MaintenanceExecutionStatus.Completed);
        RecoveryPaths(retained).Should().Equal(recoveryPaths);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CompressionBackupFailure_PreventsEverySourceMutation()
    {
        var paths = new[] { WriteQuietFile("a.jsonl", "{\"seq\":1}\n"), WriteQuietFile("b.jsonl", "{\"seq\":2}\n") };
        var original = await Task.WhenAll(paths.Select(path => File.ReadAllBytesAsync(path)));
        var blockedBackupPath = Path.Combine(_tempRoot, "backup-destination");
        await File.WriteAllTextAsync(blockedBackupPath, "a regular file cannot hold a backup directory");
        var sut = CreateSut(fileMaintenance: RealFileMaintenance());

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Compression,
            new MaintenanceTaskOptions { BackupPath = blockedBackupPath });

        execution.Result!.Success.Should().BeFalse();
        execution.Status.Should().Be(MaintenanceExecutionStatus.CompletedWithWarnings);
        execution.Result.FilesFailed.Should().Be(2);
        execution.Result.IssuesResolved.Should().Be(0);
        execution.LogMessages.Should().Contain(message => message.Contains("Backup failed"));
        for (var index = 0; index < paths.Length; index++)
        {
            (await File.ReadAllBytesAsync(paths[index])).Should().Equal(original[index]);
            File.Exists(paths[index] + ".gz").Should().BeFalse();
        }
        RecoveryPaths(execution).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CompressionDestinationCollision_PreservesBothArchivesAndReportsFailure()
    {
        var path = WriteQuietFile("a.jsonl", "{\"seq\":1}\n");
        byte[] retainedCompressed = [0x1f, 0x8b, 0x08, 0x00, 0x01];
        await File.WriteAllBytesAsync(path + ".gz", retainedCompressed);
        var sut = CreateSut(fileMaintenance: RealFileMaintenance());

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Compression);

        execution.Result!.Success.Should().BeFalse();
        execution.Result.FilesFailed.Should().Be(1);
        execution.Result.IssuesResolved.Should().Be(0);
        execution.LogMessages.Should().Contain(message => message.Contains("Refusing to replace existing compression destination"));
        (await File.ReadAllTextAsync(path)).Should().Be("{\"seq\":1}\n");
        (await File.ReadAllBytesAsync(path + ".gz")).Should().Equal(retainedCompressed);
        RecoveryPaths(execution).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteMaintenanceAsync_CompressionUnhealthyEvidence_RemainsUnresolvedAndUnchanged(bool wrongChecksum)
    {
        var path = WriteQuietFile("a.jsonl", wrongChecksum ? "{\"seq\":1}\n" : "{\"seq\":1}\nbad-json\n");
        if (wrongChecksum)
            await File.WriteAllTextAsync(path + ".sha256", $"{new string('0', 64)}  a.jsonl");
        var before = await File.ReadAllBytesAsync(path);
        var sut = CreateSut(fileMaintenance: RealFileMaintenance());

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Compression);

        execution.Result!.Success.Should().BeFalse();
        execution.Result.FilesFailed.Should().Be(1);
        execution.Result.IssuesFound.Should().Be(1);
        execution.Result.IssuesResolved.Should().Be(0);
        execution.LogMessages.Should().Contain(message => message.Contains("Compression refused for unhealthy file"));
        (await File.ReadAllBytesAsync(path)).Should().Equal(before);
        File.Exists(path + ".gz").Should().BeFalse();
        RecoveryPaths(execution).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CompressionDryRun_PreservesSourceWithoutWritingRecoveryOrGzipCopies()
    {
        var path = WriteQuietFile("a.jsonl", "{\"seq\":1}\n");
        var sut = CreateSut(fileMaintenance: RealFileMaintenance());

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Compression,
            new MaintenanceTaskOptions { DryRun = true });

        execution.Result!.Success.Should().BeTrue();
        execution.Result.Summary.Should().Contain("planned 1 files; no files changed");
        execution.Result.IssuesResolved.Should().Be(0);
        execution.Result.FilesFailed.Should().Be(0);
        execution.LogMessages.Should().Contain($"[DRY RUN] gzip: {path}");
        Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories).Should().Equal(path);
    }

    [Fact]
    public async Task ExecuteMaintenanceAsync_CompressionIncompleteResult_CannotReportSuccessWithoutErrors()
    {
        _fileMaintenance.Setup(service => service.CompressAsync(It.IsAny<FileCompressionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileCompressionResult(1, 0, [], false));
        var sut = CreateSut();

        var execution = await sut.ExecuteMaintenanceAsync(MaintenanceTaskType.Compression);

        execution.Result!.Success.Should().BeFalse();
        execution.Result.FilesFailed.Should().Be(1);
        execution.Result.IssuesResolved.Should().Be(0);
        _fileMaintenance.Verify(service => service.RepairAsync(It.IsAny<RepairOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private FileMaintenanceService RealFileMaintenance() => new(new StorageOptions { RootPath = _storageRoot });

    private static string[] RecoveryPaths(MaintenanceExecution execution) => execution.LogMessages
        .Where(message => message.StartsWith("Recovery copy: ", StringComparison.Ordinal))
        .Select(message => message["Recovery copy: ".Length..]).ToArray();

    private static async Task<byte[]> DecompressBytesAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        await using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await gzip.CopyToAsync(output);
        return output.ToArray();
    }

    // Scheduled compression defers recently written files; age sources past the quiet period.
    private string WriteQuietFile(string relativePath, string content)
    {
        var path = WriteFile(relativePath, content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - ScheduledArchiveMaintenanceService.ScheduledCompressionQuietPeriod - TimeSpan.FromMinutes(1));
        return path;
    }
}
