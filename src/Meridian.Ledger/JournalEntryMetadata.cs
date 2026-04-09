namespace Meridian.Ledger;

/// <summary>
/// Optional audit metadata attached to a journal entry.
/// This makes ledger postings easier to correlate back to fills, orders, symbols,
/// strategy runs, replay activity, and financial accounts during portfolio analysis and backtesting.
/// </summary>
public sealed record JournalEntryMetadata(
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
    IReadOnlyDictionary<string, string>? Tags = null)
{
    /// <summary>
    /// Returns a normalized copy suitable for durable comparisons and filters.
    /// </summary>
    public JournalEntryMetadata Normalize()
    {
        IReadOnlyDictionary<string, string>? tags = null;
        if (Tags is not null && Tags.Count > 0)
        {
            tags = ReadOnlyCollectionHelpers.FreezeDictionary(
                Tags.ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        }

        return this with
        {
            ActivityType = string.IsNullOrWhiteSpace(ActivityType) ? null : ActivityType.Trim(),
            Symbol = string.IsNullOrWhiteSpace(Symbol) ? null : Symbol.Trim().ToUpperInvariant(),
            ProjectId = string.IsNullOrWhiteSpace(ProjectId) ? null : ProjectId.Trim(),
            LedgerBook = string.IsNullOrWhiteSpace(LedgerBook) ? null : LedgerBook.Trim(),
            ScenarioId = string.IsNullOrWhiteSpace(ScenarioId) ? null : ScenarioId.Trim(),
            StrategyId = string.IsNullOrWhiteSpace(StrategyId) ? null : StrategyId.Trim(),
            FinancialAccountId = string.IsNullOrWhiteSpace(FinancialAccountId) ? null : FinancialAccountId.Trim(),
            CounterpartyAccountId = string.IsNullOrWhiteSpace(CounterpartyAccountId) ? null : CounterpartyAccountId.Trim(),
            Institution = string.IsNullOrWhiteSpace(Institution) ? null : Institution.Trim(),
            Tags = tags,
        };
    }
}
