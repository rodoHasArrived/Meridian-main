using System.Text.Json;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

public static class ComplianceEndpoints
{
    public static void MapComplianceEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/compliance");

        group.MapGet("/audit", ([FromServices] ImmutableAuditLogService service) =>
            Results.Json(new { events = service.GetAll(), integrityOk = service.VerifyIntegrity() }, jsonOptions));

        group.MapGet("/attestations", ([FromServices] ImmutableAuditLogService audit, [FromServices] AccessReviewService reviews) =>
            Results.Json(new
            {
                auditIntegrity = audit.VerifyIntegrity(),
                latestAccessReviewUtc = reviews.LastRunUtc,
                dormantPermissionsRevoked = reviews.LastDormantRevocationCount
            }, jsonOptions));

        group.MapPost("/access-review/run", ([FromServices] AccessReviewService reviews) =>
        {
            var revoked = reviews.RunDormantPermissionCleanup();
            return Results.Json(new { revoked }, jsonOptions);
        });
    }
}

public sealed class AccessReviewService
{
    public DateTimeOffset? LastRunUtc { get; private set; }
    public int LastDormantRevocationCount { get; private set; }

    public int RunDormantPermissionCleanup()
    {
        LastRunUtc = DateTimeOffset.UtcNow;
        LastDormantRevocationCount = 0;
        return LastDormantRevocationCount;
    }
}
