namespace Meridian.Backtesting.Sdk;

/// <summary>Point-in-time snapshot of the simulated portfolio, recorded at each simulated day-end.</summary>
public sealed record PortfolioSnapshot(
    DateTimeOffset Timestamp,
    DateOnly Date,
    decimal Cash,
    decimal MarginBalance,          // negative = debit; positive = excess margin
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal TotalEquity,            // Cash + LongMarketValue + ShortMarketValue
    decimal DailyReturn,            // (TotalEquity - PrevEquity) / PrevEquity
    IReadOnlyDictionary<string, Position> Positions,
    IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts,
    IReadOnlyList<CashFlowEntry> DayCashFlows);
