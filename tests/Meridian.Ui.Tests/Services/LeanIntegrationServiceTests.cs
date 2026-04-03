using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public class LeanIntegrationServiceTests
{
    [Fact]
    public void LeanStatus_DefaultValues_AreNotConfigured()
    {
        var status = new LeanStatus();

        status.IsInstalled.Should().BeFalse();
        status.IsConfigured.Should().BeFalse();
        status.DataSyncEnabled.Should().BeFalse();
        status.Version.Should().BeNull();
        status.LastSync.Should().BeNull();
        status.SymbolsSynced.Should().Be(0);
    }

    [Fact]
    public void LeanConfiguration_DefaultValues_AreEmpty()
    {
        var config = new LeanConfiguration();

        config.LeanPath.Should().BeNull();
        config.DataPath.Should().BeNull();
        config.ResultsPath.Should().BeNull();
        config.AutoSync.Should().BeFalse();
    }

    [Fact]
    public void LeanConfigurationUpdate_CanSetAllProperties()
    {
        var update = new LeanConfigurationUpdate
        {
            LeanPath = "/opt/lean",
            DataPath = "/data/lean",
            ResultsPath = "/results",
            AutoSync = true,
            DefaultResolution = "Minute"
        };

        update.LeanPath.Should().Be("/opt/lean");
        update.DataPath.Should().Be("/data/lean");
        update.ResultsPath.Should().Be("/results");
        update.AutoSync.Should().BeTrue();
        update.DefaultResolution.Should().Be("Minute");
    }

    [Fact]
    public void BacktestOptions_DefaultCapital_Is100000()
    {
        var options = new BacktestOptions();

        options.InitialCapital.Should().Be(100000m);
        options.AlgorithmPath.Should().BeNull();
        options.AlgorithmName.Should().BeNull();
        options.StartDate.Should().BeNull();
        options.EndDate.Should().BeNull();
    }

    [Fact]
    public void BacktestOptions_CanSetAllProperties()
    {
        var options = new BacktestOptions
        {
            AlgorithmPath = "/algorithms/test.py",
            AlgorithmName = "TestAlgorithm",
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 12, 31),
            InitialCapital = 50000m,
            Parameters = new Dictionary<string, string> { { "fast_period", "10" } }
        };

        options.AlgorithmPath.Should().Be("/algorithms/test.py");
        options.AlgorithmName.Should().Be("TestAlgorithm");
        options.StartDate.Should().Be(new DateOnly(2024, 1, 1));
        options.EndDate.Should().Be(new DateOnly(2024, 12, 31));
        options.InitialCapital.Should().Be(50000m);
        options.Parameters.Should().ContainKey("fast_period");
    }

    [Fact]
    public void BacktestState_HasExpectedValues()
    {
        Enum.GetValues<BacktestState>().Should().Contain(BacktestState.Unknown);
        Enum.GetValues<BacktestState>().Should().Contain(BacktestState.Running);
        Enum.GetValues<BacktestState>().Should().Contain(BacktestState.Completed);
        Enum.GetValues<BacktestState>().Should().Contain(BacktestState.Failed);
        Enum.GetValues<BacktestState>().Should().Contain(BacktestState.Cancelled);
    }

    [Fact]
    public void DataSyncOptions_DefaultResolution_IsDaily()
    {
        var options = new DataSyncOptions();

        options.Resolution.Should().Be("Daily");
        options.Overwrite.Should().BeFalse();
        options.Symbols.Should().BeNull();
    }

    [Fact]
    public void DataSyncOptions_CanSetSymbolsAndDates()
    {
        var options = new DataSyncOptions
        {
            Symbols = new List<string> { "AAPL", "MSFT" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 6, 30),
            Resolution = "Minute",
            Overwrite = true
        };

        options.Symbols.Should().HaveCount(2);
        options.FromDate.Should().Be(new DateOnly(2024, 1, 1));
        options.Resolution.Should().Be("Minute");
        options.Overwrite.Should().BeTrue();
    }

    [Fact]
    public void LeanBacktestResultsSummary_DefaultValues_AreZero()
    {
        var results = new LeanBacktestResultsSummaryDto();

        results.TotalReturn.Should().Be(0);
        results.SharpeRatio.Should().Be(0);
        results.MaxDrawdown.Should().Be(0);
        results.TotalTrades.Should().Be(0);
        results.WinRate.Should().Be(0);
    }

    [Fact]
    public void LeanResultsArtifactSummary_DefaultCollections_AreEmpty()
    {
        var artifact = new LeanResultsArtifactSummaryDto();

        artifact.Statistics.Should().BeEmpty();
        artifact.Parameters.Should().BeEmpty();
        artifact.Artifacts.Should().BeEmpty();
        artifact.Sections.Should().NotBeNull();
        artifact.SourceFormat.Should().Be("lean-backtest-json");
    }

    [Fact]
    public void BacktestSummary_CanTrackCompletionState()
    {
        var summary = new BacktestSummary
        {
            BacktestId = "bt-001",
            AlgorithmName = "MovingAverage",
            StartedAt = DateTime.UtcNow.AddHours(-1),
            CompletedAt = DateTime.UtcNow,
            State = BacktestState.Completed,
            TotalReturn = 0.15m,
            SharpeRatio = 1.8m
        };

        summary.BacktestId.Should().Be("bt-001");
        summary.State.Should().Be(BacktestState.Completed);
        summary.TotalReturn.Should().Be(0.15m);
        summary.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void AlgorithmInfo_CanStoreAlgorithmMetadata()
    {
        var info = new AlgorithmInfo
        {
            Name = "TestAlgo",
            Path = "/algorithms/test.py",
            Language = "Python",
            LastModified = DateTime.UtcNow,
            IsValid = true
        };

        info.Name.Should().Be("TestAlgo");
        info.Path.Should().Be("/algorithms/test.py");
        info.Language.Should().Be("Python");
        info.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LeanIntegrationService_Instance_ReturnsSameInstance()
    {
        var instance1 = LeanIntegrationService.Instance;
        var instance2 = LeanIntegrationService.Instance;

        instance1.Should().BeSameAs(instance2);
    }
}
