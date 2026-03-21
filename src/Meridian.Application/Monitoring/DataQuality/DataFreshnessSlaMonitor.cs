using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Enums;
using Serilog;

namespace Meridian.Application.Monitoring.DataQuality;

/// <summary>
/// Configuration for Data Freshness SLA monitoring (ADQ-4.1).
/// </summary>
public sealed record SlaConfig
{
    /// <summary>
    /// Default freshness threshold in seconds. Data older than this triggers a warning.
    /// </summary>
    public int DefaultFreshnessThresholdSeconds { get; init; } = 60;

    /// <summary>
    /// Critical freshness threshold in seconds. Data older than this triggers an SLA violation.
    /// </summary>
    public int CriticalFreshnessThresholdSeconds { get; init; } = 300;

    /// <summary>
    /// How often to check for SLA violations (in seconds).
    /// </summary>
    public int CheckIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// Per-symbol freshness overrides. Key is symbol, value is threshold in seconds.
    /// </summary>
    public Dictionary<string, int> SymbolThresholds { get; init; } = new();

    /// <summary>
    /// Whether to skip SLA checks during market closed hours.
    /// </summary>
    public bool SkipOutsideMarketHours { get; init; } = true;

    /// <summary>
    /// Market open time (UTC).
    /// </summary>
    public TimeOnly MarketOpenUtc { get; init; } = new(13, 30); // 9:30 AM ET

    /// <summary>
    /// Market close time (UTC).
    /// </summary>
    public TimeOnly MarketCloseUtc { get; init; } = new(20, 0); // 4:00 PM ET

    /// <summary>
    /// Cooldown period between alerts for the same symbol (in seconds).
    /// </summary>
    public int AlertCooldownSeconds { get; init; } = 300;

    public static SlaConfig Default => new();

    /// <summary>
    /// Gets the freshness threshold for a specific symbol.
    /// </summary>
    public int GetThresholdForSymbol(string symbol)
    {
        return SymbolThresholds.TryGetValue(symbol, out var threshold)
            ? threshold
            : DefaultFreshnessThresholdSeconds;
    }
}

/// <summary>
/// SLA status for a single symbol.
/// </summary>
public sealed record SymbolSlaStatus(
    string Symbol,
    DateTimeOffset LastEventTime,
    TimeSpan TimeSinceLastEvent,
    double FreshnessMs,
    SlaState State,
    int ThresholdSeconds,
    int ViolationCount,
    DateTimeOffset? LastViolationTime,
    bool IsWithinMarketHours
);

/// <summary>
/// SLA state enumeration.
/// </summary>
public enum SlaState : byte
{
    /// <summary>Data is fresh and within SLA.</summary>
    Healthy,
    /// <summary>Data is approaching staleness threshold.</summary>
    Warning,
    /// <summary>Data has exceeded the freshness threshold - SLA violation.</summary>
    Violation,
    /// <summary>No data has been received yet.</summary>
    NoData,
    /// <summary>Outside market hours, SLA not applicable.</summary>
    OutsideMarketHours
}

/// <summary>
/// Overall SLA status snapshot.
/// </summary>
public sealed record SlaStatusSnapshot(
    DateTimeOffset Timestamp,
    int TotalSymbols,
    int HealthySymbols,
    int WarningSymbols,
    int ViolationSymbols,
    int NoDataSymbols,
    long TotalViolations,
    double OverallFreshnessScore,
    bool IsMarketOpen,
    IReadOnlyList<SymbolSlaStatus> SymbolStatuses
);

/// <summary>
/// Event raised when an SLA violation occurs.
/// </summary>
public readonly record struct SlaViolationEvent(
    string Symbol,
    DateTimeOffset Timestamp,
    TimeSpan TimeSinceLastEvent,
    int ThresholdSeconds,
    int ViolationCount,
    SlaState PreviousState
);

/// <summary>
/// Event raised when SLA recovers from violation.
/// </summary>
public readonly record struct SlaRecoveryEvent(
    string Symbol,
    DateTimeOffset Timestamp,
    TimeSpan ViolationDuration,
    int ViolationCount
);

