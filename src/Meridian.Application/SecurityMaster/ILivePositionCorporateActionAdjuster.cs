namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Result of applying corporate-action adjustments to a live open position.
/// </summary>
public sealed record PositionCorporateActionAdjustment(
    string Ticker,
    decimal OriginalQuantity,
    decimal AdjustedQuantity,
    decimal OriginalCostBasis,
    decimal AdjustedCostBasis,
    int ActionCount);

/// <summary>
/// Adjusts an open live position's quantity and cost basis to account for stock splits and
/// dividends that occurred between <c>positionOpenedAt</c> and <c>asOf</c>.
/// Implemented by <c>CorporateActionAdjustmentService</c> in <c>Meridian.Backtesting</c>.
/// </summary>
public interface ILivePositionCorporateActionAdjuster
{
    /// <summary>
    /// Returns an adjusted <see cref="PositionCorporateActionAdjustment"/> for the given position.
    /// If no corporate actions are found or the symbol cannot be resolved, returns an adjustment
    /// with the original values unchanged.
    /// </summary>
    /// <param name="ticker">Ticker symbol of the security.</param>
    /// <param name="quantity">Current position quantity (positive for long, negative for short).</param>
    /// <param name="costBasis">Current average cost basis per share.</param>
    /// <param name="positionOpenedAt">Timestamp when the position was first opened (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker,
        decimal quantity,
        decimal costBasis,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default);
}
