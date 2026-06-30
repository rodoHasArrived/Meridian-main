using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private const string SecurityMasterApiRoutePrefix = "/api/security-master";

    /// <summary>
    /// Maps the Security Master Passport Workbench governed-write surface (Phase 4). Each route requires
    /// <see cref="UserPermission.ModifySecurityMaster"/>, is tenant-scoped, and routes the request through
    /// <see cref="ISecurityMasterWorkbenchCommandService"/>. The path <c>securityId</c> is authoritative —
    /// the request body's id is overwritten so a mismatched body can never target another security.
    ///
    /// <para>Domain failures map to stable status codes via <see cref="ExecuteWorkbenchAsync{T}"/>:
    /// stale expected version → <b>409</b> with the current version; an invalid lifecycle transition
    /// (e.g. publishing a revision that is not Approved, or an unknown revision) → <b>409</b>; a rejected
    /// invariant (missing justification, unacknowledged policy deviation, non-candidate winner) → <b>422</b>;
    /// a publish whose side-effect fan-out failed → <b>500</b> (the append is durable and handlers are
    /// idempotent, so the caller may retry).</para>
    /// </summary>
    internal static void MapSecurityMasterWorkbenchEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(SecurityMasterApiRoutePrefix)
            .WithTags("SecurityMasterWorkbench")
            .RequireWorkstationTenantScope();

        group.MapPost(SecurityMasterSubroute(UiApiRoutes.SecurityMasterWorkbenchField), async (
            Guid securityId,
            UpdateSecurityFieldRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterWorkbenchCommandService? service,
            [FromServices] IFundProfileTenantGuard? fundGuard,
            [FromServices] IFundProfileTenancyRegistry? fundRegistry) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster))
            {
                return EndpointHelpers.Forbidden();
            }

            if (service is null)
            {
                return WorkbenchServiceUnavailable();
            }

            if (request is null)
            {
                return WorkbenchMissingPayload("An UpdateSecurityFieldRequest body is required.");
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var tenantContext = HttpContextWorkstationTenantContextAccessor.Resolve(context);

            // Defense-in-depth tenant guard: a body-supplied fund profile is persisted on the draft and
            // later scopes downstream restatement impact, so refuse one that demonstrably belongs to a
            // different company before it is stored. The guard never denies an own/unknown fund (no
            // false-deny); the single-company-per-deployment boundary remains the primary control. See
            // docs/security/security-remediation-backlog.md (SEC-005).
            if (fundGuard is not null && !string.IsNullOrWhiteSpace(request.FundProfileId))
            {
                var fundDecision = await fundGuard
                    .EvaluateAsync(tenantContext, request.FundProfileId, context.RequestAborted)
                    .ConfigureAwait(false);
                if (!fundDecision.IsAllowed)
                {
                    return EndpointHelpers.Forbidden();
                }
            }

            // The acting principal is server-derived from the session, never trusted from the body.
            var bound = request with { SecurityId = securityId, Actor = actor };
            return await ExecuteWorkbenchAsync(
                () => service.UpdateSecurityFieldAsync(bound, context.RequestAborted),
                jsonOptions,
                // The ownership claim runs after the write has committed, so it deliberately does NOT
                // observe context.RequestAborted: a client disconnect/timeout between commit and bind
                // would otherwise cancel the bind and leave the committed fund unbound (and unbound funds
                // fail open on reads, so another tenant could later claim or see its impact).
                onSuccess: _ => ClaimFundOwnershipAsync(
                    fundRegistry, tenantContext, request.FundProfileId, CancellationToken.None)).ConfigureAwait(false);
        })
        .WithName("SecurityMasterWorkbenchField")
        .Produces<SecurityMasterEditResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(SecurityMasterSubroute(UiApiRoutes.SecurityMasterWorkbenchResolveConflict), async (
            Guid securityId,
            ResolveSourceConflictRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterWorkbenchCommandService? service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster))
            {
                return EndpointHelpers.Forbidden();
            }

            if (service is null)
            {
                return WorkbenchServiceUnavailable();
            }

            if (request is null)
            {
                return WorkbenchMissingPayload("A ResolveSourceConflictRequest body is required.");
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var bound = request with { SecurityId = securityId, Actor = actor };
            return await ExecuteWorkbenchAsync(
                () => service.ResolveSourceConflictAsync(bound, context.RequestAborted), jsonOptions).ConfigureAwait(false);
        })
        .WithName("SecurityMasterWorkbenchResolveConflict")
        .Produces<SecurityMasterConflictResolutionDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(SecurityMasterSubroute(UiApiRoutes.SecurityMasterWorkbenchSubmit), async (
            Guid securityId,
            SubmitSecurityMasterRevisionRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterWorkbenchCommandService? service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster))
            {
                return EndpointHelpers.Forbidden();
            }

            if (service is null)
            {
                return WorkbenchServiceUnavailable();
            }

            if (request is null)
            {
                return WorkbenchMissingPayload("A SubmitSecurityMasterRevisionRequest body is required.");
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            // The governed surface must submit through the approval workflow: a workflow-less submit
            // advances the revision to Submitted without binding a workflow, but the only approval path
            // requires one, leaving the revision stuck and unpublishable. Require the workflow context here.
            if (request.WorkflowId is not { } workflowId || workflowId == Guid.Empty)
            {
                return WorkbenchUnprocessable(
                    "workflow-required",
                    "A workflowId is required to submit a Security Master revision through the governed approval gate.");
            }

            // Actor is the server-derived submitter; Reviewer stays as the body-supplied assignment (who
            // must later approve) — the command service enforces that the two differ.
            var bound = request with { SecurityId = securityId, Actor = actor };
            return await ExecuteWorkbenchAsync(
                () => service.SubmitForApprovalAsync(bound, context.RequestAborted), jsonOptions).ConfigureAwait(false);
        })
        .WithName("SecurityMasterWorkbenchSubmit")
        .Produces<SecurityMasterEditResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(SecurityMasterSubroute(UiApiRoutes.SecurityMasterWorkbenchApprove), async (
            Guid securityId,
            ApproveSecurityMasterRevisionRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterWorkbenchCommandService? service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster))
            {
                return EndpointHelpers.Forbidden();
            }

            if (service is null)
            {
                return WorkbenchServiceUnavailable();
            }

            if (request is null)
            {
                return WorkbenchMissingPayload("An ApproveSecurityMasterRevisionRequest body is required.");
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            // The caller IS the reviewer: both Actor and Reviewer are server-derived from the session, so a
            // caller cannot post the assigned reviewer's name and record/bypass approval as that person.
            var bound = request with { SecurityId = securityId, Actor = actor, Reviewer = actor };
            return await ExecuteWorkbenchAsync(
                () => service.ApproveRevisionAsync(bound, context.RequestAborted), jsonOptions).ConfigureAwait(false);
        })
        .WithName("SecurityMasterWorkbenchApprove")
        .Produces<SecurityMasterEditResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost(SecurityMasterSubroute(UiApiRoutes.SecurityMasterWorkbenchPublish), async (
            Guid securityId,
            PublishSecurityMasterRevisionRequest? request,
            HttpContext context,
            [FromServices] ISecurityMasterWorkbenchCommandService? service) =>
        {
            if (!EndpointAuthorization.HasPermission(context, UserPermission.ModifySecurityMaster))
            {
                return EndpointHelpers.Forbidden();
            }

            if (service is null)
            {
                return WorkbenchServiceUnavailable();
            }

            if (request is null)
            {
                return WorkbenchMissingPayload("A PublishSecurityMasterRevisionRequest body is required.");
            }

            if (!EndpointAuthorization.TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var bound = request with { SecurityId = securityId, Actor = actor };
            return await ExecuteWorkbenchAsync(
                () => service.PublishRevisionAsync(bound, context.RequestAborted), jsonOptions).ConfigureAwait(false);
        })
        .WithName("SecurityMasterWorkbenchPublish")
        .Produces<SecurityMasterPublishResultDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Claims the fund profile for the caller's tenant after a governed write succeeds (trust-on-first-use,
    /// SEC-005). Binding only on success means a failed or rejected write can never squat a fund id and
    /// block the tenant that later performs the first real write. This runs <em>after</em> the write has
    /// committed, so the caller passes a token that is independent of the HTTP request (a client disconnect
    /// must not leave the committed fund unbound), and every failure — including a cancellation — is
    /// swallowed: ownership bookkeeping is best-effort and must never turn a committed edit into a client
    /// error. The deployment boundary remains the control until the next successful bind.
    /// </summary>
    private static async Task ClaimFundOwnershipAsync(
        IFundProfileTenancyRegistry? registry,
        WorkstationTenantContext tenant,
        string? fundProfileId,
        CancellationToken ct)
    {
        if (registry is null || !tenant.HasTenantScope || string.IsNullOrWhiteSpace(fundProfileId))
        {
            return;
        }

        try
        {
            await registry.BindAsync(fundProfileId, tenant.TenantId!, tenant.CompanyId, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Ownership bookkeeping is best-effort and post-commit: never fail (or cancel out of) a
            // committed edit because the registry is unavailable.
        }
    }

    /// <summary>
    /// Executes a governed-write command and maps its domain failures to stable status codes. Unmapped
    /// exceptions are allowed to propagate to the framework's 500 handler rather than being masked. When
    /// the write succeeds, <paramref name="onSuccess"/> runs before the 200 response is produced.
    /// </summary>
    private static async Task<IResult> ExecuteWorkbenchAsync<T>(
        Func<Task<T>> action, JsonSerializerOptions jsonOptions, Func<T, Task>? onSuccess = null)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            if (onSuccess is not null)
            {
                await onSuccess(result).ConfigureAwait(false);
            }

            return Results.Json(result, jsonOptions, statusCode: StatusCodes.Status200OK);
        }
        catch (SecurityMasterConcurrencyException ex)
        {
            // Stale expected version: the passport advanced since load. Surface the current version so
            // the client can refetch and replay without losing the operator's edit.
            return Results.Json(
                new { error = "version-conflict", currentVersion = ex.CurrentVersion },
                jsonOptions,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (SecurityMasterPublishFailedException ex)
        {
            // The durable append already happened but a side-effect handler failed. Handlers are
            // idempotent and the revision stays Approved, so the caller may retry the publish.
            return Results.Json(
                new { error = "publish-side-effect-failed", message = ex.Message },
                jsonOptions,
                statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (SecurityMasterRevisionStateException ex)
        {
            // The revision is not in a state that permits this transition (e.g. publishing one that was
            // never approved, or an unknown/foreign revision) — a lifecycle conflict the client resolves
            // by refetching the revision.
            return Results.Json(
                new { error = "revision-state-conflict", message = ex.Message },
                jsonOptions,
                statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            // A rejected governed invariant: missing justification, unacknowledged policy deviation, or a
            // chosen winner that is not a conflict candidate. The request was understood but cannot be
            // processed as-is.
            return Results.Json(
                new { error = "unprocessable", message = ex.Message },
                jsonOptions,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (InvalidOperationException ex)
        {
            // A domain-state precondition failed (security not found for edit, missing trust snapshot, or
            // an approval gate that blocked the transition).
            return Results.Json(
                new { error = "conflict", message = ex.Message },
                jsonOptions,
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static IResult WorkbenchServiceUnavailable()
        => Results.Problem(
            "The Security Master workbench command service is not registered.",
            statusCode: StatusCodes.Status501NotImplemented);

    private static IResult WorkbenchMissingPayload(string detail)
        => Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [detail] });

    private static IResult WorkbenchUnprocessable(string error, string message)
        => Results.Json(new { error, message }, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static string SecurityMasterSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        // Only strip the prefix when the route is exactly the prefix or the prefix is followed by a path
        // separator, so a similarly-named sibling (e.g. "/api/security-master-templates") is never mangled.
        if (route.Equals(SecurityMasterApiRoutePrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return route.StartsWith(SecurityMasterApiRoutePrefix + "/", StringComparison.Ordinal)
            ? route[SecurityMasterApiRoutePrefix.Length..]
            : route;
    }
}
