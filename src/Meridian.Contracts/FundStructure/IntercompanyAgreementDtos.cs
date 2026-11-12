using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementKindDto>))]
public enum IntercompanyAgreementKindDto { Loan, CostRecharge, ManagementFee, ExpenseShare, Guarantee }

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementStatusDto>))]
public enum IntercompanyAgreementStatusDto { Draft, Submitted, Approved, Rejected, Terminated, Novated }

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAgreementActionDto>))]
public enum IntercompanyAgreementActionDto { Create, Revise, Submit, Approve, Reject, Terminate, Novate }

/// <summary>One party's explicit book and stable chart node identities; paths are resolved from configuration.</summary>
public sealed record IntercompanyPartyMappingDto(
    Guid EntityId, Guid LedgerBookId, string ReceivableAccountNodeId, string PayableAccountNodeId,
    string? SettlementAccountNodeId = null, string? RevenueAccountNodeId = null, string? ExpenseAccountNodeId = null);

/// <summary>Pins signed source bytes in the tenant-scoped Evidence Vault, rather than a mutable URL.</summary>
public sealed record IntercompanySignedDocumentDto(
    string VaultId, string DocumentId, string ArtifactId, string ContentHashSha256, string Scope);

public sealed record IntercompanyEntityLifecycleReferenceDto(Guid EntityId, long Revision, string ContentHash);

/// <summary>Immutable terms identity. Version fences the whole agreement; RevisionId identifies exact terms.</summary>
public sealed record IntercompanyAgreementDto(
    Guid AgreementId, Guid OrganizationId, string FacilityReference, IntercompanyAgreementKindDto Kind,
    Guid PartyOneEntityId, Guid PartyTwoEntityId, string PartyOneRole, string PartyTwoRole,
    string Currency, string SettlementCadence, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    Guid RevisionId, int RevisionNumber, long Version, IntercompanyAgreementStatusDto Status,
    IReadOnlyList<IntercompanyPartyMappingDto> PartyMappings, IntercompanySignedDocumentDto? SignedDocument,
    string CreatedBy, DateTimeOffset CreatedAt, string UpdatedBy, DateTimeOffset UpdatedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, DateTimeOffset? TerminatedAt = null,
    Guid? PredecessorRevisionId = null, Guid? SuccessorAgreementId = null,
    IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>? LifecycleEvidence = null,
    Guid? SupersedesRevisionId = null)
{
    /// <summary>Contractual calendar-day offset from source economic date; null means no governed due date is recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SettlementDueDays { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? NovationSuccessorRevisionId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? NovationSuccessorVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>? NovationSuccessorLifecycleEvidence { get; init; }
}

/// <summary>One immutable append and its exact idempotent result, ordered by aggregate version.</summary>
public sealed record IntercompanyAgreementEventDto(
    Guid RequestId, string RequestDigest, IntercompanyAgreementActionDto Action, string Actor,
    DateTimeOffset RecordedAt, string? Reason, IntercompanyAgreementDto Snapshot,
    string PreviousHash = "", string ContentHash = "",
    Guid? TargetRevisionId = null, IntercompanyAgreementDto? TargetRevisionSnapshot = null)
{
    /// <summary>The revision affected by the event; Snapshot always remains the current authoring projection.</summary>
    [JsonIgnore] public IntercompanyAgreementDto AffectedRevision => TargetRevisionSnapshot ?? Snapshot;
}

public sealed record IntercompanyAgreementHistoryDto(IReadOnlyList<IntercompanyAgreementEventDto> Events)
{
    public IntercompanyAgreementDto Current => Events.Count > 0 ? Events[^1].Snapshot
        : throw new InvalidOperationException("Agreement history is empty.");
}

public sealed record CreateIntercompanyAgreementRequest(
    Guid RequestId, Guid AgreementId, Guid OrganizationId, string FacilityReference,
    IntercompanyAgreementKindDto Kind, Guid PartyOneEntityId, Guid PartyTwoEntityId,
    string PartyOneRole, string PartyTwoRole, string Currency, string SettlementCadence,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    IReadOnlyList<IntercompanyPartyMappingDto> PartyMappings, IntercompanySignedDocumentDto? SignedDocument = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SettlementDueDays { get; init; }
}

public sealed record ReviseIntercompanyAgreementRequest(
    Guid RequestId, long ExpectedVersion, string FacilityReference, string PartyOneRole, string PartyTwoRole,
    string Currency, string SettlementCadence, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo,
    IReadOnlyList<IntercompanyPartyMappingDto> PartyMappings, IntercompanySignedDocumentDto? SignedDocument,
    string Reason)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SettlementDueDays { get; init; }
}

public sealed record IntercompanyAgreementActionRequest(Guid RequestId, long ExpectedVersion, string? Reason = null,
    DateTimeOffset? EffectiveAt = null, Guid? SuccessorAgreementId = null,
    Guid? RevisionId = null, Guid? SuccessorRevisionId = null, long? ExpectedSuccessorVersion = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IntercompanyAgreementDto))]
[JsonSerializable(typeof(IntercompanyAgreementDto[]))]
[JsonSerializable(typeof(IntercompanyAgreementHistoryDto))]
[JsonSerializable(typeof(IntercompanyAgreementHistoryDto[]))]
[JsonSerializable(typeof(IntercompanyAgreementEventDto))]
[JsonSerializable(typeof(CreateIntercompanyAgreementRequest))]
[JsonSerializable(typeof(ReviseIntercompanyAgreementRequest))]
[JsonSerializable(typeof(IntercompanyAgreementActionRequest))]
public sealed partial class IntercompanyContractsJsonContext : JsonSerializerContext;
