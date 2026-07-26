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
    private static void MapStatementReconciliationReportEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementReconciliationReport), async (
            HttpContext context,
            HttpRequest request,
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await StartStatementReconciliationReportAsync(
                context,
                request,
                workflowService,
                jsonOptions,
                legacyStatementToReportContract: false).ConfigureAwait(false))
        .WithName("StartStatementReconciliationReportWorkflow")
        .Produces<StatementReconciliationReportWorkflowDto>(201)
        .Produces<StatementReconciliationReportWorkflowDto>(202)
        .Produces<StatementReconciliationReportWorkflowDto>(500)
        .ProducesValidationProblem()
        .Produces(403)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementReconciliationReportById), async (
            string workflowId,
            HttpContext context,
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await GetStatementReconciliationReportAsync(
                workflowId,
                context,
                workflowService,
                jsonOptions,
                legacyStatementToReportContract: false).ConfigureAwait(false))
        .WithName("GetStatementReconciliationReportWorkflow")
        .Produces<StatementReconciliationReportWorkflowDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(503);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementReconciliationReportResume), async (
            string workflowId,
            HttpContext context,
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await ResumeStatementReconciliationReportAsync(
                workflowId,
                context,
                workflowService,
                jsonOptions,
                legacyStatementToReportContract: false).ConfigureAwait(false))
        .WithName("ResumeStatementReconciliationReportWorkflow")
        .Produces<StatementReconciliationReportWorkflowDto>(200)
        .Produces<StatementReconciliationReportWorkflowDto>(202)
        .Produces<StatementReconciliationReportWorkflowDto>(500)
        .Produces(403)
        .Produces(404)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementReconciliationReportArtifact), async (
            string workflowId,
            string artifactId,
            HttpContext context,
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await DownloadStatementReconciliationReportArtifactAsync(
                workflowId,
                artifactId,
                context,
                workflowService).ConfigureAwait(false))
        .WithName("DownloadStatementReconciliationReportArtifact")
        .Produces(200)
        .Produces(403)
        .Produces(404)
        .Produces(503);

        // Retained workflows and generated clients can contain the pre-rename routes. Project their
        // original wire contract directly over the same canonical service; there is no second
        // workflow, store, renderer, or state transition path behind these compatibility aliases.
#pragma warning disable CS0618 // Endpoint metadata intentionally retains the pre-rename contract.
        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementToReport), async (
            HttpContext context,
            HttpRequest request,
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await StartStatementReconciliationReportAsync(
                context,
                request,
                workflowService,
                jsonOptions,
                legacyStatementToReportContract: true).ConfigureAwait(false))
        .WithName("StartLegacyStatementToReportWorkflow")
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
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await GetStatementReconciliationReportAsync(
                workflowId,
                context,
                workflowService,
                jsonOptions,
                legacyStatementToReportContract: true).ConfigureAwait(false))
        .WithName("GetLegacyStatementToReportWorkflow")
        .Produces<StatementToReportWorkflowDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(503);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationStatementToReportResume), async (
            string workflowId,
            HttpContext context,
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await ResumeStatementReconciliationReportAsync(
                workflowId,
                context,
                workflowService,
                jsonOptions,
                legacyStatementToReportContract: true).ConfigureAwait(false))
        .WithName("ResumeLegacyStatementToReportWorkflow")
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
            [FromServices] StatementReconciliationReportWorkflowService? workflowService) =>
            await DownloadStatementReconciliationReportArtifactAsync(
                workflowId,
                artifactId,
                context,
                workflowService).ConfigureAwait(false))
        .WithName("DownloadLegacyStatementToReportArtifact")
        .Produces(200)
        .Produces(403)
        .Produces(404)
        .Produces(503);
