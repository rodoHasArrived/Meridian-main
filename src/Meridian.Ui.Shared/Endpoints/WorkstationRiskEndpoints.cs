using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static async Task<WorkstationRiskDescriptor?> ResolveRuntimeRiskDescriptorAsync(HttpContext context)
    {
        var runtimeService = context.RequestServices.GetService<RiskRuleRuntimeService>();
        if (runtimeService is null)
        {
            return null;
        }

        var statuses = await runtimeService.GetAllStatusesAsync(context.RequestAborted).ConfigureAwait(false);
        if (statuses.Count == 0)
        {
            return null;
        }

        var constrained = statuses.FirstOrDefault(static status =>
            string.Equals(status.State, "Constrained", StringComparison.OrdinalIgnoreCase));
        var observed = statuses.FirstOrDefault(static status =>
            string.Equals(status.State, "Observe", StringComparison.OrdinalIgnoreCase));

        var selected = constrained ?? observed ?? statuses[0];
        var activeGuardrails = statuses
            .Select(static status =>
                $"{status.RuleName}: {status.CurrentValue} (threshold {status.Threshold}, state {status.State}).")
            .ToArray();

        return new WorkstationRiskDescriptor(
            State: selected.State,
            Summary: selected.Summary,
            ActiveGuardrails: activeGuardrails);
    }

    private sealed record WorkstationRiskDescriptor(
        string State,
        string Summary,
        IReadOnlyList<string> ActiveGuardrails);
}
