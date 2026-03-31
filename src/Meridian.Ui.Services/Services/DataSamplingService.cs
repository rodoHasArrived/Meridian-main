using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services;

/// <summary>
/// Service for creating data samples and subsets for development, testing, and analysis.
/// Supports various sampling strategies including random, time-based, stratified, and volatility-based.
/// </summary>
public sealed class DataSamplingService
{
    private static readonly Lazy<DataSamplingService> _instance = new(() => new DataSamplingService());
    private readonly ApiClientService _apiClient;

    public static DataSamplingService Instance => _instance.Value;

    private DataSamplingService()
    {
        _apiClient = ApiClientService.Instance;
    }

    /// <summary>
    /// Raised when sampling progress changes.
    /// </summary>
    public event EventHandler<SamplingProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Creates a data sample with the specified options.
    /// </summary>
    public async Task<SamplingResult> CreateSampleAsync(
        SamplingOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SamplingResponse>(
            "/api/sampling/create",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString(FormatHelpers.IsoDateFormat),
                toDate = options.ToDate?.ToString(FormatHelpers.IsoDateFormat),
                strategy = options.Strategy.ToString(),
                sampleSize = options.SampleSize,
                samplePercent = options.SamplePercent,
                intervalSeconds = options.IntervalSeconds,
                maintainDistribution = options.MaintainDistribution,
                seed = options.Seed,
                eventTypes = options.EventTypes,
                outputPath = options.OutputPath,
                outputFormat = options.OutputFormat.ToString(),
                includeStatistics = options.IncludeStatistics,
                name = options.Name
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new SamplingResult
            {
                Success = response.Data.Success,
                OutputPath = response.Data.OutputPath,
                TotalSourceRecords = response.Data.TotalSourceRecords,
                SampledRecords = response.Data.SampledRecords,
                SamplingRatio = response.Data.SamplingRatio,
                Statistics = response.Data.Statistics,
                Duration = TimeSpan.FromSeconds(response.Data.DurationSeconds),
                Seed = response.Data.Seed
            };
        }

        return new SamplingResult
        {
            Success = false,
            Error = response.ErrorMessage ?? "Sampling failed"
        };
    }

