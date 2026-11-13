using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

public sealed partial class LedgerTaxLotBasisAdjusterTests
{
    [Fact]
    public void Apply_FactorThenAmortization_TransformsExactBasisWithTheEffectiveQuantity()
    {
        var lot = new LedgerTaxLot("exact-face", new(2026, 1, 1), 10m, 110m, SecurityId, costBasis: 1100.0025m)
        {
            UnitKind = SecurityLotUnitKind.FaceValue,
            Multiplier = 100m
        };
        var adjustments = new LedgerTaxLotBasisAdjustment[]
        {
            new(LedgerTaxLotBasisAdjustmentKind.Factor, 0.8m, new(2026, 2, 1), SecurityId),
            new(LedgerTaxLotBasisAdjustmentKind.Amortization, -5m, new(2026, 2, 1), SecurityId)
        };
        var input = new LedgerTaxLotReliefInput(LedgerAccounts.Securities("EXACT"), new(2026, 2, 1),
            quantitySold: 8m, salePrice: 120m, LedgerTaxLotReliefMethod.Fifo, [lot], basisAdjustments: adjustments);

        var result = LedgerTaxLotReliefProjector.Project(input);

        result.CostBasis.Should().Be(840.002m, "the factor retains 880.002, then eight units amortize five each");
        result.EffectiveLots.Single().CostBasis.Should().Be(840.002m);
        result.EffectiveLots.Single().UnitCost.Should().Be(105m);
        result.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void Apply_FactorOnMatchingFractionalCentBasis_DoesNotLoseExactRelief()
    {
        var lot = new LedgerTaxLot("fractional", new(2026, 1, 1), 2m, 0.00125m, SecurityId, costBasis: 0.0025m)
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        var adjustment = new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Factor,
            0.8m, new(2026, 2, 1), SecurityId);
        var input = new LedgerTaxLotReliefInput(LedgerAccounts.Securities("EXACT"), new(2026, 2, 1),
            quantitySold: 1.6m, salePrice: 1m, LedgerTaxLotReliefMethod.Fifo, [lot], basisAdjustments: [adjustment]);

        var result = LedgerTaxLotReliefProjector.Project(input);

        result.CostBasis.Should().Be(0.002m);
        result.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void Apply_SplitReturnOfCapitalAndWashSale_PreserveExactMonetaryTransforms()
    {
        var lot = new LedgerTaxLot("exact-share", new(2026, 1, 1), 3m, 1m, SecurityId, costBasis: 3.0001m)
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        var split = new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Split,
            3m, new(2026, 2, 1), SecurityId);
        var capitalReturn = new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital,
            0.01m, new(2026, 3, 1), SecurityId);
        var washSale = new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.WashSale,
            0.0025m, new(2026, 4, 1), SecurityId, LotId: lot.LotId);

        var splitOnly = LedgerTaxLotBasisAdjuster.Apply([lot], [split]);
        var result = LedgerTaxLotBasisAdjuster.Apply([lot], [washSale, capitalReturn, split]);

        splitOnly.Single().CostBasis.Should().Be(3.0001m);
        result.Single().CostBasis.Should().Be(2.9126m, "the split preserves money, nine units return .01 each, then .0025 is capitalized");
        result.Single().Quantity.Should().Be(9m);
    }

    [Theory]
    [InlineData(LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital)]
    [InlineData(LedgerTaxLotBasisAdjustmentKind.Amortization)]
    public void Apply_ExactBasisReduction_FloorsAtZero(LedgerTaxLotBasisAdjustmentKind kind)
    {
        var lot = new LedgerTaxLot("floor", new(2026, 1, 1), 2m, 1m, SecurityId, costBasis: 2.0001m)
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        var adjustment = new LedgerTaxLotBasisAdjustment(kind,
            kind == LedgerTaxLotBasisAdjustmentKind.Amortization ? -3m : 3m, new(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply([lot], [adjustment]);

        result.Single().CostBasis.Should().Be(0m);
    }

    [Fact]
    public void Apply_UnrelatedExactBasisLot_RetainsItsOriginalInstanceAndFullReliefBasis()
    {
        var exact = new LedgerTaxLot("exact", new(2026, 1, 1), 2m, 0.001m, OtherSecurityId, costBasis: 0.0025m)
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        var adjusted = new LedgerTaxLot("adjusted", new(2026, 1, 1), 10m, 102m, SecurityId)
        {
            UnitKind = SecurityLotUnitKind.FaceValue,
            Multiplier = 100m
        };
        var adjustment = new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Amortization,
            -1m, new(2026, 2, 1), SecurityId);
        var input = new LedgerTaxLotReliefInput(LedgerAccounts.Securities("BASIS"), new(2026, 2, 1),
            quantitySold: 2m, salePrice: 1m, LedgerTaxLotReliefMethod.SpecificId, [exact, adjusted],
            specificLotIds: [exact.LotId], basisAdjustments: [adjustment]);

        var result = LedgerTaxLotReliefProjector.Project(input);

        result.EffectiveLots.Single(lot => lot.LotId == exact.LotId).Should().BeSameAs(exact);
        result.CostBasis.Should().Be(0.0025m);
        result.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void Apply_AdjustmentOnAcquisitionDate_DoesNotDiscardExactBasis()
    {
        var lot = new LedgerTaxLot("exact", new(2026, 2, 1), 2m, 0.001m, SecurityId, costBasis: 0.0025m)
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        var adjustment = new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Split,
            2m, new(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply([lot], [adjustment]);

        result.Should().ContainSingle().Which.Should().BeSameAs(lot);
        result[0].CostBasis.Should().Be(0.0025m);
    }

    [Theory]
    [InlineData(SecurityLotUnitKind.FaceValue)]
    [InlineData(SecurityLotUnitKind.Shares)]
    public void Apply_FaceUnitPrecision_DoesNotChangeShareRounding(SecurityLotUnitKind unitKind)
    {
        var lot = new LedgerTaxLot("precision", new(2026, 1, 1), 1_000_000_000m, 1.0000000001m, SecurityId)
        {
            UnitKind = unitKind,
            Multiplier = 1m
        };
        var adjustment = new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Amortization,
            -0.00000000005m, new(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply([lot], [adjustment]);

        result.Single().UnitCost.Should().Be(unitKind == SecurityLotUnitKind.FaceValue
            ? 1.00000000005m
            : 1.0000000001m);
    }
}
