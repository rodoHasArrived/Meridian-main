using Meridian.Identity.Auth;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.DataIntegration.Historical;
using Meridian.Storage;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Mvc;
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

        group.MapPost(UiApiRoutes.AlignmentCreate, (
            TimeSeriesAlignmentRequest request, HttpContext context, [FromServices] StorageOptions? storageOptions)
                => RunAlignmentAsync(request, storageOptions, jsonOptions, preview: false, context.RequestAborted))
            .WithName("CreateAlignment").RequirePermission(UserPermission.ViewHistoricalData)
            .DeclareNonMutating("Computes aligned rows from the archive and returns them to the caller without a host-side write.")
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024))
            .Produces<TimeSeriesAlignmentExecution>()
            .ProducesProblem(400)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(429)
            .ProducesProblem(503)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost(UiApiRoutes.AlignmentPreview, (
            TimeSeriesAlignmentRequest request, HttpContext context, [FromServices] StorageOptions? storageOptions)
                => RunAlignmentAsync(request, storageOptions, jsonOptions, preview: true, context.RequestAborted))
            .WithName("PreviewAlignment").RequirePermission(UserPermission.ViewHistoricalData)
            .DeclareNonMutating("Computes bounded alignment evidence and sample rows without writing to the host.")
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024))
            .Produces<TimeSeriesAlignmentPreview>()
            .ProducesProblem(400)
            .ProducesProblem(422)
            .ProducesProblem(429)
            .ProducesProblem(503)
            .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private static readonly SemaphoreSlim AlignmentSlots = new(2, 2);

    private static async Task<IResult> RunAlignmentAsync(
        TimeSeriesAlignmentRequest request, StorageOptions? storageOptions, JsonSerializerOptions jsonOptions,
        bool preview, CancellationToken ct)
    {
        if (storageOptions is null)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Historical storage is unavailable.");
        if (!await AlignmentSlots.WaitAsync(0, ct).ConfigureAwait(false))
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: "Alignment capacity is busy.");

        try
        {
            var execution = await new TimeSeriesAlignmentEngine(storageOptions).AlignAsync(request, ct).ConfigureAwait(false);
            if (preview)
                return Results.Json(new TimeSeriesAlignmentPreview(execution.Summary, execution.Rows.Take(20).ToArray()), jsonOptions);

            if (request.ExpectedContentHash is not null &&
                !string.Equals(request.ExpectedContentHash, execution.Summary.ContentHash, StringComparison.OrdinalIgnoreCase))
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Alignment inputs have changed.",
                    detail: "Run the preview again before exporting this transformation.");
            return Results.Json(execution, jsonOptions);
        }
        catch (ArgumentException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid alignment options.", detail: ex.Message);
        }
        catch (InvalidDataException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Alignment could not use the archive.", detail: ex.Message);
        }
        catch (OverflowException)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Stored values exceed the alignment numeric range.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeZoneNotFoundException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "The selected archive is temporarily unavailable.",
                detail: "Close active captures or narrow the selection, then retry.");
        }
        finally
        {
            AlignmentSlots.Release();
        }
    }
}
