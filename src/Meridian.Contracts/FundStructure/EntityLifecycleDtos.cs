using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

/// <summary>Governed operation being assessed; source evidence must establish the operation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EntityLifecycleOperationDto>))]
public enum EntityLifecycleOperationDto
{
    GeneralPosting,
    Settlement,
    ClosingExpense,
    Tax,
    Revaluation,
    Correction,
    NewAgreement,
    AgreementSettlementAmendment,
    Read,
    Consolidation
}

/// <summary>One non-overlapping, half-open effective status window.</summary>
public sealed record EntityLifecycleStateDto(
    Guid TransitionId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    LegalEntityLifecycleStatusDto Status,
    LegalEntityLifecycleEventKindDto EventKind,
    Guid? SuccessorEntityId = null);

/// <summary>An immutable complete timeline; retained decisions pin its revision and hash.</summary>
public sealed record EntityLifecycleRevisionDto(
    string TenantId,
    Guid EntityId,
    long Revision,
    DateTimeOffset RecordedAtUtc,
    string RecordedBy,
    string Reason,
    string EvidenceReference,
    string PreviousHash,
    string ContentHash,
    IReadOnlyList<EntityLifecycleStateDto> Timeline,
    bool LegacySnapshot = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IntercompanyExternalReviewAttestationDto? ExternalReview { get; init; }
}

public sealed record EntityLifecycleEligibilityDto(
    bool Allowed,
    string ReasonCode,
    string Message,
    LegalEntityLifecycleStatusDto? Status,
    long? Revision,
    bool Warning = false);

public sealed record AppendEntityLifecycleRevisionRequestDto(
    Guid EntityId,
    long ExpectedRevision,
    IReadOnlyList<EntityLifecycleStateDto> Timeline,
    string Reason,
    string EvidenceReference)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IntercompanyExternalReviewDto? ExternalReview { get; init; }
}
