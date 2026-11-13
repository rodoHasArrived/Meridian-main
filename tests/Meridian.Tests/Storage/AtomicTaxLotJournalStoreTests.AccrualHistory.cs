using FluentAssertions;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task AccrualHistory_RestartRetainsSameDayDisposalAndFinalFullPaydownInterest()
    {
        await using var fixture = await AmortFixture.CreateAsync(premium: true, factorPool: true);
        var original = fixture.Lot;
        await fixture.Store.AppendAssetPostingAsync(await FactorPaydownCommandAsync(fixture, 0.90m, FirstPaydown));
        var disposal = await fixture.CurrentBasisDisposalAsync(1_900m, FirstPaydown, "accrual-same-day-sale");
        await fixture.Store.AppendAssetPostingAsync(disposal.Command);
        await fixture.Store.AppendAssetPostingAsync(await FactorPaydownCommandAsync(fixture, 0m, SecondPaydown));

        var restarted = fixture.Restart();
        var snapshots = await restarted.ListTaxLotAccrualSnapshotsAsync(fixture.BookId, original.SecurityId,
            original.BookPositionId, FirstPaydown.AddDays(-1), SecondPaydown.AddDays(1));

        snapshots.Single(snapshot => snapshot.Date == FirstPaydown.AddDays(-1)).AccrualFace.Should().Be(950_000m);
        var first = snapshots.Single(snapshot => snapshot.Date == FirstPaydown);
        first.AccrualFace.Should().Be(760_000m, "the sale reduces the holding but the paydown follows this day's interest");
        first.OpenShare.Should().Be(0.8m);
        first.AccrualFactor.Should().Be(0.95m);
        first.Lot.CurrentFactor.Should().Be(0.90m);
        snapshots.Single(snapshot => snapshot.Date == FirstPaydown.AddDays(1)).AccrualFace.Should().Be(720_000m);
        var last = snapshots.Single(snapshot => snapshot.Date == SecondPaydown);
        last.AccrualFace.Should().Be(720_000m);
        last.Lot.CurrentFactor.Should().Be(0m);
        snapshots.Should().NotContain(snapshot => snapshot.Date == SecondPaydown.AddDays(1));
        var closed = (await restarted.GetTaxLotsByIdsAsync(fixture.BookId, [original.TaxLotRecordId])).Single();
        closed.OpenQuantity.Should().Be(0m);
        closed.CurrentFactor.Should().Be(0m);
        (await restarted.ListTaxLotAccrualSnapshotsAsync(fixture.BookId, Guid.NewGuid(), original.BookPositionId,
            FirstPaydown, SecondPaydown)).Should().BeEmpty("a different security scope cannot see these lots");
        (await restarted.ListTaxLotAccrualSnapshotsAsync(fixture.BookId, original.SecurityId, Guid.NewGuid(),
            FirstPaydown, SecondPaydown)).Should().BeEmpty("a different book position cannot see these lots");
    }
}
