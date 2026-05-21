using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class LedgerEndpoints
{
    public static void MapLedgerEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerBooks, async (
            string? fundProfileId,
            Guid? fundStructureNodeId,
            AccountingBasisKindDto? accountingBasis,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var books = await service
                .ListBooksAsync(new LedgerBookQuery(fundProfileId, fundStructureNodeId, AccountingBasis: accountingBasis), context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(books, jsonOptions);
        })
        .WithName("ListLedgerBooks")
        .Produces<IReadOnlyList<LedgerBookDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapGet(UiApiRoutes.LedgerBookById, async (Guid ledgerBookId, HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var book = await service.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
            return book is null
                ? Results.NotFound(new { error = $"Ledger book '{ledgerBookId}' was not found." })
                : Results.Json(book, jsonOptions);
        })
        .WithName("GetLedgerBook")
        .Produces<LedgerBookDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerBooks, async (CreateLedgerBookRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var book = await service.CreateBookAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(book, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (LedgerBookServiceException ex)
            {
                return MapServiceException(ex);
            }
        })
        .WithName("CreateLedgerBook")
        .Produces<LedgerBookDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapGet(UiApiRoutes.LedgerPeriods, async (
            Guid? ledgerBookId,
            string? fundProfileId,
            Guid? fundStructureNodeId,
            LedgerPeriodStatusDto? status,
            bool? openOnly,
            AccountingBasisKindDto? accountingBasis,
            HttpContext context) =>
        {
            if (!HasLedgerReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            var periods = await service
                .ListPeriodsAsync(
                    new LedgerPeriodQuery(
                        ledgerBookId,
                        fundProfileId,
                        fundStructureNodeId,
                        status,
                        OpenOnly: openOnly == true,
                        AccountingBasis: accountingBasis),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(periods, jsonOptions);
        })
        .WithName("ListLedgerPeriods")
        .Produces<IReadOnlyList<LedgerPeriodDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status501NotImplemented);

        app.MapPost(UiApiRoutes.LedgerPeriods, async (CreateLedgerPeriodRequest request, HttpContext context) =>
        {
            if (!HasLedgerMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var period = await service.CreatePeriodAsync(request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(period, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (LedgerBookServiceException ex)
            {
                return MapServiceException(ex);
            }
        })
        .WithName("CreateLedgerPeriod")
        .Produces<LedgerPeriodDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        app.MapPost(UiApiRoutes.LedgerPeriodClose, async (
            Guid periodId,
            CloseLedgerPeriodRequest request,
            HttpContext context) =>
        {
            if (!TryGetLedgerCloseActor(context, out var actor))
            {
                return Results.Forbid();
            }

            var service = ResolveService(context);
            if (service is null)
            {
                return ServiceUnavailable();
            }

            try
            {
                var trustedRequest = request with { ClosedBy = actor };
                var result = await service
                    .ClosePeriodAsync(
                        periodId,
                        request with
                        {
                            ClosedBy = actor
                        },
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (LedgerBookServiceException ex)
            {
                return MapServiceException(ex);
            }
        })
        .WithName("CloseLedgerPeriod")
        .Produces<LedgerPeriodCloseResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static ILedgerBookService? ResolveService(HttpContext context)
        => context.RequestServices.GetService<ILedgerBookService>();

    private static IResult ServiceUnavailable()
        => Results.Problem("Ledger book service is not registered.", statusCode: StatusCodes.Status501NotImplemented);

    private static bool HasLedgerReadPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending);

    private static bool HasLedgerMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending);

    private static bool HasLedgerClosePermission(HttpContext context)
        => HasLedgerMutationPermission(context);

    private static bool TryResolveActor(HttpContext context, out string actor)
        => EndpointAuthorization.TryResolveActor(context, out actor);

    private static IResult MapServiceException(LedgerBookServiceException exception)
        => exception switch
        {
            LedgerBookNotFoundException => Results.NotFound(new { error = exception.Message }),
            LedgerBookValidationException or LedgerPeriodTransitionException => Results.BadRequest(new { error = exception.Message }),
            _ => Results.Problem(exception.Message)
        };

    private static bool TryGetLedgerCloseActor(HttpContext context, out string actor)
    {
        actor = string.Empty;
        if (context.Items[LoginSessionMiddleware.CurrentUserKey] is not string username ||
            string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        if (context.Items[LoginSessionMiddleware.CurrentUserRoleKey] is not UserRole role)
        {
            return false;
        }

        if (role is not UserRole.Admin and not UserRole.Accounting)
        {
            return false;
        }

        actor = username.Trim();
        return true;
    }
}
