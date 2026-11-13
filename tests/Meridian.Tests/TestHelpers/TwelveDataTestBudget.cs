using Meridian.Infrastructure.Adapters.Core;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Isolates request budgets in recorded-response tests. The clock advances through
/// short production pacing/retry delays; concurrency and cooldown tests use a manual clock.
/// </summary>
internal static class TwelveDataTestBudget
{
    public static RateLimiterRegistry Create() => new(timeProvider: new AdvancingTimeProvider());

    private sealed class AdvancingTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly HashSet<ScheduledTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
                return _now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ScheduledTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        private bool Schedule(ScheduledTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
                throw new NotSupportedException("This clock supports one-shot timers only.");
            lock (_sync)
            {
                if (timer.Disposed)
                    return false;
                timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _now + dueTime;
                if (timer.DueAt is null)
                    _timers.Remove(timer);
                else
                    _timers.Add(timer);
            }

            // Polly installs per-attempt and total deadline timers before sending. Keep
            // those scheduled rather than firing them merely because they were created.
            if (dueTime >= TimeSpan.Zero && dueTime < TimeSpan.FromSeconds(10))
                Advance(dueTime);
            return true;
        }

        private void Advance(TimeSpan by)
        {
            DateTimeOffset target;
            lock (_sync)
                target = _now + by;
            while (true)
            {
                ScheduledTimer? due;
                lock (_sync)
                {
                    due = _timers.Where(timer => timer.DueAt <= target)
                        .OrderBy(timer => timer.DueAt)
                        .FirstOrDefault();
                    if (due is null)
                    {
                        if (_now < target)
                            _now = target;
                        return;
                    }
                    _timers.Remove(due);
                    if (_now < due.DueAt!.Value)
                        _now = due.DueAt.Value;
                    due.DueAt = null;
                }
                due.Fire();
            }
        }

        private sealed class ScheduledTimer(AdvancingTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? DueAt { get; set; }
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) => owner.Schedule(this, dueTime, period);
            public void Fire() => callback(state);
            public void Dispose()
            {
                lock (owner._sync)
                {
                    Disposed = true;
                    owner._timers.Remove(this);
                }
            }
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
