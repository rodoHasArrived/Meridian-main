using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Pipeline;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

/// <summary>
/// Tests for <see cref="IngestionJobService"/> — the unified job lifecycle manager.
/// Validates job creation, state transitions, checkpoint management, and querying.
/// </summary>
public sealed class IngestionJobServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IngestionJobService _service;

    public IngestionJobServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        _service = new IngestionJobService(_tempDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task CreateJobAsync_ReturnsJobInDraftState()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY", "AAPL" },
            "alpaca");

        job.Should().NotBeNull();
        job.State.Should().Be(IngestionJobState.Draft);
        job.WorkloadType.Should().Be(IngestionWorkloadType.Historical);
        job.Symbols.Should().BeEquivalentTo(new[] { "SPY", "AAPL" });
        job.Provider.Should().Be("alpaca");
        job.SymbolProgress.Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateJobAsync_PersistsToDisk()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        var filePath = Path.Combine(_tempDir, $"job_{job.JobId}.json");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateJobAsync_EmptySymbols_Throws()
    {
        var act = async () => await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            Array.Empty<string>(),
            "alpaca");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TransitionAsync_ValidTransition_Succeeds()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        var result = await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);

        result.Should().BeTrue();
        _service.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Queued);
    }

    [Fact]
    public async Task TransitionAsync_InvalidTransition_ReturnsFalse()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        // Draft → Running is invalid (must go through Queued)
        var result = await _service.TransitionAsync(job.JobId, IngestionJobState.Running);

        result.Should().BeFalse();
        _service.GetJob(job.JobId)!.State.Should().Be(IngestionJobState.Draft);
    }

    [Fact]
    public async Task TransitionAsync_UnknownJobId_ReturnsFalse()
    {
        var result = await _service.TransitionAsync("nonexistent", IngestionJobState.Queued);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TransitionAsync_FailedToQueued_IncrementsRetry()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);
        await _service.TransitionAsync(job.JobId, IngestionJobState.Running);
        await _service.TransitionAsync(job.JobId, IngestionJobState.Failed, "Provider error");

        // Retry
        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var updated = _service.GetJob(job.JobId);
        updated!.RetryEnvelope.AttemptCount.Should().Be(1);
        updated.RetryEnvelope.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TransitionAsync_FiresJobStateChangedEvent()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        IngestionJobState? previousState = null;
        IngestionJobState? newState = null;
        _service.JobStateChanged += (_, prev, next) =>
        {
            previousState = prev;
            newState = next;
        };

        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);

        previousState.Should().Be(IngestionJobState.Draft);
        newState.Should().Be(IngestionJobState.Queued);
    }

    [Fact]
    public async Task UpdateCheckpointAsync_SetsCheckpointToken()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        var checkpoint = new IngestionCheckpointToken
        {
            LastSymbol = "SPY",
            LastDate = new DateTime(2024, 6, 15),
            LastOffset = 12345
        };

        await _service.UpdateCheckpointAsync(job.JobId, checkpoint);

        var updated = _service.GetJob(job.JobId);
        updated!.CheckpointToken.Should().NotBeNull();
        updated.CheckpointToken!.LastSymbol.Should().Be("SPY");
        updated.CheckpointToken.LastOffset.Should().Be(12345);
        updated.CheckpointToken.CapturedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task UpdateCheckpointAsync_FiresCheckpointUpdatedEvent()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        IngestionCheckpointToken? receivedCheckpoint = null;
        _service.CheckpointUpdated += (_, cp) => receivedCheckpoint = cp;

        await _service.UpdateCheckpointAsync(job.JobId, new IngestionCheckpointToken
        {
            LastSymbol = "SPY",
            LastDate = DateTime.UtcNow
        });

        receivedCheckpoint.Should().NotBeNull();
        receivedCheckpoint!.LastSymbol.Should().Be("SPY");
    }

    [Fact]
    public async Task UpdateSymbolProgressAsync_TracksProgress()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY", "AAPL" },
            "alpaca");

        await _service.UpdateSymbolProgressAsync(
            job.JobId, "SPY",
            dataPointsProcessed: 500,
            expectedDataPoints: 1000,
            lastCommittedDate: new DateTime(2024, 6, 15));

        var updated = _service.GetJob(job.JobId);
        var spyProgress = updated!.SymbolProgress.First(p => p.Symbol == "SPY");
        spyProgress.DataPointsProcessed.Should().Be(500);
        spyProgress.ExpectedDataPoints.Should().Be(1000);
        spyProgress.ProgressPercent.Should().Be(50.0);
        spyProgress.State.Should().Be(IngestionJobState.Running);
    }

    [Fact]
    public async Task UpdateSymbolProgressAsync_CompletesWhenAllDone()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        await _service.UpdateSymbolProgressAsync(
            job.JobId, "SPY",
            dataPointsProcessed: 1000,
            expectedDataPoints: 1000);

        var updated = _service.GetJob(job.JobId);
        var spyProgress = updated!.SymbolProgress.First(p => p.Symbol == "SPY");
        spyProgress.State.Should().Be(IngestionJobState.Completed);
    }

    [Fact]
    public async Task GetJobs_FiltersCorrectly()
    {
        await _service.CreateJobAsync(IngestionWorkloadType.Historical, new[] { "SPY" }, "alpaca");
        await _service.CreateJobAsync(IngestionWorkloadType.Realtime, new[] { "AAPL" }, "polygon");

        var historical = _service.GetJobs(workloadFilter: IngestionWorkloadType.Historical);
        historical.Should().HaveCount(1);
        historical[0].Provider.Should().Be("alpaca");

        var all = _service.GetJobs();
        all.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetResumableJobs_ReturnsCorrectJobs()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);
        await _service.TransitionAsync(job.JobId, IngestionJobState.Running);
        await _service.UpdateCheckpointAsync(job.JobId, new IngestionCheckpointToken
        {
            LastSymbol = "SPY",
            LastDate = DateTime.UtcNow
        });
        await _service.TransitionAsync(job.JobId, IngestionJobState.Failed, "Connection lost");

        var resumable = _service.GetResumableJobs();
        resumable.Should().HaveCount(1);
        resumable[0].JobId.Should().Be(job.JobId);
    }

    [Fact]
    public async Task GetSummary_ReturnsCorrectCounts()
    {
        var job1 = await _service.CreateJobAsync(IngestionWorkloadType.Historical, new[] { "SPY" }, "alpaca");
        var job2 = await _service.CreateJobAsync(IngestionWorkloadType.Realtime, new[] { "AAPL" }, "polygon");

        await _service.TransitionAsync(job1.JobId, IngestionJobState.Queued);
        await _service.TransitionAsync(job1.JobId, IngestionJobState.Running);
        await _service.TransitionAsync(job1.JobId, IngestionJobState.Completed);

        var summary = _service.GetSummary();
        summary.TotalJobs.Should().Be(2);
        summary.CompletedJobs.Should().Be(1);
        summary.DraftJobs.Should().Be(1);
        summary.HistoricalJobs.Should().Be(1);
        summary.RealtimeJobs.Should().Be(1);
    }

    [Fact]
    public async Task DeleteJobAsync_DeletesTerminalJob()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);
        await _service.TransitionAsync(job.JobId, IngestionJobState.Running);
        await _service.TransitionAsync(job.JobId, IngestionJobState.Completed);

        var result = await _service.DeleteJobAsync(job.JobId);

        result.Should().BeTrue();
        _service.GetJob(job.JobId).Should().BeNull();
    }

    [Fact]
    public async Task DeleteJobAsync_RejectsNonTerminalJob()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");

        var result = await _service.DeleteJobAsync(job.JobId);

        result.Should().BeFalse();
        _service.GetJob(job.JobId).Should().NotBeNull();
    }

    [Fact]
    public async Task LoadJobsAsync_RestoresPersistedJobs()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");
        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);

        // Create a new service pointing to the same directory
        using var service2 = new IngestionJobService(_tempDir);
        await service2.LoadJobsAsync();

        var loaded = service2.GetJob(job.JobId);
        loaded.Should().NotBeNull();
        loaded!.State.Should().Be(IngestionJobState.Queued);
        loaded.Symbols.Should().BeEquivalentTo(new[] { "SPY" });
    }

    [Fact]
    public async Task PersistJobAsync_WritesAtomically_NoTempFilesRemainAndJobFileValid()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");
        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);

        var jobFile = Path.Combine(_tempDir, $"job_{job.JobId}.json");

        // The persisted file is complete and parseable (never a truncated fragment).
        File.Exists(jobFile).Should().BeTrue();
        var parse = () => System.Text.Json.JsonDocument.Parse(File.ReadAllText(jobFile));
        parse.Should().NotThrow();

        // The atomic write leaves no temp sidecars, and the loader glob resolves exactly one file.
        // AtomicFileWriter temp files are leading-dot hidden (".job_*.json.{guid}.tmp")
        // and therefore do not match the "job_*.json" load glob.
        Directory.GetFiles(_tempDir, ".job_*.tmp").Should().BeEmpty();
        Directory.GetFiles(_tempDir, "job_*.json").Should().ContainSingle();
    }

    [Fact]
    public async Task LoadJobsAsync_TruncatedJobFile_SkipsCorruptAndLoadsValid()
    {
        var validJob = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY" },
            "alpaca");
        var corruptJob = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "AAPL" },
            "polygon");

        // Simulate the failure mode a non-atomic write could produce: a truncated JSON fragment
        // on disk. The atomic write prevents this on the write path; the loader must still
        // tolerate a pre-existing corrupt file by skipping it rather than failing the whole load.
        var corruptFile = Path.Combine(_tempDir, $"job_{corruptJob.JobId}.json");
        File.WriteAllText(corruptFile, "{\"JobId\":\"" + corruptJob.JobId + "\",\"Sta");

        using var service2 = new IngestionJobService(_tempDir);
        await service2.LoadJobsAsync();

        service2.GetJob(validJob.JobId).Should().NotBeNull();
        service2.GetJob(corruptJob.JobId).Should().BeNull();
    }

    [Fact]
    public async Task PersistThenReload_RoundTripsState()
    {
        var job = await _service.CreateJobAsync(
            IngestionWorkloadType.Historical,
            new[] { "SPY", "AAPL" },
            "alpaca");
        await _service.TransitionAsync(job.JobId, IngestionJobState.Queued);
        await _service.TransitionAsync(job.JobId, IngestionJobState.Running);
        await _service.UpdateCheckpointAsync(job.JobId, new IngestionCheckpointToken
        {
            LastSymbol = "SPY",
            LastDate = new DateTime(2024, 6, 15),
            LastOffset = 4242
        });
        await _service.UpdateSymbolProgressAsync(
            job.JobId, "SPY",
            dataPointsProcessed: 500,
            expectedDataPoints: 1000);

        using var service2 = new IngestionJobService(_tempDir);
        await service2.LoadJobsAsync();

        var reloaded = service2.GetJob(job.JobId);
        reloaded.Should().NotBeNull();
        reloaded!.State.Should().Be(IngestionJobState.Running);
        reloaded.CheckpointToken.Should().NotBeNull();
        reloaded.CheckpointToken!.LastSymbol.Should().Be("SPY");
        reloaded.CheckpointToken.LastOffset.Should().Be(4242);
        reloaded.SymbolProgress.First(p => p.Symbol == "SPY")
            .DataPointsProcessed.Should().Be(500);
    }
}
