namespace Meridian.Contracts.Api;

public enum PositionLotSelectionMethod
{
    Fifo,
    Lifo,
    Hifo,
    SpecificId,
}

public sealed record PositionLotEntry(
    string LotId,
    decimal OpenQuantity,
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record PositionLotSelection(
    PositionLotSelectionMethod Method,
    string? SpecificLotId = null);
