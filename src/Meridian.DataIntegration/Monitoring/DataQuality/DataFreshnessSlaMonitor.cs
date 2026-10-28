using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Meridian.Core.Logging;
using Meridian.Contracts.Domain.Enums;
using Serilog;

namespace Meridian.DataIntegration.Monitoring.DataQuality;

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
    private sealed record SymbolScope(ConcurrentDictionary<string, SymbolFreshnessState> States, bool IsManaged);
    private volatile SymbolScope _scope = new(new(StringComparer.OrdinalIgnoreCase), false);
    private ConcurrentDictionary<string, SymbolFreshnessState> SymbolStates => _scope.States;
    private readonly object _scopeLock = new();
    private readonly SlaConfig _config;
    private readonly ITimer _checkTimer;
    private readonly TimeProvider _timeProvider;
    private volatile bool _isDisposed;

    // Metrics (ADQ-4.5)
    private long _totalViolations;
    private long _totalRecoveries;

    /// <summary>
    /// Event raised when an SLA violation is detected.
    /// </summary>
    public event Action<SlaViolationEvent>? OnViolation;

    /// <summary>
    /// Event raised when SLA recovers from a violation.
    /// </summary>
    public event Action<SlaRecoveryEvent>? OnRecovery;

    /// <summary>
    /// Publishes a fresh snapshot after every periodic compliance check, including quiet feeds
    /// and closed market sessions. Subscribers can export metrics without waiting for an event.
    /// </summary>
    public event Action<SlaStatusSnapshot>? OnSnapshot;

    public DataFreshnessSlaMonitor(SlaConfig? config = null, TimeProvider? timeProvider = null)
    {
        _config = config ?? SlaConfig.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _checkTimer = _timeProvider.CreateTimer(
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
        if (_isDisposed)
            return;
        var scope = _scope;
        var states = scope.States;
        SymbolFreshnessState? state;
        if (scope.IsManaged)
        {
            // Late provider callbacks must not recreate a deliberately removed subscription.
            if (!states.TryGetValue(symbol, out state))
                return;
        }
        else
        {
            state = states.GetOrAdd(symbol, s => new SymbolFreshnessState(s, _config.GetThresholdForSymbol(s), _timeProvider));
        }
        SlaRecoveryEvent? recovery = null;
        lock (state)
        {
            state.RecordEvent();
            if (state.CurrentState == SlaState.Violation)
            {
                var now = _timeProvider.GetUtcNow();
                var duration = now - (state.LastViolationTime ?? now);
                state.MarkRecovered();
                Interlocked.Increment(ref _totalRecoveries);
                recovery = new SlaRecoveryEvent(symbol, now, duration, state.ViolationCount);
            }
        }

        if (recovery is { } recovered)
        {
            _log.Information("SLA recovered for {Symbol} after {Duration:F1}s violation", symbol, recovered.ViolationDuration.TotalSeconds);
            try
            {
                OnRecovery?.Invoke(recovered);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error in SLA recovery event handler");
            }
        }
    }

    /// <summary>
    /// Replaces the managed subscription scope, preserving receipt history for retained symbols.
    /// Once a scope is supplied, events for other symbols cannot implicitly register themselves.
    /// </summary>
    public void SynchronizeSymbols(IEnumerable<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        lock (_scopeLock)
        {
            var current = SymbolStates;
            var next = new ConcurrentDictionary<string, SymbolFreshnessState>(StringComparer.OrdinalIgnoreCase);
            foreach (var symbol in symbols.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                next[symbol] = current.TryGetValue(symbol, out var state)
                    ? state
                    : new SymbolFreshnessState(symbol, _config.GetThresholdForSymbol(symbol), _timeProvider);
            }
            // In-flight events can finish against the retired dictionary, but cannot resurrect
            // removed symbols in the new scope or contribute to its snapshots.
            _scope = new SymbolScope(next, true);
        }
    }

    /// <summary>
    /// Registers a symbol for SLA monitoring.
    /// </summary>
    public void RegisterSymbol(string symbol, int? customThresholdSeconds = null)
    {
        var threshold = customThresholdSeconds ?? _config.GetThresholdForSymbol(symbol);
        SymbolStates.GetOrAdd(symbol, s => new SymbolFreshnessState(s, threshold, _timeProvider));
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
        SymbolStates.TryRemove(symbol, out _);
        SymbolStates.GetOrAdd(symbol, s => new SymbolFreshnessState(s, freshnessThreshold, _timeProvider));

        _log.Debug("Registered symbol {Symbol} with liquidity profile {Profile} (SLA threshold: {Threshold}s)",
            symbol, profile, freshnessThreshold);
    }

    /// <summary>
    /// Unregisters a symbol from SLA monitoring.
    /// </summary>
    public void UnregisterSymbol(string symbol)
    {
        if (SymbolStates.TryRemove(symbol, out _))
        {
            _log.Debug("Unregistered symbol {Symbol} from SLA monitoring", symbol);
        }
    }

    /// <summary>
    /// Gets the SLA status for a specific symbol.
    /// </summary>
    public SymbolSlaStatus? GetSymbolStatus(string symbol)
    {
        return SymbolStates.TryGetValue(symbol, out var state)
            ? state.GetStatus(IsMarketOpen(), GetCurrentSessionStart())
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

        foreach (var kvp in SymbolStates)
        {
            var status = kvp.Value.GetStatus(isMarketOpen, GetCurrentSessionStart());
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
            Timestamp: _timeProvider.GetUtcNow(),
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
    public long CurrentViolations => GetSnapshot().ViolationSymbols;

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

        var utcNow = _timeProvider.GetUtcNow();
        var now = TimeOnly.FromDateTime(utcNow.UtcDateTime);
        var dayOfWeek = utcNow.DayOfWeek;

        // Skip weekends
        if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
            return false;

        return now >= _config.MarketOpenUtc && now <= _config.MarketCloseUtc;
    }

    private DateTimeOffset? GetCurrentSessionStart()
    {
        if (!_config.SkipOutsideMarketHours)
            return null;
        var date = _timeProvider.GetUtcNow().UtcDateTime.Date;
        return new DateTimeOffset(date + _config.MarketOpenUtc.ToTimeSpan(), TimeSpan.Zero);
    }

    private void CheckSlaCompliance(object? state)
    {
        if (_isDisposed)
            return;

        try
        {
            var now = _timeProvider.GetUtcNow();
            var isMarketOpen = IsMarketOpen();

            foreach (var kvp in SymbolStates)
            {
                var symbolState = kvp.Value;

                // Skip checks outside market hours if configured
                if (!isMarketOpen && _config.SkipOutsideMarketHours)
                {
                    continue;
                }

                SlaViolationEvent? violation = null;
                lock (symbolState)
                {
                    var status = symbolState.GetStatus(isMarketOpen, GetCurrentSessionStart());
                    if (status.State == SlaState.Violation && symbolState.CurrentState != SlaState.Violation)
                    {
                        if (symbolState.LastAlertTime.HasValue &&
                            (now - symbolState.LastAlertTime.Value).TotalSeconds < _config.AlertCooldownSeconds)
                        {
                            continue;
                        }

                        var previousState = symbolState.CurrentState;
                        symbolState.MarkViolation();
                        Interlocked.Increment(ref _totalViolations);
                        violation = new SlaViolationEvent(kvp.Key, now, status.TimeSinceLastEvent,
                            status.ThresholdSeconds, symbolState.ViolationCount, previousState);
                    }
                    else if (status.State == SlaState.Warning && symbolState.CurrentState == SlaState.Healthy)
                    {
                        symbolState.UpdateState(SlaState.Warning);
                    }
                }

                if (violation is { } detected)
                {
                    _log.Warning("SLA violation for {Symbol}: {Freshness:F1}s since last event (threshold: {Threshold}s)",
                        detected.Symbol, detected.TimeSinceLastEvent.TotalSeconds, detected.ThresholdSeconds);
                    try
                    {
                        OnViolation?.Invoke(detected);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error in SLA violation event handler");
                    }
                }
            }

            OnSnapshot?.Invoke(GetSnapshot());
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
        SymbolStates.Clear();
    }

    /// <summary>
    /// Internal state for tracking a single symbol's freshness.
    /// </summary>
    private sealed class SymbolFreshnessState
    {
        public string Symbol { get; }
        public int ThresholdSeconds { get; }

        private readonly TimeProvider _timeProvider;
        private readonly DateTimeOffset _registeredAt;
        private DateTimeOffset? _lastEventTime;
        private DateTimeOffset? _lastViolationTime;
        private DateTimeOffset? _lastAlertTime;
        private SlaState _currentState = SlaState.NoData;
        private int _violationCount;

        public SlaState CurrentState => _currentState;
        public DateTimeOffset? LastViolationTime => _lastViolationTime;
        public DateTimeOffset? LastAlertTime => _lastAlertTime;
        public int ViolationCount => _violationCount;

        public SymbolFreshnessState(string symbol, int thresholdSeconds, TimeProvider timeProvider)
        {
            Symbol = symbol;
            ThresholdSeconds = thresholdSeconds;
            _timeProvider = timeProvider;
            _registeredAt = timeProvider.GetUtcNow();
        }

        public void RecordEvent()
        {
            _lastEventTime = _timeProvider.GetUtcNow();
            if (_currentState == SlaState.NoData || _currentState == SlaState.Warning)
            {
                _currentState = SlaState.Healthy;
            }
        }

        public void MarkViolation()
        {
            _currentState = SlaState.Violation;
            _lastViolationTime = _timeProvider.GetUtcNow();
            _lastAlertTime = _timeProvider.GetUtcNow();
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

        public SymbolSlaStatus GetStatus(bool isMarketOpen, DateTimeOffset? sessionStart)
        {
            lock (this)
                return GetStatusCore(isMarketOpen, sessionStart);
        }

        private SymbolSlaStatus GetStatusCore(bool isMarketOpen, DateTimeOffset? sessionStart)
        {
            var now = _timeProvider.GetUtcNow();

            // A subscribed feed that never starts must age into a violation too. Give it
            // one normal threshold of grace, starting no earlier than this market session.
            var waitingSince = _registeredAt;
            if (sessionStart.HasValue && sessionStart.Value > waitingSince)
                waitingSince = sessionStart.Value;
            var timeSince = now - (_lastEventTime ?? waitingSince);
            if (timeSince < TimeSpan.Zero)
                timeSince = TimeSpan.Zero;
            var freshnessMs = timeSince.TotalMilliseconds;

            SlaState state;
            if (!isMarketOpen)
            {
                state = SlaState.OutsideMarketHours;
            }
            else if (!_lastEventTime.HasValue && timeSince.TotalSeconds <= ThresholdSeconds)
            {
                state = SlaState.NoData;
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
                LastEventTime: _lastEventTime ?? DateTimeOffset.MinValue,
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
