using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    /// <summary>
    /// Read-only fiscal-year close plan (W12-YEND-001 slice 1). Blockers return 200 with
    /// <c>IsReady=false</c> so operators see every named reason; reading never posts or closes.
    /// </summary>
    private static void MapFiscalYearCloseEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerFiscalYearClosePlan, async (Guid ledgerBookId, int fiscalYear, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            if (fiscalYear is < 1900 or > 9999)
                return Results.BadRequest(new { error = "Fiscal year must be between 1900 and 9999." });
            var store = ResolveJournalStore(context);
            if (store is not IAtomicLedgerPeriodCloseStore || ResolveService(context) is null)
                return ServiceUnavailable();
            if (await ResolveAccessiblePreparationBookAsync(context, ledgerBookId).ConfigureAwait(false) is null)
                return Results.NotFound();
            var plan = await FiscalYearClosePlanService.BuildAsync(store, ledgerBookId, fiscalYear, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(plan, jsonOptions);
        })
        .WithName("GetLedgerFiscalYearClosePlan")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<FiscalYearClosePlanDto>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }
}
