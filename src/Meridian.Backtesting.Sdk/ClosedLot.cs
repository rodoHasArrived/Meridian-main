using Meridian.Contracts.SecurityMaster;

namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Immutable record of a realised tax lot — a block of shares that was opened and subsequently
/// fully or partially closed.  Each close produces exactly one <see cref="ClosedLot"/>.
/// </summary>
public sealed record ClosedLot(
    Guid LotId,
    string Symbol,
    decimal Quantity,
    decimal EntryPrice,
    DateTimeOffset OpenedAt,
    Guid OpenFillId,
    decimal ClosePrice,
    DateTimeOffset ClosedAt,
    Guid CloseFillId,
    string? AccountId = null,
    bool IsShort = false) : IUnitizedLot
{
    private SecurityLotUnitKind _unitKind;
    private decimal _multiplier;

    /// <summary>Exact booked basis; absent on legacy records that only retain an entry quote.</summary>
    public decimal? CostBasis { get; init; }

    /// <summary>Acquisition commission allocated to this disposal.</summary>
    public decimal EntryCommission { get; init; }

    /// <summary>Closing commission allocated to this disposal.</summary>
    public decimal ExitCommission { get; init; }

    /// <summary>What one unit of <see cref="Quantity"/> measures. Must be supplied explicitly.</summary>
    public required SecurityLotUnitKind UnitKind
    {
        get => _unitKind;
        init => _unitKind = Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(UnitKind), value, "A defined lot unit is required.");
    }

    /// <summary>Positive contract multiplier scaling one unit to economic exposure. Supply 1 for unscaled units.</summary>
    public required decimal Multiplier
    {
        get => _multiplier;
        init => _multiplier = value > 0m
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Multiplier), value, "A positive lot multiplier is required.");
    }

    /// <summary>Total realised profit or loss for this lot.</summary>
    public decimal RealizedPnl
    {
        get
        {
            var quantity = new LotQuantity(Quantity, UnitKind, Multiplier);
            var basis = CostBasis ?? quantity.ValueAtPrice(EntryPrice);
            var proceeds = quantity.ValueAtPrice(ClosePrice);
            return IsShort ? basis - proceeds : proceeds - basis;
        }
    }

    /// <summary>Total holding duration from open to close.</summary>
    public TimeSpan HoldingPeriod => ClosedAt - OpenedAt;

    /// <summary>
    /// Returns <c>true</c> when the lot was held for at least 365 days — the IRS
    /// long-term capital gains threshold.
    /// </summary>
    public bool IsLongTerm => HoldingPeriod >= TimeSpan.FromDays(365);

    /// <summary>Per-share realised P&amp;L (close price minus entry price).</summary>
    public decimal RealizedPnlPerShare => Quantity == 0 ? 0m : RealizedPnl / Quantity;
}
