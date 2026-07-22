using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Tests for the canonical <see cref="FaceValueLot"/> aggregate: explicit par basis, factor
/// restatement, day-count amortized basis, and the normalization seam into the ledger engines'
/// price-per-100 lot convention.
/// </summary>
public sealed class FaceValueLotTests
{
    private static readonly Guid SecurityId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    [Fact]
    public void CostBasisAndPremium_ParHundredQuote_ComputeInCurrencyUnits()
    {
        var lot = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1), originalFace: 1000m, pricePercentOfPar: 102m);

        lot.CostBasis.Should().Be(1020m);
        lot.PremiumDiscount.Should().Be(20m);
    }

    [Fact]
    public void CostBasisAndPremium_PerUnitQuote_MatchTheParHundredEquivalent()
    {
        // The same economic lot quoted per unit of face (price 1.02 against a par basis of 1)
        // must produce identical economics — this is the case that silently mis-amortized when
        // the price-per-100 convention was an implicit caller assumption.
        var perUnit = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1),
            originalFace: 1000m, pricePercentOfPar: 1.02m, parBasis: 1m);

        perUnit.CostBasis.Should().Be(1020m);
        perUnit.PremiumDiscount.Should().Be(20m);
    }

    [Fact]
    public void PremiumDiscount_DiscountLot_IsNegative()
    {
        var lot = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1), 1000m, 98m);

        lot.PremiumDiscount.Should().Be(-20m);
    }

    [Fact]
    public void CurrentFace_RestatesFromTheBookedFactor()
    {
        // Face 1000 booked when the pool factor was 0.8; at a current factor of 0.6 the
        // outstanding face is 1000 x 0.6 / 0.8 = 750.
        var lot = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1), 1000m, 100m, bookedFactor: 0.8m);

        lot.CurrentFace(0.6m).Should().Be(750m);
        lot.CurrentFace(0.8m).Should().Be(1000m);
    }

    [Fact]
    public void AmortizedBasisAsOf_HalfLifeElapsed_AmortizesHalfThePremium()
    {
        // ACT/365: acquired 2026-01-01, maturity 2028-01-01 (730 days), as of 2027-01-01 (365
        // days) -> weight 0.5 -> basis 1020 - 10 = 1010.
        var lot = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1), 1000m, 102m);

        lot.AmortizedBasisAsOf(DayCountConvention.Actual365, new DateOnly(2028, 1, 1), new DateOnly(2027, 1, 1))
            .Should().Be(1010m);
    }

    [Fact]
    public void AmortizedBasisAsOf_PastMaturity_ClampsAtPar()
    {
        var lot = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1), 1000m, 102m);

        lot.AmortizedBasisAsOf(DayCountConvention.Actual365, new DateOnly(2028, 1, 1), new DateOnly(2030, 1, 1))
            .Should().Be(1000m);
    }

    [Fact]
    public void AmortizedBasisAsOf_BeforeAnyHolding_ReturnsCostBasisUnchanged()
    {
        var lot = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 6, 1), 1000m, 102m);

        lot.AmortizedBasisAsOf(DayCountConvention.Actual365, new DateOnly(2028, 1, 1), new DateOnly(2026, 6, 1))
            .Should().Be(1020m);
    }

    [Theory]
    [InlineData("", 1000, 102, 1, 100)]
    [InlineData("lot-1", 0, 102, 1, 100)]
    [InlineData("lot-1", 1000, -1, 1, 100)]
    [InlineData("lot-1", 1000, 102, 0, 100)]
    [InlineData("lot-1", 1000, 102, 1.5, 100)]
    [InlineData("lot-1", 1000, 102, 1, 0)]
    public void Constructor_RejectsInvalidEconomics(string lotId, decimal face, decimal price, decimal factor, decimal parBasis)
    {
        var act = () => new FaceValueLot(lotId, SecurityId, new DateOnly(2026, 1, 1), face, price, factor, parBasis);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsUnlinkedLots()
    {
        var act = () => new FaceValueLot("lot-1", Guid.Empty, new DateOnly(2026, 1, 1), 1000m, 102m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToLedgerTaxLot_NormalizesAnyParBasisToTheEngineConvention()
    {
        // Par-100 and per-unit quotes of the same economic lot must adapt to the identical
        // LedgerTaxLot, so the relief and amortization engines see one convention regardless of
        // how the source quoted the price.
        var parHundred = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1), 1000m, 102m);
        var perUnit = new FaceValueLot("lot-1", SecurityId, new DateOnly(2026, 1, 1), 1000m, 1.02m, parBasis: 1m);

        var fromParHundred = parHundred.ToLedgerTaxLot();
        var fromPerUnit = perUnit.ToLedgerTaxLot();

        fromParHundred.Quantity.Should().Be(10m);
        fromParHundred.UnitCost.Should().Be(102m);
        fromPerUnit.Quantity.Should().Be(fromParHundred.Quantity);
        fromPerUnit.UnitCost.Should().Be(fromParHundred.UnitCost);
        fromPerUnit.SecurityId.Should().Be(SecurityId);

        // The engine-side premium (quantity x (unitCost - 100)) reproduces the aggregate's own.
        (fromPerUnit.Quantity * (fromPerUnit.UnitCost - FaceValueLotExtensions.LedgerLotParBasis))
            .Should().Be(perUnit.PremiumDiscount);
    }
}
