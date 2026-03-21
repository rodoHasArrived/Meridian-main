using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

public sealed class PriorityBackfillQueueTests : IDisposable
{
    private readonly PriorityBackfillQueue _queue;

    public PriorityBackfillQueueTests()
    {
        _queue = new PriorityBackfillQueue();
    }

    public void Dispose()
    {
        _queue.Dispose();
    }

    [Fact]
    public async Task EnqueueAsync_ReturnsJobWithId()
    {
        var request = CreateRequest("SPY");

        var job = await _queue.EnqueueAsync(request);

        job.Should().NotBeNull();
        job.JobId.Should().NotBeNullOrEmpty();
        job.Symbols.Should().Contain("SPY");
    }

    [Fact]
    public async Task EnqueueAsync_SetsJobStatusToPending()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("AAPL"));

        job.Status.Should().Be(BackfillJobStatus.Pending);
    }

    [Fact]
    public async Task DequeueNextAsync_ReturnsEnqueuedJob()
    {
        await _queue.EnqueueAsync(CreateRequest("SPY"));

        var job = await _queue.DequeueNextAsync();

        job.Should().NotBeNull();
        job!.Symbols.Should().Contain("SPY");
    }

    [Fact]
    public async Task DequeueNextAsync_ReturnsNull_WhenEmpty()
    {
        var job = await _queue.DequeueNextAsync();

        job.Should().BeNull();
    }

    [Fact]
    public async Task DequeueNextAsync_RespectsHigherPriorityFirst()
    {
        // Enqueue low priority first, then high priority
        await _queue.EnqueueAsync(CreateRequest("LOW", BackfillPriority.Low));
        await _queue.EnqueueAsync(CreateRequest("HIGH", BackfillPriority.High));

        var first = await _queue.DequeueNextAsync();

        first.Should().NotBeNull();
        first!.Symbols.Should().Contain("HIGH", "higher priority job should be dequeued first");
    }

    [Fact]
    public async Task DequeueNextAsync_CriticalPriorityBeforeNormal()
    {
        await _queue.EnqueueAsync(CreateRequest("NORMAL", BackfillPriority.Normal));
        await _queue.EnqueueAsync(CreateRequest("CRITICAL", BackfillPriority.Critical));

        var first = await _queue.DequeueNextAsync();

        first.Should().NotBeNull();
        first!.Symbols.Should().Contain("CRITICAL");
    }

    [Fact]
    public async Task GetJob_ReturnsJobById()
    {
        var enqueued = await _queue.EnqueueAsync(CreateRequest("SPY"));

        var found = _queue.GetJob(enqueued.JobId);

        found.Should().NotBeNull();
        found!.JobId.Should().Be(enqueued.JobId);
    }

    [Fact]
    public void GetJob_ReturnsNull_ForUnknownId()
    {
        var found = _queue.GetJob("nonexistent");

        found.Should().BeNull();
    }

    [Fact]
    public async Task CancelJob_SetsStatusToCancelled()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));

        var result = _queue.CancelJob(job.JobId);

        result.Should().BeTrue();
        job.Status.Should().Be(BackfillJobStatus.Cancelled);
    }

    [Fact]
    public async Task CancelJob_CompletedJob_ReturnsFalse()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));
        await _queue.MarkCompletedAsync(job.JobId, true);

        var result = _queue.CancelJob(job.JobId);

        result.Should().BeFalse("completed jobs cannot be cancelled");
    }

    [Fact]
    public void CancelJob_UnknownJobId_ReturnsFalse()
    {
        var result = _queue.CancelJob("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task PauseJob_SetsStatusToPaused()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));
        // Need to set it to Running or RateLimited to pause it
        job.Status = BackfillJobStatus.Running;

        var result = _queue.PauseJob(job.JobId, "User requested pause");

        result.Should().BeTrue();
        job.Status.Should().Be(BackfillJobStatus.Paused);
        job.StatusReason.Should().Be("User requested pause");
    }

    [Fact]
    public async Task ResumeJobAsync_ReEnqueuesJob()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));
        job.Status = BackfillJobStatus.Running;
        _queue.PauseJob(job.JobId);

        var result = await _queue.ResumeJobAsync(job.JobId);

        result.Should().BeTrue();
        job.Status.Should().Be(BackfillJobStatus.Pending);
    }

    [Fact]
    public async Task ResumeJobAsync_NonPausedJob_ReturnsFalse()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));

        var result = await _queue.ResumeJobAsync(job.JobId);

        result.Should().BeFalse("only paused jobs can be resumed");
    }

    [Fact]
    public async Task MarkCompletedAsync_SetsStatusCorrectly()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));

        await _queue.MarkCompletedAsync(job.JobId, true, "All done");

        job.Status.Should().Be(BackfillJobStatus.Completed);
        job.CompletedAt.Should().NotBeNull();
        job.StatusReason.Should().Be("All done");
    }

    [Fact]
    public async Task MarkCompletedAsync_WithFailure_SetsFailedStatus()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));

        await _queue.MarkCompletedAsync(job.JobId, false, "Provider error");

        job.Status.Should().Be(BackfillJobStatus.Failed);
    }

    [Fact]
    public async Task SetPriority_UpdatesJobPriority()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY", BackfillPriority.Normal));

        var result = _queue.SetPriority(job.JobId, BackfillPriority.Critical);

        result.Should().BeTrue();
        job.Options.Priority.Should().Be((int)BackfillPriority.Critical);
    }

    [Fact]
    public async Task SetPriority_CompletedJob_ReturnsFalse()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));
        await _queue.MarkCompletedAsync(job.JobId, true);

        var result = _queue.SetPriority(job.JobId, BackfillPriority.Critical);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetJobs_ReturnsAllJobs()
    {
        await _queue.EnqueueAsync(CreateRequest("SPY"));
        await _queue.EnqueueAsync(CreateRequest("AAPL"));
        await _queue.EnqueueAsync(CreateRequest("MSFT"));

        var jobs = _queue.GetJobs();

        jobs.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetJobs_WithPredicate_FiltersResults()
    {
        var spy = await _queue.EnqueueAsync(CreateRequest("SPY"));
        await _queue.EnqueueAsync(CreateRequest("AAPL"));
        await _queue.MarkCompletedAsync(spy.JobId, true);

        var pendingJobs = _queue.GetJobs(j => j.Status == BackfillJobStatus.Pending);

        pendingJobs.Should().HaveCount(1);
        pendingJobs[0].Symbols.Should().Contain("AAPL");
    }

    [Fact]
    public async Task GetStatistics_ReturnsAccurateCounts()
    {
        var j1 = await _queue.EnqueueAsync(CreateRequest("SPY"));
        var j2 = await _queue.EnqueueAsync(CreateRequest("AAPL"));
        var j3 = await _queue.EnqueueAsync(CreateRequest("MSFT"));
        await _queue.MarkCompletedAsync(j1.JobId, true);
        await _queue.MarkCompletedAsync(j2.JobId, false);

        var stats = _queue.GetStatistics();

        stats.TotalJobs.Should().Be(3);
        stats.CompletedJobs.Should().Be(1);
        stats.FailedJobs.Should().Be(1);
        stats.PendingJobs.Should().Be(1);
    }

    [Fact]
    public async Task GetCancellationToken_ReturnsValidToken()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));

        var token = _queue.GetCancellationToken(job.JobId);

        token.Should().NotBe(CancellationToken.None);
        token.CanBeCanceled.Should().BeTrue();
    }

    [Fact]
    public async Task CancelJob_CancelsToken()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));
        var token = _queue.GetCancellationToken(job.JobId);

        _queue.CancelJob(job.JobId);

        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void GetCancellationToken_UnknownJob_ReturnsNone()
    {
        var token = _queue.GetCancellationToken("nonexistent");

        token.Should().Be(CancellationToken.None);
    }

    [Fact]
    public async Task EnqueueBatchAsync_EnqueuesMultipleJobs()
    {
        var requests = new[]
        {
            CreateRequest("SPY"),
            CreateRequest("AAPL"),
            CreateRequest("MSFT"),
        };

        var result = await _queue.EnqueueBatchAsync(requests);

        result.TotalEnqueued.Should().Be(3);
        result.AllSucceeded.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueBatchAsync_WithDependencyChain_CreatesChain()
    {
        var requests = new[]
        {
            CreateRequest("SPY"),
            CreateRequest("AAPL"),
        };

        var result = await _queue.EnqueueBatchAsync(requests,
            new BatchEnqueueOptions { CreateDependencyChain = true });

        result.TotalEnqueued.Should().Be(2);
        // The second job should be paused waiting for the first
        // (depends on the internal dependency logic)
    }

    [Fact]
    public async Task JobStatusChanged_Event_FiresOnStatusChange()
    {
        var events = new List<JobStatusChangedEventArgs>();
        _queue.JobStatusChanged += (_, e) => events.Add(e);

        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));
        _queue.CancelJob(job.JobId);

        events.Should().HaveCountGreaterThanOrEqualTo(1);
        events.Should().Contain(e => e.CurrentStatus == BackfillJobStatus.Cancelled);
    }

    [Fact]
    public async Task Dispose_CancelsAllTokens()
    {
        var job = await _queue.EnqueueAsync(CreateRequest("SPY"));
        var token = _queue.GetCancellationToken(job.JobId);

        _queue.Dispose();

        token.IsCancellationRequested.Should().BeTrue(
            "all tokens should be cancelled on dispose");
    }

    #region Helpers

    private static BackfillJobRequest CreateRequest(
        string symbol,
        BackfillPriority priority = BackfillPriority.Normal)
    {
        return new BackfillJobRequest
        {
            Symbol = symbol,
            StartDate = new DateOnly(2024, 1, 1),
            EndDate = new DateOnly(2024, 12, 31),
            Priority = priority
        };
    }

    #endregion
}
