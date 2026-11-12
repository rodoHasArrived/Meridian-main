using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;

namespace Meridian.Tests.Application;

public sealed class BondObservedFixingStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-observed-fixings", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset At = new(2026, 2, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Retention_IsImmutableScopedAndRetryKeepsOriginalEvidenceAndHash()
    {
        var store = Store();
        var candidate = Candidate();
        var first = await store.RetainAsync(candidate);
        var retry = await Store(At.AddHours(1)).RetainAsync(candidate with { Observation = candidate.Observation with { RetrievedAt = At.AddMinutes(1) } });
        retry.Should().BeEquivalentTo(first);
        (await store.ListAsync(candidate.SecurityId)).Should().ContainSingle();
        (await store.GetAsync(Guid.NewGuid(), first.ReceiptId)).Should().BeNull();
        var second = await store.RetainAsync(candidate with { RateFraction = 0.04m, Observation = candidate.Observation with { Value = 4m, OriginalValue = "4" } });
        second.ReceiptId.Should().NotBe(first.ReceiptId);
        (await store.GetAsync(candidate.SecurityId, first.ReceiptId)).Should().BeEquivalentTo(first,
            "a new source value cannot overwrite an earlier explicitly selectable receipt");
    }

    [Fact]
    public async Task Retention_RefusesOlderObservationAndInconsistentUnits()
    {
        var candidate = Candidate();
        Func<Task> oldDate = () => Store().RetainAsync(candidate with { Observation = candidate.Observation with { ObservationDate = candidate.ResetDate.AddDays(-1) } });
        await oldDate.Should().ThrowAsync<ArgumentException>();
        Func<Task> wrongUnit = () => Store().RetainAsync(candidate with { RateFraction = 3m });
        await wrongUnit.Should().ThrowAsync<ArgumentException>();
        Func<Task> forecast = () => Store().RetainAsync(candidate with { Observation = candidate.Observation with { Kind = SecurityRateObservationKind.ForecastRate } });
        await forecast.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CorruptReceipt_FailsClosed()
    {
        var candidate = Candidate();
        var receipt = await Store().RetainAsync(candidate);
        var path = Path.Combine(_root, "bonds", "observed-fixings", candidate.SecurityId.ToString("N"), receipt.ReceiptId.ToString("N") + ".json");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("Exact-date fixing reviewed", "Changed outside retention"));
        Func<Task> read = () => Store().GetAsync(candidate.SecurityId, receipt.ReceiptId);
        await read.Should().ThrowAsync<InvalidDataException>();
    }

    private FileBondObservedFixingStore Store(DateTimeOffset? now = null) => new(new StorageOptions { RootPath = _root }, new FixedClock(now ?? At));
    private static BondObservedFixingCandidate Candidate() => new(Guid.NewGuid(), 7, "SOFR", new DateOnly(2026, 1, 15), 0.03m,
        new("fred", "SOFR", 3m, "3", SecurityRateUnit.Percent, "Percent", new DateOnly(2026, 1, 15), null,
            new DateOnly(2026, 1, 16), new DateOnly(9999, 12, 31), At.AddDays(-1), "lin", SecurityRateObservationKind.ObservedFixing, "USD"),
        "operator", "Exact-date fixing reviewed");
    private sealed class FixedClock(DateTimeOffset at) : TimeProvider { public override DateTimeOffset GetUtcNow() => at; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
