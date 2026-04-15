using System.Text.Json;
using Meridian.Application.EnvironmentDesign;
using Meridian.Contracts.EnvironmentDesign;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class EnvironmentDesignerEndpoints
{
    public static void MapEnvironmentDesignerEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/environment-designer").WithTags("Environment Designer");

        group.MapGet("/drafts", async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            var drafts = await service.ListDraftsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(drafts, jsonOptions);
        })
        .WithName("ListEnvironmentDrafts")
        .Produces<IReadOnlyList<EnvironmentDraftDto>>(StatusCodes.Status200OK);

        group.MapGet("/drafts/{draftId:guid}", async (Guid draftId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            var draft = await service.GetDraftAsync(draftId, context.RequestAborted).ConfigureAwait(false);
            return draft is null ? Results.NotFound() : Results.Json(draft, jsonOptions);
        })
        .WithName("GetEnvironmentDraft")
        .Produces<EnvironmentDraftDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/drafts", async (JsonElement body, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<CreateEnvironmentDraftRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var draft = await service.CreateDraftAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(draft, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateEnvironmentDraft")
        .Produces<EnvironmentDraftDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/drafts/{draftId:guid}", async (Guid draftId, JsonElement body, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            var draft = JsonSerializer.Deserialize<EnvironmentDraftDto>(body.GetRawText(), jsonOptions);
            if (draft is null)
            {
                return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var saved = await service.SaveDraftAsync(
                draft.DraftId == draftId ? draft : draft with { DraftId = draftId },
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(saved, jsonOptions);
        })
        .WithName("SaveEnvironmentDraft")
        .Produces<EnvironmentDraftDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapDelete("/drafts/{draftId:guid}", async (Guid draftId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            await service.DeleteDraftAsync(draftId, context.RequestAborted).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithName("DeleteEnvironmentDraft")
        .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/validate", async (JsonElement body, HttpContext context) =>
        {
            var validationService = context.RequestServices.GetService<IEnvironmentValidationService>();
            if (validationService is null) return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<ValidateDraftEnvelope>(body.GetRawText(), jsonOptions);
            if (request?.Draft is null)
            {
                return Results.Problem("Draft payload is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var validation = await validationService
                .ValidateAsync(request.Draft, request.PublishPlan, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(validation, jsonOptions);
        })
        .WithName("ValidateEnvironmentDraft")
        .Produces<EnvironmentValidationResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/publish/preview", async (JsonElement body, HttpContext context) =>
        {
            var publishService = context.RequestServices.GetService<IEnvironmentPublishService>();
            if (publishService is null) return ServiceUnavailable();

            var plan = JsonSerializer.Deserialize<EnvironmentPublishPlanDto>(body.GetRawText(), jsonOptions);
            if (plan is null)
            {
                return Results.Problem("Publish plan is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var preview = await publishService.PreviewPublishAsync(plan, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(preview, jsonOptions);
        })
        .WithName("PreviewEnvironmentPublish")
        .Produces<EnvironmentPublishPreviewDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/publish", async (JsonElement body, HttpContext context) =>
        {
            var publishService = context.RequestServices.GetService<IEnvironmentPublishService>();
            if (publishService is null) return ServiceUnavailable();

            var plan = JsonSerializer.Deserialize<EnvironmentPublishPlanDto>(body.GetRawText(), jsonOptions);
            if (plan is null)
            {
                return Results.Problem("Publish plan is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var version = await publishService.PublishAsync(plan, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(version, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("PublishEnvironmentDraft")
        .Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/versions", async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            var organizationId = ParseGuid(context.Request.Query["organizationId"]);
            var versions = await service.ListPublishedVersionsAsync(organizationId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(versions, jsonOptions);
        })
        .WithName("ListPublishedEnvironmentVersions")
        .Produces<IReadOnlyList<PublishedEnvironmentVersionDto>>(StatusCodes.Status200OK);

        group.MapGet("/versions/current", async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            var organizationId = ParseGuid(context.Request.Query["organizationId"]);
            var version = await service.GetCurrentPublishedVersionAsync(organizationId, context.RequestAborted).ConfigureAwait(false);
            return version is null ? Results.NotFound() : Results.Json(version, jsonOptions);
        })
        .WithName("GetCurrentPublishedEnvironmentVersion")
        .Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/versions/{versionId:guid}", async (Guid versionId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IEnvironmentDesignService>();
            if (service is null) return ServiceUnavailable();

            var version = await service.GetPublishedVersionAsync(versionId, context.RequestAborted).ConfigureAwait(false);
            return version is null ? Results.NotFound() : Results.Json(version, jsonOptions);
        })
        .WithName("GetPublishedEnvironmentVersion")
        .Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/versions/{versionId:guid}/rollback", async (Guid versionId, JsonElement body, HttpContext context) =>
        {
            var publishService = context.RequestServices.GetService<IEnvironmentPublishService>();
            if (publishService is null) return ServiceUnavailable();

            var request = JsonSerializer.Deserialize<RollbackEnvironmentVersionRequest>(body.GetRawText(), jsonOptions);
            if (request is null)
            {
                return Results.Problem("Rollback request is required.", statusCode: StatusCodes.Status400BadRequest);
            }

            var rolledBack = await publishService.RollbackAsync(
                request.VersionId == versionId ? request : request with { VersionId = versionId },
                context.RequestAborted).ConfigureAwait(false);
            return Results.Json(rolledBack, jsonOptions);
        })
        .WithName("RollbackEnvironmentVersion")
        .Produces<PublishedEnvironmentVersionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/runtime/current", async (HttpContext context) =>
        {
            var runtimeService = context.RequestServices.GetService<IEnvironmentRuntimeProjectionService>();
            if (runtimeService is null) return ServiceUnavailable();

            var organizationId = ParseGuid(context.Request.Query["organizationId"]);
            var runtime = await runtimeService.GetCurrentRuntimeAsync(organizationId, context.RequestAborted).ConfigureAwait(false);
            return runtime is null ? Results.NotFound() : Results.Json(runtime, jsonOptions);
        })
        .WithName("GetCurrentEnvironmentRuntime")
        .Produces<PublishedEnvironmentRuntimeDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/runtime/versions/{versionId:guid}", async (Guid versionId, HttpContext context) =>
        {
            var runtimeService = context.RequestServices.GetService<IEnvironmentRuntimeProjectionService>();
            if (runtimeService is null) return ServiceUnavailable();

            var runtime = await runtimeService.GetRuntimeForVersionAsync(versionId, context.RequestAborted).ConfigureAwait(false);
            return runtime is null ? Results.NotFound() : Results.Json(runtime, jsonOptions);
        })
        .WithName("GetEnvironmentRuntimeForVersion")
        .Produces<PublishedEnvironmentRuntimeDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static Guid? ParseGuid(string? value)
        => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static IResult ServiceUnavailable()
        => Results.Problem(
            "Environment designer services are not available.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private sealed record ValidateDraftEnvelope(
        EnvironmentDraftDto Draft,
        EnvironmentPublishPlanDto? PublishPlan = null);
}
