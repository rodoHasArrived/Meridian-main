using System.Reflection;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Locks the one lot model to the lot unit enum and to the typed lots in the contracts assembly:
/// adding a lot unit or a typed lot fails here until the model names it, and every asset class in
/// the registry resolves to exactly one lot model entry through its lot unit.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityLotModelTests
{
    private static readonly Guid Security = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 1, 1);

    [Fact]
    public void Model_CoversEveryLotUnitExactlyOnce()
    {
        SecurityLotModel.All.Select(static descriptor => descriptor.UnitKind)
            .Should().BeEquivalentTo(Enum.GetValues<SecurityLotUnitKind>(),
                "every lot unit needs exactly one lot model entry");
    }

    [Fact]
    public void EveryTypedLotInContracts_IsTheCanonicalLotForExactlyOneUnit()
    {
        var typedLots = typeof(IUnitizedLot).Assembly.GetTypes()
            .Where(static type => type is { IsAbstract: false, IsInterface: false } && typeof(IUnitizedLot).IsAssignableFrom(type))
            .ToArray();

        var canonical = SecurityLotModel.All
            .Where(static descriptor => descriptor.CanonicalLotType is not null)
            .Select(static descriptor => descriptor.CanonicalLotType!)
            .ToArray();

        canonical.Should().BeEquivalentTo(typedLots,
            "each typed lot in the contracts assembly owns exactly one lot unit, and no unit names a type that is not a lot");
        canonical.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryCanonicalLot_ReportsTheUnitItIsRegisteredUnder()
    {
        IUnitizedLot[] lots =
        [
            new FaceValueLot("face", Security, Date, 1_000m, 100m),
            new NotionalLot("notional", Security, Date, 1_000m),
            new FundUnitLot("units", Security, Date, 10m, 100m),
            new CommitmentLot("commitment", Security, Date, 1_000m)
        ];

        lots.Select(static lot => lot.GetType()).Should().BeEquivalentTo(
            SecurityLotModel.All.Where(static d => d.CanonicalLotType is not null).Select(static d => d.CanonicalLotType),
            "this test constructs one instance of every canonical lot");

        foreach (var lot in lots)
        {
            SecurityLotModel.For(lot.UnitKind).CanonicalLotType.Should().Be(lot.GetType());
            SecurityLotModel.For(lot).Should().BeSameAs(SecurityLotModel.For(lot.UnitKind));
            SecurityLotModel.IsCanonicalLot(lot).Should().BeTrue();
        }
    }

    [Fact]
    public void EveryAssetClass_ResolvesToOneLotModelEntryThroughItsUnit()
    {
        foreach (var descriptor in SecurityAssetClassCatalog.All)
        {
            SecurityLotModel.ForAssetClass(descriptor.AssetClass).UnitKind.Should().Be(descriptor.LotUnit, descriptor.AssetClass);
        }
    }

    [Fact]
    public void NotionalClasses_AreTheOtcDerivatives()
    {
        SecurityLotModel.AssetClassesWithUnit(SecurityLotUnitKind.Notional)
            .Should().BeEquivalentTo(["Swap", "CreditDefaultSwap", "Swaption", "CapFloor"]);
    }

    [Fact]
    public void OnlyFaceValue_TracksAPaydownFactor_AndOnlyNotional_AmortizesBySchedule()
    {
        SecurityLotModel.All.Where(static d => d.TracksPaydownFactor).Select(static d => d.UnitKind)
            .Should().Equal(SecurityLotUnitKind.FaceValue);
        SecurityLotModel.All.Where(static d => d.AmortizesBySchedule).Select(static d => d.UnitKind)
            .Should().Equal(SecurityLotUnitKind.Notional);
    }

    [Fact]
    public void IsCanonicalLot_AcceptsGenericLotsOnlyForUnitsWithoutATypedLot()
    {
        SecurityLotModel.IsCanonicalLot(new GenericLot(SecurityLotUnitKind.Shares)).Should().BeTrue();
        SecurityLotModel.IsCanonicalLot(new GenericLot(SecurityLotUnitKind.Contracts)).Should().BeTrue();
        SecurityLotModel.IsCanonicalLot(new GenericLot(SecurityLotUnitKind.CurrencyAmount)).Should().BeTrue();
        SecurityLotModel.IsCanonicalLot(new GenericLot(SecurityLotUnitKind.FaceValue)).Should().BeFalse("face has a typed lot");
        SecurityLotModel.IsCanonicalLot(new GenericLot(SecurityLotUnitKind.Notional)).Should().BeFalse("notional has a typed lot");
    }

    [Fact]
    public void For_UnknownUnit_Throws()
    {
        FluentActions.Invoking(() => SecurityLotModel.For((SecurityLotUnitKind)999)).Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed record GenericLot(SecurityLotUnitKind UnitKind) : IUnitizedLot
    {
        public decimal Quantity => 1m;

        public decimal Multiplier => 1m;
    }
}
