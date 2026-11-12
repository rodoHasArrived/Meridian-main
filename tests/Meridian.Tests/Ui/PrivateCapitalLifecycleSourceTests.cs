using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class PrivateCapitalLifecycleSourceTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 31);
    private static readonly DateTimeOffset KnownAt = DateTimeOffset.Parse("2026-11-01T12:00:00Z");
    private static readonly HashSet<string> Entities = ["entity-a"];

    [Fact]
    public void Project_SelectsEligibleRetainedRevisionAndPreservesIndependentBalances()
    {
        var first = Snapshot("snapshot-1", 1, 80m, new DateOnly(2026, 9, 30));
        var second = Snapshot("snapshot-2", 2, 90m, AsOf);
        var future = Snapshot("snapshot-3", 3, 120m, new DateOnly(2026, 11, 30)) with
        { RecordedAtUtc = DateTimeOffset.Parse("2026-12-01T00:00:00Z") };
        var read = FilePrivateCapitalLifecycleSource.Project(new(1, [first, second, future], []), "family-a", Entities, AsOf, KnownAt);
        var row = Assert.Single(read.Commitments);
        Assert.Equal("snapshot-2", row.SnapshotId);
        Assert.Equal(90m, row.CurrentNav);
        Assert.Equal(35m, row.UnfundedAmount); // manager supplied; not commitment - called (20).
        Assert.Equal(15m, row.DistributedAmount);
        Assert.True(row.BalancesAvailable);
        Assert.Equal(2, row.SourceRevision);
        Assert.Equal(64, row.SourceContentSha256?.Length);
        Assert.Empty(FilePrivateCapitalLifecycleSource.Project(new(1, [first], []), "other-family", Entities, AsOf, KnownAt).Commitments);
    }

    [Fact]
    public void Project_RefusesDuplicateAndMismatchedIdentityWithoutInventingZero()
    {
        var one = Snapshot("snapshot-1", 1, 0m, AsOf);
        var duplicate = FilePrivateCapitalLifecycleSource.Project(new(1, [one, one], []), "family-a", Entities, AsOf, KnownAt);
        Assert.Empty(duplicate.Commitments);
        Assert.Contains(duplicate.Warnings, value => value.Contains("duplicate"));
        var mismatch = one with { Commitment = one.Commitment with { EntityId = "entity-b" } };
        Assert.Empty(FilePrivateCapitalLifecycleSource.Project(new(1, [mismatch], []), "family-a", Entities, AsOf, KnownAt).Commitments);
        var realZero = FilePrivateCapitalLifecycleSource.Project(new(1, [one], []), "family-a", Entities, AsOf, KnownAt);
        Assert.Equal(0m, Assert.Single(realZero.Commitments).CurrentNav);
        Assert.True(Assert.Single(realZero.Commitments).BalancesAvailable);
        var wrongCurrency = one with { SnapshotId = "snapshot-other", Revision = 2, Commitment = one.Commitment with { Currency = "EUR" } };
        Assert.Empty(FilePrivateCapitalLifecycleSource.Project(new(1, [one, wrongCurrency], []), "family-a", Entities, AsOf, KnownAt).Commitments);
        var unavailable = one with { Commitment = one.Commitment with { BalancesAvailable = false } };
        Assert.Empty(FilePrivateCapitalLifecycleSource.Project(new(1, [unavailable], []), "family-a", Entities, AsOf, KnownAt).Commitments);
        var negativeNav = one with { Commitment = one.Commitment with { CurrentNav = -25m } };
        Assert.Equal(-25m, Assert.Single(FilePrivateCapitalLifecycleSource.Project(new(1, [negativeNav], []), "family-a", Entities, AsOf, KnownAt).Commitments).CurrentNav);
    }

    [Fact]
    public void Project_KeepsProvisionalActivityButRefusesCurrencyMismatch()
    {
        var snapshot = Snapshot("snapshot-1", 1, 90m, AsOf);
        var activity = new CapitalActivityDto("call-1", "commitment-a", "entity-a", "CapitalCall", "USD", 15m,
            AsOf, null, null, "Provisional", "manager-notice", "notice-a", AsOf, null, "Partial", "NotReconciled", null, null);
        var sourceActivity = new PrivateCapitalActivitySourceDto("family-a", KnownAt, activity);
        var read = FilePrivateCapitalLifecycleSource.Project(new(1, [snapshot], [sourceActivity]), "family-a", Entities, AsOf, KnownAt);
        Assert.Equal("Provisional", Assert.Single(read.Activities).Status);
        var mismatch = sourceActivity with { Activity = activity with { Currency = "EUR" } };
        var refused = FilePrivateCapitalLifecycleSource.Project(new(1, [snapshot], [mismatch]), "family-a", Entities, AsOf, KnownAt);
        Assert.Empty(refused.Commitments);
        Assert.Contains(refused.Warnings, value => value.Contains("currency"));
    }

    [Fact]
    public async Task ReadAsync_MissingOrMalformedSourceIsExplicitlyUnavailable()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-private-capital-{Guid.NewGuid():N}");
        try
        {
            var source = new FilePrivateCapitalLifecycleSource(directory);
            var missing = await source.ReadAsync("family-a", Entities, AsOf, KnownAt);
            Assert.Empty(missing.Commitments);
            Assert.Contains(missing.Warnings, value => value.Contains("unavailable"));
            Directory.CreateDirectory(Path.Combine(directory, "family-office"));
            await File.WriteAllTextAsync(Path.Combine(directory, "family-office", "private-capital-lifecycle.json"), "{invalid");
            var malformed = await source.ReadAsync("family-a", Entities, AsOf, KnownAt);
            Assert.Empty(malformed.Commitments);
            Assert.Contains(malformed.Warnings, value => value.Contains("validated"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ReadAsync_RequiresEveryAmountAndReadsExactRetainedValues()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-private-capital-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "family-office"));
            var file = Path.Combine(directory, "family-office", "private-capital-lifecycle.json");
            var document = new PrivateCapitalLifecycleDocumentDto(1, [Snapshot("snapshot-1", 1, 123456789012345.6789m, AsOf)], []);
            var json = System.Text.Json.JsonSerializer.Serialize(document,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            await File.WriteAllTextAsync(file, json);
            var source = new FilePrivateCapitalLifecycleSource(directory);
            var read = await source.ReadAsync("family-a", Entities, AsOf, KnownAt);
            Assert.Equal("123456789012345.6789", Assert.Single(read.Commitments).ExactBalances?.CurrentNav);
            await File.WriteAllTextAsync(file, json.Replace("\"calledAmount\":80,", "", StringComparison.Ordinal));
            var incomplete = await source.ReadAsync("family-a", Entities, AsOf, KnownAt);
            Assert.Empty(incomplete.Commitments);
            Assert.Contains(incomplete.Warnings, value => value.Contains("validated"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static PrivateCapitalSnapshotDto Snapshot(string id, int revision, decimal nav, DateOnly asOf) => new(
        "family-a", id, revision, KnownAt,
        new("commitment-a", "entity-a", "Growth Fund", "Growth", "USD", 100m, 80m, 35m, 15m, nav, 2024,
            "manager-statement", "document-a", asOf, asOf, "Complete", "NotReconciled", null, null));
}
