namespace Meridian.Backtesting.Sdk;

/// <summary>Aggregate execution cost summary across all fills in a simulation run.</summary>
/// <param name="TotalCommissions">Sum of commissions paid across all fills.</param>
/// <param name="TotalBuyNotional">Sum of (|quantity| × fillPrice) for buy-side fills.</param>
/// <param name="TotalSellNotional">Sum of (|quantity| × fillPrice) for sell-side fills.</param>
/// <param name="TotalNotional">Total two-sided notional (buy + sell).</param>
/// <param name="CommissionRateBps">Average commission as a fraction of total notional, in basis points.</param>
/// <param name="TotalFills">Number of fill events.</param>
/// <param name="BuyFills">Number of buy-side fill events.</param>
/// <param name="SellFills">Number of sell-side fill events.</param>
public sealed record TcaCostSummary(
    decimal TotalCommissions,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal TotalNotional,
    double CommissionRateBps,
    int TotalFills,
    int BuyFills,
    int SellFills);

/// <summary>Per-symbol execution cost breakdown for a simulation run.</summary>
/// <param name="Symbol">The traded symbol.</param>
/// <param name="TotalBuyNotional">Notional value of all buy fills for this symbol.</param>
/// <param name="TotalSellNotional">Notional value of all sell fills for this symbol.</param>
/// <param name="AvgBuyPrice">Volume-weighted average buy fill price (0 if no buys).</param>
/// <param name="AvgSellPrice">Volume-weighted average sell fill price (0 if no sells).</param>
/// <param name="TotalCommission">Total commissions paid for this symbol.</param>
/// <param name="CommissionRateBps">Commission as a fraction of symbol notional, in basis points.</param>
/// <param name="TotalFills">Total fill events for this symbol.</param>
public sealed record SymbolTcaSummary(
    string Symbol,
    decimal TotalBuyNotional,
    decimal TotalSellNotional,
    decimal AvgBuyPrice,
    decimal AvgSellPrice,
    decimal TotalCommission,
    double CommissionRateBps,
    int TotalFills);

/// <summary>
/// A fill whose commission rate significantly exceeded the run's average —
/// a candidate for execution quality review.
/// </summary>
/// <param name="FillId">Unique fill identifier.</param>
/// <param name="Symbol">Traded symbol.</param>
/// <param name="Notional">Absolute notional value of this fill (|quantity| × fillPrice).</param>
/// <param name="Commission">Commission charged for this fill.</param>
/// <param name="CommissionRateBps">Commission as a fraction of notional, in basis points.</param>
/// <param name="FilledAt">UTC timestamp of the fill.</param>
public sealed record TcaFillOutlier(
    Guid FillId,
    string Symbol,
    decimal Notional,
    decimal Commission,
    double CommissionRateBps,
    DateTimeOffset FilledAt);

/// <summary>
/// Complete Transaction Cost Analysis (TCA) report generated after each simulation run.
/// Contains aggregate cost attribution, per-symbol breakdowns, and outlier fills sorted
/// by cost rate descending.
/// </summary>
/// <param name="GeneratedAtUtc">Wall-clock time when the report was produced.</param>
/// <param name="StrategyAssemblyPath">
/// Path to the strategy assembly, taken directly from <see cref="BacktestRequest.StrategyAssemblyPath"/>.
/// <c>null</c> when a strategy instance is injected directly.
/// </param>
/// <param name="CostSummary">Run-level aggregate cost metrics.</param>
/// <param name="SymbolSummaries">Per-symbol cost breakdowns, ordered by total commission descending.</param>
/// <param name="Outliers">Fills with commission rate &gt; 3× the run average, ordered by rate descending.</param>
public sealed record TcaReport(
    DateTimeOffset GeneratedAtUtc,
    string? StrategyAssemblyPath,
    TcaCostSummary CostSummary,
    IReadOnlyList<SymbolTcaSummary> SymbolSummaries,
    IReadOnlyList<TcaFillOutlier> Outliers);