/// <summary>
/// Monitors data freshness and tracks SLA compliance (ADQ-4.2, ADQ-4.3, ADQ-4.4).
/// Tracks last event timestamp per symbol and alerts on data delivery delays.
/// </summary>
public sealed class DataFreshnessSlaMonitor : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<DataFreshnessSlaMonitor>();
    private readonly ConcurrentDictionary<string, SymbolFreshnessState> _symbolStates = new();
    private readonly SlaConfig _config;
    private readonly Timer _checkTimer;
    private volatile bool _isDisposed;

    // Metrics (ADQ-4.5)
    private long _totalViolations;
    private long _totalRecoveries;
    private long _currentViolations;

    /// <summary>
    /// Event raised when an SLA violation is detected.
    /// </summary>
    public event Action<SlaViolationEvent>? OnViolation;

    /// <summary>
    /// Event raised when SLA recovers from a violation.
    /// </summary>
    public event Action<SlaRecoveryEvent>? OnRecovery;

    public DataFreshnessSlaMonitor(SlaConfig? config = null)
    {
        _config = config ?? SlaConfig.Default;

        _checkTimer = new Timer(
            CheckSlaCompliance,
            null,
            TimeSpan.FromSeconds(_config.CheckIntervalSeconds),
            TimeSpan.FromSeconds(_config.CheckIntervalSeconds));

        _log.Information(
            "DataFreshnessSlaMonitor initialized with default threshold {Threshold}s, critical {Critical}s",
            _config.DefaultFreshnessThresholdSeconds,
            _config.CriticalFreshnessThresholdSeconds);
    }

    /// <summary>
    /// Records that an event was received for a symbol (ADQ-4.3).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordEvent(string symbol)
    {
        var state = _symbolStates.GetOrAdd(symbol, s => new SymbolFreshnessState(s, _config.GetThresholdForSymbol(s)));
        state.RecordEvent();

        // Check if this recovers from violation
        if (state.CurrentState == SlaState.Violation)
        {
            var violationDuration = DateTimeOffset.UtcNow - (state.LastViolationTime ?? DateTimeOffset.UtcNow);
            state.MarkRecovered();
            Interlocked.Decrement(ref _currentViolations);
            Interlocked.Increment(ref _totalRecoveries);

            _log.Information("SLA recovered for {Symbol} after {Duration:F1}s violation", symbol, violationDuration.TotalSeconds);

            try
            {
                OnRecovery?.Invoke(new SlaRecoveryEvent(symbol, DateTimeOffset.UtcNow, violationDuration, state.ViolationCount));
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in SLA recovery event handler");
            }
        }
    }

    /// <summary>
    /// Registers a symbol for SLA monitoring.
    /// </summary>
    public void RegisterSymbol(string symbol, int? customThresholdSeconds = null)
    {
        var threshold = customThresholdSeconds ?? _config.GetThresholdForSymbol(symbol);
        _symbolStates.GetOrAdd(symbol, s => new SymbolFreshnessState(s, threshold));
        _log.Debug("Registered symbol {Symbol} for SLA monitoring with threshold {Threshold}s", symbol, threshold);
    }

    /// <summary>
    /// Registers a symbol for SLA monitoring with a threshold derived from its liquidity profile.
    /// Illiquid symbols receive longer freshness thresholds to avoid false SLA violations.
    /// </summary>
    public void RegisterSymbolLiquidity(string symbol, LiquidityProfile profile)
    {
        var thresholds = LiquidityProfileProvider.GetThresholds(profile);
        var freshnessThreshold = thresholds.FreshnessThresholdSeconds;

        // Remove and re-add to update the threshold
        _symbolStates.TryRemove(symbol, out _);
        _symbolStates.GetOrAdd(symbol, s => new SymbolFreshnessState(s, freshnessThreshold));

        _log.Debug("Registered symbol {Symbol} with liquidity profile {Profile} (SLA threshold: {Threshold}s)",
            symbol, profile, freshnessThreshold);
    }

    /// <summary>
    /// Unregisters a symbol from SLA monitoring.
    /// </summary>
    public void UnregisterSymbol(string symbol)
    {
        if (_symbolStates.TryRemove(symbol, out _))
        {
            _log.Debug("Unregistered symbol {Symbol} from SLA monitoring", symbol);
        }
    }

    /// <summary>
    /// Gets the SLA status for a specific symbol.
    /// </summary>
    public SymbolSlaStatus? GetSymbolStatus(string symbol)
    {
        return _symbolStates.TryGetValue(symbol, out var state)
            ? state.GetStatus(IsMarketOpen())
            : null;
    }

    /// <summary>
    /// Gets the overall SLA status snapshot (ADQ-4.6).
    /// </summary>
    public SlaStatusSnapshot GetSnapshot()
    {
        var isMarketOpen = IsMarketOpen();
        var statuses = new List<SymbolSlaStatus>();

        int healthy = 0, warning = 0, violation = 0, noData = 0;
        double totalFreshness = 0;
        int freshnessCount = 0;

        foreach (var kvp in _symbolStates)
        {
            var status = kvp.Value.GetStatus(isMarketOpen);
            statuses.Add(status);

            switch (status.State)
            {
                case SlaState.Healthy:
                    healthy++;
                    totalFreshness += 100;
                    freshnessCount++;
                    break;
                case SlaState.Warning:
                    warning++;
                    // Score between 50-100 based on how close to threshold
                    var warningScore = 100 - (status.FreshnessMs / (status.ThresholdSeconds * 1000) * 50);
                    totalFreshness += Math.Max(50, warningScore);
                    freshnessCount++;
                    break;
                case SlaState.Violation:
                    violation++;
                    // Score between 0-50 based on severity
                    var violationScore = 50 - Math.Min(50, (status.FreshnessMs - status.ThresholdSeconds * 1000) / (status.ThresholdSeconds * 1000) * 50);
                    totalFreshness += Math.Max(0, violationScore);
                    freshnessCount++;
                    break;
                case SlaState.NoData:
                    noData++;
                    break;
                case SlaState.OutsideMarketHours:
                    // Don't count in freshness score
                    break;
            }
        }

        var overallScore = freshnessCount > 0 ? totalFreshness / freshnessCount : 100;

        return new SlaStatusSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            TotalSymbols: statuses.Count,
            HealthySymbols: healthy,
            WarningSymbols: warning,
            ViolationSymbols: violation,
            NoDataSymbols: noData,
            TotalViolations: Interlocked.Read(ref _totalViolations),
            OverallFreshnessScore: overallScore,
            IsMarketOpen: isMarketOpen,
            SymbolStatuses: statuses
        );
    }

    /// <summary>
    /// Gets total violation count for metrics.
    /// </summary>
    public long TotalViolations => Interlocked.Read(ref _totalViolations);

    /// <summary>
    /// Gets current active violation count for metrics.
    /// </summary>
    public long CurrentViolations => Interlocked.Read(ref _currentViolations);

    /// <summary>
    /// Gets total recovery count for metrics.
    /// </summary>
    public long TotalRecoveries => Interlocked.Read(ref _totalRecoveries);

    /// <summary>
    /// Checks if the market is currently open.
    /// </summary>
    public bool IsMarketOpen()
    {
        if (!_config.SkipOutsideMarketHours)
            return true;

        var now = TimeOnly.FromDateTime(DateTime.UtcNow);
        var dayOfWeek = DateTime.UtcNow.DayOfWeek;

        // Skip weekends
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
            return false;

        return now >= _config.MarketOpenUtc && now <= _config.MarketCloseUtc;
    }

    private void CheckSlaCompliance(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var now = DateTimeOffset.UtcNow;
            var isMarketOpen = IsMarketOpen();

            foreach (var kvp in _symbolStates)
            {
                var symbolState = kvp.Value;

                // Skip checks outside market hours if configured
                if (!isMarketOpen && _config.SkipOutsideMarketHours)
                {
                    continue;
                }

                var status = symbolState.GetStatus(isMarketOpen);

                // Check for new violation (ADQ-4.4)
                if (status.State == SlaState.Violation && symbolState.CurrentState != SlaState.Violation)
                {
                    // Check cooldown
                    if (symbolState.LastAlertTime.HasValue &&
                        (now - symbolState.LastAlertTime.Value).TotalSeconds < _config.AlertCooldownSeconds)
                    {
                        continue;
                    }

                    var previousState = symbolState.CurrentState;
                    symbolState.MarkViolation();
                    Interlocked.Increment(ref _totalViolations);
                    Interlocked.Increment(ref _currentViolations);

                    _log.Warning(
                        "SLA violation for {Symbol}: {Freshness:F1}s since last event (threshold: {Threshold}s)",
                        kvp.Key,
                        status.TimeSinceLastEvent.TotalSeconds,
                        status.ThresholdSeconds);

                    try
                    {
                        OnViolation?.Invoke(new SlaViolationEvent(
                            kvp.Key,
                            now,
                            status.TimeSinceLastEvent,
                            status.ThresholdSeconds,
                            symbolState.ViolationCount,
                            previousState));
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error in SLA violation event handler");
                    }
                }
                else if (status.State == SlaState.Warning && symbolState.CurrentState == SlaState.Healthy)
                {
                    symbolState.UpdateState(SlaState.Warning);
                    _log.Debug(
                        "SLA warning for {Symbol}: {Freshness:F1}s since last event (threshold: {Threshold}s)",
                        kvp.Key,
                        status.TimeSinceLastEvent.TotalSeconds,
                        status.ThresholdSeconds);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error during SLA compliance check");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _checkTimer.Dispose();
        _symbolStates.Clear();
    }

    /// <summary>
    /// Internal state for tracking a single symbol's freshness.
    /// </summary>
    private sealed class SymbolFreshnessState
    {
        public string Symbol { get; }
        public int ThresholdSeconds { get; }

        private DateTimeOffset? _lastEventTime;
        private DateTimeOffset? _lastViolationTime;
        private DateTimeOffset? _lastAlertTime;
        private SlaState _currentState = SlaState.NoData;
        private int _violationCount;

        public SlaState CurrentState => _currentState;
        public DateTimeOffset? LastViolationTime => _lastViolationTime;
        public DateTimeOffset? LastAlertTime => _lastAlertTime;
        public int ViolationCount => _violationCount;

        public SymbolFreshnessState(string symbol, int thresholdSeconds)
        {
            Symbol = symbol;
            ThresholdSeconds = thresholdSeconds;
        }

        public void RecordEvent()
        {
            _lastEventTime = DateTimeOffset.UtcNow;
            if (_currentState == SlaState.NoData || _currentState == SlaState.Warning)
            {
                _currentState = SlaState.Healthy;
            }
        }

        public void MarkViolation()
        {
            _currentState = SlaState.Violation;
            _lastViolationTime = DateTimeOffset.UtcNow;
            _lastAlertTime = DateTimeOffset.UtcNow;
            Interlocked.Increment(ref _violationCount);
        }

        public void MarkRecovered()
        {
            _currentState = SlaState.Healthy;
        }

        public void UpdateState(SlaState newState)
        {
            _currentState = newState;
        }

        public SymbolSlaStatus GetStatus(bool isMarketOpen)
        {
            var now = DateTimeOffset.UtcNow;

            if (!_lastEventTime.HasValue)
            {
                return new SymbolSlaStatus(
                    Symbol: Symbol,
                    LastEventTime: DateTimeOffset.MinValue,
                    TimeSinceLastEvent: TimeSpan.MaxValue,
                    FreshnessMs: double.MaxValue,
                    State: SlaState.NoData,
                    ThresholdSeconds: ThresholdSeconds,
                    ViolationCount: _violationCount,
                    LastViolationTime: _lastViolationTime,
                    IsWithinMarketHours: isMarketOpen
                );
            }

            var timeSince = now - _lastEventTime.Value;
            var freshnessMs = timeSince.TotalMilliseconds;

            SlaState state;
            if (!isMarketOpen)
            {
                state = SlaState.OutsideMarketHours;
            }
            else if (timeSince.TotalSeconds > ThresholdSeconds)
            {
                state = SlaState.Violation;
            }
            else if (timeSince.TotalSeconds > ThresholdSeconds * 0.7) // 70% of threshold = warning
            {
                state = SlaState.Warning;
            }
            else
            {
                state = SlaState.Healthy;
            }

            return new SymbolSlaStatus(
                Symbol: Symbol,
                LastEventTime: _lastEventTime.Value,
                TimeSinceLastEvent: timeSince,
                FreshnessMs: freshnessMs,
                State: state,
                ThresholdSeconds: ThresholdSeconds,
                ViolationCount: _violationCount,
                LastViolationTime: _lastViolationTime,
                IsWithinMarketHours: isMarketOpen
            );
        }
    }
}
