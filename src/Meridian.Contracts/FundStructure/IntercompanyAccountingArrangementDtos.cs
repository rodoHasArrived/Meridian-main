using System.Text.Json.Serialization;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccountingTemplateKindDto>))]
/// <summary><c>Loan</c> mirrors principal only; <c>LoanInterest</c> is the separately reviewed interest treatment of a loan agreement.</summary>
public enum IntercompanyAccountingTemplateKindDto { Loan, CostRecharge, ManagementFee, ExpenseShare, CashSweep, Guarantee, Manual, LoanInterest }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccountingArrangementStatusDto>))]
public enum IntercompanyAccountingArrangementStatusDto { Draft, Submitted, Approved, Rejected, Retired }
[JsonConverter(typeof(JsonStringEnumConverter<IntercompanyAccountingArrangementActionDto>))]
public enum IntercompanyAccountingArrangementActionDto { Create, Revise, Submit, Approve, Reject, Retire }

public sealed record IntercompanyPartyMappingDto(Guid EntityId, Guid LedgerBookId,
    string ReceivableAccountNodeId, string PayableAccountNodeId, string? SettlementAccountNodeId = null,
    string? RevenueAccountNodeId = null, string? ExpenseAccountNodeId = null);
public sealed record IntercompanyEntityLifecycleReferenceDto(Guid EntityId, long Revision, string ContentHash);
public sealed record IntercompanyCanonicalAgreementReferenceDto(Guid AgreementId, int AgreementVersion, string PayloadHash);

/// <summary>Immutable accounting configuration linked to native legal authority; contains no editable legal terms.</summary>
public sealed record IntercompanyAccountingArrangementDto(
    Guid ArrangementId, Guid OrganizationId, string ArrangementReference, IntercompanyAccountingTemplateKindDto TemplateKind,
    Guid CanonicalAgreementId, int CanonicalAgreementVersion, string CanonicalPayloadHash,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, Guid RevisionId, int RevisionNumber, long Version,
    IntercompanyAccountingArrangementStatusDto Status, IReadOnlyList<IntercompanyPartyMappingDto> PartyMappings,
    string CreatedBy, DateTimeOffset CreatedAt, string UpdatedBy, DateTimeOffset UpdatedAt,
    string? ApprovedBy = null, DateTimeOffset? ApprovedAt = null, DateTimeOffset? RetiredAt = null,
    Guid? PredecessorRevisionId = null, IReadOnlyList<IntercompanyEntityLifecycleReferenceDto>? LifecycleEvidence = null,
    Guid? SupersedesRevisionId = null)
{
    [JsonIgnore]
    public IntercompanyCanonicalAgreementReferenceDto CanonicalReference =>
        new(CanonicalAgreementId, CanonicalAgreementVersion, CanonicalPayloadHash);
}

/// <summary>Read projection; all legal facts are derived from the exact native approved revision.</summary>
public sealed record ResolvedIntercompanyAccountingArrangementDto(
    IntercompanyAccountingArrangementDto Arrangement, IntercompanyAgreementRevisionDto LegalRevision)
{
    public Guid ArrangementId => Arrangement.ArrangementId;
    public Guid OrganizationId => Arrangement.OrganizationId;
    public string ArrangementReference => Arrangement.ArrangementReference;
    public IntercompanyAccountingTemplateKindDto TemplateKind => Arrangement.TemplateKind;
    public Guid RevisionId => Arrangement.RevisionId;
    public int RevisionNumber => Arrangement.RevisionNumber;
    public long Version => Arrangement.Version;
    public IntercompanyAccountingArrangementStatusDto Status => Arrangement.Status;
    public IReadOnlyList<IntercompanyPartyMappingDto> PartyMappings => Arrangement.PartyMappings;
    public DateTimeOffset EffectiveFrom => Arrangement.EffectiveFrom;
    public DateTimeOffset? EffectiveTo => Arrangement.EffectiveTo;
    public Guid CanonicalAgreementId => Arrangement.CanonicalAgreementId;
    public int CanonicalAgreementVersion => Arrangement.CanonicalAgreementVersion;
    public string CanonicalPayloadHash => Arrangement.CanonicalPayloadHash;
    public Guid PartyOneEntityId => LegalRevision.Terms.PartyOneId;
    public Guid PartyTwoEntityId => LegalRevision.Terms.PartyTwoId;
    public string PartyOneRole => LegalRevision.Terms.PartyOneRole;
    public string PartyTwoRole => LegalRevision.Terms.PartyTwoRole;
    public string Currency => LegalRevision.Terms.Currency;
    public string SettlementCadence => LegalRevision.Terms.Terms?.ScheduleDescription ?? string.Empty;
    public int? SettlementDueDays => LegalRevision.Terms.Terms?.SettlementDays;
    public IReadOnlyList<IntercompanyAgreementEvidenceDto> CanonicalEvidence => LegalRevision.Terms.Evidence ?? [];
}

