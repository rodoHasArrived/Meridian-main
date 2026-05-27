using System.Text.Json;
using Meridian.Application.Banking;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Banking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class BankingEndpoints
{
    public static void MapBankingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/banking").WithTags("Banking");

        // -------------------------------------------------------------------
        // Payment initiation & approval workflow
        // -------------------------------------------------------------------

        group.MapPost("/payments", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var request = JsonSerializer.Deserialize<InitiatePaymentRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            // entityId comes from a query parameter so callers can associate payments with any entity
            if (!Guid.TryParse(context.Request.Query["entityId"], out var entityId))
            {
                return Results.Problem("'entityId' query parameter is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var pending = await service.InitiatePaymentAsync(entityId, request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(pending, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (BankingException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("InitiatePayment")
        .Produces<PendingPaymentDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/payments/pending", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryGetRequiredEntityId(context, out var entityId, out var entityError))
            {
                return entityError!;
            }

            if (!HasPermission(context, UserPermission.ViewDirectLending))
            {
                return Results.Problem("Forbidden.", statusCode: StatusCodes.Status403Forbidden);
            }

            var results = await service.GetPendingPaymentsAsync(entityId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetPendingPayments")
        .Produces<IReadOnlyList<PendingPaymentDto>>(StatusCodes.Status200OK);

        group.MapPost("/payments/{pendingPaymentId:guid}/approve", async (Guid pendingPaymentId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryGetRequiredEntityId(context, out var entityId, out var entityError))
            {
                return entityError!;
            }

            if (!TryGetCurrentUsername(context, out var currentUser))
            {
                return Results.Problem("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!HasPermission(context, UserPermission.ManageDirectLending))
            {
                return Results.Problem("Forbidden.", statusCode: StatusCodes.Status403Forbidden);
            }

            var pending = await service.GetPendingPaymentsAsync(entityId, context.RequestAborted).ConfigureAwait(false);
            if (!pending.Any(p => p.PendingPaymentId == pendingPaymentId))
            {
                return Results.NotFound();
            }

            var request = JsonSerializer.Deserialize<ApprovePaymentRequest>(body.GetRawText(), jsonOptions)
                          ?? new ApprovePaymentRequest(ReviewNotes: null, ReviewedBy: null);
            var serverRequest = request with { ReviewedBy = currentUser };

            try
            {
                var result = await service.ApprovePaymentAsync(pendingPaymentId, serverRequest, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }
            catch (BankingException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("ApprovePayment")
        .Produces<PendingPaymentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/payments/{pendingPaymentId:guid}/reject", async (Guid pendingPaymentId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryGetRequiredEntityId(context, out var entityId, out var entityError))
            {
                return entityError!;
            }

            if (!TryGetCurrentUsername(context, out var currentUser))
            {
                return Results.Problem("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!HasPermission(context, UserPermission.ManageDirectLending))
            {
                return Results.Problem("Forbidden.", statusCode: StatusCodes.Status403Forbidden);
            }

            var pending = await service.GetPendingPaymentsAsync(entityId, context.RequestAborted).ConfigureAwait(false);
            if (!pending.Any(p => p.PendingPaymentId == pendingPaymentId))
            {
                return Results.NotFound();
            }

            var request = JsonSerializer.Deserialize<RejectPaymentRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Rejection request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var serverRequest = request with { ReviewedBy = currentUser };

            try
            {
                var result = await service.RejectPaymentAsync(pendingPaymentId, serverRequest, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }
            catch (BankingException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("RejectPayment")
        .Produces<PendingPaymentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // -------------------------------------------------------------------
        // Bank transaction records
        // -------------------------------------------------------------------

        group.MapGet("/transactions", async (HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            Guid? entityId = Guid.TryParse(context.Request.Query["entityId"], out var eid) ? eid : null;
            var results = await service.GetBankTransactionsAsync(entityId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetBankTransactions")
        .Produces<IReadOnlyList<BankTransactionDto>>(StatusCodes.Status200OK);

        // -------------------------------------------------------------------
        // Bank transaction seeding (development / demo / integration-test only)
        // -------------------------------------------------------------------

        app.MapPost("/api/dev/seed/bank-transactions", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var request = JsonSerializer.Deserialize<BankTransactionSeedRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Seed request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var result = await service.SeedBankTransactionsAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (BankingException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("SeedBankTransactions")
        .Produces<BankTransactionSeedResultDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);
    }


    private static bool TryGetRequiredEntityId(HttpContext context, out Guid entityId, out IResult? error)
    {
        if (!Guid.TryParse(context.Request.Query["entityId"], out entityId))
        {
            error = Results.Problem("'entityId' query parameter is required.", statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetCurrentUsername(HttpContext context, out string username)
    {
        username = context.Items[LoginSessionMiddleware.CurrentUserKey] as string ?? string.Empty;
        return !string.IsNullOrWhiteSpace(username);
    }

    private static bool HasPermission(HttpContext context, UserPermission requiredPermission)
    {
        if (context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] is not UserPermission permissions)
        {
            return false;
        }

        return (permissions & requiredPermission) == requiredPermission;
    }

    private static IBankingService? ResolveService(HttpContext context) =>
        context.RequestServices.GetService<IBankingService>();

    private static IResult ServiceUnavailable() =>
        Results.Problem("Banking service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult ToProblem(BankingException exception) =>
        Results.Problem(exception.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
}
