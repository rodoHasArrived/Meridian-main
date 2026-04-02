namespace Meridian.Ledger;

/// <summary>
/// Structured ledger query for filtering journal entries by time range and audit metadata.
/// </summary>
public sealed record LedgerQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? DescriptionContains = null,
    string? ActivityType = null,
    string? Symbol = null,
    Guid? SecurityId = null,
    Guid? OrderId = null,
    Guid? FillId = null,
    string? ProjectId = null,
    string? LedgerBook = null,
    LedgerViewKind? LedgerView = null,
    string? ScenarioId = null,
    string? StrategyId = null,
    string? FinancialAccountId = null,
    string? CounterpartyAccountId = null,
    string? Institution = null,
    LedgerAccountType? AccountType = null);
