using System.Text.Json;
using System.Text.Json.Serialization;
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
    private static readonly PaperSessionDailyReconciliationJsonContext PaperReconciliationWireJson = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
    });

    private static void MapPaperSessionReconciliationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/sessions/{sessionId}/reconciliation", async (string sessionId, DateTimeOffset? cutoff, HttpContext context) =>
        {
            if (!EndpointAuthorization.TryGetPermissions(context, out _))
                return Results.Unauthorized();

            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Problem("Paper reconciliation is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            if (cutoff is { } requested && (requested > now || requested == default))
                return Results.Problem("Specify a paper reconciliation cutoff at or before the current instant.", statusCode: StatusCodes.Status400BadRequest);

            try
            {
                var ct = context.RequestAborted;
                await persistence.InitialiseAsync(ct).ConfigureAwait(false);
                var accountId = persistence.GetSessionAccountId(sessionId);
                if (accountId is null)
                    return Results.NotFound();

                if (Guid.TryParseExact(accountId, "D", out var accountGuid) && accountGuid != Guid.Empty)
                {
                    var error = await RequireExecutionFundAccountAccessAsync(accountGuid, UserPermission.ViewTrades, context, jsonOptions)
                        .ConfigureAwait(false);
                    if (error is not null)
                        return error;
                }
                else if (!string.Equals(accountId, PaperTradingPortfolio.DefaultAccountId, StringComparison.Ordinal)
                    || !EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
                {
                    return EndpointHelpers.Forbidden();
                }

                var receiptStore = context.RequestServices.GetService<PaperSessionDailyReconciliationStore>();
                PaperSessionCutoffEvidence? retained;
                if (cutoff is { } requestedCutoff)
                {
                    var utcCutoff = requestedCutoff.ToUniversalTime();
                    retained = receiptStore is null
                        ? null
                        : await receiptStore.ReadAsync(sessionId, accountId, utcCutoff, ct).ConfigureAwait(false);
                }
                else
                {
                    if (receiptStore is null)
                        return Results.Problem("Retained paper reconciliation is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
                    retained = await receiptStore.ReadLatestAsync(sessionId, accountId, ct).ConfigureAwait(false);
                }
                var comparisonCutoff = retained?.Cutoff ?? cutoff?.ToUniversalTime();
                if (!comparisonCutoff.HasValue)
                    return Results.NotFound();
                var current = await persistence.GetCutoffEvidenceAsync(sessionId, comparisonCutoff.Value, ct).ConfigureAwait(false);
                var evidence = retained is null ? current : PaperSessionDailyReconciliationStore.AssessFreshness(retained, current);
                return evidence is null
                    ? Results.NotFound()
                    : Results.Json(evidence, PaperReconciliationWireJson.PaperSessionCutoffEvidence);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                return Results.Problem("Retained paper evidence could not verify this cutoff.", statusCode: StatusCodes.Status409Conflict);
            }
            catch (IOException)
            {
                return Results.Problem("Paper reconciliation evidence is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("GetPaperSessionReconciliation")
        .RequirePermission(UserPermission.ViewTrades)
        .Produces<PaperSessionCutoffEvidence>(200)
        .Produces(400).Produces(401).Produces(403).Produces(404).Produces(409).Produces(503);
    }
}
