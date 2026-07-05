using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Ui.Shared.Streaming;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Workstation Server-Sent Events stream. Event-driven fan-out: connections subscribe to a topic on
/// the <see cref="IQuoteStreamBroadcaster"/>, which rebuilds and coalesces each topic's snapshot once
/// per change and pushes it to every subscriber. One snapshot build is shared across all subscribers
/// of a topic, and the market-data ingestion hot path is untouched.
/// </summary>
public static partial class WorkstationEndpoints
{
    private const int StreamHeartbeatIntervalMs = 15_000;
    private const int StreamMaxSymbols = 50;

    private static void MapStreamEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        // GET /api/workstation/stream?symbols=AAPL,MSFT — SSE channel emitting `event: quotes`
        // frames with the quotes-snapshot payload for the requested symbols whenever it changes, plus
        // `: heartbeat` comments while idle.
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

            var broadcaster = context.RequestServices.GetService<IQuoteStreamBroadcaster>();
            if (broadcaster is null)
            {
                return Results.Json(
                    new { error = "Quote stream not available" },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var topic = StreamTopic.Quotes(normalizedSymbols);
            var subscription = broadcaster.TrySubscribe(topic, ResolveStreamSessionId(context));
            if (subscription is null)
            {
                context.Response.Headers["Retry-After"] = "5";
                return Results.Json(
                    new { error = "Too many concurrent streams for this session." },
                    jsonOptions,
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            try
            {
                await using (subscription)
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // Single writer: race the next snapshot against the heartbeat interval so a
                        // heartbeat never writes to Response.Body concurrently with a snapshot.
                        using var iteration = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        var readTask = subscription.Reader.WaitToReadAsync(iteration.Token).AsTask();
                        var delayTask = Task.Delay(StreamHeartbeatIntervalMs, iteration.Token);
                        var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
                        iteration.Cancel();

                        if (completed == readTask)
                        {
                            if (!await readTask.ConfigureAwait(false))
                            {
                                break; // broadcaster completed the channel — the stream is done.
                            }

                            // Coalesce anything queued to the newest snapshot before writing.
                            QuotesSnapshotResponse? latest = null;
                            while (subscription.Reader.TryRead(out var snapshot))
                            {
                                latest = snapshot;
                            }

                            if (latest is not null)
                            {
                                var payload = JsonSerializer.Serialize(latest, jsonOptions);
                                await context.Response.WriteAsync($"event: quotes\ndata: {payload}\n\n", ct).ConfigureAwait(false);
                                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            await context.Response.WriteAsync(": heartbeat\n\n", ct).ConfigureAwait(false);
                            await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                        }
                    }
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
        .Produces(429)
        .Produces(503);
    }

    /// <summary>
    /// Resolve the per-session identity used for the concurrent-stream cap. Falls back to an empty id
    /// (treated as unscoped / uncapped) when no logged-in operator is on the request.
    /// </summary>
    private static string ResolveStreamSessionId(HttpContext context)
    {
        return context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserKey, out var user)
            && user is string userId
            && !string.IsNullOrWhiteSpace(userId)
                ? userId
                : string.Empty;
    }

    /// <summary>
    /// Trims and caps the symbol filter; returns null when the cap is exceeded and an empty string
    /// when no filter was requested (stream all tracked symbols).
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
