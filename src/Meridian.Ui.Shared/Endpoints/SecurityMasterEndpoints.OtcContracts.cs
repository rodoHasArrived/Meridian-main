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
    /// The OTC contract model: master agreements, collateral agreements, netting sets and the
    /// bilateral contracts that hang off them. The instrument stays in the Security Master; a
    /// contract names it and adds who, how much, which way and where in its life.
    /// </summary>
    private static void MapOtcContractEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        /// <summary>Lists the bilateral contracts traded on an instrument.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterContracts, async (
            Guid securityId,
            [FromServices] ISecurityMasterQueryService queryService,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
        {
            if (await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false) is null)
                return Results.NotFound();

            var contracts = await store.ListContractsForSecurityAsync(securityId, ct).ConfigureAwait(false);
            return Results.Json(contracts, jsonOptions);
        })
        .WithName("GetSecurityMasterContracts").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<OtcContractDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>Creates or amends a bilateral contract. Closed contracts (terminated, novated, matured) are refused.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcContracts, async (
            UpsertOtcContractRequest request,
            HttpContext context,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await MutateAsync(context, () => store.UpsertContractAsync(request with { UpdatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("UpsertSecurityMasterOtcContract").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcContractDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Reads one bilateral contract.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcContract, async (
            Guid contractId,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await store.GetContractAsync(contractId, ct).ConfigureAwait(false) is { } contract
                ? Results.Json(contract, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterOtcContract").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<OtcContractDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.SecurityMasterOtcContractLifecycle, async (
            Guid contractId, [FromServices] IOtcContractStore store, CancellationToken ct) =>
        {
            if (await store.GetContractAsync(contractId, ct).ConfigureAwait(false) is null)
                return Results.NotFound();
            return Results.Json(await store.GetLifecycleEventsAsync(contractId, ct).ConfigureAwait(false), jsonOptions);
        })
        .WithName("GetSecurityMasterOtcContractLifecycle").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<OtcContractLifecycleEventDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost(UiApiRoutes.SecurityMasterOtcContractLifecycle, async (
            Guid contractId, AppendOtcContractLifecycleRequest request, HttpContext context,
            [FromServices] IOtcContractStore store, CancellationToken ct) =>
        {
            if (contractId != request.ContractId)
                return Results.BadRequest(new { error = "Route and lifecycle contract ids must match." });
            try
            {
                return await MutateAsync(context, () => store.AppendLifecycleEventAsync(
                    OtcContractLifecycleRules.Normalize(request with { Actor = ResolveActor(context) }), ct), jsonOptions).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (NotSupportedException)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("AppendSecurityMasterOtcContractLifecycle").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcContractLifecycleEventDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Lists master agreements, optionally for one counterparty.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcMasterAgreements, async (
            string? counterparty,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            Results.Json(await store.ListMasterAgreementsAsync(counterparty, ct).ConfigureAwait(false), jsonOptions))
        .WithName("ListSecurityMasterOtcMasterAgreements").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<OtcMasterAgreementDto>>(StatusCodes.Status200OK);

        /// <summary>Creates or amends a master agreement (ISDA, GMRA, GMSLA).</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcMasterAgreements, async (
            UpsertOtcMasterAgreementRequest request,
            HttpContext context,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await MutateAsync(context, () => store.UpsertMasterAgreementAsync(request with { UpdatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("UpsertSecurityMasterOtcMasterAgreement").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcMasterAgreementDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Reads one master agreement.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcMasterAgreement, async (
            Guid agreementId,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await store.GetMasterAgreementAsync(agreementId, ct).ConfigureAwait(false) is { } agreement
                ? Results.Json(agreement, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterOtcMasterAgreement").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<OtcMasterAgreementDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>Creates or amends a collateral agreement (CSA) under a master agreement.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcCollateralAgreements, async (
            UpsertOtcCollateralAgreementRequest request,
            HttpContext context,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await MutateAsync(context, () => store.UpsertCollateralAgreementAsync(request with { UpdatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("UpsertSecurityMasterOtcCollateralAgreement").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcCollateralAgreementDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Reads one collateral agreement.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcCollateralAgreement, async (
            Guid collateralAgreementId,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await store.GetCollateralAgreementAsync(collateralAgreementId, ct).ConfigureAwait(false) is { } agreement
                ? Results.Json(agreement, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterOtcCollateralAgreement").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<OtcCollateralAgreementDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        /// <summary>Creates or amends a netting set under a master agreement.</summary>
        group.MapPost(UiApiRoutes.SecurityMasterOtcNettingSets, async (
            UpsertOtcNettingSetRequest request,
            HttpContext context,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await MutateAsync(context, () => store.UpsertNettingSetAsync(request with { UpdatedBy = ResolveActor(context) }, ct), jsonOptions).ConfigureAwait(false))
        .WithName("UpsertSecurityMasterOtcNettingSet").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<OtcNettingSetDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        /// <summary>Reads a netting set with its master agreement, collateral agreement and contracts.</summary>
        group.MapGet(UiApiRoutes.SecurityMasterOtcNettingSet, async (
            Guid nettingSetId,
            [FromServices] IOtcContractStore store,
            CancellationToken ct) =>
            await store.GetNettingSetAsync(nettingSetId, ct).ConfigureAwait(false) is { } view
                ? Results.Json(view, jsonOptions)
                : Results.NotFound())
        .WithName("GetSecurityMasterOtcNettingSet").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<OtcNettingSetViewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>Runs a contract-model write: permission first, then the store, with a rule failure reported as 400.</summary>
    private static async Task<IResult> MutateAsync<T>(HttpContext context, Func<Task<T>> write, JsonSerializerOptions jsonOptions)
    {
        var authorizationResult = RequireSecurityMasterMutationPermission(context);
        if (authorizationResult is not null)
            return authorizationResult;

        try
        {
            return Results.Json(await write().ConfigureAwait(false), jsonOptions);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
