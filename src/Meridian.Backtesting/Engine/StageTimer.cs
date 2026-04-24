using System.Diagnostics;
using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.Engine;

/// <summary>
/// Tracks the currently-active <see cref="BacktestStage"/> and the wall-clock
/// time spent in each stage across a single backtest run. Not thread-safe;
/// owned by the engine and only mutated on the replay thread.
/// </summary>
internal sealed class StageTimer
{
    private readonly Stopwatch _total;
    private readonly Stopwatch _stage;
    private readonly Dictionary<BacktestStage, TimeSpan> _cumulative = new();

    public StageTimer(BacktestStage initialStage = BacktestStage.ValidatingRequest)
    {
        CurrentStage = initialStage;
        _total = Stopwatch.StartNew();
        _stage = Stopwatch.StartNew();
    }

    public BacktestStage CurrentStage { get; private set; }

    public TimeSpan TotalElapsed => _total.Elapsed;

    public TimeSpan StageElapsed => _stage.Elapsed;

    /// <summary>
    /// Ends the current stage and starts a new one. The prior stage's elapsed
    /// time is accumulated into the cumulative totals. Transitioning to the
    /// same stage is a no-op so callers may idempotently request the current
    /// stage at phase boundaries.
    /// </summary>
    public void Transition(BacktestStage next)
    {
        if (next == CurrentStage)
            return;

        Accumulate(CurrentStage, _stage.Elapsed);
        CurrentStage = next;
        _stage.Restart();
    }

    /// <summary>
    /// Stops all timers and folds the active stage's time into the cumulative
    /// totals. Idempotent.
    /// </summary>
    public void Stop()
    {
        if (_stage.IsRunning)
        {
            Accumulate(CurrentStage, _stage.Elapsed);
            _stage.Stop();
        }

        if (_total.IsRunning)
            _total.Stop();
    }

    /// <summary>
    /// Snapshot of cumulative time spent in each observed stage at the moment
    /// this method is invoked. Includes the in-flight stage's current elapsed.
    /// </summary>
    public IReadOnlyDictionary<BacktestStage, TimeSpan> Cumulative()
    {
        var snapshot = new Dictionary<BacktestStage, TimeSpan>(_cumulative);
        if (_stage.IsRunning)
        {
            snapshot[CurrentStage] = snapshot.TryGetValue(CurrentStage, out var prior)
                ? prior + _stage.Elapsed
                : _stage.Elapsed;
        }
        return snapshot;
    }

    private void Accumulate(BacktestStage stage, TimeSpan elapsed)
    {
        _cumulative[stage] = _cumulative.TryGetValue(stage, out var prior)
            ? prior + elapsed
            : elapsed;
    }
}
