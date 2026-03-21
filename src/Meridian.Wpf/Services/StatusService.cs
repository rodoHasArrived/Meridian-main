using System;
using System.Net.Http;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF platform-specific status service.
/// Extends <see cref="StatusServiceBase"/> with WPF-specific HTTP client and logging.
/// Part of Phase 2 service extraction.
/// </summary>
public sealed class StatusService : StatusServiceBase
{
    private static readonly Lazy<StatusService> _instance = new(() => new StatusService());
    private static readonly HttpClient _httpClient = new();

    public static StatusService Instance => _instance.Value;

    private StatusService()
    {
    }

    protected override HttpClient GetHttpClient() => _httpClient;

    protected override void LogInfo(string message, params (string key, string value)[] properties)
    {
        LoggingService.Instance.LogInfo(message, properties);
    }
}
