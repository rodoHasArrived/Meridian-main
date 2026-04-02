using FluentAssertions;
using Meridian.Contracts.Domain;
using Meridian.Storage;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for <see cref="JsonlPositionSnapshotStore"/> — round-trip serialisation,
/// history filtering, and multi-account isolation.
/// </summary>
public sealed class PositionSnapshotStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly JsonlPositionSnapshotStore _store;

    public PositionSnapshotStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "meridian_snapshot_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _store = new JsonlPositionSnapshotStore(
            new StorageOptions { RootPath = _tempRoot },
            NullLogger<JsonlPositionSnapshotStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ─── GetLatestSnapshot ────────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestSnapshot_NoFile_ReturnsNull()
    {
        var result = await _store.GetLatestSnapshotAsync("run-1", "acc-1");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAndGetLatest_RoundTrip_MatchesOriginal()
    {
        var snapshot = BuildSnapshot("run-1", "acc-1", cash: 50_000m);

        await _store.SaveSnapshotAsync(snapshot);
        var loaded = await _store.GetLatestSnapshotAsync("run-1", "acc-1");

        loaded.Should().NotBeNull();
        loaded!.RunId.Should().Be("run-1");
        loaded.AccountId.Should().Be("acc-1");
        loaded.Cash.Should().Be(50_000m);
    }

    [Fact]
    public async Task GetLatestSnapshot_MultipleWrites_ReturnsNewest()
    {
        var first  = BuildSnapshot("run-1", "acc-1", cash: 10_000m, minutesAgo: 5);
        var second = BuildSnapshot("run-1", "acc-1", cash: 20_000m, minutesAgo: 2);
        var third  = BuildSnapshot("run-1", "acc-1", cash: 30_000m, minutesAgo: 0);

        await _store.SaveSnapshotAsync(first);
        await _store.SaveSnapshotAsync(second);
        await _store.SaveSnapshotAsync(third);

        var latest = await _store.GetLatestSnapshotAsync("run-1", "acc-1");

        latest!.Cash.Should().Be(30_000m);
    }

    // ─── GetSnapshotHistory ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSnapshotHistory_NoFile_ReturnsEmpty()
    {
        var results = new List<AccountSnapshotRecord>();
        await foreach (var r in _store.GetSnapshotHistoryAsync(
            "run-x", "acc-x",
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow))
        {
            results.Add(r);
        }

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSnapshotHistory_FiltersToDateRange()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshots = new[]
        {
            BuildSnapshotAt("run-2", "acc-1", now.AddHours(-3), cash: 1m),
            BuildSnapshotAt("run-2", "acc-1", now.AddHours(-1), cash: 2m),
            BuildSnapshotAt("run-2", "acc-1", now.AddHours(-0), cash: 3m),
        };

        foreach (var s in snapshots)
            await _store.SaveSnapshotAsync(s);

        var results = new List<AccountSnapshotRecord>();
        await foreach (var r in _store.GetSnapshotHistoryAsync(
            "run-2", "acc-1",
            now.AddHours(-2),
            now.AddMinutes(1)))
        {
            results.Add(r);
        }

        results.Should().HaveCount(2);
        results.All(r => r.Cash > 1m).Should().BeTrue();
    }

    // ─── Account isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task DifferentAccounts_DoNotShareFile()
    {
        await _store.SaveSnapshotAsync(BuildSnapshot("run-3", "acc-a", cash: 1_000m));
        await _store.SaveSnapshotAsync(BuildSnapshot("run-3", "acc-b", cash: 2_000m));

        var a = await _store.GetLatestSnapshotAsync("run-3", "acc-a");
        var b = await _store.GetLatestSnapshotAsync("run-3", "acc-b");

        a!.Cash.Should().Be(1_000m);
        b!.Cash.Should().Be(2_000m);
    }

    // ─── File path under StorageRoot (LifecyclePolicyEngine compliance) ───────

    [Fact]
    public async Task SaveSnapshot_WritesFileUnderStorageRootPortfoliosSubfolder()
    {
        await _store.SaveSnapshotAsync(BuildSnapshot("run-lifecycle", "acc-lifecycle", cash: 0m));

        var expectedPath = Path.Combine(
            _tempRoot, "portfolios", "run-lifecycle", "acc-lifecycle", "snapshots.jsonl");

        File.Exists(expectedPath).Should().BeTrue(
            "LifecyclePolicyEngine scans {StorageRoot}/portfolios/**/*.jsonl for tiered-storage enforcement");
    }

    // ─── Positions serialisation ──────────────────────────────────────────────

    [Fact]
    public async Task SaveSnapshot_WithPositions_RoundTripsPositionData()
    {
        var snapshot = BuildSnapshot("run-4", "acc-1", cash: 0m) with
        {
            Positions = [new PositionRecord("AAPL", 10m, 150m, 50m, 0m)],
        };

        await _store.SaveSnapshotAsync(snapshot);
        var loaded = await _store.GetLatestSnapshotAsync("run-4", "acc-1");

        loaded!.Positions.Should().HaveCount(1);
        loaded.Positions[0].Symbol.Should().Be("AAPL");
        loaded.Positions[0].Quantity.Should().Be(10m);
        loaded.Positions[0].CostBasis.Should().Be(150m);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AccountSnapshotRecord BuildSnapshot(
        string runId,
        string accountId,
        decimal cash,
        int minutesAgo = 0) =>
        BuildSnapshotAt(runId, accountId, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), cash);

    private static AccountSnapshotRecord BuildSnapshotAt(
        string runId,
        string accountId,
        DateTimeOffset asOf,
        decimal cash) => new(
        RunId: runId,
        AccountId: accountId,
        AccountDisplayName: $"Account {accountId}",
        AccountKind: "Brokerage",
        Cash: cash,
        MarginBalance: 0m,
        UnrealisedPnl: 0m,
        RealisedPnl: 0m,
        Positions: [],
        AsOf: asOf);
}
