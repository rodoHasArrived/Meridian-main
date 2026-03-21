using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Application.Subscriptions.Models;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Models;
using Serilog;

namespace Meridian.Application.Subscriptions.Services;

/// <summary>
/// Auto-resubscribe policy that triggers resubscription when integrity events occur.
/// Implements rate limiting and circuit breaker pattern to prevent cascading failures.
///
/// Flow:
/// 1. Integrity event received → check rate limit → check circuit breaker → resubscribe
/// 2. If resubscription fails → increment failure count → possibly open circuit
/// 3. Circuit opens after threshold failures → waits break duration → half-open → test
/// </summary>
public sealed class AutoResubscribePolicy : IAsyncDisposable
{
    private readonly SubscriptionOrchestrator _subscriptionManager;
    private readonly ILogger _log;
    private readonly AutoResubscribeOptions _options;

    // Per-symbol rate limiting: tracks last resubscription attempt time
    private readonly ConcurrentDictionary<string, SymbolResubscribeState> _symbolStates = new(StringComparer.OrdinalIgnoreCase);

    // Global circuit breaker state
    private readonly object _circuitLock = new();
    private CircuitState _circuitState = CircuitState.Closed;
    private DateTimeOffset _circuitOpenedAt;
    private int _consecutiveFailures;
    private DateTimeOffset _lastHalfOpenTest;

    // Background cleanup task
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _cleanupTask;

    public AutoResubscribePolicy(
        SubscriptionOrchestrator subscriptionManager,
        AutoResubscribeOptions? options = null,
        ILogger? log = null)
    {
        _subscriptionManager = subscriptionManager ?? throw new ArgumentNullException(nameof(subscriptionManager));
        _options = options ?? new AutoResubscribeOptions();
        _log = log ?? LoggingSetup.ForContext<AutoResubscribePolicy>();

        _cleanupTask = CleanupLoopAsync();

        _log.Information(
            "AutoResubscribePolicy initialized: " +
            "cooldown={CooldownSeconds}s, minInterval={MinIntervalSeconds}s, " +
            "circuitThreshold={CircuitThreshold}, breakDuration={BreakDurationSeconds}s",
            _options.SymbolCooldown.TotalSeconds,
            _options.MinResubscribeInterval.TotalSeconds,
            _options.CircuitBreakerThreshold,
            _options.CircuitBreakerDuration.TotalSeconds);
    }

    /// <summary>
    /// Current state of the global circuit breaker.
    /// </summary>
    public CircuitState CurrentCircuitState
    {
        get { lock (_circuitLock) return _circuitState; }
    }

    /// <summary>
    /// Number of symbols currently in cooldown.
    /// </summary>
    public int SymbolsInCooldown => _symbolStates.Count(s => s.Value.IsInCooldown(_options.SymbolCooldown));

    /// <summary>
    /// Number of symbols with open circuit breakers.
    /// </summary>
    public int SymbolsWithOpenCircuit => _symbolStates.Count(s => s.Value.CircuitState == CircuitState.Open);

