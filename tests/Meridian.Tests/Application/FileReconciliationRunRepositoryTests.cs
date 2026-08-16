using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Application;

public sealed class FileReconciliationRunRepositoryTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"meridian-recon-runs-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_PersistsAcrossNewInstance()
    {
        var detail = BuildDetail("recon-1", "run-1", DateTimeOffset.UtcNow);

        var first = CreateRepository();
        await first.SaveAsync(detail);

        var reopened = CreateRepository();
        var loaded = await reopened.GetByIdAsync("recon-1");

        loaded.Should().NotBeNull();
        loaded!.Summary.RunId.Should().Be("run-1");
        loaded.Summary.ReconciliationRunId.Should().Be("recon-1");
    }

    [Fact]
    public async Task GetLatestForRunAsync_ReturnsMostRecentByCreatedAt()
    {
        var repository = CreateRepository();
        var older = BuildDetail("recon-older", "run-1", DateTimeOffset.UtcNow.AddHours(-2));
        var newer = BuildDetail("recon-newer", "run-1", DateTimeOffset.UtcNow);
        await repository.SaveAsync(older);
        await repository.SaveAsync(newer);

        var latest = await repository.GetLatestForRunAsync("run-1");

        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be("recon-newer");
    }

    [Fact]
    public async Task GetHistoryForRunAsync_ReturnsSummariesNewestFirst()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(BuildDetail("recon-a", "run-1", DateTimeOffset.UtcNow.AddHours(-1)));
        await repository.SaveAsync(BuildDetail("recon-b", "run-1", DateTimeOffset.UtcNow));
        await repository.SaveAsync(BuildDetail("recon-other", "run-2", DateTimeOffset.UtcNow));

        var history = await repository.GetHistoryForRunAsync("run-1");

        history.Should().HaveCount(2);
        history[0].ReconciliationRunId.Should().Be("recon-b");
        history[1].ReconciliationRunId.Should().Be("recon-a");
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingRunWithSameId()
    {
        var repository = CreateRepository();
        await repository.SaveAsync(BuildDetail("recon-1", "run-1", DateTimeOffset.UtcNow, breakCount: 0));
        await repository.SaveAsync(BuildDetail("recon-1", "run-1", DateTimeOffset.UtcNow, breakCount: 5));

        var reopened = CreateRepository();
        var loaded = await reopened.GetByIdAsync("recon-1");

        loaded.Should().NotBeNull();
        loaded!.Summary.BreakCount.Should().Be(5);
    }

    [Fact]
    public async Task SaveAsync_MergesWritesFromSeparateInstancesSharingTheSameFile()
    {
        // Two instances pointing at the same data directory (e.g. browser workstation + WPF desktop).
        var instanceA = CreateRepository();
        var instanceB = CreateRepository();

        await instanceA.SaveAsync(BuildDetail("recon-a", "run-1", DateTimeOffset.UtcNow));
        // instanceB loaded before A's write; SaveAsync must re-read the snapshot so A's run survives.
        await instanceB.SaveAsync(BuildDetail("recon-b", "run-2", DateTimeOffset.UtcNow));

        var reopened = CreateRepository();
        (await reopened.GetByIdAsync("recon-a")).Should().NotBeNull();
        (await reopened.GetByIdAsync("recon-b")).Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReflectsRunSavedByAnotherInstanceAfterAnEarlierRead()
    {
        var reader = CreateRepository();
        // Prime the reader with an earlier read so a stale cache (if any) would be populated.
        (await reader.GetByIdAsync("recon-x")).Should().BeNull();

        var writer = CreateRepository();
        await writer.SaveAsync(BuildDetail("recon-x", "run-1", DateTimeOffset.UtcNow));

        // The reader must observe the run written by the other instance, not a cached empty view.
        (await reader.GetByIdAsync("recon-x")).Should().NotBeNull();
    }

    [Fact]
    public async Task SaveWithFirstObservationContinuityAsync_ConcurrentOutOfOrderWritersPreserveEarliestObservation()
    {
        var earlierAt = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);
        var laterAt = earlierAt.AddHours(3);
        var earlierWriter = CreateRepository();
        var laterWriter = CreateRepository();

        await Task.WhenAll(
            laterWriter.SaveWithFirstObservationContinuityAsync(
                BuildContinuityDetail("reconciliation-later", "run-concurrent", laterAt)),
            earlierWriter.SaveWithFirstObservationContinuityAsync(
                BuildContinuityDetail("reconciliation-earlier", "run-concurrent", earlierAt)));

        var latest = await CreateRepository().GetLatestForRunAsync("run-concurrent");

        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be("reconciliation-later");
        latest.Breaks.Should().ContainSingle();
        latest.Breaks[0].FirstObservedAt.Should().Be(earlierAt);
        latest.Breaks[0].LogicalBreakIdentity.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SaveWithFirstObservationContinuityAsync_EqualTimestampUsesCommitOrderNotRandomRunId()
    {
        var observedAt = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);
        var repository = CreateRepository();
        await repository.SaveWithFirstObservationContinuityAsync(
            BuildContinuityDetail("reconciliation-zzzz-break", "run-equal-time", observedAt));
        await repository.SaveWithFirstObservationContinuityAsync(
            BuildMatchedContinuityDetail("reconciliation-aaaa-match", "run-equal-time", observedAt));

        var latest = await CreateRepository().GetLatestForRunAsync("run-equal-time");

        latest.Should().NotBeNull();
        latest!.Summary.ReconciliationRunId.Should().Be("reconciliation-aaaa-match");
        latest.Matches.Should().ContainSingle();

        var reopenedAt = observedAt.AddMinutes(1);
        var reopened = await repository.SaveWithFirstObservationContinuityAsync(
            BuildContinuityDetail("reconciliation-reopened", "run-equal-time", reopenedAt));
        reopened.Breaks[0].FirstObservedAt.Should().Be(reopenedAt);
    }

    [Fact]
    public async Task ExecuteWithLatestForRunLeaseAsync_HoldsCrossInstanceMutationLeaseThroughCallback()
    {
        var createdAt = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);
        var leaseOwner = CreateRepository();
        var competingWriter = CreateRepository();
        await leaseOwner.SaveAsync(BuildDetail("reconciliation-leased", "run-leased", createdAt));
        var callbackStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCallback = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var leasedRead = leaseOwner.ExecuteWithLatestForRunLeaseAsync(
            "run-leased",
            async (latest, ct) =>
            {
                callbackStarted.TrySetResult(true);
                await releaseCallback.Task.WaitAsync(ct);
                return latest!.Summary.ReconciliationRunId;
            });
        await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var competingSave = competingWriter.SaveAsync(
            BuildDetail("reconciliation-competing", "run-leased", createdAt.AddMinutes(1)));
        try
        {
            competingSave.IsCompleted.Should().BeFalse();
        }
        finally
        {
            releaseCallback.TrySetResult(true);
        }

        (await leasedRead).Should().Be("reconciliation-leased");
        await competingSave;
        (await CreateRepository().GetLatestForRunAsync("run-leased"))!
            .Summary.ReconciliationRunId.Should().Be("reconciliation-competing");
    }

    [Fact]
    public async Task ExecuteWithLatestForRunLeaseAsync_ReentryFailsFastInsteadOfDeadlocking()
    {
        var createdAt = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);
        var repository = CreateRepository();
        var reentrantReader = CreateRepository();
        await repository.SaveAsync(BuildDetail("reconciliation-reentrant", "run-reentrant", createdAt));

        var act = () => repository.ExecuteWithLatestForRunLeaseAsync(
            "run-reentrant",
            async (_, ct) =>
            {
                _ = await reentrantReader.GetLatestForRunAsync("run-reentrant", ct);
                return true;
            });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot re-enter*");
    }

    private FileReconciliationRunRepository CreateRepository()
        => new(_dataRoot, NullLogger<FileReconciliationRunRepository>.Instance);

    private static ReconciliationRunDetail BuildDetail(
        string reconciliationRunId,
        string runId,
        DateTimeOffset createdAt,
        int breakCount = 0)
        => new(
            new ReconciliationRunSummary(
                ReconciliationRunId: reconciliationRunId,
                RunId: runId,
                CreatedAt: createdAt,
                PortfolioAsOf: createdAt,
                LedgerAsOf: createdAt,
                MatchCount: 3,
                BreakCount: breakCount,
                OpenBreakCount: breakCount,
                HasTimingDrift: false,
                AmountTolerance: 0.01m,
                MaxAsOfDriftMinutes: 10),
            Matches: [],
            Breaks: []);

    private static ReconciliationRunDetail BuildContinuityDetail(
        string reconciliationRunId,
        string runId,
        DateTimeOffset createdAt)
        => new(
            new ReconciliationRunSummary(
                ReconciliationRunId: reconciliationRunId,
                RunId: runId,
                CreatedAt: createdAt,
                PortfolioAsOf: createdAt,
                LedgerAsOf: createdAt,
                MatchCount: 0,
                BreakCount: 1,
                OpenBreakCount: 1,
                HasTimingDrift: false,
                AmountTolerance: 0.01m,
                MaxAsOfDriftMinutes: 10),
            Matches: [],
            Breaks:
            [
                new ReconciliationBreakDto(
                    "cash-balance",
                    "Cash balance",
                    ReconciliationBreakCategory.AmountMismatch,
                    ReconciliationBreakStatus.Open,
                    "ledger",
                    1m,
                    2m,
                    1m,
                    ReconciliationBreakSeverity.High,
                    "Cash differs.",
                    null,
                    null)
                {
                    FirstObservedAt = createdAt
                }
            ]);

    private static ReconciliationRunDetail BuildMatchedContinuityDetail(
        string reconciliationRunId,
        string runId,
        DateTimeOffset createdAt)
        => new(
            new ReconciliationRunSummary(
                ReconciliationRunId: reconciliationRunId,
                RunId: runId,
                CreatedAt: createdAt,
                PortfolioAsOf: createdAt,
                LedgerAsOf: createdAt,
                MatchCount: 1,
                BreakCount: 0,
                OpenBreakCount: 0,
                HasTimingDrift: false,
                AmountTolerance: 0.01m,
                MaxAsOfDriftMinutes: 10),
            Matches:
            [
                new ReconciliationMatchDto(
                    "cash-balance",
                    "Cash balance",
                    "portfolio",
                    "ledger",
                    1m,
                    1m,
                    0m,
                    null,
                    null)
            ],
            Breaks: []);

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
