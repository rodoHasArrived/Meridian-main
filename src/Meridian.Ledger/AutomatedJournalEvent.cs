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
    string? SourceEventId = null,
    DateOnly? EffectiveDate = null,
    string? IdempotencyKey = null,
    IReadOnlyList<JournalEvidenceReference>? EvidenceReferences = null)
{
    public IReadOnlyList<JournalEvidenceReference> EvidenceReferences { get; init; } =
        EvidenceReferences ?? [];
}