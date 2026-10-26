using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// Exposure, collateral balances and margin calls on the OTC contract model: a netting set's
    /// exposure is computed and recorded as an append-only snapshot, collateral posted or received
    /// under its collateral agreement is recorded as versioned balances, and a snapshot's movements
    /// are issued as margin calls that move through agreement, dispute, settlement or cancellation.
    /// Every write stamps the session actor; a rule failure is a 400.
    /// </summary>
    private static void MapOtcCollateralEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        /// <summary>Computes the netting set's exposure as of a date and appends it as the next snapshot version. 404 for an unknown netting set.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcNettingSetExposureSnapshots, async (
            Guid nettingSetId,
            ComputeOtcExposureSnapshotRequest request,
            HttpContext context,
            [FromServices] IOtcExposureService exposureService,
            CancellationToken ct) =>
            await MutateOrNotFoundAsync(context, () => exposureService.ComputeAsync(nettingSetId, request.AsOfDate, ResolveActor(context), request.KnowledgeAsOf, ct), jsonOptions).ConfigureAwait(false))
        .WithName("ComputeSecurityMasterOtcExposureSnapshot").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcExposureSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Lists a netting set's exposure snapshots, newest date first; by default only the latest version of each date.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcNettingSetExposureSnapshots, async (
            Guid nettingSetId,
            DateOnly? asOfDate,
            bool? latestVersionOnly,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            Results.Json(await store.ListExposureSnapshotsAsync(nettingSetId, asOfDate, latestVersionOnly ?? true, ct).ConfigureAwait(false), jsonOptions))
        .WithName("ListSecurityMasterOtcExposureSnapshots").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<OtcExposureSnapshotDto>>(StatusCodes.Status200OK);

        /// <summary>Reads one exposure snapshot.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcExposureSnapshot, async (
            Guid snapshotId,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            await store.GetExposureSnapshotAsync(snapshotId, ct).ConfigureAwait(false) is { } snapshot
                ? Results.Json(snapshot, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterOtcExposureSnapshot").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<OtcExposureSnapshotDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>Records or amends a collateral balance under a collateral agreement. The haircut comes from the agreement's eligible-collateral line.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcCollateralBalances, async (
            UpsertOtcCollateralBalanceRequest request,
            HttpContext context,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            await MutateAsync(context, () => store.UpsertCollateralBalanceAsync(request with { UpdatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("UpsertSecurityMasterOtcCollateralBalance").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcCollateralBalanceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Lists the collateral balances under a collateral agreement.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcCollateralAgreementBalances, async (
            Guid collateralAgreementId,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            Results.Json(await store.ListCollateralBalancesAsync(collateralAgreementId, ct).ConfigureAwait(false), jsonOptions))
        .WithName("ListSecurityMasterOtcCollateralBalances").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<OtcCollateralBalanceDto>>(StatusCodes.Status200OK);

        /// <summary>Issues a margin call for one movement of a complete snapshot; the amount is copied from the snapshot.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcMarginCalls, async (
            IssueOtcMarginCallRequest request,
            HttpContext context,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            await MutateAsync(context, () => store.IssueMarginCallAsync(request with { UpdatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("IssueSecurityMasterOtcMarginCall").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcMarginCallDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Agrees, disputes, settles or cancels a margin call. The route's call id wins over the body's.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcMarginCallTransition, async (
            Guid callId,
            TransitionOtcMarginCallRequest request,
            HttpContext context,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            await MutateAsync(context, () => store.TransitionMarginCallAsync(request with { CallId = callId, UpdatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("TransitionSecurityMasterOtcMarginCall").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcMarginCallDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Reads one margin call.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcMarginCall, async (
            Guid callId,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            await store.GetMarginCallAsync(callId, ct).ConfigureAwait(false) is { } call
                ? Results.Json(call, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterOtcMarginCall").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<OtcMarginCallDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>Lists a netting set's margin calls, oldest issue date first.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcNettingSetMarginCalls, async (
            Guid nettingSetId,
            [FromServices] IOtcCollateralStore store,
            CancellationToken ct) =>
            Results.Json(await store.ListMarginCallsAsync(nettingSetId, ct).ConfigureAwait(false), jsonOptions))
        .WithName("ListSecurityMasterOtcMarginCalls").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<OtcMarginCallDto>>(StatusCodes.Status200OK);
    }

    /// <summary>
    /// Runs a collateral-model write whose target may not exist: permission first, then the
    /// store, with a null result reported as 404 and a rule failure as 400.
    /// </summary>
    private static async Task<IResult> MutateOrNotFoundAsync<T>(HttpContext context, Func<Task<T?>> write, JsonSerializerOptions jsonOptions)
        where T : class
    {
        var authorizationResult = RequireSecurityMasterMutationPermission(context);
        if (authorizationResult is not null)
            return authorizationResult;

        try
        {
            return await write().ConfigureAwait(false) is { } result
                ? Results.Json(result, jsonOptions)
                : Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
