using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Enums;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Calculates data completeness scores per symbol/date based on expected vs actual events.
/// Thread-safe for real-time updates from multiple data streams.
/// </summary>
public sealed class CompletenessScoreCalculator : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<CompletenessScoreCalculator>();
    private readonly ConcurrentDictionary<string, SymbolDateState> _states = new();
    private readonly ConcurrentDictionary<string, LiquidityProfile> _symbolLiquidity = new();
    private readonly CompletenessConfig _config;
    private readonly Timer _cleanupTimer;
    private volatile bool _isDisposed;

    public CompletenessScoreCalculator(CompletenessConfig? config = null)
    {
        _config = config ?? CompletenessConfig.Default;
        _cleanupTimer = new Timer(CleanupOldStates, null,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        _log.Information("CompletenessScoreCalculator initialized with expected events/hour: {ExpectedPerHour}",
            _config.ExpectedEventsPerHour);
    }

    /// <summary>
    /// Registers a liquidity profile for a symbol. When set, the expected events per hour
    /// for that symbol will be derived from the profile instead of the global default.
    /// </summary>
    public void RegisterSymbolLiquidity(string symbol, LiquidityProfile profile)
    {
        _symbolLiquidity[symbol.ToUpperInvariant()] = profile;
        var thresholds = LiquidityProfileProvider.GetThresholds(profile);

        // Update any existing states for this symbol
        foreach (var state in _states.Values.Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            state.SetExpectedEventsPerHour(thresholds.ExpectedEventsPerHour);
        }

        _log.Debug("Registered liquidity profile {Profile} for {Symbol} (expected {Expected} events/hour)",
            profile, symbol, thresholds.ExpectedEventsPerHour);
    }

    /// <summary>
    /// Records an event for completeness tracking.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordEvent(string symbol, DateTimeOffset timestamp, string eventType)
    {
        if (_isDisposed)
            return;

        var date = DateOnly.FromDateTime(timestamp.UtcDateTime);
        var key = GetKey(symbol, date);
        var state = _states.GetOrAdd(key, _ =>
        {
            var s = new SymbolDateState(symbol, date, _config);
            // Apply liquidity-derived expected events if a profile is registered
            if (_symbolLiquidity.TryGetValue(symbol.ToUpperInvariant(), out var profile))
            {
                var thresholds = LiquidityProfileProvider.GetThresholds(profile);
                s.SetExpectedEventsPerHour(thresholds.ExpectedEventsPerHour);
            }
            return s;
        });
        state.RecordEvent(timestamp, eventType);
    }

    /// <summary>
    /// Records multiple events for completeness tracking.
    /// </summary>
    public void RecordEvents(string symbol, IEnumerable<DateTimeOffset> timestamps, string eventType)
    {
        if (_isDisposed)
            return;

        foreach (var ts in timestamps)
        {
            RecordEvent(symbol, ts, eventType);
        }
    }

    /// <summary>
    /// Gets the completeness score for a specific symbol and date.
    /// </summary>
    public CompletenessScore? GetScore(string symbol, DateOnly date)
    {
        var key = GetKey(symbol, date);
        if (!_states.TryGetValue(key, out var state))
            return null;

        return state.CalculateScore();
    }

    /// <summary>
    /// Gets completeness scores for all tracked symbol/dates.
    /// </summary>
    public IReadOnlyList<CompletenessScore> GetAllScores()
    {
        return _states.Values
            .Select(s => s.CalculateScore())
            .OrderByDescending(s => s.Date)
            .ThenBy(s => s.Symbol)
            .ToList();
    }

    /// <summary>
    /// Gets completeness scores for a specific date across all symbols.
    /// </summary>
    public IReadOnlyList<CompletenessScore> GetScoresForDate(DateOnly date)
    {
        return _states.Values
            .Where(s => s.Date == date)
            .Select(s => s.CalculateScore())
            .OrderBy(s => s.Symbol)
            .ToList();
    }

    /// <summary>
    /// Gets completeness scores for a specific symbol across all dates.
    /// </summary>
    public IReadOnlyList<CompletenessScore> GetScoresForSymbol(string symbol)
    {
        return _states.Values
            .Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.CalculateScore())
            .OrderByDescending(s => s.Date)
            .ToList();
    }

    /// <summary>
    /// Gets the average completeness score for a date.
    /// </summary>
    public double GetAverageScore(DateOnly date)
    {
        var scores = GetScoresForDate(date);
        return scores.Count > 0 ? scores.Average(s => s.Score) : 0;
    }

    /// <summary>
    /// Gets symbols with low completeness for a date.
    /// </summary>
    public IReadOnlyList<CompletenessScore> GetLowCompletenessSymbols(DateOnly date, double threshold = 0.8)
    {
        return GetScoresForDate(date)
            .Where(s => s.Score < threshold)
            .OrderBy(s => s.Score)
            .ToList();
    }

    /// <summary>
    /// Gets the overall completeness summary.
    /// </summary>
    public CompletenessSummary GetSummary()
    {
        var scores = GetAllScores();
        if (scores.Count == 0)
        {
            return new CompletenessSummary(
                TotalSymbolDates: 0,
                AverageScore: 0,
                MinScore: 0,
                MaxScore: 0,
                SymbolsTracked: 0,
                DatesTracked: 0,
                TotalEvents: 0,
                TotalExpectedEvents: 0,
                OverallCoverage: 0,
                GradeDistribution: new Dictionary<string, int>(),
                CalculatedAt: DateTimeOffset.UtcNow
            );
        }

        var gradeDistribution = scores
            .GroupBy(s => s.Grade)
            .ToDictionary(g => g.Key, g => g.Count());

        return new CompletenessSummary(
            TotalSymbolDates: scores.Count,
            AverageScore: scores.Average(s => s.Score),
            MinScore: scores.Min(s => s.Score),
            MaxScore: scores.Max(s => s.Score),
            SymbolsTracked: scores.Select(s => s.Symbol).Distinct().Count(),
            DatesTracked: scores.Select(s => s.Date).Distinct().Count(),
            TotalEvents: scores.Sum(s => s.ActualEvents),
            TotalExpectedEvents: scores.Sum(s => s.ExpectedEvents),
            OverallCoverage: scores.Sum(s => s.ActualEvents) / (double)Math.Max(1, scores.Sum(s => s.ExpectedEvents)),
            GradeDistribution: gradeDistribution,
            CalculatedAt: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Sets the expected events per hour for a specific symbol (for high-volume symbols).
    /// </summary>
    public void SetExpectedEventsPerHour(string symbol, long expectedPerHour)
    {
        foreach (var state in _states.Values.Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
        {
            state.SetExpectedEventsPerHour(expectedPerHour);
        }
    }

    /// <summary>
    /// Resets tracking for a specific symbol/date.
    /// </summary>
    public void Reset(string symbol, DateOnly date)
    {
        var key = GetKey(symbol, date);
        _states.TryRemove(key, out _);
    }

    /// <summary>
    /// Resets all tracking.
    /// </summary>
    public void ResetAll()
    {
        _states.Clear();
        _log.Information("CompletenessScoreCalculator reset all tracking data");
    }

    private static string GetKey(string symbol, DateOnly date) => $"{symbol.ToUpperInvariant()}:{date:yyyy-MM-dd}";

    private void CleanupOldStates(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_config.RetentionDays));
            var toRemove = _states.Keys
                .Where(k => DateOnly.TryParse(k.Split(':').Last(), out var date) && date < cutoff)
                .ToList();

            foreach (var key in toRemove)
            {
                _states.TryRemove(key, out _);
            }

            if (toRemove.Count > 0)
            {
                _log.Debug("Cleaned up {Count} old completeness states", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during completeness score cleanup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cleanupTimer.Dispose();
        _states.Clear();
    }

    /// <summary>
    /// Per-symbol/date tracking state.
    /// </summary>
    private sealed class SymbolDateState
    {
        private readonly object _lock = new();
        private readonly CompletenessConfig _config;
        private DateTimeOffset _firstEvent = DateTimeOffset.MaxValue;
        private DateTimeOffset _lastEvent = DateTimeOffset.MinValue;
        private long _eventCount;
        private long _expectedEventsPerHour;
        private readonly HashSet<int> _coveredMinutes = new();

        public string Symbol { get; }
        public DateOnly Date { get; }

        public SymbolDateState(string symbol, DateOnly date, CompletenessConfig config)
        {
            Symbol = symbol;
            Date = date;
            _config = config;
            _expectedEventsPerHour = config.ExpectedEventsPerHour;
        }

        public void RecordEvent(DateTimeOffset timestamp, string eventType)
        {
            lock (_lock)
            {
                _eventCount++;

                if (timestamp < _firstEvent)
                    _firstEvent = timestamp;
                if (timestamp > _lastEvent)
                    _lastEvent = timestamp;

                // Track which minutes have data
                var minuteOfDay = timestamp.Hour * 60 + timestamp.Minute;
                _coveredMinutes.Add(minuteOfDay);
            }
        }

        public void SetExpectedEventsPerHour(long expectedPerHour)
        {
            lock (_lock)
            {
                _expectedEventsPerHour = expectedPerHour;
            }
        }

        public CompletenessScore CalculateScore()
        {
            lock (_lock)
            {
                var tradingStart = new TimeOnly(_config.TradingStartHour, _config.TradingStartMinute);
                var tradingEnd = new TimeOnly(_config.TradingEndHour, _config.TradingEndMinute);
                var tradingDuration = tradingEnd - tradingStart;
                var tradingMinutes = (int)tradingDuration.TotalMinutes;

                // Calculate expected events based on trading hours
                var tradingHours = tradingDuration.TotalHours;
                var expectedEvents = (long)(tradingHours * _expectedEventsPerHour);

                // Calculate covered duration based on minutes with data
                var coveredMinutesInTradingHours = _coveredMinutes
                    .Count(m => m >= tradingStart.Hour * 60 + tradingStart.Minute &&
                                m <= tradingEnd.Hour * 60 + tradingEnd.Minute);
                var coveredDuration = TimeSpan.FromMinutes(coveredMinutesInTradingHours);

                // Calculate coverage percentage
                var coveragePercent = tradingMinutes > 0
                    ? (double)coveredMinutesInTradingHours / tradingMinutes * 100
                    : 0;

                // Calculate completeness score
                double score;
                if (expectedEvents == 0)
                {
                    score = _eventCount > 0 ? 1.0 : 0.0;
                }
                else
                {
                    // Blend event count completeness with time coverage
                    var eventScore = Math.Min(1.0, (double)_eventCount / expectedEvents);
                    var coverageScore = coveragePercent / 100.0;
                    score = (eventScore * 0.7) + (coverageScore * 0.3);
                }

                return new CompletenessScore(
                    Symbol: Symbol,
                    Date: Date,
                    Score: Math.Round(score, 4),
                    ExpectedEvents: expectedEvents,
                    ActualEvents: _eventCount,
                    MissingEvents: Math.Max(0, expectedEvents - _eventCount),
                    TradingDuration: tradingDuration,
                    CoveredDuration: coveredDuration,
                    CoveragePercent: Math.Round(coveragePercent, 2),
                    CalculatedAt: DateTimeOffset.UtcNow
                );
            }
        }
    }
}

/// <summary>
/// Configuration for completeness scoring.
/// </summary>
public sealed record CompletenessConfig
{
    /// <summary>
    /// Expected events per hour for average symbols.
    /// </summary>
    public long ExpectedEventsPerHour { get; init; } = 1000;

    /// <summary>
    /// Trading day start hour (UTC).
    /// </summary>
    public int TradingStartHour { get; init; } = 13; // 9:30 AM ET = 13:30 UTC

    /// <summary>
    /// Trading day start minute.
    /// </summary>
    public int TradingStartMinute { get; init; } = 30;

    /// <summary>
    /// Trading day end hour (UTC).
    /// </summary>
    public int TradingEndHour { get; init; } = 20; // 4:00 PM ET = 20:00 UTC

    /// <summary>
    /// Trading day end minute.
    /// </summary>
    public int TradingEndMinute { get; init; } = 0;

    /// <summary>
    /// Number of days to retain historical completeness data.
    /// </summary>
    public int RetentionDays { get; init; } = 30;

    public static CompletenessConfig Default => new();
}

/// <summary>
/// Summary of completeness across all tracked data.
/// </summary>
public sealed record CompletenessSummary(
    int TotalSymbolDates,
    double AverageScore,
    double MinScore,
    double MaxScore,
    int SymbolsTracked,
    int DatesTracked,
    long TotalEvents,
    long TotalExpectedEvents,
    double OverallCoverage,
    Dictionary<string, int> GradeDistribution,
    DateTimeOffset CalculatedAt
);
