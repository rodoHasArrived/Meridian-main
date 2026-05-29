namespace Meridian.Ledger;

/// <summary>
/// Normalized economic event used to draft a balanced ledger journal entry.
/// </summary>
public sealed record AutomatedJournalEvent(
    AutomatedJournalEventKind Kind,
    string Symbol,
    decimal Amount,
    DateTimeOffset Timestamp,
    string? FinancialAccountId = null,
    string? Description = null,
    Guid? SecurityId = null,
    string? SourceEventId = null);

