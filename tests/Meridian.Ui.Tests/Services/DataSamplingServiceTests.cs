using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="DataSamplingService"/> — validation logic, strategy definitions,
/// preset configurations, and data model behavior.
/// </summary>
public sealed class DataSamplingServiceTests
{
    private static DataSamplingService CreateService() => DataSamplingService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = DataSamplingService.Instance;
        var b = DataSamplingService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── ValidateOptions ──────────────────────────────────────────────

    [Fact]
    public void ValidateOptions_ValidOptions_ShouldReturnValid()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY", "AAPL" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SampleSize = 10000,
            OutputPath = "/tmp/output.parquet"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateOptions_NoSymbols_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = null,
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SampleSize = 10000,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("symbol"));
    }

    [Fact]
    public void ValidateOptions_EmptySymbols_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string>(),
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SampleSize = 10000,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateOptions_NoDates_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = null,
            ToDate = null,
            Strategy = SamplingStrategyType.Random,
            SampleSize = 10000,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Date range"));
    }

    [Fact]
    public void ValidateOptions_FromAfterTo_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 12, 31),
            ToDate = new DateOnly(2024, 1, 1),
            Strategy = SamplingStrategyType.Random,
            SampleSize = 10000,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("before"));
    }

    [Fact]
    public void ValidateOptions_RandomWithoutSizeOrPercent_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SampleSize = null,
            SamplePercent = null,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Sample size or percentage"));
    }

    [Fact]
    public void ValidateOptions_TimeBasedWithoutInterval_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.TimeBased,
            IntervalSeconds = null,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Interval"));
    }

    [Fact]
    public void ValidateOptions_TimeBasedWithZeroInterval_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.TimeBased,
            IntervalSeconds = 0,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateOptions_SystematicWithoutInterval_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Systematic,
            IntervalSeconds = null,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateOptions_InvalidSamplePercent_Low_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SamplePercent = 0.001,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("percentage"));
    }

    [Fact]
    public void ValidateOptions_InvalidSamplePercent_High_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SamplePercent = 150,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ValidateOptions_LargeSamplePercent_ShouldReturnWarning()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SamplePercent = 75,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.Warnings.Should().NotBeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("50%"));
    }

    [Fact]
    public void ValidateOptions_NoOutputPath_ShouldReturnError()
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = SamplingStrategyType.Random,
            SampleSize = 1000,
            OutputPath = null
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Output path"));
    }

    [Theory]
    [InlineData(SamplingStrategyType.SymbolStratified)]
    [InlineData(SamplingStrategyType.EventTypeStratified)]
    [InlineData(SamplingStrategyType.VolatilityBased)]
    [InlineData(SamplingStrategyType.FirstN)]
    [InlineData(SamplingStrategyType.LastN)]
    [InlineData(SamplingStrategyType.PeakHours)]
    public void ValidateOptions_SizeRequiringStrategies_WithoutSize_ShouldReturnError(
        SamplingStrategyType strategy)
    {
        var svc = CreateService();
        var options = new SamplingOptions
        {
            Symbols = new List<string> { "SPY" },
            FromDate = new DateOnly(2024, 1, 1),
            ToDate = new DateOnly(2024, 12, 31),
            Strategy = strategy,
            SampleSize = null,
            SamplePercent = null,
            OutputPath = "/tmp/output"
        };

        var result = svc.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
    }

    // ── GetSamplingStrategiesAsync ────────────────────────────────────

    [Fact]
    public async Task GetSamplingStrategiesAsync_ShouldReturnNineStrategies()
    {
        var svc = CreateService();

        var strategies = await svc.GetSamplingStrategiesAsync();

        strategies.Should().HaveCount(9);
    }

    [Fact]
    public async Task GetSamplingStrategiesAsync_ShouldContainAllStrategyTypes()
    {
        var svc = CreateService();

        var strategies = await svc.GetSamplingStrategiesAsync();
        var ids = strategies.Select(s => s.Id).ToList();

        ids.Should().Contain(SamplingStrategyType.Random);
        ids.Should().Contain(SamplingStrategyType.TimeBased);
        ids.Should().Contain(SamplingStrategyType.SymbolStratified);
        ids.Should().Contain(SamplingStrategyType.EventTypeStratified);
        ids.Should().Contain(SamplingStrategyType.VolatilityBased);
        ids.Should().Contain(SamplingStrategyType.FirstN);
        ids.Should().Contain(SamplingStrategyType.LastN);
        ids.Should().Contain(SamplingStrategyType.PeakHours);
        ids.Should().Contain(SamplingStrategyType.Systematic);
    }

    [Fact]
    public async Task GetSamplingStrategiesAsync_AllStrategies_ShouldHaveNameAndDescription()
    {
        var svc = CreateService();

        var strategies = await svc.GetSamplingStrategiesAsync();

        foreach (var strategy in strategies)
        {
            strategy.Name.Should().NotBeNullOrEmpty($"Strategy {strategy.Id} should have a name");
            strategy.Description.Should().NotBeNullOrEmpty($"Strategy {strategy.Id} should have a description");
            strategy.Icon.Should().NotBeNullOrEmpty($"Strategy {strategy.Id} should have an icon");
        }
    }

    // ── GetSamplingPresetsAsync ──────────────────────────────────────

    [Fact]
    public async Task GetSamplingPresetsAsync_ShouldReturnSixPresets()
    {
        var svc = CreateService();

        var presets = await svc.GetSamplingPresetsAsync();

        presets.Should().HaveCount(6);
    }

    [Fact]
    public async Task GetSamplingPresetsAsync_AllPresets_ShouldHaveNameAndDescription()
    {
        var svc = CreateService();

        var presets = await svc.GetSamplingPresetsAsync();

        foreach (var preset in presets)
        {
            preset.Name.Should().NotBeNullOrEmpty();
            preset.Description.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task GetSamplingPresetsAsync_ShouldContainReproducibleResearchPreset()
    {
        var svc = CreateService();

        var presets = await svc.GetSamplingPresetsAsync();

        var reproducible = presets.FirstOrDefault(p => p.Name.Contains("Reproducible"));
        reproducible.Should().NotBeNull();
        reproducible!.Seed.Should().Be(42);
    }

    // ── Model Defaults ───────────────────────────────────────────────

    [Fact]
    public void SamplingOptions_ShouldHaveDefaults()
    {
        var options = new SamplingOptions();
        options.Strategy.Should().Be(SamplingStrategyType.Random);
        options.OutputFormat.Should().Be(ExportFormat.Parquet);
        options.IncludeStatistics.Should().BeTrue();
        options.Symbols.Should().BeNull();
    }

    [Fact]
    public void SamplingResult_ShouldHaveDefaults()
    {
        var result = new SamplingResult();
        result.Success.Should().BeFalse();
        result.Error.Should().BeNull();
        result.TotalSourceRecords.Should().Be(0);
        result.SampledRecords.Should().Be(0);
    }

    [Fact]
    public void SamplingValidationResult_ShouldHaveDefaults()
    {
        var result = new SamplingValidationResult();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeNull().And.BeEmpty();
        result.Warnings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void SampleEstimate_ShouldHaveDefaults()
    {
        var estimate = new SampleEstimate();
        estimate.Success.Should().BeFalse();
        estimate.TotalSourceRecords.Should().Be(0);
        estimate.EstimatedSampleSize.Should().Be(0);
    }

    [Fact]
    public void SamplingStrategy_ShouldHaveDefaults()
    {
        var strategy = new SamplingStrategy();
        strategy.Name.Should().BeEmpty();
        strategy.Description.Should().BeEmpty();
        strategy.RequiresSize.Should().BeFalse();
        strategy.RequiresInterval.Should().BeFalse();
    }

    // ── SamplingStrategyType Enum ────────────────────────────────────

    [Theory]
    [InlineData(SamplingStrategyType.Random)]
    [InlineData(SamplingStrategyType.TimeBased)]
    [InlineData(SamplingStrategyType.SymbolStratified)]
    [InlineData(SamplingStrategyType.EventTypeStratified)]
    [InlineData(SamplingStrategyType.VolatilityBased)]
    [InlineData(SamplingStrategyType.FirstN)]
    [InlineData(SamplingStrategyType.LastN)]
    [InlineData(SamplingStrategyType.PeakHours)]
    [InlineData(SamplingStrategyType.Systematic)]
    public void SamplingStrategyType_AllValues_ShouldBeDefined(SamplingStrategyType type)
    {
        Enum.IsDefined(typeof(SamplingStrategyType), type).Should().BeTrue();
    }
}
