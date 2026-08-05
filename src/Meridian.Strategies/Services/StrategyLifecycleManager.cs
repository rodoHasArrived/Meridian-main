using System.Collections.Concurrent;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Trading;
using Meridian.Strategies.Models;
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
    private readonly Dictionary<string, (ILiveStrategy Strategy, StrategyRunEntry? CurrentRun)> _registered = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _commandGates =
        new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Creates a new lifecycle manager.</summary>
    public StrategyLifecycleManager(
        IStrategyRepository repository,
        ILogger<StrategyLifecycleManager> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Registers a strategy so it can be controlled by this manager.</summary>
    public void Register(ILiveStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        lock (_lock)
        {
            _registered[strategy.StrategyId] = (strategy, null);
        }

        _logger.LogInformation("Strategy registered: {StrategyId} ({Name})", strategy.StrategyId, strategy.Name);
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

    private async Task<VerifiedOperationOutcome> StartCoreAsync(
        string strategyId,
        IExecutionContext ctx,
        RunType runType,
        CancellationToken ct)
    {
        var (strategy, priorRun) = await GetRegisteredEntryWithHistoryAsync(strategyId, ct)
            .ConfigureAwait(false);

        var startTransition = StrategyLifecycleInterop.EvaluateStart(strategy.Status.ToString());
        if (!startTransition.IsValid)
        {
            throw new InvalidOperationException(startTransition.Reason);
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
        else
        {
            var recoveryAttempt = priorRun is not null &&
                                  strategy.Status is StrategyStatus.Paused or StrategyStatus.Stopped;
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
                : StrategyRunEntry.Start(
                    strategyId,
                    strategy.Name,
                    runType,
                    recovery.AttemptId ?? throw new InvalidOperationException(
                        $"Strategy '{strategyId}' recovery marker has no attempt identity."));
            run = run with
            {
                AttemptNumber = recovery?.AttemptNumber ?? 1,
                RecoveryParentRunId = recovery?.RecoveryParentRunId
            };
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

        var started = requested.Started(LifecycleActorId, "Strategy started externally.");
        return await PersistExternallyCompletedAsync(
            strategyId,
            strategy,
            started,
            StrategyRunLifecycleEventType.Started,
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
        var pauseTransition = StrategyLifecycleInterop.EvaluatePause(strategy.Status.ToString());
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
        var stopTransition = StrategyLifecycleInterop.EvaluateStop(strategy.Status.ToString());
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
        }
        catch (OperationCanceledException exception)
        {
            var cancelled = requested.Cancel(exception, "Strategy stop was cancelled.") with
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
                reconciled = currentRun.Started(
                    LifecycleActorId,
                    "Reconciled externally running strategy state.") with
                {
                    EndedAt = null,
                    TerminalStatus = null
                };
                eventType = StrategyRunLifecycleEventType.Started;
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
            case StrategyStatus.Running when currentRun.LastLifecycleEvent == StrategyRunLifecycleEventType.Started:
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
        var retained = await _repository.GetLatestRunAsync(strategyId, ct).ConfigureAwait(false);
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
