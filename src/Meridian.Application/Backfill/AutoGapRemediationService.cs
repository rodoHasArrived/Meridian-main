using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using Meridian.Core.Logging;
using Meridian.DataIntegration.Monitoring.DataQuality;
using Meridian.Application.Scheduling;
using Meridian.Infrastructure.Adapters.Core;
using Serilog;
using QualityDataGap = Meridian.DataIntegration.Monitoring.DataQuality.DataGap;
using StorageGapAnalysisResult = Meridian.Infrastructure.Adapters.Core.GapAnalysisResult;

namespace Meridian.Application.Backfill;

public enum AutoRemediationOutcome
{
    None,
    Completed,
    FailedTransient,
    FailedPermanent,
    Skipped
}

public enum AutoRemediationTriggerSource
{
    DataQualityGap,
    GapAnalyzerScan,
    QualityAlert
}

/// <summary>
/// Shared application boundary for explicit, contextual data-quality gap remediation.
/// </summary>
public interface IDataQualityGapRemediationService
{
    Task<AutoGapRemediationRequestResult> RequestDataQualityGapAsync(
        QualityDataGap gap,
        string? provider = null,
        CancellationToken ct = default);
}

/// <summary>
/// Truthful result of a completed or skipped contextual remediation request.
/// </summary>
public sealed record AutoGapRemediationRequestResult(
    AutoRemediationOutcome Outcome,
    string Provider,
    DateOnly From,
    DateOnly To,
    string IdempotencyKey);

/// <summary>
/// Guardrail policy for automatic gap remediation.
/// </summary>
/// <param name="MinimumGapDuration">
/// Minimum observed gap duration before a data-quality gap is worth remediating.
/// Applies per data-quality gap signal.
/// </param>
/// <param name="MinimumGapSize">
/// Minimum number of missing observations before a remediation is enqueued.
/// Applies per remediation request (a single symbol or a batched symbol group).
/// </param>
/// <param name="SymbolCooldown">
/// Per-symbol quiet window. After a symbol is remediated it is skipped until this window
/// elapses, preventing repeated backfills of the same symbol. Scope: individual symbol.
/// </param>
/// <param name="ProviderCooldown">
/// Per-provider quiet window. After any remediation runs against a provider, further
/// remediations for that provider are skipped until this window elapses, protecting the
/// upstream provider from bursts. Scope: individual provider (not global, not per-symbol).
/// </param>
/// <param name="MaxConcurrentRemediations">
/// Global ceiling on the number of remediation executions running at once across all symbols
/// and providers. Enforced by a semaphore, independent of the cooldown windows.
/// </param>
/// <param name="DefaultProvider">Provider used when a signal does not specify one.</param>
public sealed record AutoGapRemediationPolicy(
    TimeSpan MinimumGapDuration,
    int MinimumGapSize,
    TimeSpan SymbolCooldown,
    TimeSpan ProviderCooldown,
    int MaxConcurrentRemediations,
    string DefaultProvider)
{
    public static AutoGapRemediationPolicy Default { get; } = new(
        MinimumGapDuration: TimeSpan.FromMinutes(2),
        MinimumGapSize: 1,
        SymbolCooldown: TimeSpan.FromMinutes(5),
        ProviderCooldown: TimeSpan.FromMinutes(1),
        MaxConcurrentRemediations: 2,
        DefaultProvider: "stooq");

    /// <summary>
    /// Policy builder: constructs a policy from optional overrides, falling back to
    /// <see cref="Default"/> per field. Lets hosts tune individual windows/limits (e.g. from
    /// operator configuration) without re-declaring every value or hardcoding the defaults.
    /// </summary>
    public static AutoGapRemediationPolicy Create(
        TimeSpan? minimumGapDuration = null,
        int? minimumGapSize = null,
        TimeSpan? symbolCooldown = null,
        TimeSpan? providerCooldown = null,
        int? maxConcurrentRemediations = null,
        string? defaultProvider = null)
        => new(
            minimumGapDuration ?? Default.MinimumGapDuration,
            minimumGapSize ?? Default.MinimumGapSize,
            symbolCooldown ?? Default.SymbolCooldown,
            providerCooldown ?? Default.ProviderCooldown,
            maxConcurrentRemediations ?? Default.MaxConcurrentRemediations,
            defaultProvider ?? Default.DefaultProvider);
}

public enum BackfillRemediationSlaTier
{
    Standard,
    SameBusinessDay
}

public sealed record BackfillRemediationSlaDecision(
    BackfillRemediationSlaTier Tier,
    DateTimeOffset DueAtUtc,
    bool RequiresOwnerAssignment,
    string DownstreamWorkflow,
    string ReasonCode);

