using Meridian.Identity.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Serialization;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapCloseSnapshotEndpoints(WebApplication app)
    {
        app.MapPost("/api/ledger/period-adjustments/schedule-mark-reversal", async (HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context) || !TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.CompanyId))
                return EndpointHelpers.Forbidden();
            var store = ResolveJournalStore(context);
            var intake = context.RequestServices.GetService<AutomatedJournalDraftIntakeService>();
            var schedules = context.RequestServices.GetService<IAutomatedJournalScheduleStore>();
            if (store is null || intake is null || schedules is null || ResolveService(context) is null)
                return ServiceUnavailable();
            try
            {
                var request = await context.Request.ReadFromJsonAsync(
                    PeriodAdjustmentJsonContext.Default.PreparePeriodAdjustmentRequest, context.RequestAborted).ConfigureAwait(false);
                if (request is null)
                    return Results.BadRequest(new { error = "An explicit period-end reversal policy is required." });
                if (await ResolveAccessiblePreparationBookAsync(context, request.LedgerBookId).ConfigureAwait(false) is null)
                    return Results.NotFound();
                var result = await new PeriodAdjustmentPreparationService(store, intake, schedules)
                    .ScheduleMarkReversalAsync(request, actor, tenant.TenantId, tenant.CompanyId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, PeriodAdjustmentJsonContext.Default.AutomatedJournalScheduleWorkItem);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        })
        .WithName("ScheduleLedgerPeriodEndMarkReversal")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
        .RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost("/api/ledger/period-adjustments/prepare", async (HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context) || !TryResolveActor(context, out var actor))
                return EndpointHelpers.Forbidden();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId) || string.IsNullOrWhiteSpace(tenant.CompanyId))
                return EndpointHelpers.Forbidden();
            var store = ResolveJournalStore(context);
            var intake = context.RequestServices.GetService<AutomatedJournalDraftIntakeService>();
            if (store is null || intake is null || ResolveService(context) is null)
                return ServiceUnavailable();
            try
            {
                var request = await context.Request.ReadFromJsonAsync(
                    PeriodAdjustmentJsonContext.Default.PreparePeriodAdjustmentRequest, context.RequestAborted).ConfigureAwait(false);
                if (request is null)
                    return Results.BadRequest(new { error = "A correction preparation request is required." });
                if (await ResolveAccessiblePreparationBookAsync(context, request.LedgerBookId).ConfigureAwait(false) is null)
                    return Results.NotFound();
                var result = await new PeriodAdjustmentPreparationService(store, intake)
                    .PrepareAsync(request, actor, tenant.TenantId, tenant.CompanyId, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, PeriodAdjustmentJsonContext.Default.AutomatedJournalDraftIntakeResult);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        })
        .WithName("PrepareLedgerPriorPeriodAdjustment")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageLedgerReports)
        .RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet("/api/ledger/periods/{periodId:guid}/close-snapshot", async (
            Guid periodId, long? periodVersion, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            if (periodVersion is <= 0)
                return Results.BadRequest(new { error = "Close snapshot version must be positive." });
            var store = ResolveJournalStore(context);
            if (store is not IAtomicLedgerPeriodCloseStore snapshots || ResolveService(context) is null)
                return ServiceUnavailable();
            var period = await store.GetPeriodAsync(periodId, context.RequestAborted).ConfigureAwait(false);
            if (period?.LedgerBookId is not { } bookId
                || await ResolveAccessiblePreparationBookAsync(context, bookId).ConfigureAwait(false) is null)
                return Results.NotFound();
            try
            {
                var snapshot = await snapshots.GetCloseSnapshotAsync(periodId, periodVersion, context.RequestAborted)
                    .ConfigureAwait(false);
                return snapshot is null ? Results.NotFound()
                    : Results.Json(snapshot, LedgerPeriodCloseSnapshotJsonContext.Default.LedgerPeriodCloseSnapshot);
            }
            catch (InvalidDataException)
            {
                return Results.Problem("The retained close snapshot failed its integrity check.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("GetLedgerPeriodCloseSnapshot")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<LedgerPeriodCloseSnapshot>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }
}
