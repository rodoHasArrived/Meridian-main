using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    /// <summary>
    /// The fiscal close plan, its retained approval, and its exact-replay receipt.
    /// </summary>
    private static void MapFiscalYearCloseEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        // Both workstations get exact decimal text. The independent retained payload and
        // calculation hashes keep their existing numeric serialization.
        var closeJsonOptions = new JsonSerializerOptions(jsonOptions);
        closeJsonOptions.Converters.Insert(0, new ReportingIncomeDecimalJsonConverter());

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
            var canFinalize = false;
            if (store is IFiscalYearCloseReceiptStore fiscal && plan.IsReady && plan.ProposedClosingTransfers.Count == 0
                && CanAuthorizeFiscalClose(context, out _, out _))
            {
                try
                {
                    var retained = await fiscal.GetFiscalYearCloseReceiptAsync(ledgerBookId, fiscalYear, ct: context.RequestAborted)
                        .ConfigureAwait(false);
                    canFinalize = retained?.Plan.PlanHashSha256 != plan.PlanHashSha256;
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException)
                {
                    return FiscalCloseIntegrityConflict();
                }
            }
            return Results.Json(plan with { CanFinalizeFiscalYearClose = canFinalize }, closeJsonOptions);
        })
        .WithName("GetLedgerFiscalYearClosePlan")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<FiscalYearClosePlanDto>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapGet(UiApiRoutes.LedgerFiscalYearCloseReceipts, async (Guid ledgerBookId, int fiscalYear, Guid? receiptId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            if (fiscalYear is < 1900 or > 9999 || receiptId == Guid.Empty)
                return Results.BadRequest(new { error = "A valid fiscal year and optional receipt id are required." });
            if (ResolveJournalStore(context) is not IFiscalYearCloseReceiptStore store || ResolveService(context) is null)
                return ServiceUnavailable();
            if (await ResolveAccessiblePreparationBookAsync(context, ledgerBookId).ConfigureAwait(false) is null)
                return Results.NotFound();
            try
            {
                var receipt = await store.GetFiscalYearCloseReceiptAsync(ledgerBookId, fiscalYear, receiptId, context.RequestAborted)
                    .ConfigureAwait(false);
                return receipt is null ? Results.NotFound() : Results.Json(receipt, closeJsonOptions);
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException)
            {
                return FiscalCloseIntegrityConflict();
            }
        })
        .WithName("GetLedgerFiscalYearCloseReceipt")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<FiscalYearCloseReceiptDto>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapPost(UiApiRoutes.LedgerFiscalYearCloseReceipts, async (
            Guid ledgerBookId, int fiscalYear, FinalizeFiscalYearCloseRequestDto request, HttpContext context) =>
        {
            if (!CanAuthorizeFiscalClose(context, out var actor, out var role))
                return EndpointHelpers.Forbidden();
            if (fiscalYear is < 1900 or > 9999)
                return Results.BadRequest(new { error = "Fiscal year must be between 1900 and 9999." });
            if (ResolveJournalStore(context) is not IFiscalYearCloseReceiptStore store || ResolveService(context) is null)
                return ServiceUnavailable();
            if (await ResolveAccessiblePreparationBookAsync(context, ledgerBookId).ConfigureAwait(false) is null)
                return Results.NotFound();
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            try
            {
                var receipt = await store.FinalizeFiscalYearCloseAsync(ledgerBookId, fiscalYear, request,
                    actor, role, scope.TenantId!, scope.CompanyId!, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(receipt, closeJsonOptions);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException) { return FiscalCloseIntegrityConflict(); }
            catch (LedgerPeriodTransitionException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
        })
        .WithName("FinalizeLedgerFiscalYearClose")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<FiscalYearCloseReceiptDto>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static bool CanAuthorizeFiscalClose(HttpContext context, out string actor, out string role)
    {
        actor = string.Empty;
        role = string.Empty;
        return HasLedgerMutationPermission(context)
            && HasAccountingPackageTenantScope(HttpContextWorkstationTenantContextAccessor.Resolve(context))
            && TryResolveControllerRole(context, out role)
            && TryResolveActor(context, out actor)
            && !string.IsNullOrWhiteSpace(actor);
    }

    private static IResult FiscalCloseIntegrityConflict()
        => Results.Problem("The retained fiscal close receipt failed its integrity check.", statusCode: StatusCodes.Status409Conflict);
}
