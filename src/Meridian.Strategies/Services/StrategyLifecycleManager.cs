using System.Collections.Concurrent;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.FSharp.Trading;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Storage;

namespace Meridian.Strategies.Services;

/// <summary>
/// Manages registered live strategies and returns verified terminal receipts for every admitted
/// lifecycle command. Intent is retained before external state changes so recovery can reconcile
/// an already-completed external action without repeating it.
/// </summary>
[ImplementsAdr("ADR-016", "Strategies pillar — strategy lifecycle orchestration")]
public sealed class StrategyLifecycleManager : IAsyncDisposable
{
    private const string LifecycleActorId = "strategy-lifecycle-manager";

    private readonly IStrategyRepository _repository;
    private readonly ILogger<StrategyLifecycleManager> _logger;
    private readonly IPromotionRecordStore? _promotionRecordStore;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly Dictionary<string, (ILiveStrategy Strategy, StrategyRunEntry? CurrentRun)> _registered = new();
    // Replaced engine instances must not stay alive solely to remember their binding mode.
    private readonly ConditionalWeakTable<ILiveStrategy, object> _runBoundStrategies = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _commandGates =
        new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Creates a new lifecycle manager.</summary>
    public StrategyLifecycleManager(
        IStrategyRepository repository,
        ILogger<StrategyLifecycleManager> logger)
        : this(repository, logger, null)
    {
    }

    /// <summary>Creates a manager with the authority required to revalidate live recovery.</summary>
    public StrategyLifecycleManager(
        IStrategyRepository repository,
        ILogger<StrategyLifecycleManager> logger,
        IPromotionRecordStore? promotionRecordStore,
        ExecutionOperatorControlService? operatorControls = null,
        BrokerageConfiguration? brokerageConfiguration = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _promotionRecordStore = promotionRecordStore;
        _operatorControls = operatorControls;
        _brokerageConfiguration = brokerageConfiguration;
    }

    /// <summary>Registers a strategy so it can be controlled by this manager.</summary>
    public void Register(ILiveStrategy strategy)
        => Register(strategy, null);

    /// <summary>Registers a restored strategy together with its retained control state.</summary>
    public void Register(ILiveStrategy strategy, StrategyRunEntry? currentRun)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        if (currentRun is not null &&
            !string.Equals(currentRun.StrategyId, strategy.StrategyId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The retained run belongs to a different strategy.", nameof(currentRun));
        }

        lock (_lock)
        {
            _registered[strategy.StrategyId] = (strategy, currentRun);
            if (currentRun is null)
                _runBoundStrategies.Remove(strategy);
            else
                _runBoundStrategies.GetValue(strategy, static _ => new object());
        }

        _logger.LogInformation("Strategy registered: {StrategyId} ({Name})", strategy.StrategyId, strategy.Name);
    }

