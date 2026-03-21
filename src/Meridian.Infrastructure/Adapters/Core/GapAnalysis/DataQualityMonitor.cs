using System.Collections.Concurrent;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Type of quality issue detected.
/// </summary>
public enum QualityIssueType : byte
{
    /// <summary>High price is less than low price.</summary>
    InvalidHighLow,

    /// <summary>Close price outside of high-low range.</summary>
    CloseOutOfRange,

    /// <summary>Open price outside of high-low range.</summary>
    OpenOutOfRange,

    /// <summary>Price changed more than threshold in one day.</summary>
    SuspiciousPriceMove,

    /// <summary>Zero or negative volume.</summary>
    InvalidVolume,

    /// <summary>Missing expected data.</summary>
    MissingData,

    /// <summary>Duplicate data detected.</summary>
    DuplicateData,

    /// <summary>Price significantly different from other sources.</summary>
    PriceDiscrepancy,

    /// <summary>Data older than expected.</summary>
    StaleData,

    /// <summary>Gap in sequence numbers.</summary>
    SequenceGap,

    /// <summary>Data arrives out of order.</summary>
    OutOfOrder,

    /// <summary>Timestamp in the future.</summary>
    FutureTimestamp,

    /// <summary>Generic anomaly detected.</summary>
    Anomaly
}

/// <summary>
/// Severity of a quality issue.
/// </summary>
public enum QualitySeverity : byte
{
    /// <summary>Informational only.</summary>
    Info,

    /// <summary>Potential issue, investigation recommended.</summary>
    Warning,

    /// <summary>Definite issue that may affect analysis.</summary>
    Error,

    /// <summary>Critical issue that invalidates data.</summary>
    Critical
}

/// <summary>
/// A single quality issue detected in the data.
/// </summary>
public sealed record QualityIssue(
    QualityIssueType Type,
    string Message,
    QualitySeverity Severity,
    DateOnly? Date = null,
    string? Symbol = null,
    string? Source = null
);

/// <summary>
/// A quality dimension with its score.
/// </summary>
public sealed record QualityDimension(
    string Name,
    double Score,
    double Weight,
    string Description
)
{
    public double WeightedScore => Score * Weight;

    public static QualityDimension Completeness(double score) =>
        new("Completeness", score, 0.30, "Percentage of expected data present");

    public static QualityDimension Accuracy(double score) =>
        new("Accuracy", score, 0.25, "Data accuracy and correctness");

    public static QualityDimension Timeliness(double score) =>
        new("Timeliness", score, 0.20, "Data freshness and latency");

    public static QualityDimension Consistency(double score) =>
        new("Consistency", score, 0.15, "Cross-source consistency");

    public static QualityDimension Validity(double score) =>
        new("Validity", score, 0.10, "Data format and constraint validity");
}

/// <summary>
/// Quality score for a symbol.
/// </summary>
public sealed record QualityScore(
    string Symbol,
    double OverallScore,
    QualityDimension[] Dimensions,
    IReadOnlyList<QualityIssue> Issues,
    DateTimeOffset CalculatedAt
)
{
    /// <summary>
    /// Get letter grade based on score.
    /// </summary>
    public string Grade => OverallScore switch
    {
        >= 0.95 => "A+",
        >= 0.90 => "A",
        >= 0.85 => "A-",
        >= 0.80 => "B+",
        >= 0.75 => "B",
        >= 0.70 => "B-",
        >= 0.65 => "C+",
        >= 0.60 => "C",
        >= 0.55 => "C-",
        >= 0.50 => "D",
        _ => "F"
    };

    public int CriticalIssues => Issues.Count(i => i.Severity == QualitySeverity.Critical);
    public int ErrorIssues => Issues.Count(i => i.Severity == QualitySeverity.Error);
    public int WarningIssues => Issues.Count(i => i.Severity == QualitySeverity.Warning);
}

/// <summary>
/// Quality alert for a symbol below threshold.
/// </summary>
public sealed record QualityAlert(
    string Symbol,
    double Score,
    QualitySeverity Severity,
    string Message,
    DateTimeOffset DetectedAt,
    IReadOnlyList<QualityIssue> TopIssues
);

/// <summary>
/// Configuration for quality monitoring.
/// </summary>
public sealed record QualityMonitorOptions
{
    /// <summary>Minimum score threshold for alerts.</summary>
    public double AlertThreshold { get; init; } = 0.80;

