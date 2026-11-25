using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.Backfill;
using Meridian.Application.Scheduling;
using Meridian.Application.UI;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Stooq;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Http;
using Meridian.Storage.Archival;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using BackfillCoordinator = Meridian.Application.Backfill.BackfillCoordinator;
using BackfillExecutionContractProjection = Meridian.Ui.Shared.Services.BackfillExecutionContractProjection;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;

namespace Meridian.Tests.Application.Backfill;

public sealed class WorkstationBackfillQuotaTests : IDisposable
{
    private const string Tenant = "research-tenant";
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-backfill-quota-").FullName;
    private static readonly DateOnly Day = new(2024, 1, 16);

    [Fact]
    public async Task PreviewUsesTheSameQuotaWindowAsQueuedRequestsAndRetainsObservedRetryEvidence()
    {
        using var responses = new QuotaResponses();
        var services = new ServiceCollection();
        services.AddHttpClient(HttpClientNames.StooqHistorical)
            .ConfigurePrimaryHttpMessageHandler(() => responses)
            .AddSharedResiliencePolicy();
        using var httpServices = services.BuildServiceProvider();
        using var client = httpServices.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientNames.StooqHistorical);
        var store = await CreateConfigAsync(new(ReserveHeadroomPercent: 50, MaxConcurrentRequests: 1));
        var clock = new QuotaClock();
        var window = TimeSpan.FromMinutes(1);
        using var budget = new RateLimiter(2, window, timeProvider: clock);
        using var registry = new ProviderRegistry();
        registry.Register(ProviderRuntimeProvenance.Attach(new MeteredProvider(client, budget), new TenantCredentialContext(Tenant)));
        using var coordinator = new BackfillCoordinator(store, registry);
        var history = new BackfillExecutionHistory(HistoryPath);
        using var queue = new WorkstationBackfillQueue(coordinator, history, NullLogger<WorkstationBackfillQueue>.Instance);
        var plan = new BackfillRequest("quota-test", ["SPY", "QQQ", "spy"], Day, Day);

        var preview = await coordinator.PreviewAsync(plan, Tenant, CancellationToken.None);
        responses.Dispatched.Should().BeEmpty();
        preview.Quota.ExpectedRequests.Should().Be(2);
        preview.Quota.RequestsPerWindow.Should().Be(2);
        preview.Quota.ObservedRemainingRequests.Should().Be(2);
        preview.Quota.BackfillRemainingRequests.Should().Be(1);
        preview.Quota.MaxConcurrentRequests.Should().Be(1);
        preview.Quota.ExceedsBudget.Should().BeTrue();

        var submissionId = Guid.NewGuid();
        var submitted = await queue.SubmitAsync(submissionId, Tenant, plan, null, null);
        submitted.Status.Should().Be("Queued");
        (await queue.SubmitAsync(submissionId, Tenant, plan, null, null)).ExecutionId.Should().Be(submitted.ExecutionId);
        var resumedPlan = plan with { ResumeFromCheckpoint = true };
        var differentResumeSetting = () => queue.SubmitAsync(submissionId, Tenant, resumedPlan, null, null);
        await differentResumeSetting.Should().ThrowAsync<BackfillSubmissionConflictException>();
        var otherTenantAttempt = () => queue.SubmitAsync(submissionId, "other-tenant", plan, null, null);
        await otherTenantAttempt.Should().ThrowAsync<BackfillSubmissionConflictException>();
        var otherTenantPreview = () => coordinator.PreviewAsync(plan, "other-tenant", CancellationToken.None);
        await otherTenantPreview.Should().ThrowAsync<InvalidOperationException>();
        var otherTenantSubmission = () => queue.SubmitAsync(Guid.NewGuid(), "other-tenant", plan, null, null);
        await otherTenantSubmission.Should().ThrowAsync<InvalidOperationException>();
        history.GetRecentExecutions(workstationTenant: "other-tenant").Should().BeEmpty();

        await queue.StartAsync(CancellationToken.None);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            (await clock.ReleaseNextWaitAsync(deadline.Token)).Should().Be(window);
            (await clock.ReleaseNextWaitAsync(deadline.Token)).Should().Be(window);
            while (history.GetExecution(submitted.ExecutionId)?.CompletedAt is null)
                await Task.Delay(20, deadline.Token);

            var splitGap = resumedPlan with { Symbols = ["SPY"], From = Day.AddDays(-1), To = Day.AddDays(1) };
            var refreshPreview = await coordinator.PreviewAsync(splitGap with { ResumeFromCheckpoint = false });
            refreshPreview.Quota.ExpectedRequests.Should().Be(1);
            var splitPreview = await coordinator.PreviewAsync(splitGap);
            splitPreview.Quota.ExpectedRequests.Should().Be(2);
            splitPreview.Quota.ExceedsBudget.Should().BeTrue();

