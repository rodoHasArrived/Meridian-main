using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterSnapshotLotEconomicsTests
{
    [Theory]
    [InlineData("0.9", false)]
    [InlineData("0.9", true)]
    [InlineData("1.5", false)]
    [InlineData("1.5", true)]
    public void FractionalSnapshot_PreservesExactQuantityAndLegacyShortDirection(string quantityText, bool shortPosition)
    {
        var quantity = decimal.Parse(quantityText, System.Globalization.CultureInfo.InvariantCulture);
        var lot = new OpenLot(Guid.NewGuid(), "TEST", quantity, 100m, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        var signedQuantity = shortPosition ? -quantity : quantity;
        var position = new Position("TEST", (long)signedQuantity, 100m, 9m, 0m, [lot]) { ExactQuantity = signedQuantity };

        var result = SecurityMasterSnapshotLotEconomics.Project(lot, position);

        result.IsShort.Should().Be(shortPosition);
        result.UnrealizedPnl.Should().Be(9m);
    }

    [Fact]
    public void MissingOrZeroPosition_DoesNotInventAMarketValue()
    {
        var lot = new OpenLot(Guid.NewGuid(), "TEST", 0.9m, 100m, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            UnitKind = SecurityLotUnitKind.Shares,
            Multiplier = 1m
        };
        SecurityMasterSnapshotLotEconomics.Project(lot, null).UnrealizedPnl.Should().BeNull();
        SecurityMasterSnapshotLotEconomics.Project(lot, new("TEST", 0, 100m, 9m, 0m)).UnrealizedPnl.Should().BeNull();
    }
}
