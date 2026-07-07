using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

/// <summary>
/// Coverage for the daily reconciliation entry point (<see cref="ReconciliationRunOrchestrator"/>)
/// wired to the real <see cref="DefaultReconciliationIngestionScheduler"/> and
/// <see cref="ReconciliationMatchingEngine"/>. The matching engine's matching semantics are covered
/// separately; these tests pin the orchestration contract: ingestion fan-out, idempotency caching,
/// rerun snapshot versioning, failure propagation, and cancellation.
/// </summary>
public sealed class ReconciliationRunOrchestratorTests
{
    private static readonly DateOnly BusinessDate = new(2026, 5, 28);
    private static readonly DateTimeOffset RunTimestamp = new(2026, 5, 28, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunDailyAsync_FirstRun_CapturesSnapshotsAndMatchesThroughEngine()
    {
        var adapter = new CountingSourceAdapter(ReconciliationSourceType.Prime);
        var orchestrator = CreateOrchestrator(adapter);

        var result = await orchestrator.RunDailyAsync(BusinessDate, RunTimestamp, "USD", rerun: false, CreateTolerances(), CancellationToken.None);

        adapter.CaptureCount.Should().Be(1);
        result.run.SnapshotVersion.Should().Be(1);
        result.run.BusinessDate.Should().Be(BusinessDate);
        result.run.IdempotencyKey.Should().Be("recon:2026-05-28");
        result.run.ToleranceProfileId.Should().Be(StatementToleranceProfile.DefaultProfileId);
        // A single unmatched position must surface as a true break through the engine.
        result.breaks.Should().ContainSingle(b => b.Classification == BreakClassification.TrueBreak);
    }

    [Fact]
    public async Task RunDailyAsync_RepeatWithoutRerun_ReturnsCachedRunWithoutRecapturing()
    {
        var adapter = new CountingSourceAdapter(ReconciliationSourceType.Prime);
        var orchestrator = CreateOrchestrator(adapter);

        var first = await orchestrator.RunDailyAsync(BusinessDate, RunTimestamp, "USD", rerun: false, CreateTolerances(), CancellationToken.None);
        var second = await orchestrator.RunDailyAsync(BusinessDate, RunTimestamp, "USD", rerun: false, CreateTolerances(), CancellationToken.None);

        adapter.CaptureCount.Should().Be(1, "an idempotent re-request must reuse the captured run");
        second.run.RunId.Should().Be(first.run.RunId);
        second.run.SnapshotVersion.Should().Be(1);
    }

    [Fact]
    public async Task RunDailyAsync_RerunRequested_RecapturesAndBumpsSnapshotVersion()
    {
        var adapter = new CountingSourceAdapter(ReconciliationSourceType.Prime);
        var orchestrator = CreateOrchestrator(adapter);

        var first = await orchestrator.RunDailyAsync(BusinessDate, RunTimestamp, "USD", rerun: false, CreateTolerances(), CancellationToken.None);
        var rerun = await orchestrator.RunDailyAsync(BusinessDate, RunTimestamp, "USD", rerun: true, CreateTolerances(), CancellationToken.None);

        adapter.CaptureCount.Should().Be(2);
        rerun.run.SnapshotVersion.Should().Be(2);
        rerun.run.RunId.Should().NotBe(first.run.RunId);
    }

    [Fact]
    public async Task RunDailyAsync_WhenAdapterFails_PropagatesAndDoesNotCacheRun()
    {
        var failing = new ThrowingSourceAdapter(ReconciliationSourceType.Prime);
        var orchestrator = CreateOrchestrator(failing);

        var act = async () => await orchestrator.RunDailyAsync(BusinessDate, RunTimestamp, "USD", rerun: false, CreateTolerances(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        // The failed run must not have been cached, so a retry captures again rather than serving a
        // partial/empty cached result.
        await act.Should().ThrowAsync<InvalidOperationException>();
        failing.CaptureAttempts.Should().Be(2);
    }

    [Fact]
    public async Task RunDailyAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        var adapter = new CountingSourceAdapter(ReconciliationSourceType.Prime);
        var orchestrator = CreateOrchestrator(adapter);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await orchestrator.RunDailyAsync(BusinessDate, RunTimestamp, "USD", rerun: false, CreateTolerances(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ReconciliationRunOrchestrator CreateOrchestrator(params IReconciliationSourceAdapter[] adapters) =>
        new(new DefaultReconciliationIngestionScheduler(), adapters, new ReconciliationMatchingEngine());

    private static MatchingTolerances CreateTolerances() => new(0m, 0m, 0m, 0m, TimeSpan.FromMinutes(5));

    private static DataSourceSnapshot CreateSnapshot(string id, ReconciliationSourceType sourceType) =>
        new(id, sourceType, DateTimeOffset.UtcNow, "v1",
            [new("p1", "AAPL", null, null, "AAPL", null, 10m, 100m, 1000m, "USD", DateTimeOffset.UtcNow, "p1")],
            []);

    private sealed class CountingSourceAdapter(ReconciliationSourceType sourceType) : IReconciliationSourceAdapter
    {
        public ReconciliationSourceType SourceType { get; } = sourceType;

        public int CaptureCount { get; private set; }

        public Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CaptureCount++;
            return Task.FromResult(CreateSnapshot($"snap-{SourceType}-{request.SnapshotVersion}", SourceType));
        }
    }

    private sealed class ThrowingSourceAdapter(ReconciliationSourceType sourceType) : IReconciliationSourceAdapter
    {
        public ReconciliationSourceType SourceType { get; } = sourceType;

        public int CaptureAttempts { get; private set; }

        public Task<DataSourceSnapshot> CaptureSnapshotAsync(ReconciliationIngestionRequest request, CancellationToken ct)
        {
            CaptureAttempts++;
            throw new InvalidOperationException("adapter capture failed");
        }
    }
}
