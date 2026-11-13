using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

public sealed class BrokerActivityImportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-activity-tests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Time = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Restart_RereadsSameTimestampAndRetainsEachProviderIdOnce()
    {
        var first = new Source(_ => Snapshot(Cash("a", 10m)));
        await new BrokerActivityImportService(_root).ImportAsync(first, "account");
        DateTimeOffset? resumed = null;
        var second = new Source(since =>
        {
            resumed = since;
            return Snapshot(Cash("a", 10m), Cash("b", 20m));
        });

        var accepted = await new BrokerActivityImportService(_root).ImportAsync(second, "account");

        resumed.Should().Be(Time.AddTicks(-1));
        accepted.CashTransactions.Should().HaveCount(2);
        accepted.CashTransactions.Select(row => row.TransactionId).Should().BeEquivalentTo("a", "b");
        var replay = await new BrokerActivityImportService(_root).ImportAsync(second, "account");
        replay.CashTransactions.Should().HaveCount(2);
    }

    [Fact]
    public async Task CurrentSnapshot_RemainsDistinctFromVerifiedRetainedHistoryAcrossRestartAndEmptyFetch()
    {
        await new BrokerActivityImportService(_root).ImportAsync(new Source(_ => Snapshot(Cash("a", 10m))), "account");
        var current = Snapshot(Cash("b", 20m)) with { RetrievedAt = Time.AddDays(2) };

        var imported = await new BrokerActivityImportService(_root)
            .ImportWithHistoryAsync(new Source(_ => current), "account");

        imported.Current.Should().BeSameAs(current);
        imported.Current.CashTransactions.Should().ContainSingle(row => row.TransactionId == "b");
        imported.Retained.CashTransactions.Select(row => row.TransactionId).Should().BeEquivalentTo("a", "b");
        imported.Retained.RetrievedAt.Should().Be(current.RetrievedAt);

        var empty = await new BrokerActivityImportService(_root)
            .ImportWithHistoryAsync(new Source(_ => Snapshot()), "account");
        empty.Current.CashTransactions.Should().BeEmpty();
        empty.Retained.CashTransactions.Select(row => row.TransactionId).Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public async Task CrashAfterActivityAcceptance_BeforeCheckpoint_RetriesWithoutDuplicateEvidence()
    {
        var failCheckpointOnce = true;
        var source = new Source(_ =>
        {
            if (failCheckpointOnce)
            {
                failCheckpointOnce = false;
                Directory.CreateDirectory(Path.Combine(Directory.GetDirectories(_root).Single(), "checkpoint.json"));
            }
            return Snapshot(Cash("accepted-before-crash", 10m));
        });
        await Assert.ThrowsAnyAsync<IOException>(() => new BrokerActivityImportService(_root).ImportAsync(source, "account"));
        var accountDirectory = Directory.GetDirectories(_root).Single();
        Directory.GetFiles(accountDirectory, "*.json").Should().ContainSingle();
        Directory.Delete(Path.Combine(accountDirectory, "checkpoint.json"));

        var replay = await new BrokerActivityImportService(_root).ImportAsync(source, "account");

        replay.CashTransactions.Should().ContainSingle(row => row.TransactionId == "accepted-before-crash");
        Directory.GetFiles(accountDirectory, "*.json").Should().HaveCount(2);
    }

    [Fact]
    public async Task SameIdChangedEconomics_FailsWithoutAdvancingCheckpoint()
    {
        await new BrokerActivityImportService(_root).ImportAsync(new Source(_ => Snapshot(Cash("a", 10m))), "account");
        var checkpoint = Path.Combine(Directory.GetDirectories(_root).Single(), "checkpoint.json");
        var before = await File.ReadAllTextAsync(checkpoint);

        await Assert.ThrowsAsync<InvalidDataException>(() => new BrokerActivityImportService(_root)
            .ImportAsync(new Source(_ => Snapshot(Cash("a", 11m))), "account"));

        (await File.ReadAllTextAsync(checkpoint)).Should().Be(before);
    }

    [Fact]
    public async Task IncompleteFetch_RetainsEvidenceWithoutMovingResumeWatermark()
    {
        await new BrokerActivityImportService(_root).ImportAsync(new Source(_ => Snapshot(Cash("a", 10m))), "account");
        var partial = Snapshot(Cash("b", 20m) with { PostedAt = Time.AddHours(1) });
        partial = partial with { Cursor = partial.Cursor! with { IsComplete = false } };
        await new BrokerActivityImportService(_root).ImportAsync(new Source(_ => partial), "account");
        DateTimeOffset? resumed = null;
        var final = await new BrokerActivityImportService(_root).ImportAsync(new Source(since =>
        {
            resumed = since;
            return Snapshot(Cash("b", 20m) with { PostedAt = Time.AddHours(1) });
        }), "account");

        resumed.Should().Be(Time.AddTicks(-1));
        final.CashTransactions.Should().HaveCount(2);
    }

    [Fact]
    public async Task ResponseAccountMismatch_RefusesBeforeAnyActivityAcceptance()
    {
        var wrong = Snapshot(Cash("a", 10m)) with { AccountId = "other" };
        await Assert.ThrowsAsync<InvalidDataException>(() => new BrokerActivityImportService(_root)
            .ImportAsync(new Source(_ => wrong), "account"));
        Directory.GetFiles(Directory.GetDirectories(_root).Single(), "*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task AcceptedEvidenceTampering_FailsClosedOnRestart()
    {
        var source = new Source(_ => Snapshot(Cash("a", 10m)));
        await new BrokerActivityImportService(_root).ImportAsync(source, "account");
        var record = Directory.GetFiles(Directory.GetDirectories(_root).Single(), "*.json")
            .Single(path => Path.GetFileName(path) != "checkpoint.json");
        var json = await File.ReadAllTextAsync(record);
        await File.WriteAllTextAsync(record, json.Replace("10", "11", StringComparison.Ordinal));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BrokerActivityImportService(_root).ImportAsync(source, "account"));
    }

    [Fact]
    public async Task EmptyCompleteFetch_PreservesPreviousResumeWatermark()
    {
        await new BrokerActivityImportService(_root).ImportAsync(new Source(_ => Snapshot(Cash("a", 10m))), "account");
        await new BrokerActivityImportService(_root).ImportAsync(new Source(_ => Snapshot()), "account");
        DateTimeOffset? resumed = null;
        var snapshot = await new BrokerActivityImportService(_root).ImportAsync(new Source(since =>
        {
            resumed = since;
            return Snapshot();
        }), "account");
        resumed.Should().Be(Time.AddTicks(-1));
        snapshot.CashTransactions.Should().ContainSingle();
    }

    [Fact]
    public async Task UnsupportedFutureWatermark_RefusesWithoutCheckpoint()
    {
        var impossible = Snapshot(Cash("a", 10m));
        impossible = impossible with { Cursor = impossible.Cursor! with { HighWatermark = Time.AddDays(10) } };
        await Assert.ThrowsAsync<InvalidDataException>(() => new BrokerActivityImportService(_root)
            .ImportAsync(new Source(_ => impossible), "account"));
        Directory.GetFiles(Directory.GetDirectories(_root).Single(), "*.json").Should().BeEmpty();
    }

    [Fact]
    public async Task CheckpointMissingRequiredIndex_FailsClosedBeforeProviderFetch()
    {
        await new BrokerActivityImportService(_root).ImportAsync(new Source(_ => Snapshot(Cash("a", 10m))), "account");
        var path = Path.Combine(Directory.GetDirectories(_root).Single(), "checkpoint.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        node.Remove("activityHashes");
        await File.WriteAllTextAsync(path, node.ToJsonString());
        var called = false;

        await Assert.ThrowsAsync<InvalidDataException>(() => new BrokerActivityImportService(_root).ImportAsync(new Source(_ =>
        {
            called = true;
            return Snapshot();
        }), "account"));

        called.Should().BeFalse();
    }

    private static BrokerageCashTransactionDto Cash(string id, decimal amount) => new(id, "DIV", amount, "USD", Time, "XYZ");
    private static BrokerageActivitySnapshotDto Snapshot(params BrokerageCashTransactionDto[] cash) => new(
        "synthetic", "account", Time.AddDays(1), [], [], cash,
        Cursor: new(cash.LastOrDefault()?.TransactionId, cash.Length == 0 ? null : cash.Max(row => row.PostedAt), 1, cash.Length, true));

    private sealed class Source(Func<DateTimeOffset?, BrokerageActivitySnapshotDto> fetch) : IBrokerageActivitySync
    {
        public string ProviderId => "synthetic";
        public Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(string externalAccountId, DateTimeOffset? since = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(fetch(since));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