public sealed record IntercompanyAccountingArrangementEventDto(Guid RequestId, string RequestDigest,
    IntercompanyAccountingArrangementActionDto Action, string Actor, DateTimeOffset RecordedAt, string? Reason,
    IntercompanyAccountingArrangementDto Snapshot, string PreviousHash = "", string ContentHash = "",
    Guid? TargetRevisionId = null, IntercompanyAccountingArrangementDto? TargetRevisionSnapshot = null)
{
    [JsonIgnore] public IntercompanyAccountingArrangementDto AffectedRevision => TargetRevisionSnapshot ?? Snapshot;
}
public sealed record IntercompanyAccountingArrangementHistoryDto(IReadOnlyList<IntercompanyAccountingArrangementEventDto> Events)
{
    public IntercompanyAccountingArrangementDto Current => Events.Count > 0 ? Events[^1].Snapshot
        : throw new InvalidOperationException("Accounting arrangement history is empty.");
}
public sealed record CreateIntercompanyAccountingArrangementRequest(Guid RequestId, Guid ArrangementId,
    Guid OrganizationId, string ArrangementReference, IntercompanyAccountingTemplateKindDto TemplateKind,
    Guid CanonicalAgreementId, int CanonicalAgreementVersion, string CanonicalPayloadHash,
    DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo, IReadOnlyList<IntercompanyPartyMappingDto> PartyMappings);
public sealed record ReviseIntercompanyAccountingArrangementRequest(Guid RequestId, long ExpectedVersion,
    string ArrangementReference, IntercompanyAccountingTemplateKindDto TemplateKind,
    int CanonicalAgreementVersion, string CanonicalPayloadHash, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo, IReadOnlyList<IntercompanyPartyMappingDto> PartyMappings, string Reason);
public sealed record IntercompanyAccountingArrangementActionRequest(Guid RequestId, long ExpectedVersion,
    string? Reason = null, DateTimeOffset? EffectiveAt = null, Guid? RevisionId = null);

/// <summary>Native legal read/lease adapter. Mapping approval cannot manufacture legal approval.</summary>
public interface IIntercompanyAccountingArrangementLegalAuthority
{
    Task<IntercompanyAgreementRevisionDto> GetRevisionAsync(string tenantId,
        IntercompanyCanonicalAgreementReferenceDto reference, CancellationToken ct = default);
    Task<IntercompanyAgreementRevisionDto> GetEffectiveRevisionAsync(string tenantId,
        IntercompanyCanonicalAgreementReferenceDto reference, DateOnly economicDate, CancellationToken ct = default);
    Task<IReadOnlyList<IntercompanyAgreementRevisionDto>> ListEffectiveRevisionsAsync(string tenantId,
        Guid entityId, DateOnly economicDate, CancellationToken ct = default);
    Task<IIntercompanyAccountingArrangementLegalLease> AcquireLeaseAsync(string tenantId,
        IReadOnlyList<IntercompanyCanonicalAgreementReferenceDto> references,
        LegalEntityReadAuthorityProof? structureAuthority = null, CancellationToken ct = default);
}
public interface IIntercompanyAccountingArrangementLegalLease : IAsyncDisposable
{
    IntercompanyAgreementRevisionDto ResolveExact(IntercompanyCanonicalAgreementReferenceDto reference);
    IntercompanyAgreementRevisionDto ResolveEffective(IntercompanyCanonicalAgreementReferenceDto reference, DateOnly economicDate);
    void ValidateWindow(IntercompanyCanonicalAgreementReferenceDto reference, DateTimeOffset from, DateTimeOffset? to);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(IntercompanyAccountingArrangementDto))]
[JsonSerializable(typeof(IntercompanyAccountingArrangementDto[]))]
[JsonSerializable(typeof(ResolvedIntercompanyAccountingArrangementDto))]
[JsonSerializable(typeof(ResolvedIntercompanyAccountingArrangementDto[]))]
[JsonSerializable(typeof(IntercompanyAccountingArrangementHistoryDto))]
[JsonSerializable(typeof(IntercompanyAccountingArrangementEventDto))]
[JsonSerializable(typeof(CreateIntercompanyAccountingArrangementRequest))]
[JsonSerializable(typeof(ReviseIntercompanyAccountingArrangementRequest))]
[JsonSerializable(typeof(IntercompanyAccountingArrangementActionRequest))]
[JsonSerializable(typeof(IntercompanyAgreementRevisionDto[]))]
public sealed partial class IntercompanyAccountingArrangementsJsonContext : JsonSerializerContext;
