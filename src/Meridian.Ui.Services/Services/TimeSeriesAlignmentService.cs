using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Meridian.Contracts.Api;
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
    /// <summary>
    /// The host answers 501 when it has no alignment execution service (see
    /// <c>HistoricalEndpoints.MapAlignmentEndpoints</c>); that is a capability gap, not a run failure.
    /// </summary>
    private const int NotImplementedStatusCode = 501;

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
        var elapsed = Stopwatch.StartNew();
        var response = await _apiClient.PostWithResponseAsync<TimeSeriesAlignmentExecution>(
            UiApiRoutes.AlignmentCreate, BuildRequest(options), ct).ConfigureAwait(false);

        if (!response.Success || response.Data is not { } execution)
            return new AlignmentResult
            {
                IsServiceUnavailable = response.StatusCode == NotImplementedStatusCode,
                Error = ReadProblem(response.ErrorMessage, "Alignment failed.")
            };

        var output = options.OutputPath;
        if (string.IsNullOrWhiteSpace(output))
            return new AlignmentResult { Error = "Choose an output path." };

        await TimeSeriesAlignmentExporter.WriteAsync(execution, options.OutputFormat, output, ct).ConfigureAwait(false);
        return new AlignmentResult
        {
            Success = true,
            OutputPath = output,
            TotalSourceRecords = execution.Summary.TotalSourceRecords,
            AlignedRecords = execution.Summary.OutputRecords,
            GapsDetected = execution.Summary.GapsDetected,
            GapsFilled = execution.Summary.GapsFilled,
            Duration = elapsed.Elapsed,
            Summary = execution.Summary,
            Metadata = new AlignmentMetadata
            {
                RecordsBySymbol = execution.Rows.GroupBy(row => row.Symbol)
                    .ToDictionary(group => group.Key, group => group.LongCount()),
                GapsBySymbol = execution.Summary.Series.GroupBy(series => series.Symbol)
                    .ToDictionary(group => group.Key, group => group.Sum(series => series.Gaps)),
                FirstTimestamp = execution.Rows.FirstOrDefault()?.Timestamp,
                LastTimestamp = execution.Rows.LastOrDefault()?.Timestamp,
                TotalIntervals = execution.Summary.IntervalCount,
                CoveragePercent = 100d * execution.Summary.Series.Sum(series => series.ObservedIntervals) /
                    (execution.Summary.Series.Length * execution.Summary.IntervalCount)
            }
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
                Name = "1-Second Bars",
                Description = "High-frequency OHLCV for a short archive range",
                Interval = TimeSeriesInterval.Second1,
                Aggregation = AggregationType.OHLCV,
                GapStrategy = GapStrategy.Null,
                MarketHoursOnly = true
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
                Description = "Intraday OHLCV with bounded interpolation",
                Interval = TimeSeriesInterval.Minute1,
                Aggregation = AggregationType.OHLCV,
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
                Aggregation = AggregationType.OHLCV,
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
        var response = await _apiClient.PostWithResponseAsync<TimeSeriesAlignmentPreview>(
            UiApiRoutes.AlignmentPreview, BuildRequest(options), ct).ConfigureAwait(false);

        if (!response.Success || response.Data is not { } preview)
            return new AlignmentPreview
            {
                IsServiceUnavailable = response.StatusCode == NotImplementedStatusCode,
                Error = ReadProblem(response.ErrorMessage, "Alignment preview unavailable.")
            };

        return new AlignmentPreview
        {
            Success = true,
            TotalSourceRecords = preview.Summary.TotalSourceRecords,
            ExpectedOutputRecords = preview.Summary.OutputRecords,
            ExpectedGaps = preview.Summary.GapsDetected,
            IntervalCount = preview.Summary.IntervalCount,
            Summary = preview.Summary,
            SampleRows = preview.SampleRows
        };
    }

    internal static TimeSeriesAlignmentRequest BuildRequest(AlignmentOptions options) => new(
        options.Symbols?.ToArray(), options.FromDate, options.ToDate,
        Providers: options.Providers?.ToArray(),
        Interval: options.Interval.ToString(),
        GapStrategy: options.GapStrategy.ToString(),
        MaxGapIntervals: options.MaxGapIntervals,
        Timezone: options.Timezone,
        MarketHoursOnly: options.MarketHoursOnly,
        InputKind: options.InputKind,
        AdjustmentPolicy: options.AdjustmentPolicy,
        Fields: options.Fields,
        Aggregation: options.Aggregation.ToString(),
        ExpectedContentHash: options.ExpectedContentHash);

    private static string ReadProblem(string? response, string fallback)
    {
        if (string.IsNullOrWhiteSpace(response))
            return fallback;
        try
        {
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in new[] { "detail", "title" })
                {
                    if (document.RootElement.TryGetProperty(field, out var property) &&
                        property.ValueKind == JsonValueKind.String && property.GetString() is { Length: > 0 } message)
                        return message;
                }
            }
        }
        catch (JsonException)
        {
            return response;
        }
        return fallback;
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

        if (options.Providers is { Count: 1 or > 8 })
            errors.Add("Select two to eight provider IDs, or leave the list empty to discover them");

        if (options.Aggregation != AggregationType.OHLCV)
            errors.Add("Alignment supports OHLCV aggregation");

        if (options.MaxGapIntervals is < 0 or > 100)
            errors.Add("Maximum gap intervals must be between 0 and 100");

        if (options.Interval != TimeSeriesInterval.Daily && options.InputKind == "DailyBars")
            errors.Add("Daily bars cannot be split into intraday intervals");

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


