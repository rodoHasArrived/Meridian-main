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
}