/// <summary>
/// Typed SLA metadata attached to auto-remediation executions.
/// Replaces the legacy key=value strings previously stored in execution warnings.
/// </summary>
public sealed record BackfillRemediationSlaMetadata(
    BackfillRemediationSlaTier Tier,
    DateTimeOffset DueAtUtc,
    bool RequiresOwnerAssignment,
    string DownstreamWorkflow,
    string ReasonCode,
    string Provider,
    AutoRemediationTriggerSource TriggerSource);

public enum BackfillRemediationSlaStatus
{
    Open,
    DueSoon,
    Overdue,
    Failed,
    Completed
}

public sealed record BackfillRemediationSlaStatusItem(
    string ExecutionId,
    string IdempotencyKey,
    BackfillRemediationSlaTier Tier,
    BackfillRemediationSlaStatus Status,
    DateTimeOffset DueAtUtc,
    bool RequiresOwnerAssignment,
    string DownstreamWorkflow,
    string ReasonCode,
    string Provider,
    IReadOnlyList<string> Symbols,
    DateOnly From,
    DateOnly To,
    ExecutionStatus ExecutionStatus,
    string? LastOutcome,
    TimeSpan TimeRemaining);

public sealed record BackfillRemediationSlaSnapshot(
    DateTimeOffset EvaluatedAtUtc,
    int Total,
    int OverdueCount,
    int DueSoonCount,
    int RequiresOwnerAssignmentCount,
    IReadOnlyList<BackfillRemediationSlaStatusItem> Items);

/// <summary>
/// SLA classification policy for remediation executions.
/// </summary>
/// <param name="StandardWindow">
/// Time allowed to resolve a standard-tier gap, measured from when the gap was observed.
/// </param>
/// <param name="SameBusinessDayWindow">
/// Tighter window applied to escalated (same-business-day) gaps that feed critical downstream
/// workflows, carry a critical severity, or arrive as quality alerts.
/// </param>
public sealed record BackfillRemediationSlaPolicy(
    TimeSpan StandardWindow,
    TimeSpan SameBusinessDayWindow)
{
    private static readonly string[] CriticalDownstreamWorkflows =
    [
        "paper",
        "paper-trading",
        "reconciliation",
        "accounting",
        "reporting",
        "governed-reporting"
    ];

    public static BackfillRemediationSlaPolicy Default { get; } = new(
        StandardWindow: TimeSpan.FromHours(48),
        SameBusinessDayWindow: TimeSpan.FromHours(8));

    /// <summary>
    /// Policy builder: constructs a policy from optional window overrides, falling back to
    /// <see cref="Default"/> per field so hosts can tune SLA windows without hardcoding defaults.
    /// </summary>
    public static BackfillRemediationSlaPolicy Create(
        TimeSpan? standardWindow = null,
        TimeSpan? sameBusinessDayWindow = null)
        => new(
            standardWindow ?? Default.StandardWindow,
            sameBusinessDayWindow ?? Default.SameBusinessDayWindow);

    public BackfillRemediationSlaDecision Classify(
        AutoRemediationTriggerSource source,
        string? severity,
        string? downstreamWorkflow,
        DateTimeOffset observedAtUtc)
    {
        var normalizedWorkflow = NormalizeWorkflow(downstreamWorkflow);
        var criticalWorkflow = IsCriticalWorkflow(normalizedWorkflow);
        var criticalSeverity = IsCriticalSeverity(severity);
        var alertDriven = IsAlertDriven(source);
        var sameBusinessDay = criticalWorkflow || criticalSeverity || alertDriven;

        var tier = sameBusinessDay
            ? BackfillRemediationSlaTier.SameBusinessDay
            : BackfillRemediationSlaTier.Standard;
        var reasonCode = ResolveReasonCode(criticalWorkflow, criticalSeverity, alertDriven);

        return new BackfillRemediationSlaDecision(
            tier,
            observedAtUtc.Add(sameBusinessDay ? SameBusinessDayWindow : StandardWindow),
            sameBusinessDay,
            normalizedWorkflow,
            reasonCode);
    }

    private static bool IsCriticalWorkflow(string normalizedWorkflow)
        => CriticalDownstreamWorkflows.Any(workflow =>
            normalizedWorkflow.Contains(workflow, StringComparison.OrdinalIgnoreCase));

    private static bool IsCriticalSeverity(string? severity)
        => string.Equals(severity, "Major", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase);

    private static bool IsAlertDriven(AutoRemediationTriggerSource source)
        => source == AutoRemediationTriggerSource.QualityAlert;

    // First matching escalation reason wins; order mirrors the precedence in Classify.
    private static string ResolveReasonCode(bool criticalWorkflow, bool criticalSeverity, bool alertDriven)
    {
        if (criticalWorkflow)
            return "CriticalWorkflow";
        if (criticalSeverity)
            return "CriticalSeverity";
        if (alertDriven)
            return "QualityAlert";
        return "StandardGap";
    }

    private static string NormalizeWorkflow(string? downstreamWorkflow)
        => string.IsNullOrWhiteSpace(downstreamWorkflow)
            ? "unassigned"
            : downstreamWorkflow.Trim().ToLowerInvariant();
}

