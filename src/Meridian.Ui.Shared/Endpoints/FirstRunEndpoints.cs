using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static class FirstRunEndpoints
{
    public static void MapFirstRunEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/workstation/first-run").WithTags("First run");
        group.MapGet("/", async (HttpContext context, FirstRunExperienceService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(CurrentUser(context), ct).ConfigureAwait(false)));
        group.MapPost("/complete", async (HttpContext context, CompleteFirstRunRequestDto request, FirstRunExperienceService service, CancellationToken ct) =>
        {
            try
            { return Results.Ok(await service.CompleteAsync(CurrentUser(context), request, ct).ConfigureAwait(false)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        group.MapPost("/outcomes/complete", async (HttpContext context, CompleteActivationOutcomeRequestDto request, FirstRunExperienceService service, CancellationToken ct) =>
        {
            try
            { return Results.Ok(await service.CompleteOutcomeAsync(CurrentUser(context), request.Key, ct).ConfigureAwait(false)); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
        app.MapPost("/api/workstation/desktop/launch", (HttpContext context, DesktopLaunchRequest request, DesktopWorkstationLaunchService service) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote is not null && !System.Net.IPAddress.IsLoopback(remote))
                return Results.NotFound();
            return service.TryLaunch(request.Page, out var message)
                ? Results.Ok(new { message })
                : Results.Json(new { error = message }, statusCode: StatusCodes.Status409Conflict);
        }).WithTags("Workstation");
    }

    private static string CurrentUser(HttpContext context) =>
        context.Items[LoginSessionMiddleware.CurrentUserKey] as string
        ?? throw new InvalidOperationException("An authenticated user is required.");

    private sealed record DesktopLaunchRequest(string? Page);
}
