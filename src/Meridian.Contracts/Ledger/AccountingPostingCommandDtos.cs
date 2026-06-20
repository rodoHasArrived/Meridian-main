using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Ledger;

[JsonConverter(typeof(JsonStringEnumConverter<AccountingPostingIntentDto>))]
public enum AccountingPostingIntentDto
{
    Originating = 0,
    Adjustment = 1,
    Reversal = 2,
    Rebook = 3,
    Restatement = 4,
    AutomatedDraft = 5
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingPostingApprovalStateDto>))]
public enum AccountingPostingApprovalStateDto
{
    NotRequired = 0,
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<AccountingPostingEvidenceKindDto>))]
public enum AccountingPostingEvidenceKindDto
{
    Source = 0,
    Approval = 1,
    Reconciliation = 2,
    Settlement = 3,
    PeriodLock = 4,
    OperatorRationale = 5,
    Correction = 6,
    ReportOutput = 7,
    AuditSupport = 8
}

public sealed record AccountingPostingEvidenceReferenceDto(
    string EvidenceId,
    string Uri,
    AccountingPostingEvidenceKindDto Kind,
    string SourceSystem,
    DateTimeOffset RetainedAtUtc,
    string RetainedBy,
    string? SubjectId = null,
    string? ContentHash = null,
    string? Description = null);

public sealed record AccountingPostingCommandDto(
    Guid CommandId,
    Guid AggregateId,
    Guid PeriodId,
    DateOnly EffectiveDate,
    DateTimeOffset PostingDate,
    string IdempotencyKey,
    AccountingPostingIntentDto Intent = AccountingPostingIntentDto.Originating,
    Guid? SourceEventId = null,
    Guid? CorrelationId = null,
    Guid? CausationId = null,
    Guid? SourceJournalEntryId = null,
    long? ExpectedVersion = null,
    string? SourceEventType = null,
    TreasuryLedgerContextDto? TreasuryContext = null,
    AccountingPostingApprovalStateDto ApprovalState = AccountingPostingApprovalStateDto.Pending,
    string? ApprovalId = null,
    string? OperatorRationale = null,
    IReadOnlyList<AccountingPostingEvidenceReferenceDto>? Evidence = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator)
{
    public IReadOnlyList<AccountingPostingEvidenceReferenceDto> Evidence { get; init; } =
        Evidence ?? [];
}
