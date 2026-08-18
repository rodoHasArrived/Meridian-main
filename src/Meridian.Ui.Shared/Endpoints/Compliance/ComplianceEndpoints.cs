using System.Text.Json;
using Meridian.Audit.Compliance;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class ComplianceEndpoints
{
    public static WebApplication MapComplianceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapPost("/api/compliance/approval-requests", (
            HttpContext http,
            ComplianceApprovalRequestCommand request,
            [FromServices] IComplianceApprovalStore approvals) =>
        {
            var actor = BuildActorContext(http);
            var result = approvals.CreateRequest(actor, request);
            return Results.Json(result, statusCode: StatusCodes.Status201Created, options: jsonOptions);
        })
        .RequirePermission(UserPermission.ManageUsers);

        app.MapPost("/api/compliance/approval-requests/{approvalRequestId}/decisions", (
            HttpContext http,
            string approvalRequestId,
            ComplianceApprovalDecisionCommand request,
            [FromServices] IComplianceApprovalStore approvals) =>
        {
            try
            {
                var actor = BuildActorContext(http);
                return Results.Ok(approvals.RecordDecision(approvalRequestId, actor, request.Approved));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { error = "Compliance approval request was not found." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        })
        .RequirePermission(UserPermission.ManageUsers);

        app.MapPost("/api/compliance/actions/evaluate", (
            HttpContext http,
            ComplianceActionRequest request,
            [FromServices] ICompliancePolicyEngine policy,
            [FromServices] ImmutableAuditLogService auditLog) =>
        {
            var actor = BuildActorContext(http);
            var decision = policy.Evaluate(actor, request);
            if (!decision.Allowed)
            {
                return Results.Json(new { allowed = false, reason = decision.Reason }, statusCode: StatusCodes.Status403Forbidden, options: jsonOptions);
            }

            var evt = auditLog.Append(actor, request);
            return Results.Json(new { allowed = true, reason = decision.Reason, auditEventId = evt.EventId, hash = evt.Hash }, options: jsonOptions);
        })
        .RequirePermission(UserPermission.ManageUsers);

        app.MapGet("/api/compliance/audit/extract", ([FromServices] ImmutableAuditLogService auditLog) =>
            Results.Ok(new { integrityValid = auditLog.VerifyIntegrity(), events = auditLog.GetAll() }))
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageUsers));

        app.MapGet("/api/compliance/controls/attestation", ([FromServices] ImmutableAuditLogService auditLog) =>
            Results.Ok(new
            {
                generatedAtUtc = DateTimeOffset.UtcNow,
                controls = new[]
                {
                    "RBAC matrix for sensitive actions",
                    "Step-up controls with privileged role, dual approval, and MFA hook",
                    "Immutable append-only audit chain",
                    "Segregation-of-duties policy checks"
                },
                integrityValid = auditLog.VerifyIntegrity()
            }))
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageUsers));

        app.MapPost("/api/compliance/access-reviews/assess", async (
            HttpContext http,
            AccessReviewRunRequest request,
            [FromServices] AccessReviewService reviews,
            CancellationToken ct) =>
        {
            var reviewer = BuildActorContext(http).ActorId;
            var result = await reviews.AssessDormantPermissionsAsync(
                request.ActorId,
                reviewer,
                request.LastUsedAtUtc,
                ct).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .RequirePermission(UserPermission.ManageUsers);

        app.MapPost("/api/compliance/access-reviews/run", async (
            HttpContext http,
            AccessReviewRunRequest request,
            [FromServices] AccessReviewService reviews,
            CancellationToken ct) =>
        {
            var reviewer = BuildActorContext(http).ActorId;
            var result = await reviews.ApplyDormantPermissionRemediationAsync(
                request.ActorId,
                reviewer,
                request.LastUsedAtUtc,
                ct).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .RequirePermission(UserPermission.ManageUsers);

        app.MapGet("/api/compliance/access-reviews", ([FromServices] AccessReviewService reviews) => Results.Ok(reviews.GetReviews()))
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageUsers));

        return app;
    }

    private static ActorContext BuildActorContext(HttpContext http)
    {
        var actorId = EndpointAuthorization.TryResolveActor(http, out var currentActor)
            ? currentActor
            : "anonymous";

        var roles = http.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var currentRole) && currentRole is UserRole role
            ? new[] { role.ToString() }
            : Array.Empty<string>();

        var team = http.Request.Headers["X-Actor-Team"].FirstOrDefault();
        var mfa = http.User.Claims.Any(claim =>
            (claim.Type.Equals("amr", StringComparison.OrdinalIgnoreCase) ||
             claim.Type.Equals("acr", StringComparison.OrdinalIgnoreCase) ||
             claim.Type.Equals("mfa", StringComparison.OrdinalIgnoreCase)) &&
            claim.Value.Contains("mfa", StringComparison.OrdinalIgnoreCase));

        return new ActorContext(actorId, roles, team, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers["X-Device-Id"].FirstOrDefault(), mfa);
    }
}

public sealed record ComplianceApprovalDecisionCommand(bool Approved);

public sealed record AccessReviewRunRequest(string ActorId, DateTimeOffset LastUsedAtUtc);
