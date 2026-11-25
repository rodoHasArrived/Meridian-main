using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Services;

/// <summary>Resolves the accepted immutable source document supporting an externally reviewed entity fact.</summary>
public sealed class WorkstationEntityLifecycleExternalReviewEvidenceAuthority(
    IHttpContextAccessor accessor, IEvidenceArtifactStore evidenceStore) : IEntityLifecycleExternalReviewEvidenceAuthority
{
    public async Task<IntercompanyAgreementEvidenceDto> ResolveAsync(string tenantId, string actor, Guid entityId,
        IntercompanyAgreementEvidenceDto evidence, CancellationToken ct = default)
    {
        var http = accessor.HttpContext;
        var scope = http is null ? null : HttpContextWorkstationTenantContextAccessor.Resolve(http);
        if (scope is null || !scope.HasTenantScope || string.IsNullOrWhiteSpace(scope.CompanyId)
            || !string.Equals(scope.TenantId, tenantId, StringComparison.Ordinal)
            || !string.Equals(scope.Actor, actor, StringComparison.Ordinal) || entityId == Guid.Empty)
            throw new IntercompanyAgreementForbiddenException("An authenticated tenant and company scoped operator is required for lifecycle review evidence.");
        if (evidence is null || !EvidenceVaultReference.TryParseCanonical(evidence.Reference, out var vaultId))
            throw new IntercompanyAgreementValidationException("Select a canonical retained lifecycle Evidence Vault reference.");
        var identity = await evidenceStore.TryGetVaultIdentityAsync(vaultId, tenantId, scope.CompanyId, ct).ConfigureAwait(false);
        if (identity is null || !string.Equals(identity.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identity.Scope, scope.CompanyId, StringComparison.OrdinalIgnoreCase))
            throw new IntercompanyAgreementValidationException("Lifecycle review evidence is unavailable in this tenant and company.");
        var artifacts = identity.Artifacts.Where(artifact => artifact.Document is not null
            && (string.IsNullOrWhiteSpace(evidence.ArtifactId) || artifact.ArtifactId == evidence.ArtifactId)).ToArray();
        if (artifacts.Length != 1)
            throw new IntercompanyAgreementValidationException("Select one exact retained lifecycle document artifact.");
        var artifact = artifacts[0];
        var document = artifact.Document!;
        if (!string.Equals(document.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(document.Scope, scope.CompanyId, StringComparison.OrdinalIgnoreCase)
            || document.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted
            || document.Classification != EvidenceDocumentClassificationDto.Agreement
            || !document.ObjectLinks.Any(link => link.LinkKind == EvidenceDocumentLinkKindDto.LegalEntity
                && Guid.TryParse(link.ObjectId, out var linked) && linked == entityId)
            || !Sha256Digest.IsCanonical(artifact.ContentHashSha256)
            || !Sha256Digest.FixedEquals(artifact.ContentHashSha256, document.SourceHashSha256)
            || (!string.IsNullOrWhiteSpace(evidence.ContentHash) && !Sha256Digest.FixedEquals(evidence.ContentHash, artifact.ContentHashSha256)))
            throw new IntercompanyAgreementValidationException("Lifecycle evidence must be an accepted agreement document linked to this exact entity with unchanged source bytes.");
        if (!await evidenceStore.VerifyDocumentContentAsync(vaultId, document.DocumentId, artifact.ArtifactId,
            artifact.ContentHashSha256, tenantId, scope.CompanyId, ct).ConfigureAwait(false))
            throw new IntercompanyAgreementValidationException("The exact retained lifecycle review document failed content verification.");
        return new(EvidenceVaultReference.CanonicalPrefix + vaultId, artifact.ArtifactId, artifact.ContentHashSha256);
    }
}
