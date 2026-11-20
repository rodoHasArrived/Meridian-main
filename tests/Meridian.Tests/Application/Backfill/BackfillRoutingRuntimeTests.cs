using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.UI;
using Meridian.Contracts.Backfill;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage.Archival;
using Microsoft.Extensions.DependencyInjection;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

public sealed class BackfillRoutingRuntimeTests : IDisposable
{
    private static readonly DateOnly Date = new(2026, 7, 1);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"routing-runtime-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstPriorityEditAppliesToNextSnapshotWithoutClaimingDefaultPacingChanged()
    {
        var config = new AppConfig(DataRoot: _directory);
        var yahoo = new RecordingProvider("yahoo", _ => [Bar("yahoo")]);
        var stooq = new RecordingProvider("stooq", _ => [Bar("stooq")]);
        await using var host = await CreateHostAsync(config, yahoo, stooq);
        var runtime = host.GetRequiredService<BackfillRoutingRuntime>();
        var store = host.GetRequiredService<ConfigStore>();
        await runtime.StartAsync(CancellationToken.None);
        var before = runtime.Current!;

        await store.UpdateAsync(current => current with
        {
            Backfill = new BackfillConfig(Providers: new BackfillProvidersConfig(Stooq: new StooqConfig(Priority: 1)))
        });

        before.DefaultOrder.Select(provider => provider.ProviderId).Should().Equal("yahoo", "stooq");
        runtime.Current!.DefaultOrder.Select(provider => provider.ProviderId).Should().Equal("stooq", "yahoo");
        runtime.Current.DefaultOrder[0].Provider.Should().BeSameAs(stooq);
        store.GetChangeStatus().PendingChanges.Should().BeEmpty();
        store.GetChangeStatus().LastApply!.Entries.Should().ContainSingle(entry => entry.Kind == ConfigurationApplyKind.AppliedLive);
    }

