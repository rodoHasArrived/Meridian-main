using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Ui.Shared.Services;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class SecurityMasterEndpoints
{
    // ── Pricing Hierarchy & Golden Copy ──────────────────────────────────────

    private static void MapPricingEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        jsonOptions = FixedIncomeHttpJson.Options(jsonOptions);
        group.MapGet(UiApiRoutes.SecurityMasterPricingHierarchy, async (
            Guid securityId,
            string? accountId,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            var hierarchy = await pricingService
                .GetPricingHierarchyAsync(securityId, accountId, ct).ConfigureAwait(false);
            return hierarchy is null ? Results.NotFound() : Results.Json(hierarchy, jsonOptions);
        })
        .WithName("GetSecurityMasterPricingHierarchy")
        .Produces<SecurityPricingHierarchyDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut(UiApiRoutes.SecurityMasterPricingHierarchy, async (
            Guid securityId,
            SecurityPricingHierarchyDto? request,
            HttpContext context,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            if (request is null || request.SecurityId != securityId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["securityId"] = ["Request SecurityId must match the route parameter."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            try
            {
                await pricingService
                    .UpsertPricingHierarchyAsync(request with { UpdatedBy = actor }, ct)
                    .ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("UpsertSecurityMasterPricingHierarchy")
        .Accepts<SecurityPricingHierarchyDto>("application/json")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapPost(UiApiRoutes.SecurityMasterRecordRawPrice, async (
            Guid securityId,
            RecordRawPriceRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            if (request is null || request.SecurityId != securityId)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["securityId"] = ["Request SecurityId must match the route parameter."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            try
            {
                await pricingService
                    .RecordRawPriceAsync(request with { RecordedBy = actor }, ct)
                    .ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .WithName("RecordSecurityMasterRawPrice")
        .Accepts<RecordRawPriceRequest>("application/json")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapGet(UiApiRoutes.SecurityMasterPriceGoldenCopy, async (
            Guid securityId,
            string? accountId,
            DateTimeOffset? asOf,
            DateTimeOffset? knownAt,
            Guid? receiptId,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            if (receiptId.HasValue && (asOf.HasValue || knownAt.HasValue))
                return Results.BadRequest(new { error = "Replay by receiptId uses the retained cutoffs; omit asOf and knownAt." });
            var golden = receiptId is { } retainedId
                ? await pricingService.GetGoldenCopySelectionAsync(securityId, accountId, retainedId, ct).ConfigureAwait(false)
                : await pricingService.GetGoldenCopyPriceAsOfAsync(securityId, accountId, asOf ?? DateTimeOffset.UtcNow, ct, knownAt).ConfigureAwait(false);
            return golden is null ? Results.NotFound() : Results.Json(golden, jsonOptions);
        })
        .WithName("GetSecurityMasterPriceGoldenCopy")
        .Produces<SecurityPriceGoldenCopyDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet(UiApiRoutes.SecurityMasterRetainedPriceObservations, async (
            Guid securityId, string? accountId, DateTimeOffset asOf, DateTimeOffset? knownAt,
            [FromServices] ISecurityMasterPricingService pricingService, CancellationToken ct) =>
        {
            if (asOf == default || asOf.Offset != TimeSpan.Zero || knownAt is { Offset: var offset } && offset != TimeSpan.Zero)
                return Results.BadRequest(new { error = "Observation cutoffs must be explicit UTC timestamps." });
            return Results.Json(await pricingService.GetObservationSelectionsAsync(securityId, accountId, asOf, ct, knownAt)
                .ConfigureAwait(false), jsonOptions);
        })
        .WithName("GetSecurityMasterRetainedPricingObservations")
        .Produces<IReadOnlyList<SecurityPriceGoldenCopyDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet(UiApiRoutes.SecurityMasterPriceComparison, async (
            Guid securityId,
            [FromServices] ISecurityMasterPricingService pricingService,
            CancellationToken ct) =>
        {
            var comparison = await pricingService
                .GetComparisonPricesAsync(securityId, ct).ConfigureAwait(false);
            return Results.Json(comparison, jsonOptions);
        })
        .WithName("GetSecurityMasterPriceComparison")
        .Produces<IReadOnlyList<SecurityComparisonPriceDto>>(StatusCodes.Status200OK);
    }

    // ── Data Vendor Entitlements ─────────────────────────────────────────────

    private static void MapEntitlementEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.DataVendorEntitlements, async (
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            var all = await entitlementService.GetAllAsync(ct).ConfigureAwait(false);
            return Results.Json(all, jsonOptions);
        })
        .WithName("GetDataVendorEntitlements")
        .Produces<IReadOnlyList<DataVendorEntitlementDto>>(StatusCodes.Status200OK);

        group.MapGet(UiApiRoutes.DataVendorEntitlementsExpiring, async (
            int withinDays,
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            var expiring = await entitlementService
                .GetExpiringAsync(withinDays <= 0 ? 30 : withinDays, ct).ConfigureAwait(false);
            return Results.Json(expiring, jsonOptions);
        })
        .WithName("GetExpiringDataVendorEntitlements")
        .Produces<IReadOnlyList<DataVendorEntitlementDto>>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.DataVendorEntitlements, async (
            UpsertDataVendorEntitlementRequest? request,
            HttpContext context,
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            if (request is null)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["A data vendor entitlement request is required."]
                });

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            var result = await entitlementService
                .UpsertAsync(request with { Actor = actor }, ct).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("CreateDataVendorEntitlement")
        .Accepts<UpsertDataVendorEntitlementRequest>("application/json")
        .Produces<DataVendorEntitlementDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);

        group.MapDelete(UiApiRoutes.DataVendorEntitlementById, async (
            Guid entitlementId,
            HttpContext context,
            [FromServices] IDataVendorEntitlementService entitlementService,
            CancellationToken ct) =>
        {
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();

            try
            {
                await entitlementService.DeactivateAsync(entitlementId, actor, ct).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ex.Message);
            }
        })
        .WithName("DeactivateDataVendorEntitlement")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status429TooManyRequests)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .AddEndpointFilter(RequireModifySecurityMasterPermission);
    }
}
