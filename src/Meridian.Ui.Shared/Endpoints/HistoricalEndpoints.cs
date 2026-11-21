using Meridian.Identity.Auth;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.DataIntegration.Historical;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering historical data query API endpoints.
/// Shared between web dashboard and desktop application hosts.
/// </summary>
public static class HistoricalEndpoints
{
    /// <summary>
    /// Maps all historical data query API endpoints.
    /// </summary>
    public static void MapHistoricalEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup(UiApiRoutes.HistoricalData).WithTags("Historical");

        // Query historical data
        group.MapGet("", async (
            HttpContext context,
            HistoricalDataQueryService queryService,
            string symbol,
            DateOnly? from = null,
            DateOnly? to = null,
            string? dataType = null,
            int? skip = null,
            int? limit = null) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { error = "Symbol is required" });
            }

            var query = new HistoricalDataQuery(
                symbol.ToUpperInvariant(),
                from,
                to,
                dataType,
                skip,
                limit);

            try
            {
                var result = await queryService.QueryAsync(query, context.RequestAborted);
                return Results.Json(result, jsonOptions);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499); // Client Closed Request
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("QueryHistoricalData").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .Produces(400)
        .Produces(499);

        // Get available symbols
        group.MapGet("/symbols", (HistoricalDataQueryService queryService) =>
        {
            try
            {
                var symbols = queryService.GetAvailableSymbols();
                return Results.Json(new { symbols, count = symbols.Count }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetAvailableSymbols").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .Produces(400);

        // Get date range for a symbol
        group.MapGet("/{symbol}/daterange", (HistoricalDataQueryService queryService, string symbol) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { error = "Symbol is required" });
            }

            try
            {
                var dateRange = queryService.GetDateRange(symbol.ToUpperInvariant());
                return Results.Json(dateRange, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetSymbolDateRange").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .Produces(400);

        // Aggregate stored trades into OHLCV bars at a chosen interval. Powers price charts.
        group.MapGet("/{symbol}/bars", async (
            HttpContext context,
            HistoricalDataQueryService queryService,
            string symbol,
            int? intervalMinutes = null,
            DateOnly? from = null,
            DateOnly? to = null,
            int? maxBars = null) =>
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return Results.BadRequest(new { error = "Symbol is required" });
            }

            if (!from.HasValue || !to.HasValue)
            {
                return Results.BadRequest(new { error = "from and to date bounds are required" });
            }

            if (to.Value < from.Value)
            {
                return Results.BadRequest(new { error = "to must be on or after from" });
            }

            var interval = intervalMinutes ?? 5;
            if (interval <= 0 || interval > 1440)
            {
                return Results.BadRequest(new { error = "intervalMinutes must be between 1 and 1440" });
            }

            var capped = maxBars is null ? 5000 : Math.Clamp(maxBars.Value, 1, 5000);

            var query = new HistoricalBarsQuery(
                Symbol: symbol.ToUpperInvariant(),
                IntervalMinutes: interval,
                From: from,
                To: to,
                MaxBars: capped);

            try
            {
                var result = await queryService.GetBarsAsync(query, context.RequestAborted);
                return Results.Json(result, jsonOptions);
            }
            catch (OperationCanceledException)
            {
                return Results.StatusCode(499);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("GetSymbolBars").RequirePermission(UserPermission.ViewHistoricalData)
        .Produces(200)
        .Produces(400)
        .Produces(499);
    }

    /// <summary>
    /// Maps the /api/alignment endpoints for time series alignment operations.
    /// </summary>
    public static void MapAlignmentEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Alignment");

        // No alignment execution service is wired into this host. Keep the routes discoverable,
        // but never acknowledge a job or estimate output that has not actually been computed.
        group.MapPost(UiApiRoutes.AlignmentCreate, () => AlignmentUnavailable())
        .WithName("CreateAlignment").RequirePermission(UserPermission.ViewHistoricalData)
        .DeclareNonMutating("Reports that alignment execution is unavailable; no job is created or retained.")
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.AlignmentPreview, () => AlignmentUnavailable())
        .WithName("PreviewAlignment").RequirePermission(UserPermission.ViewHistoricalData)
        .DeclareNonMutating("Reports that alignment preview is unavailable; no preview is computed or retained.")
        .Produces(StatusCodes.Status501NotImplemented)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static IResult AlignmentUnavailable() => Results.Problem(
        statusCode: StatusCodes.Status501NotImplemented,
        title: "Time series alignment is not implemented",
        detail: "This host has no alignment execution or preview service. No job was created and no output estimate was computed.");
}
