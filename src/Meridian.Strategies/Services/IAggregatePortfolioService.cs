using Meridian.Execution.Models;
using Meridian.Execution.Services;

namespace Meridian.Strategies.Services;

/// <summary>
/// Aggregated (netted) position across multiple strategy runs.
/// </summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="TotalQuantity">Net signed quantity (long positive, short negative).</param>
/// <param name="LongQuantity">Sum of all long lots.</param>
/// <param name="ShortQuantity">Sum of all short lots (absolute value).</param>
/// <param name="WeightedAverageCost">Weighted average cost basis across all contributing runs.</param>
/// <param name="TotalUnrealisedPnl">Aggregate unrealised P&amp;L.</param>
/// <param name="Contributions">Per-run breakdown.</param>
public sealed record AggregatedPosition(
    string Symbol,
    decimal TotalQuantity,
    decimal LongQuantity,
    decimal ShortQuantity,
    decimal WeightedAverageCost,
    decimal TotalUnrealisedPnl,
    IReadOnlyList<RunPositionContribution> Contributions);

/// <summary>Contribution of one strategy run to an aggregated position.</summary>
/// <param name="RunId">Strategy run identifier.</param>
/// <param name="AccountId">Account within that run.</param>
/// <param name="Quantity">Signed quantity from this run/account.</param>
/// <param name="CostBasis">Average cost basis in this run/account.</param>
/// <param name="UnrealisedPnl">Unrealised P&amp;L in this run/account.</param>
public sealed record RunPositionContribution(
    string RunId,
    string AccountId,
    decimal Quantity,
    decimal CostBasis,
    decimal UnrealisedPnl);

/// <summary>Cross-strategy gross/net exposure summary.</summary>
/// <param name="GrossExposure">Sum of all absolute market values.</param>
/// <param name="NetExposure">Long market value minus short market value.</param>
/// <param name="Top5Concentrations">Symbols ranked by absolute gross exposure.</param>
/// <param name="AsOf">Timestamp of the calculation.</param>
public sealed record CrossStrategyExposureReport(
    decimal GrossExposure,
    decimal NetExposure,
    IReadOnlyList<string> Top5Concentrations,
    DateTimeOffset AsOf);

/// <summary>Net position for a single symbol across all active runs.</summary>
/// <param name="Symbol">Ticker symbol.</param>
/// <param name="NetQuantity">Signed net quantity.</param>
/// <param name="GrossQuantity">Sum of absolute quantities across all runs.</param>
public sealed record NetSymbolPosition(
    string Symbol,
    decimal NetQuantity,
    decimal GrossQuantity);

/// <summary>
/// Aggregates position data across all currently running strategies registered in
/// <see cref="PortfolioRegistry"/>.
/// </summary>
public interface IAggregatePortfolioService
{
    /// <summary>
    /// Returns netted positions across all active strategy runs,
    /// or only the runs specified in <paramref name="runIds"/> when non-null.
    /// </summary>
    IReadOnlyList<AggregatedPosition> GetAggregatedPositions(IReadOnlyList<string>? runIds = null);

    /// <summary>
    /// Returns gross/net exposure across all active strategies.
    /// </summary>
    CrossStrategyExposureReport GetCrossStrategyExposure();

    /// <summary>
    /// Returns the net and gross position for a single <paramref name="symbol"/> across all
    /// active strategies.
    /// </summary>
    NetSymbolPosition GetNetPositionForSymbol(string symbol);
}
