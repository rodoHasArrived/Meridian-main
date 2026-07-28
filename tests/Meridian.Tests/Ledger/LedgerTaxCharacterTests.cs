using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Holding-period character on relief projections. Classification follows IRC §1222/§1223: the
/// holding period starts the day after acquisition and must exceed one year, so a lot held exactly
/// one year is short-term. A prior wash sale moves the start earlier under §1223(3).
/// </summary>
[Trait("Category", "Unit")]
public sealed class LedgerTaxCharacterTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");

    private static LedgerAccount Account => LedgerAccounts.Securities("AAPL", "broker-1");

    [Theory]
    // Acquired 2025-01-01: one year later is 2026-01-01, which is NOT more than a year.
    [InlineData("2025-01-01", "2026-01-01", false)]
    [InlineData("2025-01-01", "2026-01-02", true)]
    // Sold the day before the anniversary is unambiguously short-term.
    [InlineData("2025-01-01", "2025-12-31", false)]
    // A span containing 29 Feb must not be promoted early by a 365-day count: 2024-02-28 +1y is
    // 2025-02-28, so 2025-02-28 is still short-term even though 366 days have elapsed.
    [InlineData("2024-02-28", "2025-02-28", false)]
    [InlineData("2024-02-28", "2025-03-01", true)]
    public void Classify_AppliesMoreThanOneYearRule(string acquired, string sold, bool expectLongTerm)
    {
        var character = TaxCharacterRule.Classify(DateOnly.Parse(acquired), DateOnly.Parse(sold));

        character.Should().Be(expectLongTerm ? TaxCharacter.LongTerm : TaxCharacter.ShortTerm);
    }

    [Fact]
    public void Projection_ReportsShortTermCharacterAndHoldingPeriodDays()
    {
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 150m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId)]));

        var selection = projection.Selections.Single();
        selection.TaxCharacter.Should().Be(TaxCharacter.ShortTerm);
        selection.HoldingPeriodDays.Should().Be(59);
        selection.HoldingPeriodExtendedByWashSale.Should().BeFalse();
        projection.ShortTermRealizedGainOrLoss.Should().Be(5_000m);
        projection.LongTermRealizedGainOrLoss.Should().Be(0m);
    }

    [Fact]
    public void Projection_SplitsCharacterTotalsAcrossLotsAndTiesToRealizedTotal()
    {
        // One lot held well over a year and one bought last month, sold together.
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 200m,
            salePrice: 150m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot("lot-old", new DateOnly(2024, 1, 1), 100m, 100m, SecurityId),
                new LedgerTaxLot("lot-new", new DateOnly(2026, 2, 1), 100m, 140m, SecurityId),
            ]));

        projection.Selections.Should().HaveCount(2);
        projection.Selections.Single(selection => selection.Lot.LotId == "lot-old")
            .TaxCharacter.Should().Be(TaxCharacter.LongTerm);
        projection.Selections.Single(selection => selection.Lot.LotId == "lot-new")
            .TaxCharacter.Should().Be(TaxCharacter.ShortTerm);

        projection.LongTermRealizedGainOrLoss.Should().Be(5_000m);  // 15,000 - 10,000
        projection.ShortTermRealizedGainOrLoss.Should().Be(1_000m); // 15,000 - 14,000

        // The split is a partition of the total, never an approximation of it.
        (projection.ShortTermRealizedGainOrLoss + projection.LongTermRealizedGainOrLoss)
            .Should().Be(projection.RealizedGainOrLoss);
    }

    [Fact]
    public void Projection_ParcelProceedsSumExactlyToTotalProceeds()
    {
        // A price that does not divide evenly across three parcels forces a rounding residual; the
        // parcels must still sum to the projection total so an export ties to the journal.
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 3m,
            salePrice: 100m / 3m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 1m, 10m, SecurityId),
                new LedgerTaxLot("lot-b", new DateOnly(2026, 1, 2), 1m, 10m, SecurityId),
                new LedgerTaxLot("lot-c", new DateOnly(2026, 1, 3), 1m, 10m, SecurityId),
            ]));

        projection.Selections.Sum(selection => selection.Proceeds).Should().Be(projection.Proceeds);
        projection.Selections.Sum(selection => selection.RealizedGainOrLoss)
            .Should().Be(projection.RealizedGainOrLoss);
    }

    [Fact]
    public void WashSaleCarry_ExtendsHoldingPeriodOntoReplacementLot()
    {
        // A replacement bought a month ago would be short-term on its own, but it absorbed a wash
        // sale whose shares were acquired in 2024 — §1223(3) carries that start onto it.
        var carried = new LedgerTaxLot(
            "lot-rep",
            new DateOnly(2026, 2, 1),
            100m,
            100m,
            SecurityId,
            holdingPeriodStartDate: new DateOnly(2024, 1, 1));

        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 150m,
            LedgerTaxLotReliefMethod.Fifo,
            [carried]));

        var selection = projection.Selections.Single();
        selection.TaxCharacter.Should().Be(TaxCharacter.LongTerm);
        selection.HoldingPeriodExtendedByWashSale.Should().BeTrue();
        projection.LongTermRealizedGainOrLoss.Should().Be(5_000m);
    }

    [Fact]
    public void WashSaleBasisAdjustment_CapitalizesLossAndCarriesHoldingPeriod()
    {
        // Replaying a retained deferral onto the replacement lot is what finally recognizes the
        // deferred loss: basis rises by the deferred amount and the holding period is inherited.
        var deferral = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.WashSale,
            2_000m,
            new DateOnly(2026, 1, 15),
            SecurityId,
            "lot-rep",
            "wash-sale-deferral:test",
            HoldingPeriodCarryDate: new DateOnly(2024, 1, 1));

        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 100m,
            salePrice: 150m,
            LedgerTaxLotReliefMethod.Fifo,
            // Acquired *after* the deferral's sale date — the symmetric §1091 window means a
            // replacement is frequently forward of the sale, which the reference-data
            // acquired-before-effective-date guard would otherwise reject.
            [new LedgerTaxLot("lot-rep", new DateOnly(2026, 2, 1), 100m, 100m, SecurityId)],
            basisAdjustments: [deferral]));

        // Basis rose from 10,000 to 12,000, so the gain is 3,000 rather than 5,000.
        projection.CostBasis.Should().Be(12_000m);
        projection.RealizedGainOrLoss.Should().Be(3_000m);

        var selection = projection.Selections.Single();
        selection.TaxCharacter.Should().Be(TaxCharacter.LongTerm);
        selection.HoldingPeriodExtendedByWashSale.Should().BeTrue();
    }

    [Fact]
    public void WashSaleBasisAdjustment_RequiresAReplacementLot()
    {
        var act = () => new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.WashSale,
            100m,
            new DateOnly(2026, 1, 15),
            SecurityId)
            .EnsureValid();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LedgerTaxLot_RejectsHoldingPeriodStartLaterThanAcquisition()
    {
        var act = () => new LedgerTaxLot(
            "lot-a",
            new DateOnly(2026, 1, 1),
            100m,
            100m,
            SecurityId,
            holdingPeriodStartDate: new DateOnly(2026, 2, 1));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
