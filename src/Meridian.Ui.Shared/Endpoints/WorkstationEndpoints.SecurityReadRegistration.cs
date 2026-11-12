using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Application.ProviderRouting;
using Meridian.DataIntegration.Monitoring;
using Meridian.Reporting;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.StrategyEngine;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Instruments.AssetOperations;
using Meridian.QuantScript.Compilation;
using Meridian.Storage.Export;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Query;
using Meridian.Storage.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    // Domain registration keeps authorization, scope filters and registration order intact.
    private static void MapSecurityMasterReadEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterSearch), async (
            string? query,
            int? take,
            bool? activeOnly,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new { error = "Query is required." });
            }

            var request = new SecuritySearchRequest(
                Query: query.Trim(),
                Take: Math.Clamp(take ?? 25, 1, 100),
                ActiveOnly: activeOnly ?? true);
            var results = await queryService.SearchAsync(request, ct).ConfigureAwait(false);
            return Results.Json(results.Select(MapToWorkstationSecurity).ToArray(), jsonOptions);
        })
        .WithName("SearchSecurityMasterWorkstation").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityMasterWorkstationDto>>(200)
        .Produces(400);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterById), async (
            Guid securityId,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(MapToWorkstationSecurity(detail), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationSecurity").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityMasterWorkstationDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterHistory), async (
            Guid securityId,
            int? take,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var history = await queryService.GetHistoryAsync(
                    new SecurityHistoryRequest(
                        SecurityId: securityId,
                        Take: Math.Clamp(take ?? 50, 1, 500)),
                    ct)
                .ConfigureAwait(false);

            return history.Count == 0
                ? Results.NotFound()
                : Results.Json(history, jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationSecurityHistory").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityMasterEventEnvelope>>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterIdentity), async (
            Guid securityId,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var detail = await queryService.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            return detail is null
                ? Results.NotFound()
                : Results.Json(MapToIdentityDrillIn(detail), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationIdentityDrillIn").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityIdentityDrillInDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterEconomicDefinition), async (
            Guid securityId,
            [FromServices] ContractSecurityMasterQueryService queryService,
            CancellationToken ct) =>
        {
            var record = await queryService.GetEconomicDefinitionByIdAsync(securityId, ct).ConfigureAwait(false);
            return record is null
                ? Results.NotFound()
                : Results.Json(MapToEconomicDefinitionSummary(record), jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationEconomicDefinition").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityEconomicDefinitionSummaryDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterTrustSnapshot), async (
            Guid securityId,
            string? fundProfileId,
            HttpContext context) =>
        {
            if (!EndpointAuthorization.HasAnyPermission(
                    context,
                    UserPermission.ViewSecurityMaster,
                    UserPermission.ModifySecurityMaster))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var workbenchService = context.RequestServices.GetService<ISecurityMasterWorkbenchQueryService>();
            if (workbenchService is null)
            {
                return Results.Problem("Security Master workbench service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var snapshot = await workbenchService
                .GetTrustSnapshotAsync(securityId, fundProfileId, context.RequestAborted)
                .ConfigureAwait(false);

            return snapshot is null
                ? Results.NotFound()
                : Results.Json(snapshot, jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationTrustSnapshot").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<SecurityMasterTrustSnapshotDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterInstrumentPassport), async (
            Guid securityId,
            string? fundProfileId,
            HttpContext context) =>
        {
            if (!EndpointAuthorization.HasAnyPermission(
                    context,
                    UserPermission.ViewSecurityMaster,
                    UserPermission.ModifySecurityMaster))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var workbenchService = context.RequestServices.GetService<ISecurityMasterWorkbenchQueryService>();
            if (workbenchService is null)
            {
                return Results.Problem("Security Master workbench service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var passport = await workbenchService
                .GetInstrumentPassportAsync(securityId, fundProfileId, context.RequestAborted)
                .ConfigureAwait(false);

            return passport is null
                ? Results.NotFound()
                : Results.Json(passport, jsonOptions);
        })
        .WithName("GetSecurityMasterWorkstationInstrumentPassport").RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<InstrumentPassportDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationSecurityMasterBulkResolveConflicts), async (
            BulkResolveSecurityMasterConflictsRequest request,
            HttpContext context) =>
        {
            if (!HasPermission(context, UserPermission.ModifySecurityMaster))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var workbenchService = context.RequestServices.GetService<ISecurityMasterWorkbenchQueryService>();
            if (workbenchService is null)
            {
                return Results.Problem("Security Master workbench service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await workbenchService
                .BulkResolveConflictsAsync(request, context.RequestAborted)
                .ConfigureAwait(false);

            return Results.Json(result, jsonOptions);
        })
        .WithName("BulkResolveSecurityMasterWorkstationConflicts").RequirePermission(UserPermission.ModifySecurityMaster)
        .Accepts<BulkResolveSecurityMasterConflictsRequest>("application/json")
        .Produces<BulkResolveSecurityMasterConflictsResult>(200)
        .Produces(403)
        .Produces(501);
    }
}