    /// <summary>
    /// Gets available sampling strategies.
    /// </summary>
    public Task<List<SamplingStrategy>> GetSamplingStrategiesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<SamplingStrategy>
        {
            new()
            {
                Id = SamplingStrategyType.Random,
                Name = "Random Sample",
                Description = "Randomly select N events or N% of total data",
                RequiresSize = true,
                SupportsPercent = true,
                Icon = "\uE8B1"
            },
            new()
            {
                Id = SamplingStrategyType.TimeBased,
                Name = "Time-Based Downsampling",
                Description = "Sample at regular time intervals (e.g., every 10 seconds)",
                RequiresInterval = true,
                Icon = "\uE823"
            },
            new()
            {
                Id = SamplingStrategyType.SymbolStratified,
                Name = "Symbol-Stratified",
                Description = "Equal representation across all symbols",
                RequiresSize = true,
                MaintainsDistribution = true,
                Icon = "\uE8EC"
            },
            new()
            {
                Id = SamplingStrategyType.EventTypeStratified,
                Name = "Event Type-Stratified",
                Description = "Maintain original trade/quote ratio in sample",
                RequiresSize = true,
                MaintainsDistribution = true,
                Icon = "\uE8A5"
            },
            new()
            {
                Id = SamplingStrategyType.VolatilityBased,
                Name = "Volatility-Based",
                Description = "Oversample high-activity periods, undersample quiet periods",
                RequiresSize = true,
                Icon = "\uE9D9"
            },
            new()
            {
                Id = SamplingStrategyType.FirstN,
                Name = "First N Records",
                Description = "Take the first N records from each symbol/date",
                RequiresSize = true,
                Icon = "\uE74B"
            },
            new()
            {
                Id = SamplingStrategyType.LastN,
                Name = "Last N Records",
                Description = "Take the last N records from each symbol/date",
                RequiresSize = true,
                Icon = "\uE74A"
            },
            new()
            {
                Id = SamplingStrategyType.PeakHours,
                Name = "Peak Trading Hours",
                Description = "Sample only market open/close periods (high activity)",
                RequiresSize = true,
                Icon = "\uEC92"
            },
            new()
            {
                Id = SamplingStrategyType.Systematic,
                Name = "Systematic (Every Nth)",
                Description = "Select every Nth record for uniform coverage",
                RequiresInterval = true,
                Icon = "\uE8FD"
            }
        });
    }

    /// <summary>
    /// Gets preset sampling configurations.
    /// </summary>
    public Task<List<SamplingPreset>> GetSamplingPresetsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new List<SamplingPreset>
        {
            new()
            {
                Name = "Quick Test Dataset",
                Description = "Small sample for rapid development testing",
                Strategy = SamplingStrategyType.Random,
                SampleSize = 10000,
                EventTypes = new[] { "Trade", "BboQuote" }
            },
            new()
            {
                Name = "Algorithm Development",
                Description = "Representative sample for strategy development",
                Strategy = SamplingStrategyType.SymbolStratified,
                SamplePercent = 5,
                EventTypes = new[] { "Trade", "BboQuote" },
                IncludeStatistics = true
            },
            new()
            {
                Name = "ML Training Subset",
                Description = "Balanced sample preserving market characteristics",
                Strategy = SamplingStrategyType.VolatilityBased,
                SamplePercent = 10,
                EventTypes = new[] { "Trade" },
                IncludeStatistics = true
            },
            new()
            {
                Name = "1-Minute Snapshots",
                Description = "Sample at 1-minute intervals for quick analysis",
                Strategy = SamplingStrategyType.TimeBased,
                IntervalSeconds = 60,
                EventTypes = new[] { "Trade", "BboQuote" }
            },
            new()
            {
                Name = "Market Open/Close Focus",
                Description = "High-activity periods for volatility research",
                Strategy = SamplingStrategyType.PeakHours,
                SamplePercent = 100,
                EventTypes = new[] { "Trade", "BboQuote", "LOBSnapshot" }
            },
            new()
            {
                Name = "Reproducible Research",
                Description = "Fixed seed sample for reproducible analysis",
                Strategy = SamplingStrategyType.Random,
                SamplePercent = 5,
                Seed = 42,
                IncludeStatistics = true
            }
        });
    }

    /// <summary>
    /// Estimates the sample size for given options.
    /// </summary>
    public async Task<SampleEstimate> EstimateSampleSizeAsync(
        SamplingOptions options,
        CancellationToken ct = default)
    {
        var response = await _apiClient.PostWithResponseAsync<SampleEstimateResponse>(
            "/api/sampling/estimate",
            new
            {
                symbols = options.Symbols,
                fromDate = options.FromDate?.ToString(FormatHelpers.IsoDateFormat),
                toDate = options.ToDate?.ToString(FormatHelpers.IsoDateFormat),
                strategy = options.Strategy.ToString(),
                sampleSize = options.SampleSize,
                samplePercent = options.SamplePercent,
                intervalSeconds = options.IntervalSeconds,
                eventTypes = options.EventTypes
            },
            ct);

        if (response.Success && response.Data != null)
        {
            return new SampleEstimate
            {
                Success = true,
                TotalSourceRecords = response.Data.TotalSourceRecords,
                EstimatedSampleSize = response.Data.EstimatedSampleSize,
                EstimatedFileSizeBytes = response.Data.EstimatedFileSizeBytes,
                EstimatedDurationSeconds = response.Data.EstimatedDurationSeconds
            };
        }

        // Return mock estimate if API unavailable
        return new SampleEstimate
        {
            Success = true,
            TotalSourceRecords = 1000000,
            EstimatedSampleSize = options.SampleSize ?? (long)((options.SamplePercent ?? 10) * 10000),
            EstimatedFileSizeBytes = (options.SampleSize ?? 100000) * 150,
            EstimatedDurationSeconds = 5
        };
    }

    /// <summary>
    /// Gets saved samples.
    /// </summary>
    public async Task<List<SavedSample>> GetSavedSamplesAsync(CancellationToken ct = default)
    {
        var response = await _apiClient.GetWithResponseAsync<SavedSamplesResponse>(
            "/api/sampling/saved",
            ct);

        if (response.Success && response.Data?.Samples != null)
        {
            return response.Data.Samples;
        }

        return new List<SavedSample>();
    }

    /// <summary>
    /// Deletes a saved sample.
    /// </summary>
    public async Task<bool> DeleteSampleAsync(string sampleId, CancellationToken ct = default)
    {
        var response = await _apiClient.DeleteWithResponseAsync<SamplingDeleteResponse>(
            $"/api/sampling/{sampleId}",
            ct);

        return response.Success;
    }

    /// <summary>
    /// Validates sampling options.
    /// </summary>
    public SamplingValidationResult ValidateOptions(SamplingOptions options)
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

        switch (options.Strategy)
        {
            case SamplingStrategyType.Random:
            case SamplingStrategyType.SymbolStratified:
            case SamplingStrategyType.EventTypeStratified:
            case SamplingStrategyType.VolatilityBased:
            case SamplingStrategyType.FirstN:
            case SamplingStrategyType.LastN:
            case SamplingStrategyType.PeakHours:
                if (options.SampleSize == null && options.SamplePercent == null)
                {
                    errors.Add("Sample size or percentage must be specified");
                }
                break;
            case SamplingStrategyType.TimeBased:
            case SamplingStrategyType.Systematic:
                if (options.IntervalSeconds == null || options.IntervalSeconds <= 0)
                {
                    errors.Add("Interval must be specified and greater than 0");
                }
                break;
        }

        if (options.SamplePercent.HasValue && (options.SamplePercent < 0.01 || options.SamplePercent > 100))
        {
            errors.Add("Sample percentage must be between 0.01 and 100");
        }

        if (options.SamplePercent > 50)
        {
            warnings.Add("Large sample sizes (>50%) may not provide significant storage savings");
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            errors.Add("Output path must be specified");
        }

        return new SamplingValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private void RaiseProgress(double progress, string? currentSymbol, long recordsProcessed)
    {
        ProgressChanged?.Invoke(this, new SamplingProgressEventArgs
        {
            Progress = progress,
            CurrentSymbol = currentSymbol,
            RecordsProcessed = recordsProcessed
        });
    }
}


