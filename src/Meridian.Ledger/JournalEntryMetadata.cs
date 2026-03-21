namespace Meridian.Ledger;

/// <summary>
/// Optional audit metadata attached to a journal entry.
/// This makes ledger postings easier to correlate back to fills, orders, symbols,
/// strategy runs, replay activity, and financial accounts during portfolio analysis and backtesting.
/// </summary>
public sealed record JournalEntryMetadata(
    string? ActivityType = null,
    string? Symbol = null,
    Guid? OrderId = null,
    Guid? FillId = null,
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
            tags = new Dictionary<string, string>(
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
            StrategyId = string.IsNullOrWhiteSpace(StrategyId) ? null : StrategyId.Trim(),
            FinancialAccountId = string.IsNullOrWhiteSpace(FinancialAccountId) ? null : FinancialAccountId.Trim(),
            CounterpartyAccountId = string.IsNullOrWhiteSpace(CounterpartyAccountId) ? null : CounterpartyAccountId.Trim(),
            Institution = string.IsNullOrWhiteSpace(Institution) ? null : Institution.Trim(),
            Tags = tags,
        };
    }
}
