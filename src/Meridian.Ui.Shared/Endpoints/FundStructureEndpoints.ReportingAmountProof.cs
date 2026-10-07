using System.Text.Json;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    private static Task<IResult> ListGovernedReportingAmountsAsync(string runId, HttpContext context,
        JsonSerializerOptions jsonOptions) => ExecuteGovernanceAsync(
            context, runId,
            async (coordinator, caller, id, ct) =>
            {
                // Apply exactly the same governed run authorization before exposing labels or money.
                _ = await coordinator.GetAsync(id, caller, ct).ConfigureAwait(false);
                var service = context.RequestServices.GetService<ReportLedgerAmountProvenanceService>()
                    ?? throw new NotSupportedException("Generated report amount proof is unavailable.");
                return service.List(id, BuildReportAccessQueryContext(context));
            },
            static (amounts, _) => amounts, StatusCodes.Status200OK, jsonOptions);
}