    /// <summary>Maximum price change percent before flagging as suspicious.</summary>
    public decimal MaxDailyPriceChangePercent { get; init; } = 50m;

    /// <summary>Maximum stale data age before flagging.</summary>
    public TimeSpan MaxDataAge { get; init; } = TimeSpan.FromDays(2);

    /// <summary>Cross-source price discrepancy threshold (percent).</summary>
    public decimal PriceDiscrepancyThresholdPercent { get; init; } = 1m;

    /// <summary>Number of issues to include in alert.</summary>
    public int TopIssuesInAlert { get; init; } = 5;
}

/// <summary>
/// Monitors and scores data quality across all stored data.
/// Provides multi-dimensional quality assessment and alerting.
/// </summary>
public sealed class DataQualityMonitor
{
    private readonly DataGapAnalyzer _gapAnalyzer;
    private readonly string _dataRoot;
    private readonly QualityMonitorOptions _options;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, QualityScore> _scoreCache = new();

    public DataQualityMonitor(
        DataGapAnalyzer gapAnalyzer,
        string dataRoot,
        QualityMonitorOptions? options = null,
        ILogger? log = null)
    {
        _gapAnalyzer = gapAnalyzer ?? throw new ArgumentNullException(nameof(gapAnalyzer));
        _dataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
        _options = options ?? new QualityMonitorOptions();
        _log = log ?? LoggingSetup.ForContext<DataQualityMonitor>();
    }

    /// <summary>
    /// Calculate quality score for a symbol.
    /// </summary>
    public async Task<QualityScore> CalculateScoreAsync(
        string symbol,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var start = startDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var end = endDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var dimensions = new List<QualityDimension>();
        var issues = new List<QualityIssue>();

        // Get bars for analysis
        var bars = await GetBarsForAnalysisAsync(symbol, start, end, ct).ConfigureAwait(false);

        // Calculate each dimension
        var completenessScore = await CalculateCompletenessAsync(symbol, start, end, issues, ct).ConfigureAwait(false);
        dimensions.Add(QualityDimension.Completeness(completenessScore));

        var accuracyScore = CalculateAccuracy(bars, issues);
        dimensions.Add(QualityDimension.Accuracy(accuracyScore));

        var timelinessScore = CalculateTimeliness(bars, issues);
        dimensions.Add(QualityDimension.Timeliness(timelinessScore));

        var consistencyScore = CalculateConsistency(bars, issues);
        dimensions.Add(QualityDimension.Consistency(consistencyScore));

        var validityScore = CalculateValidity(bars, issues);
        dimensions.Add(QualityDimension.Validity(validityScore));

        // Calculate weighted overall score
        var overallScore = dimensions.Sum(d => d.WeightedScore);

        var score = new QualityScore(
            Symbol: symbol,
            OverallScore: overallScore,
            Dimensions: dimensions.ToArray(),
            Issues: issues,
            CalculatedAt: DateTimeOffset.UtcNow
        );

        // Cache the score
        _scoreCache[symbol] = score;

        _log.Information(
            "Quality score for {Symbol}: {Score:P0} ({Grade}) with {IssueCount} issues",
            symbol, score.OverallScore, score.Grade, score.Issues.Count);

        return score;
    }

    /// <summary>
    /// Get quality alerts for symbols below threshold.
    /// </summary>
    public Task<IReadOnlyList<QualityAlert>> GetAlertsAsync(
        double? minScoreThreshold = null,
        CancellationToken ct = default)
    {
        var threshold = minScoreThreshold ?? _options.AlertThreshold;
        var alerts = new List<QualityAlert>();

        foreach (var kvp in _scoreCache)
        {
            if (kvp.Value.OverallScore < threshold)
            {
                var severity = kvp.Value.OverallScore switch
                {
                    < 0.5 => QualitySeverity.Critical,
                    < 0.7 => QualitySeverity.Error,
                    _ => QualitySeverity.Warning
                };

                alerts.Add(new QualityAlert(
                    Symbol: kvp.Key,
                    Score: kvp.Value.OverallScore,
                    Severity: severity,
                    Message: $"Quality score {kvp.Value.OverallScore:P0} ({kvp.Value.Grade}) below threshold {threshold:P0}",
                    DetectedAt: kvp.Value.CalculatedAt,
                    TopIssues: kvp.Value.Issues
                        .OrderByDescending(i => i.Severity)
                        .Take(_options.TopIssuesInAlert)
                        .ToList()
                ));
            }
        }

        return Task.FromResult<IReadOnlyList<QualityAlert>>(alerts.OrderBy(a => a.Score).ToList());
    }

