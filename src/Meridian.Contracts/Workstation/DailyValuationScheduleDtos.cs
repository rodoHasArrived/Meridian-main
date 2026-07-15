using System.Text.Json.Serialization;

namespace Meridian.Contracts.Workstation;

/// <summary>Operator-visible state of a configured daily valuation schedule.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DailyValuationScheduleStateDto>))]
public enum DailyValuationScheduleStateDto
{
    NotConfigured = 0,
    Scheduled = 1,
    Running = 2,
    DraftReady = 3,
    NoAdjustment = 4,
    Blocked = 5,
    Failed = 6,
    Posted = 7
}

/// <summary>
/// Shared close-cockpit projection for scheduled daily valuation work.
/// </summary>
public sealed record DailyValuationScheduleStatusDto(
    string? ScheduleId,
    string? FundProfileId,
    Guid? LedgerBookId,
    Guid? PeriodId,
    bool IsConfigured,
    bool IsEnabled,
    DateTimeOffset? NextRunAtUtc,
    DateTimeOffset? LastRunAtUtc,
    DailyValuationScheduleStateDto State,
    string Summary,
    Guid? JournalEntryId,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<Guid>? JournalEntryIds = null,
    string? BatchCorrelationId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    /// <summary>
    /// Every governed draft in the latest valuation batch. <see cref="JournalEntryId"/> remains
    /// the first-entry compatibility alias for older clients.
    /// </summary>
    public IReadOnlyList<Guid> JournalEntryIds { get; init; } = JournalEntryIds ?? [];
}

/// <summary>
/// One human-governed command that submits, approves, and posts every draft in the latest daily
/// valuation batch. The server derives the batch members from the retained schedule; callers
/// cannot replace the journal-entry set.
/// </summary>
public sealed record DailyValuationBatchLifecycleRequestDto(
    string ScheduleId,
    string FundProfileId,
    string Actor,
    string Notes,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

/// <summary>Result of one governed daily-valuation batch lifecycle command.</summary>
public sealed record DailyValuationBatchLifecycleResultDto(
    string ScheduleId,
    string BatchCorrelationId,
    bool IsComplete,
    IReadOnlyList<Guid> JournalEntryIds,
    IReadOnlyList<Guid> PostedJournalEntryIds,
    IReadOnlyList<string>? Blockers = null)
{
    public IReadOnlyList<string> Blockers { get; init; } = Blockers ?? [];
}

/// <summary>
/// Read-only scheduler projection consumed by close-cockpit surfaces without taking
/// a dependency on the workstation scheduler implementation.
/// </summary>
public interface IDailyValuationScheduleStatusSource
{
    Task<DailyValuationScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default,
        string? entityId = null,
        string? tenantId = null,
        string? companyId = null);
}
