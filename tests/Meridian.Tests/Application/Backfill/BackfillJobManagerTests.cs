using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Tests for <see cref="BackfillJobManager"/> covering job lifecycle transitions,
/// disk persistence, and checkpoint-based resume behaviour.
///
/// <para>
/// <b>Checkpoint resume:</b> each test that verifies "resume from checkpoint" creates
/// a manager, drives some progress, persists the job to disk, then instantiates a
/// <em>fresh</em> manager that loads from the same directory.  The fresh manager must
/// see the same progress that was recorded before the simulated interruption.
/// </para>
/// </summary>
public sealed class BackfillJobManagerTests : IDisposable
{
    private readonly string _jobsDir;
    private readonly string _dataDir;
    private readonly ProviderRateLimitTracker _rateLimitTracker;

    public BackfillJobManagerTests()
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        _jobsDir = Path.Combine(Path.GetTempPath(), "BackfillJobManagerTests", runId, "jobs");
        _dataDir = Path.Combine(Path.GetTempPath(), "BackfillJobManagerTests", runId, "data");
        Directory.CreateDirectory(_jobsDir);
        Directory.CreateDirectory(_dataDir);
        _rateLimitTracker = new ProviderRateLimitTracker();
    }

    public void Dispose()
    {
        _rateLimitTracker.Dispose();
        var root = Path.GetDirectoryName(_jobsDir)!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private BackfillJobManager CreateManager() =>
        new BackfillJobManager(
            new DataGapAnalyzer(_dataDir),
            new BackfillRequestQueue(_rateLimitTracker),
            _jobsDir);

    // ── CreateJobAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateJobAsync_StoresJobInMemory_WithCorrectProperties()
    {
        using var mgr = CreateManager();

        var job = await mgr.CreateJobAsync(
            "Test Job",
            symbols: ["AAPL", "MSFT"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 31));

        job.Should().NotBeNull();
        job.Name.Should().Be("Test Job");
        job.Symbols.Should().BeEquivalentTo(["AAPL", "MSFT"]);
        job.FromDate.Should().Be(new DateOnly(2024, 1, 2));
        job.ToDate.Should().Be(new DateOnly(2024, 1, 31));
        job.Status.Should().Be(BackfillJobStatus.Pending);
        job.JobId.Should().NotBeNullOrEmpty();

        mgr.GetJob(job.JobId).Should().BeSameAs(job);
    }

    [Fact]
    public async Task CreateJobAsync_PersistsJobToDisk()
    {
        using var mgr = CreateManager();

        var job = await mgr.CreateJobAsync(
            "Persist Test",
            symbols: ["SPY"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        var expectedFile = Path.Combine(_jobsDir, $"{job.JobId}.json");
        File.Exists(expectedFile).Should().BeTrue("each created job must be persisted to disk");
    }

    [Fact]
    public async Task CreateJobAsync_NormalizesSymbolsToUpperCase()
    {
        using var mgr = CreateManager();

        var job = await mgr.CreateJobAsync(
            "Symbol Case Test",
            symbols: ["aapl", "Msft", "SPY"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        job.Symbols.Should().BeEquivalentTo(["AAPL", "MSFT", "SPY"]);
    }

    [Fact]
    public async Task CreateJobAsync_DeduplicatesSymbols()
    {
        using var mgr = CreateManager();

        var job = await mgr.CreateJobAsync(
            "Dedup Test",
            symbols: ["AAPL", "aapl", "AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        job.Symbols.Should().HaveCount(1);
        job.Symbols.Should().Contain("AAPL");
    }

    // ── Status transitions ────────────────────────────────────────────────

    [Fact]
    public async Task StartJobAsync_TransitionsFromPendingToRunning()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Start Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);

        job.Status.Should().Be(BackfillJobStatus.Running);
        job.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task StartJobAsync_WhenJobNotFound_ThrowsInvalidOperationException()
    {
        using var mgr = CreateManager();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.StartJobAsync("nonexistent-job-id"));
    }

    [Fact]
    public async Task PauseJobAsync_TransitionsFromRunningToPaused()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Pause Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);
        await mgr.PauseJobAsync(job.JobId, reason: "unit test pause");

        job.Status.Should().Be(BackfillJobStatus.Paused);
        job.StatusReason.Should().Be("unit test pause");
        job.PausedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PauseJobAsync_WhenNotRunning_ThrowsInvalidOperationException()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Pause Invalid",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        // Job is Pending — not Running — so pause should throw.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.PauseJobAsync(job.JobId));
    }

    [Fact]
    public async Task ResumeJobAsync_TransitionsFromPausedToRunning()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Resume Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);
        await mgr.PauseJobAsync(job.JobId);
        await mgr.ResumeJobAsync(job.JobId);

        job.Status.Should().Be(BackfillJobStatus.Running);
        job.PausedAt.Should().BeNull("PausedAt must be cleared on resume");
        job.StatusReason.Should().BeNull();
    }

    [Fact]
    public async Task ResumeJobAsync_WhenNotPaused_ThrowsInvalidOperationException()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Resume Invalid",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.ResumeJobAsync(job.JobId));
    }

    [Fact]
    public async Task CancelJobAsync_TransitionsToCancelled()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Cancel Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);
        await mgr.CancelJobAsync(job.JobId);

        job.Status.Should().Be(BackfillJobStatus.Cancelled);
        job.CompletedAt.Should().NotBeNull();
        job.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task SetJobRateLimitedAsync_TransitionsFromRunningToRateLimited()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Rate Limit Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);
        await mgr.SetJobRateLimitedAsync(job.JobId, resumeAfter: TimeSpan.FromMinutes(1));

        job.Status.Should().Be(BackfillJobStatus.RateLimited);
        job.StatusReason.Should().Contain("1.0 minutes");
    }

    // ── StatusChanged event ───────────────────────────────────────────────

    [Fact]
    public async Task OnJobStatusChanged_RaisedOnTransition_ContainsPreviousAndNewStatus()
    {
        using var mgr = CreateManager();
        var statusChanges = new List<(BackfillJobStatus Previous, BackfillJobStatus Current)>();
        mgr.OnJobStatusChanged += (job, prev) => statusChanges.Add((prev, job.Status));

        var job = await mgr.CreateJobAsync(
            "Event Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);
        await mgr.PauseJobAsync(job.JobId);

        statusChanges.Should().HaveCount(2);
        statusChanges[0].Should().Be((BackfillJobStatus.Pending, BackfillJobStatus.Running));
        statusChanges[1].Should().Be((BackfillJobStatus.Running, BackfillJobStatus.Paused));
    }

    // ── LoadJobsAsync — disk persistence round-trip ───────────────────────

    [Fact]
    public async Task LoadJobsAsync_LoadsAllPersistedJobsFromDisk()
    {
        // Create 3 jobs, each persisted to the jobs directory.
        using var writer = CreateManager();
        var j1 = await writer.CreateJobAsync("Job 1", ["AAPL"], new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 31));
        var j2 = await writer.CreateJobAsync("Job 2", ["MSFT"], new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29));
        var j3 = await writer.CreateJobAsync("Job 3", ["SPY"], new DateOnly(2024, 3, 1), new DateOnly(2024, 3, 31));
        writer.Dispose();

        // Fresh manager — loads from the same directory
        using var reader = CreateManager();
        await reader.LoadJobsAsync();

        reader.GetAllJobs().Should().HaveCount(3);
        reader.GetJob(j1.JobId).Should().NotBeNull();
        reader.GetJob(j2.JobId).Should().NotBeNull();
        reader.GetJob(j3.JobId).Should().NotBeNull();
    }

    [Fact]
    public async Task LoadJobsAsync_PreservesJobProperties_AfterRoundTrip()
    {
        using var writer = CreateManager();
        var original = await writer.CreateJobAsync(
            "Round-trip Test",
            symbols: ["AAPL", "MSFT"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 31),
            granularity: DataGranularity.Daily,
            options: new BackfillJobOptions { MaxConcurrentRequests = 5, MaxRetries = 2 });
        writer.Dispose();

        using var reader = CreateManager();
        await reader.LoadJobsAsync();

        var loaded = reader.GetJob(original.JobId);
        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Round-trip Test");
        loaded.Symbols.Should().BeEquivalentTo(["AAPL", "MSFT"]);
        loaded.FromDate.Should().Be(new DateOnly(2024, 1, 2));
        loaded.ToDate.Should().Be(new DateOnly(2024, 1, 31));
        loaded.Granularity.Should().Be(DataGranularity.Daily);
        loaded.Options.MaxConcurrentRequests.Should().Be(5);
        loaded.Options.MaxRetries.Should().Be(2);
    }

    // ── Checkpoint: mid-range interruption and resume ─────────────────────

    [Fact]
    public async Task Checkpoint_AfterPauseAndReload_PreservesFilledDatesAndProgress()
    {
        // Phase 1: start job, complete some requests, pause.
        using var phase1 = CreateManager();
        var job = await phase1.CreateJobAsync(
            "Checkpoint Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 31),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await phase1.StartJobAsync(job.JobId);

        // Simulate completing the first week's data (Jan 2-5) by calling UpdateJobProgressAsync.
        var completedRequest = new BackfillRequest
        {
            JobId = job.JobId,
            Symbol = "AAPL",
            FromDate = new DateOnly(2024, 1, 2),
            ToDate = new DateOnly(2024, 1, 5),
            Status = BackfillRequestStatus.Completed,
            BarsRetrieved = 40,
            AssignedProvider = "alpaca"
        };
        await phase1.UpdateJobProgressAsync(completedRequest);

        await phase1.PauseJobAsync(job.JobId, reason: "simulated mid-range interruption");

        var pausedJobId = job.JobId;
        var filledBeforePause = job.SymbolProgress["AAPL"].FilledDates.Count;
        var barsBeforePause = job.Statistics.TotalBarsRetrieved;

        phase1.Dispose();

        // Phase 2: fresh manager loads the persisted job and verifies checkpoint data.
        using var phase2 = CreateManager();
        await phase2.LoadJobsAsync();

        var reloaded = phase2.GetJob(pausedJobId);
        reloaded.Should().NotBeNull("job must survive manager restart via disk persistence");
        reloaded!.Status.Should().Be(BackfillJobStatus.Paused,
            "paused status must be preserved across restart");
        reloaded.StatusReason.Should().Contain("mid-range interruption",
            "pause reason must be preserved in the checkpoint");

        var reloadedProgress = reloaded.SymbolProgress["AAPL"];
        reloadedProgress.FilledDates.Should().HaveCount(filledBeforePause,
            "filled dates must survive the restart checkpoint");
        reloaded.Statistics.TotalBarsRetrieved.Should().Be(barsBeforePause,
            "bars retrieved must survive the restart checkpoint");

        // The job should be resumable from its paused state.
        reloaded.CanStart.Should().BeTrue("a paused job must be resumable (CanStart includes Paused)");
    }

    [Fact]
    public async Task Checkpoint_ResumedJobDoesNotResetProgressCounters()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Resume Counter Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 31),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);

        var completedRequest = new BackfillRequest
        {
            JobId = job.JobId,
            Symbol = "AAPL",
            FromDate = new DateOnly(2024, 1, 2),
            ToDate = new DateOnly(2024, 1, 5),
            Status = BackfillRequestStatus.Completed,
            BarsRetrieved = 40,
            AssignedProvider = "polygon"
        };
        await mgr.UpdateJobProgressAsync(completedRequest);

        var barsAfterFirstBatch = job.Statistics.TotalBarsRetrieved;

        await mgr.PauseJobAsync(job.JobId);
        await mgr.ResumeJobAsync(job.JobId);

        // Progress counters must not be reset by resume.
        job.Statistics.TotalBarsRetrieved.Should().Be(barsAfterFirstBatch,
            "resume must not reset previously recorded bars retrieved");
        job.SymbolProgress["AAPL"].FilledDates.Should().NotBeEmpty(
            "resume must not clear filled dates from checkpoint");
    }

    [Fact]
    public async Task Checkpoint_MultipleSymbols_EachSymbolProgressPreservedIndependently()
    {
        using var phase1 = CreateManager();
        var job = await phase1.CreateJobAsync(
            "Multi-Symbol Checkpoint",
            symbols: ["AAPL", "MSFT"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 31),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await phase1.StartJobAsync(job.JobId);

        // Only AAPL makes progress before the interruption.
        await phase1.UpdateJobProgressAsync(new BackfillRequest
        {
            JobId = job.JobId,
            Symbol = "AAPL",
            FromDate = new DateOnly(2024, 1, 2),
            ToDate = new DateOnly(2024, 1, 5),
            Status = BackfillRequestStatus.Completed,
            BarsRetrieved = 20,
            AssignedProvider = "alpaca"
        });

        await phase1.PauseJobAsync(job.JobId, reason: "multi-symbol interruption test");
        var jobId = job.JobId;
        phase1.Dispose();

        using var phase2 = CreateManager();
        await phase2.LoadJobsAsync();

        var reloaded = phase2.GetJob(jobId);
        reloaded.Should().NotBeNull();

        reloaded!.SymbolProgress["AAPL"].FilledDates.Should().NotBeEmpty(
            "AAPL made progress and must have filled dates after reload");
        reloaded.SymbolProgress["MSFT"].FilledDates.Should().BeEmpty(
            "MSFT made no progress and must have no filled dates");
    }

    // ── UpdateJobProgressAsync ────────────────────────────────────────────

    [Fact]
    public async Task UpdateJobProgressAsync_OnCompletion_MarksTradingDaysAsFilled()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Progress Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 31),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);

        // Complete the range Jan 2-5 (Tue-Fri = 4 trading days)
        await mgr.UpdateJobProgressAsync(new BackfillRequest
        {
            JobId = job.JobId,
            Symbol = "AAPL",
            FromDate = new DateOnly(2024, 1, 2),
            ToDate = new DateOnly(2024, 1, 5),
            Status = BackfillRequestStatus.Completed,
            BarsRetrieved = 40
        });

        var progress = job.SymbolProgress["AAPL"];
        progress.FilledDates.Should().Contain(new DateOnly(2024, 1, 2));
        progress.FilledDates.Should().Contain(new DateOnly(2024, 1, 3));
        progress.FilledDates.Should().Contain(new DateOnly(2024, 1, 4));
        progress.FilledDates.Should().Contain(new DateOnly(2024, 1, 5));
        progress.FilledDates.Should().NotContain(new DateOnly(2024, 1, 6), "Saturday must not be marked as filled");
        progress.FilledDates.Should().NotContain(new DateOnly(2024, 1, 7), "Sunday must not be marked as filled");
        progress.BarsRetrieved.Should().Be(40);
    }

    [Fact]
    public async Task UpdateJobProgressAsync_OnFailure_IncrementsFailedRequestsCounter()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Failure Progress Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);

        await mgr.UpdateJobProgressAsync(new BackfillRequest
        {
            JobId = job.JobId,
            Symbol = "AAPL",
            FromDate = new DateOnly(2024, 1, 2),
            ToDate = new DateOnly(2024, 1, 5),
            Status = BackfillRequestStatus.Failed,
            ErrorMessage = "HTTP 429 Too Many Requests"
        });

        var progress = job.SymbolProgress["AAPL"];
        progress.FailedRequests.Should().Be(1);
        progress.FilledDates.Should().BeEmpty("failed requests must not mark dates as filled");
        job.Statistics.FailedRequests.Should().Be(1);
    }

    // ── GetJobsByStatus ───────────────────────────────────────────────────

    [Fact]
    public async Task GetJobsByStatus_ReturnsOnlyMatchingJobs()
    {
        using var mgr = CreateManager();
        var pending = await mgr.CreateJobAsync("Pending Job", ["AAPL"],
            new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5));
        var running = await mgr.CreateJobAsync("Running Job", ["MSFT"],
            new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(running.JobId);

        mgr.GetJobsByStatus(BackfillJobStatus.Pending).Should().ContainSingle()
            .Which.JobId.Should().Be(pending.JobId);
        mgr.GetJobsByStatus(BackfillJobStatus.Running).Should().ContainSingle()
            .Which.JobId.Should().Be(running.JobId);
    }

    // ── DeleteJobAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteJobAsync_CompletedJob_RemovesFromMemoryAndDisk()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Delete Test",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);
        await mgr.CancelJobAsync(job.JobId); // brings it to a terminal state
        await mgr.DeleteJobAsync(job.JobId);

        mgr.GetJob(job.JobId).Should().BeNull("deleted job must be removed from memory");

        var filePath = Path.Combine(_jobsDir, $"{job.JobId}.json");
        File.Exists(filePath).Should().BeFalse("deleted job must be removed from disk");
    }

    [Fact]
    public async Task DeleteJobAsync_RunningJob_ThrowsInvalidOperationException()
    {
        using var mgr = CreateManager();
        var job = await mgr.CreateJobAsync(
            "Delete Running",
            symbols: ["AAPL"],
            from: new DateOnly(2024, 1, 2),
            to: new DateOnly(2024, 1, 5),
            options: new BackfillJobOptions { SkipExistingData = false, FillGapsOnly = false });

        await mgr.StartJobAsync(job.JobId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mgr.DeleteJobAsync(job.JobId));
    }
}
