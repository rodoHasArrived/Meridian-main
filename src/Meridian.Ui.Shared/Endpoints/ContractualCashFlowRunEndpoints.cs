using System.Text.Json;
using Meridian.Application.DirectLending;
using Meridian.Application.Composition;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.SecurityMaster;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Meridian.Ui.Shared.Endpoints;

internal static class ContractualCashFlowRunEndpoints
{
    internal static void MapContractualCashFlowRuns(this RouteGroupBuilder group, string path, string kind,
        UserPermission view, UserPermission modify, JsonSerializerOptions json)
    {
        if (!ContractualCashFlowSubjectKinds.IsSupported(kind))
            throw new ArgumentException("Unsupported contractual cash-flow subject kind.", nameof(kind));
        var writeRoute = group.MapPost(path, async (Guid subjectId, CreateContractualCashFlowRunRequest? request, HttpContext context) =>
        {
            if (request is null || request.Projection is null || request.RunId == Guid.Empty)
                return Results.BadRequest("Run identity and projection request are required.");
            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            if (!await ExistsAsync(context, kind, subjectId).ConfigureAwait(false))
                return Results.NotFound();
            var bindDraft = kind == ContractualCashFlowSubjectKinds.Security && DraftChecksEnabled(context);
            var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
            if (bindDraft && (!scope.HasTenantScope || string.IsNullOrWhiteSpace(scope.CompanyId)))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var store = context.RequestServices.GetRequiredService<IContractualCashFlowRunStore>();
            try
            {
                // Retry identity is checked before looking up current terms or market data.
                var retained = await store.GetAsync(kind, subjectId, request.RunId, context.RequestAborted).ConfigureAwait(false);
                if (retained is not null)
                {
                    if (!CanReadBoundRun(retained, context)
                        || retained.AttestationContext is { } retainedContext
                        && !string.Equals(retainedContext.PreparedBy, actor, StringComparison.OrdinalIgnoreCase))
                        return Results.NotFound();
                    return ContractualCashFlowEngine.Hash(retained.Request) == ContractualCashFlowEngine.Hash(request.Projection)
                        ? Results.Json(retained, json) : Results.Conflict("Run identity is already bound to a different request.");
                }
                var preview = context.RequestServices.GetRequiredService<IContractualCashFlowPreviewService>();
                var result = kind == ContractualCashFlowSubjectKinds.Security
                    ? await preview.PreviewSecurityAsync(subjectId, request.Projection, context.RequestAborted).ConfigureAwait(false)
                    : await preview.PreviewLoanAsync(subjectId, request.Projection, context.RequestAborted).ConfigureAwait(false);
                if (result is null)
                    return Results.NotFound();
                if (!result.IsCalculated || result.ResolvedContract is null)
                    return Results.Json(result, json);
                var run = new ContractualCashFlowRun(request.RunId, kind, subjectId, DateTimeOffset.UtcNow, result.ResolvedContract, request.Projection, result, actor);
                if (bindDraft)
                    run = run with
                    {
                        AttestationContext = new(scope.TenantId!, scope.CompanyId!, actor,
                        EndpointAuthorization.DeriveActionOriginFromPrincipal(context))
                    };
                return Results.Json(await store.SaveAsync(run, context.RequestAborted).ConfigureAwait(false), json);
            }
            catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["projection"] = [EndpointHelpers.RefusalDetail(ex)] }); }
            catch (InvalidOperationException) { return Results.Conflict("Run identity conflicts with retained data."); }
        })
        .WithName("RetainContractual" + kind + "CashFlowRun")
        .RequirePermission(modify)
        .RequireRateLimiting(kind == ContractualCashFlowSubjectKinds.Loan
            ? UiEndpoints.DirectLendingMutationRateLimitPolicy
            : UiEndpoints.MutationRateLimitPolicy)
        .Produces<ContractualCashFlowRun>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);
        if (kind == ContractualCashFlowSubjectKinds.Loan)
            writeRoute.RequireFundScopedWriteTenant();

        group.MapGet(path + "/{runId:guid}", async (Guid subjectId, Guid runId, HttpContext context) =>
        {
            if (!await ExistsAsync(context, kind, subjectId).ConfigureAwait(false))
                return Results.NotFound();
            var store = context.RequestServices.GetRequiredService<IContractualCashFlowRunStore>();
            var run = await store.GetAsync(kind, subjectId, runId, context.RequestAborted).ConfigureAwait(false);
            return run is null || !CanReadBoundRun(run, context) ? Results.NotFound() : Results.Json(run, json);
        }).WithName("GetContractual" + kind + "CashFlowRun").RequireAnyPermission(view, modify)
        .Produces<ContractualCashFlowRun>().Produces(StatusCodes.Status404NotFound);

        // Pure read over two retained packets of the same subject; nothing is recalculated against current terms.
        group.MapGet(path + "/{runId:guid}/compare/{otherRunId:guid}", async (Guid subjectId, Guid runId, Guid otherRunId, HttpContext context) =>
        {
            if (runId == otherRunId)
                return Results.BadRequest("Choose two different retained runs to compare.");
            if (!await ExistsAsync(context, kind, subjectId).ConfigureAwait(false))
                return Results.NotFound();
            var store = context.RequestServices.GetRequiredService<IContractualCashFlowRunStore>();
            var left = await store.GetAsync(kind, subjectId, runId, context.RequestAborted).ConfigureAwait(false);
            var right = await store.GetAsync(kind, subjectId, otherRunId, context.RequestAborted).ConfigureAwait(false);
            if (left is null || right is null || !CanReadBoundRun(left, context) || !CanReadBoundRun(right, context))
                return Results.NotFound();
            try
            { return Results.Json(ContractualCashFlowRunComparer.Compare(left, right), json); }
            catch (ArgumentException exception)
            { return Results.BadRequest(exception.Message); }
        }).WithName("CompareContractual" + kind + "CashFlowRuns").RequireAnyPermission(view, modify)
        .Produces<ContractualCashFlowRunComparison>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);
    }

    private static bool DraftChecksEnabled(HttpContext context) =>
        (context.RequestServices.GetService<CompositionConfiguration>()?.HostConfiguration
            ?? context.RequestServices.GetService<IConfiguration>())?
            .GetValue<bool>("SinglePrincipalAttestation:EnableDraftChecks") ?? false;

    private static bool CanReadBoundRun(ContractualCashFlowRun run, HttpContext context)
    {
        if (run.AttestationContext is not { } binding)
            return true;
        var scope = HttpContextWorkstationTenantContextAccessor.Resolve(context);
        return binding.SchemaVersion == 1 && scope.HasTenantScope
            && string.Equals(binding.TenantId, scope.TenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(binding.CompanyId, scope.CompanyId, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> ExistsAsync(HttpContext context, string kind, Guid subjectId)
    {
        if (kind == ContractualCashFlowSubjectKinds.Security)
            return await context.RequestServices.GetRequiredService<ISecurityMasterQueryService>()
            .GetByIdAsync(subjectId, context.RequestAborted).ConfigureAwait(false) is not null;
        var loans = context.RequestServices.GetService<IDirectLendingService>();
        return loans is not null && await loans.GetLoanAsync(subjectId, context.RequestAborted).ConfigureAwait(false) is not null;
    }
}