/// <summary>
/// Supplies time-series alignment progress, current symbol, and processed record count.
/// </summary>
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



/// <summary>
/// Configures time-series scope, interval, aggregation, gap handling, calendar settings, and output artifact.
/// </summary>
public sealed class AlignmentOptions
{
    public List<string>? Symbols { get; set; }
    public List<string>? Providers { get; set; }
    public string InputKind { get; set; } = "Auto";
    public string AdjustmentPolicy { get; set; } = "AsStored";
    public string[]? Fields { get; set; }
    public string? ExpectedContentHash { get; set; }
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

/// <summary>
/// Reports alignment success, output location, record and gap counts, duration, and metadata.
/// </summary>
public sealed class AlignmentResult
{
    public bool Success { get; set; }

    /// <summary>
    /// True when the host reported that alignment is not implemented (HTTP 501), so retrying
    /// with different options cannot succeed.
    /// </summary>
    public bool IsServiceUnavailable { get; set; }

    public string? Error { get; set; }
    public string? OutputPath { get; set; }
    public long TotalSourceRecords { get; set; }
    public long AlignedRecords { get; set; }
    public int GapsDetected { get; set; }
    public int GapsFilled { get; set; }
    public TimeSpan Duration { get; set; }
    public AlignmentMetadata? Metadata { get; set; }
    public TimeSeriesAlignmentSummary? Summary { get; set; }
}

/// <summary>
/// Summarizes aligned records and gaps by symbol with time coverage and interval totals.
/// </summary>
public sealed class AlignmentMetadata
{
    public Dictionary<string, long>? RecordsBySymbol { get; set; }
    public Dictionary<string, int>? GapsBySymbol { get; set; }
    public DateTimeOffset? FirstTimestamp { get; set; }
    public DateTimeOffset? LastTimestamp { get; set; }
    public int TotalIntervals { get; set; }
    public double CoveragePercent { get; set; }
}

/// <summary>
/// Describes a selectable alignment interval with its display label and length in seconds.
/// </summary>
public sealed class AlignmentInterval
{
    public TimeSeriesInterval Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int Seconds { get; set; }
}

/// <summary>
/// Describes a selectable aggregation method, its explanation, and the fields it produces.
/// </summary>
public sealed class AggregationMethod
{
    public AggregationType Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[]? Fields { get; set; }
}

/// <summary>
/// Describes a selectable missing-interval policy with its display label, explanation, and icon.
/// </summary>
public sealed class GapHandlingStrategy
{
    public GapStrategy Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}

/// <summary>
/// Provides a reusable interval, aggregation, gap-handling, and market-hours configuration.
/// </summary>
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

/// <summary>
/// Reports expected alignment output, gap counts, artifact size, and source records or a preview error.
/// </summary>
public sealed class AlignmentPreview
{
    public bool Success { get; set; }

    /// <summary>
    /// True when the host reported that alignment is not implemented (HTTP 501), so retrying
    /// with different options cannot succeed.
    /// </summary>
    public bool IsServiceUnavailable { get; set; }

    public string? Error { get; set; }
    public long TotalSourceRecords { get; set; }
    public long ExpectedOutputRecords { get; set; }
    public int ExpectedGaps { get; set; }
    public long? EstimatedFileSizeBytes { get; set; }
    public int IntervalCount { get; set; }
    public TimeSeriesAlignmentSummary? Summary { get; set; }
    public TimeSeriesAlignmentRow[] SampleRows { get; set; } = [];
}

/// <summary>
/// Collects errors and warnings from validating time-series alignment options.
/// </summary>
public sealed class AlignmentValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}



/// <summary>
/// Carries API alignment artifact details, record and gap totals, duration, and metadata.
/// </summary>
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

/// <summary>
/// Carries API estimates for alignment records, gaps, artifact bytes, and intervals.
/// </summary>
public sealed class AlignmentPreviewResponse
{
    public long TotalSourceRecords { get; set; }
    public long ExpectedOutputRecords { get; set; }
    public int ExpectedGaps { get; set; }
    public long EstimatedFileSizeBytes { get; set; }
    public int IntervalCount { get; set; }
}

