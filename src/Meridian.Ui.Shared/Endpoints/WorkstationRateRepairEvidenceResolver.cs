using Meridian.Contracts.Integrity;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>HTTP tenant-scoped adapter: resolves owned retained document bytes and derives their repair evidence identity on the server.</summary>
public sealed class WorkstationRateRepairEvidenceResolver(
    IEvidenceArtifactStore artifacts, IWorkstationTenantContextAccessor contextAccessor) : ISecurityRateRepairEvidenceResolver
{
    public async Task<SecurityRateRepairEvidence> ResolveAsync(string evidenceId, string tenantId, CancellationToken ct = default)
    {
        var context = contextAccessor.GetRequired();
        if (context.TenantId != tenantId || string.IsNullOrWhiteSpace(context.CompanyId))
            throw new UnauthorizedAccessException("Retained evidence requires the authenticated tenant and company scope.");
        if (!EvidenceVaultReference.TryNormalizeVaultId(evidenceId, out var vaultId)
            && !EvidenceVaultReference.TryParseCanonical(evidenceId, out vaultId))
            throw new ArgumentException("Attach an owned Evidence Vault document or canonical vault reference.");
        var identity = await artifacts.TryGetVaultIdentityAsync(vaultId, tenantId, context.CompanyId, ct).ConfigureAwait(false)
            ?? throw new ArgumentException("The evidence is unavailable in the authenticated tenant and company.");
        if (identity.Documents.Count is < 1 or > 32)
            throw new ArgumentException("The evidence must retain between one and 32 source documents.");
        var hashes = new List<object>();
        foreach (var document in identity.Documents.OrderBy(static document => document.DocumentId, StringComparer.Ordinal))
        {
            var file = await artifacts.TryOpenDocumentContentAsync(vaultId, document.DocumentId,
                tenantId, context.CompanyId, ct).ConfigureAwait(false)
                ?? throw new ArgumentException("A retained evidence document cannot be verified.");
            await using var bytes = file.Content;
            var hash = await Sha256Digest.ComputeAsync(bytes, ct).ConfigureAwait(false);
            if (!Sha256Digest.FixedEquals(hash, document.SourceHashSha256))
                throw new InvalidDataException("The retained evidence bytes differ from their source identity.");
            hashes.Add(new { document.DocumentId, Sha256 = hash });
        }
        return new(vaultId, Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(hashes)));
    }
}
