using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Maps brokerage connection setup and lifecycle operations within the workstation tenant scope.
/// </summary>
public static class BrokerageConnectionEndpoints
{
    public static void MapBrokerageConnectionEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/brokerage-connections").WithTags("Brokerage Connections");
        group.RequireWorkstationTenantScope();

        group.MapPost("/robinhood/connect", async (HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
                return PermissionDenied();

            var service = ResolveConnectionService(context);
            if (service is null)
                return ServiceUnavailable();

            var status = await service.StartConnectionAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("StartRobinhoodBrokerageConnection").RequirePermission(UserPermission.ManageCredentials)
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/robinhood/status", async (HttpContext context) =>
        {
            if (!HasViewBrokeragePermission(context))
                return PermissionDenied();

            var service = ResolveConnectionService(context);
            if (service is null)
                return ServiceUnavailable();

            var status = await service.GetStatusAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("GetRobinhoodBrokerageConnectionStatus").RequirePermission(UserPermission.ViewTrades)
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/robinhood/callback", async (string? code, string? state, string? error, HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
                return PermissionDenied();

            var service = ResolveConnectionService(context);
            if (service is null)
                return ServiceUnavailable();

            var status = await service.CompleteCallbackAsync(code, state, error, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("CompleteRobinhoodBrokerageConnection").RequirePermission(UserPermission.ManageCredentials)
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapDelete("/robinhood", async (HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
                return PermissionDenied();

            var service = ResolveConnectionService(context);
            if (service is null)
                return ServiceUnavailable();

            var status = await service.RevokeAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("RevokeRobinhoodBrokerageConnection").RequirePermission(UserPermission.ManageCredentials)
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        // Alpaca credentials are owned by one retained connection (tenant, connection, external
        // account and environment). ?connectionId= names it; without it the tenant's single owned
        // paper connection is used. No Alpaca route reads or writes the provider-wide record.
        group.MapGet("/alpaca/status", async (HttpContext context) =>
        {
            if (!HasViewBrokeragePermission(context))
                return PermissionDenied();

            return await RunAlpacaAsync(context, jsonOptions, (service, connectionId, tenantId) =>
                service.GetStatusAsync(connectionId, tenantId, context.RequestAborted)).ConfigureAwait(false);
        })
        .WithName("GetAlpacaBrokerageConnectionStatus").RequirePermission(UserPermission.ViewTrades)
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/alpaca/connect", async (AlpacaBrokerageConnectionRequestDto request, HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
                return PermissionDenied();

            return await RunAlpacaAsync(context, jsonOptions, (service, connectionId, tenantId) =>
                service.ConnectAsync(connectionId, tenantId, request, context.RequestAborted, actor)).ConfigureAwait(false);
        })
        .WithName("ConnectAlpacaBrokerageConnection").RequirePermission(UserPermission.ManageCredentials)
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapDelete("/alpaca", async (HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context) || !EndpointAuthorization.TryResolveActor(context, out var actor))
                return PermissionDenied();

            return await RunAlpacaAsync(context, jsonOptions, (service, connectionId, tenantId) =>
                service.RevokeAsync(connectionId, tenantId, context.RequestAborted, actor)).ConfigureAwait(false);
        })
        .WithName("RevokeAlpacaBrokerageConnection").RequirePermission(UserPermission.ManageCredentials)
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented);
    }

    private static async Task<IResult> RunAlpacaAsync(
        HttpContext context,
        JsonSerializerOptions jsonOptions,
        Func<AlpacaBrokerageConnectionService, string?, string, Task<BrokerageConnectionStatusDto>> operation)
    {
        var service = ResolveAlpacaConnectionService(context);
        if (service is null)
            return ServiceUnavailable();

        string? connectionId = null;
        if (context.Request.Query.TryGetValue("connectionId", out var connectionIds))
        {
            if (connectionIds.Count != 1 || string.IsNullOrWhiteSpace(connectionIds[0]))
                return Results.BadRequest(new { error = "Name exactly one owned Alpaca connection in the connectionId query parameter." });
            connectionId = connectionIds[0];
        }

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (!tenant.HasTenantScope)
            return PermissionDenied();

        try
        {
            var status = await operation(service, connectionId, tenant.TenantId!).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        }
        catch (UnauthorizedAccessException)
        {
            // Unknown, foreign and wrong-provider connections are indistinguishable to the caller.
            return PermissionDenied();
        }
        catch (InvalidDataException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static BrokerageConnectionService? ResolveConnectionService(HttpContext context)
        => context.RequestServices.GetService<BrokerageConnectionService>();

    private static AlpacaBrokerageConnectionService? ResolveAlpacaConnectionService(HttpContext context)
        => context.RequestServices.GetService<AlpacaBrokerageConnectionService>();

    private static IResult ServiceUnavailable()
        => Results.Problem("Brokerage connection service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult PermissionDenied()
        => Results.StatusCode(StatusCodes.Status403Forbidden);

    private static bool HasViewBrokeragePermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ViewTrades);

    private static bool HasManageCredentialsPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.ManageCredentials);
}
