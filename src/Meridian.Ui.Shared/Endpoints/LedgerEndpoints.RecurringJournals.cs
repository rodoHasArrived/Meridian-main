using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public sealed record ConfigureRecurringJournalRequest(RecurringJournalScheduleSnapshot Schedule,
    JournalTemplate Template, RecurringJournalScope Scope, IReadOnlyList<JournalEvidenceReference> Evidence,
    int ExpectedScheduleVersion, int ExpectedTemplateVersion);
public sealed record RestoreRecurringJournalDefinitionsRequest(string ScheduleId, int ScheduleVersion,
    int TemplateVersion, string Reason);

public static partial class LedgerEndpoints
{
    private static void MapRecurringJournalEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerJournalAutomationRecurringOccurrences, async (HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            var source = context.RequestServices.GetService<IRecurringJournalQueueSource>();
            if (source is null)
                return ServiceUnavailable();
            var fund = context.Request.Query["fundProfileId"].ToString();
            var entity = context.Request.Query["entityId"].ToString();
            if (string.IsNullOrWhiteSpace(fund) || string.IsNullOrWhiteSpace(entity) ||
                !Guid.TryParse(context.Request.Query["ledgerBookId"], out var book) || book == Guid.Empty)
                return Results.BadRequest(new { error = "The recurring queue requires exact fund, ledger book and entity scope." });
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                return Results.Json(await source.GetQueueAsync(fund, book, entity, context.RequestAborted,
                    tenant.TenantId, tenant.CompanyId).ConfigureAwait(false), jsonOptions);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
            { return Results.Problem("The durable recurring journal queue is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable); }
        }).WithName("ListRecurringJournalOccurrences")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
            .RequireWorkstationTenantCompanyScope()
            .Produces<RecurringJournalQueueDto>(StatusCodes.Status200OK);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationRecurringInitialize, async (HttpContext context) =>
        {
            if (!TryResolveActor(context, out _))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<FileRecurringJournalStore>();
            if (store is null)
                return ServiceUnavailable();
            try
            {
                await store.InitializeAsync(context.RequestAborted).ConfigureAwait(false);
                return Results.Ok(new { initialized = true });
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            { return ApiProblemDetails.Conflict(context, "Recurring state cannot be initialized. Restore its retained snapshot and initialization marker."); }
        }).WithName("InitializeRecurringJournalStore").RequirePermission(UserPermission.AdminMaintenance)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationRecurringSchedules,
            async (ConfigureRecurringJournalRequest request, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IRecurringJournalStore>();
            var periodAuthority = context.RequestServices.GetService<IRecurringJournalPeriodAuthority>();
            if (store is null || periodAuthority is null)
                return ServiceUnavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                var scope = request.Scope with { TenantId = tenant.TenantId!, CompanyId = tenant.CompanyId! };
                await periodAuthority.ResolveAsync(scope, request.Schedule.AnchorDate, context.RequestAborted).ConfigureAwait(false);
                await using var session = await store.OpenSessionAsync(context.RequestAborted).ConfigureAwait(false);
                var prior = session.Schedules.LastOrDefault(s => string.Equals(s.ScheduleId, request.Schedule.ScheduleId, StringComparison.OrdinalIgnoreCase));
                if (prior is not null && prior.Scope != scope)
                    return EndpointHelpers.Forbidden();
                var template = session.Templates.LastOrDefault(t => string.Equals(t.TemplateId, request.Template.TemplateId, StringComparison.OrdinalIgnoreCase));
                if (template is not null && template.Scope != scope)
                    return EndpointHelpers.Forbidden();
                if (!string.Equals(request.Template.TemplateId, request.Schedule.TemplateId, StringComparison.OrdinalIgnoreCase))
                    return Results.BadRequest(new { error = "Schedule and template identity must match." });
                var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
                var schedule = request.Schedule with
                {
                    CreatedBy = prior?.Schedule.CreatedBy ?? actor,
                    CreatedAtUtc = prior?.Schedule.CreatedAtUtc ?? now
                };
                var saved = await session.ConfigureAsync(schedule.ToSchedule(), request.Template, scope, request.Evidence,
                    actor, request.ExpectedScheduleVersion, request.ExpectedTemplateVersion, now, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(saved, jsonOptions);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            { return ApiProblemDetails.Conflict(context, ex.Message); }
        }).WithName("ConfigureRecurringJournalSchedule").RequirePermission(UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerJournalAutomationRecurringRestore,
            async (RestoreRecurringJournalDefinitionsRequest request, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var store = context.RequestServices.GetService<IRecurringJournalStore>();
            if (store is null)
                return ServiceUnavailable();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                await using var session = await store.OpenSessionAsync(context.RequestAborted).ConfigureAwait(false);
                var schedule = session.GetCurrentSchedule(request.ScheduleId);
                if (!string.Equals(schedule.Scope.TenantId, tenant.TenantId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(schedule.Scope.CompanyId, tenant.CompanyId, StringComparison.OrdinalIgnoreCase))
                    return EndpointHelpers.Forbidden();
                await session.ActivateDefinitionsAsync(request.ScheduleId, request.ScheduleVersion, request.TemplateVersion,
                    actor, request.Reason, (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow(),
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Ok(new { restored = true });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            { return ApiProblemDetails.Conflict(context, ex.Message); }
        }).WithName("RestoreRecurringJournalDefinitions").RequirePermission(UserPermission.ManageLedgerReports)
            .RequireAuthenticatedSession().RequireWorkstationTenantCompanyScope().RequireFundScopedWriteTenant()
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }
}
