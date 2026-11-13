using System.Text.Json.Serialization;

namespace Meridian.Contracts.Ledger;

/// <summary>A reporting cutover into a new dedicated book; never a legal novation or source rewrite.</summary>
public sealed record ConsolidationTransitionRequestDto(
    Guid TransitionId, Guid OrganizationId, Guid OwnershipRootId, Guid PreviousEliminationBookId,
    Guid NewEliminationBookId, Guid NewPeriodId, DateOnly CutoverDate, string Reason, string EvidenceReference);

/// <summary>Retained server preview; approval accepts only its exact current fingerprint.</summary>
public sealed record ConsolidationTransitionPreviewDto(
    Guid PreviewId, ConsolidationTransitionRequestDto Request, long RootVersion, Guid? PreviousTransitionId,
    string TenantId, string? CompanyId, string PreparedBy, DateTimeOffset PreparedAt,
    string PreviousBookEvidenceDigest, IReadOnlyList<Guid> PreviousPostedJournalIds,
    DateOnly? PreviousLatestPostedAsOf, IReadOnlyList<string> PreviousEntityIds,
    ConsolidationEvidenceDto NewEvidence, IReadOnlyList<string> JoinedEntityIds,
    IReadOnlyList<string> RemovedEntityIds, IReadOnlyList<string> Blockers, string Fingerprint)
{
    public bool CanApprove => Blockers.Count == 0;
}

public sealed record ConsolidationTransitionLineageDto(Guid TransitionId, string ContentHash,
    DateOnly CutoverDate, Guid PreviousEliminationBookId);

public sealed record ApproveConsolidationTransitionRequestDto(Guid PreviewId, string PreviewFingerprint);

/// <summary>Immutable independently approved route and exact reviewed operands.</summary>
public sealed record ConsolidationTransitionReceiptDto(
    Guid TransitionId, Guid PreviewId, long RootVersion, Guid? PreviousTransitionId,
    ConsolidationTransitionRequestDto Request, string TenantId, string? CompanyId,
    string PreparedBy, string ApprovedBy, DateTimeOffset ApprovedAt, string PreviewFingerprint,
    string PreviousBookEvidenceDigest, IReadOnlyList<Guid> PreviousPostedJournalIds,
    DateOnly? PreviousLatestPostedAsOf, IReadOnlyList<string> PreviousEntityIds,
    ConsolidationEvidenceDto NewEvidence, string ContentHash);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConsolidationTransitionRequestDto))]
[JsonSerializable(typeof(ConsolidationTransitionPreviewDto))]
[JsonSerializable(typeof(ApproveConsolidationTransitionRequestDto))]
[JsonSerializable(typeof(ConsolidationTransitionReceiptDto))]
[JsonSerializable(typeof(IReadOnlyList<ConsolidationTransitionPreviewDto>))]
[JsonSerializable(typeof(IReadOnlyList<ConsolidationTransitionReceiptDto>))]
public sealed partial class ConsolidationTransitionJsonContext : JsonSerializerContext;
