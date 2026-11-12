using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Intercompany;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>Checks the exact reviewed signed source and its retained bytes at agreement approval.</summary>
public sealed class EvidenceVaultIntercompanyAgreementEvidenceAuthority(IEvidenceArtifactStore vault)
    : IIntercompanyAgreementEvidenceAuthority
{
    public async Task VerifyAsync(string tenantId, Guid agreementId, IntercompanySignedDocumentDto document, CancellationToken ct = default)
    {
        var identity = await vault.TryGetVaultIdentityAsync(document.VaultId, tenantId, document.Scope, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Signed agreement evidence was not found in the current tenant and scope.");
        var retained = identity.Documents.SingleOrDefault(x => x.DocumentId == document.DocumentId && x.ArtifactId == document.ArtifactId)
            ?? throw new InvalidOperationException("The exact signed agreement document/artifact identity is missing.");
        if (!string.Equals(retained.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(retained.SourceHashSha256, document.ContentHashSha256, StringComparison.OrdinalIgnoreCase) ||
            retained.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted ||
            retained.Classification != EvidenceDocumentClassificationDto.Agreement ||
            !retained.ObjectLinks.Any(x => x.LinkKind == EvidenceDocumentLinkKindDto.IntercompanyAgreement &&
                Guid.TryParse(x.ObjectId, out var linked) && linked == agreementId))
            throw new InvalidOperationException("The signed document must have accepted review and the exact retained source hash.");
        if (!await vault.VerifyDocumentContentAsync(document.VaultId, document.DocumentId, document.ArtifactId,
            document.ContentHashSha256, tenantId, document.Scope, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The retained signed agreement bytes failed hash verification.");
    }
}
