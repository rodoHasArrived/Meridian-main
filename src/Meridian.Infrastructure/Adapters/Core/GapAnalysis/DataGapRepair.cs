using System.Threading;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.ProviderSdk;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Type of data gap detected.
/// </summary>
public enum GapType : byte
{
    /// <summary>No data at all for the date.</summary>
    Missing,

    /// <summary>Some data missing (e.g., no volume).</summary>
    Partial,

    /// <summary>Market holiday (expected gap).</summary>
    Holiday,

    /// <summary>Weekend (expected gap).</summary>
    Weekend,

    /// <summary>Trading halt.</summary>
    Halted,

    /// <summary>Data exists but looks anomalous.</summary>
    Suspicious
}

/// <summary>
/// Severity of a data gap.
/// </summary>
public enum GapSeverity : byte
{
    /// <summary>Expected gaps (holidays, weekends).</summary>
    Info,

    /// <summary>Partial data.</summary>
    Warning,

    /// <summary>Missing critical data.</summary>
    Critical
}

/// <summary>
/// Represents a detected data gap.
/// </summary>
public sealed record DataGap(
    DateOnly StartDate,
    DateOnly EndDate,
    GapType Type,
    GapSeverity Severity,
    int MissingDays,
    string? Reason = null,
    string[]? AvailableAlternateSources = null
)
{
    public static DataGap Missing(DateOnly date, string? reason = null) =>
        new(date, date, GapType.Missing, GapSeverity.Critical, 1, reason);

    public static DataGap MissingRange(DateOnly start, DateOnly end, string? reason = null) =>
        new(start, end, GapType.Missing, GapSeverity.Critical, end.DayNumber - start.DayNumber + 1, reason);

    public static DataGap Partial(DateOnly date, string reason) =>
        new(date, date, GapType.Partial, GapSeverity.Warning, 1, reason);

    public static DataGap Holiday(DateOnly date, string holidayName) =>
        new(date, date, GapType.Holiday, GapSeverity.Info, 1, holidayName);
}

/// <summary>
/// Report of gaps detected for a symbol.
/// </summary>
public sealed record GapReport(
    string Symbol,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    IReadOnlyList<DataGap> Gaps,
    int TotalTradingDays,
    int AvailableDays,
    double CoveragePercent,
    IReadOnlyList<string> SourcesAnalyzed,
    DateTimeOffset AnalyzedAt
);

/// <summary>
/// Options for gap repair operations.
/// </summary>
public sealed record GapRepairOptions
{
    /// <summary>Preferred providers to use for repair (in priority order).</summary>
    public string[]? PreferredProviders { get; init; }

    /// <summary>Try all available providers if preferred fail.</summary>
    public bool TryAllProviders { get; init; } = true;

    /// <summary>Continue repairing other gaps if one fails.</summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>Also repair partial gaps (not just missing).</summary>
    public bool RepairPartialGaps { get; init; } = false;

    /// <summary>Maximum concurrent repair operations.</summary>
    public int MaxConcurrency { get; init; } = 2;

    /// <summary>Delay between requests to avoid rate limiting.</summary>
    public TimeSpan RequestDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum gaps to repair in one operation.</summary>
    public int? MaxGapsToRepair { get; init; }
}

/// <summary>
/// Result of repairing a single gap.
/// </summary>
public sealed record GapRepairItemResult(
    DataGap Gap,
    bool Success,
    string? ErrorMessage = null,
    string? UsedProvider = null,
    int BarsRetrieved = 0,
    TimeSpan Duration = default
);

/// <summary>
/// Result of a gap repair operation.
/// </summary>
public sealed record GapRepairResult(
    string Symbol,
    int TotalGaps,
    int RepairedGaps,
    int FailedGaps,
    IReadOnlyList<GapRepairItemResult> Results,
    TimeSpan TotalDuration = default
)
{
    public double SuccessRate => TotalGaps > 0 ? (double)RepairedGaps / TotalGaps * 100 : 0;
}

/// <summary>
/// Data coverage report for a symbol.
/// </summary>
public sealed record CoverageReport(
    string Symbol,
    DateOnly? EarliestData,
    DateOnly? LatestData,
    int TotalDays,
    int AvailableDays,
    double CoveragePercent,
    IReadOnlyList<(DateOnly Start, DateOnly End)> GapRanges,
    Dictionary<string, int> DaysBySource
);

