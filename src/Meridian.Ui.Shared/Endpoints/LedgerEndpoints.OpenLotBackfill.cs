using System.Text.Json;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.Api;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    private static void MapOpenLotBackfillEndpoints(WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapGet(UiApiRoutes.LedgerOpenLotBackfillExceptions, async (Guid ledgerBookId, HttpContext context) =>
        {
            var rejection = await AuthorizeOpenLotBackfillAsync(context, ledgerBookId).ConfigureAwait(false);
            if (rejection is not null)
            {
                return rejection;
            }

            var store = context.RequestServices.GetService<IOpenLotBackfillStore>();
            return store is null ? ServiceUnavailable() : Results.Json(
                await store.ListExceptionsAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("ListOpenLotBackfillExceptions").RequirePermission(UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .Produces<IReadOnlyList<OpenLotBackfillExceptionDto>>(StatusCodes.Status200OK);

        app.MapPost(UiApiRoutes.LedgerOpenLotBackfillSurvey, async (Guid ledgerBookId, HttpContext context) =>
        {
            var rejection = await AuthorizeOpenLotBackfillAsync(context, ledgerBookId).ConfigureAwait(false);
            if (rejection is not null)
            {
                return rejection;
            }

            var store = context.RequestServices.GetService<IOpenLotBackfillStore>();
            return store is null ? ServiceUnavailable() : Results.Json(
                await store.SurveyAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("SurveyOpenLotBackfill").RequirePermission(UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<IReadOnlyList<OpenLotBackfillExceptionDto>>(StatusCodes.Status200OK);

        app.MapGet(UiApiRoutes.LedgerOpenLotBackfillEvidenceById, async (
            Guid ledgerBookId, Guid evidenceRecordId, HttpContext context) =>
        {
            var rejection = await AuthorizeOpenLotBackfillAsync(context, ledgerBookId).ConfigureAwait(false);
            if (rejection is not null)
            {
                return rejection;
            }

            var store = context.RequestServices.GetService<IOpenLotBackfillStore>();
            if (store is null)
            {
                return ServiceUnavailable();
            }

            var evidence = await store.GetEvidenceAsync(ledgerBookId, evidenceRecordId, context.RequestAborted).ConfigureAwait(false);
            return evidence is null ? Results.NotFound() : Results.Json(evidence, jsonOptions);
        })
        .WithName("GetOpenLotBackfillEvidence").RequirePermission(UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .Produces<OpenLotBackfillEvidenceDto>(StatusCodes.Status200OK);

        app.MapPost(UiApiRoutes.LedgerOpenLotBackfillEvidence, async (
            Guid ledgerBookId, RetainOpenLotBackfillEvidenceRequest request, HttpContext context) =>
        {
            if (request.LedgerBookId != ledgerBookId)
            {
                return Results.BadRequest(new { error = "The evidence book must match the requested book." });
            }

            return await ExecuteOpenLotBackfillAsync(context, ledgerBookId, async (store, actor) =>
                await store.RetainEvidenceAsync(request with { Actor = actor }, context.RequestAborted).ConfigureAwait(false), jsonOptions)
                .ConfigureAwait(false);
        })
        .WithName("RetainOpenLotBackfillEvidence").RequirePermission(UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .RequireAuthenticatedSession()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<OpenLotBackfillEvidenceDto>(StatusCodes.Status200OK);

        app.MapPost(UiApiRoutes.LedgerOpenLotBackfillEvidenceReview, async (
            Guid ledgerBookId, Guid evidenceRecordId, ReviewOpenLotBackfillEvidenceRequest request, HttpContext context) =>
        {
            if (request.LedgerBookId != ledgerBookId || request.EvidenceRecordId != evidenceRecordId)
            {
                return Results.BadRequest(new { error = "The evidence and book must match the requested review." });
            }

            return await ExecuteOpenLotBackfillAsync(context, ledgerBookId, async (store, actor) =>
                await store.ReviewEvidenceAsync(request with
                {
                    Actor = actor,
                    ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
                }, context.RequestAborted).ConfigureAwait(false), jsonOptions).ConfigureAwait(false);
        })
        .WithName("ReviewOpenLotBackfillEvidence").RequirePermission(UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .RequireAuthenticatedSession()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<OpenLotBackfillEvidenceDto>(StatusCodes.Status200OK);

        app.MapPost(UiApiRoutes.LedgerOpenLotBackfillApply, async (
            Guid ledgerBookId, ApplyOpenLotBackfillRequest request, HttpContext context) =>
        {
            if (request.LedgerBookId != ledgerBookId)
            {
                return Results.BadRequest(new { error = "The backfill book must match the requested book." });
            }

            return await ExecuteOpenLotBackfillAsync(context, ledgerBookId, async (store, actor) =>
                await store.ApplyAsync(request with
                {
                    Actor = actor,
                    ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin)
                }, context.RequestAborted).ConfigureAwait(false), jsonOptions).ConfigureAwait(false);
        })
        .WithName("ApplyOpenLotBackfill").RequirePermission(UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .RequireAuthenticatedSession()
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<OpenLotBackfillReceiptDto>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> ExecuteOpenLotBackfillAsync<T>(
        HttpContext context, Guid ledgerBookId,
        Func<IOpenLotBackfillStore, string, Task<T>> execute, JsonSerializerOptions jsonOptions)
    {
        var rejection = await AuthorizeOpenLotBackfillAsync(context, ledgerBookId).ConfigureAwait(false);
        if (rejection is not null)
        {
            return rejection;
        }

        if (!TryResolveActor(context, out var actor))
        {
            return EndpointHelpers.Forbidden();
        }

        var store = context.RequestServices.GetService<IOpenLotBackfillStore>();
        if (store is null)
        {
            return ServiceUnavailable();
        }

        try
        {
            return Results.Json(await execute(store, actor).ConfigureAwait(false), jsonOptions);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return ApiProblemDetails.Conflict(context, exception.Message);
        }
        catch (LedgerValidationException exception)
        {
            return ApiProblemDetails.Conflict(context, exception.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return EndpointHelpers.Forbidden();
        }
    }

    private static async Task<IResult?> AuthorizeOpenLotBackfillAsync(HttpContext context, Guid ledgerBookId)
    {
        if (!HasLedgerCertificationPermission(context))
        {
            return EndpointHelpers.Forbidden();
        }

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        var books = ResolveService(context);
        var registry = context.RequestServices.GetService<IFundProfileTenancyRegistry>();
        if (!tenant.HasTenantScope || string.IsNullOrWhiteSpace(tenant.CompanyId) || books is null || registry is null)
        {
            return EndpointHelpers.Forbidden();
        }

        var book = await books.GetBookAsync(ledgerBookId, context.RequestAborted).ConfigureAwait(false);
        if (book is null || book.LedgerBookId != ledgerBookId || string.IsNullOrWhiteSpace(book.FundProfileId))
        {
            return EndpointHelpers.Forbidden();
        }

        var owner = await registry.ResolveAsync(book.FundProfileId, context.RequestAborted).ConfigureAwait(false);
        return owner is not null &&
            string.Equals(owner.FundProfileId, book.FundProfileId, StringComparison.Ordinal) &&
            string.Equals(owner.TenantId, tenant.TenantId, StringComparison.Ordinal) &&
            string.Equals(owner.CompanyId, tenant.CompanyId, StringComparison.Ordinal)
                ? null : EndpointHelpers.Forbidden();
    }
}