    /// <summary>
    /// Handles an integrity event and potentially triggers resubscription.
    /// This is the main entry point for the policy.
    /// </summary>
    /// <param name="symbol">The symbol that experienced the integrity event.</param>
    /// <param name="severity">Severity of the integrity event.</param>
    /// <param name="config">Current symbol configuration for resubscription.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if resubscription was triggered, false if skipped.</returns>
    public Task<bool> OnIntegrityEventAsync(
        string symbol,
        IntegritySeverity severity,
        AppConfig config,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Task.FromResult(false);

        symbol = symbol.Trim();

        // Only auto-resubscribe for Error-level integrity events
        if (severity < _options.MinSeverityForResubscribe)
        {
            _log.Debug("Skipping resubscription for {Symbol}: severity {Severity} below threshold",
                symbol, severity);
            return Task.FromResult(false);
        }

        // Check global circuit breaker
        if (!CanProceedGlobalCircuit())
        {
            _log.Warning("Global circuit breaker OPEN. Skipping resubscription for {Symbol}", symbol);
            ResubscriptionMetrics.IncRateLimitedSkip();
            return Task.FromResult(false);
        }

        // Get or create symbol state
        var state = _symbolStates.GetOrAdd(symbol, _ => new SymbolResubscribeState());

        // Check per-symbol rate limiting
        if (!state.CanResubscribe(_options.SymbolCooldown, _options.MinResubscribeInterval))
        {
            _log.Debug("Rate limited: skipping resubscription for {Symbol} (cooldown or interval)", symbol);
            ResubscriptionMetrics.IncRateLimitedSkip();
            return Task.FromResult(false);
        }

        // Check per-symbol circuit breaker
        if (!state.CanProceedCircuit(_options.SymbolCircuitBreakerDuration))
        {
            _log.Debug("Symbol circuit breaker OPEN for {Symbol}. Skipping resubscription.", symbol);
            ResubscriptionMetrics.IncRateLimitedSkip();
            return Task.FromResult(false);
        }

        // Attempt resubscription
        var sw = Stopwatch.StartNew();
        ResubscriptionMetrics.IncResubscribeAttempt();
        state.RecordAttempt();

        try
        {
            _log.Information("Triggering auto-resubscribe for {Symbol} due to integrity event", symbol);

            // Force unsubscribe and resubscribe through the manager
            _subscriptionManager.Apply(config);

            sw.Stop();
            var elapsedMs = sw.ElapsedMilliseconds;

            state.RecordSuccess();
            ResubscriptionMetrics.IncResubscribeSuccess(elapsedMs);
            OnGlobalSuccess();

            _log.Information("Auto-resubscribe succeeded for {Symbol} in {ElapsedMs}ms", symbol, elapsedMs);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            sw.Stop();
            state.RecordFailure(_options.SymbolCircuitBreakerThreshold);
            ResubscriptionMetrics.IncResubscribeFailure();
            OnGlobalFailure();

            _log.Error(ex, "Auto-resubscribe failed for {Symbol}", symbol);
            return Task.FromResult(false);
        }
        finally
        {
            UpdateMetricsGauges();
        }
    }

    /// <summary>
    /// Handles a depth integrity event.
    /// </summary>
    public Task<bool> OnDepthIntegrityEventAsync(
        DepthIntegrityEvent evt,
        AppConfig config,
        CancellationToken ct = default)
    {
        // Map DepthIntegrityKind to a severity-like decision
        var shouldResubscribe = evt.Kind switch
        {
            DepthIntegrityKind.Gap => true,
            DepthIntegrityKind.OutOfOrder => true,
            DepthIntegrityKind.Stale => true,
            DepthIntegrityKind.InvalidPosition => true,
            _ => false
        };

        if (!shouldResubscribe)
            return Task.FromResult(false);

        return OnIntegrityEventAsync(evt.Symbol, IntegritySeverity.Error, config, ct);
    }

    /// <summary>
    /// Handles a trade integrity event.
    /// </summary>
    public Task<bool> OnTradeIntegrityEventAsync(
        IntegrityEvent evt,
        AppConfig config,
        CancellationToken ct = default)
    {
        return OnIntegrityEventAsync(evt.Symbol, evt.Severity, config, ct);
    }

    /// <summary>
    /// Manually resets the circuit breaker to closed state.
    /// Use with caution.
    /// </summary>
    public void ResetCircuitBreaker()
    {
        lock (_circuitLock)
        {
            _circuitState = CircuitState.Closed;
            _consecutiveFailures = 0;
            ResubscriptionMetrics.IncCircuitBreakerClose();
        }
        _log.Information("Global circuit breaker manually reset to CLOSED");
    }

    /// <summary>
    /// Resets the circuit breaker for a specific symbol.
    /// </summary>
    public void ResetSymbolCircuit(string symbol)
    {
        if (_symbolStates.TryGetValue(symbol, out var state))
        {
            state.ResetCircuit();
            _log.Information("Symbol circuit breaker reset for {Symbol}", symbol);
        }
    }

