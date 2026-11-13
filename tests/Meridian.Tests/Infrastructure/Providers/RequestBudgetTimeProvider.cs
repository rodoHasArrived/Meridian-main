namespace Meridian.Tests.Infrastructure.Providers;

/// <summary>Manual timer clock for deterministic request-pacing and retry tests.</summary>
internal sealed class RequestBudgetTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
            return _now;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => GetUtcNow().Ticks;

    public int PendingTimers
    {
        get
        {
            lock (_sync)
                return _timers.Count(timer => timer.Due is not null && !timer.Disposed);
        }
    }

    public bool HasTimerDueWithin(TimeSpan delay)
    {
        lock (_sync)
            return _timers.Any(timer => !timer.Disposed && timer.Due is { } due && due <= _now + delay);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_sync)
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        List<ManualTimer> due;
        lock (_sync)
        {
            _now += elapsed;
            due = _timers.Where(timer => !timer.Disposed && timer.Due is { } at && at <= _now).ToList();
            foreach (var timer in due)
                timer.Due = timer.Period > TimeSpan.Zero ? _now + timer.Period : null;
        }
        foreach (var timer in due)
            timer.Fire();
    }

    public static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Delay(1, deadline.Token);
        }
    }

    private sealed class ManualTimer(RequestBudgetTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        internal DateTimeOffset? Due;
        internal TimeSpan Period;
        internal bool Disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._sync)
            {
                if (Disposed)
                    return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                Period = period;
                return true;
            }
        }

        internal void Fire()
        {
            lock (clock._sync)
            {
                if (Disposed)
                    return;
            }
            callback(state);
        }

        public void Dispose()
        {
            lock (clock._sync)
            {
                Disposed = true;
                Due = null;
                clock._timers.Remove(this);
            }
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
