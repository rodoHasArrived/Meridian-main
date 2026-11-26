using Meridian.Contracts.Integrity;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    public async Task<bool> VerifyDocumentContentAsync(string vaultId, string documentId, string artifactId,
        string contentHashSha256, string tenantId, string scope, CancellationToken ct = default)
    {
        var identity = await TryGetVaultIdentityAsync(vaultId, tenantId, scope, ct).ConfigureAwait(false);
        if (identity is null || !TryResolveUniqueDocument(identity, documentId, out var document) || document is null ||
            !TryResolveUniqueDocumentArtifact(identity, document, out var artifact) || artifact?.Document is null ||
            artifact.ArtifactId != artifactId || !Sha256Digest.FixedEquals(document.SourceHashSha256, contentHashSha256) ||
            !Sha256Digest.FixedEquals(artifact.ContentHashSha256, contentHashSha256) ||
            artifact.SizeBytes < 1 || artifact.SizeBytes > MaxRetainedArtifactBytes || artifact.SizeBytes > _documentVerificationByteLimit ||
            !DocumentSemanticsMatch(document, artifact.Document) || !ManifestSnapshotDocumentMatches(identity, document) ||
            !ArtifactContentMatchesDocument(artifact, document))
            return false;
        return await HasVerifiedRetainedDocumentContentAsync(new VerifiedDocumentCandidate(identity, document, artifact), ct).ConfigureAwait(false);
    }
}
