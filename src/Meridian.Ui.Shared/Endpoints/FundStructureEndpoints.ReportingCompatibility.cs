using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Storage.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class FundStructureEndpoints
{
    private static void MapFundOperationsWorkspaceAndLegacyReportingEndpoints(
        RouteGroupBuilder group,
        RouteGroupBuilder reportingGroup,
        RouteGroupBuilder legacyReportingGroup,
        JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/workspace-view", async (HttpContext context) =>
        {
            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var q = context.Request.Query;
            var fundProfileId = q["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem(
                    "fundProfileId is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var query = new FundOperationsWorkspaceQuery(
                FundProfileId: fundProfileId,
                AsOf: ParseDateTimeOffset(q["asOf"]),
                Currency: q["currency"].FirstOrDefault(),
                ScopeKind: ParseFundLedgerScope(q["scopeKind"]) ?? FundLedgerScope.Consolidated,
                ScopeId: q["scopeId"].FirstOrDefault(),
                SelectedLedgerIds: ParseSelectedLedgerIds(q["selectedLedgerIds"], q["selectedLedgerId"]));

            var result = await service.GetWorkspaceAsync(
                query,
                BuildReportAccessQueryContext(context),
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetFundOperationsWorkspaceView")
        .Produces<FundOperationsWorkspaceDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireWorkstationTenantCompanyScope();

        legacyReportingGroup.MapPost("/report-pack-preview", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs/readiness",
                "Legacy report-pack preview did not use the canonical server-owned run parameters and blocking readiness decision."))
        .WithName("PreviewFundReportPack")
        .ProducesProblem(StatusCodes.Status410Gone);

        legacyReportingGroup.MapPost("/report-packs", (HttpContext context) =>
            LegacyReportingRouteGone(
                context,
                "/api/fund-structure/reporting/runs",
                "Legacy report-pack generation bypassed certified snapshots, canonical readiness, and governed lifecycle creation."))
        .WithName("GenerateFundReportPack")
        .ProducesProblem(StatusCodes.Status410Gone);

        legacyReportingGroup.MapGet("/report-packs", async (HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (context.RequestServices.GetService<IGovernanceReportPackRepository>() is null)
            {
                return LegacyReportingRouteGone(
                    context,
                    "/api/fund-structure/reporting/runs",
                    "Legacy file-backed report-pack history is not available in this deployment.");
            }

            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            var q = context.Request.Query;
            var fundProfileId = q["fundProfileId"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem(
                    "fundProfileId is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (await RequireReportingFundProfileTenantAccessAsync(context, fundProfileId).ConfigureAwait(false)
                is { } scopeFailure)
            {
                return scopeFailure;
            }

            var limit = ParseInt(q["limit"], 20);
            var result = await service
                .GetReportPackHistoryAsync(fundProfileId, limit, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetFundReportPackHistory")
        .Produces<IReadOnlyList<FundReportPackHistoryItemDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status410Gone);

        reportingGroup.MapGet("/structured-exports/{exportId}", async (
            string exportId,
            string fundProfileId,
            DateTimeOffset? asOf,
            string? currency,
            string? format,
            HttpContext context) =>
        {
            if (!HasReportingReadPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = ResolveWorkspaceService(context);
            if (service is null)
            {
                return WorkspaceServiceUnavailable();
            }

            if (string.IsNullOrWhiteSpace(fundProfileId))
            {
                return Results.Problem(
                    "fundProfileId is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var accessContext = BuildReportAccessQueryContext(context);
                var result = await service.GetStructuredReportingExportAsync(
                    new StructuredReportingExportRequestDto(fundProfileId, exportId, asOf, currency),
                    accessContext,
                    context.RequestAborted).ConfigureAwait(false);
                ApplyStructuredExportAuditHeaders(context, result);
                if (IsStructuredCsvRequest(format))
                {
                    var fileName =
                        $"{result.Export.ExportId}-{result.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.csv";
                    return Results.File(
                        BuildStructuredExportCsv(result),
                        "text/csv",
                        fileName);
                }

                if (IsStructuredXlsxRequest(format))
                {
                    var fileName =
                        $"{result.Export.ExportId}-{result.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.xlsx";
                    return Results.File(
                        BuildStructuredExportXlsx(result),
                        StructuredXlsxContentType,
                        fileName);
                }

                if (IsStructuredJsonRequest(format))
                {
                    var fileName =
                        $"{result.Export.ExportId}-{result.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.json";
                    return Results.File(
                        BuildStructuredExportJson(result, jsonOptions),
                        "application/json",
                        fileName);
                }

                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
        })
        .WithName("GetStructuredReportingExport")
        .Produces<StructuredReportingExportPayloadDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status200OK, contentType: "application/json")
        .Produces(StatusCodes.Status200OK, contentType: "text/csv")
        .Produces(StatusCodes.Status200OK, contentType: StructuredXlsxContentType)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }
}
