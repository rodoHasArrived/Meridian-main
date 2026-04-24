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
    private readonly Func<TimeSpan> _now;
    private readonly Dictionary<BacktestStage, TimeSpan> _cumulative = new();
    private readonly TimeSpan _totalStartedAt;
    private TimeSpan _stageStartedAt;
    private TimeSpan? _stoppedTotalElapsed;
    private TimeSpan? _stoppedStageElapsed;

    public StageTimer(BacktestStage initialStage = BacktestStage.ValidatingRequest, Func<TimeSpan>? now = null)
    {
        CurrentStage = initialStage;
        if (now is null)
        {
            var stopwatch = Stopwatch.StartNew();
            _now = () => stopwatch.Elapsed;
        }
        else
        {
            _now = now;
        }

        _totalStartedAt = _now();
        _stageStartedAt = _totalStartedAt;
    }

    public BacktestStage CurrentStage { get; private set; }

    public TimeSpan TotalElapsed => _stoppedTotalElapsed ?? _now() - _totalStartedAt;

    public TimeSpan StageElapsed => _stoppedStageElapsed ?? _now() - _stageStartedAt;

    /// <summary>
    /// Ends the current stage and starts a new one. The prior stage's elapsed
    /// time is accumulated into the cumulative totals. Transitioning to the
    /// same stage is a no-op so callers may idempotently request the current
    /// stage at phase boundaries.
    /// </summary>
    public void Transition(BacktestStage next)
    {
        if (next == CurrentStage || _stoppedTotalElapsed is not null)
            return;

        Accumulate(CurrentStage, StageElapsed);
        CurrentStage = next;
        _stageStartedAt = _now();
    }

    /// <summary>
    /// Stops all timers and folds the active stage's time into the cumulative
    /// totals. Idempotent.
    /// </summary>
    public void Stop()
    {
        if (_stoppedTotalElapsed is not null)
            return;

        var stageElapsed = StageElapsed;
        var totalElapsed = TotalElapsed;
        Accumulate(CurrentStage, stageElapsed);
        _stoppedStageElapsed = stageElapsed;
        _stoppedTotalElapsed = totalElapsed;
    }

    /// <summary>
    /// Snapshot of cumulative time spent in each observed stage at the moment
    /// this method is invoked. Includes the in-flight stage's current elapsed.
    /// </summary>
    public IReadOnlyDictionary<BacktestStage, TimeSpan> Cumulative()
    {
        var snapshot = new Dictionary<BacktestStage, TimeSpan>(_cumulative);
        if (_stoppedTotalElapsed is null)
        {
            snapshot[CurrentStage] = snapshot.TryGetValue(CurrentStage, out var prior)
                ? prior + StageElapsed
                : StageElapsed;
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
