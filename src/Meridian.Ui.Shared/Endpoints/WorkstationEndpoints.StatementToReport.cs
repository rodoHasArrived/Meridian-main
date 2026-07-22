using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation.Connectors;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapStatementToReportEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementToReport), async (
            HttpContext context,
            HttpRequest request,
            [FromServices] StatementToReportWorkflowService? workflowService) =>
        {
            if (!HasReconciliationMutationPermission(context))
                return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser))
                return EndpointHelpers.Forbidden();
            if (workflowService is null)
                return StatementToReportNotRegistered();

            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId))
                return EndpointHelpers.Forbidden();

            var (document, connectorId, problem) = await ReadStatementDocumentAsync(request, context).ConfigureAwait(false);
            if (problem is not null)
                return problem;

            var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            if (!TryReadStatementToReportScope(form, out var scope, out var validationProblem))
                return validationProblem!;

            var execution = await workflowService.StartAsync(
                new StatementToReportStartCommand(
                    new StatementImportCommitRequest(
                        document!,
                        connectorId,
                        scope.SourceKind,
                        scope.SourceInstitution,
                        scope.FundAccountId,
                        scope.ExternalAccountId,
                        scope.PeriodStart,
                        scope.PeriodEnd,
                        scope.ToleranceProfileId,
                        currentUser),
                    tenant.TenantId,
                    tenant.CompanyId),
                context.RequestAborted).ConfigureAwait(false);

            return StatementToReportWorkflowResult(execution.Workflow, jsonOptions, created: true);
        })
        .WithName("StartStatementToReportWorkflow")
        .Produces<StatementToReportWorkflowDto>(201)
        .Produces<StatementToReportWorkflowDto>(202)
        .Produces<StatementToReportWorkflowDto>(500)
        .ProducesValidationProblem()
        .Produces(403)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementToReportById), async (
            string workflowId,
            HttpContext context,
            [FromServices] StatementToReportWorkflowService? workflowService) =>
        {
            if (!HasReconciliationMutationPermission(context))
                return EndpointHelpers.Forbidden();
            if (workflowService is null)
                return StatementToReportNotRegistered();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId))
                return EndpointHelpers.Forbidden();

            try
            {
                var workflow = await workflowService.GetAsync(
                    workflowId,
                    tenant.TenantId,
                    tenant.CompanyId,
                    context.RequestAborted).ConfigureAwait(false);
                return workflow is null ? Results.NotFound() : Results.Json(workflow, jsonOptions);
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
        })
        .WithName("GetStatementToReportWorkflow")
        .Produces<StatementToReportWorkflowDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(503);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementToReportResume), async (
            string workflowId,
            HttpContext context,
            [FromServices] StatementToReportWorkflowService? workflowService) =>
        {
            if (!HasReconciliationMutationPermission(context))
                return EndpointHelpers.Forbidden();
            if (workflowService is null)
                return StatementToReportNotRegistered();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId))
                return EndpointHelpers.Forbidden();

            try
            {
                var execution = await workflowService.ResumeAsync(
                    workflowId,
                    tenant.TenantId,
                    tenant.CompanyId,
                    context.RequestAborted).ConfigureAwait(false);
                return execution is null
                    ? Results.NotFound()
                    : StatementToReportWorkflowResult(execution.Workflow, jsonOptions, created: false);
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
        })
        .WithName("ResumeStatementToReportWorkflow")
        .Produces<StatementToReportWorkflowDto>(200)
        .Produces<StatementToReportWorkflowDto>(202)
        .Produces<StatementToReportWorkflowDto>(500)
        .Produces(403)
        .Produces(404)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementToReportArtifact), async (
            string workflowId,
            string artifactId,
            HttpContext context,
            [FromServices] StatementToReportWorkflowService? workflowService) =>
        {
            if (!HasReconciliationMutationPermission(context))
                return EndpointHelpers.Forbidden();
            if (workflowService is null)
                return StatementToReportNotRegistered();
            var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (string.IsNullOrWhiteSpace(tenant.TenantId))
                return EndpointHelpers.Forbidden();

            try
            {
                var artifact = await workflowService.DownloadArtifactAsync(
                    workflowId,
                    artifactId,
                    tenant.TenantId,
                    tenant.CompanyId,
                    context.RequestAborted).ConfigureAwait(false);
                return artifact is null
                    ? Results.NotFound()
                    : Results.File(
                        artifact.Content,
                        artifact.Descriptor.ContentType,
                        artifact.Descriptor.FileName,
                        enableRangeProcessing: false);
            }
            catch (UnauthorizedAccessException)
            {
                return EndpointHelpers.Forbidden();
            }
        })
        .WithName("DownloadStatementToReportArtifact")
        .Produces(200)
        .Produces(403)
        .Produces(404)
        .Produces(503);
    }

    private static IResult StatementToReportWorkflowResult(
        StatementToReportWorkflowDto workflow,
        JsonSerializerOptions jsonOptions,
        bool created)
    {
        var statusCode = workflow.Status switch
        {
            StatementToReportWorkflowStatusDto.Completed when created => StatusCodes.Status201Created,
            StatementToReportWorkflowStatusDto.Completed => StatusCodes.Status200OK,
            StatementToReportWorkflowStatusDto.Failed => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status202Accepted
        };
        return Results.Json(workflow, jsonOptions, statusCode: statusCode);
    }

    private static IResult StatementToReportNotRegistered()
        => Results.Problem(
            title: "Statement-to-report workflow is unavailable",
            detail: "The durable statement import, evidence, and reconciliation services are not registered.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static bool TryReadStatementToReportScope(
        IFormCollection form,
        out StatementToReportScope scope,
        out IResult? problem)
    {
        var sourceKind = form["sourceKind"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(sourceKind))
            sourceKind = "broker";
        var sourceInstitution = form["sourceInstitution"].FirstOrDefault()?.Trim();
        var fundAccountId = form["fundAccountId"].FirstOrDefault()?.Trim();
        var externalAccountId = form["externalAccountId"].FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(sourceInstitution))
        {
            scope = default!;
            problem = MissingDataUploadPayload("sourceInstitution", "Statement-to-report requires a broker or custodian name.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(fundAccountId))
        {
            scope = default!;
            problem = MissingDataUploadPayload("fundAccountId", "Statement-to-report requires a fund account id.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(externalAccountId))
        {
            scope = default!;
            problem = MissingDataUploadPayload("externalAccountId", "Statement-to-report requires an external account id.");
            return false;
        }
        if (!TryParseDataUploadDate(form["periodStart"].FirstOrDefault() ?? string.Empty, out var periodStart))
        {
            scope = default!;
            problem = MissingDataUploadPayload("periodStart", "Statement period start must use YYYY-MM-DD format.");
            return false;
        }
        if (!TryParseDataUploadDate(form["periodEnd"].FirstOrDefault() ?? string.Empty, out var periodEnd)
            || periodEnd < periodStart)
        {
            scope = default!;
            problem = MissingDataUploadPayload("periodEnd", "Statement period end must use YYYY-MM-DD and be on or after period start.");
            return false;
        }

        scope = new StatementToReportScope(
            sourceKind,
            sourceInstitution,
            fundAccountId,
            externalAccountId,
            periodStart,
            periodEnd,
            form["toleranceProfileId"].FirstOrDefault()?.Trim());
        problem = null;
        return true;
    }

    private sealed record StatementToReportScope(
        string SourceKind,
        string SourceInstitution,
        string FundAccountId,
        string ExternalAccountId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string? ToleranceProfileId);
}
