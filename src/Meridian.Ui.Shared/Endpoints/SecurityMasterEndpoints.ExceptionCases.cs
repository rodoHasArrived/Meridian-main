using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
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

/// <summary>An observed pool factor for an effective date.</summary>
public sealed record RecordFactorObservationRequest(DateOnly EffectiveDate, decimal Factor, string Source, string? EvidenceRef);

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
            return Results.Json(rows.Select(row => SecurityMasterReviewQueueProjection.ToQueueItem(row, now)).ToArray(), jsonOptions);
        })
        .WithName("ListSecurityMasterExceptionCases")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster)
        .Produces<IReadOnlyList<SecurityMasterExceptionCaseQueueItemDto>>(StatusCodes.Status200OK);

        group.MapPost(UiApiRoutes.SecurityMasterExceptionCasesSync, async (
            HttpContext context, [FromServices] AppSecurityMaster.SecurityMasterExceptionCaseSync sync, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            var result = await sync.SyncAsync(ct).ConfigureAwait(false);
            return Results.Json(new SecurityMasterExceptionSyncResultDto(result.Opened, result.Refreshed, result.Closed), jsonOptions);
        })
        .WithName("SyncSecurityMasterExceptionCases").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces<SecurityMasterExceptionSyncResultDto>(StatusCodes.Status200OK);

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
        .WithName("AssignSecurityMasterExceptionCase").RequirePermission(UserPermission.ModifySecurityMaster)
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status409Conflict);

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

        // Review C3: pool-factor observations (no terms revision); a restatement opens a case.
        group.MapGet(UiApiRoutes.SecurityMasterFactorObservations, async (
            Guid securityId, [FromServices] Meridian.Storage.SecurityMaster.ISecurityFactorObservationStore observations, CancellationToken ct) =>
            Results.Json(await observations.ListAsync(securityId, ct).ConfigureAwait(false), jsonOptions))
        .WithName("ListSecurityFactorObservations")
        .RequireAnyPermission(UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster);

        group.MapPost(UiApiRoutes.SecurityMasterFactorObservations, async (
            Guid securityId, RecordFactorObservationRequest request, HttpContext context,
            [FromServices] AppSecurityMaster.SecurityFactorObservationService service, CancellationToken ct) =>
        {
            var authorization = RequireSecurityMasterMutationPermission(context);
            if (authorization is not null)
                return authorization;
            try
            {
                var result = await service.RecordAsync(securityId, request.EffectiveDate, request.Factor, request.Source, request.EvidenceRef, ResolveActor(context), ct).ConfigureAwait(false);
                return Results.Json(result, jsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
        .WithName("RecordSecurityFactorObservation").RequirePermission(UserPermission.ModifySecurityMaster);
    }
}
