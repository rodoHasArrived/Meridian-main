using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class SecurityMasterCostBasisAdjustmentServiceTests
{
    private static readonly DateOnly FaceAcquired = new(2020, 1, 1);
    private static readonly DateOnly HalfLife = new(2025, 1, 1);

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task ProjectReliefAsync_LargeFaceAndSubTenPlaceAmortization_PreservesCents(int multiplier)
    {
        // Both representations hold a ten-cent premium on one billion of face. Half-life
        // amortization is five cents, even though its per-face delta has eleven decimal places.
        var lot = new LedgerTaxLot("large-face", FaceAcquired, 1_000_000_000m / multiplier,
            1.0000000001m * multiplier, SecurityId)
        {
            UnitKind = SecurityLotUnitKind.FaceValue,
            Multiplier = multiplier
        };

        var result = await FaceAmortizationService().ProjectReliefAsync(FaceReliefInput(lot, HalfLife), SecurityId);

        result.CostBasis.Should().Be(1_000_000_000.05m);
        result.EffectiveLots.Single().UnitCost.Should().Be(1.00000000005m * multiplier);
        result.AppliedAdjustments.Should().ContainSingle().Which.Value.Should().Be(-0.00000000005m * multiplier);
        result.IsBalanced.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task ProjectReliefAsync_ExactBasisWithFactorAndAmortization_PreservesMonetaryResidual(int multiplier)
    {
        var lot = new LedgerTaxLot("exact-face", FaceAcquired, 1000m / multiplier,
            1.10m * multiplier, SecurityId, costBasis: 1100.0025m)
        {
            UnitKind = SecurityLotUnitKind.FaceValue,
            Multiplier = multiplier
        };

        var result = await FaceAmortizationService(currentFactor: 0.8m).ProjectReliefAsync(
            FaceReliefInput(lot, HalfLife, quantitySold: 800m / multiplier), SecurityId);

        result.CostBasis.Should().Be(840.002m);
        result.EffectiveLots.Single().CostBasis.Should().Be(840.002m);
        result.IsBalanced.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 110, 1050)]
    [InlineData(100, 110, 1050)]
    [InlineData(1, 90, 950)]
    [InlineData(100, 90, 950)]
    [InlineData(1, 100, 1000)]
    [InlineData(100, 100, 1000)]
    public async Task ProjectReliefAsync_FaceAndHundredFaceUnits_ProduceTheSameMonetaryBasis(
        int multiplier, int pricePerHundred, int expectedBasis)
    {
        var service = FaceAmortizationService();
        var lot = FaceReliefLot(multiplier, pricePerHundred);

        var result = await service.ProjectReliefAsync(FaceReliefInput(lot, HalfLife), SecurityId);

        result.CostBasis.Should().Be(expectedBasis);
        result.Proceeds.Should().Be(1200m);
        result.RealizedGainOrLoss.Should().Be(1200m - expectedBasis);
        result.IsBalanced.Should().BeTrue();
        result.EffectiveLots.Should().ContainSingle().Which.Multiplier.Should().Be(multiplier);
        result.EffectiveLots[0].UnitKind.Should().Be(SecurityLotUnitKind.FaceValue);
        if (pricePerHundred == 100)
            result.AppliedAdjustments.Should().BeEmpty();
        else
            result.AppliedAdjustments.Should().ContainSingle().Which.Value.Should()
                .Be((100m - pricePerHundred) * multiplier / 200m);
    }

    [Theory]
    [InlineData(1, 110, 1050)]
    [InlineData(10, 110, 1050)]
    [InlineData(100, 110, 1050)]
    [InlineData(1000, 110, 1050)]
    [InlineData(1, 90, 950)]
    [InlineData(100, 90, 950)]
    public async Task ProjectReliefAsync_SourceParBasis_PreservesFaceValueLotEconomics(
        int parBasis, int pricePerHundred, int expectedBasis)
    {
        var source = new FaceValueLot("face-source", SecurityId, FaceAcquired, originalFace: 1000m,
            pricePercentOfPar: pricePerHundred * (decimal)parBasis / 100m, parBasis: parBasis);
        var lot = source.ToLedgerTaxLot();

        var result = await FaceAmortizationService().ProjectReliefAsync(FaceReliefInput(lot, HalfLife), SecurityId);

        result.CostBasis.Should().Be(expectedBasis);
        result.CostBasis.Should().Be(source.AmortizedBasisAsOf(DayCountConvention.Thirty360, new(2030, 1, 1), HalfLife));
        result.IsBalanced.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 110, 2030)]
    [InlineData(100, 110, 2032)]
    [InlineData(1, 90, 2032)]
    [InlineData(100, 90, 2030)]
    public async Task ProjectReliefAsync_AtOrAfterMaturity_StopsAtPar(
        int multiplier, int pricePerHundred, int year)
    {
        var lot = FaceReliefLot(multiplier, pricePerHundred);

        var result = await FaceAmortizationService().ProjectReliefAsync(
            FaceReliefInput(lot, new(year, 1, 1)), SecurityId);

        result.CostBasis.Should().Be(1000m);
        result.EffectiveLots.Single().UnitCost.Should().Be(multiplier);
        result.IsBalanced.Should().BeTrue();
    }

    [Theory]
    [InlineData(2019)]
    [InlineData(2020)]
    public async Task BuildAdjustmentsAsync_AtOrBeforeAcquisition_DoesNotAmortize(int year)
    {
        var result = await FaceAmortizationService().BuildAdjustmentsAsync(
            SecurityId, [FaceReliefLot(1, 110)], new(year, 1, 1));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_LotAcquiredAtMaturity_DoesNotAmortize()
    {
        var lot = new LedgerTaxLot("at-maturity", new(2030, 1, 1), 1000m, 1.10m, SecurityId)
        {
            UnitKind = SecurityLotUnitKind.FaceValue,
            Multiplier = 1m
        };

        var result = await FaceAmortizationService().BuildAdjustmentsAsync(SecurityId, [lot], new(2031, 1, 1));

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityLotUnitKind.Shares)]
    [InlineData(SecurityLotUnitKind.Units)]
    [InlineData(SecurityLotUnitKind.Contracts)]
    [InlineData(SecurityLotUnitKind.Notional)]
    [InlineData(SecurityLotUnitKind.Commitment)]
    [InlineData(SecurityLotUnitKind.CurrencyAmount)]
    public async Task BuildAdjustmentsAsync_LinkedIncompatibleUnit_RefusesParAmortization(SecurityLotUnitKind unit)
    {
        var lot = new LedgerTaxLot("wrong-unit", FaceAcquired, 1000m, 1.10m, SecurityId)
        {
            UnitKind = unit,
            Multiplier = 1m
        };

        var action = () => FaceAmortizationService().BuildAdjustmentsAsync(SecurityId, [lot], HalfLife);

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*wrong-unit*face-value lot*");
    }

    [Fact]
    public async Task BuildAdjustmentsAsync_UnlinkedAndOtherSecurityLots_AreNotAmortized()
    {
        var unlinked = new LedgerTaxLot("unlinked", FaceAcquired, 1000m, 1.10m)
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        var other = new LedgerTaxLot("other", FaceAcquired, 1000m, 1.10m,
            Guid.Parse("cccccccc-0000-0000-0000-000000000002"))
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };

        var result = await FaceAmortizationService().BuildAdjustmentsAsync(SecurityId, [unlinked, other], HalfLife);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 110, false, 840)]
    [InlineData(100, 110, false, 840)]
    [InlineData(1, 110, true, 840)]
    [InlineData(100, 110, true, 840)]
    [InlineData(1, 90, false, 760)]
    [InlineData(100, 90, false, 760)]
    [InlineData(1, 90, true, 760)]
    [InlineData(100, 90, true, 760)]
    public async Task ProjectReliefAsync_FactorRestatement_IsAppliedExactlyOnce(
        int multiplier, int pricePerHundred, bool faceAlreadyRestated, int expectedBasis)
    {
        var service = FaceAmortizationService(currentFactor: 0.8m);
        var lot = FaceReliefLot(multiplier, pricePerHundred, face: faceAlreadyRestated ? 800m : 1000m);
        // The relief input's sale quantity is measured after the pending factor adjustment.
        var input = FaceReliefInput(lot, HalfLife, quantitySold: 800m / multiplier);

        var result = await service.ProjectReliefAsync(input, SecurityId,
            priorFactor: faceAlreadyRestated ? 0.8m : 1m);

        result.CostBasis.Should().Be(expectedBasis);
        result.EffectiveLots.Should().ContainSingle().Which.Quantity.Should().Be(800m / multiplier);
        result.EffectiveLots[0].UnitCost.Should().Be((pricePerHundred + 100m) * multiplier / 200m);
        result.AppliedAdjustments.Count(adjustment => adjustment.Kind == LedgerTaxLotBasisAdjustmentKind.Factor)
            .Should().Be(faceAlreadyRestated ? 0 : 1);
        result.AppliedAdjustments.Should().ContainSingle(adjustment => adjustment.Kind == LedgerTaxLotBasisAdjustmentKind.Amortization);
        result.IsBalanced.Should().BeTrue();
    }

    private static SecurityMasterCostBasisAdjustmentService FaceAmortizationService(decimal? currentFactor = null)
        => BuildService(QueryServiceWith(security: Security("Bond", new
        {
            maturityDate = "2030-01-01",
            dayCountConvention = "30/360",
            currentFactor
        })));

    private static LedgerTaxLot FaceReliefLot(int multiplier, int pricePerHundred, decimal face = 1000m)
        => new("face-relief", FaceAcquired, face / multiplier, pricePerHundred * (decimal)multiplier / 100m, SecurityId)
        {
            UnitKind = SecurityLotUnitKind.FaceValue,
            Multiplier = multiplier
        };

    private static LedgerTaxLotReliefInput FaceReliefInput(LedgerTaxLot lot, DateOnly date, decimal? quantitySold = null)
        => new(LedgerAccounts.Securities(Ticker), date, quantitySold ?? lot.Quantity,
            salePrice: 1.20m * lot.Multiplier, LedgerTaxLotReliefMethod.Fifo, [lot]);
}
