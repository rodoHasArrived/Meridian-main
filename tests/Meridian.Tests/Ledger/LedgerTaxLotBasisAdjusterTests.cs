using FluentAssertions;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

/// <summary>
/// Unit tests for <see cref="LedgerTaxLotBasisAdjuster"/> and its integration into
/// <see cref="LedgerTaxLotReliefProjector"/> — the seam that feeds Security Master day-count,
/// factor, and corporate-action basis adjustments into cost-basis relief.
/// </summary>
[Trait("Category", "Unit")]
public sealed class LedgerTaxLotBasisAdjusterTests
{
    private static readonly Guid SecurityId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000010");
    private static readonly Guid OtherSecurityId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000020");

    [Fact]
    public void Apply_WithNoAdjustments_ReturnsSameInstance()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 10m, 90m, SecurityId)];

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, []);

        result.Should().BeSameAs(lots);
    }

    [Fact]
    public void Apply_ForwardSplit_ScalesQuantityUpAndUnitCostDown_PreservingTotalBasis()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 10m, 90m, SecurityId)];
        var split = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Split, 2m, new DateOnly(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [split]);

        result.Should().ContainSingle();
        result[0].Quantity.Should().Be(20m);
        result[0].UnitCost.Should().Be(45m);
        (result[0].Quantity * result[0].UnitCost).Should().Be(900m);
    }

    [Fact]
    public void Apply_ReverseSplit_ScalesQuantityDownAndUnitCostUp()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 10m, 9m, SecurityId)];
        var reverse = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Split, 0.1m, new DateOnly(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [reverse]);

        result[0].Quantity.Should().Be(1m);
        result[0].UnitCost.Should().Be(90m);
    }

    [Fact]
    public void Apply_DoesNotAdjustLotsAcquiredOnOrAfterEffectiveDate()
    {
        // A lot acquired on the ex-date already reflects the post-split terms.
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 2, 1), 10m, 90m, SecurityId)];
        var split = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Split, 2m, new DateOnly(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [split]);

        result[0].Quantity.Should().Be(10m);
        result[0].UnitCost.Should().Be(90m);
    }

    [Fact]
    public void Apply_ReturnOfCapital_ReducesUnitCostAndFloorsAtZero()
    {
        IReadOnlyList<LedgerTaxLot> lots =
        [
            new("lot-a", new DateOnly(2026, 1, 1), 10m, 50m, SecurityId),
            new("lot-b", new DateOnly(2026, 1, 1), 10m, 3m, SecurityId),
        ];
        var roc = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.ReturnOfCapital, 5m, new DateOnly(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [roc]);

        result[0].UnitCost.Should().Be(45m);
        result[1].UnitCost.Should().Be(0m); // floored, cannot go negative
    }

    [Fact]
    public void Apply_FactorPaydown_ScalesFaceWhileKeepingUnitCost()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 100m, 99m, SecurityId)];
        var factor = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Factor, 0.8m, new DateOnly(2026, 6, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [factor]);

        result[0].Quantity.Should().Be(80m);
        result[0].UnitCost.Should().Be(99m);
        (result[0].Quantity * result[0].UnitCost).Should().Be(7920m); // paid-down basis is relieved
    }

    [Fact]
    public void Apply_FactorPaydownToNearZero_DropsLot()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 1m, 100m, SecurityId)];
        var factor = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Factor, 0.00000000001m, new DateOnly(2026, 6, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [factor]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_PremiumAmortization_MovesUnitCostTowardParDown()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 100m, 102m, SecurityId)];
        var amortization = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Amortization, -1m, new DateOnly(2026, 6, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [amortization]);

        result[0].UnitCost.Should().Be(101m);
    }

    [Fact]
    public void Apply_DiscountAccretion_MovesUnitCostTowardParUp()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 100m, 98m, SecurityId)];
        var accretion = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Amortization, 1m, new DateOnly(2026, 6, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [accretion]);

        result[0].UnitCost.Should().Be(99m);
    }

    [Fact]
    public void Apply_SecurityScopedAdjustment_OnlyTouchesMatchingLots()
    {
        IReadOnlyList<LedgerTaxLot> lots =
        [
            new("lot-match", new DateOnly(2026, 1, 1), 10m, 90m, SecurityId),
            new("lot-other", new DateOnly(2026, 1, 1), 10m, 90m, OtherSecurityId),
        ];
        var split = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Split, 2m, new DateOnly(2026, 2, 1), SecurityId);

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [split]);

        result.Single(l => l.LotId == "lot-match").Quantity.Should().Be(20m);
        result.Single(l => l.LotId == "lot-other").Quantity.Should().Be(10m);
    }

    [Fact]
    public void Apply_LotTargetedAdjustment_OnlyTouchesNamedLot()
    {
        IReadOnlyList<LedgerTaxLot> lots =
        [
            new("lot-a", new DateOnly(2026, 1, 1), 100m, 102m, SecurityId),
            new("lot-b", new DateOnly(2026, 1, 1), 100m, 102m, SecurityId),
        ];
        var amortization = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Amortization, -2m, new DateOnly(2026, 6, 1), SecurityId, LotId: "lot-a");

        var result = LedgerTaxLotBasisAdjuster.Apply(lots, [amortization]);

        result.Single(l => l.LotId == "lot-a").UnitCost.Should().Be(100m);
        result.Single(l => l.LotId == "lot-b").UnitCost.Should().Be(102m);
    }

    [Fact]
    public void Apply_InvalidSplitRatio_Throws()
    {
        IReadOnlyList<LedgerTaxLot> lots = [new("lot-a", new DateOnly(2026, 1, 1), 10m, 90m, SecurityId)];
        var invalid = new LedgerTaxLotBasisAdjustment(
            LedgerTaxLotBasisAdjustmentKind.Split, 0m, new DateOnly(2026, 2, 1), SecurityId);

        var act = () => LedgerTaxLotBasisAdjuster.Apply(lots, [invalid]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Project_WithSplitAdjustment_RelievesAgainstSplitAdjustedLots()
    {
        var account = LedgerAccounts.Securities("AAPL", "broker-1");
        var input = new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2026, 3, 1),
            quantitySold: 12m,
            salePrice: 50m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 10m, 90m, SecurityId)],
            basisAdjustments:
            [
                new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Split, 2m, new DateOnly(2026, 2, 1), SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.IsBalanced.Should().BeTrue();
        projection.EffectiveLots.Should().ContainSingle();
        projection.EffectiveLots[0].Quantity.Should().Be(20m);
        projection.EffectiveLots[0].UnitCost.Should().Be(45m);
        projection.AppliedAdjustments.Should().ContainSingle();
        projection.Selections[0].QuantityRelieved.Should().Be(12m);
        projection.CostBasis.Should().Be(540m); // 12 shares @ 45 post-split
        projection.Proceeds.Should().Be(600m);
        projection.RealizedGainOrLoss.Should().Be(60m);
    }

    [Fact]
    public void Project_WithFactorPaydown_ReducesRelievableBasis()
    {
        var account = LedgerAccounts.Securities("MBS1", "broker-2");
        var input = new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2026, 6, 30),
            quantitySold: 40m,
            salePrice: 101m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId)],
            basisAdjustments:
            [
                new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Factor, 0.5m, new DateOnly(2026, 6, 30), SecurityId),
            ]);

        var projection = LedgerTaxLotReliefProjector.Project(input);

        projection.EffectiveLots[0].Quantity.Should().Be(50m); // 100 face paid down to 50
        projection.CostBasis.Should().Be(4000m);
        projection.RealizedGainOrLoss.Should().Be(40m);
    }

    [Fact]
    public void Project_WhenAdjustedQuantityIsInsufficient_Throws()
    {
        var account = LedgerAccounts.Securities("MBS1", "broker-2");
        var input = new LedgerTaxLotReliefInput(
            account,
            new DateOnly(2026, 6, 30),
            quantitySold: 60m,
            salePrice: 101m,
            LedgerTaxLotReliefMethod.Fifo,
            [new LedgerTaxLot("lot-a", new DateOnly(2026, 1, 1), 100m, 100m, SecurityId)],
            basisAdjustments:
            [
                new LedgerTaxLotBasisAdjustment(LedgerTaxLotBasisAdjustmentKind.Factor, 0.5m, new DateOnly(2026, 6, 30), SecurityId),
            ]);

        var act = () => LedgerTaxLotReliefProjector.Project(input);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Insufficient open tax-lot quantity*");
    }
}
