using System.Text.Json;
using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Workstation Server-Sent Events stream. Step 1 of the live data spine: a
/// poll-bridge that pushes the same read-model payloads the REST endpoints serve,
/// coalescing unchanged snapshots so idle streams cost heartbeats only. True
/// event-driven fan-out from the pipeline is a separate, hot-path-reviewed step.
/// </summary>
public static partial class WorkstationEndpoints
{
    private const int StreamPollIntervalMs = 2_000;
    private const int StreamHeartbeatIntervalMs = 15_000;
    private const int StreamMaxSymbols = 50;

    private static void MapStreamEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        // GET /api/workstation/stream?symbols=AAPL,MSFT — SSE channel emitting
        // `event: quotes` frames with the quotes-snapshot payload for the requested
        // symbols whenever it changes, plus `: heartbeat` comments while idle.
        group.MapGet(WorkstationSubroute(UiApiRoutes.WorkstationStream), async (HttpContext context, string? symbols, CancellationToken ct) =>
        {
            var normalizedSymbols = NormalizeStreamSymbols(symbols);
            if (normalizedSymbols is null)
            {
                return Results.Json(
                    new { error = $"A maximum of {StreamMaxSymbols} symbols can be streamed per connection." },
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (LiveDataEndpoints.TryBuildQuotesSnapshotResponse(context.RequestServices, normalizedSymbols) is null)
            {
                return Results.Json(
                    new { error = "Quote collector not available" },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            string? lastQuotesSignature = null;
            var lastWriteUtc = DateTimeOffset.MinValue;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var snapshot = LiveDataEndpoints.TryBuildQuotesSnapshotResponse(context.RequestServices, normalizedSymbols);
                    var wrote = false;
                    if (snapshot is not null)
                    {
                        // Coalesce on the quote rows only: the envelope timestamp moves
                        // every tick even when no quote changed.
                        var signature = JsonSerializer.Serialize(snapshot.Quotes, jsonOptions);
                        if (!string.Equals(signature, lastQuotesSignature, StringComparison.Ordinal))
                        {
                            lastQuotesSignature = signature;
                            var payload = JsonSerializer.Serialize(snapshot, jsonOptions);
                            await context.Response.WriteAsync($"event: quotes\ndata: {payload}\n\n", ct).ConfigureAwait(false);
                            wrote = true;
                        }
                    }

                    var nowUtc = DateTimeOffset.UtcNow;
                    if (!wrote && (nowUtc - lastWriteUtc).TotalMilliseconds >= StreamHeartbeatIntervalMs)
                    {
                        await context.Response.WriteAsync(": heartbeat\n\n", ct).ConfigureAwait(false);
                        wrote = true;
                    }

                    if (wrote)
                    {
                        lastWriteUtc = nowUtc;
                        await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                    }

                    await Task.Delay(StreamPollIntervalMs, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client disconnected.
            }

            return Results.Empty;
        })
        .WithName("GetWorkstationStream")
        .Produces(200)
        .Produces(400)
        .Produces(503);
    }

    /// <summary>
    /// Trims and caps the symbol filter; returns null when the cap is exceeded and
    /// an empty string when no filter was requested (stream all tracked symbols).
    /// </summary>
    private static string? NormalizeStreamSymbols(string? symbols)
    {
        if (string.IsNullOrWhiteSpace(symbols))
        {
            return string.Empty;
        }

        var parts = symbols.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > StreamMaxSymbols)
        {
            return null;
        }

        return string.Join(',', parts);
    }
}
