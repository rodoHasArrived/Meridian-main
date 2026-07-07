using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Shared plumbing for Server-Sent Events endpoints (quotes, report-run, …): the single-writer
/// heartbeat loop that drains a subscription feed to the response, and the per-session identity used
/// for the concurrent-stream cap.
/// </summary>
internal static class StreamEndpointHelpers
{
    /// <summary>Idle heartbeat cadence — a liveness frame at least this often so clients can watchdog.</summary>
    public const int DefaultHeartbeatIntervalMs = 15_000;

    /// <summary>
    /// Drain a subscription's coalesced payload feed to the response as SSE, writing
    /// <c>event: {eventName}</c> frames and <c>event: heartbeat</c> frames while idle. Single writer:
    /// the heartbeat shares the one write path via a linked-CTS timeout (never a second concurrent
    /// writer), so <c>Response.Body</c> is never written from two paths at once, and there is no
    /// per-iteration <c>Task.WhenAny</c>/<c>Task.Delay</c>/<c>.AsTask()</c> allocation churn.
    /// </summary>
    public static async Task WriteEventStreamAsync<TPayload>(
        HttpContext context,
        ChannelReader<TPayload> reader,
        string eventName,
        JsonSerializerOptions jsonOptions,
        int heartbeatIntervalMs,
        CancellationToken ct)
        where TPayload : class
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";

        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var iteration = CancellationTokenSource.CreateLinkedTokenSource(ct);
                iteration.CancelAfter(heartbeatIntervalMs);

                try
                {
                    if (!await reader.WaitToReadAsync(iteration.Token).ConfigureAwait(false))
                    {
                        break; // broadcaster completed the channel — the stream is done.
                    }

                    // Coalesce anything queued to the newest payload before writing.
                    TPayload? latest = null;
                    while (reader.TryRead(out var item))
                    {
                        latest = item;
                    }

                    if (latest is not null)
                    {
                        var payload = JsonSerializer.Serialize(latest, jsonOptions);
                        await context.Response.WriteAsync($"event: {eventName}\ndata: {payload}\n\n", ct).ConfigureAwait(false);
                        await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Heartbeat interval elapsed with no payload — keep the connection warm and give
                    // clients an observable liveness frame.
                    await context.Response.WriteAsync("event: heartbeat\ndata: {}\n\n", ct).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected.
        }
    }

    /// <summary>
    /// Resolve the per-session identity used for the concurrent-stream cap. Falls back to an empty id
    /// (treated as unscoped / uncapped) when no logged-in operator is on the request.
    /// </summary>
    public static string ResolveStreamSessionId(HttpContext context)
    {
        return context.Items.TryGetValue(LoginSessionMiddleware.CurrentUserKey, out var user)
            && user is string userId
            && !string.IsNullOrWhiteSpace(userId)
                ? userId
                : string.Empty;
    }
}
