using Meridian.Contracts.Api;

namespace Meridian.Execution.Services;

public static class PositionLotSelector
{
    public static PositionLotEntry? SelectLot(
        IReadOnlyList<PositionLotEntry> lots,
        PositionLotSelection? selection)
    {
        if (lots.Count == 0)
        {
            return null;
        }

        var method = selection?.Method ?? PositionLotSelectionMethod.Fifo;
        return method switch
        {
            PositionLotSelectionMethod.Fifo => lots.OrderBy(static l => l.OpenedAt).First(),
            PositionLotSelectionMethod.Lifo => lots.OrderByDescending(static l => l.OpenedAt).First(),
            PositionLotSelectionMethod.Hifo => lots.OrderByDescending(static l => l.EntryPrice).ThenBy(static l => l.OpenedAt).First(),
            PositionLotSelectionMethod.SpecificId => SelectSpecificLot(lots, selection?.SpecificLotId),
            _ => lots.OrderBy(static l => l.OpenedAt).First(),
        };
    }

    private static PositionLotEntry? SelectSpecificLot(IReadOnlyList<PositionLotEntry> lots, string? specificLotId)
    {
        if (string.IsNullOrWhiteSpace(specificLotId))
        {
            return null;
        }

        return lots.FirstOrDefault(lot => string.Equals(lot.LotId, specificLotId, StringComparison.OrdinalIgnoreCase));
    }
}
