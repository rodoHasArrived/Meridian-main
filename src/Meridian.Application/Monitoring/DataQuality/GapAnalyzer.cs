using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Enums;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Analyzes data gaps and generates visual timelines for data availability.
/// Tracks gaps in real-time and provides historical gap analysis.
/// </summary>
public sealed class GapAnalyzer : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<GapAnalyzer>();
    private readonly ConcurrentDictionary<string, SymbolGapState> _symbolStates = new();
    private readonly ConcurrentDictionary<string, List<DataGap>> _detectedGaps = new();
    private readonly ConcurrentDictionary<string, LiquidityProfile> _symbolLiquidity = new();
    private readonly ConcurrentDictionary<string, GapAnalyzerConfig> _effectiveConfigCache = new();
    private readonly GapAnalyzerConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    // Global statistics
    private long _totalGapsDetected;
    private long _totalEventsProcessed;

    /// <summary>
    /// Event raised when a significant gap is detected.
    /// </summary>
    public event Action<DataGap>? OnGapDetected;

    public GapAnalyzer(GapAnalyzerConfig? config = null)
    {
        _config = config ?? GapAnalyzerConfig.Default;
        _cleanupTimer = new Timer(CleanupOldData, null,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        _log.Information("GapAnalyzer initialized with gap threshold: {ThresholdSeconds}s",
            _config.GapThresholdSeconds);
    }

    /// <summary>
    /// Registers a liquidity profile for a symbol, adjusting gap detection thresholds.
    /// </summary>
    public void RegisterSymbolLiquidity(string symbol, LiquidityProfile profile)
    {
        var normalizedSymbol = symbol.ToUpperInvariant();
        _symbolLiquidity[normalizedSymbol] = profile;
        // Invalidate cached effective config so it's recomputed with the new profile
        _effectiveConfigCache.TryRemove(normalizedSymbol, out _);
        _log.Debug("Registered liquidity profile {Profile} for {Symbol}", profile, symbol);
    }

    /// <summary>
    /// Gets the liquidity profile for a symbol, defaulting to <see cref="LiquidityProfile.High"/>.
    /// </summary>
    public LiquidityProfile GetSymbolLiquidity(string symbol)
    {
        return _symbolLiquidity.TryGetValue(symbol.ToUpperInvariant(), out var profile)
            ? profile
            : LiquidityProfile.High;
    }

    /// <summary>
    /// Records an event timestamp for gap detection.
    /// Uses per-symbol liquidity thresholds when registered, falling back to global config.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordEvent(string symbol, string eventType, DateTimeOffset timestamp, long? sequenceNumber = null)
    {
        if (_isDisposed)
            return;

        Interlocked.Increment(ref _totalEventsProcessed);

        var key = GetKey(symbol, eventType);
        var state = _symbolStates.GetOrAdd(key, _ => new SymbolGapState(symbol, eventType));

        var effectiveConfig = GetEffectiveConfig(symbol);
        var liquidityProfile = GetSymbolLiquidity(symbol);
        var gap = state.RecordEvent(timestamp, sequenceNumber, effectiveConfig, liquidityProfile);
        if (gap != null)
        {
            RecordGap(gap);
        }
    }

    private GapAnalyzerConfig GetEffectiveConfig(string symbol)
    {
        var normalizedSymbol = symbol.ToUpperInvariant();
        if (!_symbolLiquidity.TryGetValue(normalizedSymbol, out _))
        {
            return _config;
        }

        // Use a per-symbol cache to avoid allocating a new GapAnalyzerConfig record
        // on every single event. The cache is invalidated when the liquidity profile
        // changes via RegisterSymbolLiquidity.
        return _effectiveConfigCache.GetOrAdd(normalizedSymbol, key =>
        {
            var profile = _symbolLiquidity[key];
            var thresholds = LiquidityProfileProvider.GetThresholds(profile);
            return _config with
            {
                GapThresholdSeconds = thresholds.GapThresholdSeconds,
                ExpectedEventsPerHour = thresholds.ExpectedEventsPerHour
            };
        });
    }

    /// <summary>
    /// Analyzes gaps for a specific symbol and date, generating a timeline.
    /// </summary>
    public GapAnalysisResult AnalyzeGaps(string symbol, DateOnly date, string? eventType = null)
    {
        var key = eventType != null ? GetKey(symbol, eventType) : symbol.ToUpperInvariant();
        var gaps = GetGapsForSymbolDate(symbol, date, eventType);
        var timeline = GenerateTimeline(symbol, date, gaps);

        var totalGapDuration = gaps.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Duration);
        var tradingDuration = GetTradingDuration(date);
        var dataAvailabilityPercent = tradingDuration.TotalMinutes > 0
            ? Math.Max(0, 100 - (totalGapDuration.TotalMinutes / tradingDuration.TotalMinutes * 100))
            : 100;

        return new GapAnalysisResult(
            Symbol: symbol,
            Date: date,
            TotalGaps: gaps.Count,
            TotalGapDuration: totalGapDuration,
            DataAvailabilityPercent: Math.Round(dataAvailabilityPercent, 2),
            Gaps: gaps,
            Timeline: timeline,
            AnalyzedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Gets all gaps for a specific symbol and date.
    /// </summary>
    public IReadOnlyList<DataGap> GetGapsForSymbolDate(string symbol, DateOnly date, string? eventType = null)
    {
        var normalizedSymbol = symbol.ToUpperInvariant();

        return _detectedGaps.Values
            .SelectMany(list => list)
            .Where(g => g.Symbol.Equals(normalizedSymbol, StringComparison.OrdinalIgnoreCase) &&
                        DateOnly.FromDateTime(g.GapStart.UtcDateTime) == date &&
                        (eventType == null || g.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(g => g.GapStart)
            .ToList();
    }

    /// <summary>
    /// Gets all gaps for a specific date across all symbols.
    /// </summary>
    public IReadOnlyList<DataGap> GetGapsForDate(DateOnly date)
    {
        return _detectedGaps.Values
            .SelectMany(list => list)
            .Where(g => DateOnly.FromDateTime(g.GapStart.UtcDateTime) == date)
            .OrderBy(g => g.GapStart)
            .ToList();
    }

    /// <summary>
    /// Gets recent gaps across all symbols.
    /// </summary>
    public IReadOnlyList<DataGap> GetRecentGaps(int count = 100)
    {
        return _detectedGaps.Values
            .SelectMany(list => list)
            .OrderByDescending(g => g.GapStart)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets gap statistics for a date.
    /// </summary>
    public GapStatistics GetStatistics(DateOnly? date = null)
    {
        var gaps = date.HasValue
            ? GetGapsForDate(date.Value)
            : _detectedGaps.Values.SelectMany(list => list).ToList();

        if (gaps.Count == 0)
        {
            return new GapStatistics(
                TotalGaps: 0,
                TotalGapDuration: TimeSpan.Zero,
                AverageGapDuration: TimeSpan.Zero,
                MaxGapDuration: TimeSpan.Zero,
                MinGapDuration: TimeSpan.Zero,
                GapsBySeverity: new Dictionary<GapSeverity, int>(),
                SymbolsAffected: 0,
                MostAffectedSymbols: Array.Empty<string>(),
                CalculatedAt: DateTimeOffset.UtcNow
            );
        }

        var gapsBySeverity = gaps
            .GroupBy(g => g.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        var symbolCounts = gaps
            .GroupBy(g => g.Symbol)
            .Select(g => (Symbol: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => x.Symbol)
            .ToArray();

        return new GapStatistics(
            TotalGaps: gaps.Count,
            TotalGapDuration: gaps.Aggregate(TimeSpan.Zero, (sum, g) => sum + g.Duration),
            AverageGapDuration: TimeSpan.FromTicks((long)gaps.Average(g => g.Duration.Ticks)),
            MaxGapDuration: gaps.Max(g => g.Duration),
            MinGapDuration: gaps.Min(g => g.Duration),
            GapsBySeverity: gapsBySeverity,
            SymbolsAffected: gaps.Select(g => g.Symbol).Distinct().Count(),
            MostAffectedSymbols: symbolCounts,
            CalculatedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Gets symbols with gaps exceeding a threshold.
    /// </summary>
    public IReadOnlyList<string> GetSymbolsWithSignificantGaps(DateOnly date, TimeSpan threshold)
    {
        return GetGapsForDate(date)
            .Where(g => g.Duration >= threshold)
            .Select(g => g.Symbol)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Generates a visual timeline for a symbol/date.
    /// </summary>
    public IReadOnlyList<TimelineEntry> GenerateTimeline(string symbol, DateOnly date, IReadOnlyList<DataGap>? gaps = null)
    {
        gaps ??= GetGapsForSymbolDate(symbol, date, null);
        var timeline = new List<TimelineEntry>();

        var tradingStart = GetTradingStart(date);
        var tradingEnd = GetTradingEnd(date);
        var preMarketStart = tradingStart.AddHours(-_config.PreMarketHours);
        var afterHoursEnd = tradingEnd.AddHours(_config.AfterHoursHours);

        // Pre-market
        if (_config.IncludeExtendedHours)
        {
            timeline.Add(new TimelineEntry(
                preMarketStart, tradingStart, TimelineEntryType.PreMarket, 0, "Pre-market session"));
        }

        // Process gaps and data segments during trading hours
        var currentTime = tradingStart;
        var sortedGaps = gaps.Where(g => g.GapStart >= tradingStart && g.GapEnd <= tradingEnd)
                              .OrderBy(g => g.GapStart)
                              .ToList();

        foreach (var gap in sortedGaps)
        {
            // Data present segment before gap
            if (gap.GapStart > currentTime)
            {
                timeline.Add(new TimelineEntry(
                    currentTime, gap.GapStart, TimelineEntryType.DataPresent,
                    EstimateEventCount(currentTime, gap.GapStart), null));
            }

            // Gap segment
            timeline.Add(new TimelineEntry(
                gap.GapStart, gap.GapEnd, TimelineEntryType.Gap,
                0, $"Gap: {gap.Duration.TotalMinutes:F1} min"));

            currentTime = gap.GapEnd;
        }

        // Final data segment after last gap
        if (currentTime < tradingEnd)
        {
            timeline.Add(new TimelineEntry(
                currentTime, tradingEnd, TimelineEntryType.DataPresent,
                EstimateEventCount(currentTime, tradingEnd), null));
        }

        // After-hours
        if (_config.IncludeExtendedHours)
        {
            timeline.Add(new TimelineEntry(
                tradingEnd, afterHoursEnd, TimelineEntryType.AfterHours, 0, "After-hours session"));
        }

        return timeline;
    }

    /// <summary>
    /// Resets gap tracking for a symbol.
    /// </summary>
    public void ResetSymbol(string symbol, string? eventType = null)
    {
        var keys = eventType != null
            ? new[] { GetKey(symbol, eventType) }
            : _symbolStates.Keys.Where(k => k.StartsWith(symbol.ToUpperInvariant() + ":")).ToArray();

        foreach (var key in keys)
        {
            _symbolStates.TryRemove(key, out _);
            _detectedGaps.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Gets total gaps detected count.
    /// </summary>
    public long TotalGapsDetected => Interlocked.Read(ref _totalGapsDetected);

    /// <summary>
    /// Gets total events processed count.
    /// </summary>
    public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);

    private void RecordGap(DataGap gap)
    {
        Interlocked.Increment(ref _totalGapsDetected);

        var key = GetKey(gap.Symbol, gap.EventType);
        var gapList = _detectedGaps.GetOrAdd(key, _ => new List<DataGap>());

        lock (gapList)
        {
            gapList.Add(gap);

            // Keep only recent gaps to prevent memory growth
            while (gapList.Count > _config.MaxGapsPerSymbol)
            {
                gapList.RemoveAt(0);
            }
        }

        // Log significant gaps
        if (gap.Severity >= GapSeverity.Significant)
        {
            _log.Warning("Significant data gap detected for {Symbol}:{EventType} - Duration: {Duration}, " +
                "Severity: {Severity}", gap.Symbol, gap.EventType, gap.Duration, gap.Severity);
        }

        try
        {
            OnGapDetected?.Invoke(gap);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in gap detected event handler");
        }
    }

    private static string GetKey(string symbol, string eventType) => $"{symbol.ToUpperInvariant()}:{eventType}";

    private TimeSpan GetTradingDuration(DateOnly date)
    {
        return TimeSpan.FromHours(_config.TradingEndHour - _config.TradingStartHour)
               .Add(TimeSpan.FromMinutes(_config.TradingEndMinute - _config.TradingStartMinute));
    }

    private DateTimeOffset GetTradingStart(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day,
            _config.TradingStartHour, _config.TradingStartMinute, 0, TimeSpan.Zero);
    }

    private DateTimeOffset GetTradingEnd(DateOnly date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day,
            _config.TradingEndHour, _config.TradingEndMinute, 0, TimeSpan.Zero);
    }

    private long EstimateEventCount(DateTimeOffset start, DateTimeOffset end)
    {
        var hours = (end - start).TotalHours;
        return (long)(hours * _config.ExpectedEventsPerHour);
    }

    private void CleanupOldData(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_config.RetentionDays);
            var keysToClean = new List<string>();

            foreach (var kvp in _detectedGaps)
            {
                lock (kvp.Value)
                {
                    kvp.Value.RemoveAll(g => g.GapEnd < cutoff);
                    if (kvp.Value.Count == 0)
                    {
                        keysToClean.Add(kvp.Key);
                    }
                }
            }

            foreach (var key in keysToClean)
            {
                _detectedGaps.TryRemove(key, out _);
            }

            // Clean up symbol states that haven't been active
            var stateKeysToRemove = _symbolStates
                .Where(kvp => kvp.Value.LastEventTime < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in stateKeysToRemove)
            {
                _symbolStates.TryRemove(key, out _);
            }

            if (keysToClean.Count > 0 || stateKeysToRemove.Count > 0)
            {
                _log.Debug("Gap analyzer cleanup: removed {GapKeys} gap lists and {StateKeys} symbol states",
                    keysToClean.Count, stateKeysToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during gap analyzer cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _symbolStates.Clear();
        _detectedGaps.Clear();
    }

    /// <summary>
    /// Per-symbol gap tracking state.
    /// </summary>
    private sealed class SymbolGapState
    {
        private DateTimeOffset _lastEventTime = DateTimeOffset.MinValue;
        private long _lastSequence = -1;
        private long _eventCount;

        public string Symbol { get; }
        public string EventType { get; }
        public DateTimeOffset LastEventTime => _lastEventTime;

        public SymbolGapState(string symbol, string eventType)
        {
            Symbol = symbol;
            EventType = eventType;
        }

        public DataGap? RecordEvent(DateTimeOffset timestamp, long? sequenceNumber, GapAnalyzerConfig config, LiquidityProfile liquidityProfile = LiquidityProfile.High)
        {
            DataGap? detectedGap = null;
            var previousTime = _lastEventTime;
            var previousSeq = _lastSequence;

            _lastEventTime = timestamp;
            _eventCount++;

            if (sequenceNumber.HasValue)
            {
                _lastSequence = sequenceNumber.Value;
            }

            // Skip gap detection for first event
            if (previousTime == DateTimeOffset.MinValue)
            {
                return null;
            }

            // Check for time-based gap using the (potentially liquidity-adjusted) threshold
            var timeDelta = timestamp - previousTime;
            if (timeDelta.TotalSeconds >= config.GapThresholdSeconds)
            {
                var severity = LiquidityProfileProvider.ClassifyGapSeverity(timeDelta, liquidityProfile);
                var estimatedMissed = EstimateMissedEvents(timeDelta, config.ExpectedEventsPerHour);
                var possibleCause = LiquidityProfileProvider.InferGapCause(timeDelta, previousTime, timestamp, liquidityProfile);

                detectedGap = new DataGap(
                    Symbol: Symbol,
                    EventType: EventType,
                    GapStart: previousTime,
                    GapEnd: timestamp,
                    Duration: timeDelta,
                    MissedSequenceStart: previousSeq + 1,
                    MissedSequenceEnd: sequenceNumber ?? previousSeq + estimatedMissed,
                    EstimatedMissedEvents: estimatedMissed,
                    Severity: severity,
                    PossibleCause: possibleCause
                );
            }

            return detectedGap;
        }

        private static long EstimateMissedEvents(TimeSpan duration, long eventsPerHour)
        {
            return (long)(duration.TotalHours * eventsPerHour);
        }
    }
}

/// <summary>
/// Configuration for gap analysis.
/// </summary>
public sealed record GapAnalyzerConfig
{
    /// <summary>
    /// Minimum gap duration in seconds to be considered a gap.
    /// </summary>
    public int GapThresholdSeconds { get; init; } = 60;

    /// <summary>
    /// Trading start hour (UTC).
    /// </summary>
    public int TradingStartHour { get; init; } = 13; // 9:30 AM ET

    /// <summary>
    /// Trading start minute.
    /// </summary>
    public int TradingStartMinute { get; init; } = 30;

    /// <summary>
    /// Trading end hour (UTC).
    /// </summary>
    public int TradingEndHour { get; init; } = 20; // 4:00 PM ET

    /// <summary>
    /// Trading end minute.
    /// </summary>
    public int TradingEndMinute { get; init; } = 0;

    /// <summary>
    /// Expected events per hour for estimation.
    /// </summary>
    public long ExpectedEventsPerHour { get; init; } = 1000;

    /// <summary>
    /// Include pre-market and after-hours in timeline.
    /// </summary>
    public bool IncludeExtendedHours { get; init; } = true;

    /// <summary>
    /// Pre-market hours before regular trading.
    /// </summary>
    public double PreMarketHours { get; init; } = 5.5; // 4:00 AM ET

    /// <summary>
    /// After-hours duration after regular trading.
    /// </summary>
    public double AfterHoursHours { get; init; } = 4.0; // Until 8:00 PM ET

    /// <summary>
    /// Maximum gaps to retain per symbol.
    /// </summary>
    public int MaxGapsPerSymbol { get; init; } = 1000;

    /// <summary>
    /// Days to retain gap history.
    /// </summary>
    public int RetentionDays { get; init; } = 30;

    public static GapAnalyzerConfig Default => new();
}

/// <summary>
/// Gap statistics summary.
/// </summary>
public sealed record GapStatistics(
    int TotalGaps,
    TimeSpan TotalGapDuration,
    TimeSpan AverageGapDuration,
    TimeSpan MaxGapDuration,
    TimeSpan MinGapDuration,
    Dictionary<GapSeverity, int> GapsBySeverity,
    int SymbolsAffected,
    string[] MostAffectedSymbols,
    DateTimeOffset CalculatedAt
);
