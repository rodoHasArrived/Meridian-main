using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapChiefOfStaffEndpoints(IEndpointRouteBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost("/chief-of-staff/sessions", async (
            ChiefOfStaffStartRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["A Chief of Staff session request payload is required."]
                });
            }

            var service = context.RequestServices.GetService<IChiefOfStaffSessionService>();
            if (service is null)
            {
                return Results.Problem("Chief of Staff session service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var session = await service.StartSessionAsync(request, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(session, jsonOptions);
        })
        .WithName("CreateWorkstationChiefOfStaffSession")
        .Produces<ChiefOfStaffSessionDto>(200)
        .Produces(403)
        .Produces(501);

        group.MapGet("/chief-of-staff/sessions", async (
            string? workspace,
            string? fundProfileId,
            Guid? fundAccountId,
            ChiefOfStaffSessionStatusDto? status,
            int? limit,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IChiefOfStaffSessionService>();
            if (service is null)
            {
                return Results.Problem("Chief of Staff session service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var sessions = await service.ListSessionsAsync(
                    new ChiefOfStaffSessionQueryDto(
                        Workspace: workspace,
                        FundProfileId: fundProfileId,
                        FundAccountId: fundAccountId,
                        Status: status,
                        Limit: limit ?? 25),
                    context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(sessions, jsonOptions);
        })
        .WithName("ListWorkstationChiefOfStaffSessions")
        .Produces<IReadOnlyList<ChiefOfStaffSessionSummaryDto>>(200)
        .Produces(501);

        group.MapGet("/chief-of-staff/sessions/{sessionId:guid}", async (Guid sessionId, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IChiefOfStaffSessionService>();
            if (service is null)
            {
                return Results.Problem("Chief of Staff session service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var session = await service.GetSessionAsync(sessionId, context.RequestAborted).ConfigureAwait(false);
            return session is null ? Results.NotFound() : Results.Json(session, jsonOptions);
        })
        .WithName("GetWorkstationChiefOfStaffSession")
        .Produces<ChiefOfStaffSessionDto>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost("/chief-of-staff/sessions/{sessionId:guid}/decisions", async (
            Guid sessionId,
            ChiefOfStaffDecisionRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (request is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["A Chief of Staff decision request payload is required."]
                });
            }

            var service = context.RequestServices.GetService<IChiefOfStaffSessionService>();
            if (service is null)
            {
                return Results.Problem("Chief of Staff session service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var session = await service.SubmitDecisionAsync(sessionId, request, context.RequestAborted).ConfigureAwait(false);
                return Results.Json(session, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("SubmitWorkstationChiefOfStaffDecision")
        .Produces<ChiefOfStaffSessionDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapPost("/chief-of-staff/sessions/{sessionId:guid}/export-trace", async (
            Guid sessionId,
            ChiefOfStaffTraceExportRequestDto? request,
            HttpContext context) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            var service = context.RequestServices.GetService<IChiefOfStaffSessionService>();
            if (service is null)
            {
                return Results.Problem("Chief of Staff session service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            try
            {
                var export = await service
                    .ExportTraceAsync(
                        sessionId,
                        request ?? new ChiefOfStaffTraceExportRequestDto("system"),
                        context.RequestAborted)
                    .ConfigureAwait(false);
                return Results.Json(export, jsonOptions);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
        .WithName("ExportWorkstationChiefOfStaffTrace")
        .Produces<ChiefOfStaffEvidenceExportDto>(200)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapGet("/chief-of-staff/health", async (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<IChiefOfStaffRuntimeClient>();
            if (service is null)
            {
                return Results.Problem("Chief of Staff runtime client is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var health = await service.GetHealthAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(health, jsonOptions);
        })
        .WithName("GetWorkstationChiefOfStaffHealth")
        .Produces<ChiefOfStaffRuntimeHealthDto>(200)
        .Produces(501);
    }
}
