using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;

namespace Meridian.Application.FundStructure;

internal static class EntityLifecycleInitialization
{
    internal static EntityLifecycleRevisionDto Create(CreateLegalEntityRequest request, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            throw new FundStructureTenantScopeException("A tenant-scoped session is required to initialize entity lifecycle history.");
        var formation = request.LifecycleEvents?.OrderBy(item => item.OccurredAt).FirstOrDefault();
        return EntityLifecyclePolicy.Seal(new EntityLifecycleRevisionDto(tenantId, request.EntityId, 1,
            DateTimeOffset.UtcNow, request.CreatedBy, "Entity initial lifecycle", formation?.EvidenceReference ?? $"entity-formation:{request.EntityId:D}",
            string.Empty, string.Empty, [new(Guid.NewGuid(), request.EffectiveFrom, null, request.LifecycleStatus,
                formation?.EventKind ?? LegalEntityLifecycleEventKindDto.Formation)]));
    }
}