            var resumed = await queue.SubmitAsync(Guid.NewGuid(), Tenant, resumedPlan, null, null, deadline.Token);
            resumed.Quota.ExpectedRequests.Should().Be(0);
            while (history.GetExecution(resumed.ExecutionId)?.CompletedAt is null)
                await Task.Delay(20, deadline.Token);
            var resumedResult = history.GetExecution(resumed.ExecutionId)!;
            resumedResult.Status.Should().Be(ExecutionStatus.Completed);
            resumedResult.QuotaReceipt!.ActualRequests.Should().Be(0);
            resumedResult.QuotaReceipt.RequestVariance.Should().Be(0);
            resumedResult.Statistics.TotalBarsRetrieved.Should().Be(0);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }

        var durableHistory = new BackfillExecutionHistory(HistoryPath);
        var completed = durableHistory.GetRecentExecutions(workstationTenant: Tenant)
            .Should().ContainSingle(execution => execution.ExecutionId == submitted.ExecutionId).Which;
        completed.Status.Should().Be(ExecutionStatus.Completed);
        completed.Statistics.TotalBarsRetrieved.Should().Be(2);
        completed.BatchReceipts.Should().HaveCount(2).And
            .OnlyContain(batch => batch.ServingProvider == "quota-test" && batch.ReceivedSessionCount == 1);
        completed.QuotaReceipt.Should().NotBeNull();
        var receipt = completed.QuotaReceipt!;
        receipt.ObservedProviders.Should().Equal("quota-test");
        receipt.ProvidersThatReturnedBars.Should().Equal("quota-test");
        receipt.ActualRequests.Should().Be(3);
        receipt.RequestVariance.Should().Be(1);
        receipt.DurationVarianceSeconds.Should().Be(receipt.ActualDurationSeconds - submitted.Quota.EstimatedDurationSeconds);
        receipt.ThrottlingEvents.Should().ContainSingle(e => e.Reason == "provider-rate-limit")
            .Which.WaitSeconds.Should().Be(1);
        receipt.ThrottlingEvents.Should().Contain(e => e.Reason == "quota-admission" && e.WaitSeconds == window.TotalSeconds);
        var healthHistory = ProviderRateLimitHistory.ForDataRoot(store.GetDataRoot(), new AtomicFileWriterAdapter());
        var healthEvent = healthHistory.Read("quota-test", 1).Should()
            .ContainSingle(entry => entry.EventType == "rate-limited").Which;
        healthEvent.HttpStatus.Should().Be(429);
        healthEvent.RequestsUsed.Should().Be(1);
        healthHistory.TryReadDailyRollups("quota-test", out var rollups).Should().BeTrue();
        rollups.Should().ContainSingle().Which.ThrottleCount.Should().Be(1);
        var dispatches = responses.Dispatched.ToArray();
        dispatches.Should().HaveCount(3);
        Stopwatch.GetElapsedTime(dispatches[0], dispatches[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(950));

        var projected = BackfillExecutionContractProjection.Build([completed], "stooq").Executions.Single();
        projected.QuotaReceipt!.ActualRequests.Should().Be(3);
        projected.BatchReceipts.Should().BeEquivalentTo(completed.BatchReceipts);
        var provenanceStore = coordinator.GetReceiptStore();
        provenanceStore.ReadRecentRetainedResults(10, Tenant).Should().HaveCount(2);
        provenanceStore.ReadRecentRetainedResults(10, "other-tenant").Should().BeEmpty();
        coordinator.TryReadLast().Should().BeNull();
    }

    [Fact]
    public async Task UnavailableQuotaHasNoEstimateAndScheduledApprovalIsPersisted()
    {
        using var registry = new ProviderRegistry();
        registry.Register(new StooqHistoricalDataProvider());
        registry.Register(new MeteredProvider());
        var store = await CreateConfigAsync(new(), new(ApprovedEquivalentProviders: ["quota-test"]));
        using var coordinator = new BackfillCoordinator(store, registry);
        var history = new BackfillExecutionHistory(HistoryPath);
        using var queue = new WorkstationBackfillQueue(coordinator, history, NullLogger<WorkstationBackfillQueue>.Instance);
        var request = new BackfillRequest("stooq", ["SPY"], Day, Day);

        var preview = await coordinator.PreviewAsync(request);
        preview.Quota.RequestsPerWindow.Should().BeNull();
        preview.Quota.ExpectedRequests.Should().BeNull();
        preview.Quota.ObservedRemainingRequests.Should().BeNull();
        preview.EstimatedDurationSeconds.Should().BeNull();
        preview.ApprovedAlternatives.Should().ContainSingle().Which.Provider.Should().Be("quota-test");

        var scheduledAt = DateTimeOffset.UtcNow.AddHours(1);
        var scheduled = await queue.SubmitAsync(Guid.NewGuid(), Tenant, request with { Provider = "quota-test" }, scheduledAt, "stooq");
        scheduled.Status.Should().Be("Scheduled");
        var reloaded = new BackfillExecutionHistory(HistoryPath).GetRecentExecutions(workstationTenant: Tenant).Single();
        reloaded.ScheduledAt.Should().Be(scheduledAt);
        reloaded.WorkstationSubmission!.AlternativeFor.Should().Be("stooq");

        var unapproved = () => queue.SubmitAsync(Guid.NewGuid(), Tenant, request, scheduledAt, "quota-test");
        (await unapproved.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*no longer approved*");
    }

    [Fact]
    public async Task DailyCreditHeadroomCanBlockAPlanThatFitsTheShortWindow()
    {
        using var budget = new RateLimiter(new RequestBudgetConfiguration(10, TimeSpan.FromMinutes(1), TimeSpan.Zero,
            DailyMaximum: 4, Unit: "credits"));
        budget.RecordUnits(3);
        using var registry = new ProviderRegistry();
        registry.Register(new MeteredProvider(quota: budget));
        using var coordinator = new BackfillCoordinator(await CreateConfigAsync(new(ReserveHeadroomPercent: 20)), registry);

        var preview = await coordinator.PreviewAsync(new BackfillRequest("quota-test", ["SPY"], Day, Day));
        preview.Quota.ExpectedRequests.Should().Be(1);
        preview.Quota.QuotaUnit.Should().Be("credits");
        preview.Quota.BackfillRequestsPerWindow.Should().Be(8);
        preview.Quota.ObservedDailyRemaining.Should().Be(1);
        preview.Quota.DailyBackfillAllowance.Should().Be(3);
        preview.Quota.BackfillRemainingRequests.Should().Be(0);
        preview.Quota.ExceedsBudget.Should().BeTrue();

        using var cancellation = new CancellationTokenSource();
        var wait = TimeSpan.Zero;
        var attempt = () => budget.WaitForUnitsAsync(1, 20, delay =>
        {
            wait = delay;
            cancellation.Cancel();
        }, cancellation.Token);
        await attempt.Should().ThrowAsync<OperationCanceledException>();
        wait.Should().BeGreaterThan(TimeSpan.FromHours(23));
        budget.GetBudgetStatus().DailyUnitsUsed.Should().Be(3);
    }

    private string HistoryPath => Path.Combine(_root, "backfill-history.json");

    private async Task<ConfigStore> CreateConfigAsync(BackfillOperationalBudgetConfig meteredBudget,
        BackfillOperationalBudgetConfig? stooqBudget = null)
    {
        var store = new ConfigStore(Path.Combine(_root, "appsettings.json"));
        await store.SaveAsync(new AppConfig(DataRoot: Path.Combine(_root, "data"),
            Backfill: new BackfillConfig(EnableSymbolResolution: false,
                OperationalBudgets: new() { ["quota-test"] = meteredBudget, ["stooq"] = stooqBudget ?? new() })));
        return store;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class QuotaClock : TimeProvider
    {
        private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);
        private readonly Channel<TimeSpan> _admissionWaits = Channel.CreateUnbounded<TimeSpan>();

        public override DateTimeOffset GetUtcNow() => _clock.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _clock.CreateTimer(callback, state, dueTime, period);
            _admissionWaits.Writer.TryWrite(dueTime);
            return timer;
        }

        public async Task<TimeSpan> ReleaseNextWaitAsync(CancellationToken ct)
        {
            var wait = await _admissionWaits.Reader.ReadAsync(ct);
            _clock.Advance(wait);
            return wait;
        }
    }

    private sealed class TenantCredentialContext(string tenantId) : ICredentialContext
    {
        public string? ProvenanceTenantId => tenantId;
        public string? Get(string name) => null;
        public bool IsConfigured(string name) => false;
    }

    private sealed class MeteredProvider(HttpClient? client = null, RateLimiter? quota = null) : BaseHistoricalDataProvider(client)
    {
        public override string Name => "quota-test";
        public override string DisplayName => "Quota test provider";
        public override string Description => "Deterministic provider for quota testing.";
        protected override string HttpClientName => HttpClientNames.StooqHistorical;
        protected override RateLimiter RequestRateLimiter => quota ?? RateLimiter;
        public override int MaxRequestsPerWindow => 2;
        public override TimeSpan RateLimitWindow => TimeSpan.FromMilliseconds(400);

        public override Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            await ExecuteGetAndReadAsync($"https://quota.test/{symbol}", symbol, "bars", ct);
            return [new HistoricalBar(symbol, Day, 100m, 102m, 99m, 101m, 1_000, Source: Name, SequenceNumber: 1)];
        }
    }

    private sealed class QuotaResponses : HttpMessageHandler
    {
        private int _requests;
        public ConcurrentQueue<long> Dispatched { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Dispatched.Enqueue(Stopwatch.GetTimestamp());
            if (Interlocked.Increment(ref _requests) != 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("bars") });

            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
            return Task.FromResult(limited);
        }
    }
}