public sealed class SamplingProgressEventArgs : EventArgs
{
    public double Progress { get; set; }
    public string? CurrentSymbol { get; set; }
    public long RecordsProcessed { get; set; }
}



public enum SamplingStrategyType : byte
{
    Random,
    TimeBased,
    SymbolStratified,
    EventTypeStratified,
    VolatilityBased,
    FirstN,
    LastN,
    PeakHours,
    Systematic
}



public sealed class SamplingOptions
{
    public string? Name { get; set; }
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public SamplingStrategyType Strategy { get; set; } = SamplingStrategyType.Random;
    public long? SampleSize { get; set; }
    public double? SamplePercent { get; set; }
    public int? IntervalSeconds { get; set; }
    public bool MaintainDistribution { get; set; }
    public int? Seed { get; set; }
    public string[]? EventTypes { get; set; }
    public string? OutputPath { get; set; }
    public ExportFormat OutputFormat { get; set; } = ExportFormat.Parquet;
    public bool IncludeStatistics { get; set; } = true;
}

public sealed class SamplingResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? OutputPath { get; set; }
    public long TotalSourceRecords { get; set; }
    public long SampledRecords { get; set; }
    public double SamplingRatio { get; set; }
    public SampleStatistics? Statistics { get; set; }
    public TimeSpan Duration { get; set; }
    public int? Seed { get; set; }
}

public sealed class SampleStatistics
{
    public Dictionary<string, long>? RecordsBySymbol { get; set; }
    public Dictionary<string, long>? RecordsByEventType { get; set; }
    public Dictionary<string, double>? PriceStatistics { get; set; }
    public Dictionary<string, double>? VolumeStatistics { get; set; }
    public DateTimeOffset? FirstTimestamp { get; set; }
    public DateTimeOffset? LastTimestamp { get; set; }
    public TimeSpan TimeSpanCovered { get; set; }
}

public sealed class SamplingStrategy
{
    public SamplingStrategyType Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresSize { get; set; }
    public bool RequiresInterval { get; set; }
    public bool SupportsPercent { get; set; }
    public bool MaintainsDistribution { get; set; }
    public string Icon { get; set; } = "\uE8A5";
}

public sealed class SamplingPreset
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SamplingStrategyType Strategy { get; set; }
    public long? SampleSize { get; set; }
    public double? SamplePercent { get; set; }
    public int? IntervalSeconds { get; set; }
    public string[]? EventTypes { get; set; }
    public int? Seed { get; set; }
    public bool IncludeStatistics { get; set; }
}

public sealed class SampleEstimate
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long TotalSourceRecords { get; set; }
    public long EstimatedSampleSize { get; set; }
    public long EstimatedFileSizeBytes { get; set; }
    public double EstimatedDurationSeconds { get; set; }
}

public sealed class SavedSample
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public long RecordCount { get; set; }
    public long FileSizeBytes { get; set; }
    public string? OutputPath { get; set; }
    public List<string>? Symbols { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
}

public sealed class SamplingValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}



public sealed class SamplingResponse
{
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public long TotalSourceRecords { get; set; }
    public long SampledRecords { get; set; }
    public double SamplingRatio { get; set; }
    public SampleStatistics? Statistics { get; set; }
    public double DurationSeconds { get; set; }
    public int? Seed { get; set; }
}

public sealed class SampleEstimateResponse
{
    public long TotalSourceRecords { get; set; }
    public long EstimatedSampleSize { get; set; }
    public long EstimatedFileSizeBytes { get; set; }
    public double EstimatedDurationSeconds { get; set; }
}

public sealed class SavedSamplesResponse
{
    public List<SavedSample>? Samples { get; set; }
}

public sealed class SamplingDeleteResponse
{
    public bool Success { get; set; }
}

