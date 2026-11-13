using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.PrivateCapital;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.PortfolioRecords.PrivateCapital;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Services;

/// <summary>Checks real authorized structure identities and retained scoped document bytes.</summary>
public sealed class PrivateCapitalRecordAuthority(
    IFundStructureService structures, ISecurityMasterQueryService securities,
    IEvidenceArtifactStore evidenceStore) : IPrivateCapitalRecordAuthority
{
    public async Task<PrivateCapitalChoices> ChoicesAsync(PrivateCapitalScope scope, CancellationToken ct)
    {
        var graph = await structures.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: true), ct).ConfigureAwait(false);
        var vehicles = new List<PrivateCapitalChoice>();
        // The source query is paginated; do not present the first source page as the full universe.
        for (var skip = 0; ; skip += 200)
        {
            ct.ThrowIfCancellationRequested();
            var page = await securities.SearchAsync(new SecuritySearchRequest("", Take: 200, Skip: skip, ActiveOnly: true), ct).ConfigureAwait(false);
            vehicles.AddRange(page.Where(item => SecurityAssetClassCatalog.GetOrDefault(item.AssetClass).SupportsPrivateCapitalRecords)
                .Select(item => new PrivateCapitalChoice(item.SecurityId, item.DisplayName)));
            if (page.Count < 200)
                break;
            if (skip >= 100_000)
                throw new InvalidOperationException("Security choices exceed the bounded source query; narrow the source population before capture.");
        }
        return new(graph.Entities.Where(item => item.IsActive).Select(item => new PrivateCapitalChoice(item.EntityId, item.Name)).ToArray(),
            vehicles.DistinctBy(item => item.Id).ToArray());
    }

    public async Task<PrivateCapitalVehicle> ValidateRegisterAsync(PrivateCapitalScope scope, Guid entityId,
        Guid securityId, string currency, CancellationToken ct)
    {
        var graph = await structures.GetOrganizationStructureAsync(new OrganizationStructureQuery(ActiveOnly: true), ct).ConfigureAwait(false);
        if (!graph.Entities.Any(item => item.EntityId == entityId && item.IsActive))
            throw new UnauthorizedAccessException("The holder entity is not available in the authorized structure.");
        var security = await securities.GetByIdAsync(securityId, ct).ConfigureAwait(false);
        if (security is null || !SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).SupportsPrivateCapitalRecords
            || security.Status != SecurityStatusDto.Active)
            throw new ArgumentException("Select an active PrivateFundInterest security by its retained identity.");
        if (!string.Equals(security.Currency, currency, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Commitment currency must match the selected vehicle security's retained currency.");
        var terms = security.AssetSpecificTerms;
        var strategy = terms.ValueKind == JsonValueKind.Object && terms.TryGetProperty("strategy", out var s)
            && s.ValueKind == JsonValueKind.String ? s.GetString()! : "Unclassified";
        int? vintage = terms.ValueKind == JsonValueKind.Object && terms.TryGetProperty("vintage", out var v)
            && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var year) ? year : null;
        return new(security.DisplayName, strategy, vintage, security.Version);
    }

    public async Task ValidateEvidenceAsync(PrivateCapitalScope scope, PrivateCapitalEvidence evidence, CancellationToken ct)
    {
        var identity = await evidenceStore.TryGetVaultIdentityAsync(evidence.VaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The retained source vault is unavailable in this tenant and company.");
        var documents = identity.Documents.Where(item => item.DocumentId == evidence.DocumentId).ToArray();
        if (documents.Length != 1 || documents[0].TenantId != scope.TenantId || documents[0].Scope != scope.CompanyId ||
            !Sha256Digest.FixedEquals(documents[0].SourceHashSha256, evidence.ContentHash))
            throw new InvalidOperationException("The exact retained source document or content hash does not match.");
        if (documents[0].ReviewerState.Status == EvidenceDocumentReviewStatusDto.Rejected)
            throw new InvalidOperationException("Rejected source evidence cannot support a financial observation.");
        if (!await evidenceStore.VerifyRetainedContentAsync(evidence.VaultId, scope.TenantId, scope.CompanyId, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The retained source bytes could not be verified.");
    }
}
