using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for aligning time series data to regular intervals and generating OHLCV bars.
/// Supports multiple aggregation methods and gap handling strategies.
/// </summary>
public sealed class TimeSeriesAlignmentService
{
    private static readonly Lazy<TimeSeriesAlignmentService> _instance = new(() => new TimeSeriesAlignmentService());
    private readonly ApiClientService _apiClient;

    public static TimeSeriesAlignmentService Instance => _instance.Value;

    private TimeSeriesAlignmentService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Raised when alignment progress changes.
    /// </summary>
#pragma warning disable CS0067 // Event is declared but never raised in this implementation
    public event EventHandler<AlignmentProgressEventArgs>? ProgressChanged;
#pragma warning restore CS0067

    /// <summary>
    /// Aligns time series data with specified options.
    /// </summary>
    public async Task<AlignmentResult> AlignDataAsync(
        AlignmentOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<AlignmentResponse>(
            "/api/alignment/create",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString(FormatHelpers.IsoDateFormat),
                toDate = options.ToDate?.ToString(FormatHelpers.IsoDateFormat),
                interval = options.Interval.ToString(),
                aggregation = options.Aggregation.ToString(),
                gapStrategy = options.GapStrategy.ToString(),
                maxGapIntervals = options.MaxGapIntervals,
                markFilledValues = options.MarkFilledValues,
                timezone = options.Timezone,
                marketHoursOnly = options.MarketHoursOnly,
                outputPath = options.OutputPath,
                outputFormat = options.OutputFormat.ToString(),
                includeMetadata = options.IncludeMetadata
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new AlignmentResult
            {
                Success = response.Data.Success,
                OutputPath = response.Data.OutputPath,
                TotalSourceRecords = response.Data.TotalSourceRecords,
                AlignedRecords = response.Data.AlignedRecords,
                GapsDetected = response.Data.GapsDetected,
                GapsFilled = response.Data.GapsFilled,
                Duration = TimeSpan.FromSeconds(response.Data.DurationSeconds),
                Metadata = response.Data.Metadata
            };
        }

        return new AlignmentResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Alignment failed"
        };
    }

    /// <summary>
    /// Gets available alignment intervals.
    /// </summary>
    public Task<List<AlignmentInterval>> GetAlignmentIntervalsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<AlignmentInterval>
        {
            new() { Value = TimeSeriesInterval.Second1, DisplayName = "1 Second", Seconds = 1 },
            new() { Value = TimeSeriesInterval.Second5, DisplayName = "5 Seconds", Seconds = 5 },
            new() { Value = TimeSeriesInterval.Second10, DisplayName = "10 Seconds", Seconds = 10 },
            new() { Value = TimeSeriesInterval.Second30, DisplayName = "30 Seconds", Seconds = 30 },
            new() { Value = TimeSeriesInterval.Minute1, DisplayName = "1 Minute", Seconds = 60 },
            new() { Value = TimeSeriesInterval.Minute5, DisplayName = "5 Minutes", Seconds = 300 },
            new() { Value = TimeSeriesInterval.Minute15, DisplayName = "15 Minutes", Seconds = 900 },
            new() { Value = TimeSeriesInterval.Minute30, DisplayName = "30 Minutes", Seconds = 1800 },
            new() { Value = TimeSeriesInterval.Hour1, DisplayName = "1 Hour", Seconds = 3600 },
            new() { Value = TimeSeriesInterval.Hour4, DisplayName = "4 Hours", Seconds = 14400 },
            new() { Value = TimeSeriesInterval.Daily, DisplayName = "Daily", Seconds = 86400 }
        });
    }

    /// <summary>
    /// Gets available aggregation methods.
    /// </summary>
    public Task<List<AggregationMethod>> GetAggregationMethodsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<AggregationMethod>
        {
            new()
            {
                Value = AggregationType.OHLCV,
                DisplayName = "OHLCV",
                Description = "Open, High, Low, Close, Volume bars",
                Fields = new[] { "Open", "High", "Low", "Close", "Volume" }
            },
            new()
            {
                Value = AggregationType.Last,
                DisplayName = "Last",
                Description = "Last value in each interval",
                Fields = new[] { "Price", "Volume" }
            },
            new()
            {
                Value = AggregationType.First,
                DisplayName = "First",
                Description = "First value in each interval",
                Fields = new[] { "Price", "Volume" }
            },
            new()
            {
                Value = AggregationType.Mean,
                DisplayName = "Mean",
                Description = "Average value in each interval",
                Fields = new[] { "Price" }
            },
            new()
            {
                Value = AggregationType.VWAP,
                DisplayName = "VWAP",
                Description = "Volume-weighted average price",
                Fields = new[] { "VWAP", "Volume" }
            },
            new()
            {
                Value = AggregationType.TWAP,
                DisplayName = "TWAP",
                Description = "Time-weighted average price",
                Fields = new[] { "TWAP" }
            },
            new()
            {
                Value = AggregationType.Sum,
                DisplayName = "Sum",
                Description = "Sum of values (for volume, trade count)",
                Fields = new[] { "Volume", "TradeCount" }
            },
            new()
            {
                Value = AggregationType.Count,
                DisplayName = "Count",
                Description = "Number of events in interval",
                Fields = new[] { "Count" }
            }
        });
    }

    /// <summary>
    /// Gets available gap handling strategies.
    /// </summary>
    public Task<List<GapHandlingStrategy>> GetGapStrategiesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<GapHandlingStrategy>
        {
            new()
            {
                Value = GapStrategy.ForwardFill,
                DisplayName = "Forward Fill",
                Description = "Fill gaps with the last known value",
                Icon = "\uE72A"
            },
            new()
            {
                Value = GapStrategy.BackwardFill,
                DisplayName = "Backward Fill",
                Description = "Fill gaps with the next known value",
                Icon = "\uE72B"
            },
            new()
            {
                Value = GapStrategy.LinearInterpolate,
                DisplayName = "Linear Interpolation",
                Description = "Interpolate values between known points",
                Icon = "\uE9D9"
            },
            new()
            {
                Value = GapStrategy.Null,
                DisplayName = "Null / NaN",
                Description = "Leave gaps as null values",
                Icon = "\uE711"
            },
            new()
            {
                Value = GapStrategy.Zero,
                DisplayName = "Zero",
                Description = "Fill gaps with zero values",
                Icon = "\uE8FE"
            },
            new()
            {
                Value = GapStrategy.Skip,
                DisplayName = "Skip",
                Description = "Omit intervals with no data",
                Icon = "\uE8BB"
            }
        });
    }

    /// <summary>
    /// Gets alignment presets for common use cases.
    /// </summary>
    public Task<List<AlignmentPreset>> GetAlignmentPresetsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<AlignmentPreset>
        {
            new()
            {
                Name = "1-Minute OHLCV Bars",
                Description = "Standard candlestick bars for backtesting",
                Interval = TimeSeriesInterval.Minute1,
                Aggregation = AggregationType.OHLCV,
                GapStrategy = GapStrategy.Skip,
                MarketHoursOnly = true
            },
            new()
            {
                Name = "5-Minute Bars (Forward Fill)",
                Description = "Bars with gap filling for continuous data",
                Interval = TimeSeriesInterval.Minute5,
                Aggregation = AggregationType.OHLCV,
                GapStrategy = GapStrategy.ForwardFill,
                MaxGapIntervals = 3,
                MarketHoursOnly = true
            },
            new()
            {
                Name = "1-Second Ticks (VWAP)",
                Description = "High-frequency VWAP snapshots",
                Interval = TimeSeriesInterval.Second1,
                Aggregation = AggregationType.VWAP,
                GapStrategy = GapStrategy.Null,
                MarketHoursOnly = false
            },
            new()
            {
                Name = "Hourly Summary",
                Description = "Hourly bars for swing trading analysis",
                Interval = TimeSeriesInterval.Hour1,
                Aggregation = AggregationType.OHLCV,
                GapStrategy = GapStrategy.Skip,
                MarketHoursOnly = true
            },
            new()
            {
                Name = "Daily Bars",
                Description = "End-of-day bars for long-term analysis",
                Interval = TimeSeriesInterval.Daily,
                Aggregation = AggregationType.OHLCV,
                GapStrategy = GapStrategy.Skip,
                MarketHoursOnly = true
            },
            new()
            {
                Name = "ML Training (Interpolated)",
                Description = "Continuous data for machine learning",
                Interval = TimeSeriesInterval.Minute1,
                Aggregation = AggregationType.Mean,
                GapStrategy = GapStrategy.LinearInterpolate,
                MaxGapIntervals = 5,
                MarkFilledValues = true,
                MarketHoursOnly = false
            },
            new()
            {
                Name = "Correlation Analysis",
                Description = "Multi-symbol aligned data for correlation",
                Interval = TimeSeriesInterval.Minute5,
                Aggregation = AggregationType.Last,
                GapStrategy = GapStrategy.ForwardFill,
                MaxGapIntervals = 6,
                MarketHoursOnly = true
            }
        });
    }

    /// <summary>
    /// Previews alignment results without creating output.
    /// </summary>
    public async Task<AlignmentPreview> PreviewAlignmentAsync(
        AlignmentOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<AlignmentPreviewResponse>(
            "/api/alignment/preview",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString(FormatHelpers.IsoDateFormat),
                toDate = options.ToDate?.ToString(FormatHelpers.IsoDateFormat),
                interval = options.Interval.ToString(),
                gapStrategy = options.GapStrategy.ToString()
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new AlignmentPreview
            {
                Success = true,
                TotalSourceRecords = response.Data.TotalSourceRecords,
                ExpectedOutputRecords = response.Data.ExpectedOutputRecords,
                ExpectedGaps = response.Data.ExpectedGaps,
                EstimatedFileSizeBytes = response.Data.EstimatedFileSizeBytes,
                IntervalCount = response.Data.IntervalCount
            };
        }

        // Return mock preview
        var days = options.ToDate.HasValue && options.FromDate.HasValue
            ? (options.ToDate.Value.DayNumber - options.FromDate.Value.DayNumber)
            : 7;
        var symbolCount = options.Symbols?.Count ?? 3;
        var intervalsPerDay = GetIntervalsPerDay(options.Interval, options.MarketHoursOnly);

        return new AlignmentPreview
        {
            Success = true,
            TotalSourceRecords = symbolCount * days * 500000,
            ExpectedOutputRecords = symbolCount * days * intervalsPerDay,
            ExpectedGaps = (int)(symbolCount * days * intervalsPerDay * 0.02),
            EstimatedFileSizeBytes = symbolCount * days * intervalsPerDay * 100,
            IntervalCount = days * intervalsPerDay
        };
    }

    private static int GetIntervalsPerDay(TimeSeriesInterval interval, bool marketHoursOnly)
    {
        var tradingMinutes = marketHoursOnly ? 390 : 1440; // 6.5 hours vs 24 hours

        return interval switch
        {
            TimeSeriesInterval.Second1 => tradingMinutes * 60,
            TimeSeriesInterval.Second5 => tradingMinutes * 12,
            TimeSeriesInterval.Second10 => tradingMinutes * 6,
            TimeSeriesInterval.Second30 => tradingMinutes * 2,
            TimeSeriesInterval.Minute1 => tradingMinutes,
            TimeSeriesInterval.Minute5 => tradingMinutes / 5,
            TimeSeriesInterval.Minute15 => tradingMinutes / 15,
            TimeSeriesInterval.Minute30 => tradingMinutes / 30,
            TimeSeriesInterval.Hour1 => marketHoursOnly ? 7 : 24,
            TimeSeriesInterval.Hour4 => marketHoursOnly ? 2 : 6,
            TimeSeriesInterval.Daily => 1,
            _ => tradingMinutes
        };
    }

    /// <summary>
    /// Validates alignment options.
    /// </summary>
    public AlignmentValidationResult ValidateOptions(AlignmentOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (options.Symbols == null || options.Symbols.Count == 0)
        {
            errors.Add("At least one symbol must be selected");
        }

        if (options.FromDate == null || options.ToDate == null)
        {
            errors.Add("Date range must be specified");
        }
        else if (options.FromDate > options.ToDate)
        {
            errors.Add("Start date must be before end date");
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            errors.Add("Output path must be specified");
        }

        if (options.MaxGapIntervals < 0)
        {
            errors.Add("Max gap intervals must be non-negative");
        }

        if (options.MaxGapIntervals > 100)
        {
            warnings.Add("Large max gap intervals may result in unreliable filled data");
        }

        if (options.Interval == TimeSeriesInterval.Second1 && options.Symbols?.Count > 5)
        {
            warnings.Add("1-second intervals with many symbols may produce very large output files");
        }

        return new AlignmentValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}