public sealed record QualityAlertRemediationSignal(
    string Symbol,
    DateOnly From,
    DateOnly To,
    string? Provider,
    string AlertId,
    string Reason,
    int GapSize = 1,
    string? Severity = null,
    string? DownstreamWorkflow = null);

internal sealed class AutoRemediationState
{
    public int Attempts { get; set; }
    public DateTimeOffset LastAttemptAt { get; set; }
    public AutoRemediationOutcome LastOutcome { get; set; }
}

/// <summary>
/// Coordinates automatic data-gap remediation requests from quality/gap signals.
/// Applies guardrails and executes through the backfill coordinator.
/// </summary>
public sealed class AutoGapRemediationService : IDataQualityGapRemediationService, IDisposable
{
    // Synchronization model (see issue: mixed lock + SemaphoreSlim on shared state):
    //   * _idempotency + the per-entry lock(state): guard the read-modify-write of a single
    //     AutoRemediationState (attempt count / last outcome) for one idempotency key. The lock is
    //     held only for that in-memory bookkeeping and is NEVER held across an await or while
    //     acquiring _concurrencyGate.
    //   * _concurrencyGate (SemaphoreSlim): throttles how many remediation executions run at once
    //     (MaxConcurrentRemediations). It bounds execution; it does not protect any shared field.
    //   * _cooldowns: lock-free (ConcurrentDictionary-backed) last-attempt timestamps.
    // Because the per-state lock is never held while waiting on the semaphore (and vice versa),
    // there is no lock-ordering cycle and no deadlock path between the two primitives.
    private readonly IBackfillExecutionGateway _backfillGateway;
    private readonly BackfillExecutionHistory _history;
    private readonly AutoGapRemediationPolicy _policy;
    private readonly BackfillRemediationSlaPolicy _slaPolicy;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, AutoRemediationState> _idempotency = new(StringComparer.OrdinalIgnoreCase);
    private readonly RemediationCooldownTracker _cooldowns;
    private readonly BackfillRemediationSlaEvaluator _slaEvaluator;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly DataQualityMonitoringService? _qualityMonitoringService;

    // Graceful-shutdown tracking for background (event-driven) remediation tasks. New work observes
    // _shutdownCts, and Dispose drains in-flight tasks before releasing resources. _lifecycleLock
    // serializes the _disposed transition against background-task spawning so a task can never be
    // registered (and left undrained, or touch a disposed _shutdownCts) after Dispose has begun.
    private readonly object _lifecycleLock = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly ConcurrentDictionary<Task, byte> _inFlight = new();
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(10);
    private bool _disposed;

    public AutoGapRemediationService(
        IBackfillExecutionGateway backfillGateway,
        BackfillExecutionHistory history,
        DataQualityMonitoringService? qualityMonitoringService = null,
        AutoGapRemediationPolicy? policy = null,
        ILogger? log = null,
        BackfillRemediationSlaPolicy? slaPolicy = null)
    {
        _backfillGateway = backfillGateway;
        _history = history;
        _qualityMonitoringService = qualityMonitoringService;
        _policy = policy ?? AutoGapRemediationPolicy.Default;
        _slaPolicy = slaPolicy ?? BackfillRemediationSlaPolicy.Default;
        _log = log ?? LoggingSetup.ForContext<AutoGapRemediationService>();
        _cooldowns = new RemediationCooldownTracker(_policy.SymbolCooldown, _policy.ProviderCooldown);
        _slaEvaluator = new BackfillRemediationSlaEvaluator(_history);
        _concurrencyGate = new SemaphoreSlim(Math.Max(1, _policy.MaxConcurrentRemediations));

        if (_qualityMonitoringService is not null)
        {
            _qualityMonitoringService.OnGapDetected += OnQualityGapDetected;
        }
    }

