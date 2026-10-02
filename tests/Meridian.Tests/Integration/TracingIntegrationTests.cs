using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Composition;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Domain.Events;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Platform.Tracing;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Meridian.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using EnvironmentVariableScope = Meridian.Tests.Identity.EnvironmentVariableScope;

namespace Meridian.Tests.Integration;

// Activity listeners and deployment settings are process-wide. These tests must not borrow
// a provider or environment from another composition test.
[Collection("Sequential")]
public sealed class TracingIntegrationTests
{
    [Fact]
    public async Task EnabledHost_ExportsConnectedPipelineAndQueuedBackfillTraceOnShutdown()
    {
        using var environment = UseLocalEnvironment();
        using var artifacts = TestArtifactDirectory.Create(nameof(TracingIntegrationTests));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var exporter = new RecordingExporter();
        await using var host = await CreateHostAsync(artifacts.RootPath, true, exporter, timeout.Token);
        var pipeline = host.GetRequiredService<EventPipeline>();
        var backfill = host.GetRequiredService<BackfillServices>();
        ActivityContext parent;

        using (var operation = OpenTelemetrySetup.ActivitySource.StartActivity("operation.import"))
        {
            operation.Should().NotBeNull("the common host composition must install the listener");
            parent = operation!.Context;
            await pipeline.PublishAsync(CreateTradeEvent(), timeout.Token);
            var job = await backfill.JobManager.CreateJobAsync(
                "traced historical import", ["SPY"], BackfillDate, BackfillDate,
                options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false, MaxRetries = 0 },
                preferredProviders: [HistoricalProvider.ProviderName], ct: timeout.Token);
            await backfill.JobManager.StartJobAsync(job.JobId, timeout.Token);
        }

        await WaitUntilAsync(() => pipeline.ConsumedCount == 1 && backfill.RequestQueue.IsEmpty
            && backfill.JobManager.GetAllJobs().Single().Status == BackfillJobStatus.Completed, timeout.Token);
        await pipeline.FlushAsync(timeout.Token);
        exporter.Activities.Should().BeEmpty("the long-delay batch must remain buffered until host shutdown");
        host.ServiceProvider.GetServices<TracerProvider>().Should().ContainSingle();

        await host.DisposeAsync();

