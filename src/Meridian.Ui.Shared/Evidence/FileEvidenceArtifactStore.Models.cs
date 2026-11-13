using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    private static void ValidatePreparerDisclosures(PreparerAttestationEvidenceProjectionDto? projection,
        string tenantId, string companyId)
    {
        if (projection is null)
            return;
        bool SameScope(SinglePrincipalAttestationScopeDto scope) =>
            string.Equals(scope.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(scope.CompanyId, companyId, StringComparison.OrdinalIgnoreCase);
        var entries = projection.Acknowledgments;
        if (projection.SchemaVersion != 1 || !SameScope(projection.Scope)
            || projection.Disclosure != SinglePrincipalAttestationPolicy.Disclosure
            || projection.Boundary != PreparerAttestationEvidenceProjectionService.Boundary || entries is null
            || entries.Any(entry => !SameScope(entry.Receipt.Scope) || !SameScope(entry.OriginalDeclaration.Scope)
                || entry.Receipt.DeclarationId != entry.OriginalDeclaration.DeclarationId
                || entry.Receipt.DeclarationRevision != entry.OriginalDeclaration.Revision
                || entry.Receipt.SchemaVersion != SinglePrincipalAttestationPolicy.SchemaVersion
                || entry.Receipt.DecisionKind != SinglePrincipalAttestationPolicy.DecisionKind
                || entry.Receipt.Disclosure != SinglePrincipalAttestationPolicy.Disclosure))
            throw new InvalidOperationException("Preparer evidence disclosures do not match the retained export's scope and classification.");
        if (projection.Availability == "Unavailable")
        {
            if (entries.Count != 0 || projection.RecordedReceiptCount is not null || projection.UnrevokedReceiptCount is not null
                || projection.RevokedReceiptCount is not null || projection.SyntheticReceiptCount is not null
                || projection.UnclassifiedReceiptCount is not null)
                throw new InvalidOperationException("Unavailable preparer evidence history cannot claim receipt counts.");
            return;
        }
        var revoked = entries.Count(entry => entry.Revocation is not null);
        var synthetic = entries.Count(entry => string.Equals(entry.Receipt.SourceProvenance, "Synthetic", StringComparison.OrdinalIgnoreCase));
        if (projection.Availability != "Available" || projection.RecordedReceiptCount != entries.Count
            || projection.RevokedReceiptCount != revoked || projection.UnrevokedReceiptCount != entries.Count - revoked
            || projection.SyntheticReceiptCount != synthetic || projection.UnclassifiedReceiptCount != entries.Count - synthetic)
            throw new InvalidOperationException("Preparer evidence receipt counts do not match the frozen task inventory.");
    }

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
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public PreparerAttestationEvidenceProjectionDto? PreparerAttestations { get; init; }
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
