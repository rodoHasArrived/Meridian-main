using Meridian.Contracts.Domain.Models;

namespace Meridian.Strategies.Live;

/// <summary>
/// Restores operator control state without running user code. The first explicit resume warms
/// a fresh strategy against the engine-owned context; subsequent pauses retain that context.
/// </summary>
internal sealed class RestoredPausedLiveStrategy(
    ILiveStrategy strategy,
    LiveStrategyExecutionContext executionContext) : ILiveStrategy, ILiveOrderOutcomeObserver
{
    private int _awaitingResume = 1;
    private int _stoppedWithoutStarting;
    private int _startAttempted;
    private volatile Exception? _startFailure;
    private bool _initialized;

    public string StrategyId => strategy.StrategyId;
    public string Name => strategy.Name;
    public string? FaultReason => _startFailure?.Message ?? strategy.FaultReason;
    public StrategyStatus Status => _startFailure is not null
        ? StrategyStatus.Faulted
        : Volatile.Read(ref _stoppedWithoutStarting) != 0
        ? StrategyStatus.Stopped
        : Volatile.Read(ref _awaitingResume) != 0
        ? StrategyStatus.Paused
        : strategy.Status;

    public async Task StartAsync(IExecutionContext ctx, CancellationToken ct = default)
    {
        Volatile.Write(ref _startAttempted, 1);
        Volatile.Write(ref _stoppedWithoutStarting, 0);
        try
        {
            await strategy.StartAsync(executionContext, ct).ConfigureAwait(false);
            if (!_initialized)
            {
                strategy.Initialize(executionContext);
                _initialized = true;
            }
            Volatile.Write(ref _awaitingResume, 0);
        }
        catch (Exception exception)
        {
            _startFailure = exception;
            Volatile.Write(ref _awaitingResume, 0);
            throw;
        }
    }

    public Task PauseAsync(CancellationToken ct = default) => Status == StrategyStatus.Paused
        ? Task.CompletedTask
        : strategy.PauseAsync(ct);

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _startAttempted) == 0)
        {
            ct.ThrowIfCancellationRequested();
            Volatile.Write(ref _stoppedWithoutStarting, 1);
            return;
        }
        await strategy.StopAsync(ct).ConfigureAwait(false);
        _startFailure = null;
        Volatile.Write(ref _awaitingResume, 0);
    }

    public void Initialize(IBacktestContext ctx) { }
    public void OnTrade(Trade trade, IBacktestContext ctx) => strategy.OnTrade(trade, ctx);
    public void OnQuote(BboQuotePayload quote, IBacktestContext ctx) => strategy.OnQuote(quote, ctx);
    public void OnBar(HistoricalBar bar, IBacktestContext ctx) => strategy.OnBar(bar, ctx);
    public void OnOrderBook(LOBSnapshot snapshot, IBacktestContext ctx) => strategy.OnOrderBook(snapshot, ctx);
    public void OnOrderFill(FillEvent fill, IBacktestContext ctx) => strategy.OnOrderFill(fill, ctx);
    public void OnOrderTerminated(Guid orderId, LiveOrderOutcome outcome)
    {
        if (strategy is ILiveOrderOutcomeObserver observer)
        {
            observer.OnOrderTerminated(orderId, outcome);
        }
    }
    public void OnDayEnd(DateOnly date, IBacktestContext ctx) => strategy.OnDayEnd(date, ctx);
    public void OnFinished(IBacktestContext ctx)
    {
        if (_initialized)
        {
            strategy.OnFinished(ctx);
        }
    }
}
