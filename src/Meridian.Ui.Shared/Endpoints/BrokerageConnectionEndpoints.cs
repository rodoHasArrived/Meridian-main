using System.Text.Json;
using Meridian.Identity.Auth;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class BrokerageConnectionEndpoints
{
    public static void MapBrokerageConnectionEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/brokerage-connections").WithTags("Brokerage Connections");

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
        .WithName("StartRobinhoodBrokerageConnection")
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
        .WithName("GetRobinhoodBrokerageConnectionStatus")
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
        .WithName("CompleteRobinhoodBrokerageConnection")
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
        .WithName("RevokeRobinhoodBrokerageConnection")
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapGet("/alpaca/status", async (HttpContext context) =>
        {
            if (!HasViewBrokeragePermission(context))
                return PermissionDenied();

            var service = ResolveAlpacaConnectionService(context);
            if (service is null)
                return ServiceUnavailable();

            var status = await service.GetStatusAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("GetAlpacaBrokerageConnectionStatus")
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapPost("/alpaca/connect", async (AlpacaBrokerageConnectionRequestDto request, HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
                return PermissionDenied();

            var service = ResolveAlpacaConnectionService(context);
            if (service is null)
                return ServiceUnavailable();

            var status = await service.ConnectAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("ConnectAlpacaBrokerageConnection")
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        group.MapDelete("/alpaca", async (HttpContext context) =>
        {
            if (!HasManageCredentialsPermission(context))
                return PermissionDenied();

            var service = ResolveAlpacaConnectionService(context);
            if (service is null)
                return ServiceUnavailable();

            var status = await service.RevokeAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(status, jsonOptions);
        })
        .WithName("RevokeAlpacaBrokerageConnection")
        .Produces<BrokerageConnectionStatusDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);
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
