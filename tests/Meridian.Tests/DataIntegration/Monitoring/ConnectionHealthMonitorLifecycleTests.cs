using FluentAssertions;
using Meridian.Core.Monitoring;
using Meridian.DataIntegration.Monitoring;
using Xunit;

namespace Meridian.Tests.DataIntegration.Monitoring;

public sealed class ConnectionHealthMonitorLifecycleTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HeartbeatScans_RunProviderPingsConcurrentlyWithoutOverlappingScans()
    {
        var clock = new ManualTimeProvider(Start);
        await using var monitor = CreateMonitor(clock, pingTimeoutSeconds: 10);
        var firstWaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePings = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activePings = 0;
        var maximumActivePings = 0;
        var totalStarts = 0;
        monitor.PingSender = async (_, ct) =>
        {
            var active = Interlocked.Increment(ref activePings);
            UpdateMaximum(ref maximumActivePings, active);
            if (Interlocked.Increment(ref totalStarts) == 3)
                firstWaveStarted.TrySetResult();

            try
            {
                await releasePings.Task.WaitAsync(ct);
                return false;
            }
            finally
            {
                Interlocked.Decrement(ref activePings);
            }
        };
        monitor.RegisterConnection("connection-1", "provider-1");
        monitor.RegisterConnection("connection-2", "provider-2");
        monitor.RegisterConnection("connection-3", "provider-3");
        clock.Advance(TimeSpan.FromSeconds(60));

        var firstScan = monitor.CheckHeartbeatsOnceAsync();
        await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondScan = monitor.CheckHeartbeatsOnceAsync();

        await Task.Yield();
        secondScan.IsCompleted.Should().BeFalse("the first scan still owns the scan gate");
        maximumActivePings.Should().Be(3, "providers should be pinged concurrently but scans must remain serialized");
        Volatile.Read(ref totalStarts).Should().Be(3, "the second scan must wait for the first scan gate");

        releasePings.TrySetResult();
        await Task.WhenAll(firstScan, secondScan).WaitAsync(TimeSpan.FromSeconds(2));
        totalStarts.Should().Be(6, "the serialized second scan should start its own complete provider wave");
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndDrainsCooperativeInFlightPing()
    {
        var clock = new ManualTimeProvider(Start);
        var monitor = CreateMonitor(clock, pingTimeoutSeconds: 30);
        var pingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pingCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.PingSender = async (_, ct) =>
        {
            pingStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                pingCancelled.TrySetResult();
                throw;
            }
        };
        monitor.RegisterConnection("connection-1", "provider-1");
        clock.Advance(TimeSpan.FromSeconds(60));
        var scan = monitor.CheckHeartbeatsOnceAsync();

        await pingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        await pingCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
    }

    [Fact]
    public async Task PingSender_CanRequestSynchronousDisposalWithoutSelfDeadlock()
    {
        var clock = new ManualTimeProvider(Start);
        var monitor = CreateMonitor(clock, pingTimeoutSeconds: 10);
        var disposeReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.PingSender = (_, _) =>
        {
            monitor.Dispose();
            disposeReturned.TrySetResult();
            return Task.FromResult(false);
        };
        monitor.RegisterConnection("connection-1", "provider-1");
        clock.Advance(TimeSpan.FromSeconds(60));

        var scan = monitor.CheckHeartbeatsOnceAsync();
        await disposeReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await IgnoreCancellationAsync(scan).WaitAsync(TimeSpan.FromSeconds(2));
        await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task HeartbeatEvent_CanRequestSynchronousDisposalWithoutSelfDeadlock()
    {
        var clock = new ManualTimeProvider(Start);
        var monitor = new ConnectionHealthMonitor(
            new ConnectionHealthConfig
            {
                HeartbeatIntervalSeconds = 100,
                HeartbeatTimeoutSeconds = 1,
                MaxMissedHeartbeats = 1,
                PingTimeoutSeconds = 10,
            },
            clock);
        var disposeReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.OnHeartbeatMissed += _ =>
        {
            monitor.Dispose();
            disposeReturned.TrySetResult();
        };
        monitor.RegisterConnection("connection-1", "provider-1");
        clock.Advance(TimeSpan.FromSeconds(2));

        var scan = monitor.CheckHeartbeatsOnceAsync();
        await disposeReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await IgnoreCancellationAsync(scan).WaitAsync(TimeSpan.FromSeconds(2));
        await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DisposeAsync_DoesNotWaitIndefinitelyForNonCooperativePingAndObservesLateFault()
    {
        var clock = new ManualTimeProvider(Start);
        var monitor = CreateMonitor(clock, pingTimeoutSeconds: 30);
        var pingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lingeringPing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.PingSender = (_, _) =>
        {
            pingStarted.TrySetResult();
            return lingeringPing.Task;
        };
        monitor.RegisterConnection("connection-1", "provider-1");
        clock.Advance(TimeSpan.FromSeconds(60));
        var scan = monitor.CheckHeartbeatsOnceAsync();

        await pingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);

        lingeringPing.TrySetException(new InvalidOperationException("late ping failure"));
        await Task.Yield();
    }

    [Fact]
    public async Task HeartbeatMissed_ReRegistrationDuringCallbackDoesNotDisconnectReplacement()
    {
        var clock = new ManualTimeProvider(Start);
        await using var monitor = new ConnectionHealthMonitor(
            new ConnectionHealthConfig
            {
                HeartbeatIntervalSeconds = 100,
                HeartbeatTimeoutSeconds = 1,
                MaxMissedHeartbeats = 1,
                PingTimeoutSeconds = 10,
            },
            clock);
        ConnectionLostEvent? lostEvent = null;
        monitor.OnHeartbeatMissed += _ =>
        {
            monitor.UnregisterConnection("connection-1");
            monitor.RegisterConnection("connection-1", "replacement-provider");
        };
        monitor.OnConnectionLost += evt => lostEvent = evt;
        monitor.RegisterConnection("connection-1", "original-provider");
        clock.Advance(TimeSpan.FromSeconds(2));

        await monitor.CheckHeartbeatsOnceAsync().WaitAsync(TimeSpan.FromSeconds(2));

        var status = monitor.GetConnectionStatus("connection-1");
        status.Should().NotBeNull();
        status!.Value.ProviderName.Should().Be("replacement-provider");
        status.Value.IsConnected.Should().BeTrue();
        status.Value.MissedHeartbeats.Should().Be(0);
        lostEvent.Should().BeNull("the disconnected generation was replaced during the missed-heartbeat callback");
    }

    [Fact]
    public async Task InFlightPing_FromUnregisteredGenerationCannotCreditReplacement()
    {
        var clock = new ManualTimeProvider(Start);
        await using var monitor = CreateMonitor(clock, pingTimeoutSeconds: 30);
        var pingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldPing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.PingSender = (_, _) =>
        {
            pingStarted.TrySetResult();
            return oldPing.Task;
        };
        monitor.RegisterConnection("connection-1", "original-provider");
        clock.Advance(TimeSpan.FromSeconds(60));
        var scan = monitor.CheckHeartbeatsOnceAsync();
        await pingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        monitor.UnregisterConnection("connection-1");
        clock.Advance(TimeSpan.FromSeconds(1));
        var replacementRegisteredAt = clock.GetUtcNow();
        monitor.RegisterConnection("connection-1", "replacement-provider");
        clock.Advance(TimeSpan.FromSeconds(1));
        oldPing.TrySetResult(true);

        await scan.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Yield();

        var status = monitor.GetConnectionStatus("connection-1");
        status.Should().NotBeNull();
        status!.Value.ProviderName.Should().Be("replacement-provider");
        status.Value.LastHeartbeatTime.Should().Be(replacementRegisteredAt);
        status.Value.AverageLatencyMs.Should().Be(0);
    }

    [Fact]
    public async Task StatsCleanup_DoesNotEvictReactivatedOrReplacementConnection()
    {
        var clock = new ManualTimeProvider(Start);
        await using var monitor = new ConnectionHealthMonitor(
            new ConnectionHealthConfig
            {
                HeartbeatIntervalSeconds = 100,
                HeartbeatTimeoutSeconds = 1,
                PingTimeoutSeconds = 10,
            },
            clock);

        monitor.RegisterConnection("reactivated", "provider-1");
        monitor.MarkDisconnected("reactivated");
        monitor.RegisterConnection("replacement", "old-provider");
        monitor.MarkDisconnected("replacement");
        clock.Advance(TimeSpan.FromSeconds(3));

        monitor.UpdateStatsOnce(connectionId =>
        {
            if (connectionId == "reactivated")
            {
                monitor.MarkConnected(connectionId);
                return;
            }

            if (connectionId == "replacement")
            {
                monitor.UnregisterConnection(connectionId);
                monitor.RegisterConnection(connectionId, "new-provider");
            }
        });

        monitor.GetConnectionStatus("reactivated")!.Value.IsConnected.Should().BeTrue();
        var replacement = monitor.GetConnectionStatus("replacement");
        replacement.Should().NotBeNull();
        replacement!.Value.ProviderName.Should().Be("new-provider");
        replacement.Value.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task HeartbeatScan_TimesOutWhenPingSenderBlocksBeforeReturningTask()
    {
        var clock = new ManualTimeProvider(Start);
        var monitor = CreateMonitor(clock, pingTimeoutSeconds: 1);
        var releaseSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var senderExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.PingSender = (_, _) =>
        {
            pingStarted.TrySetResult();
            try
            {
                releaseSender.Task.GetAwaiter().GetResult();
                throw new InvalidOperationException("late synchronous ping failure");
            }
            finally
            {
                senderExited.TrySetResult();
            }
        };
        monitor.RegisterConnection("connection-1", "provider-1");
        clock.Advance(TimeSpan.FromSeconds(60));
        var scan = monitor.CheckHeartbeatsOnceAsync();

        try
        {
            await pingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(TimeSpan.FromSeconds(1));
            await scan.WaitAsync(TimeSpan.FromSeconds(2));
            await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            releaseSender.TrySetResult();
            if (pingStarted.Task.IsCompletedSuccessfully)
                await senderExited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task DisposeAsync_DoesNotWaitForPingSenderBlockedBeforeReturningTask()
    {
        var clock = new ManualTimeProvider(Start);
        var monitor = CreateMonitor(clock, pingTimeoutSeconds: 30);
        var releaseSender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.PingSender = (_, _) =>
        {
            pingStarted.TrySetResult();
            releaseSender.Task.GetAwaiter().GetResult();
            return Task.FromResult(false);
        };
        monitor.RegisterConnection("connection-1", "provider-1");
        clock.Advance(TimeSpan.FromSeconds(60));
        var scan = monitor.CheckHeartbeatsOnceAsync();

        try
        {
            await pingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        }
        finally
        {
            releaseSender.TrySetResult();
            await monitor.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task ConnectionTransitionEvents_UseNonZeroFixedClockDurations()
    {
        var clock = new ManualTimeProvider(Start);
        await using var monitor = CreateMonitor(clock, pingTimeoutSeconds: 10);
        ConnectionLostEvent? lostEvent = null;
        ConnectionRecoveredEvent? recoveredEvent = null;
        monitor.OnConnectionLost += evt => lostEvent = evt;
        monitor.OnConnectionRecovered += evt => recoveredEvent = evt;
        monitor.RegisterConnection("connection-1", "provider-1");

        clock.Advance(TimeSpan.FromMinutes(5));
        monitor.MarkDisconnected("connection-1", "test disconnect");
        clock.Advance(TimeSpan.FromMinutes(2));
        monitor.MarkConnected("connection-1");

        lostEvent.Should().NotBeNull();
        lostEvent!.Value.Timestamp.Should().Be(Start.AddMinutes(5));
        lostEvent.Value.UptimeDuration.Should().Be(TimeSpan.FromMinutes(5));
        recoveredEvent.Should().NotBeNull();
        recoveredEvent!.Value.Timestamp.Should().Be(Start.AddMinutes(7));
        recoveredEvent.Value.DowntimeDuration.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void PublicConstructor_PreservesSingleConfigurationParameter()
    {
        typeof(ConnectionHealthMonitor)
            .GetConstructor(new[] { typeof(ConnectionHealthConfig) })
            .Should()
            .NotBeNull();
    }

    [Fact]
    public void Constructor_RejectsNonPositivePingSenderConcurrencyLimit()
    {
        var config = ConnectionHealthConfig.Default with
        {
            MaxConcurrentPingSenderInvocations = 0,
        };

        Action act = () => _ = new ConnectionHealthMonitor(config);

        act.Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Maximum concurrent ping sender invocations must be positive*");
    }

    private static ConnectionHealthMonitor CreateMonitor(
        TimeProvider timeProvider,
        int pingTimeoutSeconds)
    {
        return new ConnectionHealthMonitor(
            new ConnectionHealthConfig
            {
                HeartbeatIntervalSeconds = 100,
                HeartbeatTimeoutSeconds = 1_000,
                PingTimeoutSeconds = pingTimeoutSeconds,
            },
            timeProvider);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Disposal is expected to cancel the scan that requested it.
        }
    }

    private static void UpdateMaximum(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly HashSet<ManualTimer> _timers = new();
        private long _utcTicks;
        private long _timestamp;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcTicks = utcNow.UtcTicks;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return new DateTimeOffset(_utcTicks, TimeSpan.Zero);
            }
        }

        public override long GetTimestamp()
        {
            lock (_sync)
            {
                return _timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_sync)
            {
                var timer = new ManualTimer(this, callback, state);
                _timers.Add(timer);
                timer.ChangeCore(_timestamp, dueTime, period);
                return timer;
            }
        }

        public void Advance(TimeSpan amount)
        {
            if (amount < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(amount));

            List<ManualTimer> callbacks = new();
            lock (_sync)
            {
                _utcTicks += amount.Ticks;
                _timestamp += amount.Ticks;
                foreach (var timer in _timers.ToArray())
                {
                    timer.CollectDueCallbacksCore(_timestamp, callbacks);
                }
            }

            foreach (var timer in callbacks)
            {
                timer.InvokeIfActive();
            }
        }

        private bool ChangeTimer(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                if (timer.IsDisposedCore)
                    return false;

                timer.ChangeCore(_timestamp, dueTime, period);
                return true;
            }
        }

        private void DisposeTimer(ManualTimer timer)
        {
            lock (_sync)
            {
                if (timer.IsDisposedCore)
                    return;

                timer.IsDisposedCore = true;
                timer.NextDueTimestampCore = null;
                _timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private readonly ManualTimeProvider _owner = owner;
            private readonly TimerCallback _callback = callback;
            private readonly object? _state = state;

            internal bool IsDisposedCore { get; set; }
            internal long? NextDueTimestampCore { get; set; }
            internal long PeriodTicksCore { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                return _owner.ChangeTimer(this, dueTime, period);
            }

            public void Dispose()
            {
                _owner.DisposeTimer(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void ChangeCore(long nowTimestamp, TimeSpan dueTime, TimeSpan period)
            {
                ValidateTimerInterval(dueTime, nameof(dueTime));
                ValidateTimerInterval(period, nameof(period));

                NextDueTimestampCore = dueTime == Timeout.InfiniteTimeSpan
                    ? null
                    : checked(nowTimestamp + dueTime.Ticks);
                PeriodTicksCore = period == Timeout.InfiniteTimeSpan
                    ? -1
                    : period.Ticks;
            }

            internal void CollectDueCallbacksCore(long nowTimestamp, List<ManualTimer> callbacks)
            {
                while (!IsDisposedCore &&
                       NextDueTimestampCore.HasValue &&
                       NextDueTimestampCore.Value <= nowTimestamp)
                {
                    callbacks.Add(this);
                    if (PeriodTicksCore <= 0)
                    {
                        NextDueTimestampCore = null;
                    }
                    else
                    {
                        NextDueTimestampCore = checked(NextDueTimestampCore.Value + PeriodTicksCore);
                    }
                }
            }

            internal void InvokeIfActive()
            {
                lock (_owner._sync)
                {
                    if (IsDisposedCore)
                        return;
                }

                _callback(_state);
            }

            private static void ValidateTimerInterval(TimeSpan value, string parameterName)
            {
                if (value < TimeSpan.Zero && value != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
