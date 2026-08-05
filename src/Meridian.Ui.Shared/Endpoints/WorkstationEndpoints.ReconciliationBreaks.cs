using Meridian.Contracts.Workstation;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Resolves the immutable tenant and company scope used by all reconciliation queue reads and
/// mutations. Queue publication belongs to the authoritative source workflow and never to a
/// workstation read or casework request.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static bool TryResolveReconciliationBreakQueueScope(
        HttpContext context,
        out ReconciliationBreakQueueScope scope)
    {
        var tenantId = context.Items[LoginSessionMiddleware.CurrentTenantIdKey] as string;
        var companyId = context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] as string;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId))
        {
            scope = null!;
            return false;
        }

        scope = new ReconciliationBreakQueueScope(tenantId, companyId);
        return true;
    }
}
