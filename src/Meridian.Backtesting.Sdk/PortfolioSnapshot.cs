using System.Text.Json.Serialization;

namespace Meridian.Backtesting.Sdk;

/// <summary>Point-in-time snapshot of the simulated portfolio, recorded at each simulated day-end.</summary>
public sealed record PortfolioSnapshot(
    DateTimeOffset Timestamp,
    DateOnly Date,
    decimal Cash,
    decimal MarginBalance,          // negative = debit; positive = excess margin
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal TotalEquity,            // Cash + LongMarketValue + ShortMarketValue + DividendReceivable
    decimal DailyReturn,            // (TotalEquity - PrevEquity) / PrevEquity
    IReadOnlyDictionary<string, Position> Positions,
    IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts,
    IReadOnlyList<CashFlowEntry> DayCashFlows)
{
    /// <summary>
    /// Atomically captured native account facts retained by a paper source. Null denotes legacy
    /// or unavailable source coverage; an empty list is a captured, authorized empty partition.
    /// These receipts do not fabricate simulation lots from quoted average prices.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Meridian.Contracts.Workstation.StrategyPositionAccountingEvidence>? AccountingPositions { get; init; }

    /// <summary>Accounting currency declared by the bound simulation ledger; legacy absence stays unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FunctionalCurrency { get; init; }

    /// <summary>Earned dividends included in equity but not yet available as cash.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal DividendReceivable { get; init; }
}
