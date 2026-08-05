using Meridian.Backtesting.Sdk;
using Meridian.Execution.Interfaces;
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
    private StrategyStatus _status = StrategyStatus.Registered;

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

    /// <summary>The execution context supplied to <see cref="StartAsync"/>, if started.</summary>
    protected IExecutionContext? ExecutionContext { get; private set; }

    /// <inheritdoc/>
    public async Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        lock (_stateLock)
        {
            if (_status is StrategyStatus.Running or StrategyStatus.WarmingUp)
            {
                throw new InvalidOperationException(
                    $"Strategy '{StrategyId}' cannot start because it is already {_status}.");
            }

            _status = StrategyStatus.WarmingUp;
        }

        ExecutionContext = ctx;
        try
        {
            await OnStartingAsync(ctx, ct).ConfigureAwait(false);
        }
        catch
        {
            lock (_stateLock)
            {
                _status = StrategyStatus.Stopped;
            }

            throw;
        }

        lock (_stateLock)
        {
            _status = StrategyStatus.Running;
        }
    }

    /// <inheritdoc/>
    public Task PauseAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (_status != StrategyStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Strategy '{StrategyId}' cannot pause because it is {_status}.");
            }

            _status = StrategyStatus.Paused;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (_status == StrategyStatus.Stopped)
            {
                return;
            }

            _status = StrategyStatus.Stopped;
        }

        await OnStoppedAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Hook invoked while the strategy transitions through
    /// <see cref="StrategyStatus.WarmingUp"/>. Override for indicator warm-up or state loading.
    /// </summary>
    protected virtual Task OnStartingAsync(IExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Hook invoked after the strategy transitions to <see cref="StrategyStatus.Stopped"/>.</summary>
    protected virtual Task OnStoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