    public void Dispose()
    {
        // Flip _disposed under the lifecycle lock so any concurrent OnQualityGapDetected either
        // registered its task before this point (and is therefore in _inFlight for draining) or
        // observes _disposed and bails out — no task can slip in after this transition.
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        if (_qualityMonitoringService is not null)
        {
            _qualityMonitoringService.OnGapDetected -= OnQualityGapDetected;
        }

        _shutdownCts.Cancel();

        // Graceful drain: give background remediation tasks a bounded window to observe cancellation
        // and unwind. Track whether the drain actually completed so we only tear down the shutdown
        // token and concurrency gate once no task can still touch them.
        var drained = true;
        var pending = _inFlight.Keys.ToArray();
        if (pending.Length > 0)
        {
            try
            {
                // Wait returns false on timeout (tasks still running); true when all completed.
                drained = Task.WhenAll(pending).Wait(ShutdownDrainTimeout);
            }
            catch (Exception ex)
            {
                // An AggregateException here means the tasks finished (faulting) within the window,
                // so the drain is complete — the tasks are no longer running.
                _log.Debug(ex, "In-flight auto-remediation tasks completed with errors during shutdown drain");
                drained = true;
            }
        }

        if (drained)
        {
            _shutdownCts.Dispose();
            _concurrencyGate.Dispose();
        }
        else
        {
            // Drain timed out: at least one remediation is still running and will release the
            // semaphore / observe the token as it unwinds (e.g. in its finally block). Disposing
            // now would turn that valid in-flight work into an ObjectDisposedException, so we defer
            // teardown to finalization instead. Neither type holds an unmanaged handle here (we
            // never allocated a wait handle), so skipping explicit Dispose does not leak resources.
            _log.Warning(
                "Auto-remediation shutdown drain timed out after {TimeoutSeconds}s with up to {Count} task(s) " +
                "still running; deferring disposal of shutdown resources to finalization to avoid tearing them " +
                "down under active use",
                ShutdownDrainTimeout.TotalSeconds, pending.Length);
        }
    }

    public Task HandleDataQualityGapAsync(QualityDataGap gap, string? provider = null, CancellationToken ct = default)
    {
        if (gap.Duration < _policy.MinimumGapDuration)
        {
            _log.Debug("Skipping remediation for {Symbol}: gap {Duration} below minimum {Minimum}", gap.Symbol, gap.Duration, _policy.MinimumGapDuration);
            return Task.CompletedTask;
        }

        var from = DateOnly.FromDateTime(gap.GapStart.UtcDateTime);
        var to = DateOnly.FromDateTime(gap.GapEnd.UtcDateTime);
        return EnqueueRemediationAsync(
            [gap.Symbol],
            from,
            to,
            provider ?? gap.Provider ?? _policy.DefaultProvider,
            AutoRemediationTriggerSource.DataQualityGap,
            $"gap:{gap.Severity}:{gap.Duration}",
            (int)Math.Max(gap.EstimatedMissedEvents, 1),
            gap.Severity.ToString(),
            downstreamWorkflow: null,
            ct: ct);
    }

    /// <summary>
    /// Executes the same guarded quality-gap path used by event-driven remediation and reports the
    /// observed outcome to an interactive caller. Existing fire-and-forget integrations continue
    /// to use <see cref="HandleDataQualityGapAsync"/> unchanged.
    /// </summary>
    public async Task<AutoGapRemediationRequestResult> RequestDataQualityGapAsync(
        QualityDataGap gap,
        string? provider = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gap);

        var normalizedProvider = NormalizeProvider(provider ?? gap.Provider);
        var from = DateOnly.FromDateTime(gap.GapStart.UtcDateTime);
        var to = DateOnly.FromDateTime(gap.GapEnd.UtcDateTime);
        var idempotencyKey = BuildIdempotencyKey(
            [gap.Symbol.Trim().ToUpperInvariant()],
            normalizedProvider,
            from,
            to);

        if (gap.Duration < _policy.MinimumGapDuration || gap.EstimatedMissedEvents < _policy.MinimumGapSize)
        {
            return new AutoGapRemediationRequestResult(
                AutoRemediationOutcome.Skipped,
                normalizedProvider,
                from,
                to,
                idempotencyKey);
        }

        await HandleDataQualityGapAsync(gap, normalizedProvider, ct).ConfigureAwait(false);
        var outcome = _idempotency.TryGetValue(idempotencyKey, out var state)
            ? ReadOutcome(state)
            : AutoRemediationOutcome.Skipped;

