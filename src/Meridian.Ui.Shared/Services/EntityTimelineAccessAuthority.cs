using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Services;

public interface IEntityTimelineAccessAuthority
{
    Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext caller,
        IReadOnlyList<Guid> entityIds, CancellationToken ct = default);
}

/// <summary>Uses the accounting reader's actual legal-entity grants without granting legal-registry access.</summary>
public sealed class HttpContextEntityTimelineAccessAuthority(IHttpContextAccessor accessor) : IEntityTimelineAccessAuthority
{
    public async Task<bool> CanAccessEntitiesAsync(IntercompanyAgreementCallContext caller,
        IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        var http = accessor.HttpContext;
        if (http is null || entityIds.Count == 0 || entityIds.Any(id => id == Guid.Empty)) return false;
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(http);
        if (!scope.HasTenantScope || scope.TenantId != caller.TenantId || scope.Actor != caller.Actor) return false;
        if (EndpointAuthorization.HasPermission(http, UserPermission.AdminMaintenance)) return true;
        var ids = entityIds.ToHashSet();
        var allowed = new HashSet<Guid>();
        foreach (var permission in new[] { UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports,
                     UserPermission.ManageFundStructure })
            if (EndpointAuthorization.HasPermission(http, permission))
                allowed.UnionWith(await EndpointAuthorization.AuthorizeScopedManyAsync(http, permission,
                    AccessScopeKindDto.LegalEntity, ids, ct).ConfigureAwait(false));
        return ids.IsSubsetOf(allowed);
    }
}
