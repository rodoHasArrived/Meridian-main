using System.Text.Json;
using Meridian.Application.Scheduling;
using Meridian.Contracts.Api;
using Meridian.Core.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering cron expression validation API endpoints.
/// </summary>
public static class CronEndpoints
{
    public static void MapCronEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Scheduling");

        // Validate cron expression
        group.MapPost(UiApiRoutes.SchedulesCronValidate, (CronValidateRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Expression))
                return Results.BadRequest(new { error = "Cron expression is required" });

            var isValid = CronExpressionParser.IsValid(req.Expression);
            var description = isValid ? CronExpressionParser.GetDescription(req.Expression) : null;

            return Results.Json(new
            {
                expression = req.Expression,
                valid = isValid,
                description,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("ValidateCronExpression")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Get next run times
        group.MapPost(UiApiRoutes.SchedulesCronNextRuns, (CronNextRunsRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Expression))
                return Results.BadRequest(new { error = "Cron expression is required" });

            if (!CronExpressionParser.IsValid(req.Expression))
                return Results.BadRequest(new { error = "Invalid cron expression" });

            var count = Math.Min(req.Count ?? 5, 50);
            var timeZone = TimeZoneInfo.Utc;

            if (!string.IsNullOrWhiteSpace(req.TimeZoneId))
            {
                try
                { timeZone = TimeZoneInfo.FindSystemTimeZoneById(req.TimeZoneId); }
                catch { /* fall back to UTC */ }
            }

            var nextRuns = new List<DateTimeOffset>();
            var from = DateTimeOffset.UtcNow;
            for (int i = 0; i < count; i++)
            {
                var next = CronExpressionParser.GetNextOccurrence(req.Expression, timeZone, from);
                if (next is null)
                    break;
                nextRuns.Add(next.Value);
                from = next.Value.AddMinutes(1);
            }

            return Results.Json(new
            {
                expression = req.Expression,
                description = CronExpressionParser.GetDescription(req.Expression),
                timeZone = timeZone.Id,
                nextRuns,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetCronNextRuns")
        .Produces(200)
        .Produces(400)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record CronValidateRequest(string? Expression);
    private sealed record CronNextRunsRequest(string? Expression, int? Count, string? TimeZoneId);
}