    [Fact]
    public async Task EnablingAnUnbuiltProviderRetainsThePriorSnapshot()
    {
        var config = new AppConfig(DataRoot: _directory, Backfill: new(Providers: new(Tiingo: new(Enabled: false))));
        await using var host = await CreateHostAsync(config, new RecordingProvider("stooq", _ => [Bar("stooq")]));
        var runtime = host.GetRequiredService<BackfillRoutingRuntime>();
        var store = host.GetRequiredService<ConfigStore>();
        await runtime.StartAsync(CancellationToken.None);
        var before = runtime.Current;

        await store.UpdateAsync(current => current with
        {
            Backfill = current.Backfill! with { Providers = new(Tiingo: new(Enabled: true), Stooq: new(Priority: 1)) }
        });

        runtime.Current.Should().BeSameAs(before);
        store.GetChangeStatus().RequiresRestart.Should().BeTrue();
        store.GetChangeStatus().LastApply!.Entries.Should().OnlyContain(entry => entry.Kind == ConfigurationApplyKind.Deferred);

        await store.UpdateAsync(current => current with
        {
            Backfill = current.Backfill! with { Providers = current.Backfill.Providers! with { Tiingo = new(Enabled: false) } }
        });

        runtime.Current.Should().NotBeSameAs(before);
        runtime.Current!.DefaultOrder.Should().ContainSingle().Which.Priority.Should().Be(1);
        store.GetChangeStatus().PendingChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task PreferredRouteRecordsActualCallsAndPreservesObservedDataPrecedence()
    {
        var first = new RecordingProvider("first", _ => [Bar("first")]);
        var throttled = new RecordingProvider("throttled", _ => throw new RateLimitException("fixture throttle", "throttled", retryAfter: TimeSpan.FromMinutes(1)));
        var fictional = new RecordingProvider("synthetic", _ => [Bar("synthetic")], isSimulated: true);
        using var admission = new BackfillRoutingAdmission([first, throttled, fictional], perProvider: 1, new ProviderRateLimitHistory());
        var attempts = new List<BackfillRoutingAttempt>();
        var snapshot = new BackfillRoutingSnapshot([new(first, 1), new(throttled, 2), new(fictional, 0)],
            new BackfillConfig(EnableRateLimitRotation: false), admission, DateTimeOffset.UtcNow);
        using var provider = snapshot.CreateComposite(["synthetic", "throttled"], null, Serilog.Log.Logger, null, attempts.Add);

        var bars = await provider.GetDailyBarsAsync("SPY", Date, Date);

        bars.Should().ContainSingle(bar => bar.Source == "first");
        attempts.Select(attempt => (attempt.ProviderId, attempt.Reason, attempt.Attempted))
            .Should().Equal(("throttled", "rate-limited", true), ("first", "completed", true));
        admission.GetStatus("synthetic")!.RequestsInWindow.Should().Be(0);
        admission.GetStatus("throttled")!.RequestsInWindow.Should().Be(1);
        admission.GetStatus("first")!.RequestsInWindow.Should().Be(1);
    }

    [Fact]
    public async Task UnavailableFirstPreferenceWithoutFallbackFailsBeforeFetch()
    {
        var provider = new RecordingProvider("alternative", _ => [Bar("alternative")]);
        using var admission = new BackfillRoutingAdmission([provider], perProvider: 1);
        var snapshot = new BackfillRoutingSnapshot([new(provider, 0)], new BackfillConfig(EnableFallback: false),
            admission, DateTimeOffset.UtcNow);
        using var route = snapshot.CreateComposite(["missing", "alternative"], null, Serilog.Log.Logger, null);

        await Assert.ThrowsAsync<AggregateException>(() => route.GetDailyBarsAsync("SPY", Date, Date));
        admission.GetStatus("alternative")!.RequestsInWindow.Should().Be(0);
    }

    [Fact]
    public async Task WaitingVendorSlotObservesCooldownRecordedByEarlierRequest()
    {
        var throttled = new GatedProvider();
        var fallback = new RecordingProvider("fallback", _ => [Bar("fallback")]);
        using var admission = new BackfillRoutingAdmission([throttled, fallback], perProvider: 1);
        var snapshot = new BackfillRoutingSnapshot([new(throttled, 0), new(fallback, 1)],
            new BackfillConfig(), admission, DateTimeOffset.UtcNow);
        var secondAttempts = new List<BackfillRoutingAttempt>();
        using var first = snapshot.CreateComposite([], null, Serilog.Log.Logger, null);
        using var second = snapshot.CreateComposite([], null, Serilog.Log.Logger, null, secondAttempts.Add);

        var firstFetch = first.GetDailyBarsAsync("SPY", Date, Date);
        await throttled.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondFetch = second.GetDailyBarsAsync("QQQ", Date, Date);
        throttled.Release.TrySetResult();
        await Task.WhenAll(firstFetch, secondFetch).WaitAsync(TimeSpan.FromSeconds(5));

        throttled.Calls.Should().Be(1);
        secondAttempts.Should().Contain(attempt => attempt.ProviderId == throttled.Name &&
            attempt.Reason == "skipped-rate-limited" && !attempt.Attempted);
        admission.GetStatus(throttled.Name)!.RequestsInWindow.Should().Be(1);
    }

    [Fact]
    public async Task QuotaWaitRechecksCooldownBeforeRecordingAnAttempt()
    {
        var throttled = new GatedProvider();
        var fallback = new RecordingProvider("stooq", _ => [Bar("stooq")]);
        using var admission = new BackfillRoutingAdmission([throttled, fallback], perProvider: 2);
        var snapshot = new BackfillRoutingSnapshot([new(throttled, 5), new(fallback, 20)],
            new BackfillConfig(), admission, DateTimeOffset.UtcNow);
        var beforeQuota = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseQuota = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var quotaAdmissions = new ConcurrentQueue<string>();
        var beforeThrottle = 0;

        using var observation = new HistoricalBackfillRequestScope(_ => 0, async (provider, ct) =>
        {
            if (provider == throttled && Interlocked.Increment(ref beforeThrottle) == 2)
            {
                beforeQuota.TrySetResult();
                await releaseQuota.Task.WaitAsync(ct);
            }
        }, _ => { }, _ => { }, (_, _, _) => { }, retryRateLimited: true, unmeteredFetchStarted: quotaAdmissions.Enqueue);

        using var first = snapshot.CreateComposite([], null, Serilog.Log.Logger, null);
        var attempts = new List<BackfillRoutingAttempt>();
        using var second = snapshot.CreateComposite([], null, Serilog.Log.Logger, null, attempts.Add);
        var provenance = new BackfillProvenanceCapture(second, DataGranularity.Daily, symbolTimelineResolution: false);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var started = DateTimeOffset.UtcNow;
        var firstFetch = first.GetDailyBarsAsync("SPY", Date, Date, deadline.Token);
        await throttled.Entered.Task.WaitAsync(deadline.Token);
        var secondFetch = second.GetDailyBarsAsync("QQQ", Date, Date, provenance, deadline.Token);
        await beforeQuota.Task.WaitAsync(deadline.Token);
        throttled.Release.TrySetResult();
        await firstFetch;
        releaseQuota.TrySetResult();
        await secondFetch;

        var request = BackfillProvenanceCapture.DescribeRequest(Date, Date, ["QQQ"], DataGranularity.Daily, 1);
        var receipt = provenance.Complete(request, started, DateTimeOffset.UtcNow);
        receipt.Providers.Select(provider => (provider.Priority, provider.Requests, provider.Unavailable))
            .Should().Equal((5, 0L, 1L), (20, 1L, 0L));
        quotaAdmissions.Should().Equal(throttled.Name, fallback.Name, fallback.Name);
        attempts.Select(attempt => (attempt.ProviderId, attempt.Reason, attempt.Attempted))
            .Should().Equal((throttled.Name, "skipped-rate-limited", false), (fallback.Name, "completed", true));
        admission.GetStatus(throttled.Name)!.RequestsInWindow.Should().Be(1);
    }

    [Fact]
    public async Task CoordinatorUsesLiveTenantRouteAndProtectsTheResultEvidence()
    {
        var restricted = ProviderRuntimeProvenance.Attach(new RecordingProvider("yahoo", _ => [Bar("yahoo")]),
            new TenantCredentials("owner"));
        var publicProvider = new RecordingProvider("stooq", _ => [Bar("stooq")]);
        await using var host = await CreateHostAsync(new AppConfig(DataRoot: _directory), publicProvider, restricted);
        var runtime = host.GetRequiredService<BackfillRoutingRuntime>();
        await runtime.StartAsync(CancellationToken.None);
        var store = host.GetRequiredService<ConfigStore>();
        using var coordinator = new BackfillCoordinator(store, host.GetRequiredService<ProviderRegistry>(),
            symbolResolver: new AliasResolver(), routingRuntime: runtime);

        var result = await coordinator.RunAsync(new BackfillRequest("composite", ["SPY"], Date, Date), "requester", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.BarsWritten.Should().Be(1);
        result.ProviderProvenance!.FallbackChain.Should().Equal("stooq");
        result.RoutingAttempts.Should().ContainSingle(attempt => attempt is { ProviderId: "stooq", Attempted: true });
        result.QuotaReceipt!.ObservedProviders.Should().Equal("stooq");
        result.ForTenant("requester").RoutingAttempts.Should().BeSameAs(result.RoutingAttempts);
        result.ForTenant("owner").RoutingAttempts.Should().BeNull();
        runtime.Admission!.GetStatus(restricted.Name)!.RequestsInWindow.Should().Be(0);
    }

    [Fact]
    public async Task QueuedJobKeepsItsCapturedRouteAndRetainsTheCanonicalSymbol()
    {
        var provider = new RecordingProvider("first", symbol => [new HistoricalBar(symbol, Date, 100, 101, 99, 100, 1_000, "first")]);
        var other = new RecordingProvider("other", _ => throw new InvalidOperationException("Unexpected provider."));
        var config = new BackfillConfig(Jobs: new BackfillJobsConfig(MaxConcurrentRequests: 1));
        using var admission = new BackfillRoutingAdmission([provider, other], perProvider: 1);
        var current = new BackfillRoutingSnapshot([new(provider, 12), new(other, 1)], config, admission, DateTimeOffset.UtcNow);
        var writer = new CapturingWriter();
        var factory = new BackfillServiceFactory(new AtomicFileWriterAdapter(), _ => writer,
            symbolResolver: new AliasResolver(), captureRouting: () => current);
        await using var services = factory.CreateServices(new AppConfig(DataRoot: _directory, Backfill: config), config,
            _directory, [provider, other]);
        var complete = new TaskCompletionSource<BackfillJob>(TaskCreationOptions.RunContinuationsAsynchronously);
        services.JobManager.OnJobStatusChanged += (job, _) =>
        {
            if (job.IsComplete)
                complete.TrySetResult(job);
        };
        var scheduled = await services.JobManager.CreateJobAsync("alias", ["SPY"], Date, Date.AddDays(1),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false }, preferredProviders: ["first"]);
        await services.JobManager.StartJobAsync(scheduled.JobId);
        current = new BackfillRoutingSnapshot([new(other, 0)], config, admission, DateTimeOffset.UtcNow);
        services.StartWorker();

        var result = await complete.Task.WaitAsync(TimeSpan.FromSeconds(10));

        writer.Bars.Should().ContainSingle(bar => bar.Symbol == "SPY" && bar.Source == "first");
        result.SymbolProgress["SPY"].ServedBy["first"].Should().Be(1L);
        result.SymbolProgress["SPY"].FilledDates.Should().ContainSingle().Which.Should().Be(Date);
        result.ProviderProvenance!.FallbackChain.Should().Equal(ProviderProvenanceLabels.ProviderId(provider.Name),
            ProviderProvenanceLabels.ProviderId(other.Name));
        result.ProviderProvenance.Providers[0].Priority.Should().Be(12);
        result.ProviderProvenance.Providers[0].Requests.Should().Be(1);
        admission.GetStatus("other")!.RequestsInWindow.Should().Be(0);
    }

    private async Task<ServiceProvider> CreateHostAsync(AppConfig config, params IHistoricalDataProvider[] providers)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "appsettings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, AppConfigJsonOptions.Write));
        return new ServiceCollection()
            .AddSingleton(new ConfigStore(path))
            .AddSingleton(new ProviderFactory(config, new NoCredentials()))
            .AddSingleton(_ =>
            {
                var registry = new ProviderRegistry();
                foreach (var provider in providers)
                    registry.Register(provider);
                return registry;
            })
            .AddSingleton<BackfillRoutingRuntime>()
            .BuildServiceProvider();
    }