/// <summary>
/// Service for detecting and repairing gaps in historical data.
/// Extends the DataGapAnalyzer with repair capabilities.
/// </summary>
public sealed class DataGapRepairService
{
    private readonly DataGapAnalyzer _analyzer;
    private readonly IEnumerable<IHistoricalDataProvider> _providers;
    private readonly string _dataRoot;
    private readonly IHistoricalBarWriter? _barWriter;
    private readonly ILogger _log;

    public DataGapRepairService(
        DataGapAnalyzer analyzer,
        IEnumerable<IHistoricalDataProvider> providers,
        string dataRoot,
        IHistoricalBarWriter? barWriter = null,
        ILogger? log = null)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _dataRoot = dataRoot ?? throw new ArgumentNullException(nameof(dataRoot));
        _barWriter = barWriter;
        _log = log ?? LoggingSetup.ForContext<DataGapRepairService>();
    }

    /// <summary>
    /// Detect gaps in stored data for a symbol.
    /// </summary>
    public async Task<GapReport> DetectGapsAsync(
        string symbol,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken ct = default)
    {
        var analysisResult = await _analyzer.AnalyzeSymbolGapsAsync(symbol, startDate, endDate, ct: ct).ConfigureAwait(false);

        var gaps = new List<DataGap>();

        // Convert gap dates to DataGap records with consolidated ranges
        var gapRanges = analysisResult.GetGapRanges();
        foreach (var (from, to) in gapRanges)
        {
            gaps.Add(DataGap.MissingRange(from, to));
        }

        // Check which alternate providers might have data for these gaps
        await EnrichGapsWithAlternateSourcesAsync(gaps, symbol, ct).ConfigureAwait(false);

        return new GapReport(
            Symbol: symbol,
            RangeStart: startDate,
            RangeEnd: endDate,
            Gaps: gaps,
            TotalTradingDays: analysisResult.ExpectedDays,
            AvailableDays: analysisResult.CoveredDays,
            CoveragePercent: analysisResult.CoveragePercent,
            SourcesAnalyzed: new[] { "local" },
            AnalyzedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Repair detected gaps by fetching from providers.
    /// </summary>
    public async Task<GapRepairResult> RepairGapsAsync(
        GapReport report,
        GapRepairOptions options,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var repairableGaps = report.Gaps
            .Where(g => g.Type == GapType.Missing ||
                       (g.Type == GapType.Partial && options.RepairPartialGaps))
            .Where(g => g.AvailableAlternateSources?.Length > 0 || options.TryAllProviders)
            .Take(options.MaxGapsToRepair ?? int.MaxValue)
            .ToList();

        var results = new List<GapRepairItemResult>();
        var repairedCount = 0;

        _log.Information("Starting gap repair for {Symbol}: {GapCount} gaps to repair", report.Symbol, repairableGaps.Count);

        foreach (var gap in repairableGaps)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await RepairSingleGapAsync(report.Symbol, gap, options, ct).ConfigureAwait(false);
                results.Add(result);

                if (result.Success)
                {
                    repairedCount++;
                    _log.Information("Repaired gap {Start} to {End} using {Provider}: {Bars} bars",
                        gap.StartDate, gap.EndDate, result.UsedProvider, result.BarsRetrieved);
                }
                else
                {
                    _log.Warning("Failed to repair gap {Start} to {End}: {Error}",
                        gap.StartDate, gap.EndDate, result.ErrorMessage);
                }

                // Apply delay between requests
                if (options.RequestDelay > TimeSpan.Zero)
                {
                    await Task.Delay(options.RequestDelay, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                results.Add(new GapRepairItemResult(gap, false, ex.Message));

                if (!options.ContinueOnError)
                    break;
            }
        }

        sw.Stop();

        _log.Information("Gap repair completed for {Symbol}: {Repaired}/{Total} gaps repaired in {Duration}",
            report.Symbol, repairedCount, repairableGaps.Count, sw.Elapsed);

        return new GapRepairResult(
            Symbol: report.Symbol,
            TotalGaps: repairableGaps.Count,
            RepairedGaps: repairedCount,
            FailedGaps: repairableGaps.Count - repairedCount,
            Results: results,
            TotalDuration: sw.Elapsed
        );
    }

    /// <summary>
    /// Get coverage summary for a symbol.
    /// </summary>
    public async Task<CoverageReport> GetCoverageAsync(
        string symbol,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var inventory = await _analyzer.GetSymbolInventoryAsync(symbol, ct).ConfigureAwait(false);

        var start = startDate ?? inventory.OldestDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var end = endDate ?? inventory.NewestDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var analysis = await _analyzer.AnalyzeSymbolGapsAsync(symbol, start, end, ct: ct).ConfigureAwait(false);

        var gapRanges = analysis.GetGapRanges();

        return new CoverageReport(
            Symbol: symbol,
            EarliestData: inventory.OldestDate,
            LatestData: inventory.NewestDate,
            TotalDays: analysis.ExpectedDays,
            AvailableDays: analysis.CoveredDays,
            CoveragePercent: analysis.CoveragePercent,
            GapRanges: gapRanges,
            DaysBySource: new Dictionary<string, int>
            {
                ["local"] = analysis.CoveredDays
            }
        );
    }

    /// <summary>
    /// Check which providers might have data for the given gaps.
    /// </summary>
    private async Task EnrichGapsWithAlternateSourcesAsync(
        List<DataGap> gaps,
        string symbol,
        CancellationToken ct)
    {
        var availabilityTasks = _providers.Select(p => p.IsAvailableAsync(ct));
        var availability = await Task.WhenAll(availabilityTasks).ConfigureAwait(false);
        var availableProviders = _providers
            .Zip(availability, (p, available) => (p, available))
            .Where(x => x.available)
            .Select(x => x.p.Name)
            .ToArray();

        // For simplicity, mark all gaps as having all available providers
        // In production, you might check provider-specific date ranges
        for (int i = 0; i < gaps.Count; i++)
        {
            gaps[i] = gaps[i] with { AvailableAlternateSources = availableProviders };
        }
    }

    private async Task<GapRepairItemResult> RepairSingleGapAsync(
        string symbol,
        DataGap gap,
        GapRepairOptions options,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var providersToTry = GetProvidersToTry(gap, options);

        foreach (var provider in providersToTry)
        {
            try
            {
                var bars = await provider.GetDailyBarsAsync(symbol, gap.StartDate, gap.EndDate, ct).ConfigureAwait(false);

                if (bars.Count > 0)
                {
                    // Store the data
                    await StoreBarsAsync(bars, ct).ConfigureAwait(false);

                    sw.Stop();
                    return new GapRepairItemResult(
                        gap,
                        Success: true,
                        UsedProvider: provider.Name,
                        BarsRetrieved: bars.Count,
                        Duration: sw.Elapsed
                    );
                }
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Provider {Provider} failed to repair gap for {Symbol}", provider.Name, symbol);
            }
        }

        sw.Stop();
        return new GapRepairItemResult(gap, Success: false, ErrorMessage: "No provider could provide data", Duration: sw.Elapsed);
    }

    private IEnumerable<IHistoricalDataProvider> GetProvidersToTry(DataGap gap, GapRepairOptions options)
    {
        if (options.PreferredProviders?.Length > 0)
        {
            // Return preferred providers first
            foreach (var providerId in options.PreferredProviders)
            {
                var provider = _providers.FirstOrDefault(p => p.Name.Equals(providerId, StringComparison.OrdinalIgnoreCase));
                if (provider != null)
                    yield return provider;
            }
        }

        if (options.TryAllProviders)
        {
            // Return remaining providers
            foreach (var provider in _providers)
            {
                if (options.PreferredProviders?.Contains(provider.Name, StringComparer.OrdinalIgnoreCase) != true)
                    yield return provider;
            }
        }
    }

    private async Task StoreBarsAsync(IReadOnlyList<HistoricalBar> bars, CancellationToken ct)
    {
        if (_barWriter == null)
        {
            _log.Warning("No IHistoricalBarWriter configured — {Count} bars will not be persisted", bars.Count);
            return;
        }

        await _barWriter.WriteBarsAsync(bars, ct).ConfigureAwait(false);
    }
}
