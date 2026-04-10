using System.Text.Json;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
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
        .WithName("QueryHistoricalData")
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
        .WithName("GetAvailableSymbols")
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
        .WithName("GetSymbolDateRange")
        .Produces(200)
        .Produces(400);
    }

    /// <summary>
    /// Maps the /api/alignment endpoints for time series alignment operations.
    /// </summary>
    public static void MapAlignmentEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Alignment");

        // Create alignment
        group.MapPost(UiApiRoutes.AlignmentCreate, (AlignmentCreateRequest req) =>
        {
            var jobId = Guid.NewGuid().ToString("N")[..12];

            return Results.Json(new
            {
                jobId,
                symbols = req.Symbols ?? Array.Empty<string>(),
                interval = req.Interval ?? "1min",
                aggregationMethod = req.AggregationMethod ?? "last",
                gapStrategy = req.GapStrategy ?? "forward_fill",
                status = "queued",
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("CreateAlignment")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Preview alignment
        group.MapPost(UiApiRoutes.AlignmentPreview, (AlignmentCreateRequest req) =>
        {
            var symbols = req.Symbols ?? Array.Empty<string>();
            var interval = req.Interval ?? "1min";

            return Results.Json(new
            {
                symbols,
                interval,
                estimatedOutputRows = symbols.Length * 390, // ~390 minutes in trading day
                supportedIntervals = new[] { "1sec", "5sec", "30sec", "1min", "5min", "15min", "30min", "1hour", "1day" },
                supportedAggregations = new[] { "last", "first", "mean", "median", "vwap", "ohlc" },
                supportedGapStrategies = new[] { "forward_fill", "backward_fill", "interpolate", "null", "skip" },
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("PreviewAlignment")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record AlignmentCreateRequest(string[]? Symbols, string? Interval, string? AggregationMethod, string? GapStrategy, DateTime? StartDate, DateTime? EndDate);
}