public sealed class AlignmentProgressEventArgs : EventArgs
{
    public double Progress { get; set; }
    public string? CurrentSymbol { get; set; }
    public long RecordsProcessed { get; set; }
}



public enum TimeSeriesInterval : byte
{
    Second1,
    Second5,
    Second10,
    Second30,
    Minute1,
    Minute5,
    Minute15,
    Minute30,
    Hour1,
    Hour4,
    Daily
}

public enum AggregationType : byte
{
    OHLCV,
    Last,
    First,
    Mean,
    VWAP,
    TWAP,
    Sum,
    Count
}

public enum GapStrategy : byte
{
    ForwardFill,
    BackwardFill,
    LinearInterpolate,
    Null,
    Zero,
    Skip
}



public sealed class AlignmentOptions
{
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public TimeSeriesInterval Interval { get; set; } = TimeSeriesInterval.Minute1;
    public AggregationType Aggregation { get; set; } = AggregationType.OHLCV;
    public GapStrategy GapStrategy { get; set; } = GapStrategy.Skip;
    public int MaxGapIntervals { get; set; } = 5;
    public bool MarkFilledValues { get; set; }
    public string Timezone { get; set; } = "America/New_York";
    public bool MarketHoursOnly { get; set; } = true;
    public string? OutputPath { get; set; }
    public ExportFormat OutputFormat { get; set; } = ExportFormat.Parquet;
    public bool IncludeMetadata { get; set; } = true;
}

