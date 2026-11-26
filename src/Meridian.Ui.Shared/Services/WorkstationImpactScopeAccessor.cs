using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Ui.Shared.Services;

internal sealed class WorkstationImpactScopeAccessor(IWorkstationTenantContextAccessor context) : ISecurityMasterImpactScopeAccessor
{
    public SecurityMasterImpactScope GetRequired()
    {
        var current = context.GetRequired();
        return new(current.TenantId!, current.CompanyId);
    }
}
