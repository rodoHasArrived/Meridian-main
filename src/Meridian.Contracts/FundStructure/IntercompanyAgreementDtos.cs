using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementTypeDto>))]
public enum IntercompanyAgreementTypeDto { Loan, ManagementFee, CostShare, CashSweep, Guarantee, Other }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementActionDto>))]
public enum IntercompanyAgreementActionDto { Activate, Amend, Terminate }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementProposalStateDto>))]
public enum IntercompanyAgreementProposalStateDto { Draft, Submitted, Approved, Rejected, Withdrawn }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementStatusDto>))]
public enum IntercompanyAgreementStatusDto { Unapproved, Scheduled, Active, Expired, Terminated }

public sealed record IntercompanyAgreementTermsDto(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    decimal? FixedAnnualRate = null, string? RateDescription = null, string? ScheduleDescription = null,
    string? AllocationDescription = null, int? SettlementDays = null, string? Notes = null);

/// <summary>Submitted references are resolved by the evidence authority; supplied hashes are never trusted.</summary>
public sealed record IntercompanyAgreementEvidenceDto(string Reference, string? ArtifactId = null, string? ContentHash = null);

public sealed record IntercompanyAgreementDraftDto(
    string Code, string Name, Guid PartyOneId, string PartyOneRole, Guid PartyTwoId, string PartyTwoRole,
    IntercompanyAgreementTypeDto AgreementType, string Currency, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null, IntercompanyAgreementTermsDto? Terms = null,
    IReadOnlyList<IntercompanyAgreementEvidenceDto>? Evidence = null,
    IntercompanyAgreementActionDto Action = IntercompanyAgreementActionDto.Activate,
    DateOnly? TerminationEffectiveDate = null, string? Purpose = null);

/// <summary>Actor and tenant are deliberately absent: authenticated service context supplies both.</summary>
public sealed record IntercompanyAgreementCommandDto(
    Guid CommandId, Guid AgreementId, long ExpectedAggregateVersion, Guid ProposalId,
    IntercompanyAgreementDraftDto? Draft = null, string? Rationale = null, string? SubmittedHash = null);

public sealed record IntercompanyAgreementProposalDto(
    Guid ProposalId, IntercompanyAgreementActionDto Action, IntercompanyAgreementProposalStateDto State,
    IntercompanyAgreementDraftDto Draft, int? BaseAgreementVersion, IReadOnlyList<string> Authors,
    string? Submitter, DateTimeOffset? SubmittedAtUtc, string? SubmittedHash,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, string? Rationale);

public sealed record IntercompanyAgreementRevisionDto(
    Guid AgreementId, int AgreementVersion, int? PredecessorAgreementVersion, int? BaseAgreementVersion,
    IntercompanyAgreementActionDto Action, IntercompanyAgreementDraftDto Terms,
    Guid ProposalId, string PayloadHash, IReadOnlyList<string> Authors,
    string Submitter, string Approver, DateTimeOffset ApprovedAtUtc, string Rationale);

public sealed record IntercompanyAgreementAuditDto(
    Guid EventId, Guid ProposalId, string Action, string Actor, DateTimeOffset RecordedAtUtc,
    long AggregateVersion, string? Rationale, IntercompanyAgreementDraftDto? Snapshot = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? MigrationWarnings { get; init; }
}

public sealed record IntercompanyAgreementCapabilitiesDto(
    bool CanEdit, bool CanSubmit, bool CanApprove, bool CanReject, bool CanWithdraw,
    bool CanAmend, bool CanTerminate, string? BlockingReason = null)
{
    public bool CanCreateProposal { get; init; }
}

public sealed record IntercompanyAgreementDetailDto(
    Guid AgreementId, long AggregateVersion, string Code, string Name,
    Guid PartyOneId, string PartyOneRole, Guid PartyTwoId, string PartyTwoRole,
    IntercompanyAgreementTypeDto AgreementType, string Currency, DateOnly AsOf,
    IntercompanyAgreementStatusDto EffectiveStatus, int? LatestApprovedVersion,
    IntercompanyAgreementRevisionDto? ApplicableRevision,
    IReadOnlyList<IntercompanyAgreementProposalDto> Proposals,
    IReadOnlyList<IntercompanyAgreementRevisionDto> Revisions,
    IReadOnlyList<IntercompanyAgreementAuditDto> History,
    IntercompanyAgreementCapabilitiesDto Capabilities)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? MigrationWarnings { get; init; }
}

public sealed record IntercompanyAgreementCommandReceiptDto(
    Guid CommandId, string Action, string PayloadHash, string Actor, DateTimeOffset RecordedAtUtc,
    long AggregateVersion, IntercompanyAgreementDetailDto Detail);
public sealed record IntercompanyAgreementCommandResultDto(
    IntercompanyAgreementDetailDto Detail, IntercompanyAgreementCommandReceiptDto Receipt, bool Replayed = false);
public sealed record IntercompanyAgreementQueryDto(
    Guid? EntityId = null, IntercompanyAgreementTypeDto? AgreementType = null,
    IntercompanyAgreementStatusDto? Status = null, DateOnly? AsOf = null,
    DateTimeOffset? KnownAtUtc = null, string? Cursor = null, int PageSize = 50);
public sealed record IntercompanyAgreementListDto(IReadOnlyList<IntercompanyAgreementDetailDto> Items, string? NextCursor)
{
    public bool CanCreate { get; init; }
    public bool ReferencePostingSupported { get; init; }
}

public sealed record IntercompanyAgreementReviewPayloadDto(
    IntercompanyAgreementDraftDto Draft, int? BaseAgreementVersion, int? PredecessorAgreementVersion, string? Rationale)
{
    public Guid AgreementId { get; init; }
    public Guid ProposalId { get; init; }
}

/// <summary>Trusted server context, never deserialize this from a command body.</summary>
public sealed record IntercompanyAgreementCallContext(string TenantId, string Actor);

public class IntercompanyAgreementValidationException(string message) : InvalidOperationException(message);
public sealed class IntercompanyAgreementConflictException(string message) : IntercompanyAgreementValidationException(message);
public sealed class IntercompanyAgreementNotFoundException() : IntercompanyAgreementValidationException("Agreement or party was not found.");
public sealed class IntercompanyAgreementForbiddenException(string message) : IntercompanyAgreementValidationException(message);
