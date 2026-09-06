using System.Text.Json;
using Meridian.Application.DirectLending;
using Meridian.Identity.Auth;
using Meridian.Contracts.Api;
using Meridian.Contracts.DirectLending;
using Meridian.Ui.Shared.Serialization;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class DirectLendingEndpoints
{
    public static void MapDirectLendingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var requireViewDirectLending = EndpointAuthorization.RequireAny(
            UserPermission.ViewDirectLending,
            UserPermission.ManageDirectLending);
        var requireManageDirectLending = EndpointAuthorization.Require(UserPermission.ManageDirectLending);

        var group = app.MapGroup("/api/loans")
            .WithTags("Direct Lending")
            .AddEndpointFilter(requireViewDirectLending);
        var mutationGroup = app.MapGroup("/api/loans")
            .WithTags("Direct Lending")
            .RequireRateLimiting(UiEndpoints.DirectLendingMutationRateLimitPolicy)
            .AddEndpointFilter(requireViewDirectLending);

        // SEC-001: every loan route exposes or mutates direct-lending contracts, so the group requires
        // at least a direct-lending view permission. Mutation routes additionally require
        // ManageDirectLending below. Routes mapped directly on the app (outside this group) attach the
        // same gates explicitly.
        group.RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);
        mutationGroup.RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<CreateLoanRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var detail = await service.CreateLoanAsync(request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(detail, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("CreateLoan")
        .Produces<LoanContractDetailDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var detail = await service.GetLoanAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("GetLoan")
        .Produces<LoanContractDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{loanId:guid}/projections/contract", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var detail = await service.GetContractProjectionAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("GetLoanContractProjection")
        .Produces<LoanContractDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{loanId:guid}/history", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var history = await service.GetHistoryAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(history, jsonOptions);
        })
        .WithName("GetLoanHistory")
        .Produces<IReadOnlyList<LoanEventLineageDto>>(StatusCodes.Status200OK);

        mutationGroup.MapPost("/{loanId:guid}/rebuild-state", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var snapshot = await service.RebuildStateFromHistoryAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return snapshot is null ? Results.NotFound() : Results.Json(snapshot, jsonOptions);
        })
        .WithName("RebuildLoanState")
        .Produces<LoanAggregateSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/terms-versions", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var versions = await service.GetTermsVersionsAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(versions, jsonOptions);
        })
        .WithName("GetLoanTermsVersions")
        .Produces<IReadOnlyList<LoanTermsVersionDto>>(StatusCodes.Status200OK);

        group.MapGet("/{loanId:guid}/projections/terms-versions", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var versions = await service.GetTermsVersionProjectionsAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(versions, jsonOptions);
        })
        .WithName("GetLoanTermsVersionProjections")
        .Produces<IReadOnlyList<LoanTermsVersionDto>>(StatusCodes.Status200OK);

        mutationGroup.MapPut("/{loanId:guid}/terms", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<AmendLoanTermsRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var detail = await service.AmendTermsAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("AmendLoanTerms")
        .Produces<LoanContractDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/activate", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ActivateLoanRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            var detail = await service.ActivateLoanAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("ActivateLoan")
        .Produces<LoanContractDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/servicing-state", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var servicing = await service.GetServicingStateAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
        })
        .WithName("GetLoanServicingState")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{loanId:guid}/projections/servicing", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var servicing = await service.GetServicingProjectionAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
        })
        .WithName("GetLoanServicingProjection")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{loanId:guid}/projections/drawdown-lots", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var lots = await service.GetDrawdownLotProjectionsAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(lots, jsonOptions);
        })
        .WithName("GetLoanDrawdownLotProjections")
        .Produces<IReadOnlyList<DrawdownLotDto>>(StatusCodes.Status200OK);

        group.MapGet("/{loanId:guid}/projections/revisions", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var revisions = await service.GetServicingRevisionProjectionsAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(revisions, jsonOptions);
        })
        .WithName("GetLoanServicingRevisionProjections")
        .Produces<IReadOnlyList<ServicingRevisionDto>>(StatusCodes.Status200OK);

        group.MapGet("/{loanId:guid}/projections/accruals", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var accruals = await service.GetAccrualEntryProjectionsAsync(loanId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(accruals, jsonOptions);
        })
        .WithName("GetLoanAccrualEntryProjections")
        .Produces<IReadOnlyList<DailyAccrualEntryDto>>(StatusCodes.Status200OK);

        mutationGroup.MapPost("/{loanId:guid}/drawdowns", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<BookDrawdownRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.BookDrawdownAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("BookLoanDrawdown")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/rate-resets", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ApplyRateResetRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.ApplyRateResetAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("ApplyLoanRateReset")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/payments/principal", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ApplyPrincipalPaymentRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.ApplyPrincipalPaymentAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("ApplyLoanPrincipalPayment")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/accruals/daily", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<PostDailyAccrualRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var entry = await service.PostDailyAccrualAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return entry is null ? Results.NotFound() : Results.Json(entry, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("PostDailyLoanAccrual")
        .Produces<DailyAccrualEntryDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/payments", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ApplyMixedPaymentRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.ApplyMixedPaymentAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/cash-transactions", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetCashTransactionsAsync(loanId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        });

        group.MapGet("/{loanId:guid}/payment-allocations", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetPaymentAllocationsAsync(loanId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        });

        mutationGroup.MapPost("/{loanId:guid}/fees", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<AssessFeeRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.AssessFeeAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/fee-balances", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetFeeBalancesAsync(loanId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        });

        group.MapGet("/operations", async (Guid? ledgerBookId, HttpContext context) =>
        {
            var operationsService = context.RequestServices.GetService<DirectLendingOperationsReadService>();
            if (operationsService is null)
            {
                return ServiceUnavailable();
            }

            return Results.Json(await operationsService.GetOperationsAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("GetDirectLendingOperations")
        .Produces<DirectLendingOperationsReadModelDto>(StatusCodes.Status200OK);

        mutationGroup.MapPost("/servicer-statements/preview", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveServicerStatementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var request = JsonSerializer.Deserialize<ServicerStatementImportRequestDto>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Servicer statement import request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var preview = await service.PreviewAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(preview, jsonOptions);
        })
        .WithName("PreviewServicerStatement")
        .Produces<ServicerStatementPreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/servicer-statements/import", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveServicerStatementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ServicerStatementImportRequestDto>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.ImportAsync(request!, metadata, context.RequestAborted).ConfigureAwait(false);
            var statusCode = result.BatchId == Guid.Empty ? StatusCodes.Status400BadRequest : StatusCodes.Status201Created;
            return Results.Json(result, jsonOptions, statusCode: statusCode);
        })
        .WithName("ImportServicerStatement")
        .Produces<ServicerStatementImportResultDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/servicer-statements/{batchId:guid}", async (Guid batchId, HttpContext context) =>
        {
            var service = ResolveServicerStatementService(context);
            var batch = service is null ? null : await service.GetBatchAsync(batchId, context.RequestAborted).ConfigureAwait(false);
            return batch is null ? Results.NotFound() : Results.Json(batch, jsonOptions);
        })
        .WithName("GetServicerStatementBatch")
        .Produces<ServicerStatementPreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/servicer-statements/{batchId:guid}/rows", async (Guid batchId, HttpContext context) =>
        {
            var service = ResolveServicerStatementService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetRowsAsync(batchId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("GetServicerStatementRows")
        .Produces<IReadOnlyList<ServicerStatementRowDto>>(StatusCodes.Status200OK);

        mutationGroup.MapPost("/servicer-statements/{batchId:guid}/apply", async (Guid batchId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveServicerStatementService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ServicerStatementApplyRequestDto>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.ApplyAsync(batchId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
            return result.Status == "NotFound" ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .WithName("ApplyServicerStatementRows")
        .Produces<ServicerStatementApplyResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/collateral", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetCollateralAsync(loanId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("GetLoanCollateral")
        .Produces<IReadOnlyList<CollateralDto>>(StatusCodes.Status200OK);

        mutationGroup.MapPost("/{loanId:guid}/collateral", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<AddCollateralRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.AddCollateralAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("AddLoanCollateral")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/collateral/remove", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<RemoveCollateralRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.RemoveCollateralAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("RemoveLoanCollateral")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPut("/{loanId:guid}/collateral/value", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<UpdateCollateralValueRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.UpdateCollateralValueAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("UpdateLoanCollateralValue")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/status-transitions", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<TransitionLoanStatusRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.TransitionLoanStatusAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("TransitionLoanStatus")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/pik", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<TogglePikRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.TogglePikAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("ToggleLoanPik")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/restructures", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<RestructureLoanRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var contract = await service.RestructureLoanAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return contract is null ? Results.NotFound() : Results.Json(contract, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("RestructureLoan")
        .Produces<LoanContractDetailDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/amortization/discount-premium", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<AmortizeDiscountPremiumRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.AmortizeDiscountPremiumAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("AmortizeLoanDiscountPremium")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/writeoffs", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ApplyWriteOffRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.ApplyWriteOffAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/prepayment-penalties", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ChargePrepaymentPenaltyRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.ChargePrepaymentPenaltyAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("ChargeLoanPrepaymentPenalty")
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/prepayment-penalty", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<ChargePrepaymentPenaltyRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var servicing = await service.ChargePrepaymentPenaltyAsync(loanId, request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return servicing is null ? Results.NotFound() : Results.Json(servicing, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .Produces<LoanServicingStateDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/projections", async (Guid loanId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            // ADR-014: Use source-generated context to eliminate reflection overhead.
            var request = JsonSerializer.Deserialize(body.GetRawText(), DirectLendingJsonContext.Default.RequestProjectionRunRequest);
            try
            {
                var projection = await service.RequestProjectionAsync(loanId, request?.ProjectionAsOf, context.RequestAborted, MergeMetadata(null, context)).ConfigureAwait(false);
                return Results.Json(projection, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/projections", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetProjectionsAsync(loanId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        });

        app.MapGet("/api/projections/{projectionRunId:guid}/flows", async (Guid projectionRunId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetProjectedCashFlowsAsync(projectionRunId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/journals", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetJournalsAsync(loanId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        });

        app.MapPost("/api/journals/{journalEntryId:guid}/post", async (Guid journalEntryId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var entry = await service.PostJournalAsync(journalEntryId, context.RequestAborted).ConfigureAwait(false);
                return entry is null ? Results.NotFound() : Results.Json(entry, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.DirectLendingMutationRateLimitPolicy)
        .RequirePermission(UserPermission.ManageDirectLending);

        mutationGroup.MapPost("/{loanId:guid}/reconcile", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var run = await service.ReconcileAsync(loanId, context.RequestAborted, MergeMetadata(null, context)).ConfigureAwait(false);
                return run is null ? Results.NotFound() : Results.Json(run, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequirePermission(UserPermission.ManageDirectLending);

        group.MapGet("/{loanId:guid}/reconciliation-runs", async (Guid loanId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetReconciliationRunsAsync(loanId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        });

        app.MapGet("/api/reconciliation/{runId:guid}/results", async (Guid runId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetReconciliationResultsAsync(runId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        app.MapGet("/api/reconciliation/exceptions", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetReconciliationExceptionsAsync(context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        app.MapPost("/api/reconciliation/exceptions/{exceptionId:guid}/resolve", async (Guid exceptionId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            // ADR-014: Use source-generated context to eliminate reflection overhead.
            var request = JsonSerializer.Deserialize(body.GetRawText(), DirectLendingJsonContext.Default.ResolveReconciliationExceptionRequest);
            if (request is null)
            {
                return Results.Problem("Resolution request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await service.ResolveReconciliationExceptionAsync(exceptionId, request, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.DirectLendingMutationRateLimitPolicy)
        .RequirePermission(UserPermission.ManageDirectLending);

        app.MapPost("/api/servicer-reports", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryBindCommand<CreateServicerReportBatchRequest>(body, jsonOptions, context, out var request, out var metadata, out var error))
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var batch = await service.CreateServicerReportBatchAsync(request!, metadata, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(batch, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.DirectLendingMutationRateLimitPolicy)
        .RequirePermission(UserPermission.ManageDirectLending);

        app.MapGet("/api/servicer-reports/{batchId:guid}", async (Guid batchId, HttpContext context) =>
        {
            var service = ResolveService(context);
            var batch = service is null ? null : await service.GetServicerReportBatchAsync(batchId, context.RequestAborted).ConfigureAwait(false);
            return batch is null ? Results.NotFound() : Results.Json(batch, jsonOptions);
        })
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        app.MapGet("/api/servicer-reports/{batchId:guid}/position-lines", async (Guid batchId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetServicerPositionLinesAsync(batchId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        app.MapGet("/api/servicer-reports/{batchId:guid}/transaction-lines", async (Guid batchId, HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetServicerTransactionLinesAsync(batchId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        app.MapGet("/api/loans/rebuild-checkpoints", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetRebuildCheckpointsAsync(context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        app.MapGet(UiApiRoutes.DirectLendingPortfolioSummary, async (HttpContext context) =>
        {
            var service = ResolveService(context);
            return service is null
                ? ServiceUnavailable()
                : Results.Json(await service.GetPortfolioSummaryAsync(context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("GetPortfolioSummary")
        .Produces<LoanPortfolioSummaryDto>(StatusCodes.Status200OK)
        .RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending);

        app.MapPost("/api/loans/rebuild-all", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var snapshots = await service.RebuildAllAsync(context.RequestAborted).ConfigureAwait(false);
                return Results.Json(snapshots, jsonOptions);
            }
            catch (DirectLendingCommandException ex)
            {
                return ToProblem(ex);
            }
        })
        .AddEndpointFilter(requireManageDirectLending)
        .RequireFundScopedWriteTenant()
        .RequireRateLimiting(UiEndpoints.DirectLendingMutationRateLimitPolicy)
        .RequirePermission(UserPermission.ManageDirectLending);
    }

    private static IDirectLendingService? ResolveService(HttpContext context) =>
        context.RequestServices.GetService<IDirectLendingService>();

    private static IDirectLendingServicerStatementService? ResolveServicerStatementService(HttpContext context) =>
        context.RequestServices.GetService<IDirectLendingServicerStatementService>();

    private static IResult ServiceUnavailable() =>
        Results.Problem("Direct lending service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult ToProblem(DirectLendingCommandException exception)
        => Results.Problem(exception.Message, statusCode: exception.Error.Code switch
        {
            DirectLendingErrorCode.NotFound => StatusCodes.Status404NotFound,
            DirectLendingErrorCode.ConcurrencyConflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        });

    private static bool TryBindCommand<TCommand>(
        JsonElement body,
        JsonSerializerOptions jsonOptions,
        HttpContext context,
        out TCommand? command,
        out DirectLendingCommandMetadataDto metadata,
        out string? error)
    {
        error = null;

        // ADR-014 note: DirectLendingCommandEnvelope<TCommand> and TCommand are open-generic type
        // parameters resolved at runtime. System.Text.Json source generators require concrete types
        // known at compile time and cannot generate metadata for open generics. Reflection-based
        // deserialization is therefore unavoidable here and is accepted as a documented exception.
        var envelope = JsonSerializer.Deserialize<DirectLendingCommandEnvelope<TCommand>>(body.GetRawText(), jsonOptions);
        if (envelope is not null && envelope.Command is not null)
        {
            command = envelope.Command;
            metadata = MergeMetadata(envelope.Metadata, context);
            return true;
        }

        // ADR-014 note: Same open-generic constraint applies here — TCommand is not statically known.
        command = JsonSerializer.Deserialize<TCommand>(body.GetRawText(), jsonOptions);
        if (command is null)
        {
            metadata = MergeMetadata(metadata: null, context);
            error = $"Request body could not be deserialized as {typeof(TCommand).Name} or DirectLendingCommandEnvelope<{typeof(TCommand).Name}>.";
            return false;
        }

        metadata = MergeMetadata(metadata: null, context);
        return true;
    }

    private static DirectLendingCommandMetadataDto MergeMetadata(DirectLendingCommandMetadataDto? metadata, HttpContext context)
        => new(
            CommandId: metadata?.CommandId ?? TryParseGuidHeader(context, "X-Command-Id"),
            CorrelationId: metadata?.CorrelationId ?? TryParseGuidHeader(context, "X-Correlation-Id"),
            CausationId: metadata?.CausationId ?? TryParseGuidHeader(context, "X-Causation-Id"),
            SourceSystem: metadata?.SourceSystem ?? context.Request.Headers["X-Source-System"].ToString(),
            ReplayFlag: false);

    private static Guid? TryParseGuidHeader(HttpContext context, string headerName)
        => Guid.TryParse(context.Request.Headers[headerName].ToString(), out var value) ? value : null;
}
