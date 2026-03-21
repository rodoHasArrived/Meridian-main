using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class LifecyclePolicyEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageOptions _options;
    private readonly Mock<ITierMigrationService> _tierMigrationMock;
    private readonly Mock<IFileMaintenanceService> _maintenanceMock;
    private readonly Mock<ILogger<LifecyclePolicyEngine>> _loggerMock;
    private readonly LifecyclePolicyEngine _engine;

    public LifecyclePolicyEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _options = new StorageOptions
        {
            RootPath = _tempDir,
            Policies = new Dictionary<string, StoragePolicyConfig>
            {
                ["Trade"] = new StoragePolicyConfig
                {
                    Classification = DataClassification.Standard,
                    HotTierDays = 7,
                    WarmTierDays = 30,
                    ColdTierDays = 90
                },
                ["HistoricalBar"] = new StoragePolicyConfig
                {
                    Classification = DataClassification.Critical,
                    HotTierDays = 7,
                    WarmTierDays = 90,
                    ColdTierDays = 365,
                    ArchiveTier = "perpetual"
                }
            }
        };

        _tierMigrationMock = new Mock<ITierMigrationService>();
        _maintenanceMock = new Mock<IFileMaintenanceService>();
        _loggerMock = new Mock<ILogger<LifecyclePolicyEngine>>();

        _engine = new LifecyclePolicyEngine(
            _options,
            _tierMigrationMock.Object,
            _maintenanceMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { /* Best effort cleanup */ }
    }

    [Fact]
    public void ResolvePolicy_ShouldReturnMatchingPolicy()
    {
        var policy = _engine.ResolvePolicy(Path.Combine(_tempDir, "AAPL", "Trade", "2024-01-15.jsonl"));

        policy.HotTierDays.Should().Be(7);
        policy.WarmTierDays.Should().Be(30);
    }

    [Fact]
    public void ResolvePolicy_ShouldReturnDefaultForUnknownPath()
    {
        var policy = _engine.ResolvePolicy(Path.Combine(_tempDir, "unknown", "data.jsonl"));

        policy.Classification.Should().Be(DataClassification.Standard);
        policy.HotTierDays.Should().Be(7);
    }

    [Fact]
    public async Task EvaluateAsync_ShouldIdentifyMigrationActions()
    {
        // Create a "hot" file that's old enough to migrate to warm
        var tradeDir = Path.Combine(_tempDir, "AAPL", "Trade");
        Directory.CreateDirectory(tradeDir);
        var oldFile = Path.Combine(tradeDir, "2024-01-01.jsonl");
        await File.WriteAllTextAsync(oldFile, "{\"test\":true}");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-15));

        var result = await _engine.EvaluateAsync();

        result.FilesEvaluated.Should().BeGreaterThan(0);
        result.Actions.Should().Contain(a => a.ActionType == LifecycleActionType.TierMigration);
    }

    [Fact]
    public async Task EvaluateAsync_ShouldNotDeleteCriticalData()
    {
        // Create a critical data file that's very old
        var barDir = Path.Combine(_tempDir, "SPY", "HistoricalBar");
        Directory.CreateDirectory(barDir);
        var oldFile = Path.Combine(barDir, "2020-01-01.jsonl");
        await File.WriteAllTextAsync(oldFile, "{\"test\":true}");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-1000));

        var result = await _engine.EvaluateAsync();

        // Critical data should never be marked for deletion
        result.Actions.Should().NotContain(a =>
            a.FilePath == oldFile && a.ActionType == LifecycleActionType.Delete);
    }

    [Fact]
    public async Task EvaluateAsync_ShouldReturnEmptyForNonExistentDirectory()
    {
        var engine = new LifecyclePolicyEngine(
            new StorageOptions { RootPath = "/nonexistent" },
            _tierMigrationMock.Object,
            _maintenanceMock.Object,
            _loggerMock.Object);

        var result = await engine.EvaluateAsync();

        result.FilesEvaluated.Should().Be(0);
        result.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_DryRun_ShouldNotModifyFiles()
    {
        var actions = new List<LifecycleAction>
        {
            new(
                FilePath: Path.Combine(_tempDir, "test.jsonl"),
                ActionType: LifecycleActionType.TierMigration,
                CurrentTier: StorageTier.Hot,
                TargetTier: StorageTier.Warm,
                Reason: "test",
                EstimatedSizeBytes: 1000)
        };

        var result = await _engine.ExecuteAsync(actions, dryRun: true);

        result.DryRun.Should().BeTrue();
        result.ActionsExecuted.Should().Be(1);
        _tierMigrationMock.Verify(
            m => m.MigrateAsync(It.IsAny<string>(), It.IsAny<StorageTier>(), It.IsAny<MigrationOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Delete_ShouldRemoveFile()
    {
        var filePath = Path.Combine(_tempDir, "to_delete.jsonl");
        await File.WriteAllTextAsync(filePath, "test data");

        var actions = new List<LifecycleAction>
        {
            new(
                FilePath: filePath,
                ActionType: LifecycleActionType.Delete,
                CurrentTier: StorageTier.Hot,
                TargetTier: null,
                Reason: "expired",
                EstimatedSizeBytes: 100)
        };

        var result = await _engine.ExecuteAsync(actions, dryRun: false);

        result.ActionsExecuted.Should().Be(1);
        result.BytesDeleted.Should().BeGreaterThan(0);
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void GetTierStatistics_ShouldReturnEmptyForNewEngine()
    {
        var stats = _engine.GetTierStatistics();

        stats.Should().BeEmpty();
    }
}
