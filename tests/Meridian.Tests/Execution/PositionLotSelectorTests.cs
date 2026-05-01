using Meridian.Contracts.Api;
using Meridian.Execution.Services;

namespace Meridian.Tests.Execution;

public sealed class PositionLotSelectorTests
{
    private static readonly DateTimeOffset BaseTs = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SelectLot_DefaultsToFifo()
    {
        var lots = BuildLots();

        var selected = PositionLotSelector.SelectLot(lots, null);

        selected!.LotId.Should().Be("lot-1");
    }

    [Fact]
    public void SelectLot_UsesLifo()
    {
        var selected = PositionLotSelector.SelectLot(BuildLots(), new PositionLotSelection(PositionLotSelectionMethod.Lifo));

        selected!.LotId.Should().Be("lot-3");
    }

    [Fact]
    public void SelectLot_UsesHifo()
    {
        var selected = PositionLotSelector.SelectLot(BuildLots(), new PositionLotSelection(PositionLotSelectionMethod.Hifo));

        selected!.LotId.Should().Be("lot-2");
    }

    [Fact]
    public void SelectLot_UsesSpecificId()
    {
        var selected = PositionLotSelector.SelectLot(BuildLots(), new PositionLotSelection(PositionLotSelectionMethod.SpecificId, "LOT-3"));

        selected!.LotId.Should().Be("lot-3");
    }

    private static IReadOnlyList<PositionLotEntry> BuildLots() =>
    [
        new("lot-1", 10, 100m, BaseTs),
        new("lot-2", 10, 110m, BaseTs.AddMinutes(1)),
        new("lot-3", 10, 105m, BaseTs.AddMinutes(2)),
    ];
}
