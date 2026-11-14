using System.Text.Json;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundAccountEndpoints
{
    private static void MapThreeWayReconciliationEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapPost("/api/fund-accounts/{accountId:guid}/brokerage-sync/reconcile-three-way",
            async (Guid accountId, ThreeWayReconciliationRequestDto request, HttpContext context) =>
            {
                if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context, requireWriteAccess: true).ConfigureAwait(false))
                    return EndpointHelpers.Forbidden();
                var service = context.RequestServices.GetService<ThreeWayReconciliationService>();
                if (service is null)
                    return Results.Problem("Three-way accounting reconciliation is unavailable.", statusCode: StatusCodes.Status501NotImplemented);
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                try
                {
                    var result = await service.RunAsync(accountId, new(tenant.TenantId!, tenant.CompanyId!), request,
                        context.RequestAborted).ConfigureAwait(false);
                    return Results.Json(result, jsonOptions);
                }
                catch (UnauthorizedAccessException)
                {
                    return EndpointHelpers.Forbidden();
                }
                catch (ArgumentException exception)
                {
                    return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
                }
            })
            .WithName("RunThreeWayAccountingReconciliation").WithTags("Fund Accounts")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
            .RequireWorkstationTenantCompanyScope();

        app.MapPost("/api/fund-accounts/{accountId:guid}/brokerage-sync/three-way-reconciliation/adjustments/prepare",
            async (Guid accountId, PrepareThreeWayAdjustmentRequestDto request, HttpContext context) =>
            {
                if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context, requireWriteAccess: true).ConfigureAwait(false))
                    return EndpointHelpers.Forbidden();
                if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                    return Results.Unauthorized();
                var service = context.RequestServices.GetService<ThreeWayAdjustmentDraftService>();
                if (service is null)
                    return Results.Problem("Governed correction preparation is unavailable.", statusCode: StatusCodes.Status501NotImplemented);
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                try
                {
                    var draft = await service.PrepareAsync(accountId, new(tenant.TenantId!, tenant.CompanyId!), request, actor,
                        EndpointAuthorization.ResolveReportGroupPrincipalIds(context), context.RequestAborted).ConfigureAwait(false);
                    return Results.Json(draft, jsonOptions);
                }
                catch (UnauthorizedAccessException)
                {
                    return EndpointHelpers.Forbidden();
                }
                catch (ArgumentException exception)
                {
                    return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
                }
            })
            .WithName("PrepareThreeWayReconciliationAdjustment").WithTags("Fund Accounts")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageDirectLending)
            .RequireWorkstationTenantCompanyScope();

        app.MapGet("/api/fund-accounts/{accountId:guid}/brokerage-sync/three-way-reconciliation/latest",
            async (Guid accountId, Guid ledgerBookId, Guid accountingPeriodId, HttpContext context) =>
            {
                if (!await CanAccessFundAccountBrokerageSyncAsync(accountId, context).ConfigureAwait(false))
                    return EndpointHelpers.Forbidden();
                var service = context.RequestServices.GetService<ThreeWayReconciliationService>();
                if (service is null)
                    return Results.Problem("Three-way accounting reconciliation is unavailable.", statusCode: StatusCodes.Status501NotImplemented);
                var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
                try
                {
                    var result = await service.GetLatestAsync(accountId, new(tenant.TenantId!, tenant.CompanyId!),
                        ledgerBookId, accountingPeriodId, context.RequestAborted).ConfigureAwait(false);
                    return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
                }
                catch (UnauthorizedAccessException)
                {
                    return EndpointHelpers.Forbidden();
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
                }
            })
            .WithName("GetLatestThreeWayAccountingReconciliation").WithTags("Fund Accounts")
            .RequirePermission(UserPermission.ViewTrades).RequireWorkstationTenantCompanyScope();
    }
}
