using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FixedIncome;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task RemainingFaceAmortization_AfterAtomicPaydown_PostsOnlySurvivingPremiumAndReplays()
    {
        await using var fixture = await AmortFixture.CreateAsync(true, factorPool: true,
            factorAmortization: BondAmortizationMethod.StraightLine);
        var paydown = await FactorPaydownCommandAsync(fixture, 0.90m, new DateOnly(2026, 1, 1));
        var paid = await fixture.Store.AppendAssetPostingAsync(paydown);
        var lot = paid.MutatedLots.Single();
        lot.ToOpenLot().OpenTransactionCostBasis.Should().Be(913_500m);
        var command = fixture.Command(expectedLot: lot.ToOpenLot(), asOf: new DateOnly(2026, 7, 1), remainingFace: true);
        var posted = await fixture.Store.AppendAssetPostingAsync(command);
        posted.MutatedLots.Single().CurrentFactor.Should().Be(0.90m);
        posted.MutatedLots.Single().OpenQuantity.Should().Be(9_500m);
        // 75% of a two-year life: acquisition premium 14,250 x 25% x surviving 18/19.
        posted.MutatedLots.Single().ToOpenLot().OpenTransactionCostBasis.Should().Be(903_375m);
        posted.Journal.Entry.Lines.Single(line => line.Account == AmortAccount).Credit.Should().Be(10_125m);
        (await fixture.Restart().AppendAssetPostingAsync(command)).IsExactReplay.Should().BeTrue();
        var changed = command.Amortization! with { AmortizeRemainingFace = false };
        var retry = () => fixture.Restart().AppendAssetPostingAsync((command with
        {
            Amortization = changed,
            Journal = command.Journal with { PostingCommand = command.Journal.PostingCommand! with { LotAmortization = changed } }
        }).WithComputedFingerprint());
        await retry.Should().ThrowAsync<LedgerValidationException>();
        (await fixture.Restart().GetAtomicTaxLotPostingAsync(command.MutationBatchId))!.MutatedLots.Single()
            .ToOpenLot().OpenTransactionCostBasis.Should().Be(903_375m);
    }
}