    private bool CanProceedGlobalCircuit()
    {
        lock (_circuitLock)
        {
            switch (_circuitState)
            {
                case CircuitState.Closed:
                    return true;

                case CircuitState.Open:
                    // Check if break duration has passed
                    if (DateTimeOffset.UtcNow - _circuitOpenedAt >= _options.CircuitBreakerDuration)
                    {
                        _circuitState = CircuitState.HalfOpen;
                        _lastHalfOpenTest = DateTimeOffset.UtcNow;
                        ResubscriptionMetrics.IncCircuitBreakerHalfOpen();
                        _log.Information("Global circuit breaker transitioning to HALF-OPEN");
                        return true;
                    }
                    return false;

                case CircuitState.HalfOpen:
                    // Only allow one test at a time (throttle half-open tests)
                    if (DateTimeOffset.UtcNow - _lastHalfOpenTest < TimeSpan.FromSeconds(5))
                        return false;
                    _lastHalfOpenTest = DateTimeOffset.UtcNow;
                    return true;

                default:
                    return false;
            }
        }
    }

    private void OnGlobalSuccess()
    {
        lock (_circuitLock)
        {
            if (_circuitState == CircuitState.HalfOpen)
            {
                _circuitState = CircuitState.Closed;
                _consecutiveFailures = 0;
                ResubscriptionMetrics.IncCircuitBreakerClose();
                _log.Information("Global circuit breaker CLOSED after successful half-open test");
            }
            else
            {
                _consecutiveFailures = 0;
            }
        }
    }

    private void OnGlobalFailure()
    {
        lock (_circuitLock)
        {
            _consecutiveFailures++;

            if (_circuitState == CircuitState.HalfOpen)
            {
                // Half-open test failed, back to open
                _circuitState = CircuitState.Open;
                _circuitOpenedAt = DateTimeOffset.UtcNow;
                ResubscriptionMetrics.IncCircuitBreakerOpen();
                _log.Warning("Global circuit breaker re-opened after failed half-open test");
            }
            else if (_consecutiveFailures >= _options.CircuitBreakerThreshold)
            {
                _circuitState = CircuitState.Open;
                _circuitOpenedAt = DateTimeOffset.UtcNow;
                ResubscriptionMetrics.IncCircuitBreakerOpen();
                _log.Error(
                    "Global circuit breaker OPENED after {Failures} consecutive failures. " +
                    "Breaking for {Duration}s",
                    _consecutiveFailures,
                    _options.CircuitBreakerDuration.TotalSeconds);
            }
        }
    }

    private void UpdateMetricsGauges()
    {
        ResubscriptionMetrics.SetSymbolsInCooldown(SymbolsInCooldown);
        ResubscriptionMetrics.SetSymbolsCircuitOpen(SymbolsWithOpenCircuit);
    }

