using FluentAssertions;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class ExecutionAuditTrailServiceTests
{
    [Fact]
    public async Task Constructor_DefersWalInitialisationUntilFirstOperation()
    {
        var tempRoot = CreateTempRoot();
        var options = new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit"));

        await using var auditTrail = new ExecutionAuditTrailService(
            options,
            NullLogger<ExecutionAuditTrailService>.Instance);

        Directory.GetFiles(options.WalDirectory, "*.wal").Should().BeEmpty();

        _ = await auditTrail.GetAllAsync();

        Directory.GetFiles(options.WalDirectory, "*.wal").Should().NotBeEmpty();
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
