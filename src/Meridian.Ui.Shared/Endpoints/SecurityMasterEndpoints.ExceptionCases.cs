using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Storage.SecurityMaster;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using AppSecurityMaster = Meridian.Application.SecurityMaster;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Securities to shadow-compare and the valuation date to compare from.</summary>
public sealed record CashFlowShadowCompareRequest(IReadOnlyList<Guid> SecurityIds, DateOnly? ValuationDate);

/// <summary>Body assigning an exception case.</summary>
public sealed record AssignSecurityMasterExceptionCaseRequest(string Assignee);

public static partial class SecurityMasterEndpoints
{
    /// <summary>
    /// Review D2: the deployment-global Security Master exception queue (conflicts, identifier
    /// ambiguities and data-quality violations), with SLA due dates and assignment.
    /// </summary>
    private static void MapExceptionCaseEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(UiApiRoutes.SecurityMasterExceptionCases, async (
            string? status, [FromServices] ISecurityMasterExceptionCaseStore cases, CancellationToken ct) =>
        {
            var rows = await cases.ListAsync(string.IsNullOrWhiteSpace(status) ? SecurityMasterExceptionCaseStatus.Open : status.Trim(), ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            return Results.Json(rows.Select(row => new { row, slaBreached = row.IsBreached(now), ageHours = Math.Round((now - row.OpenedAt).TotalHours, 1) }), jsonOptions);
        })
        .WithName("ListSecurityMasterExceptionCases")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);

        group.MapPost(UiApiRoutes.SecurityMasterExceptionCasesSync, async (
            HttpContext context, [FromServices] AppSecurityMaster.SecurityMasterExceptionCaseSync sync, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            return Results.Json(await sync.SyncAsync(ct).ConfigureAwait(false), jsonOptions);
        })
        .WithName("SyncSecurityMasterExceptionCases");

        group.MapPost(UiApiRoutes.SecurityMasterExceptionCaseAssign, async (
            Guid caseId, AssignSecurityMasterExceptionCaseRequest request, HttpContext context,
            [FromServices] ISecurityMasterExceptionCaseStore cases, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            if (string.IsNullOrWhiteSpace(request.Assignee))
                return Results.Problem("An assignee is required.", statusCode: StatusCodes.Status400BadRequest);
            return await cases.AssignAsync(caseId, request.Assignee.Trim(), ct).ConfigureAwait(false)
                ? Results.NoContent()
                : Results.Conflict(new { error = $"Case '{caseId:D}' is not open." });
        })
        .WithName("AssignSecurityMasterExceptionCase");

        // Review B1: read-only diff of the legacy and contractual cash-flow engines; no behaviour switch.
        group.MapPost(UiApiRoutes.SecurityMasterCashFlowShadowCompare, async (
            CashFlowShadowCompareRequest request,
            [FromServices] Meridian.Application.SecurityMaster.CashFlow.CashFlowEngineShadowComparer comparer,
            CancellationToken ct) =>
        {
            if (request.SecurityIds is not { Count: > 0 and <= 500 })
                return Results.Problem("Name between 1 and 500 security ids.", statusCode: StatusCodes.Status400BadRequest);
            var report = await comparer.CompareAsync(request.SecurityIds, request.ValuationDate ?? DateOnly.FromDateTime(DateTime.UtcNow), ct).ConfigureAwait(false);
            return Results.Json(report, jsonOptions);
        })
        .WithName("ShadowCompareSecurityMasterCashFlows")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);
    }
}