        var spans = exporter.Activities.Where(span => span.TraceId == parent.TraceId).ToArray();
        var operationSpan = spans.Should().ContainSingle(span => span.OperationName == "operation.import").Which;
        var process = spans.Should().ContainSingle(span => span.OperationName == "ProcessMarketEvent.Trade").Which;
        var storage = spans.Should().ContainSingle(span => span.OperationName == "StoreMarketEvent.JsonlStorageSink").Which;
        var worker = spans.Should().ContainSingle(span => span.OperationName == $"Backfill.{HistoricalProvider.ProviderName}").Which;
        var fetch = spans.Should().ContainSingle(span => span.OperationName == $"BackfillFetch.{HistoricalProvider.ProviderName}").Which;
        var backfillStorage = spans.Should().ContainSingle(span => span.OperationName == "BackfillStorage.WriteBars").Which;
        process.ParentSpanId.Should().Be(operationSpan.SpanId);
        storage.ParentSpanId.Should().Be(process.SpanId);
        worker.ParentSpanId.Should().Be(operationSpan.SpanId, "the job queue must capture its producer before the worker runs");
        fetch.ParentSpanId.Should().Be(worker.SpanId);
        backfillStorage.ParentSpanId.Should().Be(worker.SpanId);
        spans.Where(span => span.SpanId != operationSpan.SpanId).Should().OnlyContain(
            span => spans.Any(parentSpan => parentSpan.SpanId == span.ParentSpanId),
            "every exported operation span must have an inspectable parent");
        spans.Should().OnlyContain(span => span.Status != ActivityStatusCode.Error);
        spans.Select(span => span.SpanId).Should().OnlyHaveUniqueItems("one host must not duplicate exports");
        exporter.DisposeCount.Should().Be(1);
        ReadStoredEvents(artifacts.RootPath).Should().Contain("SPY").And.Contain(HistoricalProvider.ProviderName).And.Contain("Trade");
    }

    [Fact]
    public async Task EnabledHost_ExportsCorrelatedFailuresAndContinuesPipelineProcessing()
    {
        using var environment = UseLocalEnvironment();
        using var artifacts = TestArtifactDirectory.Create(nameof(TracingIntegrationTests));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var exporter = new RecordingExporter();
        var failure = new IOException("injected storage append failure");
        await using var host = await CreateHostAsync(
            artifacts.RootPath, true, exporter, timeout.Token,
            failBackfill: true, decorateSink: sink => new ControlledSink(sink, failure));
        var pipeline = host.GetRequiredService<EventPipeline>();
        var backfill = host.GetRequiredService<BackfillServices>();
        var request = CreateBackfillRequest();
        ActivityContext parent;

        using (var operation = OpenTelemetrySetup.ActivitySource.StartActivity("operation.failure"))
        {
            operation.Should().NotBeNull();
            parent = operation!.Context;
            await pipeline.PublishAsync(CreateTradeEvent(), timeout.Token);
            await backfill.RequestQueue.EnqueueAsync(request, timeout.Token);
        }

        await WaitUntilAsync(() => pipeline.ConsumedCount == 1 && request.Status == BackfillRequestStatus.Failed, timeout.Token);
        await host.DisposeAsync();

        var spans = exporter.Activities.Where(span => span.TraceId == parent.TraceId).ToArray();
        var failedStorage = spans.Should().ContainSingle(span =>
            span.OperationName == "StoreMarketEvent.ControlledSink" && span.Status == ActivityStatusCode.Error).Which;
        failedStorage.Events.Should().Contain(item => item.Name == "exception");
        failedStorage.StatusDescription.Should().Contain(failure.Message);
        spans.Should().Contain(span => span.OperationName == "StoreMarketEvent.ControlledSink"
            && span.Status != ActivityStatusCode.Error, "the real pipeline retries the transient append failure");
        var worker = spans.Should().ContainSingle(span => span.OperationName == $"Backfill.{HistoricalProvider.ProviderName}").Which;
        var fetch = spans.Should().ContainSingle(span => span.OperationName == $"BackfillFetch.{HistoricalProvider.ProviderName}").Which;
        worker.ParentSpanId.Should().Be(parent.SpanId);
        worker.Status.Should().Be(ActivityStatusCode.Error);
        fetch.ParentSpanId.Should().Be(worker.SpanId);
        fetch.Status.Should().Be(ActivityStatusCode.Error);
        fetch.Events.Should().Contain(item => item.Name == "exception");
        spans.Should().NotContain(span => span.OperationName == "BackfillStorage.WriteBars");
        request.ErrorMessage.Should().Contain("injected historical provider failure");
        ReadStoredEvents(artifacts.RootPath).Should().Contain("Trade");
        exporter.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Shutdown_DeliversStorageSpanThatFinishesWhileHostIsStopping()
    {
        using var environment = UseLocalEnvironment();
        using var artifacts = TestArtifactDirectory.Create(nameof(TracingIntegrationTests));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var exporter = new RecordingExporter();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await CreateHostAsync(
            artifacts.RootPath, true, exporter, timeout.Token,
            decorateSink: sink => new ControlledSink(sink, entered: entered, release: release));
        ActivityTraceId traceId;
        using (var operation = OpenTelemetrySetup.ActivitySource.StartActivity("operation.shutdown"))
        {
            operation.Should().NotBeNull();
            traceId = operation!.TraceId;
            await host.Pipeline.PublishAsync(CreateTradeEvent(), timeout.Token);
        }
        await entered.Task.WaitAsync(timeout.Token);

        try
        {
            var stopping = host.DisposeAsync().AsTask();
            stopping.IsCompleted.Should().BeFalse("shutdown must wait for accepted pipeline work");
            exporter.DisposeCount.Should().Be(0, "telemetry must outlive the draining pipeline");
            release.TrySetResult();
            await stopping.WaitAsync(timeout.Token);
        }
        finally
        {
            release.TrySetResult();
        }

        exporter.Activities.Should().ContainSingle(span => span.TraceId == traceId
            && span.OperationName == "StoreMarketEvent.ControlledSink");
        exporter.DisposeCount.Should().Be(1);
        ReadStoredEvents(artifacts.RootPath).Should().Contain("Trade");
    }

    [Fact]
    public async Task DisabledHost_StartsAndProcessesPipelineAndBackfillWithoutTracingProvider()
    {
        using var environment = UseLocalEnvironment();
        using var artifacts = TestArtifactDirectory.Create(nameof(TracingIntegrationTests));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var host = await CreateHostAsync(artifacts.RootPath, false, null, timeout.Token);
        var backfill = host.GetRequiredService<BackfillServices>();
        var request = CreateBackfillRequest();

        host.GetService<TracerProvider>().Should().BeNull();
        using var operation = OpenTelemetrySetup.ActivitySource.StartActivity("operation.disabled");
        operation.Should().BeNull();
        await host.Pipeline.PublishAsync(CreateTradeEvent(), timeout.Token);
        await backfill.RequestQueue.EnqueueAsync(request, timeout.Token);
        await WaitUntilAsync(() => host.Pipeline.ConsumedCount == 1 && request.Status == BackfillRequestStatus.Completed, timeout.Token);
        await host.DisposeAsync();

        request.BarsRetrieved.Should().Be(1);
        ReadStoredEvents(artifacts.RootPath).Should().Contain(HistoricalProvider.ProviderName).And.Contain("Trade");
    }

    private static readonly DateOnly BackfillDate = new(2026, 7, 1);

    private static async Task<HostStartup> CreateHostAsync(
        string dataRoot,
        bool tracingEnabled,
        RecordingExporter? exporter,
        CancellationToken ct,
        bool failBackfill = false,
        Func<IStorageSink, IStorageSink>? decorateSink = null)
    {
        var config = new AppConfig(
            DataRoot: dataRoot,
            Compress: false,
            Backfill: new BackfillConfig(
                EnableSymbolResolution: false,
                EnableRateLimitRotation: false,
                Jobs: new BackfillJobsConfig(PersistJobs: false, MaxConcurrentRequests: 1, MaxConcurrentPerProvider: 1)),
            Tracing: new TracingConfig { Enabled = tracingEnabled });
        var configPath = Path.Combine(dataRoot, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(config, AppConfigJsonOptions.Write), ct);

        return await HostStartup.CreateStartedHostAsync(
            CompositionOptions.Minimal with
            {
                ConfigPath = configPath,
                EnablePipelineServices = true,
                EnableBackfillServices = true
            },
            enableProcessWideHostedServices: false,
            services =>
            {
                if (exporter is not null)
                {
                    services.ConfigureOpenTelemetryTracerProvider((_, builder) => builder.AddProcessor(
                        new BatchActivityExportProcessor(exporter, maxQueueSize: 2048,
                            scheduledDelayMilliseconds: 600_000, maxExportBatchSize: 1024)));
                }
                if (decorateSink is not null)
                    services.AddSingleton<IStorageSink>(sp => decorateSink(sp.GetRequiredService<JsonlStorageSink>()));

                services.AddSingleton(sp => sp.GetRequiredService<BackfillServiceFactory>().CreateServices(
                    config, config.Backfill!, dataRoot, [new HistoricalProvider(failBackfill)]));
                services.AddSingleton<IHostedService, BackfillHostService>();
            },
            ct,
            enableDatabaseInitialization: false);
    }

    private static EnvironmentVariableScope UseLocalEnvironment()
    {
        var scope = new EnvironmentVariableScope();
        foreach (var name in new[]
        {
            "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "MERIDIAN_ENVIRONMENT",
            "MERIDIAN_DEPLOYMENT_ENVIRONMENT", "MERIDIAN_MODE", "MERIDIAN_API_DEPLOYMENT_MODE",
            "MERIDIAN_TENANT_SCOPE_ENFORCEMENT", "MERIDIAN_SCOPED_ACCESS_CONNECTION_STRING",
            MeridianDatabaseEnvironment.UnifiedVariable
        }.Concat(MeridianDatabaseEnvironment.PropagatedConnectionStringVariables))
            scope.Set(name, null);
        return scope.Set("DOTNET_ENVIRONMENT", Environments.Development)
            .Set("ASPNETCORE_ENVIRONMENT", Environments.Development)
            .Set("MERIDIAN_USE_INMEMORY_GOVERNANCE", "true");
    }

    private static MarketEvent CreateTradeEvent()
    {
        var now = DateTimeOffset.UtcNow;
        var trade = new Trade(now, "SPY", 100.50m, 100, AggressorSide.Buy, 1, Venue: "XNYS");
        return MarketEvent.Trade(now, "SPY", trade, seq: 1, source: "tracing-integration");
    }

    private static BackfillRequest CreateBackfillRequest() => new()
    {
        JobId = "tracing-integration",
        Symbol = "SPY",
        FromDate = BackfillDate,
        ToDate = BackfillDate,
        PreferredProviders = [HistoricalProvider.ProviderName],
        MaxRetries = 0
    };

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
            await Task.Delay(20, ct);
    }

    private static string ReadStoredEvents(string dataRoot) => string.Join("\n",
        Directory.EnumerateFiles(dataRoot, "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));

    private sealed class BackfillHostService(BackfillServices services) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await services.InitializeAsync(cancellationToken);
            services.StartWorker();
        }

        public Task StopAsync(CancellationToken cancellationToken) => services.StopWorkerAsync(cancellationToken);
    }

    private sealed class HistoricalProvider(bool fail) : IHistoricalDataProvider
    {
        public const string ProviderName = "tracing-provider";
        public string Name => ProviderName;
        public string DisplayName => ProviderName;
        public string Description => "Deterministic historical input for the composed backfill worker.";

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (fail)
                throw new InvalidOperationException("injected historical provider failure");
            return Task.FromResult<IReadOnlyList<HistoricalBar>>(
                [new HistoricalBar(symbol, from ?? BackfillDate, 100m, 102m, 99m, 101m, 1000, Name)]);
        }

        public void Dispose() { }
    }

    private sealed class ControlledSink(
        IStorageSink inner,
        Exception? firstFailure = null,
        TaskCompletionSource? entered = null,
        TaskCompletionSource? release = null) : IStorageSink
    {
        private int _attempts;

        public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _attempts) == 1 && firstFailure is not null)
                throw firstFailure;
            entered?.TrySetResult();
            if (release is not null)
                await release.Task.WaitAsync(ct);
            await inner.AppendAsync(evt, ct);
        }

        public Task FlushAsync(CancellationToken ct = default) => inner.FlushAsync(ct);

        // The inner JSONL sink is a separate host-owned singleton.
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingExporter : BaseExporter<Activity>
    {
        public ConcurrentQueue<Activity> Activities { get; } = new();
        public int DisposeCount { get; private set; }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
                Activities.Enqueue(activity);
            return ExportResult.Success;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
