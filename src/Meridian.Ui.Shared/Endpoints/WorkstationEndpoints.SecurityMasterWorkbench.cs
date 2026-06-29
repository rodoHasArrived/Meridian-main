using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
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
                return WorkbenchMissingPayload("An UpdateSecurityFieldRequest body is required.");
            }

            var bound = request with { SecurityId = securityId };
            return await ExecuteWorkbenchAsync(
                () => service.UpdateSecurityFieldAsync(bound, context.RequestAborted), jsonOptions).ConfigureAwait(false);
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

            var bound = request with { SecurityId = securityId };
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

            var bound = request with { SecurityId = securityId };
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

            var bound = request with { SecurityId = securityId };
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

            var bound = request with { SecurityId = securityId };
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
    /// Executes a governed-write command and maps its domain failures to stable status codes. Unmapped
    /// exceptions are allowed to propagate to the framework's 500 handler rather than being masked.
    /// </summary>
    private static async Task<IResult> ExecuteWorkbenchAsync<T>(
        Func<Task<T>> action, JsonSerializerOptions jsonOptions)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
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

    private static string SecurityMasterSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.StartsWith(SecurityMasterApiRoutePrefix, StringComparison.Ordinal)
            ? route[SecurityMasterApiRoutePrefix.Length..]
            : route;
    }
}
