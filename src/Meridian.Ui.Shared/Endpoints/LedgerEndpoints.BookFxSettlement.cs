using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapBookFxSettlementEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var settlementJson = new JsonSerializerOptions(jsonOptions);
        settlementJson.Converters.Insert(0, new ReportingIncomeDecimalJsonConverter());

        app.MapGet(UiApiRoutes.LedgerBookFxSettlementSet, async (Guid setId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
                return EndpointHelpers.Forbidden();
            if (!TryResolveBookFxScope(context, out var tenant, out var company))
                return EndpointHelpers.Forbidden();
            if (ResolveJournalStore(context) is not ILedgerBookFxSettlementStore store || ResolveService(context) is null)
                return ServiceUnavailable();
            try
            {
                var result = await store.GetBookFxSettlementSetAsync(setId, tenant, company, context.RequestAborted)
                    .ConfigureAwait(false);
                if (result is null || !await CanReadBookFxPairAsync(context, result.Plan.Input).ConfigureAwait(false))
                    return Results.NotFound();
                return Results.Json(result, settlementJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                return BookFxError(ex);
            }
        })
        .WithName("GetLedgerBookFxSettlementSet")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending,
            UserPermission.ViewLedgerReports, UserPermission.ManageLedgerReports)
        .Produces<BookFxSettlementSetDto>();

        app.MapPost(UiApiRoutes.LedgerBookFxSettlementSets, async (RetainBookFxSettlementSetRequestDto request, HttpContext context) =>
        {
            if (!CanAuthorizeFiscalClose(context, out var actor, out var role)
                || !TryResolveBookFxScope(context, out var tenant, out var company))
                return EndpointHelpers.Forbidden();
            if (ResolveJournalStore(context) is not ILedgerBookFxSettlementStore store || ResolveService(context) is null)
                return ServiceUnavailable();
            if (!await CanReadBookFxPairAsync(context, request).ConfigureAwait(false))
                return Results.NotFound();
            try
            {
                var result = await store.RetainBookFxSettlementSetAsync(request, actor, role, tenant, company, context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, settlementJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException
                or LedgerValidationException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                return BookFxError(ex);
            }
        })
        .WithName("RetainLedgerBookFxSettlementSet")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<BookFxSettlementSetDto>();

        app.MapPost(UiApiRoutes.LedgerBookFxSettlementCapture, async (
            Guid setId, CaptureBookFxSettlementLegRequestDto request, HttpContext context) =>
        {
            if (!CanAuthorizeFiscalClose(context, out var actor, out var role)
                || !TryResolveBookFxScope(context, out var tenant, out var company))
                return EndpointHelpers.Forbidden();
            if (ResolveJournalStore(context) is not ILedgerBookFxSettlementStore store || ResolveService(context) is null)
                return ServiceUnavailable();
            try
            {
                var retained = await store.GetBookFxSettlementSetAsync(setId, tenant, company, context.RequestAborted)
                    .ConfigureAwait(false);
                if (retained is null || !await CanReadBookFxPairAsync(context, retained.Plan.Input).ConfigureAwait(false))
                    return Results.NotFound();
                var receipt = await store.CaptureBookFxSettlementLegAsync(setId, request, actor, role, tenant, company,
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Json(receipt, settlementJson);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException
                or LedgerValidationException or JsonException or LedgerBookNotFoundException or UnauthorizedAccessException)
            {
                return BookFxError(ex);
            }
        })
        .WithName("CaptureLedgerBookFxSettlementLeg")
        .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending, UserPermission.ManageLedgerReports)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<BookFxSettlementLegReceiptDto>();
    }

    private static bool TryResolveBookFxScope(HttpContext context, out string tenant, out string company)
    {
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        tenant = scope.TenantId ?? string.Empty;
        company = scope.CompanyId ?? string.Empty;
        return HasAccountingPackageTenantScope(scope);
    }

    private static async Task<bool> CanReadBookFxPairAsync(HttpContext context, RetainBookFxSettlementSetRequestDto input)
        => await ResolveAccessiblePreparationBookAsync(context, input.GaapBookId).ConfigureAwait(false) is not null
            && await ResolveAccessiblePreparationBookAsync(context, input.CashBookId).ConfigureAwait(false) is not null;

    private static IResult BookFxError(Exception exception)
        => exception switch
        {
            UnauthorizedAccessException => EndpointHelpers.Forbidden(),
            LedgerBookNotFoundException => Results.NotFound(),
            ArgumentException => Results.BadRequest(new { error = exception.Message }),
            OverflowException => Results.BadRequest(new { error = "The settlement input amounts exceed supported decimal precision." }),
            InvalidDataException or JsonException => Results.Problem(
                "Retained settlement evidence failed its integrity check.", statusCode: StatusCodes.Status409Conflict),
            _ => Results.Conflict(new { error = exception.Message })
        };
}
