using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed class LedgerBalanceSnapshotExtensionsTests
{
    [Fact]
    public void Composition_ResolvesNativeBalanceSourceFromTheSameJournalStoreSingleton()
    {
        var services = new ServiceCollection();
        services.AddLedgerJournalStore("Host=localhost;Database=meridian_synthetic;Username=fixture");
        using var provider = services.BuildServiceProvider();

        var journals = provider.GetRequiredService<ILedgerJournalStore>();
        var balances = provider.GetRequiredService<ILedgerBalanceSnapshotSource>();

        balances.Should().BeOfType<PostgresLedgerJournalStore>().Which.Should().BeSameAs(journals);
    }

    [Fact]
    public async Task CompatibilityReplay_PreservesKnownZeroAndMatchesAccountAndDimensionsIgnoringCase()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var book = Guid.NewGuid();
        var period = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        var asOf = at.AddMinutes(30);
        var records = new[]
        {
            Record(book, period, at, 1, 100m, "broker-a", "Entity-A"),
            Record(book, period, at, 2, -100m, "broker-a", "Entity-A"),
            Record(book, period, at, 3, 70m, "broker-a", "Entity-B"),
            Record(book, period, at, 4, 50m, "broker-b", "Entity-A"),
            Record(book, period, asOf.AddMinutes(1), 5, 1_000m, "broker-a", "Entity-A")
        };
        LedgerJournalEntryQuery? query = null;
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                query = call.ArgAt<LedgerJournalEntryQuery>(0);
                return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(records);
            });

        var balances = await store.ReadTrialBalanceAsOfAsync(book, asOf, "BROKER-A",
            new LedgerLineDimensionSet(EntityId: "ENTITY-A"));

        query.Should().NotBeNull();
        query!.LedgerBookId.Should().Be(book);
        query.OccurredTo.Should().Be(asOf);
        query.LineDimensions.Should().BeNull(
            "a case-sensitive SQL containment prefilter would discard case-insensitive accounting matches");
        balances.Should().HaveCount(2);
        balances.Should().ContainKey(Cash("broker-a")).WhoseValue.Should().Be(0m);
        balances.Should().ContainKey(Capital("broker-a")).WhoseValue.Should().Be(0m);
    }

    [Fact]
    public async Task CompatibilityReplay_UnknownAccountIsMissingRatherThanCertifiedZero()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var book = Guid.NewGuid();
        var at = DateTimeOffset.Parse("2026-05-15T12:00:00Z");
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                [Record(book, Guid.NewGuid(), at, 1, 100m, "broker-a", "Entity-A")]));

        var balances = await store.ReadTrialBalanceAsOfAsync(book, at, "missing-owner");

        balances.Should().BeEmpty();
    }

    private static LedgerAccount Cash(string owner) => new("Cash", LedgerAccountType.Asset, FinancialAccountId: owner);
    private static LedgerAccount Capital(string owner) => new("Capital Account", LedgerAccountType.Equity, FinancialAccountId: owner);

    private static LedgerJournalEntryRecord Record(Guid book, Guid period, DateTimeOffset at, int sequence,
        decimal amount, string owner, string entity)
    {
        var id = Guid.NewGuid();
        const string description = "Synthetic checkpoint fallback";
        var dimensions = new LedgerLineDimensionSet(EntityId: entity);
        var entry = new JournalEntry(id, at, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, at, Cash(owner), Math.Max(amount, 0m), Math.Max(-amount, 0m), description, dimensions),
            new LedgerEntry(Guid.NewGuid(), id, at, Capital(owner), Math.Max(-amount, 0m), Math.Max(amount, 0m), description, dimensions)
        ]);
        return new LedgerJournalEntryRecord(entry, book, period, null, null, sequence, at);
    }
}
