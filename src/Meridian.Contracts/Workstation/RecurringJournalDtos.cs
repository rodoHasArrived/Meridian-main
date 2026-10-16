namespace Meridian.Contracts.Workstation;

/// <summary>
/// Retained occurrence identity and approval-draft lineage shared by both Accounting queues.
/// Evidence references identify retained source metadata; they do not assert that source bytes
/// have been independently verified. State and approval status are authoritative server values.
/// </summary>
public sealed record RecurringJournalOccurrenceDto(
    string OccurrenceId,
    string ScheduleId,
    int ScheduleVersion,
    string TemplateId,
    int TemplateVersion,
    DateOnly EffectiveDate,
    string FundProfileId,
    Guid LedgerBookId,
    string EntityId,
    string? PeriodId,
    string State,
    Guid? JournalEntryId,
    string? ApprovalStatus,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceEvidenceReferences,
    string? PeriodLockOwner = null,
    string? GovernedReopenPath = null);

/// <summary>An exact fund, book and entity projection. Unavailable state must fail the read.</summary>
public sealed record RecurringJournalQueueDto(
    string FundProfileId,
    Guid LedgerBookId,
    string EntityId,
    IReadOnlyList<RecurringJournalOccurrenceDto> Occurrences);

/// <summary>Read-only shared projection; clients never claim occurrences or infer approval.</summary>
public interface IRecurringJournalQueueSource
{
    Task<RecurringJournalQueueDto> GetQueueAsync(
        string fundProfileId,
        Guid ledgerBookId,
        string entityId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null);
}
