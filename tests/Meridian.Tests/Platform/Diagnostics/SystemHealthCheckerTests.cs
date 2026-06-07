using FluentAssertions;
using Meridian.Platform.Diagnostics;
using Xunit;

namespace Meridian.Tests.Platform.Diagnostics;

public sealed class SystemHealthCheckerTests
{
    [Fact]
    public void GetMemoryInfo_ShouldReportCurrentProcessMemory()
    {
        using var checker = new SystemHealthChecker(new SystemHealthConfig
        {
            CheckIntervalSeconds = 3600,
            WarningMemoryMb = double.MaxValue,
            CriticalMemoryMb = double.MaxValue
        });

        var memory = checker.GetMemoryInfo();

        memory.WorkingSetMb.Should().BeGreaterThan(0);
        memory.PrivateMemoryMb.Should().BeGreaterThan(0);
        memory.Gen0Collections.Should().BeGreaterThanOrEqualTo(0);
        memory.IsWarning.Should().BeFalse();
        memory.IsCritical.Should().BeFalse();
    }

    [Fact]
    public void GetDiskSpaceInfo_ShouldIncludeRegisteredExistingPath()
    {
        using var checker = new SystemHealthChecker(new SystemHealthConfig
        {
            CheckIntervalSeconds = 3600,
            WarningDiskSpaceGb = 0,
            CriticalDiskSpaceGb = 0
        });
        var path = Path.GetTempPath();

        checker.AddMonitoredPath(path);

        var diskInfo = checker.GetDiskSpaceInfo();

        diskInfo.Should().Contain(disk => string.Equals(disk.Path, path, StringComparison.Ordinal));
        diskInfo.Should().OnlyContain(disk => disk.TotalSpaceGb > 0);
    }

    [Fact]
    public void GetCurrentWarnings_ShouldClassifyCriticalMemoryThresholds()
    {
        using var checker = new SystemHealthChecker(new SystemHealthConfig
        {
            CheckIntervalSeconds = 3600,
            WarningMemoryMb = 0,
            CriticalMemoryMb = 0
        });

        var warnings = checker.GetCurrentWarnings();

        warnings.Should().Contain(warning =>
            warning.Category == HealthWarningCategory.Memory &&
            warning.Severity == HealthWarningSeverity.Critical);
        checker.GetOverallStatus().Should().Be(SystemHealthStatus.Critical);
    }
}