    /// <summary>Releases an engine-owned instance after its execution and controls have drained.</summary>
    internal async Task UnregisterAsync(ILiveStrategy strategy)
    {
        var gate = _commandGates.GetOrAdd(strategy.StrategyId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_lock)
            {
                _runBoundStrategies.Remove(strategy);
                if (_registered.TryGetValue(strategy.StrategyId, out var current) &&
                    ReferenceEquals(current.Strategy, strategy))
                {
                    _registered.Remove(strategy.StrategyId);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Starts a strategy and returns its retained terminal operation receipt.</summary>
    public Task<VerifiedOperationOutcome> StartAsync(
        string strategyId,
        IExecutionContext ctx,
        RunType runType,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ArgumentNullException.ThrowIfNull(ctx);
        return ExecuteSerializedAsync(
            strategyId,
            ct,
            token => StartCoreAsync(strategyId, ctx, runType, token));
    }

    /// <summary>Resumes the exact engine-owned instance and retained run under the strategy command gate.</summary>
    internal Task<VerifiedOperationOutcome> ResumeRunAsync(
        ILiveStrategy strategy,
        string runId,
        IExecutionContext ctx,
        RunType runType,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(ctx);
        return ExecuteSerializedAsync(strategy.StrategyId, ct, async token =>
        {
            var run = await _repository.GetRunByIdAsync(runId, token).ConfigureAwait(false);
            if (run is null ||
                !string.Equals(run.StrategyId, strategy.StrategyId, StringComparison.Ordinal) ||
                run.RunType != runType || run.EndedAt.HasValue ||
                run.TerminalStatus != StrategyRunStatus.Paused)
            {
                throw new InvalidOperationException("The requested run no longer has resumable paused evidence.");
            }

            lock (_lock)
            {
                _registered[strategy.StrategyId] = (strategy, run);
                _runBoundStrategies.GetValue(strategy, static _ => new object());
            }

            // Carry the pair directly into the command. A concurrent registration of another
            // run of this strategy must never redirect the external action or its evidence.
            return await StartCoreAsync(
                strategy.StrategyId, ctx, runType, token, (strategy, run)).ConfigureAwait(false);
        });
    }

    private async Task<VerifiedOperationOutcome> StartCoreAsync(
        string strategyId,
        IExecutionContext ctx,
        RunType runType,
        CancellationToken ct,
        (ILiveStrategy Strategy, StrategyRunEntry? CurrentRun)? boundEntry = null)
    {
        var (strategy, priorRun) = boundEntry ?? await GetRegisteredEntryWithHistoryAsync(strategyId, ct)
            .ConfigureAwait(false);
        var resumesPausedRun = strategy.Status == StrategyStatus.Paused;
        if (priorRun?.TerminalStatus == StrategyRunStatus.Paused && !resumesPausedRun)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategyId}' must restore its retained paused control state before an explicit resume.");
        }

        if (resumesPausedRun)
        {
            if (priorRun is null)
            {
                throw new InvalidOperationException(
                    $"Strategy '{strategyId}' cannot resume without a retained current run.");
            }

            if (priorRun.RunType != runType)
            {
                throw new InvalidOperationException(
                    $"Strategy '{strategyId}' must resume its retained {priorRun.RunType} run. " +
                    "Use the promotion workflow to approve a different run type.");
            }
        }

        var startTransition = StrategyLifecycleInterop.EvaluateStart(
            strategy.Status.ToString(), strategy.FaultReason ?? string.Empty);
        if (!startTransition.IsValid)
        {
            throw new InvalidOperationException(startTransition.Reason);
        }

        var recoveryAttempt = !resumesPausedRun && priorRun is not null &&
                              (strategy.Status == StrategyStatus.Stopped || priorRun.EndedAt.HasValue ||
                               priorRun.LastLifecycleEvent == StrategyRunLifecycleEventType.RecoveryAttempted);
        if ((resumesPausedRun || recoveryAttempt) && priorRun!.RunType != runType)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategyId}' must retain its approved {priorRun.RunType} execution mode during recovery. " +
                "Use the promotion workflow to approve a different run type.");
        }

        if (resumesPausedRun || recoveryAttempt)
        {
            await ValidateLiveRecoveryApprovalAsync(priorRun!, ct).ConfigureAwait(false);
        }

        StrategyRunEntry requested;
        if (priorRun?.LastLifecycleEvent == StrategyRunLifecycleEventType.StartRequested)
        {
            if (priorRun.RunType != runType)
            {
                throw new InvalidOperationException(
                    $"Strategy '{strategyId}' already has a retained {priorRun.RunType} start intent. " +
                    "Reconcile or complete that intent before requesting a different run type.");
            }

            requested = priorRun;
        }
        else if (resumesPausedRun)
        {
            requested = priorRun!.RequestStart(LifecycleActorId, "Paused strategy resume requested.");
            await _repository.RecordLifecycleEventAsync(
                requested,
                StrategyRunLifecycleEventType.StartRequested,
                ct).ConfigureAwait(false);
            UpdateCurrentRun(strategyId, strategy, requested);
        }
        else
        {
            StrategyRunEntry? recovery = null;
            if (recoveryAttempt)
            {
                recovery = priorRun!.LastLifecycleEvent == StrategyRunLifecycleEventType.RecoveryAttempted
                    ? priorRun
                    : priorRun.RecoveryAttempted(
                        priorRun.RunId,
                        priorRun.AttemptNumber + 1,
                        LifecycleActorId,
                        $"Restart requested while strategy was {strategy.Status}.");
                if (priorRun.LastLifecycleEvent != StrategyRunLifecycleEventType.RecoveryAttempted)
                {
                    await _repository.RecordLifecycleEventAsync(
                        recovery,
                        StrategyRunLifecycleEventType.RecoveryAttempted,
                        ct).ConfigureAwait(false);
                    UpdateCurrentRun(strategyId, strategy, recovery);
                }
            }

            var run = recovery is null
                ? StrategyRunEntry.Start(strategyId, strategy.Name, runType)
                : CreateRecoveryRun(recovery);
            requested = run.RequestStart(
                LifecycleActorId,
                recoveryAttempt ? "Strategy recovery start requested." : "Strategy start requested.");
            await _repository.RecordLifecycleEventAsync(
                requested,
                StrategyRunLifecycleEventType.StartRequested,
                ct).ConfigureAwait(false);
            UpdateCurrentRun(strategyId, strategy, requested);
        }

        _logger.LogInformation(
            "Starting strategy {StrategyId} (run {RunId}, mode {RunType})",
            strategyId,
            requested.RunId,
            runType);

        try
        {
            await strategy.StartAsync(ctx, ct).ConfigureAwait(false);
            RequireExternalStatus(strategy, StrategyStatus.Running, "start");
        }
        catch (OperationCanceledException exception)
        {
            var cancelled = requested.Cancel(exception, "Strategy start was cancelled.") with
            {
                ActorId = LifecycleActorId
            };
            return await PersistTerminalAndUpdateAsync(
                strategyId,
                strategy,
                cancelled,
                StrategyRunLifecycleEventType.Cancelled,
                exception).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var failed = requested.StartFailed(exception) with { ActorId = LifecycleActorId };
            return await PersistTerminalAndUpdateAsync(
                strategyId,
                strategy,
                failed,
                StrategyRunLifecycleEventType.StartFailed,
                exception).ConfigureAwait(false);
        }

        var started = resumesPausedRun
            ? requested.Resumed(LifecycleActorId, "Paused strategy resumed externally.")
            : requested.RecoveryParentRunId is not null
                ? requested.Recovered(LifecycleActorId, "Strategy recovery started externally.")
                : requested.Started(LifecycleActorId, "Strategy started externally.");
        return await PersistExternallyCompletedAsync(
            strategyId,
            strategy,
            started,
            started.LastLifecycleEvent,
            "The strategy started externally, but its final start evidence could not be retained.",
            ct).ConfigureAwait(false);
    }

    /// <summary>Pauses a running strategy and returns its retained terminal operation receipt.</summary>
    public Task<VerifiedOperationOutcome> PauseAsync(
        string strategyId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        return ExecuteSerializedAsync(
            strategyId,
            ct,
            token => PauseCoreAsync(strategyId, token));
    }

    private async Task<VerifiedOperationOutcome> PauseCoreAsync(
        string strategyId,
        CancellationToken ct)
    {
        var (strategy, currentRun) = await GetRegisteredEntryWithHistoryAsync(strategyId, ct)
            .ConfigureAwait(false);
        var pauseTransition = StrategyLifecycleInterop.EvaluatePause(
            strategy.Status.ToString(), strategy.FaultReason ?? string.Empty);
        if (!pauseTransition.IsValid)
        {
            throw new InvalidOperationException(pauseTransition.Reason);
        }

        if (currentRun is null)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategyId}' cannot be paused without a retained current run.");
        }

        var requested = currentRun.LastLifecycleEvent == StrategyRunLifecycleEventType.PauseRequested
            ? currentRun
            : currentRun.PauseRequested(LifecycleActorId);
        if (currentRun.LastLifecycleEvent != StrategyRunLifecycleEventType.PauseRequested)
        {
            await _repository.RecordLifecycleEventAsync(
                requested,
                StrategyRunLifecycleEventType.PauseRequested,
                ct).ConfigureAwait(false);
            UpdateCurrentRun(strategyId, strategy, requested);
        }

        _logger.LogInformation("Pausing strategy {StrategyId}", strategyId);
        try
        {
            await strategy.PauseAsync(ct).ConfigureAwait(false);
            RequireExternalStatus(strategy, StrategyStatus.Paused, "pause");
        }
        catch (OperationCanceledException exception)
        {
            var cancelled = requested.Cancel(exception, "Strategy pause was cancelled.") with
            {
                ActorId = LifecycleActorId
            };
            return await PersistTerminalAndUpdateAsync(
                strategyId,
                strategy,
                cancelled,
                StrategyRunLifecycleEventType.Cancelled,
                exception).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var failed = requested.PauseFailed(exception, LifecycleActorId);
            return await PersistTerminalAndUpdateAsync(
                strategyId,
                strategy,
                failed,
                StrategyRunLifecycleEventType.PauseFailed,
                exception).ConfigureAwait(false);
        }

        var paused = requested.Paused(LifecycleActorId, "Strategy paused externally.") with
        {
            TerminalStatus = StrategyRunStatus.Paused
        };
        return await PersistExternallyCompletedAsync(
            strategyId,
            strategy,
            paused,
            StrategyRunLifecycleEventType.Paused,
            "The strategy paused externally, but its final pause evidence could not be retained.",
            ct).ConfigureAwait(false);
    }

    /// <summary>Stops a strategy and returns its retained terminal operation receipt.</summary>
    public Task<VerifiedOperationOutcome> StopAsync(
        string strategyId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        return ExecuteSerializedAsync(
            strategyId,
            ct,
            token => StopCoreAsync(strategyId, token));
    }

    private async Task<VerifiedOperationOutcome> StopCoreAsync(
        string strategyId,
        CancellationToken ct)
    {
        var (strategy, currentRun) = await GetRegisteredEntryWithHistoryAsync(strategyId, ct)
            .ConfigureAwait(false);
        var stopTransition = StrategyLifecycleInterop.EvaluateStop(
            strategy.Status.ToString(), strategy.FaultReason ?? string.Empty);
        if (!stopTransition.IsValid)
        {
            throw new InvalidOperationException(stopTransition.Reason);
        }

        if (currentRun is null)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategyId}' cannot be stopped without a retained current run.");
        }

        _logger.LogInformation("Stopping strategy {StrategyId}", strategyId);
        var requested = currentRun.LastLifecycleEvent == StrategyRunLifecycleEventType.StopRequested
            ? currentRun
            : currentRun.RequestStop(LifecycleActorId);
        if (currentRun.LastLifecycleEvent != StrategyRunLifecycleEventType.StopRequested)
        {
            await _repository.RecordLifecycleEventAsync(
                requested,
                StrategyRunLifecycleEventType.StopRequested,
                ct).ConfigureAwait(false);
            UpdateCurrentRun(strategyId, strategy, requested);
        }

        try
        {
            await strategy.StopAsync(ct).ConfigureAwait(false);
            RequireExternalStatus(strategy, StrategyStatus.Stopped, "stop");
        }
        catch (OperationCanceledException exception)
        {
            var cancelled = requested.Cancel(exception, "Strategy stop was cancelled.") with
            {
                ActorId = LifecycleActorId
            };
            cancelled = cancelled with { EndedAt = requested.EndedAt ?? cancelled.EndedAt };
            return await PersistTerminalAndUpdateAsync(
                strategyId,
                strategy,
                cancelled,
                StrategyRunLifecycleEventType.Cancelled,
                exception).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var failed = requested.StopFailed(exception, LifecycleActorId);
            return await PersistTerminalAndUpdateAsync(
                strategyId,
                strategy,
                failed,
                StrategyRunLifecycleEventType.StopFailed,
                exception).ConfigureAwait(false);
        }

        var completed = requested.Complete(metrics: null) with
        {
            ActorId = LifecycleActorId,
            TerminalStatus = StrategyRunStatus.Stopped
        };
        completed = completed with { EndedAt = requested.EndedAt ?? completed.EndedAt };
        return await PersistExternallyCompletedAsync(
            strategyId,
            strategy,
            completed,
            StrategyRunLifecycleEventType.Completed,
            "The strategy stopped externally, but its final completion evidence could not be retained.",
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reconciles retained history with an external strategy state after an earlier final-evidence
    /// append failed. The external action is not repeated.
    /// </summary>
    public Task<VerifiedOperationOutcome> ReconcileExternalStateAsync(
        string strategyId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        return ExecuteSerializedAsync(
            strategyId,
            ct,
            token => ReconcileExternalStateCoreAsync(strategyId, token));
    }

    private async Task<VerifiedOperationOutcome> ReconcileExternalStateCoreAsync(
        string strategyId,
        CancellationToken ct)
    {
        var (strategy, currentRun) = await GetRegisteredEntryWithHistoryAsync(strategyId, ct)
            .ConfigureAwait(false);
        if (currentRun is null)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategyId}' has no retained run to reconcile.");
        }

        StrategyRunEntry reconciled;
        StrategyRunLifecycleEventType eventType;
        switch (strategy.Status)
        {
            case StrategyStatus.Running when currentRun.LastLifecycleEvent is
                StrategyRunLifecycleEventType.StartRequested or
                StrategyRunLifecycleEventType.StartFailed or
                StrategyRunLifecycleEventType.EvidencePersistenceFailed:
                var running = currentRun.TerminalStatus == StrategyRunStatus.Paused
                    ? currentRun.Resumed(LifecycleActorId, "Reconciled externally resumed strategy state.")
                    : currentRun.RecoveryParentRunId is not null
                        ? currentRun.Recovered(LifecycleActorId, "Reconciled externally recovered strategy state.")
                        : currentRun.Started(LifecycleActorId, "Reconciled externally running strategy state.");
                reconciled = running with
                {
                    EndedAt = null,
                    TerminalStatus = null
                };
                eventType = reconciled.LastLifecycleEvent;
                break;
            case StrategyStatus.Paused when currentRun.LastLifecycleEvent is
                StrategyRunLifecycleEventType.PauseRequested or
                StrategyRunLifecycleEventType.PauseFailed or
                StrategyRunLifecycleEventType.EvidencePersistenceFailed:
                reconciled = currentRun.Paused(
                    LifecycleActorId,
                    "Reconciled externally paused strategy state.") with
                {
                    TerminalStatus = StrategyRunStatus.Paused
                };
                eventType = StrategyRunLifecycleEventType.Paused;
                break;
            case StrategyStatus.Stopped when currentRun.LastLifecycleEvent is
                StrategyRunLifecycleEventType.StopRequested or
                StrategyRunLifecycleEventType.StopFailed or
                StrategyRunLifecycleEventType.EvidencePersistenceFailed:
                var now = DateTimeOffset.UtcNow;
                reconciled = currentRun with
                {
                    EndedAt = currentRun.EndedAt ?? now,
                    TerminalStatus = StrategyRunStatus.Stopped,
                    LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
                    LifecycleEventAtUtc = now,
                    ActorId = LifecycleActorId,
                    Reason = "Reconciled externally stopped strategy state.",
                    ExceptionType = null,
                    ExceptionMessage = null,
                    ExceptionStackTrace = null
                };
                eventType = StrategyRunLifecycleEventType.Completed;
                break;
            case StrategyStatus.Running when currentRun.LastLifecycleEvent is
                StrategyRunLifecycleEventType.Started or
                StrategyRunLifecycleEventType.Activated or
                StrategyRunLifecycleEventType.Resumed or
                StrategyRunLifecycleEventType.Recovered:
            case StrategyStatus.Paused when currentRun.LastLifecycleEvent == StrategyRunLifecycleEventType.Paused:
            case StrategyStatus.Stopped when currentRun.LastLifecycleEvent == StrategyRunLifecycleEventType.Completed:
                return RequireTerminalOutcome(currentRun, currentRun.LastLifecycleEvent);
            default:
                throw new InvalidOperationException(
                    $"Strategy '{strategyId}' external state '{strategy.Status}' cannot be reconciled from " +
                    $"retained event '{currentRun.LastLifecycleEvent}'.");
        }

        return await PersistTerminalAndUpdateAsync(
            strategyId,
            strategy,
            reconciled,
            eventType,
            operationException: null,
            ct).ConfigureAwait(false);
    }

    /// <summary>Returns a snapshot of all registered strategy IDs and their current status.</summary>
    public IReadOnlyDictionary<string, StrategyStatus> GetStatuses()
    {
        lock (_lock)
        {
            return _registered.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Strategy.Status);
        }
    }

    private async Task<VerifiedOperationOutcome> ExecuteSerializedAsync(
        string strategyId,
        CancellationToken ct,
        Func<CancellationToken, Task<VerifiedOperationOutcome>> operation)
    {
        var commandGate = _commandGates.GetOrAdd(
            strategyId,
            static _ => new SemaphoreSlim(1, 1));
        await commandGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await operation(ct).ConfigureAwait(false);
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async Task<(ILiveStrategy Strategy, StrategyRunEntry? CurrentRun)>
        GetRegisteredEntryWithHistoryAsync(string strategyId, CancellationToken ct)
    {
        var registered = GetRegisteredEntry(strategyId);
        bool runBound;
        lock (_lock)
        {
            runBound = registered.CurrentRun is not null && _runBoundStrategies.TryGetValue(registered.Strategy, out _);
        }
        var retained = runBound
            ? await _repository.GetRunByIdAsync(registered.CurrentRun!.RunId, ct).ConfigureAwait(false)
            : await _repository.GetLatestRunAsync(strategyId, ct).ConfigureAwait(false);
        if (retained is null)
        {
            return registered;
        }

        lock (_lock)
        {
            if (!_registered.TryGetValue(strategyId, out var current))
            {
                throw new InvalidOperationException($"Strategy '{strategyId}' is not registered.");
            }

            if (runBound && (!ReferenceEquals(current.Strategy, registered.Strategy) ||
                             current.CurrentRun?.RunId != registered.CurrentRun!.RunId))
            {
                throw new InvalidOperationException(
                    $"Strategy '{strategyId}' registration changed while its retained run was being loaded.");
            }

            if (ShouldUseRetainedRun(current.CurrentRun, retained))
            {
                current = (current.Strategy, retained);
                _registered[strategyId] = current;
            }

            return current;
        }
    }

    private static bool ShouldUseRetainedRun(
        StrategyRunEntry? current,
        StrategyRunEntry retained)
    {
        if (current is null)
        {
            return true;
        }

        var currentUpdatedAt = current.LifecycleEventAtUtc ?? current.EndedAt ?? current.StartedAt;
        var retainedUpdatedAt = retained.LifecycleEventAtUtc ?? retained.EndedAt ?? retained.StartedAt;

        if (!string.Equals(current.RunId, retained.RunId, StringComparison.Ordinal))
        {
            // A repository projection may lag the command that just updated the in-memory run.
            // Never replace a newer run merely because the retained projection has another ID.
            return retainedUpdatedAt > currentUpdatedAt;
        }

        return retainedUpdatedAt >= currentUpdatedAt;
    }

    private (ILiveStrategy Strategy, StrategyRunEntry? CurrentRun) GetRegisteredEntry(string strategyId)
    {
        lock (_lock)
        {
            if (!_registered.TryGetValue(strategyId, out var entry))
            {
                throw new InvalidOperationException($"Strategy '{strategyId}' is not registered.");
            }

            return entry;
        }
    }

    private void UpdateCurrentRun(string strategyId, ILiveStrategy strategy, StrategyRunEntry run)
    {
        lock (_lock)
        {
            _registered[strategyId] = (strategy, run);
        }
    }

    private static StrategyRunEntry CreateRecoveryRun(StrategyRunEntry prior)
    {
        var attemptId = prior.AttemptId ?? throw new InvalidOperationException(
            $"Strategy '{prior.StrategyId}' recovery marker has no attempt identity.");
        var now = DateTimeOffset.UtcNow;

        // A restart executes the same approved inputs: keep the parameter, feed, engine,
        // portfolio/ledger/fund scope, realism, evidence and promotion-parent declarations.
        // Correlation identifies the original execution across attempts; recovery parent
        // identifies the immediate predecessor. Outcomes belong only to their producing run.
        var run = prior with
        {
            RunId = attemptId,
            StartedAt = now,
            EndedAt = null,
            Metrics = null,
            TerminalStatus = null,
            LastLifecycleEvent = StrategyRunLifecycleEventType.StartRequested,
            LifecycleEventAtUtc = now,
            CorrelationId = prior.CorrelationId ?? prior.RunId,
            ExceptionType = null,
            ExceptionMessage = null,
            ExceptionStackTrace = null,
            OutputMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        };
        return run with { InputHashSha256 = StrategyRunEntry.ComputeRealismBoundInputHash(run) };
    }

    private async Task ValidateLiveRecoveryApprovalAsync(StrategyRunEntry run, CancellationToken ct)
    {
        if (run.RunType != RunType.Live)
        {
            return;
        }

        if (_promotionRecordStore is null || _operatorControls is null)
        {
            throw new InvalidOperationException(
                "Live recovery requires the durable promotion authority and current execution controls.");
        }

        if (_brokerageConfiguration is not { LiveExecutionEnabled: true })
        {
            throw new InvalidOperationException("Live recovery requires enabled live brokerage routing.");
        }

        var originalRunId = run.CorrelationId ?? run.RunId;
        var decisions = await _promotionRecordStore.LoadAllAsync(ct).ConfigureAwait(false);
        var approval = decisions.FirstOrDefault(decision =>
            string.Equals(decision.TargetRunId, originalRunId, StringComparison.Ordinal) &&
            string.Equals(decision.SourceRunId, run.ParentRunId, StringComparison.Ordinal) &&
            string.Equals(decision.StrategyId, run.StrategyId, StringComparison.Ordinal) &&
            string.Equals(decision.AuditReference, run.AuditReference, StringComparison.Ordinal) &&
            decision.SourceRunType == RunType.Paper && decision.TargetRunType == RunType.Live);
        if (approval is null ||
            !string.Equals(approval.Decision, PromotionDecisionKinds.Approved, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(approval.ApprovedBy) ||
            PromotionApprovalChecklist.GetMissingRequiredItems(RunType.Live, approval.ApprovalChecklist).Length > 0)
        {
            throw new InvalidOperationException("Live recovery has no matching retained operator promotion approval.");
        }

        var approvedRun = await _repository.GetRunByIdAsync(originalRunId, ct).ConfigureAwait(false);
        if (approvedRun is null ||
            !string.Equals(StrategyRunEntry.ComputeRealismBoundInputHash(approvedRun),
                StrategyRunEntry.ComputeRealismBoundInputHash(run), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Live recovery inputs differ from the approved execution configuration.");
        }

        var controlDecision = _operatorControls.EvaluateLivePromotion(
            approval.SourceRunId,
            run.StrategyId,
            approval.ManualOverrideId);
        if (!controlDecision.IsAllowed)
        {
            throw new InvalidOperationException(
                $"Live recovery approval is no longer valid: {controlDecision.RejectReason}");
        }

        var controls = _operatorControls.GetSnapshot();
        if (controls.ManualOverrides.Any(entry =>
                string.Equals(entry.Kind, ExecutionManualOverrideKinds.ForceBlockOrders, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(entry.StrategyId) ||
                 string.Equals(entry.StrategyId, run.StrategyId, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(entry.RunId) ||
                 string.Equals(entry.RunId, approval.SourceRunId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(entry.RunId, originalRunId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(entry.RunId, run.RunId, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException("Live recovery is blocked by an active ForceBlockOrders override.");
        }
    }

    private async Task<VerifiedOperationOutcome> PersistExternallyCompletedAsync(
        string strategyId,
        ILiveStrategy strategy,
        StrategyRunEntry completed,
        StrategyRunLifecycleEventType eventType,
        string warningReason,
        CancellationToken ct)
    {
        try
        {
            return await PersistTerminalAndUpdateAsync(
                strategyId,
                strategy,
                completed,
                eventType,
                operationException: null,
                ct).ConfigureAwait(false);
        }
        catch (Exception persistenceException)
        {
            try
            {
                var retained = await _repository
                    .GetRunByIdAsync(completed.RunId, CancellationToken.None)
                    .ConfigureAwait(false);
                if (retained is not null && retained.LastLifecycleEvent == eventType)
                {
                    UpdateCurrentRun(strategyId, strategy, retained);
                    return RequireTerminalOutcome(retained, eventType);
                }
            }
            catch (Exception verificationException)
            {
                _logger.LogWarning(
                    verificationException,
                    "Could not verify whether strategy lifecycle event {EventType} was retained for {StrategyId}",
                    eventType,
                    strategyId);
            }

            var warning = completed.EvidencePersistenceFailed(
                persistenceException,
                warningReason,
                LifecycleActorId);
            try
            {
                return await PersistTerminalAndUpdateAsync(
                    strategyId,
                    strategy,
                    warning,
                    StrategyRunLifecycleEventType.EvidencePersistenceFailed,
                    persistenceException,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception warningPersistenceException)
            {
                throw new InvalidOperationException(
                    $"Strategy '{strategyId}' completed its external lifecycle action, but neither its final " +
                    "transition nor recovery warning could be retained.",
                    new AggregateException(persistenceException, warningPersistenceException));
            }
        }
    }

    private async Task<VerifiedOperationOutcome> PersistTerminalAndUpdateAsync(
        string strategyId,
        ILiveStrategy strategy,
        StrategyRunEntry terminal,
        StrategyRunLifecycleEventType eventType,
        Exception? operationException,
        CancellationToken ct = default)
    {
        try
        {
            var outcome = await _repository
                .RecordLifecycleEventWithOutcomeAsync(terminal, eventType, ct)
                .ConfigureAwait(false);
            if (outcome is null)
            {
                throw new InvalidOperationException(
                    $"Lifecycle event '{eventType}' did not produce a terminal operation receipt.");
            }

            VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);
            UpdateCurrentRun(strategyId, strategy, terminal with { LastLifecycleEvent = eventType });
            return outcome;
        }
        catch (Exception persistenceException) when (operationException is not null &&
                                                     !ReferenceEquals(operationException, persistenceException))
        {
            throw new InvalidOperationException(
                $"Strategy '{strategyId}' lifecycle action failed and its terminal evidence could not be retained.",
                new AggregateException(operationException, persistenceException));
        }
    }

    private static VerifiedOperationOutcome RequireTerminalOutcome(
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType eventType) =>
        StrategyRunStore.CreateLifecycleOutcome(entry, eventType)
        ?? throw new InvalidOperationException(
            $"Lifecycle event '{eventType}' did not produce a terminal operation receipt.");

    private static void RequireExternalStatus(
        ILiveStrategy strategy,
        StrategyStatus expectedStatus,
        string command)
    {
        var observedStatus = strategy.Status;
        if (observedStatus != expectedStatus)
        {
            throw new InvalidOperationException(
                $"Strategy '{strategy.StrategyId}' returned from {command} with state '{observedStatus}'; " +
                $"expected '{expectedStatus}'.");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        string[] ids;
        lock (_lock)
        {
            ids = [.. _registered.Keys];
        }

        foreach (var id in ids)
        {
            try
            {
                var outcome = await StopAsync(id).ConfigureAwait(false);
                if (!outcome.IsSuccessful)
                {
                    _logger.LogWarning(
                        "Strategy {StrategyId} stop completed with terminal state {State} during dispose",
                        id,
                        outcome.State);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Error stopping strategy {StrategyId} during dispose", id);
            }
        }
    }
}
