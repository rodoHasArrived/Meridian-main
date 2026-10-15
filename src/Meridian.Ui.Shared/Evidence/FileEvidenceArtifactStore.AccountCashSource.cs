using Meridian.Contracts.Integrity;

namespace Meridian.Ui.Shared.Evidence;

public sealed partial class FileEvidenceArtifactStore
{
    public async Task<EvidenceManifestFile?> TryOpenDocumentContentAsync(string vaultId, string documentId,
        string tenantId, string scope, CancellationToken ct = default)
    {
        var identity = await TryGetVaultIdentityAsync(vaultId, tenantId, scope, ct).ConfigureAwait(false);
        if (identity is null || !TryResolveUniqueDocument(identity, documentId, out var document) || document is null
            || !TryResolveUniqueDocumentArtifact(identity, document, out var artifact) || artifact?.Document is null
            || !DocumentSemanticsMatch(document, artifact.Document) || !ManifestSnapshotDocumentMatches(identity, document)
            || !ArtifactContentMatchesDocument(artifact, document) || artifact.SizeBytes is < 1 or > 1048576)
            return null;
        var path = ResolveVaultArtifactPath(identity.VaultId, artifact.RelativePath);
        if (path is null)
            return null;
        try
        {
            // Parse the same copied bytes whose digest is verified; a pathname cannot rebind them.
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (file.Length != artifact.SizeBytes)
                return null;
            var bytes = new byte[(int)artifact.SizeBytes];
            await file.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            if (!Sha256Digest.FixedEquals(Sha256Digest.Compute(bytes), artifact.ContentHashSha256))
                return null;
            return new(new MemoryStream(bytes, writable: false), document.ContentType ?? "application/octet-stream",
                document.FileName, document.ReceivedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
