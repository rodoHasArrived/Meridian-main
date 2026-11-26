using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>Trusted server read authority, never a request-body contract.</summary>
public sealed record PreparerAttestationEvidenceReadContext(
    SinglePrincipalAttestationScopeDto Scope,
    SinglePrincipalAttestationAuthority Authority,
    Func<string, string, bool> CanReadSourceSubject);

/// <summary>
/// Adds separate evidence-task disclosures to exact visible source documents. It never changes
/// financial approval, evidence readiness, completeness, proof-layer coverage or Verified Coverage.
/// </summary>
public sealed class PreparerAttestationEvidenceProjectionService(
    SinglePrincipalAttestationStore attestations,
    IEvidenceArtifactStore artifacts,
    EvidenceAttestationSubjectResolver resolver)
{
    public const string Boundary = "Evidence acknowledgments only; no financial approval, independent review or Verified Coverage credit.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PreparerAttestationEvidenceProjectionDto?> ProjectAsync(
        EvidencePacketDto packet,
        PreparerAttestationEvidenceReadContext? context,
        CancellationToken ct = default)
    {
        if (context is null)
            return null;
        if (!context.Authority.CanRead)
            return Unavailable(context.Scope, "Permission to read preparer evidence assertions is unavailable.");
        if (packet.LedgerAmount is { } amount && !SameScope(context.Scope, amount.Scope.TenantId, amount.Scope.CompanyId))
            return Unavailable(context.Scope, "The amount and assertion scopes do not match.");

        SinglePrincipalAttestationStateDto state;
        try
        {
            state = await attestations.GetAsync(context.Scope, context.Authority, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is SinglePrincipalAttestationException or IOException
            or UnauthorizedAccessException or JsonException or TimeoutException)
        {
            return Unavailable(context.Scope, "Scoped preparer evidence history could not be verified.");
        }

        var visible = packet.Nodes.SelectMany(node => node.ArtifactRefs).ToArray();
        var entries = new List<PreparerEvidenceAcknowledgmentDto>();
        var current = new Dictionary<string, SinglePrincipalAttestationSubjectSnapshotDto?>(StringComparer.Ordinal);
        var identities = new Dictionary<string, EvidenceVaultIdentityDto?>(StringComparer.Ordinal);
        foreach (var receipt in state.Receipts)
        {
            ct.ThrowIfCancellationRequested();
            // Draft-check tasks are not acknowledgments of source documents and cannot be
            // propagated through a supporting-evidence graph.
            if (receipt.ActionId == SinglePrincipalAttestationPolicy.DraftCheck)
                continue;
            if (receipt.SourceSubjectKind is not { } sourceKind || receipt.SourceSubjectId is not { } sourceId
                || !TryReadBoundSource(receipt, out var source))
                return Unavailable(context.Scope, "An assertion's retained source binding could not be verified.");
            if (!MatchesVisibleSource(visible, receipt, source!))
                continue;
            if (!context.CanReadSourceSubject(sourceKind, sourceId))
                return Unavailable(context.Scope, "A source's current preparer-evidence read authority is unavailable.");

            var declaration = state.DeclarationHistory.SingleOrDefault(item =>
                item.DeclarationId == receipt.DeclarationId && item.Revision == receipt.DeclarationRevision);
            if (declaration is null)
                return Unavailable(context.Scope, "An assertion's original declaration could not be verified.");

            var revocation = state.Revocations.SingleOrDefault(item => item.ReceiptId == receipt.ReceiptId);
            var applicability = revocation is null ? "Unavailable" : "Revoked";
            if (revocation is null)
            {
                if (!current.TryGetValue(receipt.SubjectId, out var snapshot))
                {
                    snapshot = await ResolveCurrentAsync(source!, context.Scope, identities, ct).ConfigureAwait(false);
                    current[receipt.SubjectId] = snapshot;
                }
                if (snapshot is not null)
                    applicability = snapshot.Eligible && snapshot.Revision == receipt.SubjectRevision && snapshot.Hash == receipt.SubjectHash
                        ? "ExactSource" : "StaleSource";
            }
            entries.Add(new(receipt, declaration, revocation, applicability));
        }

        var ordered = entries.OrderBy(entry => entry.Receipt.RecordedAt).ThenBy(entry => entry.Receipt.ReceiptId).ToArray();
        var revoked = ordered.Count(entry => entry.Revocation is not null);
        var synthetic = ordered.Count(entry => string.Equals(entry.Receipt.SourceProvenance, "Synthetic", StringComparison.OrdinalIgnoreCase));
        return new(context.Scope, 1, "Available", DateTimeOffset.UtcNow, SinglePrincipalAttestationPolicy.Disclosure,
            Boundary, ordered.Length, ordered.Length - revoked, revoked, synthetic, ordered.Length - synthetic,
            ordered, []);
    }

    private async Task<SinglePrincipalAttestationSubjectSnapshotDto?> ResolveCurrentAsync(
        BoundSource source, SinglePrincipalAttestationScopeDto scope,
        Dictionary<string, EvidenceVaultIdentityDto?> identities, CancellationToken ct)
    {
        try
        {
            if (!identities.TryGetValue(source.VaultId, out var identity))
            {
                identity = await artifacts.TryGetVaultIdentityAsync(source.VaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
                identities[source.VaultId] = identity;
            }
            if (identity is null || !SameScope(scope, identity.TenantId, identity.Scope))
                return null;
            var snapshot = await resolver.ResolveAsync(source.VaultId, source.Document.DocumentId,
                scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false);
            return snapshot is null ? null : new(snapshot.SubjectKind, snapshot.SubjectId, snapshot.Revision,
                snapshot.Hash, snapshot.SourceArtifactId, snapshot.SourceHash, snapshot.SourceActor,
                snapshot.SourceProvenance, snapshot.Eligible, snapshot.Checks,
                snapshot.SourceSubjectKind, snapshot.SourceSubjectId, snapshot.SubjectSnapshotJson);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool SameScope(SinglePrincipalAttestationScopeDto expected, string? tenant, string? company) =>
        string.Equals(expected.TenantId, tenant, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected.CompanyId, company, StringComparison.OrdinalIgnoreCase);

    private static PreparerAttestationEvidenceProjectionDto Unavailable(SinglePrincipalAttestationScopeDto scope, string reason) =>
        new(scope, 1, "Unavailable", DateTimeOffset.UtcNow, SinglePrincipalAttestationPolicy.Disclosure,
            Boundary, null, null, null, null, null, [], [reason]);

    private sealed record BoundSource(string VaultId, string ContentHashSha256,
        EvidenceDocumentDto Document, EvidenceVaultArtifactDto Artifact);

    private static bool TryReadBoundSource(PreparerAttestationReceiptDto receipt, out BoundSource? source)
    {
        source = null;
        if (receipt.SchemaVersion != SinglePrincipalAttestationPolicy.SchemaVersion
            || receipt.DecisionKind != SinglePrincipalAttestationPolicy.DecisionKind
            || receipt.Disclosure != SinglePrincipalAttestationPolicy.Disclosure
            || receipt.ActionId != SinglePrincipalAttestationPolicy.EvidenceAcknowledgment
            || receipt.SubjectSnapshotJson is null
            || !Sha256Digest.FixedEquals(Sha256Digest.ComputeUtf8(receipt.SubjectSnapshotJson), receipt.SubjectHash))
            return false;
        try
        {
            source = JsonSerializer.Deserialize<BoundSource>(receipt.SubjectSnapshotJson, JsonOptions);
            if (source is null || source.Document is null || source.Artifact is null
                || receipt.SubjectId != $"{source.VaultId}/{source.Document.DocumentId}"
                || source.Document.VaultId != source.VaultId || source.Document.ArtifactId != source.Artifact.ArtifactId
                || source.Artifact.ArtifactId != receipt.SourceArtifactId
                || !Sha256Digest.FixedEquals(source.Document.SourceHashSha256, receipt.SourceHash)
                || !Sha256Digest.FixedEquals(source.Artifact.ContentHashSha256, receipt.SourceHash)
                || !SameScope(receipt.Scope, source.Document.TenantId, source.Document.Scope))
            {
                source = null;
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool MatchesVisibleSource(EvidenceArtifactRefDto[] visible,
        PreparerAttestationReceiptDto receipt, BoundSource source)
    {
        var vaultRoute = $"/workstation/evidence/vault/{Uri.EscapeDataString(source.VaultId)}";
        return visible.Any(artifact =>
            (SameRoute(artifact.Route, vaultRoute) || SameRoute(artifact.Route, source.Document.ManifestRoute))
                && Sha256Digest.FixedEquals(artifact.Hash, source.ContentHashSha256)
            || artifact.CanonicalSubjectKind == receipt.SourceSubjectKind
                && artifact.CanonicalSubjectId == receipt.SourceSubjectId
                && !string.IsNullOrWhiteSpace(artifact.Path)
                && artifact.Path.Replace('\\', '/') == source.Artifact.RelativePath.Replace('\\', '/')
                && Sha256Digest.FixedEquals(artifact.Hash, receipt.SourceHash));
    }

    private static bool SameRoute(string? candidate, string? expected) =>
        candidate is not null && expected is not null
        && expected.StartsWith("/workstation/evidence/", StringComparison.Ordinal)
        && candidate.Split('?', 2)[0] == expected.Split('?', 2)[0];
}
