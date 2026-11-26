using System.Text.Json.Serialization;

namespace Meridian.Backtesting.Sdk;

/// <summary>
/// Point-in-time view of a single financial account inside a broader portfolio snapshot.
/// </summary>
public sealed record FinancialAccountSnapshot(
    string AccountId,
    string DisplayName,
    FinancialAccountKind Kind,
    string? Institution,
    decimal Cash,
    decimal MarginBalance,
    decimal LongMarketValue,
    decimal ShortMarketValue,
    decimal Equity,
    IReadOnlyDictionary<string, Position> Positions,
    FinancialAccountRules Rules,
    IReadOnlyList<OpenLot>? OpenLots = null,
    IReadOnlyList<ClosedLot>? ClosedLots = null)
{
    /// <summary>Accounting currency declared by the bound simulation ledger; legacy absence stays unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FunctionalCurrency { get; init; }

    /// <summary>Earned dividends included in equity but not yet available as cash.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public decimal DividendReceivable { get; init; }
}
