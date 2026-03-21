using Meridian.Backtesting.Sdk;

namespace Meridian.Backtesting.FillModels;

/// <summary>
/// Result of attempting to execute a working order against a single market event.
/// Captures any generated fills plus the updated order state to carry forward.
/// </summary>
internal sealed record OrderFillResult(
    Order UpdatedOrder,
    IReadOnlyList<FillEvent> Fills,
    bool RemoveOrder,
    bool WasTriggered = false);
