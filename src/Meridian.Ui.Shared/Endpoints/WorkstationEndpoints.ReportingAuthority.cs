using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapReportingAuthorityEndpoints(
        RouteGroupBuilder group,
        JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationReporting), async (HttpContext context) =>
        {
            ReportingDeploymentCapabilityDto? deployment;
            try
            {
                deployment = context.RequestServices
                    .GetService<IReportingDeploymentReadinessService>()
                    ?.Evaluate();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return WorkstationServiceUnavailable(
                    "The reporting deployment capability is temporarily unavailable.");
            }

            if (deployment is null)
            {
                return WorkstationServiceUnavailable(
                    "The reporting deployment capability service is not registered.");
            }

            if (!deployment.IsReady)
            {
                return WorkstationServiceUnavailable(
                    $"Authoritative reporting is unavailable: {string.Join(" ", deployment.BlockingReasons)}");
            }

            var readService = context.RequestServices.GetService<ReportPackRunReadService>();
            if (readService is null)
            {
                return WorkstationServiceUnavailable(
                    "The authoritative reporting read service is not registered.");
            }

            try
            {
                var reporting = await readService
                    .BuildPayloadAsync(
                        BuildReportAccessQueryContext(context),
                        ct: context.RequestAborted)
                    .ConfigureAwait(false);
                reporting = reporting with
                {
                    DeploymentCapability = deployment
                };
                return Results.Ok(reporting);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return WorkstationServiceUnavailable(
                    "The authoritative reporting store is temporarily unavailable.");
            }
        })
        .WithName("GetWorkstationReporting")
        .RequireAnyPermission(UserPermission.ViewReporting, UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .Produces<WorkstationReportingPayload>(200)
        .Produces(401)
        .Produces(403)
        .Produces(503);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationReportingStructuredExport), (
            string exportId,
            string? format,
            HttpContext context) =>
        {
            ReportingDeploymentCapabilityDto? deployment;
            try
            {
                deployment = context.RequestServices
                    .GetService<IReportingDeploymentReadinessService>()
                    ?.Evaluate();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return WorkstationServiceUnavailable(
                    "Authoritative reporting exports are unavailable until the durable reporting deployment is ready.");
            }

            if (deployment?.IsReady != true)
            {
                return WorkstationServiceUnavailable(
                    "Authoritative reporting exports are unavailable until the durable reporting deployment is ready.");
            }

            var service = context.RequestServices.GetService<ReportPackRunReadService>();
            if (service is null)
            {
                return WorkstationServiceUnavailable(
                    "The authoritative reporting read service is not registered.");
            }
            try
            {
                var payload = service.GetStructuredReportingExport(
                    exportId,
                    BuildReportAccessQueryContext(context));
                ApplyWorkstationStructuredExportAuditHeaders(context, payload);

                if (IsWorkstationStructuredCsvRequest(format))
                {
                    var fileName = $"{payload.Export.ExportId}-{payload.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.csv";
                    return Results.File(
                        BuildWorkstationStructuredExportCsv(payload),
                        "text/csv",
                        fileName);
                }

                if (IsWorkstationStructuredXlsxRequest(format))
                {
                    var fileName = $"{payload.Export.ExportId}-{payload.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.xlsx";
                    return Results.File(
                        BuildWorkstationStructuredExportXlsx(payload),
                        WorkstationStructuredXlsxContentType,
                        fileName);
                }

                if (IsWorkstationStructuredJsonRequest(format))
                {
                    var fileName = $"{payload.Export.ExportId}-{payload.Export.AsOf.UtcDateTime:yyyyMMddHHmmss}.json";
                    return Results.File(
                        JsonSerializer.SerializeToUtf8Bytes(payload, jsonOptions),
                        "application/json",
                        fileName);
                }

                return Results.Json(payload, jsonOptions);
            }
            catch (Exception exception) when (exception is not ArgumentException
                and not KeyNotFoundException
                and not OperationCanceledException)
            {
                return WorkstationServiceUnavailable(
                    "The authoritative reporting store is temporarily unavailable.");
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
        .WithName("GetWorkstationStructuredReportingExport")
        .RequireAnyPermission(UserPermission.ViewReporting, UserPermission.AdminMaintenance)
        .RequireWorkstationTenantCompanyScope()
        .Produces<StructuredReportingExportPayloadDto>(200)
        .Produces(200, contentType: "application/json")
        .Produces(200, contentType: "text/csv")
        .Produces(200, contentType: WorkstationStructuredXlsxContentType)
        .Produces(401)
        .Produces(403)
        .Produces(400)
        .Produces(404)
        .Produces(503);
    }

    private static WorkstationReportingPayload BuildEmbeddedReportingPayload(HttpContext context)
    {
        ReportingDeploymentCapabilityDto? deployment = null;
        try
        {
            var readiness = context.RequestServices.GetService<IReportingDeploymentReadinessService>();
            if (readiness is null)
            {
                return BuildUnavailableReportingPayload(BuildUnavailableReportingCapability(
                    deployment: null,
                    "The reporting deployment capability service is not registered."));
            }

            deployment = readiness.Evaluate();
            if (!deployment.IsReady)
            {
                return BuildUnavailableReportingPayload(BuildUnavailableReportingCapability(
                    deployment,
                    "The reporting deployment capability is not ready."));
            }

            var readService = context.RequestServices.GetService<ReportPackRunReadService>();
            if (readService is null)
            {
                return BuildUnavailableReportingPayload(BuildUnavailableReportingCapability(
                    deployment,
                    "The authoritative reporting read service is not registered."));
            }

            return readService.BuildPayload(BuildReportAccessQueryContext(context)) with
            {
                DeploymentCapability = deployment
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return BuildUnavailableReportingPayload(BuildUnavailableReportingCapability(
                deployment,
                "The authoritative reporting store is temporarily unavailable."));
        }
    }

    private static ReportingDeploymentCapabilityDto BuildUnavailableReportingCapability(
        ReportingDeploymentCapabilityDto? deployment,
        string blockingReason)
    {
        var components = (deployment?.Components ?? [])
            .Where(static component => !string.Equals(
                component.ComponentId,
                "workspace-read",
                StringComparison.OrdinalIgnoreCase))
            .Append(new ReportingDeploymentComponentDto(
                "workspace-read",
                "Reporting workspace read",
                false,
                blockingReason))
            .ToArray();
        var blockingReasons = (deployment?.BlockingReasons ?? [])
            .Append(blockingReason)
            .Where(static reason => !string.IsNullOrWhiteSpace(reason))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ReportingDeploymentCapabilityDto(
            IsReady: false,
            DurableGovernance: deployment?.DurableGovernance ?? false,
            DurableArtifacts: deployment?.DurableArtifacts ?? false,
            DurableReconciliationEvidence:
                deployment?.DurableReconciliationEvidence ?? false,
            DurableRuns: deployment?.DurableRuns ?? false,
            DurableScheduling: deployment?.DurableScheduling ?? false,
            DurableDelivery: deployment?.DurableDelivery ?? false,
            RecipientDestinationsConfigured: deployment?.RecipientDestinationsConfigured ?? false,
            ClientDocumentsConfigured: deployment?.ClientDocumentsConfigured ?? false,
            MigrationsManaged: deployment?.MigrationsManaged ?? false,
            Components: components,
            BlockingReasons: blockingReasons);
    }
}
