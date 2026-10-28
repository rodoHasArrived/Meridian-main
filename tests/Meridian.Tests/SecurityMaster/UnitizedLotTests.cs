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
        var invalidMultiplier = () => LotQuantity.UnitFor("Future", JsonSerializer.SerializeToElement(new { multiplier = -5m }));
        invalidMultiplier.Should().Throw<ArgumentException>();
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
        var lot = new TaxLot(Guid.NewGuid(), "VTI", 10.5m, 250m, DateTimeOffset.UtcNow) { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m };

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

        open.CostBasis().Should().Be(500_000m);
        LotQuantity.Of(open).EconomicUnits.Should().Be(125m);
    }

    [Fact]
    public void LedgerTaxLot_RequiresExplicitShareConvention()
    {
        IUnitizedLot lot = new LedgerTaxLot("l", new DateOnly(2026, 1, 1), 3m, 10m) { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m };

        LotQuantity.Of(lot).Should().Be(new LotQuantity(3m, SecurityLotUnitKind.Shares, 1m));
    }

    [Theory]
    [InlineData("Option")]
    [InlineData("Future")]
    public void UnitFor_RejectsMissingContractMultiplier(string assetClass)
    {
        var act = () => LotQuantity.UnitFor(assetClass);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UnitFor_RejectsUnknownAssetClass()
    {
        var act = () => LotQuantity.UnitFor("NewUnregisteredClass");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LotRecords_RejectNonPositiveMultiplier(int multiplier)
    {
        Action execution = () => _ = new TaxLot(Guid.NewGuid(), "X", 1m, 10m, DateTimeOffset.UtcNow)
        { UnitKind = SecurityLotUnitKind.Contracts, Multiplier = multiplier };
        Action backtest = () => _ = new OpenLot(Guid.NewGuid(), "X", 1m, 10m, DateTimeOffset.UtcNow, Guid.NewGuid())
        { UnitKind = SecurityLotUnitKind.Contracts, Multiplier = multiplier };
        Action ledger = () => _ = new LedgerTaxLot("x", new DateOnly(2026, 1, 1), 1m, 10m)
        { UnitKind = SecurityLotUnitKind.Contracts, Multiplier = multiplier };

        execution.Should().Throw<ArgumentOutOfRangeException>();
        backtest.Should().Throw<ArgumentOutOfRangeException>();
        ledger.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LotJson_RejectsMissingUnitAndMultiplier()
    {
        var incomplete = JsonSerializer.Serialize(new
        {
            LotId = Guid.NewGuid(),
            Symbol = "X",
            Quantity = 1m,
            CostBasis = 10m,
            OpenedAt = DateTimeOffset.UtcNow
        });

        Action act = () => JsonSerializer.Deserialize<TaxLot>(incomplete);
        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(SecurityLotUnitKind.FaceValue, 10_000, 1, 99, 101, 9_900, 10_100)]
    [InlineData(SecurityLotUnitKind.Contracts, 2, 100, 5, 6, 1_000, 1_200)]
    [InlineData(SecurityLotUnitKind.Units, 2, 1, 100, 110, 200, 220)]
    public void LotPrices_ApplyDenominationAndMultiplier(SecurityLotUnitKind unitKind,
        decimal amount, decimal multiplier, decimal entryPrice, decimal closePrice, decimal basis, decimal proceeds)
    {
        var now = DateTimeOffset.UtcNow;
        var execution = new TaxLot(Guid.NewGuid(), "X", amount, entryPrice, now)
        { UnitKind = unitKind, Multiplier = multiplier };
        var open = new OpenLot(Guid.NewGuid(), "X", amount, entryPrice, now, Guid.NewGuid())
        { UnitKind = unitKind, Multiplier = multiplier };
        var closed = new ClosedLot(open.LotId, "X", amount, entryPrice, now, open.OpenFillId,
            closePrice, now.AddDays(1), Guid.NewGuid())
        { UnitKind = unitKind, Multiplier = multiplier };
        var relieved = new Meridian.Execution.TaxLotAccounting.RelievedLot(execution, (long)amount, closePrice);

        execution.TotalCost.Should().Be(basis);
        open.CostBasis().Should().Be(basis);
        open.NotionalValue(closePrice).Should().Be(proceeds);
        open.UnrealizedPnl(closePrice).Should().Be(proceeds - basis);
        (open with { IsShort = true }).UnrealizedPnl(closePrice).Should().Be(basis - proceeds);
        closed.RealizedPnl.Should().Be(proceeds - basis);
        (closed with { IsShort = true }).RealizedPnl.Should().Be(basis - proceeds);
        relieved.CostRelieved.Should().Be(basis);
        relieved.Proceeds.Should().Be(proceeds);
        relieved.RealizedPnl.Should().Be(proceeds - basis);
    }

    [Fact]
    public void ClosedContractLot_RetainedMoneyBasisIsNotScaledAgain()
    {
        var now = DateTimeOffset.UtcNow;
        var closed = new ClosedLot(Guid.NewGuid(), "X", 2m, 5m, now, Guid.NewGuid(), 6m,
            now.AddDays(1), Guid.NewGuid())
        {
            UnitKind = SecurityLotUnitKind.Contracts,
            Multiplier = 100m,
            CostBasis = 999m
        };

        closed.RealizedPnl.Should().Be(201m);
    }
}