        return new AutoGapRemediationRequestResult(outcome, normalizedProvider, from, to, idempotencyKey);
    }

    public async Task HandleGapAnalysisResultAsync(StorageGapAnalysisResult result, string? provider = null, CancellationToken ct = default)
    {
        var candidates = new List<AutoGapRemediationCandidate>();
        var remediationProvider = provider ?? _policy.DefaultProvider;

        foreach (var (symbol, info) in result.SymbolGaps)
        {
            if (!info.HasGaps || info.GapDates.Count < _policy.MinimumGapSize)
            {
                continue;
            }

            foreach (var range in info.GetGapRanges())
            {
                candidates.Add(new AutoGapRemediationCandidate(
                    symbol,
                    range.From,
                    range.To,
                    remediationProvider,
                    AutoRemediationTriggerSource.GapAnalyzerScan,
                    $"scan:{result.Granularity}:{info.GapDates.Count}",
                    info.GapDates.Count));
            }
        }

        foreach (var group in candidates
                     .GroupBy(static candidate => new AutoRemediationBatchKey(
                         candidate.Provider,
                         candidate.From,
                         candidate.To,
                         candidate.Source))
                     .OrderBy(static group => group.Key.From)
                     .ThenBy(static group => group.Key.To)
                     .ThenBy(static group => group.Key.Provider, StringComparer.OrdinalIgnoreCase))
        {
            var symbols = group
                .Select(static candidate => candidate.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var gapSize = group.Sum(static candidate => Math.Max(candidate.GapSize, 1));

            await EnqueueRemediationAsync(
                symbols,
                group.Key.From,
                group.Key.To,
                group.Key.Provider,
                group.Key.Source,
                BuildBatchedReason(group.Select(static candidate => candidate.Reason)),
                gapSize,
                severity: null,
                downstreamWorkflow: null,
                ct: ct).ConfigureAwait(false);
        }
    }

    public Task HandleQualityAlertAsync(QualityAlertRemediationSignal signal, CancellationToken ct = default)
    {
        return EnqueueRemediationAsync(
            [signal.Symbol],
            signal.From,
            signal.To,
            signal.Provider ?? _policy.DefaultProvider,
            AutoRemediationTriggerSource.QualityAlert,
            $"alert:{signal.AlertId}:{signal.Reason}",
            Math.Max(signal.GapSize, 1),
            signal.Severity,
            signal.DownstreamWorkflow,
            ct);
    }

    public BackfillRemediationSlaSnapshot EvaluateRemediationSla(
        DateTimeOffset? nowUtc = null,
        int maxExecutions = 100,
        TimeSpan? dueSoonWindow = null)
        => _slaEvaluator.Evaluate(nowUtc, maxExecutions, dueSoonWindow);

    private void OnQualityGapDetected(QualityDataGap gap)
    {
        // Event-driven remediation runs in the background: attach it to the shutdown cancellation
        // token and track the task so Dispose can drain in-flight work instead of losing it. The
        // lifecycle lock guarantees registration is atomic with the _disposed check, so a task is
        // never spawned (nor _shutdownCts touched) once Dispose has started.
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                return;
            }

            TrackBackgroundRemediation(HandleDataQualityGapAsync(gap, ct: _shutdownCts.Token));
        }
    }

    private void TrackBackgroundRemediation(Task task)
    {
        _inFlight[task] = 0;

        // Remove on completion and observe faults so a background failure never becomes an
        // unobserved-task exception. Runs synchronously on completion; no extra scheduling.
        task.ContinueWith(
            static (completed, state) =>
            {
                var self = (AutoGapRemediationService)state!;
                self._inFlight.TryRemove(completed, out _);
                if (completed.IsFaulted)
                {
                    self._log.Warning(
                        completed.Exception?.GetBaseException(),
                        "Background auto-remediation task faulted");
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task EnqueueRemediationAsync(
        IReadOnlyList<string> symbols,
        DateOnly from,
        DateOnly to,
        string provider,
        AutoRemediationTriggerSource source,
        string reason,
        int gapSize,
        string? severity,
        string? downstreamWorkflow,
        CancellationToken ct)
    {
        if (gapSize < _policy.MinimumGapSize)
        {
            return;
        }

        var normalizedSymbols = symbols
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedSymbols.Length == 0)
        {
            return;
        }

        var normalizedProvider = NormalizeProvider(provider);
        var now = DateTimeOffset.UtcNow;
        var slaDecision = _slaPolicy.Classify(source, severity, downstreamWorkflow, now);

        if (_cooldowns.IsProviderCoolingDown(normalizedProvider, now))
        {
            _log.Debug("Auto-remediation provider cooldown active for {Provider}", normalizedProvider);
            return;
        }

        var eligibleSymbols = normalizedSymbols
            .Where(symbol => !_cooldowns.IsSymbolCoolingDown(symbol, now))
            .ToArray();

        if (eligibleSymbols.Length == 0)
        {
            _log.Debug("Auto-remediation symbol cooldown active for {Provider}", provider);
            return;
        }

        var idempotencyKey = BuildIdempotencyKey(eligibleSymbols, normalizedProvider, from, to);

        // Idempotency gate: the key identifies one (symbols|provider|date-range) remediation. We hold
        // a per-key lock only long enough to read-and-update the shared AutoRemediationState so two
        // concurrent triggers for the same key cannot both pass the gate. A recently Completed or
        // Skipped attempt within the symbol-cooldown window short-circuits (deduplicates); otherwise
        // we claim this attempt (increment count, reset outcome) and proceed to execute outside the
        // lock. The lock is never held across the await on _concurrencyGate or the gateway call.
        var state = _idempotency.GetOrAdd(idempotencyKey, _ => new AutoRemediationState());
        lock (state)
        {
            if (state.LastOutcome is AutoRemediationOutcome.Completed or AutoRemediationOutcome.Skipped &&
                now - state.LastAttemptAt < _policy.SymbolCooldown)
            {
                return;
            }

            state.Attempts++;
            state.LastAttemptAt = now;
            state.LastOutcome = AutoRemediationOutcome.None;
        }

        if (!await _concurrencyGate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            UpdateOutcome(state, AutoRemediationOutcome.Skipped);
            return;
        }

        try
        {
            var execution = CreateExecutionLog(
                eligibleSymbols,
                normalizedProvider,
                from,
                to,
                source,
                reason,
                idempotencyKey,
                state.Attempts,
                slaDecision);
            _history.AddExecution(execution);

            try
            {
                var request = new BackfillRequest(normalizedProvider, eligibleSymbols, from, to);
                var result = await _backfillGateway.RunAsync(request, ct).ConfigureAwait(false);

                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Status = result.Success ? ExecutionStatus.Completed : ExecutionStatus.Failed;
                execution.ErrorMessage = result.Error;
                execution.Statistics.TotalBarsRetrieved = result.BarsWritten;
                execution.Statistics.TotalSymbols = eligibleSymbols.Length;
                execution.Statistics.SuccessfulSymbols = result.Success ? eligibleSymbols.Length : 0;
                execution.Statistics.FailedSymbols = result.Success ? 0 : eligibleSymbols.Length;
                execution.AutoRemediationLastOutcome = result.Success
                    ? AutoRemediationOutcome.Completed.ToString()
                    : AutoRemediationOutcome.FailedPermanent.ToString();

                _cooldowns.Record(eligibleSymbols, normalizedProvider, now);
                UpdateOutcome(state, result.Success ? AutoRemediationOutcome.Completed : AutoRemediationOutcome.FailedPermanent);
            }
            catch (Exception ex)
            {
                execution.CompletedAt = DateTimeOffset.UtcNow;
                execution.Status = ExecutionStatus.Failed;
                execution.ErrorMessage = ex.Message;
                execution.AutoRemediationLastOutcome = IsTransientFailure(ex)
                    ? AutoRemediationOutcome.FailedTransient.ToString()
                    : AutoRemediationOutcome.FailedPermanent.ToString();

                var outcome = IsTransientFailure(ex)
                    ? AutoRemediationOutcome.FailedTransient
                    : AutoRemediationOutcome.FailedPermanent;

                UpdateOutcome(state, outcome);

                if (outcome == AutoRemediationOutcome.FailedTransient)
                {
                    _idempotency.TryRemove(idempotencyKey, out _);
                }
            }
            finally
            {
                // AddExecution persisted the initial Running row. Persist the same mutated object
                // again after every terminal path so outcome/SLA evidence survives restart.
                _history.UpdateExecution(execution);
            }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private string NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized.ToLowerInvariant();
        }

        normalized = _policy.DefaultProvider.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "unknown"
            : normalized.ToLowerInvariant();
    }

    internal static string BuildIdempotencyKey(IReadOnlyList<string> symbols, string provider, DateOnly from, DateOnly to)
        => $"{string.Join(",", symbols)}|{provider.ToLowerInvariant()}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}";

    private static string BuildBatchedReason(IEnumerable<string> reasons)
    {
        var distinctReasons = reasons
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static reason => reason, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinctReasons.Length switch
        {
            0 => "batch",
            1 => distinctReasons[0],
            _ => string.Join("|", distinctReasons)
        };
    }

    private static bool IsTransientFailure(Exception ex)
        => ex is HttpRequestException or TimeoutException or OperationCanceledException;

    private static void UpdateOutcome(AutoRemediationState state, AutoRemediationOutcome outcome)
    {
        lock (state)
        {
            state.LastOutcome = outcome;
        }
    }

    private static AutoRemediationOutcome ReadOutcome(AutoRemediationState state)
    {
        lock (state)
        {
            return state.LastOutcome;
        }
    }

    private static BackfillExecutionLog CreateExecutionLog(
        IReadOnlyList<string> symbols,
        string provider,
        DateOnly from,
        DateOnly to,
        AutoRemediationTriggerSource source,
        string reason,
        string idempotencyKey,
        int attempt,
        BackfillRemediationSlaDecision slaDecision)
    {
        return new BackfillExecutionLog
        {
            ScheduleId = "auto-gap-remediation",
            ScheduleName = "Auto Gap Remediation",
            Trigger = ExecutionTrigger.AutoRemediation,
            ScheduledAt = DateTimeOffset.UtcNow,
            StartedAt = DateTimeOffset.UtcNow,
            FromDate = from,
            ToDate = to,
            Symbols = symbols.ToList(),
            Status = ExecutionStatus.Running,
            AutoRemediationTriggerReason = reason,
            AutoRemediationAttemptCount = attempt,
            AutoRemediationLastOutcome = AutoRemediationOutcome.None.ToString(),
            AutoRemediationIdempotencyKey = idempotencyKey,
            AutoRemediationSla = new BackfillRemediationSlaMetadata(
                slaDecision.Tier,
                slaDecision.DueAtUtc,
                slaDecision.RequiresOwnerAssignment,
                slaDecision.DownstreamWorkflow,
                slaDecision.ReasonCode,
                provider,
                source)
        };
    }

    private sealed record AutoGapRemediationCandidate(
        string Symbol,
        DateOnly From,
        DateOnly To,
        string Provider,
        AutoRemediationTriggerSource Source,
        string Reason,
        int GapSize);

    private sealed record AutoRemediationBatchKey(
        string Provider,
        DateOnly From,
        DateOnly To,
        AutoRemediationTriggerSource Source);
}

/// <summary>
/// Tracks per-symbol and per-provider cooldown windows for auto-remediation. Split out of
/// <see cref="AutoGapRemediationService"/> so cooldown state and its quiet-window rules live in one
/// place. Backed by concurrent dictionaries; safe for concurrent readers and writers.
/// </summary>
internal sealed class RemediationCooldownTracker
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _symbolCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _providerCooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _symbolCooldown;
    private readonly TimeSpan _providerCooldown;

    public RemediationCooldownTracker(TimeSpan symbolCooldown, TimeSpan providerCooldown)
    {
        _symbolCooldown = symbolCooldown;
        _providerCooldown = providerCooldown;
    }

    /// <summary>True when the provider was remediated within its cooldown window as of <paramref name="now"/>.</summary>
    public bool IsProviderCoolingDown(string provider, DateTimeOffset now)
        => IsCoolingDown(_providerCooldowns, provider, _providerCooldown, now);

    /// <summary>True when the symbol was remediated within its cooldown window as of <paramref name="now"/>.</summary>
    public bool IsSymbolCoolingDown(string symbol, DateTimeOffset now)
        => IsCoolingDown(_symbolCooldowns, symbol, _symbolCooldown, now);

    /// <summary>Records the last-attempt timestamp for the given symbols and provider.</summary>
    public void Record(IReadOnlyList<string> symbols, string provider, DateTimeOffset timestamp)
    {
        foreach (var symbol in symbols)
        {
            _symbolCooldowns[symbol] = timestamp;
        }

        _providerCooldowns[provider] = timestamp;
    }

    private static bool IsCoolingDown(
        ConcurrentDictionary<string, DateTimeOffset> state,
        string key,
        TimeSpan cooldown,
        DateTimeOffset now)
        => state.TryGetValue(key, out var lastAttempt) && (now - lastAttempt) < cooldown;
}

/// <summary>
/// Evaluates SLA status for recorded auto-remediation executions and builds a snapshot. Split out of
/// <see cref="AutoGapRemediationService"/> so SLA classification/status projection is isolated from
/// remediation execution and cooldown concerns. Reads from <see cref="BackfillExecutionHistory"/>;
/// performs no mutation.
/// </summary>
internal sealed class BackfillRemediationSlaEvaluator
{
    private readonly BackfillExecutionHistory _history;

    public BackfillRemediationSlaEvaluator(BackfillExecutionHistory history)
    {
        _history = history;
    }

    public BackfillRemediationSlaSnapshot Evaluate(
        DateTimeOffset? nowUtc,
        int maxExecutions,
        TimeSpan? dueSoonWindow)
    {
        var evaluatedAt = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();

        if (maxExecutions <= 0)
        {
            return new BackfillRemediationSlaSnapshot(
                evaluatedAt,
                Total: 0,
                OverdueCount: 0,
                DueSoonCount: 0,
                RequiresOwnerAssignmentCount: 0,
                Items: Array.Empty<BackfillRemediationSlaStatusItem>());
        }

        var dueSoonThreshold = dueSoonWindow ?? TimeSpan.FromHours(1);
        if (dueSoonThreshold < TimeSpan.Zero)
        {
            dueSoonThreshold = TimeSpan.Zero;
        }

        var items = _history.GetRecentExecutions(maxExecutions)
            .Where(static execution => execution.Trigger == ExecutionTrigger.AutoRemediation)
            .Select(execution => TryBuildSlaStatusItem(execution, evaluatedAt, dueSoonThreshold))
            .Where(static item => item is not null)
            .Select(static item => item!)
            .OrderBy(static item => SlaStatusPriority(item.Status))
            .ThenBy(static item => item.DueAtUtc)
            .ThenBy(static item => item.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => string.Join(",", item.Symbols), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BackfillRemediationSlaSnapshot(
            evaluatedAt,
            items.Count,
            items.Count(static item => item.Status == BackfillRemediationSlaStatus.Overdue),
            items.Count(static item => item.Status == BackfillRemediationSlaStatus.DueSoon),
            items.Count(static item =>
                item.RequiresOwnerAssignment && item.Status != BackfillRemediationSlaStatus.Completed),
            items);
    }

    private static BackfillRemediationSlaStatusItem? TryBuildSlaStatusItem(
        BackfillExecutionLog execution,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonThreshold)
    {
        if (execution.AutoRemediationSla is { } sla)
        {
            return BuildSlaStatusItem(
                execution,
                sla.Tier,
                sla.DueAtUtc,
                sla.RequiresOwnerAssignment,
                sla.DownstreamWorkflow,
                sla.ReasonCode,
                sla.Provider,
                evaluatedAt,
                dueSoonThreshold);
        }

        // Legacy fallback: executions recorded before typed SLA metadata carried
        // key=value pairs in Warnings.
        var metadata = ParseWarningMetadata(execution.Warnings);
        if (!metadata.TryGetValue("sla-due-utc", out var dueText) ||
            !DateTimeOffset.TryParse(
                dueText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dueAt))
        {
            return null;
        }

        if (!metadata.TryGetValue("sla-tier", out var tierText) ||
            !Enum.TryParse<BackfillRemediationSlaTier>(tierText, ignoreCase: true, out var tier))
        {
            tier = BackfillRemediationSlaTier.Standard;
        }

        var provider = metadata.TryGetValue("provider", out var providerText)
            ? providerText
            : string.Empty;
        var downstreamWorkflow = metadata.TryGetValue("downstream-workflow", out var workflowText)
            ? workflowText
            : "unassigned";
        var reasonCode = metadata.TryGetValue("sla-reason", out var reasonText)
            ? reasonText
            : "Unknown";
        var requiresOwnerAssignment = metadata.TryGetValue("sla-requires-owner", out var requiresOwnerText) &&
            bool.TryParse(requiresOwnerText, out var requiresOwner) &&
            requiresOwner;

        return BuildSlaStatusItem(
            execution,
            tier,
            dueAt,
            requiresOwnerAssignment,
            downstreamWorkflow,
            reasonCode,
            provider,
            evaluatedAt,
            dueSoonThreshold);
    }

    private static BackfillRemediationSlaStatusItem BuildSlaStatusItem(
        BackfillExecutionLog execution,
        BackfillRemediationSlaTier tier,
        DateTimeOffset dueAt,
        bool requiresOwnerAssignment,
        string downstreamWorkflow,
        string reasonCode,
        string provider,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonThreshold)
    {
        var idempotencyKey = execution.AutoRemediationIdempotencyKey ??
            AutoGapRemediationService.BuildIdempotencyKey(execution.Symbols, provider, execution.FromDate, execution.ToDate);
        var status = ResolveSlaStatus(execution, dueAt, evaluatedAt, dueSoonThreshold);

        return new BackfillRemediationSlaStatusItem(
            execution.ExecutionId,
            idempotencyKey,
            tier,
            status,
            dueAt,
            requiresOwnerAssignment,
            downstreamWorkflow,
            reasonCode,
            provider,
            execution.Symbols.ToArray(),
            execution.FromDate,
            execution.ToDate,
            execution.Status,
            execution.AutoRemediationLastOutcome,
            dueAt - evaluatedAt);
    }

    private static Dictionary<string, string> ParseWarningMetadata(IEnumerable<string> warnings)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var warning in warnings)
        {
            var separatorIndex = warning.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == warning.Length - 1)
            {
                continue;
            }

            var key = warning[..separatorIndex].Trim();
            var value = warning[(separatorIndex + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    private static BackfillRemediationSlaStatus ResolveSlaStatus(
        BackfillExecutionLog execution,
        DateTimeOffset dueAt,
        DateTimeOffset evaluatedAt,
        TimeSpan dueSoonThreshold)
    {
        if (execution.Status == ExecutionStatus.Completed ||
            string.Equals(
                execution.AutoRemediationLastOutcome,
                AutoRemediationOutcome.Completed.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            return BackfillRemediationSlaStatus.Completed;
        }

        if (evaluatedAt >= dueAt)
        {
            return BackfillRemediationSlaStatus.Overdue;
        }

        if (execution.Status is ExecutionStatus.Failed or ExecutionStatus.Cancelled or ExecutionStatus.Skipped)
        {
            return BackfillRemediationSlaStatus.Failed;
        }

        return dueAt - evaluatedAt <= dueSoonThreshold
            ? BackfillRemediationSlaStatus.DueSoon
            : BackfillRemediationSlaStatus.Open;
    }

    private static int SlaStatusPriority(BackfillRemediationSlaStatus status)
        => status switch
        {
            BackfillRemediationSlaStatus.Overdue => 0,
            BackfillRemediationSlaStatus.Failed => 1,
            BackfillRemediationSlaStatus.DueSoon => 2,
            BackfillRemediationSlaStatus.Open => 3,
            BackfillRemediationSlaStatus.Completed => 4,
            _ => 5
        };
}
