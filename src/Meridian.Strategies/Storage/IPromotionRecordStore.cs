using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Storage;

/// <summary>
/// Durable storage seam for strategy promotion decisions.
/// </summary>
public interface IPromotionRecordStore
{
    /// <summary>
    /// Loads historical records from durable storage.
    /// </summary>
    Task<IReadOnlyList<StrategyPromotionRecord>> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Appends a promotion decision atomically.
    /// </summary>
    Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default);

    /// <summary>
    /// Returns full promotion history from durable storage.
    /// </summary>
    Task<IReadOnlyList<StrategyPromotionRecord>> GetHistoryAsync(CancellationToken ct = default);
}