#pragma warning restore CS0618
    }

    private static async Task<IResult> StartStatementReconciliationReportAsync(
        HttpContext context,
        HttpRequest request,
        StatementReconciliationReportWorkflowService? workflowService,
        JsonSerializerOptions jsonOptions,
        bool legacyStatementToReportContract)
    {
        if (!HasReconciliationMutationPermission(context))
            return EndpointHelpers.Forbidden();
        if (!TryResolveCurrentUser(context, out var currentUser))
            return EndpointHelpers.Forbidden();
        if (workflowService is null)
            return StatementReconciliationReportNotRegistered();

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        if (string.IsNullOrWhiteSpace(tenant.TenantId))
            return EndpointHelpers.Forbidden();

        var (document, connectorId, problem) = await ReadStatementDocumentAsync(request, context)
            .ConfigureAwait(false);
        if (problem is not null)
            return problem;

        var form = await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
        if (!TryReadStatementReconciliationReportScope(form, out var scope, out var validationProblem))
            return validationProblem!;

        var execution = await workflowService.StartAsync(
            new StatementReconciliationReportStartCommand(
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

        return StatementReconciliationReportWorkflowResult(
            execution.Workflow,
            jsonOptions,
            created: true,
            legacyStatementToReportContract);
    }

    private static async Task<IResult> GetStatementReconciliationReportAsync(
        string workflowId,
        HttpContext context,
        StatementReconciliationReportWorkflowService? workflowService,
        JsonSerializerOptions jsonOptions,
        bool legacyStatementToReportContract)
    {
        if (!HasReconciliationMutationPermission(context))
            return EndpointHelpers.Forbidden();
        if (workflowService is null)
            return StatementReconciliationReportNotRegistered();
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
            return workflow is null
                ? Results.NotFound()
                : Results.Json(
                    legacyStatementToReportContract
                        ? ToLegacyStatementToReportWorkflow(workflow)
                        : workflow,
                    jsonOptions);
        }
        catch (UnauthorizedAccessException)
        {
            return EndpointHelpers.Forbidden();
        }
    }

    private static async Task<IResult> ResumeStatementReconciliationReportAsync(
        string workflowId,
        HttpContext context,
        StatementReconciliationReportWorkflowService? workflowService,
        JsonSerializerOptions jsonOptions,
        bool legacyStatementToReportContract)
    {
        if (!HasReconciliationMutationPermission(context))
            return EndpointHelpers.Forbidden();
        if (workflowService is null)
            return StatementReconciliationReportNotRegistered();
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
                : StatementReconciliationReportWorkflowResult(
                    execution.Workflow,
                    jsonOptions,
                    created: false,
                    legacyStatementToReportContract);
        }
        catch (UnauthorizedAccessException)
        {
            return EndpointHelpers.Forbidden();
        }
    }

    private static async Task<IResult> DownloadStatementReconciliationReportArtifactAsync(
        string workflowId,
        string artifactId,
        HttpContext context,
        StatementReconciliationReportWorkflowService? workflowService)
    {
        if (!HasReconciliationMutationPermission(context))
            return EndpointHelpers.Forbidden();
        if (workflowService is null)
            return StatementReconciliationReportNotRegistered();
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
    }

    private static IResult StatementReconciliationReportWorkflowResult(
        StatementReconciliationReportWorkflowDto workflow,
        JsonSerializerOptions jsonOptions,
        bool created,
        bool legacyStatementToReportContract = false)
    {
        var statusCode = workflow.Status switch
        {
            StatementReconciliationReportWorkflowStatusDto.Completed when created => StatusCodes.Status201Created,
            StatementReconciliationReportWorkflowStatusDto.Completed => StatusCodes.Status200OK,
            StatementReconciliationReportWorkflowStatusDto.Failed => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status202Accepted
        };
        return Results.Json(
            legacyStatementToReportContract
                ? ToLegacyStatementToReportWorkflow(workflow)
                : workflow,
            jsonOptions,
            statusCode: statusCode);
    }

#pragma warning disable CS0618 // Intentionally project the retained pre-rename wire contract.
    internal static StatementToReportWorkflowDto ToLegacyStatementToReportWorkflow(
        StatementReconciliationReportWorkflowDto workflow)
        => StatementToReportWorkflowService.ToLegacyWorkflow(workflow);
#pragma warning restore CS0618

    private static IResult StatementReconciliationReportNotRegistered()
        => Results.Problem(
            title: "Statement reconciliation report workflow is unavailable",
            detail: "The retained statement import, evidence, reconciliation casework, and JSON/CSV reconciliation report services are not registered.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static bool TryReadStatementReconciliationReportScope(
        IFormCollection form,
        out StatementReconciliationReportScope scope,
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
            problem = MissingDataUploadPayload("sourceInstitution", "Statement reconciliation report requires a broker or custodian name.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(fundAccountId))
        {
            scope = default!;
            problem = MissingDataUploadPayload("fundAccountId", "Statement reconciliation report requires a fund account id.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(externalAccountId))
        {
            scope = default!;
            problem = MissingDataUploadPayload("externalAccountId", "Statement reconciliation report requires an external account id.");
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

        scope = new StatementReconciliationReportScope(
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

    private sealed record StatementReconciliationReportScope(
        string SourceKind,
        string SourceInstitution,
        string FundAccountId,
        string ExternalAccountId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string? ToleranceProfileId);
}