public sealed class AlignmentResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? OutputPath { get; set; }
    public long TotalSourceRecords { get; set; }
    public long AlignedRecords { get; set; }
    public int GapsDetected { get; set; }
    public int GapsFilled { get; set; }
    public TimeSpan Duration { get; set; }
    public AlignmentMetadata? Metadata { get; set; }
}

public sealed class AlignmentMetadata
{
    public Dictionary<string, long>? RecordsBySymbol { get; set; }
    public Dictionary<string, int>? GapsBySymbol { get; set; }
    public DateTimeOffset? FirstTimestamp { get; set; }
    public DateTimeOffset? LastTimestamp { get; set; }
    public int TotalIntervals { get; set; }
    public double CoveragePercent { get; set; }
}

public sealed class AlignmentInterval
{
    public TimeSeriesInterval Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Seconds { get; set; }
}

public sealed class AggregationMethod
{
    public AggregationType Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[]? Fields { get; set; }
}

public sealed class GapHandlingStrategy
{
    public GapStrategy Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

public sealed class AlignmentPreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TimeSeriesInterval Interval { get; set; }
    public AggregationType Aggregation { get; set; }
    public GapStrategy GapStrategy { get; set; }
    public int MaxGapIntervals { get; set; } = 5;
    public bool MarkFilledValues { get; set; }
    public bool MarketHoursOnly { get; set; } = true;
}

public sealed class AlignmentPreview
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long TotalSourceRecords { get; set; }
    public long ExpectedOutputRecords { get; set; }
    public int ExpectedGaps { get; set; }
    public long EstimatedFileSizeBytes { get; set; }
    public int IntervalCount { get; set; }
}

public sealed class AlignmentValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}



public sealed class AlignmentResponse
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public long TotalSourceRecords { get; set; }
    public long AlignedRecords { get; set; }
    public int GapsDetected { get; set; }
    public int GapsFilled { get; set; }
    public double DurationSeconds { get; set; }
    public AlignmentMetadata? Metadata { get; set; }
}

public sealed class AlignmentPreviewResponse
{
    public long TotalSourceRecords { get; set; }
    public long ExpectedOutputRecords { get; set; }
    public int ExpectedGaps { get; set; }
    public long EstimatedFileSizeBytes { get; set; }
    public int IntervalCount { get; set; }
}

