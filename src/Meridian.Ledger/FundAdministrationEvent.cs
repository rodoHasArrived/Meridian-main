namespace Meridian.Ledger;

/// <summary>
/// Classifies an immutable fund-administration governance event. Every privileged administration
/// action — posting a journal, locking or reopening a period, exporting a report, or delivering a
/// file — appends one event of the matching kind to a <see cref="FundAdministrationEventLog"/> so the
/// full administration trail is tamper-evident and replayable.
/// </summary>
public enum FundAdministrationEventKind
{
    /// <summary>A journal entry was posted to a ledger book.</summary>
    JournalPosted,

    /// <summary>A journal template was registered or amended in the template library.</summary>
    JournalTemplateRegistered,

    /// <summary>A recurring journal schedule was created or amended.</summary>
    RecurringJournalScheduled,

    /// <summary>A recurring journal occurrence was materialized into a posting draft.</summary>
    RecurringJournalRun,

    /// <summary>An accounting period was locked.</summary>
    PeriodLocked,

    /// <summary>A locked accounting period was reopened with supporting evidence.</summary>
    PeriodReopened,

    /// <summary>A fiscal year-end close was executed.</summary>
    YearEndClosed,

    /// <summary>A portfolio pricing rule was created, amended, or retired.</summary>
    PricingRuleChanged,

    /// <summary>An onboarding template was applied to stamp a new fund structure.</summary>
    OnboardingApplied,

    /// <summary>A governed report pack was exported.</summary>
    ReportExported,

    /// <summary>A normalized file was delivered to an administrator, custodian, or counterparty.</summary>
    FileDelivered,

    /// <summary>A reconciliation true-break was escalated.</summary>
    ReconciliationBreakEscalated,

    /// <summary>A service-level-agreement timer breached its due time.</summary>
    SlaBreached,
}

/// <summary>
/// A single immutable entry in the fund-administration event log. Each event carries the hash of the
/// previous event (<see cref="PreviousHash"/>) and its own content hash (<see cref="Hash"/>), forming
/// a forward hash chain so any retroactive edit, reorder, or deletion is detectable by
/// <see cref="FundAdministrationEventLog.VerifyIntegrity"/>.
/// </summary>
public sealed record FundAdministrationEvent(
    string EventId,
    long Sequence,
    FundAdministrationEventKind Kind,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string SubjectId,
    string Summary,
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyList<JournalEvidenceReference>? Evidence,
    string Hash,
    string? PreviousHash)
{
    /// <summary>Attributes normalized to an ordinal, case-insensitive read-only map.</summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Evidence references, defaulted to an empty list when none were supplied.</summary>
    public IReadOnlyList<JournalEvidenceReference> Evidence { get; init; } = Evidence ?? [];
}

/// <summary>
/// A request to append one governance event to a <see cref="FundAdministrationEventLog"/>. The log
/// stamps sequence, timestamp, and hash-chain fields; callers supply only the domain payload.
/// </summary>
public sealed record FundAdministrationEventRequest(
    FundAdministrationEventKind Kind,
    string Actor,
    string SubjectId,
    string Summary,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<JournalEvidenceReference>? Evidence = null,
    DateTimeOffset? OccurredAtUtc = null);
