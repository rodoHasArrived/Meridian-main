using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// WPF platform-specific status service.
/// Extends <see cref="StatusServiceBase"/> with WPF-specific HTTP client and logging.
/// Implements <see cref="IStatusService"/> to provide DI-compatible status queries.
/// Part of Phase 2 service extraction.
/// </summary>
public sealed class StatusService : StatusServiceBase, IStatusService
{
    private static readonly Lazy<StatusService> _instance = new(() => new StatusService());
    private static readonly HttpClient _httpClient = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static StatusService Instance => _instance.Value;

    private StatusService()
    {
    }

    protected override HttpClient GetHttpClient() => _httpClient;

    protected override void LogInfo(string message, params (string key, string value)[] properties)
    {
        LoggingService.Instance.LogInfo(message, properties);
    }

    // ── IStatusService explicit implementation ────────────────────────────

    string IStatusService.ServiceUrl => BaseUrl;

    async Task<StatusResponse?> IStatusService.GetStatusAsync(CancellationToken ct)
    {
        try
        {
            using var cts = MakeTimeoutCts(ct);
            var response = await _httpClient.GetAsync($"{BaseUrl}/api/status", cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                return JsonSerializer.Deserialize<StatusResponse>(json, _jsonOptions);
            }
        }
        catch (OperationCanceledException) { /* timeout or caller cancelled — non-fatal */ }
        catch (HttpRequestException ex) { LogInfo("Status endpoint unreachable", ("error", ex.Message)); }
        catch (JsonException ex) { LogInfo("Status response parse failed", ("error", ex.Message)); }

        return null;
    }

    async Task<ApiResponse<StatusResponse>> IStatusService.GetStatusWithResponseAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = MakeTimeoutCts(ct);
            var response = await _httpClient.GetAsync($"{BaseUrl}/api/status", cts.Token);
            sw.Stop();
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var data = JsonSerializer.Deserialize<StatusResponse>(json, _jsonOptions);
                if (data != null)
                    return ApiResponse<StatusResponse>.Ok(data, (int)response.StatusCode);
            }

            return ApiResponse<StatusResponse>.Fail($"HTTP {(int)response.StatusCode}", (int)response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            return ApiResponse<StatusResponse>.Fail("Request timed out", isConnectionError: true);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return ApiResponse<StatusResponse>.Fail(ex.Message, isConnectionError: true);
        }
    }

    async Task<ServiceHealthResult> IStatusService.CheckHealthAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = MakeTimeoutCts(ct);
            var response = await _httpClient.GetAsync($"{BaseUrl}/healthz", cts.Token);
            sw.Stop();
            return response.IsSuccessStatusCode
                ? ServiceHealthResult.Healthy(true, (float)sw.Elapsed.TotalMilliseconds)
                : ServiceHealthResult.Unhealthy($"HTTP {(int)response.StatusCode}", (float)sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return ServiceHealthResult.Unhealthy("Request timed out");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return ServiceHealthResult.Unhealthy(ex.Message, (float)sw.Elapsed.TotalMilliseconds);
        }
    }

    private static CancellationTokenSource MakeTimeoutCts(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        return cts;
    }
}
