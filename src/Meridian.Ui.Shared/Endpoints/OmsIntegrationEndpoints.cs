using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Contracts.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class OmsIntegrationEndpoints
{
    private const string KeyIdHeader = "X-Meridian-Key-Id";
    private const string TimestampHeader = "X-Meridian-Timestamp";
    private const string SignatureHeader = "X-Meridian-Signature";

    public static WebApplication MapOmsIntegrationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/oms");

        group.MapPost("/ingest", (HttpContext context, OmsInboundMessage request, IOmsIntegrationApiHandler handler) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageOrders))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                return Results.Json(handler.Ingest(request, TryCreateSignature(context, request.CorrelationId)), jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequirePermission(UserPermission.ManageOrders);

        group.MapGet("/messages", (HttpContext context, IOmsIntegrationApiHandler handler) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewTrades))
            {
                return EndpointHelpers.Forbidden();
            }

            return Results.Json(handler.Snapshot(), jsonOptions);
        }).RequirePermission(UserPermission.ViewTrades);

        group.MapGet("/adapters/diagnostics", (HttpContext context, IOmsIntegrationApiHandler handler) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewDiagnostics))
            {
                return EndpointHelpers.Forbidden();
            }

            return Results.Json(handler.AdapterDiagnostics(), jsonOptions);
        }).RequirePermission(UserPermission.ViewDiagnostics);

        group.MapPost("/excel/sync", (HttpContext context, OmsSyncRequest request, IOmsIntegrationApiHandler handler) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageOrders))
            {
                return EndpointHelpers.Forbidden();
            }

            try
            {
                return Results.Json(handler.ResolveSyncConflict(request, TryCreateSignature(context, request.CorrelationId)), jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequirePermission(UserPermission.ManageOrders);

        group.MapPost("/auth/signing-keys/rotate", (HttpContext context, OmsKeyRotationRequest request, IOmsIntegrationApiHandler handler) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials))
            {
                return EndpointHelpers.Forbidden();
            }

            return Results.Json(handler.RotateSigningKey(request), jsonOptions);
        }).RequirePermission(UserPermission.ManageCredentials);

        group.MapGet("/audit", (HttpContext context, int? take, IOmsIntegrationApiHandler handler) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ViewDiagnostics))
            {
                return EndpointHelpers.Forbidden();
            }

            return Results.Json(handler.AuditTrail(take ?? 200), jsonOptions);
        }).RequirePermission(UserPermission.ViewDiagnostics);

        return app;
    }

    private static OmsRequestSignatureInput? TryCreateSignature(HttpContext context, string canonicalPayload)
    {
        var headers = context.Request.Headers;
        if (!headers.TryGetValue(KeyIdHeader, out var keyId) ||
            !headers.TryGetValue(TimestampHeader, out var timestamp) ||
            !headers.TryGetValue(SignatureHeader, out var signature))
        {
            return null;
        }

        return new OmsRequestSignatureInput(keyId.ToString(), timestamp.ToString(), signature.ToString(), canonicalPayload);
    }
}
