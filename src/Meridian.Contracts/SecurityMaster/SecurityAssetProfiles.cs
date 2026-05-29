using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAssetProfileStatusDto>))]
public enum SecurityAssetProfileStatusDto
{
    Draft,
    Approved,
    Superseded,
    Retired
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAssetProfileFieldTypeDto>))]
public enum SecurityAssetProfileFieldTypeDto
{
    Text,
    Decimal,
    Integer,
    Boolean,
    Date,
    Enum,
    CurrencyCode,
    SecurityLink
}

[JsonConverter(typeof(JsonStringEnumConverter<SecurityAssetProfileAccountingImpactHintDto>))]
public enum SecurityAssetProfileAccountingImpactHintDto
{
    Valuation,
    LedgerClassification,
    CommitmentAccounting,
    NavBasedValuation,
    FactorSchedule,
    OwnershipPercentage,
    IncomeAccrual
}

public sealed record SecurityAssetProfileFieldDefinitionDto(
    string Key,
    string Label,
    SecurityAssetProfileFieldTypeDto FieldType,
    bool IsRequired,
    IReadOnlyList<string> AllowedValues,
    string? Description,
    decimal? MinValue,
    decimal? MaxValue,
    bool IsProjected,
    bool IsSearchable);

public sealed record SecurityAssetProfileDateOrderRuleDto(
    string StartFieldKey,
    string EndFieldKey,
    string Code,
    string Message);

public sealed record SecurityAssetProfileIdentifierPreferenceDto(
    SecurityIdentifierKind Kind,
    bool IsRequiredForClose,
    string Reason);

public sealed record SecurityAssetProfileDefinitionDto(
    string ProfileId,
    int Version,
    string Name,
    string Category,
    string? SubType,
    SecurityAssetProfileStatusDto Status,
    IReadOnlyList<SecurityAssetProfileFieldDefinitionDto> Fields,
    IReadOnlyList<SecurityAssetProfileIdentifierPreferenceDto> IdentifierPreferences,
    IReadOnlyList<string> LifecycleStates,
    IReadOnlyList<SecurityAssetProfileAccountingImpactHintDto> AccountingImpactHints,
    IReadOnlyList<SecurityAssetProfileDateOrderRuleDto> DateOrderRules,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string ChangeReason);

public sealed record SecurityAssetProfileTermsDto(
    string CustomProfileId,
    int ProfileVersion,
    IReadOnlyDictionary<string, JsonElement> ProfileFields,
    SecurityAssetProfileApprovalMetadataDto ProfileApproval,
    IReadOnlyList<SecurityEvidenceLinkDto> EvidenceLinks);

public sealed record SecurityAssetProfileApprovalMetadataDto(
    string ApprovedBy,
    DateTimeOffset ApprovedAtUtc,
    string ApprovalReference);

public sealed record SecurityAssetProfileDraftRequestDto(
    string ProfileId,
    string Name,
    string Category,
    string? SubType,
    IReadOnlyList<SecurityAssetProfileFieldDefinitionDto> Fields,
    IReadOnlyList<SecurityAssetProfileIdentifierPreferenceDto> IdentifierPreferences,
    IReadOnlyList<string> LifecycleStates,
    IReadOnlyList<SecurityAssetProfileAccountingImpactHintDto> AccountingImpactHints,
    IReadOnlyList<SecurityAssetProfileDateOrderRuleDto> DateOrderRules,
    string? RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record SecurityAssetProfileApprovalRequestDto(
    string ProfileId,
    int Version,
    DateOnly EffectiveFrom,
    string ApprovalReference,
    string? RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record SecurityAssetProfileRollbackRequestDto(
    string ProfileId,
    int TargetVersion,
    DateOnly EffectiveFrom,
    string ApprovalReference,
    string? RequestedBy,
    string Rationale,
    string? CorrelationId = null);

public sealed record SecurityAssetProfileGovernanceAuditEventDto(
    string AuditId,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Rationale,
    string CorrelationId,
    string ProfileId,
    int Version,
    SecurityAssetProfileStatusDto Status,
    int? PreviousVersion,
    string? ApprovalReference);

public sealed record SecurityAssetProfileLineageDto(
    string ProfileId,
    IReadOnlyList<SecurityAssetProfileDefinitionDto> Versions,
    IReadOnlyList<SecurityAssetProfileGovernanceAuditEventDto> AuditEvents);

public sealed record SecurityAssetProfileGovernanceResultDto(
    SecurityAssetProfileDefinitionDto Profile,
    SecurityAssetProfileLineageDto Lineage,
    SecurityAssetProfileGovernanceAuditEventDto AuditEvent);
