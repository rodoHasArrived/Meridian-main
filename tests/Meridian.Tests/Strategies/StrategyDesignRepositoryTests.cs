using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Guards the draft recovery scenario where an operator closes the workstation mid-design and
/// expects the latest Strategy Builder draft to survive process restart.
/// </summary>
public sealed class StrategyDesignRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "strategy-designs", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Scenario_DraftRestart_LatestJsonlVersionShouldBeLoaded()
    {
        var first = BuildDocument("Income screen", DateTimeOffset.UtcNow.AddMinutes(-5));
        var second = first with
        {
            Name = "Income screen revised",
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var writer = BuildRepository();
        await writer.SaveAsync(first);
        await writer.SaveAsync(second);

        var reader = BuildRepository();
        var loaded = await reader.GetAsync(first.DocumentId);
        var drafts = await reader.ListDraftsAsync();

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Income screen revised");
        drafts.Should().ContainSingle();
        drafts[0].Should().Match<StrategyDesignDraftSummary>(summary =>
            summary.DocumentId == first.DocumentId &&
            summary.Name == "Income screen revised" &&
            summary.CellCount == 2);
    }

    [Fact]
    public async Task SaveAsync_CanceledToken_ShouldNotAppendDraftLine()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var repository = BuildRepository();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.SaveAsync(BuildDocument("Canceled draft", DateTimeOffset.UtcNow), cts.Token));

        var drafts = await repository.ListDraftsAsync();
        drafts.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private JsonlStrategyDesignRepository BuildRepository()
        => new(_root, NullLogger<JsonlStrategyDesignRepository>.Instance);

    private static StrategyDesignDocument BuildDocument(string name, DateTimeOffset updatedAt)
        => new(
            "draft-1",
            name,
            "Restart-safe draft",
            "1",
            "provider-bars/equities/daily",
            ["SPY"],
            [
                new("universe", "Universe", "visual", "universe", "PRICE > 20", ["PRICE"]),
                new("rank", "Rank", "formula", "rank", "MOMENTUM_63D", ["MOMENTUM_63D"])
            ],
            [
                new("t1", "universe", "rank", "next", "universe ready")
            ],
            null,
            updatedAt.AddMinutes(-10),
            updatedAt);
}
