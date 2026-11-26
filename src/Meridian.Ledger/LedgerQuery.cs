namespace Meridian.Ledger;

/// <summary>
/// Structured ledger query for filtering journal entries by time range and audit metadata.
/// <c>RunId</c> matches entries tagged with <see cref="StrategyRunIdTag"/> (exact, case-sensitive).
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
    LedgerAccountType? AccountType = null,
    DateOnly? EffectiveDate = null,
    string? IdempotencyKey = null,
    string? FundEventId = null,
    string? FundEventType = null,
    string? CapitalAccountId = null,
    string? InvestorId = null,
    string? PaymentIntentId = null,
    string? SettlementReference = null,
    LedgerLineDimensionSet? LineDimensions = null,
    string? RunId = null)
{
    /// <summary>
    /// Journal tag carrying the strategy run that produced an entry. Trade-fill posting stamps
    /// it on attributed fills; <see cref="RunId"/> filters on it. The run stays a tag rather
    /// than a metadata field so retained journals keep comparing equal on replay. This label
    /// alone is not trusted execution ownership; use <see cref="LedgerRunAttribution"/> for proof.
    /// </summary>
    public const string StrategyRunIdTag = "strategy.runId";

    /// <summary>New typed, admitted strategy-run ownership retained by the execution posting boundary.</summary>
    public const string AccountingRunIdTag = "execution.accountingRunId.v1";
}
