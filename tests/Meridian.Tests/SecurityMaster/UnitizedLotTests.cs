using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The shared lot shape: execution, backtesting, ledger and face-value lots all expose a decimal
/// quantity, a unit kind and a multiplier, and the asset-class registry supplies the unit and
/// multiplier for a class.
/// </summary>
[Trait("Category", "Unit")]
public sealed class UnitizedLotTests
{
    [Fact]
    public void UnitFor_ReadsTheRegistryUnitAndTheDeclaredMultiplierTerm()
    {
        var terms = JsonSerializer.SerializeToElement(new { multiplier = 100m });

        LotQuantity.UnitFor("Option", terms).Should().Be((SecurityLotUnitKind.Contracts, 100m));
        LotQuantity.UnitFor("Bond").Should().Be((SecurityLotUnitKind.FaceValue, 1m));
        LotQuantity.UnitFor("Equity", terms).Should().Be((SecurityLotUnitKind.Shares, 1m), "equity declares no multiplier term");
        LotQuantity.UnitFor("Future", JsonSerializer.SerializeToElement(new { multiplier = -5m }))
            .Should().Be((SecurityLotUnitKind.Contracts, 1m), "a non-positive multiplier is not a multiplier");
    }

    [Fact]
    public void For_ComputesEconomicUnits()
    {
        var quantity = LotQuantity.For("Option", 3m, JsonSerializer.SerializeToElement(new { multiplier = 100m }));

        quantity.EconomicUnits.Should().Be(300m);
    }

    [Fact]
    public void FaceValueLot_IsTheFaceDenominatedCase()
    {
        IUnitizedLot lot = new FaceValueLot("lot-1", Guid.NewGuid(), new DateOnly(2026, 1, 2), 250_000m, 101.25m);

        LotQuantity.Of(lot).Should().Be(new LotQuantity(250_000m, SecurityLotUnitKind.FaceValue, 1m));
    }

    [Fact]
    public void FaceValueLot_ToLedgerTaxLot_StatesItsHundredOfFaceUnit()
    {
        var face = new FaceValueLot("lot-2", Guid.NewGuid(), new DateOnly(2026, 1, 2), 250_000m, 101.25m);

        var ledger = face.ToLedgerTaxLot();

        ledger.UnitKind.Should().Be(SecurityLotUnitKind.FaceValue);
        LotQuantity.Of(ledger).EconomicUnits.Should().Be(250_000m, "quantity times multiplier is the face amount");
    }

    [Fact]
    public void ExecutionTaxLot_SupportsFractionalQuantities()
    {
        var lot = new TaxLot(Guid.NewGuid(), "VTI", 10.5m, 250m, DateTimeOffset.UtcNow);

        var reduced = lot.WithReducedQuantity(0.25m);

        reduced.Quantity.Should().Be(10.25m);
        lot.TotalCost.Should().Be(2_625m);
        lot.UnitKind.Should().Be(SecurityLotUnitKind.Shares);
    }

    [Fact]
    public void BacktestingLots_CarryDecimalQuantityAndUnit()
    {
        var open = new OpenLot(Guid.NewGuid(), "ESZ6", 2.5m, 4_000m, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            UnitKind = SecurityLotUnitKind.Contracts,
            Multiplier = 50m
        };

        open.CostBasis().Should().Be(10_000m);
        LotQuantity.Of(open).EconomicUnits.Should().Be(125m);
    }

    [Fact]
    public void LedgerTaxLot_DefaultsToShares()
    {
        IUnitizedLot lot = new LedgerTaxLot("l", new DateOnly(2026, 1, 1), 3m, 10m);

        LotQuantity.Of(lot).Should().Be(new LotQuantity(3m, SecurityLotUnitKind.Shares, 1m));
    }
}
