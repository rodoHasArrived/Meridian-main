namespace Meridian.Contracts.Api;

/// <summary>One bounded transformation over selected provider archives.</summary>
public sealed record TimeSeriesAlignmentRequest(
    string[]? Symbols,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string[]? Providers = null,
    string Interval = "Daily",
    string GapStrategy = "Skip",
    int MaxGapIntervals = 5,
    string Timezone = "America/New_York",
    bool MarketHoursOnly = true,
    string InputKind = "Auto",
    string AdjustmentPolicy = "AsStored",
    string[]? Fields = null,
    string Aggregation = "OHLCV",
    string? ExpectedContentHash = null);

public sealed record TimeSeriesAlignmentRow(
    string Provider,
    string Symbol,
    DateOnly SessionDate,
    DateTimeOffset Timestamp,
    string AdjustmentBasis,
    string ValueStatus,
    decimal? Open,
    decimal? High,
    decimal? Low,
    decimal? Close,
    decimal? Volume,
    string[] SourceIds);

public sealed record TimeSeriesAlignmentSeries(
    string Provider,
    string Symbol,
    string AdjustmentBasis,
    int SourceRecords,
    int ObservedIntervals,
    int Gaps,
    int GapsFilled,
    int DuplicateRecords);

public sealed record TimeSeriesAlignmentDifference(
    string Symbol,
    string LeftProvider,
    string RightProvider,
    int ObservedOverlap,
    int EqualCloses,
    decimal? MaxAbsoluteCloseDifference,
    string? ComparisonNote);

public sealed record TimeSeriesAlignmentSummary(
    string ContentHash,
    string Calendar,
    string TimestampRule,
    string Timezone,
    string InputKind,
    string Interval,
    string GapStrategy,
    int MaxGapIntervals,
    string MissingValueRule,
    string AdjustmentPolicy,
    string DuplicatePolicy,
    string[] Fields,
    int IntervalCount,
    int TotalSourceRecords,
    int OutputRecords,
    int GapsDetected,
    int GapsFilled,
    int RecordsOutsideCalendar,
    TimeSeriesAlignmentSeries[] Series,
    TimeSeriesAlignmentDifference[] Differences);

public sealed record TimeSeriesAlignmentPreview(
    TimeSeriesAlignmentSummary Summary,
    TimeSeriesAlignmentRow[] SampleRows);

public sealed record TimeSeriesAlignmentExecution(
    TimeSeriesAlignmentSummary Summary,
    TimeSeriesAlignmentRow[] Rows);
