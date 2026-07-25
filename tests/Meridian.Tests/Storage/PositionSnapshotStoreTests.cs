using System.Text;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _tempRoot;
    private readonly JsonlPositionSnapshotStore _store;

    public PositionSnapshotStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "meridian_snapshot_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _store = CreateStore();
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
    public async Task SaveAndGetLatest_OwnedScope_DoesNotFallBackAcrossTenant()
    {
        var owner = new PositionSnapshotOwnerScope(
            "tenant-a",
            "company-a",
            "fund-a",
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            "entity-a");
        var snapshot = BuildSnapshot("run-owned", "acc-owned", cash: 50_000m) with
        {
            TenantId = owner.TenantId,
            CompanyId = owner.CompanyId,
            FundProfileId = owner.FundProfileId,
            LedgerBookId = owner.LedgerBookId,
            EntityId = owner.EntityId
        };

        await _store.SaveSnapshotAsync(snapshot);

        var owned = await _store.GetLatestSnapshotAsync("run-owned", "acc-owned", owner);
        var otherTenant = await _store.GetLatestSnapshotAsync(
            "run-owned",
            "acc-owned",
            owner with { TenantId = "tenant-b" });

        owned.Should().BeEquivalentTo(snapshot);
        otherTenant.Should().BeNull();
        (await _store.GetLatestSnapshotAsync("run-owned", "acc-owned")).Should().BeNull(
            "owned snapshots must not be exposed through the legacy unscoped lookup");
    }

    [Fact]
    public async Task SaveSnapshot_PartialOwnership_FailsClosed()
    {
        var partial = BuildSnapshot("run-partial", "acc-partial", cash: 0m) with
        {
            TenantId = "tenant-a"
        };

        var act = () => _store.SaveSnapshotAsync(partial);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tenant, company, fund profile, ledger book, and entity together*");
    }

    [Fact]
    public async Task GetLatestSnapshot_MultipleWrites_ReturnsNewest()
    {
        var first = BuildSnapshot("run-1", "acc-1", cash: 10_000m, minutesAgo: 5);
        var second = BuildSnapshot("run-1", "acc-1", cash: 20_000m, minutesAgo: 2);
        var third = BuildSnapshot("run-1", "acc-1", cash: 30_000m, minutesAgo: 0);

        await _store.SaveSnapshotAsync(first);
        await _store.SaveSnapshotAsync(second);
        await _store.SaveSnapshotAsync(third);

        var latest = await _store.GetLatestSnapshotAsync("run-1", "acc-1");

        latest!.Cash.Should().Be(30_000m);
    }

    [Fact]
    public async Task GetLatestSnapshot_OutOfOrderAppend_ReturnsGreatestAsOf()
    {
        var now = DateTimeOffset.UtcNow;
        var newest = BuildSnapshotAt("run-order", "acc-1", now, cash: 30_000m);
        var delayedOlder = BuildSnapshotAt("run-order", "acc-1", now.AddMinutes(-5), cash: 10_000m);

        await _store.SaveSnapshotAsync(newest);
        await _store.SaveSnapshotAsync(delayedOlder);

        var latest = await _store.GetLatestSnapshotAsync("run-order", "acc-1");

        latest.Should().NotBeNull();
        latest!.AsOf.Should().Be(newest.AsOf);
        latest.Cash.Should().Be(newest.Cash);
    }

    [Fact]
    public async Task GetLatestSnapshot_OwnedOutOfOrderAppend_ReturnsGreatestAsOf()
    {
        var owner = new PositionSnapshotOwnerScope(
            "tenant-a",
            "company-a",
            "fund-a",
            Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            "entity-a");
        var now = DateTimeOffset.UtcNow;
        var newest = BuildSnapshotAt("run-owned-order", "acc-1", now, cash: 30_000m) with
        {
            TenantId = owner.TenantId,
            CompanyId = owner.CompanyId,
            FundProfileId = owner.FundProfileId,
            LedgerBookId = owner.LedgerBookId,
            EntityId = owner.EntityId
        };
        var delayedOlder = newest with
        {
            Cash = 10_000m,
            AsOf = now.AddMinutes(-5)
        };

        await _store.SaveSnapshotAsync(newest);
        await _store.SaveSnapshotAsync(delayedOlder);

        var latest = await _store.GetLatestSnapshotAsync(
            "run-owned-order",
            "acc-1",
            owner);

        latest.Should().NotBeNull();
        latest!.AsOf.Should().Be(newest.AsOf);
        latest.Cash.Should().Be(newest.Cash);
    }

    [Fact]
    public async Task SaveSnapshot_SameTimestampEquivalentPayload_IsSingleAtomicAppend()
    {
        var asOf = new DateTimeOffset(2026, 7, 15, 20, 30, 0, TimeSpan.Zero);
        var first = BuildSnapshotAt("run-retry", "acc-1", asOf, cash: 30_000m) with
        {
            Positions =
            [
                new PositionRecord("AAPL", 10m, 150m, 50m, 0m),
                new PositionRecord("MSFT", 20m, 400m, 75m, 0m)
            ]
        };
        var equivalentRetry = first with
        {
            Positions =
            [
                new PositionRecord("msft", 20m, 400m, 75m, 0m),
                new PositionRecord("aapl", 10m, 150m, 50m, 0m)
            ]
        };

        var firstOutcome = await _store.SaveSnapshotConditionallyAsync(first);
        var retryOutcome = await _store.SaveSnapshotConditionallyAsync(equivalentRetry);

        firstOutcome.Should().Be(PositionSnapshotSaveOutcome.Appended);
        retryOutcome.Should().Be(PositionSnapshotSaveOutcome.EquivalentAlreadyExists);
        File.ReadLines(GetSnapshotPath("run-retry", "acc-1")).Should().ContainSingle();
        var latest = await _store.GetLatestSnapshotAsync("run-retry", "acc-1");
        PositionSnapshotEquivalence.AreEquivalent(latest!, first).Should().BeTrue();
    }

    [Fact]
    public async Task SaveSnapshot_ConcurrentDifferentPayloadsAtSameTimestamp_CommitsExactlyOne()
    {
        var firstStore = CreateStore();
        var secondStore = CreateStore();
        var asOf = new DateTimeOffset(2026, 7, 15, 20, 30, 0, TimeSpan.Zero);
        var first = BuildSnapshotAt("run-conflict", "acc-1", asOf, cash: 10_000m);
        var second = first with { Cash = 20_000m };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(PositionSnapshotSaveOutcome? Outcome, Exception? Error)> AttemptAsync(
            JsonlPositionSnapshotStore store,
            AccountSnapshotRecord snapshot)
        {
            await gate.Task;
            try
            {
                return (await store.SaveSnapshotConditionallyAsync(snapshot), null);
            }
            catch (Exception ex)
            {
                return (null, ex);
            }
        }

        var attempts = new[]
        {
            AttemptAsync(firstStore, first),
            AttemptAsync(secondStore, second)
        };
        gate.SetResult();
        var results = await Task.WhenAll(attempts);

        results.Should().ContainSingle(result => result.Outcome == PositionSnapshotSaveOutcome.Appended);
        results.Should().ContainSingle(result => result.Error is PositionSnapshotConflictException);
        File.ReadLines(GetSnapshotPath("run-conflict", "acc-1")).Should().ContainSingle();
        var latest = await _store.GetLatestSnapshotAsync("run-conflict", "acc-1");
        latest!.Cash.Should().BeOneOf(10_000m, 20_000m);
    }

    [Fact]
    public async Task SaveSnapshot_ConcurrentEquivalentPayloadsAcrossStoreInstances_AppendsOnce()
    {
        var firstStore = CreateStore();
        var secondStore = CreateStore();
        var asOf = new DateTimeOffset(2026, 7, 15, 20, 30, 0, TimeSpan.Zero);
        var first = BuildSnapshotAt("run-concurrent-retry", "acc-1", asOf, cash: 10_000m) with
        {
            Positions =
            [
                new PositionRecord("AAPL", 10m, 150m, 50m, 0m),
                new PositionRecord("MSFT", 20m, 400m, 75m, 0m)
            ]
        };
        var second = first with
        {
            Positions =
            [
                new PositionRecord("msft", 20m, 400m, 75m, 0m),
                new PositionRecord("aapl", 10m, 150m, 50m, 0m)
            ]
        };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PositionSnapshotSaveOutcome> AttemptAsync(
            JsonlPositionSnapshotStore store,
            AccountSnapshotRecord snapshot)
        {
            await gate.Task;
            return await store.SaveSnapshotConditionallyAsync(snapshot);
        }

        var attempts = new[]
        {
            AttemptAsync(firstStore, first),
            AttemptAsync(secondStore, second)
        };
        gate.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        outcomes.Should().ContainSingle(outcome => outcome == PositionSnapshotSaveOutcome.Appended);
        outcomes.Should().ContainSingle(outcome => outcome == PositionSnapshotSaveOutcome.EquivalentAlreadyExists);
        File.ReadLines(GetSnapshotPath("run-concurrent-retry", "acc-1")).Should().ContainSingle();
    }

    [Fact]
    public async Task GetLatestSnapshot_ConflictingLegacyEqualTimestampRecords_FailsClosed()
    {
        var asOf = new DateTimeOffset(2026, 7, 15, 20, 30, 0, TimeSpan.Zero);
        var first = BuildSnapshotAt("run-legacy-conflict", "acc-1", asOf, cash: 10_000m);
        var second = first with { Cash = 20_000m };
        await WriteSnapshotsDirectAsync("run-legacy-conflict", "acc-1", [first, second]);

        var act = () => _store.GetLatestSnapshotAsync("run-legacy-conflict", "acc-1");

        await act.Should().ThrowAsync<PositionSnapshotConflictException>()
            .WithMessage("*different payload at the same source timestamp*");
    }

    [Fact]
    public async Task GetLatestSnapshot_LargeLifecycleHistory_SelectsLatestAndReleasesFileHandle()
    {
        const int snapshotCount = 5_000;
        var firstAsOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var snapshots = Enumerable.Range(0, snapshotCount)
            .Select(index => BuildSnapshotAt(
                "run-large-history",
                "acc-1",
                firstAsOf.AddMinutes(index),
                cash: index));
        await WriteSnapshotsDirectAsync("run-large-history", "acc-1", snapshots);

        var latest = await _store.GetLatestSnapshotAsync("run-large-history", "acc-1");

        latest.Should().NotBeNull();
        latest!.Cash.Should().Be(snapshotCount - 1);
        latest.AsOf.Should().Be(firstAsOf.AddMinutes(snapshotCount - 1));
        var snapshotPath = GetSnapshotPath("run-large-history", "acc-1");
        var movedPath = snapshotPath + ".moved";
        File.Move(snapshotPath, movedPath);
        File.Exists(movedPath).Should().BeTrue("the streaming reader must release its file handle");
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

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData(@"..\outside")]
    [InlineData("run/child")]
    [InlineData(@"run\child")]
    [InlineData("/absolute")]
    [InlineData(@"C:\absolute")]
    public async Task SaveSnapshot_InvalidRunPathSegment_RejectsWithoutCreatingFiles(string runId)
    {
        var act = () => _store.SaveSnapshotAsync(BuildSnapshot(runId, "acc-contained", cash: 1_000m));

        await act.Should().ThrowAsync<ArgumentException>();
        Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task SnapshotPartition_DotTraversal_CannotReadOrWriteOutsideConfiguredRoot()
    {
        var containedRoot = Path.Combine(_tempRoot, "contained-root");
        var outsidePath = Path.Combine(_tempRoot, "snapshots.jsonl");
        var outsideSnapshot = BuildSnapshot("..", "..", cash: 91_000m);
        var outsideJson = JsonSerializer.Serialize(outsideSnapshot, SnapshotJsonOptions);
        await File.WriteAllTextAsync(outsidePath, outsideJson + Environment.NewLine);
        var store = new JsonlPositionSnapshotStore(
            new StorageOptions { RootPath = containedRoot },
            NullLogger<JsonlPositionSnapshotStore>.Instance);

        var read = () => store.GetLatestSnapshotAsync("..", "..");
        var write = () => store.SaveSnapshotAsync(outsideSnapshot with { Cash = 1m });

        await read.Should().ThrowAsync<ArgumentException>();
        await write.Should().ThrowAsync<ArgumentException>();
        (await File.ReadAllTextAsync(outsidePath)).Should().Be(outsideJson + Environment.NewLine);
        Directory.Exists(containedRoot).Should().BeFalse();
    }

    [Fact]
    public async Task SaveOwnedSnapshot_InvalidTenantPathSegment_RejectsWithoutCreatingFiles()
    {
        var snapshot = BuildSnapshot("run-owned-contained", "acc-owned-contained", cash: 1_000m) with
        {
            TenantId = "..",
            CompanyId = "company-1",
            FundProfileId = "fund-1",
            LedgerBookId = Guid.NewGuid(),
            EntityId = "entity-1"
        };

        var act = () => _store.SaveSnapshotAsync(snapshot);

        await act.Should().ThrowAsync<ArgumentException>();
        Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task SaveSnapshot_ExistingDirectoryLink_RejectsWithoutWritingThroughLink()
    {
        var containedRoot = Path.Combine(_tempRoot, "link-root");
        var outsideRoot = Path.Combine(_tempRoot, "outside-root");
        Directory.CreateDirectory(containedRoot);
        Directory.CreateDirectory(outsideRoot);
        var portfoliosLink = Path.Combine(containedRoot, "portfolios");
        if (!TryCreateDirectoryLink(portfoliosLink, outsideRoot))
            return;

        try
        {
            var store = new JsonlPositionSnapshotStore(
                new StorageOptions { RootPath = containedRoot },
                NullLogger<JsonlPositionSnapshotStore>.Instance);

            var act = () => store.SaveSnapshotAsync(
                BuildSnapshot("run-linked", "acc-linked", cash: 1_000m));

            await act.Should().ThrowAsync<InvalidOperationException>();
            Directory.EnumerateFiles(outsideRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(portfoliosLink))
                Directory.Delete(portfoliosLink);
        }
    }

    [Fact]
    public async Task SaveSnapshot_ConfiguredRootLink_RejectsWithoutWritingThroughLink()
    {
        var outsideRoot = Path.Combine(_tempRoot, "outside-root-link-target");
        var linkedRoot = Path.Combine(_tempRoot, "linked-snapshot-root");
        Directory.CreateDirectory(outsideRoot);
        if (!TryCreateDirectoryLink(linkedRoot, outsideRoot))
            return;

        try
        {
            var store = new JsonlPositionSnapshotStore(
                new StorageOptions { RootPath = linkedRoot },
                NullLogger<JsonlPositionSnapshotStore>.Instance);

            var act = () => store.SaveSnapshotAsync(
                BuildSnapshot("run-root-linked", "acc-root-linked", cash: 1_000m));

            await act.Should().ThrowAsync<InvalidOperationException>();
            Directory.EnumerateFiles(outsideRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(linkedRoot))
                Directory.Delete(linkedRoot);
        }
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

    private JsonlPositionSnapshotStore CreateStore()
        => new(
            new StorageOptions { RootPath = _tempRoot },
            NullLogger<JsonlPositionSnapshotStore>.Instance);

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private string GetSnapshotPath(string runId, string accountId)
        => Path.Combine(_tempRoot, "portfolios", runId, accountId, "snapshots.jsonl");

    private async Task WriteSnapshotsDirectAsync(
        string runId,
        string accountId,
        IEnumerable<AccountSnapshotRecord> snapshots)
    {
        var path = GetSnapshotPath(runId, accountId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65_536,
            FileOptions.Asynchronous);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var snapshot in snapshots)
            await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot, SnapshotJsonOptions));
    }

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