    private async Task CleanupLoopAsync(CancellationToken ct = default)
    {
        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), _cts.Token);

                // Clean up stale symbol states (no activity for 1 hour)
                var staleThreshold = DateTimeOffset.UtcNow.AddHours(-1);
                var staleSymbols = _symbolStates
                    .Where(kvp => kvp.Value.LastActivityTime < staleThreshold)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var symbol in staleSymbols)
                {
                    _symbolStates.TryRemove(symbol, out _);
                }

                if (staleSymbols.Count > 0)
                {
                    _log.Debug("Cleaned up {Count} stale symbol states", staleSymbols.Count);
                }

                UpdateMetricsGauges();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try
        {
            await _cleanupTask;
        }
        catch
        {
            // Ignore
        }
        _cts.Dispose();
    }

    /// <summary>
    /// Per-symbol resubscription state tracking.
    /// </summary>
    private sealed class SymbolResubscribeState
    {
        private readonly object _lock = new();
        private DateTimeOffset _lastAttemptTime;
        private DateTimeOffset _lastSuccessTime;
        private int _consecutiveFailures;
        private CircuitState _circuitState = CircuitState.Closed;
        private DateTimeOffset _circuitOpenedAt;

        public DateTimeOffset LastActivityTime { get; private set; } = DateTimeOffset.UtcNow;
        public CircuitState CircuitState { get { lock (_lock) return _circuitState; } }

        public bool IsInCooldown(TimeSpan cooldown)
        {
            lock (_lock)
            {
                return _lastSuccessTime != default &&
                       DateTimeOffset.UtcNow - _lastSuccessTime < cooldown;
            }
        }

        public bool CanResubscribe(TimeSpan cooldown, TimeSpan minInterval)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;

                // Check minimum interval between attempts
                if (_lastAttemptTime != default && now - _lastAttemptTime < minInterval)
                    return false;

                // Check cooldown after success (don't immediately resubscribe after success)
                if (_lastSuccessTime != default && now - _lastSuccessTime < cooldown)
                    return false;

                return true;
            }
        }

        public bool CanProceedCircuit(TimeSpan breakDuration)
        {
            lock (_lock)
            {
                if (_circuitState == CircuitState.Closed)
                    return true;

                if (_circuitState == CircuitState.Open)
                {
                    if (DateTimeOffset.UtcNow - _circuitOpenedAt >= breakDuration)
                    {
                        _circuitState = CircuitState.HalfOpen;
                        return true;
                    }
                    return false;
                }

                // Half-open: allow test
                return true;
            }
        }

        public void RecordAttempt()
        {
            lock (_lock)
            {
                _lastAttemptTime = DateTimeOffset.UtcNow;
                LastActivityTime = _lastAttemptTime;
            }
        }

        public void RecordSuccess()
        {
            lock (_lock)
            {
                _lastSuccessTime = DateTimeOffset.UtcNow;
                _consecutiveFailures = 0;
                _circuitState = CircuitState.Closed;
                LastActivityTime = _lastSuccessTime;
            }
        }

        public void RecordFailure(int threshold)
        {
            lock (_lock)
            {
                _consecutiveFailures++;
                LastActivityTime = DateTimeOffset.UtcNow;

                if (_circuitState == CircuitState.HalfOpen)
                {
                    _circuitState = CircuitState.Open;
                    _circuitOpenedAt = DateTimeOffset.UtcNow;
                }
                else if (_consecutiveFailures >= threshold)
                {
                    _circuitState = CircuitState.Open;
                    _circuitOpenedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        public void ResetCircuit()
        {
            lock (_lock)
            {
                _circuitState = CircuitState.Closed;
                _consecutiveFailures = 0;
            }
        }
    }
}

/// <summary>
/// Configuration options for the auto-resubscribe policy.
/// </summary>
public sealed record AutoResubscribeOptions
{
    /// <summary>
    /// Minimum severity level required to trigger auto-resubscribe.
    /// Default: Error (only Error-level integrity events trigger resubscription).
    /// </summary>
    public IntegritySeverity MinSeverityForResubscribe { get; init; } = IntegritySeverity.Error;

    /// <summary>
    /// Cooldown period after a successful resubscription.
    /// Prevents rapid repeated resubscriptions for the same symbol.
    /// Default: 30 seconds.
    /// </summary>
    public TimeSpan SymbolCooldown { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Minimum interval between resubscription attempts for the same symbol.
    /// Applies even if previous attempt failed.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan MinResubscribeInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of consecutive failures before the global circuit breaker opens.
    /// Default: 5 failures.
    /// </summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>
    /// Duration the global circuit breaker remains open before transitioning to half-open.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan CircuitBreakerDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Number of consecutive failures before a per-symbol circuit breaker opens.
    /// Default: 3 failures.
    /// </summary>
    public int SymbolCircuitBreakerThreshold { get; init; } = 3;

    /// <summary>
    /// Duration a per-symbol circuit breaker remains open.
    /// Default: 120 seconds (longer than global to allow global recovery first).
    /// </summary>
    public TimeSpan SymbolCircuitBreakerDuration { get; init; } = TimeSpan.FromSeconds(120);
}
