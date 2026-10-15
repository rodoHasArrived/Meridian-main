using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterProjectionWarmupLifecycleTests
{
    [Fact]
    public async Task LazyStartupWithoutNotifications_SeedsDurableIdentifiersWithoutWarmingCache()
    {
        var (warmup, cache, seed, store) = CreateWarmup();
        using (warmup)
        {
            await warmup.StartAsync(CancellationToken.None);

            seed.TryGetSecurityId("DURABLE").Should().NotBeNull();
            cache.Snapshot().Should().BeEmpty();
            await store.Received(1).LoadAllAsync(Arg.Any<CancellationToken>());
            await store.DidNotReceive().GetProjectionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
            await warmup.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LazyStartupFailure_RetriesWithoutNotificationsOrPeriodicRefresh(bool durableReadFails)
    {
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        var (warmup, cache, seed, store) = CreateWarmup(registry);
        var attempts = 0;
        if (durableReadFails)
        {
            var durable = Projection();
            store.LoadAllAsync(Arg.Any<CancellationToken>())
                .Returns(_ => Interlocked.Increment(ref attempts) == 1
                    ? Task.FromException<IReadOnlyList<SecurityProjectionRecord>>(new IOException("store temporarily unavailable"))
                    : Task.FromResult<IReadOnlyList<SecurityProjectionRecord>>([durable]));
        }
        else
        {
            registry.RegisterBatchAsync(Arg.Any<IEnumerable<CanonicalSymbolDefinition>>(), Arg.Any<CancellationToken>())
                .Returns(_ => Interlocked.Increment(ref attempts) == 1
                    ? Task.FromException<int>(new IOException("registry temporarily unavailable"))
                    : Task.FromResult(1));
        }

        using (warmup)
        {
            await warmup.StartAsync(CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (seed.TryGetSecurityId("DURABLE") is null && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            seed.TryGetSecurityId("DURABLE").Should().NotBeNull();
            attempts.Should().BeGreaterThanOrEqualTo(2);
            cache.Snapshot().Should().BeEmpty();
            await warmup.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StoppingWarmup_CancelsAnInflightRecoverySeed()
    {
        var (warmup, _, _, store) = CreateWarmup();
        var retryStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(async call =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new IOException("startup store failure");
            var token = call.Arg<CancellationToken>();
            retryStarted.TrySetResult(token);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return (IReadOnlyList<SecurityProjectionRecord>)Array.Empty<SecurityProjectionRecord>();
        });

        using (warmup)
        {
            await warmup.StartAsync(CancellationToken.None);
            var recoveryToken = await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            await warmup.StopAsync(CancellationToken.None);

            recoveryToken.IsCancellationRequested.Should().BeTrue();
        }
    }

    private static (SecurityMasterProjectionWarmupService Warmup, SecurityMasterProjectionCache Cache,
        SecurityMasterCanonicalSymbolSeedService Seed, ISecurityMasterStore Store) CreateWarmup(ICanonicalSymbolRegistry? registry = null)
    {
        var options = new SecurityMasterOptions
        {
            PreloadProjectionCache = false,
            ProjectionCacheNotificationsEnabled = false,
            ProjectionCacheRefreshMinutes = 0
        };
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { Projection() });
        var cache = new SecurityMasterProjectionCache();
        registry ??= Substitute.For<ICanonicalSymbolRegistry>();
        var seed = new SecurityMasterCanonicalSymbolSeedService(cache, registry,
            NullLogger<SecurityMasterCanonicalSymbolSeedService>.Instance, store, options);
        var rebuilder = new SecurityMasterAggregateRebuilder(Substitute.For<ISecurityMasterEventStore>(),
            Substitute.For<ISecurityMasterSnapshotStore>());
        var projectionService = new SecurityMasterProjectionService(store, cache, rebuilder,
            NullLogger<SecurityMasterProjectionService>.Instance);
        return (new SecurityMasterProjectionWarmupService(projectionService, options,
            NullLogger<SecurityMasterProjectionWarmupService>.Instance, seed)
        {
            RefreshRetryDelay = TimeSpan.FromMilliseconds(10)
        }, cache, seed, store);
    }

    private static SecurityProjectionRecord Projection()
        => SecurityMasterProjectionChangeNotificationTests.CreateProjection(Guid.NewGuid(), "Durable", 1)
            with
        { PrimaryIdentifierValue = "DURABLE" };
}
