using FluentAssertions;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Tests for PreflightChecker covering disk space, file permissions,
/// system time, environment variable checks, and failure modes.
/// </summary>
public sealed class PreflightCheckerTests : IDisposable
{
    private readonly string _tempDir;

    public PreflightCheckerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"preflight_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            { Directory.Delete(_tempDir, true); }
            catch { }
        }
    }

    #region RunChecksAsync - Full Run

    [Fact]
    public async Task RunChecksAsync_ValidDirectory_ReturnsResult()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2,
            NetworkTimeoutMs = 1000
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        result.Checks.Should().NotBeEmpty();
        result.TotalDurationMs.Should().BeGreaterThan(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RunChecksAsync_ValidDirectory_PassesDiskSpaceCheck()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var diskCheck = result.Checks.FirstOrDefault(c => c.Name == "Disk Space");
        diskCheck.Status.Should().Be(PreflightCheckStatus.Passed);
    }

    [Fact]
    public async Task RunChecksAsync_ValidDirectory_PassesFilePermissionsCheck()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var permCheck = result.Checks.FirstOrDefault(c => c.Name == "File Permissions");
        permCheck.Status.Should().Be(PreflightCheckStatus.Passed);
        permCheck.Message.Should().Contain("readable and writable");
    }

    [Fact]
    public async Task RunChecksAsync_SystemTimeCheck_Passes()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var timeCheck = result.Checks.FirstOrDefault(c => c.Name == "System Time");
        timeCheck.Status.Should().Be(PreflightCheckStatus.Passed);
    }

    [Fact]
    public async Task RunChecksAsync_MemoryCheck_PassesOrWarns()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var memCheck = result.Checks.FirstOrDefault(c => c.Name == "Memory Availability");
        memCheck.Status.Should().NotBe(PreflightCheckStatus.Failed);
    }

    [Fact]
    public async Task RunChecksAsync_EnvironmentVariablesCheck_DoesNotFail()
    {
        // Arrange - PATH is always set
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var envCheck = result.Checks.FirstOrDefault(c => c.Name == "Environment Variables");
        envCheck.Status.Should().NotBe(PreflightCheckStatus.Failed);
    }

    #endregion

    #region RunChecksAsync - Provider Connectivity Toggle

    [Fact]
    public async Task RunChecksAsync_ProviderConnectivityDisabled_SkipsProviderCheck()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        result.Checks.Should().NotContain(c => c.Name == "Provider Endpoints");
    }

    [Fact]
    public async Task RunChecksAsync_ProviderConnectivityEnabled_IncludesProviderCheck()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckProviderConnectivity = true,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2,
            NetworkTimeoutMs = 1000
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        result.Checks.Should().Contain(c => c.Name == "Provider Endpoints");
    }

    #endregion

    #region Disk Space Thresholds

    [Fact]
    public async Task RunChecksAsync_UnreasonableDiskSpaceThreshold_FailsDiskCheck()
    {
        // Arrange - require 999999 GB free, which no disk has
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 999999,
            WarnDiskSpaceGb = 999999,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var diskCheck = result.Checks.FirstOrDefault(c => c.Name == "Disk Space");
        diskCheck.Status.Should().Be(PreflightCheckStatus.Failed);
        diskCheck.Remediation.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RunChecksAsync_LowDiskSpaceWarningThreshold_WarnsOnDisk()
    {
        // Arrange - set warning threshold very high to trigger warning
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 999999,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var diskCheck = result.Checks.FirstOrDefault(c => c.Name == "Disk Space");
        diskCheck.Status.Should().Be(PreflightCheckStatus.Warning);
    }

    #endregion

    #region Memory Thresholds

    [Fact]
    public async Task RunChecksAsync_UnreasonableMemoryThreshold_FailsOrWarns()
    {
        // Arrange - require 999999 MB, which will fail
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 999999,
            WarnMemoryMb = 999999
        });

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert
        var memCheck = result.Checks.FirstOrDefault(c => c.Name == "Memory Availability");
        memCheck.Status.Should().BeOneOf(PreflightCheckStatus.Failed, PreflightCheckStatus.Warning);
    }

    #endregion

    #region File Permissions

    [Fact]
    public async Task RunChecksAsync_NonExistentDirectory_CreatesIt()
    {
        // Arrange
        var newDir = Path.Combine(_tempDir, "new_subdir");
        Directory.Exists(newDir).Should().BeFalse();

        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var result = await checker.RunChecksAsync(newDir, CancellationToken.None);

        // Assert
        Directory.Exists(newDir).Should().BeTrue();
        var permCheck = result.Checks.FirstOrDefault(c => c.Name == "File Permissions");
        permCheck.Status.Should().Be(PreflightCheckStatus.Passed);
    }

    #endregion

    #region EnsureReadyAsync

    [Fact]
    public async Task EnsureReadyAsync_AllChecksPassed_DoesNotThrow()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 0.001,
            WarnDiskSpaceGb = 0.01,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var act = () => checker.EnsureReadyAsync(_tempDir, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureReadyAsync_FailedCheck_ThrowsPreflightException()
    {
        // Arrange - set impossible disk space requirement
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 999999,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act
        var act = () => checker.EnsureReadyAsync(_tempDir, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<PreflightException>();
        exception.Which.Result.AllChecksPassed.Should().BeFalse();
        exception.Which.Message.Should().Contain("Pre-flight checks failed");
    }

    [Fact]
    public async Task EnsureReadyAsync_FailedCheck_ExceptionContainsResult()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckNetworkConnectivity = false,
            CheckProviderConnectivity = false,
            MinDiskSpaceGb = 999999,
            MinMemoryMb = 1,
            WarnMemoryMb = 2
        });

        // Act & Assert
        try
        {
            await checker.EnsureReadyAsync(_tempDir, CancellationToken.None);
            Assert.Fail("Expected PreflightException");
        }
        catch (PreflightException ex)
        {
            ex.Result.Checks.Should().NotBeEmpty();
            ex.Result.Checks.Should().Contain(c => c.Status == PreflightCheckStatus.Failed);
        }
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task RunChecksAsync_CancellationRequested_ReturnsPartialResults()
    {
        // Arrange
        var checker = new PreflightChecker(new PreflightConfig
        {
            CheckProviderConnectivity = true,
            NetworkTimeoutMs = 30000
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await checker.RunChecksAsync(_tempDir, cts.Token);

        // Assert - should return some checks even when cancelled
        result.Checks.Should().NotBeNull();
    }

    #endregion

    #region PreflightConfig Defaults

    [Fact]
    public void PreflightConfig_Default_HasExpectedValues()
    {
        // Act
        var config = PreflightConfig.Default;

        // Assert
        config.MinDiskSpaceGb.Should().Be(1.0);
        config.WarnDiskSpaceGb.Should().Be(5.0);
        config.MinMemoryMb.Should().Be(256);
        config.WarnMemoryMb.Should().Be(512);
        config.NetworkTimeoutMs.Should().Be(5000);
        config.CheckProviderConnectivity.Should().BeTrue();
    }

    #endregion

    #region PreflightResult

    [Fact]
    public void PreflightResult_GetSummary_ContainsExpectedInfo()
    {
        // Arrange
        var result = new PreflightResult(
            AllChecksPassed: true,
            HasWarnings: false,
            Checks: new[]
            {
                PreflightCheckResult.Passed("Test1", "OK"),
                PreflightCheckResult.Warning("Test2", "Warn"),
                PreflightCheckResult.Failed("Test3", "Fail")
            },
            TotalDurationMs: 42.5,
            CheckedAt: DateTimeOffset.UtcNow);

        // Act
        var summary = result.GetSummary();

        // Assert
        summary.Should().Contain("1 passed");
        summary.Should().Contain("1 warnings");
        summary.Should().Contain("1 failed");
        summary.Should().Contain("42ms");
    }

    #endregion

    #region PreflightCheckResult Static Factories

    [Fact]
    public void PreflightCheckResult_Passed_SetsCorrectStatus()
    {
        var result = PreflightCheckResult.Passed("Test", "All good");
        result.Status.Should().Be(PreflightCheckStatus.Passed);
        result.Name.Should().Be("Test");
        result.Message.Should().Be("All good");
        result.Remediation.Should().BeNull();
    }

    [Fact]
    public void PreflightCheckResult_Warning_SetsCorrectStatus()
    {
        var result = PreflightCheckResult.Warning("Test", "Something off", "Fix it");
        result.Status.Should().Be(PreflightCheckStatus.Warning);
        result.Remediation.Should().Be("Fix it");
    }

    [Fact]
    public void PreflightCheckResult_Failed_SetsCorrectStatus()
    {
        var details = new Dictionary<string, object> { ["key"] = "value" };
        var result = PreflightCheckResult.Failed("Test", "Broken", "Repair", details);
        result.Status.Should().Be(PreflightCheckStatus.Failed);
        result.Remediation.Should().Be("Repair");
        result.Details.Should().ContainKey("key");
    }

    #endregion

    #region PreflightException

    [Fact]
    public void PreflightException_ContainsMessageAndResult()
    {
        // Arrange
        var preflightResult = new PreflightResult(
            AllChecksPassed: false,
            HasWarnings: false,
            Checks: new[] { PreflightCheckResult.Failed("Disk", "No space") },
            TotalDurationMs: 10,
            CheckedAt: DateTimeOffset.UtcNow);

        // Act
        var exception = new PreflightException("Checks failed", preflightResult);

        // Assert
        exception.Message.Should().Be("Checks failed");
        exception.Result.AllChecksPassed.Should().BeFalse();
        exception.Result.Checks.Should().HaveCount(1);
    }

    #endregion

    #region Default Config Constructor

    [Fact]
    public async Task PreflightChecker_DefaultConfig_RunsAllChecks()
    {
        // Arrange - use default config (will try provider connectivity)
        var checker = new PreflightChecker();

        // Act
        var result = await checker.RunChecksAsync(_tempDir, CancellationToken.None);

        // Assert - should have at least the core checks
        result.Checks.Count.Should().BeGreaterThanOrEqualTo(6);
        result.Checks.Should().Contain(c => c.Name == "Disk Space");
        result.Checks.Should().Contain(c => c.Name == "File Permissions");
        result.Checks.Should().Contain(c => c.Name == "Network Connectivity");
        result.Checks.Should().Contain(c => c.Name == "Memory Availability");
        result.Checks.Should().Contain(c => c.Name == "System Time");
        result.Checks.Should().Contain(c => c.Name == "Environment Variables");
    }

    #endregion

    #region ValidateProviderCredentials

    [Fact]
    public void ValidateProviderCredentials_UnknownProvider_ReturnsPassed()
    {
        var checker = new PreflightChecker();
        var result = checker.ValidateProviderCredentials("UnknownProvider");
        result.Status.Should().Be(PreflightCheckStatus.Passed);
    }

    [Fact]
    public void ValidateProviderCredentials_IB_PassesWithoutCredentials()
    {
        // Interactive Brokers uses a local connection — no API credentials required.
        var checker = new PreflightChecker();
        var result = checker.ValidateProviderCredentials("IB");
        result.Status.Should().Be(PreflightCheckStatus.Passed);
    }

    [Fact]
    public void ValidateProviderCredentials_Synthetic_PassesWithoutCredentials()
    {
        // Synthetic provider is fully self-contained — no API credentials required.
        var checker = new PreflightChecker();
        var result = checker.ValidateProviderCredentials("Synthetic");
        result.Status.Should().Be(PreflightCheckStatus.Passed);
        result.Message.Should().Contain("Synthetic");
        result.Message.Should().Contain("does not require API credentials");
    }

    [Fact]
    public void ValidateProviderCredentials_Alpaca_FailsWhenCredentialsMissing()
    {
        // Ensure env vars are not set
        Environment.SetEnvironmentVariable("ALPACA__KEYID", null);
        Environment.SetEnvironmentVariable("ALPACA_KEY_ID", null);
        Environment.SetEnvironmentVariable("MDC_ALPACA_KEY_ID", null);
        Environment.SetEnvironmentVariable("ALPACA__SECRETKEY", null);
        Environment.SetEnvironmentVariable("ALPACA_SECRET_KEY", null);
        Environment.SetEnvironmentVariable("MDC_ALPACA_SECRET_KEY", null);

        var checker = new PreflightChecker();
        var result = checker.ValidateProviderCredentials("Alpaca");

        result.Status.Should().Be(PreflightCheckStatus.Failed);
        result.Message.Should().Contain("Alpaca");
    }

    [Fact]
    public void ValidateProviderCredentials_Alpaca_PassesWhenPrimaryCredentialsSet()
    {
        Environment.SetEnvironmentVariable("ALPACA__KEYID", "test-key");
        Environment.SetEnvironmentVariable("ALPACA__SECRETKEY", "test-secret");
        try
        {
            var checker = new PreflightChecker();
            var result = checker.ValidateProviderCredentials("Alpaca");
            result.Status.Should().Be(PreflightCheckStatus.Passed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ALPACA__KEYID", null);
            Environment.SetEnvironmentVariable("ALPACA__SECRETKEY", null);
        }
    }

    [Fact]
    public void ValidateProviderCredentials_Alpaca_PassesWhenAlternativeCredentialSet()
    {
        Environment.SetEnvironmentVariable("ALPACA__KEYID", null);
        Environment.SetEnvironmentVariable("MDC_ALPACA_KEY_ID", "alt-key");
        Environment.SetEnvironmentVariable("ALPACA__SECRETKEY", null);
        Environment.SetEnvironmentVariable("MDC_ALPACA_SECRET_KEY", "alt-secret");
        try
        {
            var checker = new PreflightChecker();
            var result = checker.ValidateProviderCredentials("Alpaca");
            result.Status.Should().Be(PreflightCheckStatus.Passed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MDC_ALPACA_KEY_ID", null);
            Environment.SetEnvironmentVariable("MDC_ALPACA_SECRET_KEY", null);
        }
    }

    [Fact]
    public void ValidateProviderCredentials_Polygon_FailsWhenMissing()
    {
        Environment.SetEnvironmentVariable("POLYGON__APIKEY", null);
        Environment.SetEnvironmentVariable("POLYGON_API_KEY", null);
        Environment.SetEnvironmentVariable("MDC_POLYGON_API_KEY", null);

        var checker = new PreflightChecker();
        var result = checker.ValidateProviderCredentials("Polygon");

        result.Status.Should().Be(PreflightCheckStatus.Failed);
        result.Message.Should().Contain("Polygon");
    }

    [Fact]
    public void ValidateProviderCredentials_Polygon_PassesWhenSet()
    {
        Environment.SetEnvironmentVariable("POLYGON__APIKEY", "test-key");
        try
        {
            var checker = new PreflightChecker();
            var result = checker.ValidateProviderCredentials("Polygon");
            result.Status.Should().Be(PreflightCheckStatus.Passed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POLYGON__APIKEY", null);
        }
    }

    [Fact]
    public void ValidateProviderCredentials_CaseInsensitive_ReturnsResult()
    {
        // Should handle case-insensitive provider name lookup
        var checker = new PreflightChecker();
        var act = () => checker.ValidateProviderCredentials("alpaca");
        act.Should().NotThrow();
    }

    #endregion
}
