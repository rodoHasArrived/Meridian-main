using Meridian.Domain.Events;

namespace Meridian.Backtesting.FillModels;

/// <summary>
/// Determines whether and how a pending order is filled given the current market event.
/// Each implementation represents a different execution realism assumption.
/// </summary>
internal interface IFillModel
{
    /// <summary>
    /// Attempt to fill <paramref name="order"/> against <paramref name="evt"/>.
    /// Returns the updated order state together with zero or more fill events.
    /// </summary>
    OrderFillResult TryFill(Order order, MarketEvent evt);
}
