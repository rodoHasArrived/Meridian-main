using Meridian.Backtesting.Sdk;

namespace Meridian.Ui.Shared.Services;

/// <summary>Projects retained snapshot monetary values without rounding a fractional position to whole shares.</summary>
internal static class SecurityMasterSnapshotLotEconomics
{
    public static (bool IsShort, decimal? UnrealizedPnl) Project(OpenLot lot, Position? position)
    {
        ArgumentNullException.ThrowIfNull(lot);
        var isShort = lot.IsShort || (position?.ExactQuantity ?? 0m) < 0m;
        if (position is null || position.ExactQuantity == 0m)
            return (isShort, null);
        // AverageCostBasis is monetary basis per position unit here, not a raw bond/contract
        // quote. Applying a quote multiplier or percent-of-par conversion again would double it.
        var unitValue = position.AverageCostBasis + position.UnrealizedPnl / position.ExactQuantity;
        var value = lot.Quantity * unitValue;
        var basis = lot.CostBasis();
        return (isShort, isShort ? basis - value : value - basis);
    }
}
