using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.DataIntegration.Monitoring;
using Meridian.Identity.Auth;
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
    public static void MapMessagingEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        // Activity belongs to this host, including when multiple TestServers share a process.
        var activityLog = new List<MessagingActivityEntry>();
        var errorLog = new List<MessagingErrorEntry>();
        var activityLock = new object();
        var totalSent = 0;
        var totalFailed = 0;
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
        .WithName("GetMessagingConfig").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
        .Produces(200);

        // Messaging status - returns actual webhook delivery stats
        group.MapGet(UiApiRoutes.MessagingStatus, ([FromServices] DailySummaryWebhook? webhook) =>
        {
            lock (activityLock)
            {
                return Results.Json(new
                {
                    running = webhook != null,
                    queued = 0,
                    delivered = totalSent,
                    failed = totalFailed,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingStatus").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
        .Produces(200);

        // Messaging stats
        group.MapGet(UiApiRoutes.MessagingStats, () =>
        {
            lock (activityLock)
            {
                var byChannel = new Dictionary<string, int>();
                foreach (var entry in activityLog)
                {
                    byChannel.TryGetValue(entry.Channel, out var count);
                    byChannel[entry.Channel] = count + 1;
                }

                return Results.Json(new
                {
                    totalSent = totalSent,
                    totalFailed = totalFailed,
                    totalQueued = 0,
                    averageDeliveryMs = activityLog.Count > 0
                        ? (int)activityLog.Average(a => a.DeliveryMs)
                        : 0,
                    byChannel,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingStats").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
        .Produces(200);

        // Messaging activity - returns recent activity log
        group.MapGet(UiApiRoutes.MessagingActivity, (int? limit) =>
        {
            lock (activityLock)
            {
                var items = activityLog
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
                    total = activityLog.Count,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingActivity").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
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
        .WithName("GetMessagingConsumers").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
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
        .WithName("GetMessagingEndpointsList").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
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

                lock (activityLock)
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
                    activityLog.Add(entry);
                    if (allSuccess)
                        totalSent++;
                    else
                        totalFailed++;

                    // Keep activity log bounded
                    while (activityLog.Count > 500)
                        activityLog.RemoveAt(0);
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller hung up. Propagate instead of recording a delivery failure: the
                // counters and error log below are operator-visible, so counting an aborted
                // request as a failed send would put a webhook delivery that never happened
                // into the messaging error queue.
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                lock (activityLock)
                {
                    totalFailed++;
                    errorLog.Add(new MessagingErrorEntry
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
        .RequirePermission(UserPermission.AdminMaintenance)
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Publishing stats
        group.MapGet(UiApiRoutes.MessagingPublishing, ([FromServices] DailySummaryWebhook? webhook) =>
        {
            lock (activityLock)
            {
                return Results.Json(new
                {
                    isPublishing = webhook != null,
                    messagesPublished = totalSent,
                    messagesFailed = totalFailed,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingPublishing").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
        .Produces(200);

        // Purge queue - clears in-memory activity/error logs
        group.MapPost(UiApiRoutes.MessagingQueuePurge, (string queueName) =>
        {
            int removed;
            lock (activityLock)
            {
                removed = activityLog.Count + errorLog.Count;
                activityLog.Clear();
                errorLog.Clear();
                totalSent = 0;
                totalFailed = 0;
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
        .RequirePermission(UserPermission.AdminMaintenance)
        .Produces(200)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // Messaging errors - returns actual error log
        group.MapGet(UiApiRoutes.MessagingErrors, (int? limit) =>
        {
            lock (activityLock)
            {
                var items = errorLog
                    .OrderByDescending(e => e.Timestamp)
                    .Take(limit ?? 50)
                    .Select(e => new { e.Id, e.Channel, e.Error, e.Timestamp });

                return Results.Json(new
                {
                    errors = items,
                    total = errorLog.Count,
                    timestamp = DateTimeOffset.UtcNow
                }, jsonOptions);
            }
        })
        .WithName("GetMessagingErrors").RequireAnyPermission(UserPermission.ViewDiagnostics, UserPermission.AdminMaintenance)
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
            lock (activityLock)
            {
                errorEntry = errorLog.FirstOrDefault(e => e.Id == messageId);
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

                lock (activityLock)
                {
                    if (success)
                    {
                        errorLog.Remove(errorEntry);
                        totalSent++;
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The caller hung up mid-retry. Propagate rather than answering 200 with
                // retried = false, which reads as "the retry ran and failed".
                throw;
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
        .RequirePermission(UserPermission.AdminMaintenance)
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
