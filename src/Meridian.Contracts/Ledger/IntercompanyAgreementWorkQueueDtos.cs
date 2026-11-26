using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementWorkItemStateDto>))]
public enum IntercompanyAgreementWorkItemStateDto { Open, AwaitingReview, Resolved, Reopened }

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementWorkItemActionDto>))]
public enum IntercompanyAgreementWorkItemActionDto { AssignOwner, AddNote, AwaitReview, Resolve, Reopen }

/// <summary>One retained operator action. A queue decision never approves legal terms or a journal.</summary>
public sealed record IntercompanyAgreementWorkItemHistoryDto(
    Guid CommandId, long Version, string RecordHash, string Action,
    IntercompanyAgreementWorkItemStateDto State, string Actor, DateTimeOffset RecordedAtUtc,
    string Reason, string? Owner, string? Note, IReadOnlyList<string> EvidenceReferences,
    DateOnly ObservedAsOf, string SourceFingerprint)
{
    public IReadOnlyList<IntercompanyObligationSourceVersionDto> SourceVersions { get; init; } = [];
}

/// <summary>
/// Stable case identity is independent of amount, horizon, observation date and approved revision.
/// DueDate identifies a distinct obligation boundary. AgeDays measures retained first observation,
/// while DaysOverdue measures the explicit due date against the selected as-of date.
/// </summary>
public sealed record IntercompanyAgreementWorkItemDto(
    string CaseId, Guid LedgerBookId, Guid AgreementId, int? AgreementVersion,
    string FindingCode, string Severity, string Message, string SuggestedAction,
    DateOnly? DueDate, DateOnly ObservedAsOf, bool IsCurrentlyObserved, bool IsTracked,
    IntercompanyAgreementWorkItemStateDto State, string? Owner,
    DateTimeOffset? FirstSeenAtUtc, DateTimeOffset? LastObservedAtUtc,
    int? AgeDays, int? DaysOverdue, string LastObservedSourceFingerprint,
    long Version, string? RecordHash, IReadOnlyList<IntercompanyAgreementWorkItemHistoryDto> History);

/// <summary>Approved legal disclosure only. A declared cap is never a measured exposure.</summary>
public sealed record IntercompanyGuaranteeDisclosureDto(
    Guid AgreementId, string Code, string Name, int AgreementVersion, string PayloadHash,
    Guid PartyOneId, string PartyOneRole, Guid PartyTwoId, string PartyTwoRole,
    string Currency, string? BeneficiaryReference,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? LimitAmount,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, DateOnly? TerminationEffectiveDate,
    IntercompanyAgreementStatusDto EffectiveStatus,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] decimal? CurrentExposure,
    string ExposureStatus, string ExposureUnavailableReason);

/// <summary>Read-only observation plus durable case history. Missing findings never resolve retained cases.</summary>
public sealed record IntercompanyAgreementWorkQueueDto(
    DateOnly AsOf, int ExpiryHorizonDays, DateTimeOffset EvaluatedAtUtc,
    string Status, bool IsComplete, string SourceFingerprint,
    IReadOnlyList<IntercompanyAgreementWorkItemDto> Cases,
    IReadOnlyList<IntercompanyGuaranteeDisclosureDto> Guarantees,
    IReadOnlyList<string> Diagnostics)
{
    public Guid LedgerBookId { get; init; }
    public string? FundProfileId { get; init; }
    /// <summary>Host permission projection; command authorization never trusts this value.</summary>
    public bool CanManage { get; init; }
}

/// <summary>Explicitly tracks or refreshes one observed finding; GET never appends history.</summary>
public sealed record IntercompanyAgreementWorkQueueObserveRequestDto(
    Guid CommandId, string CaseId, string ExpectedSourceFingerprint, string Reason,
    long ExpectedVersion = 0, string? ExpectedRecordHash = null);

/// <summary>Compare-and-append case action against the exact current source observation and retained case.</summary>
public sealed record IntercompanyAgreementWorkQueueUpdateRequestDto(
    Guid CommandId, string CaseId, IntercompanyAgreementWorkItemActionDto Action,
    long ExpectedVersion, string ExpectedRecordHash, string ExpectedSourceFingerprint,
    string Reason, string? Owner = null, string? Note = null,
    IReadOnlyList<string>? EvidenceReferences = null);

public interface IIntercompanyAgreementWorkQueueService
{
    Task<IntercompanyAgreementWorkQueueDto> GetAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto scope, CancellationToken ct = default);
    Task<IntercompanyAgreementWorkItemDto> ObserveAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueObserveRequestDto request, CancellationToken ct = default);
    Task<IntercompanyAgreementWorkItemDto> UpdateAsync(IntercompanyAgreementCallContext context,
        string? companyId, IntercompanyObligationMonitorRequestDto scope,
        IntercompanyAgreementWorkQueueUpdateRequestDto request, CancellationToken ct = default);
}

public sealed class IntercompanyAgreementWorkQueueConflictException(string message) : InvalidOperationException(message);
public sealed class IntercompanyAgreementWorkQueueUnavailableException(string message) : InvalidOperationException(message);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IntercompanyAgreementWorkQueueDto))]
[JsonSerializable(typeof(IntercompanyAgreementWorkItemDto))]
[JsonSerializable(typeof(IntercompanyAgreementWorkQueueObserveRequestDto))]
[JsonSerializable(typeof(IntercompanyAgreementWorkQueueUpdateRequestDto))]
[JsonSerializable(typeof(string[]))]
public partial class IntercompanyAgreementWorkQueueJsonContext : JsonSerializerContext;
