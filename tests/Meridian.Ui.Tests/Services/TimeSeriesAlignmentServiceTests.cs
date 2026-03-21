using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public class TimeSeriesAlignmentServiceTests
{
    private readonly TimeSeriesAlignmentService _service = TimeSeriesAlignmentService.Instance;

    [Fact]
    public void ValidateOptions_WithNoSymbols_ReturnsError()
    {
        var options = new AlignmentOptions
        {
            Symbols = null,
            FromDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7)),
            ToDate = DateOnly.FromDateTime(DateTime.Now),
            OutputPath = "/tmp/test.parquet"
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("symbol"));
    }

    [Fact]
    public void ValidateOptions_WithEmptySymbolList_ReturnsError()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string>(),
            FromDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7)),
            ToDate = DateOnly.FromDateTime(DateTime.Now),
            OutputPath = "/tmp/test.parquet"
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("symbol"));
    }

    [Fact]
    public void ValidateOptions_WithNoDates_ReturnsError()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string> { "AAPL", "MSFT" },
            FromDate = null,
            ToDate = null,
            OutputPath = "/tmp/test.parquet"
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("date") || e.Contains("Date"));
    }

    [Fact]
    public void ValidateOptions_WithReversedDates_ReturnsError()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string> { "AAPL", "MSFT" },
            FromDate = DateOnly.FromDateTime(DateTime.Now),
            ToDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7)),
            OutputPath = "/tmp/test.parquet"
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("before"));
    }

    [Fact]
    public void ValidateOptions_WithNoOutputPath_ReturnsError()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string> { "AAPL", "MSFT" },
            FromDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7)),
            ToDate = DateOnly.FromDateTime(DateTime.Now),
            OutputPath = null
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Output path") || e.Contains("output") || e.Contains("path"));
    }

    [Fact]
    public void ValidateOptions_WithValidOptions_ReturnsValid()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string> { "AAPL", "MSFT", "GOOGL" },
            FromDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-30)),
            ToDate = DateOnly.FromDateTime(DateTime.Now),
            OutputPath = "/tmp/alignment_output.parquet",
            Interval = TimeSeriesInterval.Minute5,
            GapStrategy = GapStrategy.ForwardFill
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateOptions_WithNegativeMaxGapIntervals_ReturnsError()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string> { "AAPL", "MSFT" },
            FromDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7)),
            ToDate = DateOnly.FromDateTime(DateTime.Now),
            OutputPath = "/tmp/test.parquet",
            MaxGapIntervals = -1
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("gap"));
    }

    [Fact]
    public void ValidateOptions_WithLargeMaxGapIntervals_ReturnsWarning()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string> { "AAPL", "MSFT" },
            FromDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7)),
            ToDate = DateOnly.FromDateTime(DateTime.Now),
            OutputPath = "/tmp/test.parquet",
            MaxGapIntervals = 200
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void ValidateOptions_WithHighFrequencyAndManySymbols_ReturnsWarning()
    {
        var options = new AlignmentOptions
        {
            Symbols = new List<string> { "A", "B", "C", "D", "E", "F" },
            FromDate = DateOnly.FromDateTime(DateTime.Now.AddDays(-7)),
            ToDate = DateOnly.FromDateTime(DateTime.Now),
            OutputPath = "/tmp/test.parquet",
            Interval = TimeSeriesInterval.Second1
        };

        var result = _service.ValidateOptions(options);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public async Task GetAlignmentIntervalsAsync_ReturnsIntervals()
    {
        var intervals = await _service.GetAlignmentIntervalsAsync();

        intervals.Should().HaveCountGreaterThanOrEqualTo(5);
        intervals.Should().Contain(i => i.Value == TimeSeriesInterval.Minute1);
        intervals.Should().Contain(i => i.Value == TimeSeriesInterval.Minute5);
        intervals.Should().Contain(i => i.Value == TimeSeriesInterval.Daily);

        foreach (var interval in intervals)
        {
            interval.DisplayName.Should().NotBeNullOrEmpty();
            interval.Seconds.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task GetAggregationMethodsAsync_ReturnsMethods()
    {
        var methods = await _service.GetAggregationMethodsAsync();

        methods.Should().HaveCountGreaterThanOrEqualTo(4);
        methods.Should().Contain(m => m.Value == AggregationType.OHLCV);
        methods.Should().Contain(m => m.Value == AggregationType.VWAP);

        foreach (var method in methods)
        {
            method.DisplayName.Should().NotBeNullOrEmpty();
            method.Description.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task GetGapStrategiesAsync_ReturnsStrategies()
    {
        var strategies = await _service.GetGapStrategiesAsync();

        strategies.Should().HaveCountGreaterThanOrEqualTo(4);
        strategies.Should().Contain(s => s.Value == GapStrategy.ForwardFill);
        strategies.Should().Contain(s => s.Value == GapStrategy.Skip);
        strategies.Should().Contain(s => s.Value == GapStrategy.Zero);

        foreach (var strategy in strategies)
        {
            strategy.DisplayName.Should().NotBeNullOrEmpty();
            strategy.Description.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task GetAlignmentPresetsAsync_ReturnsPresets()
    {
        var presets = await _service.GetAlignmentPresetsAsync();

        presets.Should().HaveCountGreaterThanOrEqualTo(3);

        foreach (var preset in presets)
        {
            preset.Name.Should().NotBeNullOrEmpty();
            preset.Description.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void AlignmentOptions_HasCorrectDefaults()
    {
        var options = new AlignmentOptions();

        options.Interval.Should().Be(TimeSeriesInterval.Minute1);
        options.Aggregation.Should().Be(AggregationType.OHLCV);
        options.GapStrategy.Should().Be(GapStrategy.Skip);
        options.MaxGapIntervals.Should().Be(5);
        options.MarkFilledValues.Should().BeFalse();
        options.Timezone.Should().Be("America/New_York");
        options.MarketHoursOnly.Should().BeTrue();
        options.OutputFormat.Should().Be(ExportFormat.Parquet);
        options.IncludeMetadata.Should().BeTrue();
    }
}
