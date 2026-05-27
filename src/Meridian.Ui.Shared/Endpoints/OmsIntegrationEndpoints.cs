using System.Text.Json;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class OmsIntegrationEndpoints
{
    public static WebApplication MapOmsIntegrationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/oms");

        group.MapPost("/ingest", (OmsInboundMessage request, OmsIntegrationService service) => Results.Json(service.Ingest(request), jsonOptions));
        group.MapGet("/messages", (OmsIntegrationService service) => Results.Json(service.Snapshot(), jsonOptions));

        group.MapGet("/adapters/diagnostics", () => Results.Json(new[]
        {
            new OmsAdapterDiagnostics("fix", "tcp", 0, "healthy", DateTimeOffset.UtcNow),
            new OmsAdapterDiagnostics("sftp", "file-transfer", 1, "retrying", DateTimeOffset.UtcNow),
            new OmsAdapterDiagnostics("file-drop", "local", 0, "healthy", DateTimeOffset.UtcNow)
        }, jsonOptions));

        group.MapPost("/excel/sync", (OmsSyncRequest request, OmsIntegrationService service) => Results.Json(service.ResolveSyncConflict(request), jsonOptions));
        group.MapGet("/audit", (int? take, OmsIntegrationService service) => Results.Json(service.AuditTrail(take ?? 200), jsonOptions));

        return app;
    }
}
