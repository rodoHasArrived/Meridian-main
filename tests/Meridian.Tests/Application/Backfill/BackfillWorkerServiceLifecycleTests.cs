using System.Collections.Concurrent;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Config;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Testing;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Guards the backfill shutdown failure mode where a provider request remains in flight while
/// worker-owned queue, semaphore, or provider resources are being disposed.
/// </summary>
public sealed class BackfillWorkerServiceLifecycleTests
{
    [Fact]
    public async Task Scenario_BackfillShutdown_InFlightProviderFetch_IsCancelledAndRemovedFromInFlightState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            Scenario_BackfillShutdown_InFlightProviderFetch_IsCancelledAndRemovedFromInFlightState));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(artifacts.RootPath, provider);
        var request = CreateRequest(provider.Name);
        var runningStates = new ConcurrentQueue<bool>();
        services.Worker.OnRunningStateChanged += runningStates.Enqueue;

        try
        {
            await services.RequestQueue.EnqueueAsync(request, timeout.Token);
            services.Worker.Start();
            await provider.FetchStarted.WaitAsync(timeout.Token);

            await services.Worker.StopAsync(timeout.Token);

            provider.CancellationObserved.IsCompletedSuccessfully.Should().BeTrue();
            provider.FetchCompleted.IsCompletedSuccessfully.Should().BeTrue();
            provider.HasActiveFetch.Should().BeFalse();
            services.Worker.IsRunning.Should().BeFalse();
            services.RequestQueue.InFlightCount.Should().Be(0);
            request.Status.Should().Be(BackfillRequestStatus.Cancelled);
            request.ErrorMessage.Should().Contain("stopping");
            runningStates.Should().Equal(true, false);
            Directory.EnumerateFiles(artifacts.RootPath, "*.jsonl", SearchOption.AllDirectories)
                .Should().BeEmpty("a cancelled provider fetch must not be represented as persisted backfill data");
        }
        finally
        {
            services.Dispose();
        }
    }

    [Fact]
    public async Task Scenario_BackfillShutdown_UncooperativeProvider_CallerDeadlineDoesNotDisposeLiveResources()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            Scenario_BackfillShutdown_UncooperativeProvider_CallerDeadlineDoesNotDisposeLiveResources));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: false);
        var services = CreateServices(artifacts.RootPath, provider);
        var request = CreateRequest(provider.Name);

        try
        {
            await services.RequestQueue.EnqueueAsync(request, timeout.Token);
            services.Worker.Start();
            await provider.FetchStarted.WaitAsync(timeout.Token);

            using var cancelledCaller = new CancellationTokenSource();
            cancelledCaller.Cancel();
            Func<Task> stop = () => services.Worker.StopAsync(cancelledCaller.Token);

            await stop.Should().ThrowAsync<OperationCanceledException>();
            services.Worker.IsRunning.Should().BeTrue(
                "the internal stop remains active until the provider request has quiesced");
            provider.HasActiveFetch.Should().BeTrue();
            provider.DisposeCount.Should().Be(0);
            services.RequestQueue.InFlightCount.Should().Be(1);

            provider.Release();
            await services.Worker.StopAsync(timeout.Token);

            provider.FetchCompleted.IsCompletedSuccessfully.Should().BeTrue();
            services.Worker.IsRunning.Should().BeFalse();
            services.RequestQueue.InFlightCount.Should().Be(0);
            request.Status.Should().Be(BackfillRequestStatus.Cancelled);
        }
        finally
        {
            provider.Release();
            services.Dispose();
        }
    }

    [Fact]
    public async Task Scenario_BackfillDisposal_InFlightFetchQuiescesBeforeProviderIsDisposed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            Scenario_BackfillDisposal_InFlightFetchQuiescesBeforeProviderIsDisposed));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(artifacts.RootPath, provider);
        var request = CreateRequest(provider.Name);

        Task? disposeTask = null;
        try
        {
            await services.RequestQueue.EnqueueAsync(request, timeout.Token);
            services.Worker.Start();
            await provider.FetchStarted.WaitAsync(timeout.Token);

            disposeTask = Task.Run(services.Dispose);
            await provider.CancellationObserved.WaitAsync(timeout.Token);
            await disposeTask.WaitAsync(timeout.Token);

            provider.FetchCompleted.IsCompletedSuccessfully.Should().BeTrue();
            provider.DisposedWhileFetchActive.Should().BeFalse();
            provider.DisposeCount.Should().Be(1);
            services.Worker.IsRunning.Should().BeFalse();
            services.RequestQueue.InFlightCount.Should().Be(0);
            request.Status.Should().Be(BackfillRequestStatus.Cancelled);
        }
        finally
        {
            provider.Release();
            if (disposeTask is null)
            {
                services.Dispose();
            }
            else
            {
                await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task CancelJobAsync_InFlightProviderAttempt_ObservesCancellationBeforeTerminalJobState()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            CancelJobAsync_InFlightProviderAttempt_ObservesCancellationBeforeTerminalJobState));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(artifacts.RootPath, provider, persistJobs: true);

        try
        {
            var job = await services.JobManager.CreateJobAsync(
                "cancel admitted provider attempt",
                ["SPY"],
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                options: new BackfillJobOptions
                {
                    SkipExistingData = false,
                    FillGapsOnly = false,
                    MaxRetries = 0
                },
                preferredProviders: [provider.Name],
                ct: timeout.Token);
            await services.JobManager.StartJobAsync(job.JobId, timeout.Token);
            services.Worker.Start();
            await provider.FetchStarted.WaitAsync(timeout.Token);

            await services.JobManager.CancelJobAsync(job.JobId, timeout.Token);

            provider.CancellationObserved.IsCompletedSuccessfully.Should().BeTrue();
            provider.FetchCompleted.IsCompletedSuccessfully.Should().BeTrue();
            provider.HasActiveFetch.Should().BeFalse();
            services.RequestQueue.InFlightCount.Should().Be(0);
            services.RequestQueue.PendingCount.Should().Be(0);
            job.Status.Should().Be(BackfillJobStatus.Cancelled);
            job.StatusReason.Should().Be("Cancelled by user");
            services.Worker.IsRunning.Should().BeTrue(
                "cancelling one job must not stop the shared worker");

            var persistedPayload = await File.ReadAllTextAsync(
                Path.Combine(artifacts.RootPath, "_backfill_jobs", $"{job.JobId}.json"),
                timeout.Token);
            JsonSerializer.Deserialize<BackfillJob>(persistedPayload)!.Status
                .Should().Be(BackfillJobStatus.Cancelled);
        }
        finally
        {
            provider.Release();
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task CancelJobAsync_CallerCancelsAfterFence_CompletesTruthfulTerminalTransition()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            CancelJobAsync_CallerCancelsAfterFence_CompletesTruthfulTerminalTransition));
        var provider = new ControlledHistoricalProvider(
            observeWorkerCancellation: true,
            holdAfterCancellation: true);
        var services = CreateServices(artifacts.RootPath, provider, persistJobs: true);

        try
        {
            var job = await services.JobManager.CreateJobAsync(
                "caller cancellation after job fence",
                ["SPY"],
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                options: new BackfillJobOptions
                {
                    SkipExistingData = false,
                    FillGapsOnly = false,
                    MaxRetries = 0
                },
                preferredProviders: [provider.Name],
                ct: timeout.Token);
            await services.JobManager.StartJobAsync(job.JobId, timeout.Token);
            services.Worker.Start();
            await provider.FetchStarted.WaitAsync(timeout.Token);
            using var caller = new CancellationTokenSource();

            var cancelTask = services.JobManager.CancelJobAsync(job.JobId, caller.Token);
            await provider.CancellationObserved.WaitAsync(timeout.Token);
            caller.Cancel();

            cancelTask.IsCompleted.Should().BeFalse(
                "the provider deliberately holds the post-fence cancellation cleanup");
            provider.Release();
            await cancelTask.WaitAsync(timeout.Token);

            job.Status.Should().Be(BackfillJobStatus.Cancelled);
            services.RequestQueue.InFlightCount.Should().Be(0);
            provider.HasActiveFetch.Should().BeFalse();
            var persistedPayload = await File.ReadAllTextAsync(
                Path.Combine(artifacts.RootPath, "_backfill_jobs", $"{job.JobId}.json"),
                timeout.Token);
            JsonSerializer.Deserialize<BackfillJob>(persistedPayload)!.Status
                .Should().Be(BackfillJobStatus.Cancelled);
        }
        finally
        {
            provider.Release();
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartJobAsync_CallerCancellation_RestoresPendingStateWithoutFailureClassification()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            StartJobAsync_CallerCancellation_RestoresPendingStateWithoutFailureClassification));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(artifacts.RootPath, provider, persistJobs: true);

        try
        {
            var job = await services.JobManager.CreateJobAsync(
                "cancelled start",
                ["SPY"],
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 2),
                options: new BackfillJobOptions
                {
                    SkipExistingData = false,
                    FillGapsOnly = false
                },
                preferredProviders: [provider.Name],
                ct: timeout.Token);
            using var cancelledCaller = new CancellationTokenSource();
            cancelledCaller.Cancel();

            Func<Task> start = () => services.JobManager.StartJobAsync(
                job.JobId,
                cancelledCaller.Token);
            await start.Should().ThrowAsync<OperationCanceledException>();

            job.Status.Should().Be(BackfillJobStatus.Pending);
            job.StatusReason.Should().BeNull();
            job.StartedAt.Should().BeNull();
            job.CompletedAt.Should().BeNull();
            job.SymbolProgress.Should().BeEmpty();
            services.RequestQueue.PendingCount.Should().Be(0);
            services.RequestQueue.InFlightCount.Should().Be(0);

            var persistedPayload = await File.ReadAllTextAsync(
                Path.Combine(artifacts.RootPath, "_backfill_jobs", $"{job.JobId}.json"),
                timeout.Token);
            var persistedJob = JsonSerializer.Deserialize<BackfillJob>(persistedPayload);
            persistedJob!.Status.Should().Be(BackfillJobStatus.Pending);
            persistedJob.StatusReason.Should().BeNull();
            persistedJob.CompletedAt.Should().BeNull();
        }
        finally
        {
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartJobAsync_PersistenceCancellationAfterDequeue_RevokesExactUncommittedBatch()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            StartJobAsync_PersistenceCancellationAfterDequeue_RevokesExactUncommittedBatch));
        var startPersistenceEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStartPersistence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCount = 0;

        async Task ControlledAtomicWriteAsync(
            string path,
            string payload,
            CancellationToken ct)
        {
            if (Interlocked.Increment(ref writeCount) == 2)
            {
                startPersistenceEntered.TrySetResult();
                await releaseStartPersistence.Task.ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }

            await File.WriteAllTextAsync(path, payload, ct).ConfigureAwait(false);
        }

        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(
            artifacts.RootPath,
            provider,
            persistJobs: true,
            atomicWriteAsync: ControlledAtomicWriteAsync);

        try
        {
            var job = await services.JobManager.CreateJobAsync(
                "cancelled persistence after dequeue",
                ["SPY"],
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                options: new BackfillJobOptions
                {
                    SkipExistingData = false,
                    FillGapsOnly = false,
                    MaxRetries = 0
                },
                preferredProviders: [provider.Name],
                ct: timeout.Token);
            services.Worker.Start();
            using var caller = new CancellationTokenSource();

            var startTask = services.JobManager.StartJobAsync(job.JobId, caller.Token);
            await startPersistenceEntered.Task.WaitAsync(timeout.Token);
            await provider.FetchStarted.WaitAsync(timeout.Token);

            caller.Cancel();
            releaseStartPersistence.TrySetResult();

            Func<Task> observeStart = () => startTask;
            await observeStart.Should().ThrowAsync<OperationCanceledException>();

            provider.CancellationObserved.IsCompletedSuccessfully.Should().BeTrue();
            provider.FetchCompleted.IsCompletedSuccessfully.Should().BeTrue();
            provider.HasActiveFetch.Should().BeFalse();
            services.RequestQueue.PendingCount.Should().Be(0);
            services.RequestQueue.InFlightCount.Should().Be(0);
            job.Status.Should().Be(BackfillJobStatus.Pending);
            job.StartedAt.Should().BeNull();
            job.CompletedAt.Should().BeNull();

            var persistedPayload = await File.ReadAllTextAsync(
                Path.Combine(artifacts.RootPath, "_backfill_jobs", $"{job.JobId}.json"),
                timeout.Token);
            JsonSerializer.Deserialize<BackfillJob>(persistedPayload)!.Status
                .Should().Be(BackfillJobStatus.Pending);
            Volatile.Read(ref writeCount).Should().Be(3);
        }
        finally
        {
            releaseStartPersistence.TrySetResult();
            provider.Release();
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartAndStopConcurrent_StartNotificationAlwaysPrecedesStopNotification()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            StartAndStopConcurrent_StartNotificationAlwaysPrecedesStopNotification));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(artifacts.RootPath, provider);
        using var startNotificationEntered = new ManualResetEventSlim();
        using var releaseStartNotification = new ManualResetEventSlim();
        var observedStates = new ConcurrentQueue<bool>();

        services.Worker.OnRunningStateChanged += isRunning =>
        {
            observedStates.Enqueue(isRunning);
            if (isRunning)
            {
                startNotificationEntered.Set();
                releaseStartNotification.Wait(timeout.Token);
            }
        };

        try
        {
            var startTask = Task.Run(services.Worker.Start, timeout.Token);
            startNotificationEntered.Wait(timeout.Token);

            var stopTask = services.Worker.StopAsync(timeout.Token);

            stopTask.IsCompleted.Should().BeFalse(
                "the stopped notification must wait for the started notification to publish");
            observedStates.Should().Equal(true);

            releaseStartNotification.Set();
            await startTask.WaitAsync(timeout.Token);
            await stopTask.WaitAsync(timeout.Token);

            observedStates.Should().Equal(true, false);
            services.Worker.IsRunning.Should().BeFalse();
        }
        finally
        {
            releaseStartNotification.Set();
            await services.DisposeAsync();
        }
    }

    [Fact]
    public void CompositeDispose_ChildFailures_AttemptsEveryChildAndAggregatesFailures()
    {
        var first = new DisposalTrackingHistoricalProvider(
            "first",
            new IOException("first injected disposal failure"));
        var second = new DisposalTrackingHistoricalProvider("second");
        var third = new DisposalTrackingHistoricalProvider(
            "third",
            new InvalidOperationException("third injected disposal failure"));
        var composite = new CompositeHistoricalDataProvider(
            [first, second, third],
            enableRateLimitRotation: false);

        Action dispose = composite.Dispose;
        var failure = dispose.Should().Throw<AggregateException>().Which;

        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
        third.DisposeCount.Should().Be(1);
        failure.InnerExceptions.Should().HaveCount(2);
        failure.ToString().Should().Contain("first injected disposal failure");
        failure.ToString().Should().Contain("third injected disposal failure");

        composite.Dispose();
        first.DisposeCount.Should().Be(1);
        second.DisposeCount.Should().Be(1);
        third.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task CompletionNotifications_BoundedBackpressure_DrainsEveryCommittedTransitionWithoutLoss()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var rateLimits = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(rateLimits)
        {
            MaxConcurrentRequests = 1,
            MaxConcurrentPerProvider = 1
        };
        var expectedRequestIds = new List<string>();

        // Fill the completion policy's 500-item bounded capacity without a reader.
        for (var index = 0; index < 500; index++)
        {
            var completed = CreateRequest("controlled", $"saturation-{index}");
            expectedRequestIds.Add(completed.RequestId);
            await queue.EnqueueAsync(completed, timeout.Token);
            var dequeued = await queue.TryDequeueAsync(timeout.Token)
                ?? throw new InvalidOperationException("Expected a dequeued backfill attempt.");
            dequeued.Request.Should().BeSameAs(completed);
            var attemptToken = dequeued.Token;
            await queue.CompleteRequestAsync(
                completed,
                attemptToken,
                success: true,
                ct: timeout.Token);
        }

        var overflow = CreateRequest("controlled", "bounded-overflow");
        expectedRequestIds.Add(overflow.RequestId);
        await queue.EnqueueAsync(overflow, timeout.Token);
        var overflowDequeued = await queue.TryDequeueAsync(timeout.Token)
            ?? throw new InvalidOperationException("Expected the overflow backfill attempt.");
        overflowDequeued.Request.Should().BeSameAs(overflow);
        var overflowAttempt = overflowDequeued.Token;

        var blockedPublication = queue.CompleteRequestAsync(
            overflow,
            overflowAttempt,
            success: true,
            ct: timeout.Token);
        blockedPublication.IsCompleted.Should().BeFalse(
            "the bounded channel applies backpressure instead of dropping a committed transition");
        overflow.Status.Should().Be(BackfillRequestStatus.Completed);
        queue.InFlightCount.Should().Be(0);

        var observedRequestIds = new List<string>();
        var reader = Task.Run(async () =>
        {
            await foreach (var notification in queue.CompletedRequests.ReadAllAsync(timeout.Token))
                observedRequestIds.Add(notification.RequestId);
        }, timeout.Token);

        await blockedPublication.WaitAsync(timeout.Token);
        queue.CompleteCompletionNotifications();
        await reader.WaitAsync(timeout.Token);

        observedRequestIds.Should().Equal(expectedRequestIds);
    }

    [Fact]
    public async Task CancelInFlightRequestAsync_DuplicateRequestIds_ReleasesOnlyMatchingAttempt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var rateLimits = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(rateLimits)
        {
            MaxConcurrentRequests = 2,
            MaxConcurrentPerProvider = 2
        };
        const string duplicateRequestId = "same-request";
        var first = CreateRequest("controlled", "first", duplicateRequestId);
        var second = CreateRequest("controlled", "second", duplicateRequestId);

        await queue.EnqueueAsync(first, timeout.Token);
        await queue.EnqueueAsync(second, timeout.Token);
        var firstDequeued = await queue.TryDequeueAsync(timeout.Token)
            ?? throw new InvalidOperationException("Expected the first backfill attempt.");
        var secondDequeued = await queue.TryDequeueAsync(timeout.Token)
            ?? throw new InvalidOperationException("Expected the second backfill attempt.");
        var dequeued = new[] { firstDequeued, secondDequeued };
        dequeued.Select(static attempt => attempt.Request)
            .Should().Contain(item => ReferenceEquals(item, first));
        dequeued.Select(static attempt => attempt.Request)
            .Should().Contain(item => ReferenceEquals(item, second));

        var firstAttempt = dequeued.Single(attempt => ReferenceEquals(attempt.Request, first)).Token;
        var secondAttempt = dequeued.Single(attempt => ReferenceEquals(attempt.Request, second)).Token;
        firstAttempt.Should().NotBe(secondAttempt);
        (await queue.CancelInFlightRequestAsync(
            first,
            firstAttempt,
            "first stopped",
            timeout.Token)).Should().BeTrue();
        queue.InFlightCount.Should().Be(1);
        second.Status.Should().Be(BackfillRequestStatus.InProgress);

        (await queue.CancelInFlightRequestAsync(
            second,
            secondAttempt,
            "second stopped",
            timeout.Token)).Should().BeTrue();
        queue.InFlightCount.Should().Be(0);
    }

    [Fact]
    public async Task CompleteAndCancel_StaleTokenForRequeuedRequest_CannotAffectNewAttempt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var rateLimits = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(rateLimits)
        {
            MaxConcurrentRequests = 1,
            MaxConcurrentPerProvider = 1
        };
        var request = CreateRequest("controlled", "same-object-retry");

        await queue.EnqueueAsync(request, timeout.Token);
        var firstAttempt = await queue.TryDequeueAsync(timeout.Token)
            ?? throw new InvalidOperationException("Expected the original backfill attempt.");
        firstAttempt.Request.Should().BeSameAs(request);
        var staleAttempt = firstAttempt.Token;
        (await queue.RequeueInFlightAttemptAsync(
            request,
            staleAttempt,
            "retry the same request object",
            timeout.Token)).Should().BeTrue();

        var secondAttempt = await queue.TryDequeueAsync(timeout.Token)
            ?? throw new InvalidOperationException("Expected the replacement backfill attempt.");
        secondAttempt.Request.Should().BeSameAs(request);
        var currentAttempt = secondAttempt.Token;
        currentAttempt.Should().NotBe(staleAttempt);

        await queue.CompleteRequestAsync(
            request,
            staleAttempt,
            success: true,
            ct: timeout.Token);
        (await queue.CancelInFlightRequestAsync(
            request,
            staleAttempt,
            "stale cancellation",
            timeout.Token)).Should().BeFalse();

        request.Status.Should().Be(BackfillRequestStatus.InProgress);
        queue.InFlightCount.Should().Be(1);

        (await queue.CancelInFlightRequestAsync(
            request,
            currentAttempt,
            "current attempt cleanup",
            timeout.Token)).Should().BeTrue();
        request.Status.Should().Be(BackfillRequestStatus.Cancelled);
        queue.InFlightCount.Should().Be(0);
    }

    [Fact]
    public async Task CancelJobRequestsAsync_TargetJob_RemovesOnlyTargetPendingRequests()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var rateLimits = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(rateLimits);
        var target = CreateRequest("controlled", "target-job");
        var unrelated = CreateRequest("controlled", "unrelated-job");
        await queue.EnqueueAsync(target, timeout.Token);
        await queue.EnqueueAsync(unrelated, timeout.Token);

        (await queue.GetJobRequestsAsync("target-job", timeout.Token)).Should().ContainSingle()
            .Which.Should().BeSameAs(target);
        queue.PendingCount.Should().Be(2, "reading one job must not mutate another job's queue entries");

        await queue.CancelJobRequestsAsync("target-job", timeout.Token);

        target.Status.Should().Be(BackfillRequestStatus.Cancelled);
        queue.PendingCount.Should().Be(1);
        (await queue.GetJobRequestsAsync("unrelated-job", timeout.Token)).Should().ContainSingle()
            .Which.Should().BeSameAs(unrelated);
    }

    [Fact]
    public async Task Scenario_BackfillRestart_CleanShutdownPersistsResumablePausedJob()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            Scenario_BackfillRestart_CleanShutdownPersistsResumablePausedJob));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(artifacts.RootPath, provider, persistJobs: true);
        var job = await services.JobManager.CreateJobAsync(
            "restart-safe backfill",
            ["SPY"],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 1),
            options: new BackfillJobOptions
            {
                SkipExistingData = false,
                FillGapsOnly = false,
                MaxRetries = 0
            },
            preferredProviders: [provider.Name],
            ct: timeout.Token);

        await services.JobManager.StartJobAsync(job.JobId, timeout.Token);
        services.Worker.Start();
        await provider.FetchStarted.WaitAsync(timeout.Token);
        await services.Worker.StopAsync(timeout.Token);

        job.Status.Should().Be(BackfillJobStatus.Paused);
        job.StatusReason.Should().Be(BackfillJobManager.HostShutdownPauseReason);
        await services.DisposeAsync();

        var restoredProvider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var restored = CreateServices(artifacts.RootPath, restoredProvider, persistJobs: true);
        try
        {
            await restored.InitializeAsync(timeout.Token);
            var loaded = restored.JobManager.GetJob(job.JobId);
            loaded.Should().NotBeNull();
            loaded!.Status.Should().Be(BackfillJobStatus.Paused);
            loaded.CanStart.Should().BeTrue();

            await restored.JobManager.ResumeJobAsync(job.JobId, timeout.Token);

            loaded.Status.Should().Be(BackfillJobStatus.Running);
            restored.RequestQueue.PendingCount.Should().BeGreaterThan(0);
            await restored.JobManager.PauseJobAsync(
                job.JobId,
                "test cleanup",
                timeout.Token);
        }
        finally
        {
            await restored.DisposeAsync();
        }
    }

    [Fact]
    public async Task Scenario_BackfillRestart_StrandedRunningJobLoadsAsResumablePaused()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            Scenario_BackfillRestart_StrandedRunningJobLoadsAsResumablePaused));
        var firstProvider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var first = CreateServices(artifacts.RootPath, firstProvider, persistJobs: true);
        var job = await first.JobManager.CreateJobAsync(
            "interrupted backfill",
            ["SPY"],
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 1),
            options: new BackfillJobOptions
            {
                SkipExistingData = false,
                FillGapsOnly = false
            },
            ct: timeout.Token);
        await first.JobManager.StartJobAsync(job.JobId, timeout.Token);

        // Simulate a prior process that persisted Running but never started its worker shutdown
        // transition. Disposing an unstarted worker intentionally leaves the recovery case intact.
        await first.DisposeAsync();

        var restored = CreateServices(
            artifacts.RootPath,
            new ControlledHistoricalProvider(observeWorkerCancellation: true),
            persistJobs: true);
        try
        {
            await restored.InitializeAsync(timeout.Token);
            var loaded = restored.JobManager.GetJob(job.JobId);

            loaded.Should().NotBeNull();
            loaded!.Status.Should().Be(BackfillJobStatus.Paused);
            loaded.StatusReason.Should().Be(BackfillJobManager.InterruptedHostPauseReason);
            loaded.CanStart.Should().BeTrue();
        }
        finally
        {
            await restored.DisposeAsync();
        }
    }

    [Fact]
    public async Task LoadJobsAsync_TraversalJobIdentity_DoesNotEscapeRootOrEnterMemory()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            LoadJobsAsync_TraversalJobIdentity_DoesNotEscapeRootOrEnterMemory));
        var services = CreateServices(
            artifacts.RootPath,
            new ControlledHistoricalProvider(observeWorkerCancellation: true),
            persistJobs: true);
        var jobsDirectory = Path.Combine(artifacts.RootPath, "_backfill_jobs");
        var escapedPath = Path.Combine(artifacts.RootPath, "escaped.json");
        var maliciousJob = new BackfillJob
        {
            JobId = "../escaped",
            Name = "malicious persisted identity",
            Symbols = ["SPY"],
            FromDate = new DateOnly(2026, 7, 1),
            ToDate = new DateOnly(2026, 7, 1),
            Status = BackfillJobStatus.Running
        };
        var payload = JsonSerializer.Serialize(maliciousJob);
        await File.WriteAllTextAsync(
            Path.Combine(jobsDirectory, "malicious.json"),
            payload,
            timeout.Token);

        try
        {
            await services.InitializeAsync(timeout.Token);

            services.JobManager.GetAllJobs().Should().BeEmpty();
            File.Exists(escapedPath).Should().BeFalse(
                "a persisted job identity must never become an unconstrained startup write path");
        }
        finally
        {
            await services.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("../escaped-jobs")]
    [InlineData(@"..\escaped-jobs")]
    [InlineData("nested/../../escaped-jobs")]
    public void CreateServices_TraversalJobsDirectory_IsRejectedBeforeCreatingOutsideRoot(
        string jobsDirectory)
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(
            CreateServices_TraversalJobsDirectory_IsRejectedBeforeCreatingOutsideRoot));
        var escapedDirectory = Path.Combine(artifacts.RootPath, "..", "escaped-jobs");
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var config = new BackfillConfig(
            EnableSymbolResolution: false,
            EnableRateLimitRotation: false,
            Jobs: new BackfillJobsConfig(JobsDirectory: jobsDirectory));

        var create = () => new BackfillServiceFactory().CreateServices(
            new AppConfig(DataRoot: artifacts.RootPath),
            config,
            artifacts.RootPath,
            [provider]);

        create.Should().Throw<ArgumentException>();
        Directory.Exists(escapedDirectory).Should().BeFalse(
            "configured job persistence must remain below DataRoot");
    }

    [Fact]
    public async Task StartJobAsync_AtomicWriterFailureAfterDequeue_RevokesBatchBeforeMarkingFailed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            StartJobAsync_AtomicWriterFailureAfterDequeue_RevokesBatchBeforeMarkingFailed));
        var runningPersistenceEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRunningPersistence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCount = 0;

        async Task FailingRunningStateWriteAsync(
            string path,
            string payload,
            CancellationToken ct)
        {
            if (Interlocked.Increment(ref writeCount) == 2)
            {
                runningPersistenceEntered.TrySetResult();
                await releaseRunningPersistence.Task.ConfigureAwait(false);
                throw new IOException("injected running-state persistence failure");
            }

            await File.WriteAllTextAsync(path, payload, ct).ConfigureAwait(false);
        }

        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(
            artifacts.RootPath,
            provider,
            persistJobs: true,
            atomicWriteAsync: FailingRunningStateWriteAsync);

        try
        {
            var job = await services.JobManager.CreateJobAsync(
                "atomic persistence failure",
                ["SPY"],
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                options: new BackfillJobOptions
                {
                    SkipExistingData = false,
                    FillGapsOnly = false
                },
                ct: timeout.Token);
            services.Worker.Start();

            var startTask = services.JobManager.StartJobAsync(job.JobId, timeout.Token);
            await runningPersistenceEntered.Task.WaitAsync(timeout.Token);
            await provider.FetchStarted.WaitAsync(timeout.Token);
            releaseRunningPersistence.TrySetResult();

            Func<Task> observeStart = () => startTask;
            await observeStart.Should().ThrowAsync<IOException>()
                .WithMessage("*injected running-state persistence failure*");

            provider.CancellationObserved.IsCompletedSuccessfully.Should().BeTrue();
            provider.FetchCompleted.IsCompletedSuccessfully.Should().BeTrue();
            provider.HasActiveFetch.Should().BeFalse();
            services.RequestQueue.PendingCount.Should().Be(0);
            services.RequestQueue.InFlightCount.Should().Be(0);
            job.Status.Should().Be(BackfillJobStatus.Failed);
            Volatile.Read(ref writeCount).Should().Be(3);

            var persistedPayload = await File.ReadAllTextAsync(
                Path.Combine(artifacts.RootPath, "_backfill_jobs", $"{job.JobId}.json"),
                timeout.Token);
            JsonSerializer.Deserialize<BackfillJob>(persistedPayload)!.Status
                .Should().Be(BackfillJobStatus.Failed);
        }
        finally
        {
            releaseRunningPersistence.TrySetResult();
            provider.Release();
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task StartJobAsync_OriginalAndTerminalPersistenceFailures_AreBothReported()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var artifacts = TestArtifactDirectory.Create(nameof(
            StartJobAsync_OriginalAndTerminalPersistenceFailures_AreBothReported));
        var runningPersistenceEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRunningPersistence = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writeCount = 0;

        async Task FailingStateWritesAsync(
            string path,
            string payload,
            CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref writeCount);
            if (attempt == 2)
            {
                runningPersistenceEntered.TrySetResult();
                await releaseRunningPersistence.Task.ConfigureAwait(false);
                throw new IOException("original running-state persistence failure");
            }

            if (attempt == 3)
                throw new IOException("terminal failed-state persistence failure");

            await File.WriteAllTextAsync(path, payload, ct).ConfigureAwait(false);
        }

        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var services = CreateServices(
            artifacts.RootPath,
            provider,
            persistJobs: true,
            atomicWriteAsync: FailingStateWritesAsync);

        try
        {
            var job = await services.JobManager.CreateJobAsync(
                "aggregate persistence failures",
                ["SPY"],
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 1),
                options: new BackfillJobOptions
                {
                    SkipExistingData = false,
                    FillGapsOnly = false,
                    MaxRetries = 0
                },
                preferredProviders: [provider.Name],
                ct: timeout.Token);
            services.Worker.Start();

            var startTask = services.JobManager.StartJobAsync(job.JobId, timeout.Token);
            await runningPersistenceEntered.Task.WaitAsync(timeout.Token);
            await provider.FetchStarted.WaitAsync(timeout.Token);
            releaseRunningPersistence.TrySetResult();

            Func<Task> observeStart = () => startTask;
            var failure = (await observeStart.Should().ThrowAsync<AggregateException>()).Which;
            failure.InnerExceptions.Should().HaveCount(2);
            failure.InnerExceptions[0].Should().BeOfType<IOException>()
                .Which.Message.Should().Be("original running-state persistence failure");
            failure.InnerExceptions[1].ToString()
                .Should().Contain("terminal failed-state persistence failure");

            provider.CancellationObserved.IsCompletedSuccessfully.Should().BeTrue();
            provider.FetchCompleted.IsCompletedSuccessfully.Should().BeTrue();
            provider.HasActiveFetch.Should().BeFalse();
            services.RequestQueue.PendingCount.Should().Be(0);
            services.RequestQueue.InFlightCount.Should().Be(0);
            job.Status.Should().Be(BackfillJobStatus.Failed);
            Volatile.Read(ref writeCount).Should().Be(3);

            var retainedPayload = await File.ReadAllTextAsync(
                Path.Combine(artifacts.RootPath, "_backfill_jobs", $"{job.JobId}.json"),
                timeout.Token);
            JsonSerializer.Deserialize<BackfillJob>(retainedPayload)!.Status
                .Should().Be(
                    BackfillJobStatus.Pending,
                    "both replacement writes failed, so the last committed restart state survives");
        }
        finally
        {
            releaseRunningPersistence.TrySetResult();
            provider.Release();
            await services.DisposeAsync();
        }
    }

    [Fact]
    public async Task Scenario_BackfillCleanup_LifecycleObserverFails_AllOwnedResourcesAreDisposedAndFailureIsReported()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(
            Scenario_BackfillCleanup_LifecycleObserverFails_AllOwnedResourcesAreDisposedAndFailureIsReported));
        var provider = new ControlledHistoricalProvider(observeWorkerCancellation: true);
        var ownedResolver = new TrackingDisposable();
        var services = CreateServices(
            artifacts.RootPath,
            provider,
            persistJobs: false,
            ownedSymbolResolver: ownedResolver);
        services.Worker.OnRunningStateChanged += isRunning =>
        {
            if (!isRunning)
                throw new InvalidOperationException("injected lifecycle cleanup failure");
        };
        services.Worker.Start();

        Func<Task> dispose = async () => await services.DisposeAsync();
        var failure = await dispose.Should().ThrowAsync<AggregateException>();

        failure.Which.ToString().Should().Contain("injected lifecycle cleanup failure");
        provider.DisposeCount.Should().Be(1);
        ownedResolver.DisposeCount.Should().Be(1);
        services.Worker.IsRunning.Should().BeFalse();
    }

    private static BackfillServices CreateServices(
        string dataRoot,
        ControlledHistoricalProvider provider,
        bool persistJobs = false,
        IDisposable? ownedSymbolResolver = null,
        Func<string, string, CancellationToken, Task>? atomicWriteAsync = null)
    {
        var config = new BackfillConfig(
            EnableSymbolResolution: false,
            EnableRateLimitRotation: false,
            Jobs: new BackfillJobsConfig(
                PersistJobs: persistJobs,
                MaxConcurrentRequests: 1,
                MaxConcurrentPerProvider: 1,
                WorkerErrorRetryDelayMs: 10));

        if (ownedSymbolResolver is null && atomicWriteAsync is null)
        {
            return new BackfillServiceFactory().CreateServices(
                new AppConfig(DataRoot: dataRoot),
                config,
                dataRoot,
                [provider]);
        }

        var jobsConfig = config.Jobs!;
        var rateLimits = new ProviderRateLimitTracker();
        rateLimits.RegisterProvider(provider);
        var composite = new CompositeHistoricalDataProvider(
            [provider],
            enableRateLimitRotation: false);
        var gapAnalyzer = new DataGapAnalyzer(dataRoot);
        var requestQueue = new BackfillRequestQueue(rateLimits)
        {
            MaxConcurrentRequests = 1,
            MaxConcurrentPerProvider = 1
        };
        var jobsDirectory = Path.Combine(dataRoot, jobsConfig.JobsDirectory);
        var jobManager = atomicWriteAsync is null
            ? new BackfillJobManager(
                gapAnalyzer,
                requestQueue,
                jobsDirectory)
            : new BackfillJobManager(
                gapAnalyzer,
                requestQueue,
                jobsDirectory,
                atomicWriteAsync);
        var worker = new BackfillWorkerService(
            jobManager,
            requestQueue,
            composite,
            rateLimits,
            jobsConfig,
            new AppConfig(DataRoot: dataRoot),
            dataRoot);
        return new BackfillServices(
            jobManager,
            requestQueue,
            gapAnalyzer,
            rateLimits,
            composite,
            worker,
            ownedSymbolResolver);
    }

    private static BackfillRequest CreateRequest(
        string providerName,
        string jobId = "shutdown-scenario",
        string? requestId = null)
        => new()
        {
            RequestId = requestId ?? Guid.NewGuid().ToString("N")[..12],
            JobId = jobId,
            Symbol = "SPY",
            FromDate = new DateOnly(2026, 7, 1),
            ToDate = new DateOnly(2026, 7, 2),
            PreferredProviders = [providerName],
            MaxRetries = 0
        };

    private sealed class ControlledHistoricalProvider(
        bool observeWorkerCancellation,
        bool holdAfterCancellation = false)
        : IHistoricalDataProvider
    {
        private readonly TaskCompletionSource<bool> _fetchStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _fetchCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _cancellationObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeFetches;
        private int _disposeCount;
        private int _disposedWhileFetchActive;

        public string Name => "controlled";
        public string DisplayName => "Controlled historical provider";
        public string Description => "Deterministic provider used for worker lifecycle failure injection.";
        public Task FetchStarted => _fetchStarted.Task;
        public Task FetchCompleted => _fetchCompleted.Task;
        public Task CancellationObserved => _cancellationObserved.Task;
        public bool HasActiveFetch => Volatile.Read(ref _activeFetches) != 0;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool DisposedWhileFetchActive => Volatile.Read(ref _disposedWhileFetchActive) != 0;

        public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol,
            DateOnly? from,
            DateOnly? to,
            CancellationToken ct = default)
        {
            Interlocked.Increment(ref _activeFetches);
            _fetchStarted.TrySetResult(true);

            try
            {
                if (!observeWorkerCancellation)
                {
                    await _release.Task.ConfigureAwait(false);
                    return [];
                }

                try
                {
                    await _release.Task.WaitAsync(ct).ConfigureAwait(false);
                    return [];
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _cancellationObserved.TrySetResult(true);
                    if (holdAfterCancellation)
                        await _release.Task.ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeFetches);
                _fetchCompleted.TrySetResult(true);
            }
        }

        public void Release() => _release.TrySetResult(true);

        public void Dispose()
        {
            if (HasActiveFetch)
                Interlocked.Exchange(ref _disposedWhileFetchActive, 1);
            Interlocked.Increment(ref _disposeCount);
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class DisposalTrackingHistoricalProvider(
        string name,
        Exception? disposalFailure = null) : IHistoricalDataProvider
    {
        private int _disposeCount;

        public string Name => name;
        public string DisplayName => name;
        public string Description => "Deterministic composite disposal test provider.";
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol,
            DateOnly? from,
            DateOnly? to,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>([]);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            if (disposalFailure is not null)
                throw disposalFailure;
        }
    }
}
