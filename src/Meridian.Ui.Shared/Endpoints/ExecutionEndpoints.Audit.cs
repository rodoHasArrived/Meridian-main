using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class ExecutionEndpoints
{
    /// <summary>Composes retained execution audit browse and exact-order journey reads under the existing execution group.</summary>
    private static void MapExecutionAuditEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/audit", async (int? take, HttpContext context) =>
        {
            var auditTrail = context.RequestServices.GetService<ExecutionAuditTrailService>();
            if (auditTrail is null)
            {
                return Results.Json(Array.Empty<ExecutionAuditEntry>(), jsonOptions);
            }

            var entries = await auditTrail
                .GetRecentAsync(take ?? 100, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(entries, jsonOptions);
        })
        .WithName("GetExecutionAudit").RequirePermission(UserPermission.ViewTrades)
        .Produces<IReadOnlyList<ExecutionAuditEntry>>(200);

        group.MapGet("/audit/search", async (
            string? searchText,
            string? runId,
            string? category,
            string? action,
            string? outcome,
            string? actor,
            string? symbol,
            string? correlationId,
            string? objectKind,
            string? objectId,
            string? relatedObjectId,
            string? orderId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int? limit,
            HttpContext context) =>
        {
            if (!await AuditTrailHttpAuthority.CanReadExecutionFeedAsync(context).ConfigureAwait(false))
                return EndpointHelpers.Forbidden();
            var explorer = context.RequestServices.GetService<AuditTrailExplorerService>();
            if (explorer is null)
            {
                var auditTrail = context.RequestServices.GetService<ExecutionAuditTrailService>();
                explorer = auditTrail is null
                    ? new AuditTrailExplorerService()
                    : new AuditTrailExplorerService(auditTrail);
            }

            var result = await explorer.SearchAuthorizedAsync(
                new AuditTrailExplorerQueryDto(
                    SearchText: searchText,
                    RunId: runId,
                    Category: category,
                    Action: action,
                    Outcome: outcome,
                    Actor: actor,
                    Symbol: symbol,
                    CorrelationId: correlationId,
                    ObjectKind: objectKind,
                    ObjectId: objectId,
                    RelatedObjectId: relatedObjectId,
                    FromUtc: fromUtc,
                    ToUtc: toUtc,
                    Limit: limit ?? 100,
                    OrderId: orderId),
                (workflow, token) => AuditTrailHttpAuthority.CanReadWorkflowAsync(context, workflow, token),
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(result, jsonOptions);
        })
        .WithName("SearchExecutionAuditTrail").RequirePermission(UserPermission.ViewTrades)
        .Produces<AuditTrailExplorerResultDto>(200);
    }
}
