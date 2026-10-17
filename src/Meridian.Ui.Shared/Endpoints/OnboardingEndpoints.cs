using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>Read-only accounting onboarding; no route confers posting or accounting authority.</summary>
public static class OnboardingEndpoints
{
    public static void MapOnboardingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Accounting Onboarding")
            .RequireAnyPermission(UserPermission.AdminMaintenance, UserPermission.ManageFundStructure);
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (!scope.HasTenantScope || !TenantReadPredicate.IsResolvedTenant(scope.CompanyId) || string.IsNullOrWhiteSpace(scope.Actor))
                return EndpointHelpers.Forbidden();

            try
            {
                if (HttpMethods.IsPost(context.Request.Method) &&
                    !context.Request.Path.Value!.EndsWith("/replay", StringComparison.Ordinal))
                    OperationsOriginGuard.RequireHumanOperator(
                        EndpointAuthorization.ResolveTrustedActionOrigin(context, OperationsActionOriginDto.HumanOperator),
                        "change onboarding decisions");
                return await next(invocation).ConfigureAwait(false);
            }
            catch (OnboardingConcurrencyException ex) { return Results.Conflict(new { error = ex.Message }); }
            catch (HumanOperatorRequiredException ex) { return Results.Json(new { error = ex.Message }, statusCode: 403); }
            catch (UnauthorizedAccessException) { return EndpointHelpers.Forbidden(); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        group.MapGet(UiApiRoutes.OnboardingWorkspaces, async (HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            return Results.Json(await service.ListAsync(scope.TenantId!, scope.CompanyId!, context.RequestAborted), jsonOptions);
        }).WithName("ListOnboardingWorkspaces");

        Mutation(group.MapPost(UiApiRoutes.OnboardingWorkspaces, async (
            CreateOnboardingWorkspaceRequestDto request, HttpContext context,
            OnboardingWorkspaceService service, ICloseReadinessSubjectSource subjects, [FromServices] IFundProfileTenancyRegistry tenancy) =>
        {
            var scope = Scope(context);
            ArgumentNullException.ThrowIfNull(request.Scope);
            ArgumentNullException.ThrowIfNull(request.Scope.AccountIds);
            var trustedScope = request.Scope with { TenantId = scope.TenantId!, CompanyId = scope.CompanyId! };
            var ownership = await tenancy.ResolveAsync(trustedScope.FundProfileId, context.RequestAborted);
            if (ownership is null || !ownership.IsHeldBy(scope.TenantId) ||
                !string.Equals(ownership.CompanyId, scope.CompanyId, StringComparison.Ordinal))
                return EndpointHelpers.Forbidden();
            foreach (var account in trustedScope.AccountIds)
            {
                if (!Guid.TryParse(account, out var accountId))
                    throw new OnboardingValidationException("Select retained financial account identities for the onboarding population.");
                var subject = await subjects.GetSubjectAsync(new CloseReadinessScopeDto(
                    trustedScope.FundProfileId, trustedScope.LedgerBookId, accountId, trustedScope.EntityId, null), context.RequestAborted);
                if (subject?.Status != "Ready")
                    throw new OnboardingValidationException("The selected entity, book and account population could not be verified.");
            }
            return Results.Json(await service.CreateAsync(request with { Scope = trustedScope }, scope.Actor!, context.RequestAborted), jsonOptions);
        }).WithName("CreateOnboardingWorkspace"));

        group.MapGet(UiApiRoutes.OnboardingWorkspace, async (string workspaceId, HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            var workspace = await service.GetAsync(scope.TenantId!, scope.CompanyId!, workspaceId, context.RequestAborted);
            return workspace is null ? Results.NotFound() : Results.Json(workspace, jsonOptions);
        }).WithName("GetOnboardingWorkspace");

        group.MapGet(UiApiRoutes.OnboardingSources, async (
            string workspaceId, HttpContext context, OnboardingWorkspaceService service, OnboardingComparisonSource sources) =>
        {
            var scope = Scope(context);
            var workspace = await service.GetAsync(scope.TenantId!, scope.CompanyId!, workspaceId, context.RequestAborted);
            return workspace is null ? Results.NotFound() : Results.Json(
                await sources.GetSelectionAsync(workspace, context.RequestAborted), jsonOptions);
        }).WithName("ListOnboardingSources");

        Mutation(group.MapPost(UiApiRoutes.OnboardingCriteria, async (
            string workspaceId, UpdateOnboardingCriteriaRequestDto request, HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            return Results.Json(await service.UpdateCriteriaAsync(scope.TenantId!, scope.CompanyId!, workspaceId,
                request, scope.Actor!, context.RequestAborted), jsonOptions);
        }).WithName("UpdateOnboardingCriteria"));

        Mutation(group.MapPost(UiApiRoutes.OnboardingComparisons, async (
            string workspaceId, CaptureOnboardingComparisonRequestDto request, HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            return Results.Json(await service.CompareAsync(scope.TenantId!, scope.CompanyId!, workspaceId,
                request, scope.Actor!, context.RequestAborted), jsonOptions);
        }).WithName("CaptureOnboardingComparison"));

        Mutation(group.MapPost(UiApiRoutes.OnboardingDifferenceAssignment, async (
            string workspaceId, string differenceKey, AssignOnboardingDifferenceRequestDto request,
            HttpContext context, OnboardingWorkspaceService service, IEvidenceArtifactStore evidence) =>
        {
            var scope = Scope(context);
            await VerifyEvidenceAsync(service, evidence, scope, workspaceId, request.EvidenceIds, context.RequestAborted);
            return Results.Json(await service.AssignDifferenceAsync(scope.TenantId!, scope.CompanyId!, workspaceId,
                differenceKey, request, scope.Actor!, context.RequestAborted), jsonOptions);
        }).WithName("AssignOnboardingDifference"));

        Mutation(group.MapPost(UiApiRoutes.OnboardingReviews, async (
            string workspaceId, ReviewOnboardingWorkspaceRequestDto request, HttpContext context,
            OnboardingWorkspaceService service, IEvidenceArtifactStore evidence) =>
        {
            var scope = Scope(context);
            await VerifyEvidenceAsync(service, evidence, scope, workspaceId, request.EvidenceIds, context.RequestAborted);
            return Results.Json(await service.ReviewAsync(scope.TenantId!, scope.CompanyId!, workspaceId,
                request, scope.Actor!, context.RequestAborted), jsonOptions);
        }).WithName("ReviewOnboardingWorkspace"));

        Mutation(group.MapPost(UiApiRoutes.OnboardingPackets, async (
            string workspaceId, FreezeOnboardingPacketRequestDto request, HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            return Results.Json(await service.FreezePacketAsync(scope.TenantId!, scope.CompanyId!, workspaceId,
                request, scope.Actor!, context.RequestAborted), jsonOptions);
        }).WithName("FreezeOnboardingReadinessPacket"));

        group.MapGet(UiApiRoutes.OnboardingPacket, async (
            string workspaceId, string packetId, HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            var packet = await service.GetPacketAsync(scope.TenantId!, scope.CompanyId!, workspaceId,
                packetId, context.RequestAborted);
            return packet is null ? Results.NotFound() : Results.Json(packet, jsonOptions);
        }).WithName("GetOnboardingReadinessPacket");

        group.MapGet(UiApiRoutes.OnboardingPacketExport, async (
            string workspaceId, string packetId, HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            var packet = await service.GetPacketAsync(scope.TenantId!, scope.CompanyId!, workspaceId, packetId, context.RequestAborted);
            // Serialize on the server: passing decimals through JavaScript before download loses precision.
            return packet is null ? Results.NotFound() : Results.File(
                JsonSerializer.SerializeToUtf8Bytes(packet, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                "application/json", $"onboarding-readiness-{packet.PacketId}.json");
        }).WithName("DownloadOnboardingReadinessPacket");

        group.MapPost(UiApiRoutes.OnboardingReplay, async (
            string workspaceId, string comparisonId, HttpContext context, OnboardingWorkspaceService service) =>
        {
            var scope = Scope(context);
            var comparison = await service.ReplayComparisonAsync(scope.TenantId!, scope.CompanyId!, workspaceId,
                comparisonId, context.RequestAborted);
            return comparison is null ? Results.NotFound() : Results.Json(comparison, jsonOptions);
        }).WithName("ReplayOnboardingComparison");
    }

    private static WorkstationTenantContext Scope(HttpContext context)
        => HttpContextWorkstationTenantContextAccessor.Resolve(context);

    private static async Task VerifyEvidenceAsync(OnboardingWorkspaceService service, IEvidenceArtifactStore evidence,
        WorkstationTenantContext scope, string workspaceId, IReadOnlyList<string> evidenceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evidenceIds);
        var workspace = await service.GetAsync(scope.TenantId!, scope.CompanyId!, workspaceId, ct)
            ?? throw new KeyNotFoundException();
        var retained = workspace.Comparisons.SelectMany(run => run.Inputs.Snapshots)
            .SelectMany(snapshot => snapshot.EvidenceIds.Append(snapshot.SnapshotId)).ToHashSet(StringComparer.Ordinal);
        foreach (var id in evidenceIds)
        {
            var vaultId = EvidenceVaultReference.TryParseCanonical(id, out var parsedVaultId) ? parsedVaultId : id;
            if (!retained.Contains(id) && !await evidence.VerifyRetainedContentAsync(vaultId, scope.TenantId!, scope.CompanyId!, ct))
                throw new OnboardingValidationException("Supporting evidence must be a retained comparison source or verified evidence in this tenant and company.");
        }
    }

    private static void Mutation(RouteHandlerBuilder route)
        => route.RequireFundScopedWriteTenant().RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
}
