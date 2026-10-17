using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

/// <summary>
/// The paper-trading lot book is read in the shared lot shape: each open <c>PositionLot</c> is an
/// <see cref="IUnitizedLot"/> whose unit and multiplier come live from the position its fills opened.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PaperPositionUnitizedLotTests
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 2, 3, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Lots_AreSharesByDefault_WithUnsignedQuantity()
    {
        var position = new PaperPosition("MSFT");

        position.AddLot(-12.5m, 400m, OpenedAt);

        var lot = position.UnitizedLots.Should().ContainSingle().Subject;
        lot.Quantity.Should().Be(12.5m, "the shared shape carries size; the paper lot keeps the short sign separately");
        lot.UnitKind.Should().Be(SecurityLotUnitKind.Shares);
        lot.Multiplier.Should().Be(1m);
        LotQuantity.Of(lot).EconomicUnits.Should().Be(12.5m);
    }

    [Fact]
    public void ContractMultiplier_RecordedAfterTheLotOpened_IsNotStaleOnTheLot()
    {
        var position = new PaperPosition("ESZ6");
        position.AddLot(2m, 4_000m, OpenedAt);

        position.AttributeFill("fund-a", 2m, contractMultiplier: 50m);

        var lot = position.UnitizedLots.Should().ContainSingle().Subject;
        lot.UnitKind.Should().Be(SecurityLotUnitKind.Contracts);
        lot.Multiplier.Should().Be(50m);
        LotQuantity.Of(lot).EconomicUnits.Should().Be(100m);
    }

    [Fact]
    public void FaceValuePricing_MakesTheLotsFaceDenominated()
    {
        var position = new PaperPosition("T 4.5 2036") { UsesFaceValuePricing = true };

        position.AddLot(250_000m, 1.0125m, OpenedAt);

        var lot = position.UnitizedLots.Should().ContainSingle().Subject;
        lot.UnitKind.Should().Be(SecurityLotUnitKind.FaceValue);
        lot.Quantity.Should().Be(250_000m);
        lot.Multiplier.Should().Be(1m);
    }

    [Fact]
    public void RescaledLots_KeepTheSharedShape()
    {
        var position = new PaperPosition("AAPL");
        position.AddLot(10m, 100m, OpenedAt);

        position.RescaleLots(quantityFactor: 2m, priceFactor: 0.5m, adjustedQuantity: 20m, adjustedTotalCostBasis: null);

        var lot = position.UnitizedLots.Should().ContainSingle().Subject;
        lot.Quantity.Should().Be(20m);
        lot.UnitKind.Should().Be(SecurityLotUnitKind.Shares);
        LotQuantity.Of(lot).EconomicUnits.Should().Be(20m);
    }
}
