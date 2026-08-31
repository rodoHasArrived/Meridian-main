using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackRunReadService
{
    private IReadOnlyList<ReportingRunSnapshot> ListRunSnapshots(
        ReportAccessQueryContext? accessContext,
        int limit)
    {
        if (_runStore is null)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(accessContext?.TenantId))
        {
            var snapshots = _runStore.ListRuns(limit);
            return accessContext is null
                ? snapshots
                : snapshots
                    .Where(snapshot => ReportAccessPolicyEvaluator
                        .Evaluate(snapshot.Manifest, accessContext)
                        .IsAccessible)
                    .ToArray();
        }

        const int pageSize = 200;
        var visible = new List<ReportingRunSnapshot>(Math.Clamp(limit, 1, 200));
        for (var offset = 0; visible.Count < limit; offset += pageSize)
        {
            var page = _runStore.ListRuns(
                accessContext.TenantId.Trim(),
                accessContext.CompanyId,
                offset,
                pageSize);
            visible.AddRange(page.Where(snapshot => ReportAccessPolicyEvaluator
                .Evaluate(snapshot.Manifest, accessContext)
                .IsAccessible));
            if (page.Count < pageSize)
            {
                break;
            }
        }

        return visible.Take(Math.Clamp(limit, 1, 200)).ToArray();
    }
}
