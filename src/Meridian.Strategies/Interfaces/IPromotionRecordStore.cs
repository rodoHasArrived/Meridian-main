using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Interfaces;

/// <summary>
/// Durable append-only store for promotion decisions and audit metadata.
/// </summary>
public interface IPromotionRecordStore
{
    /// <summary>
    /// Loads all recorded promotion decisions in append order.
    /// </summary>
    Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Appends a new promotion decision to the durable history.
    /// </summary>
    Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default);
}
