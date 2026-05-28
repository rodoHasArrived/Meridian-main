using System.Text.Json;
using Meridian.Contracts.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class OmsIntegrationEndpoints
{
    public static WebApplication MapOmsIntegrationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/oms");

        group.MapPost("/ingest", (HttpContext context, OmsInboundMessage request, OmsIntegrationService service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageOrders))
            {
                return Results.Forbid();
            }

            return Results.Json(service.Ingest(request), jsonOptions);
        });

        group.MapGet("/messages", (HttpContext context, OmsIntegrationService service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewTrades))
            {
                return Results.Forbid();
            }

            return Results.Json(service.Snapshot(), jsonOptions);
        });

        group.MapGet("/adapters/diagnostics", (HttpContext context) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewDiagnostics))
            {
                return Results.Forbid();
            }

            return Results.Json(new[]
            {
                new OmsAdapterDiagnostics("fix", "tcp", 0, "healthy", DateTimeOffset.UtcNow),
                new OmsAdapterDiagnostics("sftp", "file-transfer", 1, "retrying", DateTimeOffset.UtcNow),
                new OmsAdapterDiagnostics("file-drop", "local", 0, "healthy", DateTimeOffset.UtcNow)
            }, jsonOptions);
        });

        group.MapPost("/excel/sync", (HttpContext context, OmsSyncRequest request, OmsIntegrationService service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageOrders))
            {
                return Results.Forbid();
            }

            return Results.Json(service.ResolveSyncConflict(request), jsonOptions);
        });

        group.MapGet("/audit", (HttpContext context, int? take, OmsIntegrationService service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewDiagnostics))
            {
                return Results.Forbid();
            }

            return Results.Json(service.AuditTrail(take ?? 200), jsonOptions);
        });

        return app;
    }
}
