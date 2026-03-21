using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Extension methods for registering messaging and notification API endpoints.
/// Wired to the actual DailySummaryWebhook and ConnectionStatusWebhook services.
/// </summary>
public static class MessagingEndpoints
{
    // Track messaging activity in-process
    private static readonly List<MessagingActivityEntry> s_activityLog = new();
    private static readonly List<MessagingErrorEntry> s_errorLog = new();
    private static readonly object s_lock = new();
    private static int s_totalSent;
    private static int s_totalFailed;

    public static void MapMessagingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("").WithTags("Messaging");

        // Messaging config - reads actual webhook configuration
        group.MapGet(UiApiRoutes.MessagingConfig, ([FromServices] DailySummaryWebhook? webhook) =>
        {
            var webhookConfigured = webhook != null;
            var channels = new List<object>
            {
                new { name = "webhook", enabled = webhookConfigured, description = "HTTP webhook notifications (Slack, Discord, Teams, generic)" },
                new { name = "email", enabled = false, description = "Email notifications (SMTP) - not yet implemented" },
                new { name = "slack", enabled = webhookConfigured, description = "Slack integration via webhook" }
            };

            return Results.Json(new
            {
                enabled = webhookConfigured,
                channels,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetMessagingConfig")
        .Produces(200);

        // Messaging status - returns actual webhook delivery stats
        group.MapGet(UiApiRoutes.MessagingStatus, ([FromServices] DailySummaryWebhook? webhook) =>
        {
            lock (s_lock)
            {
                return Results.Json(new
                {
                    running = webhook != null,
                    queued = 0,
                    delivered = s_totalSent,
                    failed = s_totalFailed,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingStatus")
        .Produces(200);

        // Messaging stats
        group.MapGet(UiApiRoutes.MessagingStats, () =>
        {
            lock (s_lock)
            {
                var byChannel = new Dictionary<string, int>();
                foreach (var entry in s_activityLog)
                {
                    byChannel.TryGetValue(entry.Channel, out var count);
                    byChannel[entry.Channel] = count + 1;
                }

                return Results.Json(new
                {
                    totalSent = s_totalSent,
                    totalFailed = s_totalFailed,
                    totalQueued = 0,
                    averageDeliveryMs = s_activityLog.Count > 0
                        ? (int)s_activityLog.Average(a => a.DeliveryMs)
                        : 0,
                    byChannel,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingStats")
        .Produces(200);

        // Messaging activity - returns recent activity log
        group.MapGet(UiApiRoutes.MessagingActivity, (int? limit) =>
        {
            lock (s_lock)
            {
                var items = s_activityLog
                    .OrderByDescending(a => a.Timestamp)
                    .Take(limit ?? 50)
                    .Select(a => new
                    {
                        a.Id,
                        a.Channel,
                        a.Title,
                        a.Status,
                        a.DeliveryMs,
                        a.Timestamp
                    });

                return Results.Json(new
                {
                    activity = items,
                    total = s_activityLog.Count,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingActivity")
        .Produces(200);

        // Messaging consumers
        group.MapGet(UiApiRoutes.MessagingConsumers, ([FromServices] DailySummaryWebhook? webhook, [FromServices] ConnectionStatusWebhook? connWebhook) =>
        {
            var consumers = new List<object>();
            if (webhook != null)
                consumers.Add(new { name = "DailySummaryWebhook", type = "webhook", status = "active" });
            if (connWebhook != null)
                consumers.Add(new { name = "ConnectionStatusWebhook", type = "webhook", status = "active" });

            return Results.Json(new
            {
                consumers,
                total = consumers.Count,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetMessagingConsumers")
        .Produces(200);

        // Messaging endpoints list - shows configured webhook URLs
        group.MapGet(UiApiRoutes.MessagingEndpoints, ([FromServices] DailySummaryWebhook? webhook) =>
        {
            // We can't directly access the webhook config from the service,
            // but we can report whether a webhook service is registered
            var endpoints = new List<object>();
            if (webhook != null)
            {
                endpoints.Add(new
                {
                    name = "DailySummaryWebhook",
                    type = "webhook",
                    status = "configured",
                    description = "Sends end-of-day summaries and custom messages to configured webhook endpoints"
                });
            }

            return Results.Json(new
            {
                endpoints,
                total = endpoints.Count,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("GetMessagingEndpointsList")
        .Produces(200);

        // Test messaging - actually sends a test message via the webhook service
        group.MapPost(UiApiRoutes.MessagingTest, async (MessagingTestRequest? req, [FromServices] DailySummaryWebhook? webhook, CancellationToken ct) =>
        {
            if (webhook == null)
            {
                return Results.Json(new
                {
                    success = false,
                    channel = req?.Channel ?? "webhook",
                    message = "Messaging channels are not configured. Configure webhook settings in appsettings.json.",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            var testMessage = req?.Message ?? "This is a test message from Meridian.";
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var results = await webhook.SendMessageAsync(testMessage, "Test Notification", ct);
                sw.Stop();

                var allSuccess = results.All(r => r.Success);

                lock (s_lock)
                {
                    var entry = new MessagingActivityEntry
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Channel = req?.Channel ?? "webhook",
                        Title = "Test Notification",
                        Status = allSuccess ? "delivered" : "failed",
                        DeliveryMs = (int)sw.ElapsedMilliseconds,
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    s_activityLog.Add(entry);
                    if (allSuccess)
                        s_totalSent++;
                    else
                        s_totalFailed++;

                    // Keep activity log bounded
                    while (s_activityLog.Count > 500)
                        s_activityLog.RemoveAt(0);
                }

                return Results.Json(new
                {
                    success = allSuccess,
                    channel = req?.Channel ?? "webhook",
                    message = allSuccess
                        ? $"Test message sent successfully to {results.Count} webhook(s) in {sw.ElapsedMilliseconds}ms"
                        : $"Test message delivery failed for {results.Count(r => !r.Success)} of {results.Count} webhook(s)",
                    deliveryResults = results.Select(r => new
                    {
                        r.WebhookName,
                        webhookType = r.WebhookType.ToString(),
                        r.Success,
                        r.StatusCode,
                        r.ErrorMessage
                    }),
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                sw.Stop();
                lock (s_lock)
                {
                    s_totalFailed++;
                    s_errorLog.Add(new MessagingErrorEntry
                    {
                        Id = Guid.NewGuid().ToString("N")[..12],
                        Channel = req?.Channel ?? "webhook",
                        Error = ex.Message,
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }

                return Results.Json(new
                {
                    success = false,
                    channel = req?.Channel ?? "webhook",
                    message = $"Test message failed: {ex.Message}",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("TestMessaging")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Publishing stats
        group.MapGet(UiApiRoutes.MessagingPublishing, ([FromServices] DailySummaryWebhook? webhook) =>
        {
            lock (s_lock)
            {
                return Results.Json(new
                {
                    isPublishing = webhook != null,
                    messagesPublished = s_totalSent,
                    messagesFailed = s_totalFailed,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingPublishing")
        .Produces(200);

        // Purge queue - clears in-memory activity/error logs
        group.MapPost(UiApiRoutes.MessagingQueuePurge, (string queueName) =>
        {
            int removed;
            lock (s_lock)
            {
                removed = s_activityLog.Count + s_errorLog.Count;
                s_activityLog.Clear();
                s_errorLog.Clear();
                s_totalSent = 0;
                s_totalFailed = 0;
            }

            return Results.Json(new
            {
                purged = true,
                queueName,
                messagesRemoved = removed,
                timestamp = DateTimeOffset.UtcNow
            }, jsonOptions);
        })
        .WithName("PurgeMessagingQueue")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Messaging errors - returns actual error log
        group.MapGet(UiApiRoutes.MessagingErrors, (int? limit) =>
        {
            lock (s_lock)
            {
                var items = s_errorLog
                    .OrderByDescending(e => e.Timestamp)
                    .Take(limit ?? 50)
                    .Select(e => new { e.Id, e.Channel, e.Error, e.Timestamp });

                return Results.Json(new
                {
                    errors = items,
                    total = s_errorLog.Count,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingErrors")
        .Produces(200);

        // Retry failed message
        group.MapPost(UiApiRoutes.MessagingErrorRetry, async (string messageId, [FromServices] DailySummaryWebhook? webhook, CancellationToken ct) =>
        {
            if (webhook == null)
            {
                return Results.Json(new
                {
                    messageId,
                    retried = false,
                    message = "Webhook service not configured",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            MessagingErrorEntry? errorEntry;
            lock (s_lock)
            {
                errorEntry = s_errorLog.FirstOrDefault(e => e.Id == messageId);
            }

            if (errorEntry == null)
            {
                return Results.Json(new
                {
                    messageId,
                    retried = false,
                    message = "Message not found in error queue",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }

            try
            {
                var results = await webhook.SendMessageAsync($"Retried message (original error: {errorEntry.Error})", "Retry Notification", ct);
                var success = results.All(r => r.Success);

                lock (s_lock)
                {
                    if (success)
                    {
                        s_errorLog.Remove(errorEntry);
                        s_totalSent++;
                    }
                }

                return Results.Json(new
                {
                    messageId,
                    retried = success,
                    message = success ? "Message retried successfully" : "Retry failed",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
            catch (Exception ex)
            {
                return Results.Json(new
                {
                    messageId,
                    retried = false,
                    message = $"Retry failed: {ex.Message}",
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("RetryMessagingError")
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    private sealed record MessagingTestRequest(string? Channel, string? Target, string? Message);

    private sealed class MessagingActivityEntry
    {
        public string Id { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int DeliveryMs { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    private sealed class MessagingErrorEntry
    {
        public string Id { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
        public DateTimeOffset Timestamp { get; init; }
    }
}
