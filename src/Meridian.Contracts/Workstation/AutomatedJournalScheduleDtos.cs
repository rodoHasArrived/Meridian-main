using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.Workstation;

/// <summary>Operator-visible state of scheduled fee-accrual and dividend-capture work.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutomatedJournalScheduleStateDto>))]
public enum AutomatedJournalScheduleStateDto
{
    NotConfigured = 0,
    Scheduled = 1,
    Running = 2,
    DraftReady = 3,
    NeedsInvestigation = 4,
    NoDraftRequired = 5,
    Blocked = 6,
    Failed = 7
}

/// <summary>
/// Reviewed capital-account tie-out for the NAV and high-water-mark inputs used by one
/// monthly fee-accrual cycle. The scheduler verifies the retained values, variance,
/// confidence, reviewer, source version, and evidence before it can create a draft.
/// </summary>
public sealed record AutomatedJournalCapitalAccountReconciliationDto(
    string ReconciliationId,
    string PeriodId,
    string Currency,
    decimal ReconciledBeginningNav,
    decimal ReconciledEndingNavBeforeFees,
    decimal ReconciledHighWaterMark,
    decimal CapitalAccountOpeningBalance,
    decimal CapitalAccountEndingBalanceBeforeFees,
    decimal CapitalAccountHighWaterMark,
    decimal MaximumVarianceTolerance,
    decimal ConfidenceScore,
    bool IsReconciled,
    string SourceVersion,
    string ReviewedBy,
    DateTimeOffset ReviewedAtUtc,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null)
{
    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

/// <summary>
/// Close-cockpit projection for the explicitly scoped monthly automated-journal work.
/// Counts are schedule-run counts, not posted-journal counts; every produced item remains
/// in the existing manual workbench for human approval and posting.
/// </summary>
public sealed record AutomatedJournalScheduleStatusDto(
    string? FundProfileId,
    Guid? LedgerBookId,
    string? PeriodId,
    int ConfiguredCount,
    int EnabledCount,
    int FeeScheduleCount,
    int DividendScheduleCount,
    int DraftReadyCount,
    int NeedsInvestigationCount,
    int BlockedCount,
    AutomatedJournalScheduleStateDto State,
    string Summary,
    IReadOnlyList<OperationsEvidenceLinkDto>? EvidenceLinks = null,
    IReadOnlyList<string>? Blockers = null,
    IReadOnlyList<Guid>? JournalEntryIds = null,
    decimal? MinimumEvidenceConfidence = null,
    AutomatedJournalEvidenceQualityDto? LowestEvidenceQuality = null,
    int HumanReviewQueueCount = 0,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks { get; init; } = EvidenceLinks ?? [];

    public IReadOnlyList<string> Blockers { get; init; } = Blockers ?? [];

    public IReadOnlyList<Guid> JournalEntryIds { get; init; } = JournalEntryIds ?? [];
}

/// <summary>
/// Read-only projection consumed by close surfaces without depending on the scheduler host.
/// </summary>
public interface IAutomatedJournalScheduleStatusSource
{
    Task<AutomatedJournalScheduleStatusDto> GetStatusAsync(
        string? fundProfileId,
        Guid? ledgerBookId,
        string? periodId,
        CancellationToken ct = default,
        string? tenantId = null,
        string? companyId = null,
        string? entityId = null);
}
