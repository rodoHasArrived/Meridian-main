using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Opt-in, reviewed intercompany accrual schedules. Every route is scoped to a book the caller's tenant and company
/// own; a run drafts only, through the ordinary preview-then-draft path.
/// </summary>
public static class IntercompanyAccrualScheduleEndpoints
{
    private const string Route = "/api/ledger/intercompany/accrual-schedules";

    public static void MapIntercompanyAccrualScheduleEndpoints(this WebApplication app, JsonSerializerOptions options)
    {
        app.MapGet(Route, (Guid ledgerBookId, HttpContext context) => ExecuteAsync(context, ledgerBookId, options,
            (service, scope, _) => service.ListAsync(scope.TenantId!, ledgerBookId, context.RequestAborted)))
            .RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        app.MapPost(Route, (ProposeIntercompanyAccrualScheduleRequest request, HttpContext context) => ExecuteAsync(context, request.LedgerBookId, options,
            (service, scope, actor) => service.ProposeAsync(scope.TenantId!, scope.CompanyId, actor!, request, context.RequestAborted), requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(Route + "/{scheduleId:guid}/decisions", (Guid scheduleId, Guid ledgerBookId, DecideIntercompanyAccrualScheduleRequest request,
            HttpContext context) => ExecuteAsync(context, ledgerBookId, options,
                (service, scope, actor) => Scoped(service.DecideAsync(scope.TenantId!, scope.CompanyId, actor!, scheduleId, request, context.RequestAborted), ledgerBookId),
                requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // What a run would draft as of a date; nothing is created.
        app.MapGet(Route + "/{scheduleId:guid}/due", (Guid scheduleId, Guid ledgerBookId, DateOnly asOf, HttpContext context) => ExecuteAsync(context, ledgerBookId, options,
            (service, scope, _) => service.DueAsync(scope.TenantId!, scope.CompanyId, scheduleId, asOf, context.RequestAborted)))
            .RequireWorkstationTenantCompanyScope()
            .RequireAnyPermission(UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance);

        // Runs an active schedule now, as of today; it drafts exactly what the background run would.
        app.MapPost(Route + "/{scheduleId:guid}/run", (Guid scheduleId, Guid ledgerBookId, HttpContext context) => ExecuteAsync(context, ledgerBookId, options,
            (service, scope, _) => service.RunAsync(scope.TenantId!, scope.CompanyId, scheduleId,
                DateOnly.FromDateTime((context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().UtcDateTime), context.RequestAborted),
            requiresActor: true))
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireAnyPermission(UserPermission.ManageLedgerReports, UserPermission.AdminMaintenance)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    // A decision names the book it was read under; a schedule of another book is not decided through it.
    private static async Task<IntercompanyAccrualScheduleDto> Scoped(Task<IntercompanyAccrualScheduleDto> decision, Guid ledgerBookId)
    {
        var schedule = await decision.ConfigureAwait(false);
        return schedule.LedgerBookId == ledgerBookId ? schedule : throw new UnauthorizedAccessException("The schedule belongs to another book.");
    }

    private static async Task<IResult> ExecuteAsync<T>(HttpContext context, Guid ledgerBookId, JsonSerializerOptions options,
        Func<IntercompanyAccrualScheduleService, WorkstationTenantContext, string?, Task<T>> operation, bool requiresActor = false)
    {
        var service = context.RequestServices.GetService<IntercompanyAccrualScheduleService>();
        if (service is null)
            return Results.Problem("Intercompany accrual schedules are unavailable.", statusCode: 503);
        string? actor = null;
        if (requiresActor && !EndpointAuthorization.TryResolveActor(context, out actor))
            return Results.Unauthorized();
        if (!await IntercompanyOperationsEndpoints.BookAllowedAsync(context, ledgerBookId).ConfigureAwait(false))
            return EndpointHelpers.Forbidden();
        try
        {
            return Results.Json(await operation(service, HttpContextWorkstationTenantContextAccessor.Resolve(context), actor).ConfigureAwait(false), options);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or Meridian.Contracts.Tenancy.TenantScopeRejectedException)
        { return EndpointHelpers.Forbidden(); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        { return Results.Conflict(new { error = error.Message }); }
    }
}
