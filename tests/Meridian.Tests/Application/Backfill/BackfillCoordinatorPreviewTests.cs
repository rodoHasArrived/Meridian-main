using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.YahooFinance;
using Meridian.Ui.Shared.Services;
using Xunit;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

public sealed class BackfillCoordinatorPreviewTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _configPath;
    private readonly string _dataRoot;

    public BackfillCoordinatorPreviewTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_backfill_preview_{Guid.NewGuid():N}");
        _configPath = Path.Combine(_testRoot, "appsettings.json");
        _dataRoot = Path.Combine(_testRoot, "data");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Fact]
    public async Task PreviewAsync_IntradayRequest_IgnoresDailyFilesWhenCheckingExistingData()
    {
        await using var fixture = await CreateCoordinatorAsync();
        var symbolDir = Path.Combine(_dataRoot, "historical", "SPY");
        Directory.CreateDirectory(symbolDir);
        await File.WriteAllTextAsync(Path.Combine(symbolDir, "bar_daily_2024-01-02.jsonl"), "{}");

        var preview = await fixture.Coordinator.PreviewAsync(new BackfillRequest(
            "yahoo",
            ["SPY"],
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 2),
            DataGranularity.Minute1));

        var symbol = preview.Symbols.Should().ContainSingle().Subject;
        symbol.ExistingData.HasData.Should().BeFalse();
        symbol.ExistingData.FileCount.Should().Be(0);
        symbol.WouldOverwrite.Should().BeFalse();
    }

    [Fact]
    public async Task PreviewAsync_DailyRequest_IgnoresIntradayFilesWhenCheckingExistingData()
    {
        await using var fixture = await CreateCoordinatorAsync();
        var symbolDir = Path.Combine(_dataRoot, "historical", "SPY");
        Directory.CreateDirectory(symbolDir);
        await File.WriteAllTextAsync(Path.Combine(symbolDir, "bar_1min_2024-01-02.jsonl"), "{}");

        var preview = await fixture.Coordinator.PreviewAsync(new BackfillRequest(
            "yahoo",
            ["SPY"],
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 2),
            DataGranularity.Daily));

        var symbol = preview.Symbols.Should().ContainSingle().Subject;
        symbol.ExistingData.HasData.Should().BeFalse();
        symbol.ExistingData.FileCount.Should().Be(0);
        symbol.WouldOverwrite.Should().BeFalse();
    }

    private async Task<PreviewFixture> CreateCoordinatorAsync()
    {
        var store = new ConfigStore(_configPath);
        await store.SaveAsync(new AppConfig(DataRoot: _dataRoot));

        var registry = new ProviderRegistry();
        registry.Register(new YahooFinanceHistoricalDataProvider());

        return new PreviewFixture(new BackfillCoordinator(store, registry));
    }

    private sealed class PreviewFixture : IAsyncDisposable
    {
        public PreviewFixture(BackfillCoordinator coordinator)
        {
            Coordinator = coordinator;
        }

        public BackfillCoordinator Coordinator { get; }

        public ValueTask DisposeAsync()
        {
            Coordinator.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
