using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class FirstRunEndpoints
{
    public static void MapFirstRunEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workstation/first-run").WithTags("First run");
        group.MapGet("/", async (HttpContext context, FirstRunExperienceService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(CurrentUser(context), ct).ConfigureAwait(false)))
            .RequireAuthenticatedSessionOrScopedLocalOperatorRead();
        group.MapPost("/complete", async (HttpContext context, CompleteFirstRunRequestDto request, FirstRunExperienceService service, CancellationToken ct) =>
        {
            try
            { return Results.Ok(await service.CompleteAsync(CurrentUser(context), request, ct).ConfigureAwait(false)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).RequirePermission(UserPermission.AdminMaintenance);
        group.MapPost("/outcomes/complete", async (HttpContext context, CompleteActivationOutcomeRequestDto request, FirstRunExperienceService service, CancellationToken ct) =>
        {
            try
            { return Results.Ok(await service.CompleteOutcomeAsync(CurrentUser(context), request.Key, ct).ConfigureAwait(false)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        }).RequireAuthenticatedSession();
        app.MapPost(
                "/api/workstation/desktop/launch",
                (HttpContext context, DesktopLaunchRequest request, [FromServices] DesktopWorkstationLaunchService service) =>
                {
                    var remote = context.Connection.RemoteIpAddress;
                    if (remote is not null && !System.Net.IPAddress.IsLoopback(remote))
                    {
                        return Results.NotFound();
                    }

                    var hostBaseAddress = $"{context.Request.Scheme}://{context.Request.Host}";
                    return service.TryLaunch(
                        CurrentUser(context),
                        hostBaseAddress,
                        request.Page,
                        out var message)
                        ? Results.Ok(new { message })
                        : Results.Json(
                            new { error = message },
                            statusCode: StatusCodes.Status409Conflict);
                })
            .WithTags("Workstation").RequireAuthenticatedSession();

        app.MapGet(
                "/api/auth/desktop-launch/{ticket}",
                (HttpContext context, string ticket, [FromServices] DesktopLaunchTicketService service) =>
                {
                    var redemption = service.Redeem(context.Connection.RemoteIpAddress, ticket);
                    return redemption is null ? Results.NotFound() : Results.Ok(redemption);
                })
            .DeclareOpenRead("One-use desktop launch ticket redeemed by the WPF client before it has a session -- the handoff exists precisely because no session is established yet -- and bound to the redeeming caller's remote address rather than to a permission.")
            .ExcludeFromDescription();
    }

    private static string CurrentUser(HttpContext context) =>
        context.Items[LoginSessionMiddleware.CurrentUserKey] as string
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private sealed record DesktopLaunchRequest(string? Page);
}
