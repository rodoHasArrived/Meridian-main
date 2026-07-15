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
    Failed = 6
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
    IReadOnlyList<string> Blockers);

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
        CancellationToken ct = default);
}
