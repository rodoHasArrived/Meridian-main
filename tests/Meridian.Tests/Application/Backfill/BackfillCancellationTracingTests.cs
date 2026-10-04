using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Archival;
using Meridian.Storage.Backfill;
using Meridian.Testing;

namespace Meridian.Tests.Backfill;

[Collection("Sequential")]
public sealed class BackfillCancellationTracingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Cancellation_RecordsStageErrorOnlyWhenWorkerTokenIsActive(
        bool storageStage, bool cancelWorker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var artifacts = TestArtifactDirectory.Create(nameof(BackfillCancellationTracingTests));
        var spans = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Meridian",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Enqueue
        };
        ActivitySource.AddActivityListener(listener);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new CancellationProvider(storageStage, cancelWorker, entered);
        var config = new BackfillConfig(
            EnableSymbolResolution: false, EnableRateLimitRotation: false,
            Jobs: new BackfillJobsConfig(PersistJobs: false, MaxConcurrentRequests: 1, MaxConcurrentPerProvider: 1));
        await using var services = new BackfillServiceFactory(new AtomicFileWriterAdapter(), root => new JsonlBackfillBarWriter(root)).CreateServices(
            new AppConfig(DataRoot: artifacts.RootPath), config, artifacts.RootPath, [provider]);
        if (storageStage)
        {
            // Bar notifications execute inside the storage stage after the durable append.
            // This seam injects a storage-stage timeout without replacing production IO.
            services.Worker.OnBarWritten += (_, _) =>
            {
                entered.TrySetResult();
                if (cancelWorker)
                    Task.Delay(Timeout.Infinite, provider.WorkerToken).GetAwaiter().GetResult();
                throw new OperationCanceledException("injected storage timeout");
            };
        }
        var request = new BackfillRequest
        {
            JobId = "cancellation-tracing",
            Symbol = "SPY",
            FromDate = CancellationProvider.Date,
            ToDate = CancellationProvider.Date,
            PreferredProviders = [provider.Name],
            MaxRetries = 0
        };
        await services.RequestQueue.EnqueueAsync(request, timeout.Token);
        services.Worker.Start();
        await entered.Task.WaitAsync(timeout.Token);
        if (cancelWorker)
            await services.Worker.StopAsync(timeout.Token);
        else
        {
            while (request.Status != BackfillRequestStatus.Failed)
                await Task.Delay(10, timeout.Token);
            await services.Worker.StopAsync(timeout.Token);
        }

        var stageName = storageStage ? "BackfillStorage.WriteBars" : $"BackfillFetch.{provider.Name}";
        var stage = spans.Should().ContainSingle(span => span.OperationName == stageName).Which;
        var attempt = spans.Should().ContainSingle(span => span.OperationName.StartsWith("Backfill.")).Which;
        stage.ParentSpanId.Should().Be(attempt.SpanId);
        if (cancelWorker)
        {
            request.Status.Should().Be(BackfillRequestStatus.Cancelled);
            stage.Status.Should().NotBe(ActivityStatusCode.Error);
            stage.Events.Should().NotContain(item => item.Name == "exception");
            attempt.Status.Should().NotBe(ActivityStatusCode.Error);
            attempt.GetTagItem("backfill.outcome").Should().Be("cancelled");
        }
        else
        {
            request.Status.Should().Be(BackfillRequestStatus.Failed);
            stage.Status.Should().Be(ActivityStatusCode.Error);
            stage.Events.Should().ContainSingle(item => item.Name == "exception");
            attempt.Status.Should().Be(ActivityStatusCode.Error);
            request.ErrorMessage.Should().Contain("timeout");
        }
    }

    private sealed class CancellationProvider(
        bool storageStage, bool cancelWorker, TaskCompletionSource entered) : IHistoricalDataProvider
    {
        public static readonly DateOnly Date = new(2026, 7, 1);
        public string Name => "cancellation-provider";
        public string DisplayName => Name;
        public string Description => "Deterministic backfill cancellation input.";
        public CancellationToken WorkerToken { get; private set; }

        public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            WorkerToken = ct;
            if (!storageStage)
            {
                entered.TrySetResult();
                if (cancelWorker)
                    await Task.Delay(Timeout.Infinite, ct);
                throw new OperationCanceledException("injected provider timeout");
            }
            return [new HistoricalBar(symbol, Date, 100m, 102m, 99m, 101m, 1000, Name)];
        }

        public void Dispose() { }
    }
}
