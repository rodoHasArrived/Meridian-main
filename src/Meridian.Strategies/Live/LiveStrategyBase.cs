using Meridian.Backtesting.Sdk;
using Meridian.Execution.Interfaces;
using Meridian.FSharp.Trading;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Live;

/// <summary>
/// Convenience base class for concrete <see cref="ILiveStrategy"/> implementations.
/// Combines the no-op <see cref="IBacktestStrategy"/> callbacks of
/// <see cref="BacktestStrategyBase"/> with a thread-safe implementation of the
/// <see cref="IStrategyLifecycle"/> state machine, so a strategy only overrides the
/// market-event callbacks it cares about. Enforced by ADR-016.
/// </summary>
public abstract class LiveStrategyBase : BacktestStrategyBase, ILiveStrategy
{
    private readonly Lock _stateLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private StrategyStatus _status = StrategyStatus.Registered;
    private string? _faultReason;

    /// <inheritdoc/>
    public abstract string StrategyId { get; }

    /// <inheritdoc/>
    public StrategyStatus Status
    {
        get
        {
            lock (_stateLock)
            {
                return _status;
            }
        }
    }

    /// <inheritdoc/>
    public string? FaultReason
    {
        get
        {
            lock (_stateLock)
            {
                return _faultReason;
            }
        }
    }

    /// <summary>The execution context supplied to <see cref="StartAsync"/>, if started.</summary>
    protected IExecutionContext? ExecutionContext { get; private set; }

    /// <inheritdoc/>
    public async Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var transition = ApplyTransition(StrategyLifecycleInterop.EvaluateStart);
            if (transition.NextState == nameof(StrategyStatus.Running))
            {
                // Resuming preserves the warmed strategy and the context that owns its state.
                return;
            }

            ExecutionContext = ctx;
            try
            {
                await OnStartingAsync(ctx, ct).ConfigureAwait(false);
                ApplyTransition(StrategyLifecycleInterop.EvaluateWarmupCompleted);
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task PauseAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ApplyTransition(StrategyLifecycleInterop.EvaluatePause);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Status == StrategyStatus.Stopped)
            {
                return;
            }

            ApplyTransition(StrategyLifecycleInterop.EvaluateStop);
            try
            {
                await OnStoppedAsync(ct).ConfigureAwait(false);
                ApplyTransition(StrategyLifecycleInterop.EvaluateStopCompleted);
                ExecutionContext = null;
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private StrategyTransitionDto ApplyTransition(Func<string, string?, StrategyTransitionDto> evaluate)
    {
        lock (_stateLock)
        {
            var transition = evaluate(_status.ToString(), _faultReason);
            if (!transition.IsValid)
            {
                throw new InvalidOperationException($"Strategy '{StrategyId}': {transition.Reason}");
            }

            _status = Enum.Parse<StrategyStatus>(transition.NextState);
            _faultReason = string.IsNullOrEmpty(transition.FaultReason) ? null : transition.FaultReason;
            return transition;
        }
    }

    private void RecordFailure(Exception exception)
    {
        var reason = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        ApplyTransition((state, faultReason) => StrategyLifecycleInterop.EvaluateFail(state, faultReason, reason));
    }

    /// <summary>
    /// Hook invoked while the strategy transitions through
    /// <see cref="StrategyStatus.WarmingUp"/>. Override for indicator warm-up or state loading.
    /// </summary>
    protected virtual Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Cleanup hook invoked while the strategy is <see cref="StrategyStatus.Stopping"/>.
    /// Successful completion transitions to <see cref="StrategyStatus.Stopped"/>; failure or
    /// cancellation faults the strategy and requires another successful stop before restart.
    /// </summary>
    protected virtual Task OnStoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
