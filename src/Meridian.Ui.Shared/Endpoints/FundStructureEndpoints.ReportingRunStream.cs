using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

// Report-run status stream surface, split out of FundStructureEndpoints.cs to keep that file under
// its size cap. The report-run SSE endpoint (subscribe-time authorization + the shared event-stream
// loop) is added here in D2.
public static partial class FundStructureEndpoints
{
    /// <summary>
    /// Build a report-run status/audit payload by run id alone, for the report-run stream fan-out
    /// (<see cref="Meridian.Ui.Shared.Streaming.ReportRunStreamBroadcaster"/>). Authorization is
    /// enforced at subscribe time by the SSE endpoint, so this pure builder needs no access context —
    /// a run's status is a property of the run, not the viewer. Returns null when the orchestration
    /// service or the run is unavailable.
    /// </summary>
    internal static ReportingRunAuditTrailDto? TryBuildReportRunAuditTrail(IServiceProvider services, string runId)
    {
        var orchestration = services.GetService<IReportingOrchestrationService>();
        if (orchestration is null)
        {
            return null;
        }

        var manifest = orchestration.GetManifest((runId ?? string.Empty).Trim());
        return manifest is null
            ? null
            : ProjectReportingRunAuditTrail(manifest, orchestration.GetAudit(manifest.RunId));
    }
}
