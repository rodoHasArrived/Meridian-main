using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Meridian.Application.Scheduling;
using Meridian.Application.UI;
using Meridian.Contracts.Backfill;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Http;
using Meridian.Storage.Backfill;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using BackfillCoordinator = Meridian.Application.Backfill.BackfillCoordinator;
using BackfillJobQueue = Meridian.Application.Backfill.BackfillJobQueue;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

public sealed class BackfillQuotaTests : IDisposable
{
    private const string Tenant = "quota-tests";
    private const string HistoricalClient = "quota-test-historical";
    private static readonly DateOnly Session = new(2025, 9, 12);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"backfill-quota-{Guid.NewGuid():N}");

    [Fact]
    public async Task PreviewUsesHistoricalBudgetAndAnInclusiveSingleDayPlan()
    {
        using var provider = new PagedProvider(new HttpClient(new ProviderResponses()));
        using var stooq = new StooqHistoricalDataProvider();
        using var coordinator = await CreateCoordinatorAsync(provider, stooq);
        provider.RecordEarlierRequest();

        var preview = await coordinator.PreviewAsync(Request("paged", "SPY", "spy", "QQQ"));
        var quota = preview.Quota!;
        quota.ExpectedRequests.Should().Be(2);
        quota.ProviderLimit.Should().Be(2);
        quota.ObservedRemainingRequests.Should().Be(1);
        quota.BackfillRequestsPerWindow.Should().Be(1);
        quota.AvailableBackfillRequests.Should().Be(0);
        quota.EstimatedDurationSeconds.Should().BeGreaterThan(0);
        quota.ExceedsBudget.Should().BeTrue();
        preview.ApprovedEquivalentProviders.Should().ContainSingle().Which.Provider.Should().Be("stooq");

        var unavailable = await coordinator.PreviewAsync(Request("stooq", "SPY"));
        unavailable.Quota!.ProviderLimit.Should().BeNull();
        unavailable.Quota.ObservedRemainingRequests.Should().BeNull();
        unavailable.Quota.ExpectedRequests.Should().BeNull();
        unavailable.EstimatedDurationSeconds.Should().BeNull();
        unavailable.Quota.ExceedsBudget.Should().BeFalse();
    }

    [Fact]
    public async Task QueuedRunPacesPagesAndRetriesAndRetainsObservedVariance()
    {
        var transport = new ProviderResponses();
        var services = new ServiceCollection();
        services.AddHttpClient(HistoricalClient)
            .ConfigurePrimaryHttpMessageHandler(() => transport)
            .AddSharedResiliencePolicy();
        using var httpServices = services.BuildServiceProvider();
        using var provider = new PagedProvider(httpServices.GetRequiredService<IHttpClientFactory>().CreateClient(HistoricalClient));
        using var coordinator = await CreateCoordinatorAsync(provider);
        var historyPath = Path.Combine(_root, "executions.json");
        var history = new BackfillExecutionHistory(historyPath);
        using var jobs = new BackfillJobQueue(coordinator, history, NullLogger<BackfillJobQueue>.Instance);
        var request = Request("paged", "SPY", "QQQ");
        var preview = await coordinator.PreviewAsync(request);
        var id = jobs.Prepare(Tenant, request, preview.Quota!);

        var unconfirmed = () => jobs.Submit(Tenant, new QueueBackfillRequest(id));
        unconfirmed.Should().Throw<InvalidOperationException>().WithMessage("*confirm queueing*");
        var submission = new QueueBackfillRequest(id, AcceptThrottle: true);
        var accepted = jobs.Submit(Tenant, submission);
        accepted.Status.Should().Be("Queued");
        jobs.Submit(Tenant, submission).ExecutionId.Should().Be(accepted.ExecutionId);
        jobs.Read("different-tenant", accepted.ExecutionId).Should().BeNull();
        history.GetRecentExecutionsForTenant("different-tenant", 50).Should().BeEmpty();
        transport.Sends.Should().Be(0);

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await jobs.StartAsync(deadline.Token);
        try
        {
            var receipt = jobs.Read(Tenant, accepted.ExecutionId)!;
            var sawThrottle = false;
            while (receipt.Status is "Queued" or "Running" or "Throttled")
            {
                sawThrottle |= receipt.Status == "Throttled";
                await Task.Delay(10, deadline.Token);
                receipt = jobs.Read(Tenant, accepted.ExecutionId)!;
            }
            receipt.Status.Should().Be("Completed");
            sawThrottle.Should().BeTrue();
            receipt.Actual!.ObservedProviders.Should().Equal("paged");
            receipt.Actual.ActualRequests.Should().Be(5);
            receipt.Actual.ActualRequests.Should().Be(transport.Sends);
            receipt.Actual.RequestVariance.Should().Be(3);
            receipt.Actual.DurationVarianceSeconds.Should().NotBeNull();
            receipt.Actual.ThrottlingEvents.Select(e => e.Reason)
                .Should().Contain(["headroom", "provider-rate-limit", "concurrency"]);
            receipt.BatchReceipts.Should().HaveCount(2);
            coordinator.TryReadSymbolCheckpoints().Should().BeNull();
            var dataRoot = Path.Combine(_root, "data");
            var archiveBatches = new BackfillStatusStore(dataRoot).Batches;
            foreach (var batch in receipt.BatchReceipts!)
            {
                var archived = await archiveBatches.ReadAsync(batch.RevisionId);
                archived!.Receipt.Should().BeEquivalentTo(batch);
            }
            new BackfillStatusStore(dataRoot, Tenant)
                .TryReadSymbolCoverage(DataGranularity.Daily, provider.Name, provider.CoverageFeed).Should().HaveCount(2);
            new BackfillStatusStore(dataRoot, "another-tenant")
                .TryReadSymbolCoverage(DataGranularity.Daily, provider.Name, provider.CoverageFeed).Should().BeNull();
            var retained = new BackfillExecutionHistory(historyPath).GetExecution(accepted.ExecutionId);
            retained!.QuotaReceipt.Should().BeEquivalentTo(receipt.Actual);
            retained.BatchReceipts.Should().BeEquivalentTo(receipt.BatchReceipts);
        }
        finally
        {
            await jobs.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task FutureJobRetainsTheReviewedScopeAcrossRestart()
    {
        using var provider = new PagedProvider(new HttpClient(new ProviderResponses()));
        using var coordinator = await CreateCoordinatorAsync(provider);
        var historyPath = Path.Combine(_root, "executions.json");
        var request = Request("paged", "SPY", "QQQ");
        var preview = await coordinator.PreviewAsync(request);
        var notBefore = DateTimeOffset.UtcNow.AddHours(1);
        string executionId;
        using (var jobs = new BackfillJobQueue(coordinator, new BackfillExecutionHistory(historyPath), NullLogger<BackfillJobQueue>.Instance))
        {
            var id = jobs.Prepare(Tenant, request, preview.Quota!);
            executionId = jobs.Submit(Tenant, new QueueBackfillRequest(id, NotBeforeUtc: notBefore)).ExecutionId;
        }

        using var restarted = new BackfillJobQueue(coordinator, new BackfillExecutionHistory(historyPath), NullLogger<BackfillJobQueue>.Instance);
        var job = restarted.Read(Tenant, executionId);
        job!.Status.Should().Be("Scheduled");
        job.NotBeforeUtc.Should().Be(notBefore);
        job.From.Should().Be(Session);
        job.To.Should().Be(Session);
        job.Symbols.Should().Equal("SPY", "QQQ");
        job.Actual.Should().BeNull();
    }

    private static BackfillRequest Request(string provider, params string[] symbols) =>
        new(provider, symbols, Session, Session);

    private async Task<BackfillCoordinator> CreateCoordinatorAsync(params IHistoricalDataProvider[] providers)
    {
        Directory.CreateDirectory(_root);
        var store = new ConfigStore(Path.Combine(_root, "appsettings.json"));
        await store.SaveAsync(new AppConfig(DataRoot: Path.Combine(_root, "data"), Backfill: new BackfillConfig(
            EnableFallback: false,
            Providers: new BackfillProvidersConfig(OpenFigi: new OpenFigiConfig(Enabled: false)),
            QuotaPolicies: new() { ["paged"] = new(50, 1, ["stooq"]) })));
        var registry = new ProviderRegistry();
        foreach (var provider in providers)
            registry.Register(provider);
        return new BackfillCoordinator(store, registry);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class PagedProvider(HttpClient client) : BaseHistoricalDataProvider(client)
    {
        public override string Name => "paged";
        public override string DisplayName => "Synthetic paged adapter";
        public override string Description => DisplayName;
        protected override string HttpClientName => HistoricalClient;
        public override int MaxRequestsPerWindow => 2;
        public override TimeSpan RateLimitWindow => TimeSpan.FromMilliseconds(500);

        public void RecordEarlierRequest() => RateLimiter.RecordRequest();

        public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            for (var page = 0; page < 2; page++)
                await ExecuteGetAndReadAsync($"https://example.test/{symbol}/{page}", symbol, "bars", ct);

            return [new HistoricalBar(symbol, Session, 100m, 101m, 99m, 100m, 1_000, Name)];
        }
    }

    private sealed class ProviderResponses : HttpMessageHandler
    {
        private int _sends;
        public int Sends => Volatile.Read(ref _sends);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref _sends);
            await Task.Delay(15, ct);
            if (attempt == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                return limited;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("synthetic") };
        }
    }
}
