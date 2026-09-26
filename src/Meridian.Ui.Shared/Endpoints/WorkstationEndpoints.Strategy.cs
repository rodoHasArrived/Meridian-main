using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.StrategyEngine;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.QuantScript.Compilation;
using Meridian.Strategies.Live.Designer;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Strategy Designer and Strategy Engine endpoints for the workstation API surface,
/// split out of the WorkstationEndpoints core partial as a capability group.
/// </summary>
public static partial class WorkstationEndpoints
{
    private static void MapStrategyDesignerEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerTemplates), (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<StrategyDesignService>();
            return service is null
                ? StrategyDesignerUnavailable(jsonOptions)
                : Results.Json(service.GetTemplates(), jsonOptions);
        })
        .WithName("GetStrategyDesignerTemplates").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyDesignTemplate>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerFieldCatalog), (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<StrategyDesignService>();
            return service is null
                ? StrategyDesignerUnavailable(jsonOptions)
                : Results.Json(service.GetFieldCatalog(), jsonOptions);
        })
        .WithName("GetStrategyDesignerFieldCatalog").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyDesignFieldCatalogItem>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerDrafts), async (HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IStrategyDesignRepository>();
            if (repository is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var drafts = await repository.ListDraftsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(drafts, jsonOptions);
        })
        .WithName("GetStrategyDesignerDrafts").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyDesignDraftSummary>>(200)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerDraftById), async (string documentId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IStrategyDesignRepository>();
            if (repository is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var document = await repository.GetAsync(documentId, context.RequestAborted).ConfigureAwait(false);
            return document is null
                ? Results.NotFound(new { error = "Strategy design draft was not found." })
                : Results.Json(document, jsonOptions);
        })
        .WithName("GetStrategyDesignerDraft").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<StrategyDesignDocument>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerDrafts), async (StrategyDesignDraftSaveRequest? request, HttpContext context) =>
        {
            if (!HasPermission(context, UserPermission.ManageStrategies))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (request?.Document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            var repository = context.RequestServices.GetService<IStrategyDesignRepository>();
            if (service is null || repository is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var document = service.Normalize(request.Document);
            var validation = service.Validate(document);
            await repository.SaveAsync(document, context.RequestAborted).ConfigureAwait(false);
            var response = new StrategyDesignDraftSaveResponse(
                document,
                StrategyDesignService.CreateDraftSummary(document),
                validation,
                service.BuildRunTrace(document, validation));
            return Results.Json(response, jsonOptions);
        })
        .WithName("SaveStrategyDesignerDraft").RequirePermission(UserPermission.ManageStrategies)
        .Produces<StrategyDesignDraftSaveResponse>(200)
        .Produces(400)
        .Produces(403)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerValidate), (StrategyDesignDocument? document, HttpContext context) =>
        {
            if (document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            if (service is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var normalized = service.Normalize(document);
            return Results.Json(service.Validate(normalized), jsonOptions);
        })
        .WithName("ValidateStrategyDesignerDocument").DeclareNonMutating("Normalizes and validates the posted design document in memory; StrategyDesignService holds only a field catalog and templates and persists nothing.").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<StrategyDesignValidationResult>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerPreview), (StrategyDesignDocument? document, HttpContext context) =>
        {
            if (document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            if (service is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var normalized = service.Normalize(document);
            var preview = service.Preview(normalized);
            return preview.Validation.IsValid
                ? Results.Json(preview, jsonOptions)
                : Results.Json(preview, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("PreviewStrategyDesignerDocument").DeclareNonMutating("Normalizes and previews the posted design document in memory; the preview is computed from the body and StrategyDesignService persists nothing. Saving a design is a separate route.").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<StrategyDesignPreviewResult>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationStrategyDesignerRunBacktest), async (
            StrategyDesignRunBacktestRequest? request,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (!HasPermission(context, UserPermission.ManageStrategies))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (request?.Document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            if (!StrategyRunEvidenceLoop.TryCreateRequired(
                    request.Document.DocumentId,
                    request.OperatorAcceptanceCriteria,
                    request.RetainedEvidenceReferences,
                    request.AccountingRecordReferences,
                    request.ApprovalReferences,
                    request.PaperValidationReferences,
                    request.GovernedReportReferences,
                    out var evidenceLoop,
                    out var evidenceValidationError))
            {
                return Results.BadRequest(new { error = evidenceValidationError });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            if (service is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var document = service.Normalize(request.Document);
            var preview = service.Preview(document);
            if (!preview.Validation.IsValid)
            {
                return Results.Json(
                    CreateBacktestResponse(document, preview, null, new Dictionary<string, string>(), "Validation failed."),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var runner = context.RequestServices.GetService<IScriptRunner>();
            var repository = context.RequestServices.GetService<IStrategyRepository>();
            if (runner is null)
            {
                return Results.Json(
                    new
                    {
                        error = "Quant Lab is not enabled on this host. Set QuantLab:Enabled to true to enable.",
                        quantLabEnabled = false
                    },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (repository is null)
            {
                return Results.Json(
                    new
                    {
                        error = "Strategy run persistence is not enabled on this host.",
                        strategyRunPersistenceEnabled = false
                    },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var parameters = request.Parameters is null
                ? new Dictionary<string, object?>()
                : request.Parameters.ToDictionary(
                    static item => item.Key,
                    static item => (object?)item.Value,
                    StringComparer.OrdinalIgnoreCase);
            var result = await runner.RunAsync(preview.Compiled.Source, parameters, ct).ConfigureAwait(false);
            var metrics = result.Metrics.ToDictionary(
                static item => item.Key,
                static item => item.Value,
                StringComparer.OrdinalIgnoreCase);
            var capturedBacktest = result.CapturedBacktests.Count == 1
                ? result.CapturedBacktests[0]
                : null;
            var biasDisclosure = StrategyRunReadService.MapBiasDisclosure(capturedBacktest?.BiasDisclosure);
            if (!result.Success)
            {
                return Results.Json(
                    CreateBacktestResponse(document, preview, null, metrics, result.RuntimeError, biasDisclosure),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (capturedBacktest is null)
            {
                return Results.Json(
                    CreateBacktestResponse(
                        document,
                        preview,
                        null,
                        metrics,
                        $"QuantScript execution captured {result.CapturedBacktests.Count} BacktestResult values; exactly one is required."),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // Refused here rather than at activation: LiveStrategyCatalog resolves an exact built-in
            // factory id before consulting any fallback, so a run recorded under one of these ids
            // would trade the built-in strategy and never reach the designer source that carries
            // this document's gates, sizing, and risk guards (PRD-020).
            if (DesignerDocumentRevision.IsReservedDocumentId(document.DocumentId))
            {
                return Results.Json(
                    CreateBacktestResponse(
                        document,
                        preview,
                        null,
                        metrics,
                        $"Designer document id '{document.DocumentId}' collides with a built-in live strategy. A run "
                        + "recorded under this id would activate the built-in strategy instead of this design, "
                        + "bypassing its gates, sizing, and risk guards. Rename the document before running it.",
                        biasDisclosure),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var runId = Guid.NewGuid().ToString("N");
            var entry = (StrategyRunEntry
                .StartWithEvidence(
                    document.DocumentId,
                    document.Name,
                    RunType.Backtest,
                    runId,
                    datasetReference: document.DatasetReference,
                    feedReference: "strategy-designer:v1",
                    engine: "QuantScript",
                    parameterSet: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["designerDocumentId"] = document.DocumentId,
                        ["datasetFingerprint"] = preview.Compiled.DatasetFingerprint,
                        ["cellCount"] = document.Cells.Count.ToString(CultureInfo.InvariantCulture),

                        // The document's universe is carried as 'symbols' because promotion copies
                        // this parameter set verbatim and LiveTradingEngine.ResolveUniverse reads
                        // only 'symbol'/'symbols'. Without it a promoted designer run defers for
                        // having no trading universe, or silently trades the host's DefaultSymbols
                        // instead of the design's own (PRD-020).
                        ["symbols"] = string.Join(",", document.Universe),

                        // Pins activation to this exact revision: the design repository returns the
                        // latest saved draft for a document id, so an edit made after this backtest
                        // would otherwise become what a promoted run trades.
                        [DesignerDocumentRevision.ParameterKey] = DesignerDocumentRevision.ComputeHash(document)
                    },
                    operatorAcceptanceCriteria: evidenceLoop.OperatorAcceptanceCriteria,
                    retainedEvidenceReferences: evidenceLoop.RetainedEvidenceReferences,
                    accountingRecordReferences: evidenceLoop.AccountingRecordReferences,
                    approvalReferences: evidenceLoop.ApprovalReferences,
                    paperValidationReferences: evidenceLoop.PaperValidationReferences,
                    governedReportReferences: evidenceLoop.GovernedReportReferences))
                .Complete(capturedBacktest);
            await repository.RecordRunAsync(entry, ct).ConfigureAwait(false);

            return Results.Json(CreateBacktestResponse(document, preview, runId, metrics, null, biasDisclosure), jsonOptions);
        })
        .WithName("RunStrategyDesignerBacktest").RequirePermission(UserPermission.ManageStrategies)
        .Produces<StrategyDesignRunBacktestResponse>(200)
        .Produces(400)
        .Produces(403)
        .Produces(503);
    }

    private static StrategyDesignRunBacktestResponse CreateBacktestResponse(
        StrategyDesignDocument document,
        StrategyDesignPreviewResult preview,
        string? runId,
        IReadOnlyDictionary<string, string> metrics,
        string? runtimeError,
        BiasDisclosureDto? biasDisclosure = null)
    {
        var success = runId is not null && runtimeError is null;
        var trace = preview.Trace
            .Concat([
                new StrategyDesignRunTraceEntry(
                    "record-run",
                    "Record StrategyRunEntry",
                    success ? "complete" : "blocked",
                    success
                        ? $"Recorded backtest run {runId} for promotion review."
                        : runtimeError ?? "Backtest did not produce a recorded run.",
                    OccurredAt: DateTimeOffset.UtcNow)
            ])
            .ToArray();

        return new StrategyDesignRunBacktestResponse(
            success,
            runId,
            document.DocumentId,
            document.Name,
            preview.Validation,
            preview.Compiled,
            trace,
            preview.Rows,
            metrics,
            runtimeError,
            success ? $"/api/promotion/evaluate/{runId}" : null,
            success ? $"/api/workstation/runs/{runId}/review-packet" : null,
            biasDisclosure);
    }

    private static IResult StrategyDesignerUnavailable(JsonSerializerOptions jsonOptions)
        => Results.Json(
            new { error = "Strategy Designer services are not registered." },
            jsonOptions,
            statusCode: StatusCodes.Status501NotImplemented);

    private static void MapStrategyEngineEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStrategyEngineDefinitions), (HttpContext context) =>
        {
            var registry = context.RequestServices.GetService<StrategyEngineRegistry>();
            return registry is null
                ? StrategyEngineUnavailable(jsonOptions)
                : Results.Json(registry.GetDefinitions(), jsonOptions);
        })
        .WithName("GetStrategyEngineDefinitions").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<IReadOnlyList<StrategyEngineDefinition>>(200)
        .Produces(501);

        group.MapPost(WorkstationSubroute(UiApiRoutes.WorkstationStrategyEngineValidateRun), (
            StrategyEngineValidateRunRequest? request,
            HttpContext context) =>
        {
            if (request?.RunRequest is null)
            {
                return Results.BadRequest(new { error = "A strategy run request is required." });
            }

            var validation = context.RequestServices.GetService<StrategyEngineValidationService>();
            if (validation is null)
            {
                return StrategyEngineUnavailable(jsonOptions);
            }

            var result = validation.Validate(request.RunRequest, request.DataAvailability ?? []);
            return result.IsValid
                ? Results.Json(result, jsonOptions)
                : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("ValidateStrategyEngineRun").DeclareNonMutating("Validates a posted run request against the engine registry and the supplied data availability; StrategyEngineValidationService holds only the registry and does not start or record a run.").RequireAnyPermission(UserPermission.ViewStrategies, UserPermission.ManageStrategies)
        .Produces<StrategyEngineValidationResult>(200)
        .Produces<StrategyEngineValidationResult>(400)
        .Produces(501);
    }

    private static IResult StrategyEngineUnavailable(JsonSerializerOptions jsonOptions)
        => Results.Json(
            new { error = "Strategy Engine services are not registered." },
            jsonOptions,
            statusCode: StatusCodes.Status501NotImplemented);

    private static StrategyRunReadScope ResolveStrategyRunReadScope(HttpContext context)
    {
        var accessor = context.RequestServices.GetService<IWorkstationTenantContextAccessor>();
        var tenantContext = accessor is not null && accessor.TryGetCurrent(out var current)
            ? current
            : HttpContextWorkstationTenantContextAccessor.Resolve(context);

        return new StrategyRunReadScope(tenantContext.TenantId, tenantContext.CompanyId);
    }

    private static async ValueTask<object?> RequireStrategyRunReadAccessAsync(
        EndpointFilterInvocationContext invocationContext,
        EndpointFilterDelegate next)
    {
        var httpContext = invocationContext.HttpContext;
        var runId = httpContext.Request.RouteValues["runId"]?.ToString();
        var readService = httpContext.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return Results.Problem(
                "Strategy run service is not registered.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        if (string.IsNullOrWhiteSpace(runId) ||
            !await readService.IsRunAccessibleAsync(
                    runId,
                    ResolveStrategyRunReadScope(httpContext),
                    httpContext.RequestAborted)
                .ConfigureAwait(false))
        {
            return Results.NotFound();
        }

        return await next(invocationContext).ConfigureAwait(false);
    }

    private static async Task<bool> AreStrategyRunsAccessibleAsync(
        StrategyRunReadService readService,
        IEnumerable<string> runIds,
        StrategyRunReadScope scope,
        CancellationToken ct)
    {
        foreach (var runId in runIds
                     .Where(static id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.Ordinal))
        {
            if (!await readService.IsRunAccessibleAsync(runId, scope, ct).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record StrategyEngineValidateRunRequest(
        StrategyEngineRunRequest RunRequest,
        IReadOnlyList<StrategyEngineDataAvailability>? DataAvailability);
}
