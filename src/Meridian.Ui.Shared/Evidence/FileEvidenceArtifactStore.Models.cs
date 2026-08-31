using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    private sealed record RetainedEvidenceManifestDto(
        int SchemaVersion,
        DateTimeOffset ExportedAt,
        string? RequestedBy,
        string? Reason,
        bool ManifestOnly,
        EvidenceSubjectDto Subject,
        EvidenceCompletenessDto Completeness,
        IReadOnlyList<EvidenceNodeDto> Nodes,
        IReadOnlyList<EvidenceEdgeDto> Edges,
        IReadOnlyList<WorkflowActionDto> Actions,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<EvidenceRequestListDto> RequestLists,
        IReadOnlyList<EvidenceSupportRequestDto> SupportRequests,
        EvidenceVaultIdentityDto? VaultIdentity,
        EvidenceLifecycleMetadataDto? Lifecycle,
        EvidenceSubjectLinkageDto? Linkage)
    {
        public string? TenantId { get; init; }
        public string? Scope { get; init; }
    }

    private sealed record EvidenceRequestListTarget(
        string RequestListKind,
        EvidenceRequestListKindDto RequestListKindCode,
        string TargetKind,
        string TargetId);

    private sealed record DocumentListCandidate(
        EvidenceVaultIdentityDto Locator,
        EvidenceDocumentDto IndexedDocument);

    private sealed record VerifiedDocumentCandidate(
        EvidenceVaultIdentityDto Identity,
        EvidenceDocumentDto Document,
        EvidenceVaultArtifactDto Artifact);
}
