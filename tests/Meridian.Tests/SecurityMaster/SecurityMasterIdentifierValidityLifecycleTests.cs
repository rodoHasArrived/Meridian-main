using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterIdentifierValidityLifecycleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ClockOnlyBoundaries_ActivateAndExpireAliasesProviderMappingsAndTypedIdentifiers_WithDefaultLazyOptions()
    {
        await using var host = new Host();
        host.Record = host.Record with
        {
            Aliases = [Alias(host.Id, "GENERIC", T0.AddSeconds(10), T0.AddSeconds(20)),
                Alias(host.Id, "VENDOR.A", T0.AddSeconds(10), T0.AddSeconds(20), "vendor")],
            Identifiers = [new(SecurityIdentifierKind.Isin, "US0000000001", false, T0.AddSeconds(10), T0.AddSeconds(20)),
                new(SecurityIdentifierKind.Cusip, "123456789", false, T0.AddSeconds(10), T0.AddSeconds(20)),
                new(SecurityIdentifierKind.Figi, "BBG000VALID", false, T0.AddSeconds(10), T0.AddSeconds(20)),
                new(SecurityIdentifierKind.Sedol, "1234567", false, T0.AddSeconds(10), T0.AddSeconds(20))]
        };
        await host.StartAsync();
        await host.WaitForTimerAsync(T0.AddSeconds(10));

        foreach (var value in new[] { "GENERIC", "VENDOR.A", "US0000000001", "123456789", "BBG000VALID", "1234567" })
            host.Registry.ResolveToCanonical(value).Should().BeNull();
        host.Registry.GetProviderSymbol("VALIDITY", "vendor").Should().BeNull();
        host.Clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
        host.Registry.ResolveToCanonical("GENERIC").Should().BeNull("ValidFrom is not active before its exact boundary");

        host.Clock.Advance(TimeSpan.FromTicks(1));
        await UntilAsync(() => host.Registry.GetProviderSymbol("VALIDITY", "vendor") == "VENDOR.A");
        foreach (var value in new[] { "GENERIC", "US0000000001", "123456789", "BBG000VALID", "1234567" })
            host.Registry.ResolveToCanonical(value).Should().Be("VALIDITY");
        var active = host.Registry.GetDefinition("VALIDITY")!;
        active.Isin.Should().Be("US0000000001");
        active.Cusip.Should().Be("123456789");
        active.Figi.Should().Be("BBG000VALID");
        active.Sedol.Should().Be("1234567");
        await host.WaitForTimerAsync(T0.AddSeconds(20));

        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await UntilAsync(() => host.Registry.GetProviderSymbol("VALIDITY", "vendor") is null);
        foreach (var value in new[] { "GENERIC", "US0000000001", "123456789", "BBG000VALID", "1234567" })
            host.Registry.ResolveToCanonical(value).Should().BeNull("ValidTo is exclusive and no projection was written");
        var expired = host.Registry.GetDefinition("VALIDITY")!;
        expired.Isin.Should().BeNull();
        expired.Cusip.Should().BeNull();
        expired.Figi.Should().BeNull();
        expired.Sedol.Should().BeNull();
        expired.AliasDefinitions.Should().Contain(alias => alias.Alias == "GENERIC" && !alias.IsActive
            && alias.ValidFrom == T0.AddSeconds(10) && alias.ValidTo == T0.AddSeconds(20));
        host.Cache.Snapshot().Should().BeEmpty();
        host.Reads.Should().Be(3, "only initial seed and the two boundaries read durable membership");
        host.Seed.TryGetSecurityId("VALIDITY").Should().Be(host.Id);
    }

    [Fact]
    public async Task SuccessfulWriteSeed_WakesALaterTimer_ForNewEarlierBoundary()
    {
        await using var host = new Host();
        host.Record = host.Record with { Aliases = [Alias(host.Id, "LATER", T0.AddSeconds(100))] };
        await host.StartAsync();
        await host.WaitForTimerAsync(T0.AddSeconds(100));

        host.Record = host.Record with { Aliases = [Alias(host.Id, "EARLIER", T0.AddSeconds(10))] };
        await host.Seed.SeedAsync();
        await host.WaitForTimerAsync(T0.AddSeconds(10));
        host.Clock.Advance(TimeSpan.FromSeconds(10));

        await UntilAsync(() => host.Registry.ResolveToCanonical("EARLIER") == "VALIDITY");
        host.Registry.ResolveToCanonical("LATER").Should().BeNull();
        host.Reads.Should().Be(3);
        host.Cache.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task FailedBoundarySeed_RetriesWithBackoff_AndDoesNotSpinOrLoseTheBoundary()
    {
        await using var host = new Host();
        host.Record = host.Record with { Aliases = [Alias(host.Id, "RETRY", T0.AddSeconds(10))] };
        await host.StartAsync();
        await host.WaitForTimerAsync(T0.AddSeconds(10));
        host.FailReads = true;
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await UntilAsync(() => host.Reads == 2);
        await host.WaitForTimerAsync(T0.AddSeconds(20));
        host.Registry.ResolveToCanonical("RETRY").Should().BeNull();
        host.Seed.NextValidityTransitionUtc.Should().Be(T0.AddSeconds(10), "failed publication must retain its pending boundary");

        host.Clock.Advance(TimeSpan.FromSeconds(9));
        host.Reads.Should().Be(2, "a due boundary must not bypass retry backoff");
        host.FailReads = false;
        host.Clock.Advance(TimeSpan.FromSeconds(1));

        await UntilAsync(() => host.Registry.ResolveToCanonical("RETRY") == "VALIDITY"
            && host.Seed.NextValidityTransitionUtc is null);
        host.Reads.Should().Be(3);
        host.Seed.NextValidityTransitionUtc.Should().BeNull();
    }

    [Fact]
    public async Task Stop_CancelsPendingValidityTimer_AndPreventsLaterReads()
    {
        await using var host = new Host();
        host.Record = host.Record with { Aliases = [Alias(host.Id, "STOPPED", T0.AddSeconds(10))] };
        await host.StartAsync();
        await host.WaitForTimerAsync(T0.AddSeconds(10));

        await host.Warmup.StopAsync(CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromSeconds(20));

        host.Reads.Should().Be(1);
        host.Registry.ResolveToCanonical("STOPPED").Should().BeNull();
        host.Clock.PendingTimerCount.Should().Be(0);
    }

    [Fact]
    public async Task IdentifierBoundary_DoesNotPostponeConfiguredProjectionRefresh()
    {
        await using var host = new Host(projectionRefreshMinutes: 1);
        host.Record = host.Record with { Aliases = [Alias(host.Id, "BOUNDARY", T0.AddSeconds(10))] };
        await host.StartAsync();
        await host.WaitForTimerAsync(T0.AddSeconds(10));
        host.Clock.Advance(TimeSpan.FromSeconds(10));
        await UntilAsync(() => host.Registry.ResolveToCanonical("BOUNDARY") == "VALIDITY");
        await host.WaitForTimerAsync(T0.AddMinutes(1));

        host.Clock.Advance(TimeSpan.FromSeconds(50));

        await UntilAsync(() => host.Reads >= 3);
        host.Reads.Should().Be(3, "the configured refresh remains due one minute after startup, not one minute after the identifier seed");
    }

    private static SecurityAliasDto Alias(Guid id, string value, DateTimeOffset from, DateTimeOffset? to = null, string? provider = null)
        => new(Guid.NewGuid(), id, "Ticker", value, provider, SecurityAliasScope.Operations, null, "test", T0, from, to, true);

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(5);
        condition().Should().BeTrue("the asynchronous validity worker should complete after manual time advances");
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "sm-validity-" + Guid.NewGuid().ToString("N"));
        private int _reads;
        public Guid Id { get; } = Guid.NewGuid();
        public ManualClock Clock { get; } = new(T0);
        public SecurityProjectionRecord Record { get; set; }
        public bool FailReads { get; set; }
        public int Reads => Volatile.Read(ref _reads);
        public SecurityMasterProjectionCache Cache { get; } = new();
        public CanonicalSymbolRegistry Registry { get; }
        public SecurityMasterCanonicalSymbolSeedService Seed { get; }
        public SecurityMasterProjectionWarmupService Warmup { get; }

        public Host(int projectionRefreshMinutes = 0)
        {
            Record = SecurityMasterProjectionChangeNotificationTests.CreateProjection(Id, "Validity", 1)
                with
            { PrimaryIdentifierValue = "VALIDITY" };
            var store = Substitute.For<ISecurityMasterStore>();
            store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Interlocked.Increment(ref _reads);
                return FailReads
                    ? Task.FromException<IReadOnlyList<SecurityProjectionRecord>>(new IOException("temporary durable failure"))
                    : Task.FromResult<IReadOnlyList<SecurityProjectionRecord>>([Record]);
            });
            var options = new SecurityMasterOptions
            {
                PreloadProjectionCache = false,
                ProjectionCacheNotificationsEnabled = false,
                ProjectionCacheRefreshMinutes = projectionRefreshMinutes
            };
            Registry = new CanonicalSymbolRegistry(new SymbolRegistryService(_directory));
            Seed = new(Cache, Registry, NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance, store, options) { Clock = Clock };
            var rebuilder = new SecurityMasterAggregateRebuilder(Substitute.For<ISecurityMasterEventStore>(),
                Substitute.For<ISecurityMasterSnapshotStore>());
            var projection = new SecurityMasterProjectionService(store, Cache, rebuilder, NullLogger<SecurityMasterProjectionService>.Instance);
            Warmup = new(projection, options, NullLogger<SecurityMasterProjectionWarmupService>.Instance, Seed)
            { Clock = Clock, RefreshRetryDelay = TimeSpan.FromSeconds(5) };
        }

        public Task StartAsync() => Warmup.StartAsync(CancellationToken.None);
        public Task WaitForTimerAsync(DateTimeOffset due) => UntilAsync(() => Clock.HasTimerAt(due));
        public async ValueTask DisposeAsync()
        {
            await Warmup.StopAsync(CancellationToken.None);
            Warmup.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class ManualClock(DateTimeOffset initial) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly HashSet<ManualTimer> _timers = [];
        private DateTimeOffset _now = initial;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
        public override long GetTimestamp() { lock (_gate) return (_now - initial).Ticks; }
        public int PendingTimerCount { get { lock (_gate) return _timers.Count(timer => timer.Due.HasValue); } }
        public bool HasTimerAt(DateTimeOffset due) { lock (_gate) return _timers.Any(timer => timer.Due == due); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate)
            {
                _timers.Add(timer);
                timer.Change(dueTime, period);
            }
            return timer;
        }
        public void Advance(TimeSpan duration)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _now += duration;
                due = _timers.Where(timer => timer.Due <= _now).ToList();
                foreach (var timer in due)
                    timer.Due = timer.Period == Timeout.InfiniteTimeSpan ? null : _now + timer.Period;
            }
            foreach (var timer in due)
                timer.Callback(timer.State);
        }
        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            public TimerCallback Callback { get; } = callback;
            public object? State { get; } = state;
            public DateTimeOffset? Due { get; set; }
            public TimeSpan Period { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (!owner._timers.Contains(this))
                        return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                    Period = period;
                    return true;
                }
            }
            public void Dispose() { lock (owner._gate) { Due = null; owner._timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