    /// <summary>
    /// Get cached quality score for a symbol.
    /// </summary>
    public QualityScore? GetCachedScore(string symbol)
    {
        return _scoreCache.TryGetValue(symbol, out var score) ? score : null;
    }

    /// <summary>
    /// Get all cached quality scores.
    /// </summary>
    public IReadOnlyDictionary<string, QualityScore> GetAllScores()
    {
        return _scoreCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Clear all cached scores.
    /// </summary>
    public void ClearCache()
    {
        _scoreCache.Clear();
    }

    private async Task<double> CalculateCompletenessAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        List<QualityIssue> issues,
        CancellationToken ct)
    {
        var gapInfo = await _gapAnalyzer.AnalyzeSymbolGapsAsync(symbol, start, end, ct: ct).ConfigureAwait(false);

        if (gapInfo.GapDates.Count > 0)
        {
            var gapRanges = gapInfo.GetGapRanges();
            foreach (var (gapStart, gapEnd) in gapRanges.Take(10)) // Limit issues reported
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.MissingData,
                    $"Missing data from {gapStart:yyyy-MM-dd} to {gapEnd:yyyy-MM-dd}",
                    gapEnd.DayNumber - gapStart.DayNumber > 5 ? QualitySeverity.Error : QualitySeverity.Warning,
                    gapStart,
                    symbol
                ));
            }
        }

        return gapInfo.CoveragePercent / 100.0;
    }

    private double CalculateAccuracy(IReadOnlyList<HistoricalBar> bars, List<QualityIssue> issues)
    {
        if (bars.Count == 0)
            return 0;

        var issueCount = 0;
        HistoricalBar? previousBar = null;

        foreach (var bar in bars)
        {
            // Check High >= Low
            if (bar.High < bar.Low)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.InvalidHighLow,
                    $"High ({bar.High}) < Low ({bar.Low}) on {bar.SessionDate}",
                    QualitySeverity.Critical,
                    bar.SessionDate,
                    bar.Symbol
                ));
                issueCount++;
            }

            // Check Close within range
            if (bar.Close < bar.Low || bar.Close > bar.High)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.CloseOutOfRange,
                    $"Close ({bar.Close}) outside High-Low range on {bar.SessionDate}",
                    QualitySeverity.Error,
                    bar.SessionDate,
                    bar.Symbol
                ));
                issueCount++;
            }

            // Check Open within range
            if (bar.Open < bar.Low || bar.Open > bar.High)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.OpenOutOfRange,
                    $"Open ({bar.Open}) outside High-Low range on {bar.SessionDate}",
                    QualitySeverity.Error,
                    bar.SessionDate,
                    bar.Symbol
                ));
                issueCount++;
            }

            // Check for suspicious price moves
            if (previousBar != null && previousBar.Close > 0)
            {
                var priceChange = Math.Abs((bar.Close - previousBar.Close) / previousBar.Close * 100);
                if (priceChange > _options.MaxDailyPriceChangePercent)
                {
                    issues.Add(new QualityIssue(
                        QualityIssueType.SuspiciousPriceMove,
                        $"Price changed {priceChange:F1}% from {previousBar.Close} to {bar.Close} on {bar.SessionDate}",
                        QualitySeverity.Warning,
                        bar.SessionDate,
                        bar.Symbol
                    ));
                    issueCount++;
                }
            }

            // Check volume
            if (bar.Volume <= 0)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.InvalidVolume,
                    $"Invalid volume ({bar.Volume}) on {bar.SessionDate}",
                    QualitySeverity.Warning,
                    bar.SessionDate,
                    bar.Symbol
                ));
                issueCount++;
            }

            previousBar = bar;
        }

        return Math.Max(0, 1.0 - ((double)issueCount / bars.Count));
    }

    private double CalculateTimeliness(IReadOnlyList<HistoricalBar> bars, List<QualityIssue> issues)
    {
        if (bars.Count == 0)
            return 0;

        var latestBar = bars.OrderByDescending(b => b.SessionDate).FirstOrDefault();
        if (latestBar == null)
            return 0;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysSinceLatest = today.DayNumber - latestBar.SessionDate.DayNumber;

        // Adjust for weekends
        var effectiveDays = daysSinceLatest;
        if (today.DayOfWeek == DayOfWeek.Saturday)
            effectiveDays--;
        if (today.DayOfWeek == DayOfWeek.Sunday)
            effectiveDays -= 2;

        if (effectiveDays > _options.MaxDataAge.TotalDays)
        {
            issues.Add(new QualityIssue(
                QualityIssueType.StaleData,
                $"Latest data is {effectiveDays} days old (from {latestBar.SessionDate})",
                effectiveDays > 7 ? QualitySeverity.Error : QualitySeverity.Warning,
                latestBar.SessionDate,
                latestBar.Symbol
            ));
        }

        // Score based on staleness (1.0 for today, decreasing with age)
        return Math.Max(0, 1.0 - (effectiveDays / 30.0));
    }

    private double CalculateConsistency(IReadOnlyList<HistoricalBar> bars, List<QualityIssue> issues)
    {
        if (bars.Count < 2)
            return 1.0;

        var issueCount = 0;

        // Check for duplicate dates
        var duplicateDates = bars
            .GroupBy(b => b.SessionDate)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var date in duplicateDates)
        {
            issues.Add(new QualityIssue(
                QualityIssueType.DuplicateData,
                $"Duplicate data for {date}",
                QualitySeverity.Warning,
                date,
                bars.First().Symbol
            ));
            issueCount++;
        }

        // Check for out-of-order data
        var sortedBars = bars.OrderBy(b => b.SessionDate).ToList();
        for (int i = 1; i < sortedBars.Count; i++)
        {
            var prev = sortedBars[i - 1];
            var curr = sortedBars[i];

            // Gap of more than 5 trading days is suspicious
            var dayGap = curr.SessionDate.DayNumber - prev.SessionDate.DayNumber;
            if (dayGap > 7) // Roughly a week, accounting for weekends
            {
                // Already captured by completeness check
            }
        }

        return Math.Max(0, 1.0 - ((double)issueCount / bars.Count));
    }

    private double CalculateValidity(IReadOnlyList<HistoricalBar> bars, List<QualityIssue> issues)
    {
        if (bars.Count == 0)
            return 0;

        var issueCount = 0;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var bar in bars)
        {
            // Check for future dates
            if (bar.SessionDate > today)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.FutureTimestamp,
                    $"Bar date {bar.SessionDate} is in the future",
                    QualitySeverity.Critical,
                    bar.SessionDate,
                    bar.Symbol
                ));
                issueCount++;
            }

            // Check for negative prices
            if (bar.Open < 0 || bar.High < 0 || bar.Low < 0 || bar.Close < 0)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.Anomaly,
                    $"Negative price detected on {bar.SessionDate}",
                    QualitySeverity.Critical,
                    bar.SessionDate,
                    bar.Symbol
                ));
                issueCount++;
            }

            // Check for zero prices (unless volume is also zero - might be valid for some instruments)
            if ((bar.Open == 0 || bar.Close == 0) && bar.Volume > 0)
            {
                issues.Add(new QualityIssue(
                    QualityIssueType.Anomaly,
                    $"Zero price with non-zero volume on {bar.SessionDate}",
                    QualitySeverity.Warning,
                    bar.SessionDate,
                    bar.Symbol
                ));
                issueCount++;
            }
        }

        return Math.Max(0, 1.0 - ((double)issueCount / bars.Count));
    }

    private async Task<IReadOnlyList<HistoricalBar>> GetBarsForAnalysisAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        CancellationToken ct)
    {
        var bars = new List<HistoricalBar>();

        // Read bars from storage
        var inventory = await _gapAnalyzer.GetSymbolInventoryAsync(symbol, ct).ConfigureAwait(false);

        foreach (var kvp in inventory.DailyBarFiles)
        {
            if (kvp.Key < start || kvp.Key > end)
                continue;

            try
            {
                var fileContent = await File.ReadAllLinesAsync(kvp.Value.Path, ct).ConfigureAwait(false);
                foreach (var line in fileContent)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        var bar = System.Text.Json.JsonSerializer.Deserialize<HistoricalBar>(line);
                        if (bar != null)
                            bars.Add(bar);
                    }
                    catch
                    {
                        // Skip malformed lines
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Failed to read file {Path}", kvp.Value.Path);
            }
        }

        return bars.OrderBy(b => b.SessionDate).ToList();
    }
}
