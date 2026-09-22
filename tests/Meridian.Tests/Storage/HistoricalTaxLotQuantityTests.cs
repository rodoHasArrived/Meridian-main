using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class HistoricalTaxLotQuantityTests
{
    [Theory]
    [InlineData(40)]
    [InlineData(100)]
    public void Project_RetainsEntitlementBeforePartialOrFullLaterDisposal(int sold)
    {
        var (lot, history) = Scenario(sold);
        var projected = HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 15));
        projected.OpenQuantity.Should().Be(100m);
        projected.Version.Should().Be(lot.Version);
        projected.OriginalFace.Should().Be(10_000m);
        HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 20)).OpenQuantity.Should().Be(100m - sold);
        HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 11)).OpenQuantity.Should().Be(0m);
    }

    [Fact]
    public void Project_UsesEconomicDateWhenBackdatedDisposalWasRecordedAfterLaterDisposal()
    {
        var (lot, history) = Scenario(40);
        var third = history[1] with { MutationBatchId = Guid.NewGuid(), Before = 60m, Delta = -10m,
            After = 50m, ExpectedVersion = 2, ResultVersion = 3, EffectiveDate = new(2026, 5, 14) };
        lot = lot with { OpenQuantity = 50m, Version = 3, LastMutationBatchId = third.MutationBatchId };
        HistoricalTaxLotQuantity.Project(lot, [.. history, third], new(2026, 5, 15)).OpenQuantity.Should().Be(90m);
    }

    [Theory]
    [InlineData("missing-acquisition")]
    [InlineData("missing-disposal")]
    [InlineData("wrong-scope")]
    [InlineData("broken-quantity-chain")]
    [InlineData("duplicate-version")]
    [InlineData("wrong-last-batch")]
    [InlineData("unknown-mutation")]
    [InlineData("disposal-before-acquisition")]
    public void Project_RefusesIncompleteOrInconsistentRetainedHistory(string defect)
    {
        var (lot, history) = Scenario(40);
        switch (defect)
        {
            case "missing-acquisition": history = [history[1]]; break;
            case "missing-disposal": history = [history[0]]; break;
            case "wrong-scope": history[1] = history[1] with { BookPositionId = Guid.NewGuid() }; break;
            case "broken-quantity-chain": history[1] = history[1] with { Before = 99m, After = 59m }; break;
            case "duplicate-version": history = [.. history, history[1]]; break;
            case "wrong-last-batch": lot = lot with { LastMutationBatchId = Guid.NewGuid() }; break;
            case "unknown-mutation": history[1] = history[1] with { Kind = (AtomicTaxLotMutationKind)99 }; break;
            case "disposal-before-acquisition": history[1] = history[1] with { EffectiveDate = new(2026, 5, 10) }; break;
        }
        var act = () => HistoricalTaxLotQuantity.Project(lot, history, new(2026, 5, 15));
        act.Should().Throw<LedgerValidationException>().WithMessage("*complete retained mutation evidence*");
    }

    [Fact]
    public void Project_RefusesLegacyReliefWithoutHistoryButAcceptsPristineAcquisition()
    {
        var (lot, _) = Scenario(40);
        lot = lot with { OriginatingMutationBatchId = null, LastMutationBatchId = null, Version = 1 };
        var act = () => HistoricalTaxLotQuantity.Project(lot, [], new(2026, 5, 15));
        act.Should().Throw<LedgerValidationException>();
        HistoricalTaxLotQuantity.Project(lot with { OpenQuantity = 100m }, [], new(2026, 5, 15))
            .OpenQuantity.Should().Be(100m);
    }

    private static (LedgerTaxLotRecord Lot, DatedTaxLotQuantityMutation[] History) Scenario(decimal sold)
    {
        var acquired = Guid.NewGuid();
        var disposed = Guid.NewGuid();
        var lot = new LedgerTaxLotRecord(Guid.NewGuid(), Guid.NewGuid(), LedgerAccounts.Securities("POOL"),
            "pool-lot", new(2026, 5, 12), 100m, 100m - sold, 100m, "USD", DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, Version: 2, OriginatingMutationBatchId: acquired, LastMutationBatchId: disposed,
            SecurityId: Guid.NewGuid(), BookPositionId: Guid.NewGuid(), OriginalFace: 10_000m, BookedFactor: 0.8m, ParBasis: 100m);
        return (lot,
        [
            new(lot.TaxLotRecordId, acquired, AtomicTaxLotMutationKind.Acquisition, 0m, 100m, 100m,
                0, 1, lot.AcquiredDate, lot.SecurityId, lot.BookPositionId),
            new(lot.TaxLotRecordId, disposed, AtomicTaxLotMutationKind.Disposal, 100m, -sold, 100m - sold,
                1, 2, new(2026, 5, 20), lot.SecurityId, lot.BookPositionId)
        ]);
    }
}
