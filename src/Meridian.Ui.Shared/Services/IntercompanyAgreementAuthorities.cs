using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Storage.FundStructure;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Services;

/// <summary>Checks both legal entities against current tenant ownership and retained operator grants.</summary>
public sealed class HttpContextIntercompanyAgreementAccessAuthority(
    IHttpContextAccessor accessor, IFundStructureStore structure) : IIntercompanyAgreementAccessAuthority
{
    private static readonly object TenantMapKey = new();
    internal static readonly object RequiredPermissionKey = new();

    public async Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext context,
        IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        var http = accessor.HttpContext;
        if (http is null || entityIds.Count == 0 || entityIds.Any(id => id == Guid.Empty))
            return false;
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(http);
        if (!scope.HasTenantScope || !string.Equals(scope.TenantId, context.TenantId, StringComparison.Ordinal)
            || !string.Equals(scope.Actor, context.Actor, StringComparison.Ordinal))
            return false;

        if (!http.Items.TryGetValue(TenantMapKey, out var cached) || cached is not Task<FundStructureTenantMap> tenantTask)
        {
            tenantTask = structure.GetNodeTenantsAsync(ct);
            http.Items[TenantMapKey] = tenantTask;
        }
        var tenants = await tenantTask.ConfigureAwait(false);
        var ids = entityIds.ToHashSet();
        if (!tenants.IsPartitioned || ids.Any(id => !tenants.NodeTenants.TryGetValue(id, out var owner)
            || !string.Equals(owner, context.TenantId, StringComparison.Ordinal)))
            return false;

        if (http.Items.TryGetValue(RequiredPermissionKey, out var required) && required is UserPermission explicitPermission)
        {
            if (!EndpointAuthorization.HasPermission(http, explicitPermission))
                return false;
            var reviewedEntities = await EndpointAuthorization.AuthorizeScopedManyAsync(http, explicitPermission,
                AccessScopeKindDto.LegalEntity, ids, ct).ConfigureAwait(false);
            return ids.IsSubsetOf(reviewedEntities);
        }
        if (http.Items.TryGetValue(RequiredPermissionKey, out required)
            && required is IReadOnlyCollection<UserPermission> alternativePermissions)
        {
            if (EndpointAuthorization.HasPermission(http, UserPermission.AdminMaintenance))
                return true;
            var allowedEntities = new HashSet<Guid>();
            foreach (var alternative in alternativePermissions.Distinct())
            {
                if (!EndpointAuthorization.HasPermission(http, alternative))
                    continue;
                allowedEntities.UnionWith(await EndpointAuthorization.AuthorizeScopedManyAsync(http, alternative,
                    AccessScopeKindDto.LegalEntity, ids, ct).ConfigureAwait(false));
            }
            return ids.IsSubsetOf(allowedEntities);
        }
        if (EndpointAuthorization.HasPermission(http, UserPermission.AdminMaintenance))
            return true;
        var permission = new[] { UserPermission.ManageFundStructure, UserPermission.ManageDirectLending,
            UserPermission.ManageCompliance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports }
            .FirstOrDefault(candidate => EndpointAuthorization.HasPermission(http, candidate));
        if (permission == UserPermission.None)
            return false;
        var allowed = await EndpointAuthorization.AuthorizeScopedManyAsync(http, permission,
            AccessScopeKindDto.LegalEntity, ids, ct).ConfigureAwait(false);
        return ids.IsSubsetOf(allowed);
    }
}

/// <summary>Resolves accepted, scoped Evidence Vault documents and verifies their retained bytes.</summary>
public sealed class WorkstationIntercompanyAgreementEvidenceAuthority(
    IHttpContextAccessor accessor, IEvidenceArtifactStore evidenceStore) : IIntercompanyAgreementEvidenceAuthority
{
    public async Task<IReadOnlyList<IntercompanyAgreementEvidenceDto>> ResolveAsync(
        IntercompanyAgreementCallContext context, Guid agreementId, Guid proposalId,
        IReadOnlyList<IntercompanyAgreementEvidenceDto> evidence, CancellationToken ct = default)
    {
        var http = accessor.HttpContext;
        var scope = http is null ? null : HttpContextWorkstationTenantContextAccessor.Resolve(http);
        if (scope is null || !scope.HasTenantScope || string.IsNullOrWhiteSpace(scope.CompanyId)
            || !string.Equals(scope.TenantId, context.TenantId, StringComparison.Ordinal)
            || !string.Equals(scope.Actor, context.Actor, StringComparison.Ordinal))
            throw new IntercompanyAgreementForbiddenException("A tenant and company scoped operator is required for agreement evidence.");
        if (agreementId == Guid.Empty || proposalId == Guid.Empty || evidence.Count == 0)
            throw new IntercompanyAgreementValidationException("Retained agreement evidence is required.");

        var resolved = new List<IntercompanyAgreementEvidenceDto>();
        var verifiedVaults = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in evidence)
        {
            if (!EvidenceVaultReference.TryParseCanonical(reference.Reference, out var vaultId))
                throw new IntercompanyAgreementValidationException("Select a canonical retained Evidence Vault reference.");
            var identity = await evidenceStore.TryGetVaultIdentityAsync(vaultId, context.TenantId,
                scope.CompanyId, ct).ConfigureAwait(false);
            if (identity is null)
                throw new IntercompanyAgreementValidationException("Agreement evidence is unavailable in this scope.");
            var artifacts = identity.Artifacts.Where(artifact => artifact.Document is not null
                && (string.IsNullOrWhiteSpace(reference.ArtifactId)
                    || string.Equals(artifact.ArtifactId, reference.ArtifactId, StringComparison.Ordinal))).ToArray();
            if (artifacts.Length != 1)
                throw new IntercompanyAgreementValidationException("Select one exact retained document artifact.");
            var artifact = artifacts[0];
            var document = artifact.Document!;
            if (!string.Equals(document.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(document.Scope, scope.CompanyId, StringComparison.OrdinalIgnoreCase)
                || document.ReviewerState.Status != EvidenceDocumentReviewStatusDto.Accepted
                || document.Classification != EvidenceDocumentClassificationDto.Agreement
                || !document.ObjectLinks.Any(link => link.LinkKind == EvidenceDocumentLinkKindDto.IntercompanyAgreement
                    && Guid.TryParse(link.ObjectId, out var linked) && linked == agreementId)
                || string.IsNullOrWhiteSpace(artifact.ContentHashSha256)
                || !string.Equals(artifact.ContentHashSha256, document.SourceHashSha256, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(reference.ContentHash)
                    && !string.Equals(reference.ContentHash, artifact.ContentHashSha256, StringComparison.OrdinalIgnoreCase)))
                throw new IntercompanyAgreementValidationException("Agreement evidence must be accepted and unchanged since review.");
            if (!await evidenceStore.VerifyDocumentContentAsync(vaultId, document.DocumentId, artifact.ArtifactId,
                artifact.ContentHashSha256, context.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
                throw new IntercompanyAgreementValidationException("The exact retained agreement document failed content verification.");
            if (verifiedVaults.Add(vaultId)
                && !await evidenceStore.VerifyRetainedContentAsync(vaultId, context.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
                throw new IntercompanyAgreementValidationException("Retained agreement document content could not be verified.");
            resolved.Add(new(EvidenceVaultReference.CanonicalPrefix + vaultId,
                artifact.ArtifactId, artifact.ContentHashSha256.ToLowerInvariant()));
        }
        return resolved.Distinct().OrderBy(item => item.Reference, StringComparer.Ordinal)
            .ThenBy(item => item.ArtifactId, StringComparer.Ordinal).ToArray();
    }
}
