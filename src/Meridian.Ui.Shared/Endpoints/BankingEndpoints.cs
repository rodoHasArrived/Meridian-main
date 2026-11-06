using System.Text.Json;
using Meridian.FinancialOperations.Banking;
using Meridian.Identity.Auth;
using Meridian.Contracts.Banking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Maps banking payment initiation, approval, and status operations to the shared HTTP API.
/// </summary>
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

            if (!TryDeserialize<InitiatePaymentRequest>(body, jsonOptions, out var request, out var parseError))
            {
                return parseError!;
            }

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
        .RequirePermission(UserPermission.ManageDirectLending)
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

            var results = await service.GetPendingPaymentsAsync(entityId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(results, jsonOptions);
        })
        .WithName("GetPendingPayments")
        .RequirePermission(UserPermission.ViewDirectLending)
        .Produces<IReadOnlyList<PendingPaymentDto>>(StatusCodes.Status200OK);

        group.MapPost("/payments/{pendingPaymentId:guid}/currency-remediation", async (Guid pendingPaymentId, JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
                return ServiceUnavailable();
            if (!TryGetRequiredEntityId(context, out var entityId, out var entityError))
                return entityError!;
            if (!TryGetCurrentUsername(context, out var currentUser))
                return Results.Problem("Unauthorized.", statusCode: StatusCodes.Status401Unauthorized);

            var payment = await service.GetPaymentAsync(
                pendingPaymentId,
                context.RequestAborted).ConfigureAwait(false);
            if (payment is null || payment.EntityId != entityId)
                return Results.NotFound();

            if (!TryDeserialize<RemediatePaymentCurrencyRequest>(body, jsonOptions, out var request, out var parseError))
                return parseError!;
            if (request is null)
            {
                return Results.Problem(
                    "Currency remediation request body is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var serverRequest = request with
            {
                RemediatedBy = currentUser,
                // The body's HumanOperator default is not proof of a human caller; derive the
                // origin from the authenticated principal so API keys cannot pass the
                // service's human-only gate.
                ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
            };
            try
            {
                var result = await service.RemediatePaymentCurrencyAsync(
                    pendingPaymentId,
                    serverRequest,
                    context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
            }
            catch (BankingException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("RemediatePaymentCurrency")
        .RequirePermission(UserPermission.ManageDirectLending)
        .Produces<PendingPaymentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

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

            var payment = await service.GetPaymentAsync(pendingPaymentId, context.RequestAborted).ConfigureAwait(false);
            if (payment is null || payment.EntityId != entityId)
            {
                return Results.NotFound();
            }

            if (!TryDeserialize<ApprovePaymentRequest>(body, jsonOptions, out var parsedApproval, out var parseError))
            {
                return parseError!;
            }

            var request = parsedApproval ?? new ApprovePaymentRequest(ReviewNotes: null, ReviewedBy: null);
            var serverRequest = request with
            {
                ReviewedBy = currentUser,
                ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
            };

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
        .RequirePermission(UserPermission.ManageDirectLending)
        .Produces<PendingPaymentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
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

            var payment = await service.GetPaymentAsync(pendingPaymentId, context.RequestAborted).ConfigureAwait(false);
            if (payment is null || payment.EntityId != entityId)
            {
                return Results.NotFound();
            }

            if (!TryDeserialize<RejectPaymentRequest>(body, jsonOptions, out var request, out var parseError))
            {
                return parseError!;
            }

            if (request is null)
            {
                return Results.Problem("Rejection request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var serverRequest = request with
            {
                ReviewedBy = currentUser,
                ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
            };

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
        .RequirePermission(UserPermission.ManageDirectLending)
        .Produces<PendingPaymentDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/payments/{pendingPaymentId:guid}/bank-evidence", async (Guid pendingPaymentId, JsonElement body, HttpContext context) =>
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

            var payment = await service.GetPaymentAsync(pendingPaymentId, context.RequestAborted).ConfigureAwait(false);
            if (payment is null || payment.EntityId != entityId)
            {
                return Results.NotFound();
            }

            if (!TryDeserialize<RecordPaymentBankEvidenceRequest>(body, jsonOptions, out var request, out var parseError))
            {
                return parseError!;
            }

            if (request is null)
            {
                return Results.Problem("Bank evidence request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var serverRequest = request with
            {
                RecordedBy = currentUser,
                ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
            };

            try
            {
                var result = await service.RecordPaymentBankEvidenceAsync(pendingPaymentId, serverRequest, context.RequestAborted).ConfigureAwait(false);
                return result is null ? Results.NotFound() : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (BankingException ex)
            {
                return ToProblem(ex);
            }
        })
        .WithName("RecordPaymentBankEvidence")
        .RequirePermission(UserPermission.ManageDirectLending)
        .Produces<BankTransactionDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
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
        .RequirePermission(UserPermission.ViewDirectLending)
        .Produces<IReadOnlyList<BankTransactionDto>>(StatusCodes.Status200OK);

        // -------------------------------------------------------------------
        // Bank transaction seeding (development / demo / integration-test only)
        // -------------------------------------------------------------------

        // The seed route writes synthetic bank activity into whatever banking store is registered,
        // so it is only mapped in Development and Test hosts. Every other host (Staging,
        // Production, packaged builds) never exposes it.
        if (!IsSeedEnvironment(app.Environment))
        {
            return;
        }

        app.MapPost("/api/dev/seed/bank-transactions", async (JsonElement body, HttpContext context) =>
        {
            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            if (!TryDeserialize<BankTransactionSeedRequest>(body, jsonOptions, out var request, out var parseError))
            {
                return parseError!;
            }

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
        .RequirePermission(UserPermission.AdminMaintenance)
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

    internal static bool IsSeedEnvironment(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Test");

    /// <summary>
    /// Deserializes a request body, converting malformed or type-mismatched JSON into a 400
    /// problem response instead of letting <see cref="JsonException"/> escape as a 500.
    /// </summary>
    private static bool TryDeserialize<T>(
        JsonElement body,
        JsonSerializerOptions jsonOptions,
        out T? value,
        out IResult? error)
        where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(body.GetRawText(), jsonOptions);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            value = null;
            error = Results.Problem(
                "Request body is not valid for this operation.",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }
    }

    private static IBankingService? ResolveService(HttpContext context) =>
        context.RequestServices.GetService<IBankingService>();

    private static IResult ServiceUnavailable() =>
        Results.Problem("Banking service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static IResult ToProblem(BankingException exception) =>
        Results.Problem(
            exception.Message,
            statusCode: exception is BankingConflictException
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status422UnprocessableEntity);
}