    private static HistoricalBar Bar(string provider) => new("SPY", Date, 100, 101, 99, 100, 1_000, provider);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingProvider(string id, Func<string, IReadOnlyList<HistoricalBar>> read, bool isSimulated = false)
        : IHistoricalDataProvider
    {
        public string Name => id;
        public string DisplayName => id;
        public string Description => id;
        public bool IsSimulated => isSimulated;
        public int MaxRequestsPerWindow => 20;

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => Task.FromResult(read(symbol));
    }

    private sealed class NoCredentials : IProviderCredentialResolver, ICredentialContext
    {
        public ICredentialContext CreateContext(Type providerType, IReadOnlyDictionary<string, string?>? configuredValues = null)
            => this;
        public string? Get(string name) => null;
        public bool IsConfigured(string name) => false;
    }

    private sealed class TenantCredentials(string tenantId) : ICredentialContext
    {
        public string? ProvenanceTenantId => tenantId;
        public string? Get(string name) => null;
        public bool IsConfigured(string name) => false;
    }

    private sealed class GatedProvider : IHistoricalDataProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public string Name => "throttled";
        public string DisplayName => Name;
        public string Description => Name;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            throw new RateLimitException("fixture throttle", Name, retryAfter: TimeSpan.FromMinutes(1));
        }
    }

    private sealed class CapturingWriter : IBackfillBarWriter
    {
        public List<HistoricalBar> Bars { get; } = [];

        public Task<string> WriteAsync(DataGranularity granularity, IReadOnlyList<HistoricalBar> bars, string fallbackSource,
            CancellationToken ct = default)
        {
            Bars.AddRange(bars);
            return Task.FromResult("captured");
        }
    }

    private sealed class AliasResolver : ISymbolResolver
    {
        public string Name => "fixture";
        public Task<SymbolResolution?> ResolveAsync(string symbol, string? exchange = null, CancellationToken ct = default)
            => Task.FromResult<SymbolResolution?>(null);
        public Task<string?> MapSymbolAsync(string symbol, string fromProvider, string toProvider, CancellationToken ct = default)
            => Task.FromResult<string?>(symbol + ".alias");
        public Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SymbolSearchResult>>([]);
    }
}
