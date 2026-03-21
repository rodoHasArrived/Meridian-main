using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Monitoring;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Application.Pipeline;
using Meridian.Infrastructure.Http;
using Serilog;

namespace Meridian.Application.Services;

// Type alias: StaleDataAlert is a DataAnomaly with Type = StaleData
using StaleDataAlert = Meridian.Application.Monitoring.DataQuality.DataAnomaly;

/// <summary>
/// Sends end-of-day summary digests via webhook to external services.
/// Supports Slack, Discord, Microsoft Teams, and generic JSON webhooks.
/// </summary>
public sealed class DailySummaryWebhook : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<DailySummaryWebhook>();
    private readonly DailySummaryWebhookConfig _config;
    private readonly HttpClient _httpClient;
    private readonly Timer? _scheduledTimer;
    private readonly TradingCalendar _tradingCalendar;
    private readonly CancellationTokenSource _cts = new();

    // Statistics collectors
    private Func<PipelineStatistics>? _getPipelineStats;
    private Func<IReadOnlyList<StaleDataAlert>>? _getStaleSymbols;
    private Func<SystemHealthSnapshot>? _getHealthSnapshot;

    /// <summary>
    /// Event raised when a summary is sent successfully.
    /// </summary>
    public event Action<DailySummaryResult>? OnSummarySent;

    /// <summary>
    /// Event raised when a summary fails to send.
    /// </summary>
    public event Action<DailySummaryResult>? OnSummaryFailed;

    public DailySummaryWebhook(DailySummaryWebhookConfig config, TradingCalendar? tradingCalendar = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _tradingCalendar = tradingCalendar ?? new TradingCalendar();

        // TD-10: Use HttpClientFactory instead of creating new HttpClient instances
        _httpClient = HttpClientFactoryProvider.CreateClient(HttpClientNames.DailySummaryWebhook);
        _httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);

        if (config.EnableScheduledSummary && !string.IsNullOrEmpty(config.ScheduledTime))
        {
            _scheduledTimer = new Timer(ScheduledCallback, null, GetNextScheduledTime(), TimeSpan.FromDays(1));
            _log.Information("DailySummaryWebhook scheduled for {ScheduledTime} ET", config.ScheduledTime);
        }

        _log.Information("DailySummaryWebhook initialized with {WebhookCount} webhook(s)", config.Webhooks?.Length ?? 0);
    }

    /// <summary>
    /// Registers statistics providers for inclusion in the summary.
    /// </summary>
    public void RegisterStatisticsProviders(
        Func<PipelineStatistics>? pipelineStats = null,
        Func<IReadOnlyList<StaleDataAlert>>? staleSymbols = null,
        Func<SystemHealthSnapshot>? healthSnapshot = null)
    {
        _getPipelineStats = pipelineStats;
        _getStaleSymbols = staleSymbols;
        _getHealthSnapshot = healthSnapshot;
    }

    /// <summary>
    /// Sends a daily summary immediately to all configured webhooks.
    /// </summary>
    public async Task<DailySummaryResult> SendSummaryAsync(CancellationToken ct = default)
    {
        var summary = BuildSummary();
        var results = new List<WebhookDeliveryResult>();
        var startTime = DateTimeOffset.UtcNow;

        if (_config.Webhooks is null || _config.Webhooks.Length == 0)
        {
            _log.Warning("No webhooks configured for daily summary");
            return new DailySummaryResult(
                Success: false,
                Summary: summary,
                DeliveryResults: Array.Empty<WebhookDeliveryResult>(),
                SentAt: startTime,
                ErrorMessage: "No webhooks configured"
            );
        }

        foreach (var webhook in _config.Webhooks)
        {
            if (!webhook.Enabled)
            {
                _log.Debug("Skipping disabled webhook: {WebhookName}", webhook.Name);
                continue;
            }

            var result = await SendToWebhookAsync(webhook, summary, ct);
            results.Add(result);
        }

        var allSuccess = results.All(r => r.Success);
        var summaryResult = new DailySummaryResult(
            Success: allSuccess,
            Summary: summary,
            DeliveryResults: results.ToArray(),
            SentAt: startTime,
            ErrorMessage: allSuccess ? null : "One or more webhooks failed"
        );

        if (allSuccess)
        {
            _log.Information("Daily summary sent successfully to {WebhookCount} webhook(s)", results.Count);
            OnSummarySent?.Invoke(summaryResult);
        }
        else
        {
            _log.Warning("Daily summary delivery had failures: {FailedCount}/{TotalCount} failed",
                results.Count(r => !r.Success), results.Count);
            OnSummaryFailed?.Invoke(summaryResult);
        }

        return summaryResult;
    }

    /// <summary>
    /// Sends a custom message to all configured webhooks.
    /// </summary>
    public async Task<IReadOnlyList<WebhookDeliveryResult>> SendMessageAsync(string message, string? title = null, CancellationToken ct = default)
    {
        var results = new List<WebhookDeliveryResult>();

        if (_config.Webhooks is null || _config.Webhooks.Length == 0)
            return results;

        foreach (var webhook in _config.Webhooks.Where(w => w.Enabled))
        {
            var payload = FormatCustomMessage(webhook.Type, message, title);
            var result = await DeliverPayloadAsync(webhook, payload, ct);
            results.Add(result);
        }

        return results;
    }

    private DailySummary BuildSummary()
    {
        var pipelineStats = _getPipelineStats?.Invoke();
        var staleSymbols = _getStaleSymbols?.Invoke() ?? Array.Empty<StaleDataAlert>();
        var healthSnapshot = _getHealthSnapshot?.Invoke();
        var marketStatus = _tradingCalendar.GetCurrentStatus();

        var summary = new DailySummary
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            MarketStatus = marketStatus.State.ToString(),
            IsHalfDay = marketStatus.IsHalfDay,

            // Pipeline statistics
            TotalEventsPublished = pipelineStats?.PublishedCount ?? 0,
            TotalEventsConsumed = pipelineStats?.ConsumedCount ?? 0,
            TotalEventsDropped = pipelineStats?.DroppedCount ?? 0,
            PeakQueueSize = pipelineStats?.PeakQueueSize ?? 0,
            AverageProcessingTimeUs = pipelineStats?.AverageProcessingTimeUs ?? 0,

            // Data quality
            StaleSymbolCount = staleSymbols.Count,
            StaleSymbols = staleSymbols.Select(s => s.Symbol).Take(10).ToArray(),

            // System health
            SystemHealthStatus = healthSnapshot?.OverallStatus.ToString() ?? "Unknown",
            WarningCount = healthSnapshot?.Warnings.Count ?? 0,
            Warnings = healthSnapshot?.Warnings.Select(w => w.Message).Take(5).ToArray() ?? Array.Empty<string>(),

            // Memory stats
            MemoryUsageMb = healthSnapshot?.MemoryInfo.WorkingSetMb ?? 0,
            GcCollections = new GcStats
            {
                Gen0 = healthSnapshot?.MemoryInfo.Gen0Collections ?? 0,
                Gen1 = healthSnapshot?.MemoryInfo.Gen1Collections ?? 0,
                Gen2 = healthSnapshot?.MemoryInfo.Gen2Collections ?? 0
            }
        };

        return summary;
    }

    private async Task<WebhookDeliveryResult> SendToWebhookAsync(WebhookConfig webhook, DailySummary summary, CancellationToken ct)
    {
        try
        {
            var payload = FormatPayload(webhook.Type, summary);
            return await DeliverPayloadAsync(webhook, payload, ct);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to send summary to webhook {WebhookName}", webhook.Name);
            return new WebhookDeliveryResult(
                WebhookName: webhook.Name,
                WebhookType: webhook.Type,
                Success: false,
                StatusCode: null,
                ErrorMessage: ex.Message,
                SentAt: DateTimeOffset.UtcNow
            );
        }
    }

    private async Task<WebhookDeliveryResult> DeliverPayloadAsync(WebhookConfig webhook, string payload, CancellationToken ct)
    {
        var sentAt = DateTimeOffset.UtcNow;

        for (var attempt = 1; attempt <= _config.RetryCount + 1; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
                {
                    Content = new StringContent(payload, Encoding.UTF8, GetContentType(webhook.Type))
                };

                // Add custom headers if specified
                if (webhook.Headers is not null)
                {
                    foreach (var header in webhook.Headers)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                using var response = await _httpClient.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    _log.Debug("Successfully delivered to webhook {WebhookName} (attempt {Attempt})", webhook.Name, attempt);
                    return new WebhookDeliveryResult(
                        WebhookName: webhook.Name,
                        WebhookType: webhook.Type,
                        Success: true,
                        StatusCode: (int)response.StatusCode,
                        ErrorMessage: null,
                        SentAt: sentAt
                    );
                }

                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _log.Warning("Webhook {WebhookName} returned {StatusCode}: {Error} (attempt {Attempt})",
                    webhook.Name, (int)response.StatusCode, errorBody, attempt);

                if (attempt <= _config.RetryCount)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
                }
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt <= _config.RetryCount)
            {
                _log.Warning(ex, "Webhook delivery attempt {Attempt} failed for {WebhookName}, retrying...",
                    attempt, webhook.Name);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        return new WebhookDeliveryResult(
            WebhookName: webhook.Name,
            WebhookType: webhook.Type,
            Success: false,
            StatusCode: null,
            ErrorMessage: "All retry attempts failed",
            SentAt: sentAt
        );
    }

    private static string FormatPayload(WebhookType type, DailySummary summary)
    {
        return type switch
        {
            WebhookType.Slack => FormatSlackPayload(summary),
            WebhookType.Discord => FormatDiscordPayload(summary),
            WebhookType.Teams => FormatTeamsPayload(summary),
            WebhookType.Generic => FormatGenericPayload(summary),
            _ => FormatGenericPayload(summary)
        };
    }

    private static string FormatSlackPayload(DailySummary summary)
    {
        var statusEmoji = summary.SystemHealthStatus switch
        {
            "Healthy" => ":white_check_mark:",
            "Warning" => ":warning:",
            "Critical" => ":x:",
            _ => ":question:"
        };

        var payload = new
        {
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new { type = "plain_text", text = $"Meridian - Daily Summary {summary.Date:yyyy-MM-dd}" }
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Market Status:*\n{summary.MarketStatus}{(summary.IsHalfDay ? " (Half Day)" : "")}" },
                        new { type = "mrkdwn", text = $"*System Health:*\n{statusEmoji} {summary.SystemHealthStatus}" }
                    }
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Events Published:*\n{summary.TotalEventsPublished:N0}" },
                        new { type = "mrkdwn", text = $"*Events Dropped:*\n{summary.TotalEventsDropped:N0}" }
                    }
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Peak Queue Size:*\n{summary.PeakQueueSize:N0}" },
                        new { type = "mrkdwn", text = $"*Avg Processing:*\n{summary.AverageProcessingTimeUs:F2} µs" }
                    }
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Stale Symbols:*\n{summary.StaleSymbolCount}" },
                        new { type = "mrkdwn", text = $"*Memory:*\n{summary.MemoryUsageMb:F0} MB" }
                    }
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new { type = "mrkdwn", text = $"Generated at {summary.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC" }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string FormatDiscordPayload(DailySummary summary)
    {
        var color = summary.SystemHealthStatus switch
        {
            "Healthy" => 0x00FF00, // Green
            "Warning" => 0xFFFF00, // Yellow
            "Critical" => 0xFF0000, // Red
            _ => 0x808080 // Gray
        };

        var payload = new
        {
            embeds = new[]
            {
                new
                {
                    title = $"Meridian - Daily Summary",
                    description = $"Summary for {summary.Date:yyyy-MM-dd}",
                    color,
                    fields = new object[]
                    {
                        new { name = "Market Status", value = $"{summary.MarketStatus}{(summary.IsHalfDay ? " (Half Day)" : "")}", inline = true },
                        new { name = "System Health", value = summary.SystemHealthStatus, inline = true },
                        new { name = "Events Published", value = $"{summary.TotalEventsPublished:N0}", inline = true },
                        new { name = "Events Dropped", value = $"{summary.TotalEventsDropped:N0}", inline = true },
                        new { name = "Peak Queue Size", value = $"{summary.PeakQueueSize:N0}", inline = true },
                        new { name = "Avg Processing", value = $"{summary.AverageProcessingTimeUs:F2} µs", inline = true },
                        new { name = "Stale Symbols", value = $"{summary.StaleSymbolCount}", inline = true },
                        new { name = "Memory Usage", value = $"{summary.MemoryUsageMb:F0} MB", inline = true },
                        new { name = "Warnings", value = $"{summary.WarningCount}", inline = true }
                    },
                    footer = new { text = $"Generated at {summary.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC" }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string FormatTeamsPayload(DailySummary summary)
    {
        var themeColor = summary.SystemHealthStatus switch
        {
            "Healthy" => "00FF00",
            "Warning" => "FFFF00",
            "Critical" => "FF0000",
            _ => "808080"
        };

        var payload = new
        {
            @type = "MessageCard",
            themeColor,
            summary = "Meridian - Daily Summary",
            sections = new[]
            {
                new
                {
                    activityTitle = $"Daily Summary - {summary.Date:yyyy-MM-dd}",
                    activitySubtitle = $"Market: {summary.MarketStatus} | Health: {summary.SystemHealthStatus}",
                    facts = new object[]
                    {
                        new { name = "Events Published", value = $"{summary.TotalEventsPublished:N0}" },
                        new { name = "Events Dropped", value = $"{summary.TotalEventsDropped:N0}" },
                        new { name = "Peak Queue Size", value = $"{summary.PeakQueueSize:N0}" },
                        new { name = "Avg Processing Time", value = $"{summary.AverageProcessingTimeUs:F2} µs" },
                        new { name = "Stale Symbols", value = $"{summary.StaleSymbolCount}" },
                        new { name = "Memory Usage", value = $"{summary.MemoryUsageMb:F0} MB" },
                        new { name = "Warnings", value = $"{summary.WarningCount}" }
                    },
                    markdown = true
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string FormatGenericPayload(DailySummary summary)
    {
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string FormatCustomMessage(WebhookType type, string message, string? title)
    {
        return type switch
        {
            WebhookType.Slack => JsonSerializer.Serialize(new { text = title != null ? $"*{title}*\n{message}" : message }),
            WebhookType.Discord => JsonSerializer.Serialize(new { content = title != null ? $"**{title}**\n{message}" : message }),
            WebhookType.Teams => JsonSerializer.Serialize(new { @type = "MessageCard", summary = title ?? "Notification", text = message }),
            _ => JsonSerializer.Serialize(new { title, message, timestamp = DateTimeOffset.UtcNow })
        };
    }

    private static string GetContentType(WebhookType type) => "application/json";

    private TimeSpan GetNextScheduledTime()
    {
        if (!TimeOnly.TryParse(_config.ScheduledTime, out var scheduledTime))
        {
            scheduledTime = new TimeOnly(16, 30); // Default: 4:30 PM ET
        }

        var easternZone = TimeZoneInfo.FindSystemTimeZoneById(GetEasternTimeZoneId());
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, easternZone);
        var todayScheduled = now.Date.Add(scheduledTime.ToTimeSpan());

        if (now.DateTime > todayScheduled)
        {
            todayScheduled = todayScheduled.AddDays(1);
        }

        var nextRun = new DateTimeOffset(todayScheduled, easternZone.GetUtcOffset(todayScheduled));
        return nextRun - DateTimeOffset.UtcNow;
    }

    private static string GetEasternTimeZoneId()
    {
        try
        { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York").Id; }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").Id; }
    }

    private async void ScheduledCallback(object? state)
    {
        try
        {
            // Only send on trading days
            if (!_tradingCalendar.IsTodayTradingDay())
            {
                _log.Debug("Skipping scheduled summary - not a trading day");
                return;
            }

            await SendSummaryAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error in scheduled daily summary");
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _scheduledTimer?.Dispose();
        _httpClient.Dispose();
        _cts.Dispose();
        return default;
    }
}

/// <summary>
/// Configuration for daily summary webhooks.
/// </summary>
public sealed record DailySummaryWebhookConfig
{
    /// <summary>
    /// List of webhook endpoints to send summaries to.
    /// </summary>
    public WebhookConfig[]? Webhooks { get; init; }

    /// <summary>
    /// Whether to enable scheduled daily summaries.
    /// </summary>
    public bool EnableScheduledSummary { get; init; } = true;

    /// <summary>
    /// Time to send the daily summary (ET timezone). Format: "HH:mm"
    /// Default: "16:30" (4:30 PM ET, after market close)
    /// </summary>
    public string ScheduledTime { get; init; } = "16:30";

    /// <summary>
    /// Number of retry attempts for failed deliveries.
    /// </summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>
    /// HTTP request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Configuration for a single webhook endpoint.
/// </summary>
public sealed record WebhookConfig
{
    /// <summary>
    /// Display name for the webhook.
    /// </summary>
    public string Name { get; init; } = "Default";

    /// <summary>
    /// Webhook URL.
    /// </summary>
    public string Url { get; init; } = "";

    /// <summary>
    /// Type of webhook (affects message formatting).
    /// </summary>
    public WebhookType Type { get; init; } = WebhookType.Generic;

    /// <summary>
    /// Whether this webhook is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Custom headers to include with requests.
    /// </summary>
    public Dictionary<string, string>? Headers { get; init; }
}

/// <summary>
/// Supported webhook types.
/// </summary>
public enum WebhookType : byte
{
    /// <summary>Generic JSON webhook.</summary>
    Generic,

    /// <summary>Slack incoming webhook.</summary>
    Slack,

    /// <summary>Discord webhook.</summary>
    Discord,

    /// <summary>Microsoft Teams webhook connector.</summary>
    Teams
}

/// <summary>
/// Daily summary data structure.
/// </summary>
public sealed class DailySummary
{
    public DateTimeOffset GeneratedAt { get; init; }
    public DateOnly Date { get; init; }
    public string MarketStatus { get; init; } = "";
    public bool IsHalfDay { get; init; }

    // Pipeline stats
    public long TotalEventsPublished { get; init; }
    public long TotalEventsConsumed { get; init; }
    public long TotalEventsDropped { get; init; }
    public long PeakQueueSize { get; init; }
    public double AverageProcessingTimeUs { get; init; }

    // Data quality
    public int StaleSymbolCount { get; init; }
    public string[] StaleSymbols { get; init; } = Array.Empty<string>();

    // System health
    public string SystemHealthStatus { get; init; } = "";
    public int WarningCount { get; init; }
    public string[] Warnings { get; init; } = Array.Empty<string>();

    // Memory
    public double MemoryUsageMb { get; init; }
    public GcStats GcCollections { get; init; } = new();
}

/// <summary>
/// Garbage collection statistics.
/// </summary>
public sealed class GcStats
{
    public int Gen0 { get; init; }
    public int Gen1 { get; init; }
    public int Gen2 { get; init; }
}

/// <summary>
/// Result of a daily summary send operation.
/// </summary>
public readonly record struct DailySummaryResult(
    bool Success,
    DailySummary Summary,
    IReadOnlyList<WebhookDeliveryResult> DeliveryResults,
    DateTimeOffset SentAt,
    string? ErrorMessage
);

/// <summary>
/// Result of a single webhook delivery.
/// </summary>
public readonly record struct WebhookDeliveryResult(
    string WebhookName,
    WebhookType WebhookType,
    bool Success,
    int? StatusCode,
    string? ErrorMessage,
    DateTimeOffset SentAt
);
