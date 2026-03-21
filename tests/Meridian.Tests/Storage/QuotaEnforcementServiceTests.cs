using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class QuotaEnforcementServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ILogger<QuotaEnforcementService>> _loggerMock;

    public QuotaEnforcementServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _loggerMock = new Mock<ILogger<QuotaEnforcementService>>();
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best effort cleanup */ }
    }

    [Fact]
    public void CheckQuota_ShouldAllowWhenNoQuotasConfigured()
    {
        var options = new StorageOptions { RootPath = _tempDir };
        var service = new QuotaEnforcementService(options, _loggerMock.Object);

        var result = service.CheckQuota("AAPL", "alpaca", "Trade", 1000);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CheckQuota_ShouldWarnOnGlobalQuotaExceeded()
    {
        var options = new StorageOptions
        {
            RootPath = _tempDir,
            Quotas = new QuotaOptions
            {
                Global = new StorageQuota
                {
                    MaxBytes = 1000,
                    Enforcement = QuotaEnforcementPolicy.Warn
                }
            }
        };

        var service = new QuotaEnforcementService(options, _loggerMock.Object);
        service.RecordUsage(Path.Combine(_tempDir, "test.jsonl"), 900);

        var result = service.CheckQuota("AAPL", "alpaca", "Trade", 200);

        result.IsAllowed.Should().BeTrue();
        result.Warning.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CheckQuota_ShouldBlockOnHardLimit()
    {
        var options = new StorageOptions
        {
            RootPath = _tempDir,
            Quotas = new QuotaOptions
            {
                Global = new StorageQuota
                {
                    MaxBytes = 1000,
                    Enforcement = QuotaEnforcementPolicy.HardLimit
                }
            }
        };

        var service = new QuotaEnforcementService(options, _loggerMock.Object);
        service.RecordUsage(Path.Combine(_tempDir, "test.jsonl"), 950);

        var result = service.CheckQuota("AAPL", "alpaca", "Trade", 200);

        result.IsAllowed.Should().BeFalse();
        result.Warning.Should().Contain("Hard limit");
    }

    [Fact]
    public void CheckQuota_ShouldEnforcePerSourceQuota()
    {
        var options = new StorageOptions
        {
            RootPath = _tempDir,
            NamingConvention = FileNamingConvention.BySource,
            Quotas = new QuotaOptions
            {
                PerSource = new Dictionary<string, StorageQuota>
                {
                    ["alpaca"] = new StorageQuota
                    {
                        MaxBytes = 500,
                        Enforcement = QuotaEnforcementPolicy.HardLimit
                    }
                }
            }
        };

        var service = new QuotaEnforcementService(options, _loggerMock.Object);

        // Record usage for alpaca source
        var alpacaFile = Path.Combine(_tempDir, "alpaca", "AAPL", "Trade", "2024-01-15.jsonl");
        service.RecordUsage(alpacaFile, 400);

        // This should fail because source quota will be exceeded
        var result = service.CheckQuota("AAPL", "alpaca", "Trade", 200);
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void CheckQuota_ShouldEnforcePerSymbolQuota()
    {
        var options = new StorageOptions
        {
            RootPath = _tempDir,
            Quotas = new QuotaOptions
            {
                PerSymbol = new Dictionary<string, StorageQuota>
                {
                    ["AAPL"] = new StorageQuota
                    {
                        MaxBytes = 1000,
                        Enforcement = QuotaEnforcementPolicy.SoftLimit
                    }
                }
            }
        };

        var service = new QuotaEnforcementService(options, _loggerMock.Object);
        service.RecordUsage(Path.Combine(_tempDir, "AAPL", "trade.jsonl"), 900);

        var result = service.CheckQuota("AAPL", "alpaca", "Trade", 200);

        result.IsAllowed.Should().BeTrue();
        result.RequiresCleanup.Should().BeTrue();
    }

    [Fact]
    public void GetStatus_ShouldReportAllConfiguredQuotas()
    {
        var options = new StorageOptions
        {
            RootPath = _tempDir,
            Quotas = new QuotaOptions
            {
                Global = new StorageQuota { MaxBytes = 10_000 },
                PerSource = new Dictionary<string, StorageQuota>
                {
                    ["alpaca"] = new StorageQuota { MaxBytes = 5_000 }
                },
                PerSymbol = new Dictionary<string, StorageQuota>
                {
                    ["AAPL"] = new StorageQuota { MaxBytes = 2_000 }
                }
            }
        };

        var service = new QuotaEnforcementService(options, _loggerMock.Object);
        var status = service.GetStatus();

        status.Entries.Should().HaveCount(3);
        status.Entries.Should().Contain(e => e.QuotaId == "global");
        status.Entries.Should().Contain(e => e.QuotaId == "source:alpaca");
        status.Entries.Should().Contain(e => e.QuotaId == "symbol:AAPL");
    }

    [Fact]
    public async Task ScanAndUpdateAsync_ShouldCountFiles()
    {
        // Create test files
        var tradeDir = Path.Combine(_tempDir, "AAPL", "Trade");
        Directory.CreateDirectory(tradeDir);
        await File.WriteAllTextAsync(Path.Combine(tradeDir, "2024-01-15.jsonl"), "test data line 1");
        await File.WriteAllTextAsync(Path.Combine(tradeDir, "2024-01-16.jsonl"), "test data line 2");

        var options = new StorageOptions
        {
            RootPath = _tempDir,
            Quotas = new QuotaOptions
            {
                Global = new StorageQuota { MaxBytes = 10_000_000 }
            }
        };

        var service = new QuotaEnforcementService(options, _loggerMock.Object);
        var result = await service.ScanAndUpdateAsync();

        result.FilesScanned.Should().Be(2);
        result.TotalBytes.Should().BeGreaterThan(0);
    }
}
