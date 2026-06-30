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

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
