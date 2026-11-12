using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>Resolves evidence acknowledgments without granting document review or accounting authority.</summary>
public sealed class EvidenceAttestationSubjectResolver(IEvidenceArtifactStore artifacts)
{
    public const string SubjectKind = "evidence-document";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the exact retained source and current document operands. The assertion's preparer
    /// is a separate task owner; the source creator/importer is never substituted for that owner.
    /// </summary>
    public async Task<EvidenceAttestationSubjectSnapshot?> ResolveAsync(
        string vaultId, string documentId, string tenantId, string companyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(vaultId) || string.IsNullOrWhiteSpace(documentId)
            || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
            return null;

        var identity = await artifacts.TryGetVaultIdentityAsync(vaultId, tenantId, companyId, ct).ConfigureAwait(false);
        if (identity is null || !SameScope(identity.TenantId, identity.Scope, tenantId, companyId))
            return null;
        var documents = identity.Documents.Where(document => document.DocumentId == documentId).ToArray();
        if (documents.Length != 1)
            return null;
        var document = documents[0];
        var sources = identity.Artifacts.Where(artifact => artifact.ArtifactId == document.ArtifactId).ToArray();
        if (sources.Length != 1 || !SameScope(document.TenantId, document.Scope, tenantId, companyId)
            || document.VaultId != identity.VaultId || document.SourceRecord is not { } source
            || !SameScope(source.TenantId, source.Scope, tenantId, companyId))
            return null;
        var artifact = sources[0];
        if (!Sha256Digest.IsCanonical(document.SourceHashSha256)
            || !Sha256Digest.FixedEquals(document.SourceHashSha256, artifact.ContentHashSha256)
            || !Sha256Digest.FixedEquals(document.SourceHashSha256, source.SourceHashSha256))
            return null;

        var content = await artifacts.TryOpenDocumentContentAsync(vaultId, documentId, tenantId, companyId, ct)
            .ConfigureAwait(false);
        if (content is null)
            return null;
        await using (content.Content.ConfigureAwait(false))
        {
            var contentHash = await Sha256Digest.ComputeAsync(content.Content, ct).ConfigureAwait(false);
            if (!Sha256Digest.FixedEquals(contentHash, document.SourceHashSha256))
                return null;
        }

        // Review metadata may change independently of source bytes. Bind those operands too,
        // and refuse a read whose source changed while the verified content was being opened.
        var current = await artifacts.TryGetVaultIdentityAsync(vaultId, tenantId, companyId, ct).ConfigureAwait(false);
        if (current is null || !SameScope(current.TenantId, current.Scope, tenantId, companyId)
            || !Sha256Digest.FixedEquals(identity.ContentHashSha256, current.ContentHashSha256))
            return null;

        var checks = new List<string>
        {
            "Retained source bytes match the document and artifact SHA-256.",
            "Document, source and vault belong to the authenticated tenant and company.",
            "Current document operands are bound separately from the immutable source bytes."
        };
        var eligible = document.ExtractionStatus is EvidenceExtractionStatusDto.Extracted or EvidenceExtractionStatusDto.Accepted
            && Enum.IsDefined(document.ReviewerState.Status)
            && document.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Rejected
            && document.ExtractedFields.Count > 0
            && document.ExtractedFields.All(field => field.ValidationStatus == EvidenceStatusDto.Ready
                && !string.IsNullOrWhiteSpace(field.ExtractedValue)
                && (field.ExpectedValue is null || field.ExpectedValue == field.ExtractedValue)
                && !HasUnresolvedFieldReviewState(field.ReviewState));
        checks.Add(eligible
            ? "All retained extraction checks are ready and consistent."
            : "Extraction support is missing, rejected, pending, conflicted, stale or unsupported.");
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Version = 1,
            identity.VaultId,
            identity.ContentHashSha256,
            Document = document,
            Artifact = artifact
        }, JsonOptions);
        var revision = Sha256Digest.ComputeUtf8(snapshotJson);
        // Fixture metadata may prove simulation; absence of that marker never proves live data.
        var provenance = document.ExtractedFields.Any(field => string.Equals(field.ReviewState, "Fixture", StringComparison.OrdinalIgnoreCase))
            ? "Synthetic"
            : "Unclassified";
        return new(SubjectKind, $"{identity.VaultId}/{document.DocumentId}", revision, revision,
            artifact.ArtifactId, document.SourceHashSha256, source.Actor, provenance, eligible, checks,
            identity.SubjectKind, identity.SubjectId, snapshotJson);
    }

    private static bool SameScope(string? actualTenant, string? actualCompany, string tenant, string company) =>
        string.Equals(actualTenant, tenant, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actualCompany, company, StringComparison.OrdinalIgnoreCase);

    private static bool HasUnresolvedFieldReviewState(string? state) =>
        string.IsNullOrWhiteSpace(state) || state.Trim().ToLowerInvariant() is
            "conflict" or "unsupported" or "stale" or "pending" or "rejected" or "blocked" or
            "missing" or "unknown" or "needsreview" or "reviewrequired";
}

public sealed record EvidenceAttestationSubjectSnapshot(
    string SubjectKind,
    string SubjectId,
    string Revision,
    string Hash,
    string SourceArtifactId,
    string SourceHash,
    string? SourceActor,
    string SourceProvenance,
    bool Eligible,
    IReadOnlyList<string> Checks,
    string? SourceSubjectKind = null,
    string? SourceSubjectId = null,
    string? SubjectSnapshotJson = null);
