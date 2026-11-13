using FluentAssertions;
using Meridian.Ledger;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed class HistoricalTaxLotAccrualTests
{
    [Fact]
    public void Project_PreservesDisposalsAndAccruesBeforeEachPaydownIncludingTheFinalDay()
    {
        var (lot, history) = Scenario();
        var snapshots = HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 14), new(2026, 6, 1));

        snapshots.Single(snapshot => snapshot.Date == new DateOnly(2026, 5, 14)).AccrualFace.Should().Be(1_000m);
        snapshots.Single(snapshot => snapshot.Date == new DateOnly(2026, 5, 15)).AccrualFace.Should().Be(600m);
        var paydown = snapshots.Single(snapshot => snapshot.Date == new DateOnly(2026, 5, 20));
        paydown.OpenShare.Should().Be(0.6m);
        paydown.AccrualFactor.Should().Be(0.8m);
        paydown.Lot.CurrentFactor.Should().Be(0.6m);
        paydown.AccrualFace.Should().Be(600m);
        snapshots.Single(snapshot => snapshot.Date == new DateOnly(2026, 5, 21)).AccrualFace.Should().Be(450m);
        var finalDay = snapshots.Single(snapshot => snapshot.Date == new DateOnly(2026, 5, 31));
        finalDay.AccrualFace.Should().Be(450m);
        finalDay.Lot.CurrentFactor.Should().Be(0m);
        snapshots.Should().NotContain(snapshot => snapshot.Date == new DateOnly(2026, 6, 1));
        lot.OpenQuantity.Should().Be(0m, "the read does not restore the durable closed lot");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_SameDayDisposalIsAppliedButFactorPaydownFollowsInterest(bool disposalFirst)
    {
        var (lot, history) = Scenario();
        var sale = history[1] with { EffectiveDate = new(2026, 5, 20) };
        var paydown = history[2];
        if (!disposalFirst)
        {
            paydown = paydown with { Before = 10m, After = 10m, ExpectedVersion = 1, ResultVersion = 2 };
            sale = sale with { ExpectedVersion = 2, ResultVersion = 3, CurrentFactorAfter = 0.6m };
        }
        history = disposalFirst ? [history[0], sale, paydown, history[3]] : [history[0], paydown, sale, history[3]];

        var snapshot = HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 20), new(2026, 5, 20)).Single();

        snapshot.OpenShare.Should().Be(0.6m);
        snapshot.AccrualFactor.Should().Be(0.8m);
        snapshot.AccrualFace.Should().Be(600m);
        snapshot.Lot.CurrentFactor.Should().Be(0.6m);
    }

    [Fact]
    public void Project_SameDayDisposalBeforeFullPaydownDoesNotRestoreDisposedFace()
    {
        var (lot, history) = Scenario();
        history[1] = history[1] with { EffectiveDate = new(2026, 5, 31) };
        history[2] = history[2] with { EffectiveDate = new(2026, 5, 31) };

        var snapshot = HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 31), new(2026, 5, 31)).Single();

        snapshot.OpenShare.Should().Be(0.6m);
        snapshot.AccrualFactor.Should().Be(0.8m);
        snapshot.AccrualFace.Should().Be(600m);
        snapshot.Lot.CurrentFactor.Should().Be(0m);
    }

    [Fact]
    public void Project_EquivalentSourceQuoteBasesRetainTheSameFaceAndAcquisitionEconomics()
    {
        var (lot, history) = Scenario();
        var per100 = HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 21), new(2026, 5, 21)).Single();
        var per1 = HistoricalTaxLotAccrual.Project(lot with { ParBasis = 1m }, history,
            new(2026, 5, 21), new(2026, 5, 21)).Single();

        per1.Lot.ParBasis.Should().Be(1m);
        per1.Lot.PricePercentOfPar.Should().Be(1.10m);
        per100.Lot.PricePercentOfPar.Should().Be(110m);
        per1.AccrualFace.Should().Be(per100.AccrualFace);
        per1.Lot.CostBasis.Should().Be(per100.Lot.CostBasis);
        per1.Lot.PremiumDiscount.Should().Be(per100.Lot.PremiumDiscount);
    }

    [Theory]
    [InlineData("missing-face")]
    [InlineData("missing-history")]
    [InlineData("unexplained-factor")]
    public void Project_RefusesMissingTermsOrHistoryRatherThanReturningAPartialEntitlement(string defect)
    {
        var (lot, history) = Scenario();
        if (defect == "missing-face")
            lot = lot with { OriginalFace = null, BookedFactor = null, ParBasis = null };
        else if (defect == "missing-history")
            history = [history[0], history[2], history[3]];
        else
            lot = lot with { CurrentFactor = 0.1m };

        var act = () => HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 14), new(2026, 5, 31));

        act.Should().Throw<LedgerValidationException>();
    }

    [Fact]
    public void Project_AFullyDisposedLotEarnsNothingOnOrAfterItsDisposalDate()
    {
        var (lot, history) = Scenario();
        history = [history[0], history[1] with { Delta = -10m, After = 0m }];
        lot = lot with { CurrentFactor = null, Version = 2, LastMutationBatchId = history[1].MutationBatchId };

        HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 15), new(2026, 5, 31)).Should().BeEmpty();
        HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 14), new(2026, 5, 14))
            .Should().ContainSingle().Which.AccrualFace.Should().Be(1_000m);
    }

    [Theory]
    [InlineData(AtomicTaxLotMutationKind.CapitalReturn)]
    [InlineData(AtomicTaxLotMutationKind.BasisRedistribution)]
    [InlineData(AtomicTaxLotMutationKind.Amortization)]
    public void Project_BasisRestatementIsUnavailableEvenWhenLaterReliefMasksItsReason(AtomicTaxLotMutationKind kind)
    {
        var (original, source) = Scenario();
        var restatementDate = new DateOnly(2026, 5, 20);
        var restatement = source[0] with
        {
            MutationBatchId = Guid.NewGuid(),
            Kind = kind,
            Before = 10m,
            Delta = 0m,
            After = 10m,
            ExpectedVersion = 1,
            ResultVersion = 2,
            EffectiveDate = restatementDate
        };
        var disposal = source[1] with { EffectiveDate = restatementDate.AddDays(1), ExpectedVersion = 2, ResultVersion = 3 };
        var lot = original with
        {
            OpenQuantity = 6m,
            CurrentFactor = null,
            Version = 3,
            LastMutationBatchId = disposal.MutationBatchId,
            BasisAdjustment = new(disposal.MutationBatchId, OpenLotBasisAdjustmentReasons.DisposalRelief, 6m, 600m, 600m)
        };
        DatedTaxLotQuantityMutation[] history = [source[0], restatement, disposal];

        // A later restatement does not rewrite the purchase quote earned before its effective date.
        HistoricalTaxLotAccrual.Project(lot, history, restatementDate.AddDays(-1), restatementDate.AddDays(-1))
            .Should().ContainSingle().Which.Lot.PricePercentOfPar.Should().Be(110m);
        var nextWindow = () => HistoricalTaxLotAccrual.Project(lot, history, restatementDate, restatementDate.AddDays(1));
        nextWindow.Should().Throw<LedgerValidationException>().WithMessage("*historical basis restatement*");
    }

    [Fact]
    public void Project_MissingZeroQuantityMutationCannotBeHiddenByAVersionGap()
    {
        var (lot, history) = Scenario();
        history[1] = history[1] with { ExpectedVersion = 2, ResultVersion = 3 };
        history[2] = history[2] with { ExpectedVersion = 3, ResultVersion = 4 };
        history[3] = history[3] with { ExpectedVersion = 4, ResultVersion = 5 };
        lot = lot with { Version = 5 };

        var act = () => HistoricalTaxLotAccrual.Project(lot, history, new(2026, 5, 14), new(2026, 5, 31));
        act.Should().Throw<LedgerValidationException>().WithMessage("*complete consecutive*history*");
    }

    private static (LedgerTaxLotRecord Lot, DatedTaxLotQuantityMutation[] History) Scenario()
    {
        var acquired = Guid.NewGuid();
        var sold = Guid.NewGuid();
        var partial = Guid.NewGuid();
        var full = Guid.NewGuid();
        var lot = new LedgerTaxLotRecord(Guid.NewGuid(), Guid.NewGuid(), LedgerAccounts.Securities("POOL"),
            "pool-lot", new(2026, 5, 1), 10m, 0m, 110m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            Version: 4, OriginatingMutationBatchId: acquired, LastMutationBatchId: full,
            SecurityId: Guid.NewGuid(), BookPositionId: Guid.NewGuid(), OriginalFace: 1_000m, BookedFactor: 0.8m,
            ParBasis: 100m, CurrentFactor: 0m);
        return (lot,
        [
            new(lot.TaxLotRecordId, acquired, AtomicTaxLotMutationKind.Acquisition, 0m, 10m, 10m,
                0, 1, lot.AcquiredDate, lot.SecurityId, lot.BookPositionId),
            new(lot.TaxLotRecordId, sold, AtomicTaxLotMutationKind.Disposal, 10m, -4m, 6m,
                1, 2, new(2026, 5, 15), lot.SecurityId, lot.BookPositionId),
            new(lot.TaxLotRecordId, partial, AtomicTaxLotMutationKind.FactorPaydown, 6m, 0m, 6m,
                2, 3, new(2026, 5, 20), lot.SecurityId, lot.BookPositionId, 0.6m),
            new(lot.TaxLotRecordId, full, AtomicTaxLotMutationKind.FactorPaydown, 6m, -6m, 0m,
                3, 4, new(2026, 5, 31), lot.SecurityId, lot.BookPositionId, 0m)
        ]);
    }
}
