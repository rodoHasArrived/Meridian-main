using System.Text.Json;
using Meridian.Application.Compliance;
using Meridian.Contracts.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static class ComplianceEndpoints
{
    public static WebApplication MapComplianceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        app.MapPost("/api/compliance/actions/evaluate", (
            HttpContext http,
            ComplianceActionRequest request,
            ICompliancePolicyEngine policy,
            ImmutableAuditLogService auditLog) =>
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
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageUsers));

        app.MapGet("/api/compliance/audit/extract", (ImmutableAuditLogService auditLog) =>
            Results.Ok(new { integrityValid = auditLog.VerifyIntegrity(), events = auditLog.GetAll() }))
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageUsers));

        app.MapGet("/api/compliance/controls/attestation", (ImmutableAuditLogService auditLog) =>
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

        app.MapPost("/api/compliance/access-reviews/run", (
            AccessReviewRunRequest request,
            AccessReviewService reviews) =>
        {
            var result = reviews.ReviewDormantPermissions(request.ActorId, request.ReviewedBy, request.CurrentRoles, request.LastUsedAtUtc);
            return Results.Ok(result);
        })
        .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageUsers));

        app.MapGet("/api/compliance/access-reviews", (AccessReviewService reviews) => Results.Ok(reviews.GetReviews()))
            .AddEndpointFilter(EndpointAuthorization.Require(UserPermission.ManageUsers));

        return app;
    }

    private static ActorContext BuildActorContext(HttpContext http)
    {
        var actorId = EndpointAuthorization.TryResolveActor(http, out var currentActor)
            ? currentActor
            : "anonymous";

        var roles = http.Items.TryGetValue(LoginSessionMiddleware.CurrentUserRoleKey, out var currentRole) && currentRole is UserRole role
            ? [role.ToString()]
            : [];

        var team = http.Request.Headers["X-Actor-Team"].FirstOrDefault();
        var mfa = http.User.Claims.Any(claim =>
            (claim.Type.Equals("amr", StringComparison.OrdinalIgnoreCase) ||
             claim.Type.Equals("acr", StringComparison.OrdinalIgnoreCase) ||
             claim.Type.Equals("mfa", StringComparison.OrdinalIgnoreCase)) &&
            claim.Value.Contains("mfa", StringComparison.OrdinalIgnoreCase));

        return new ActorContext(actorId, roles, team, http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers["X-Device-Id"].FirstOrDefault(), mfa);
    }
}

public sealed record AccessReviewRunRequest(string ActorId, string ReviewedBy, string[] CurrentRoles, DateTimeOffset LastUsedAtUtc);
