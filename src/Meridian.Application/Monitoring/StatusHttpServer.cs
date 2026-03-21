using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Pipeline;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Lightweight HTTP server exposing runtime status, metrics (Prometheus format), and a minimal HTML dashboard.
/// Avoids pulling in ASP.NET for small deployments.
/// Uses shared StatusEndpointHandlers for consistent response generation with UiServer.
/// Enhanced with detailed health check (QW-32), backpressure status (MON-18), and provider latency (PROV-11).
/// </summary>
public sealed class StatusHttpServer : IAsyncDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<StatusHttpServer>();
    private readonly HttpListener _listener = new();
    private readonly StatusEndpointHandlers _handlers;
    private readonly Func<IReadOnlyList<DepthIntegrityEvent>> _integrityProvider;
    private readonly SemaphoreSlim _requestLimiter;
    private readonly string? _accessToken;
    private readonly bool _requireRemoteAuth;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public StatusHttpServer(int port,
        Func<MetricsSnapshot> metricsProvider,
        Func<PipelineStatistics> pipelineProvider,
        Func<IReadOnlyList<DepthIntegrityEvent>> integrityProvider,
        Func<ErrorRingBuffer?>? errorBufferProvider = null,
        string bindAddress = "localhost",
        bool allowRemoteAccess = false,
        string? accessToken = null,
        int maxConcurrentRequests = 16)
    {
        _integrityProvider = integrityProvider;
        _accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
        _requireRemoteAuth = allowRemoteAccess;
        _requestLimiter = new SemaphoreSlim(Math.Max(1, maxConcurrentRequests));

        // Create shared handlers for consistent response generation
        _handlers = new StatusEndpointHandlers(
            metricsProvider,
            pipelineProvider,
            integrityProvider,
            errorBufferProvider);

        var resolvedBindAddress = ResolveBindAddress(bindAddress, allowRemoteAccess);
        if (!allowRemoteAccess && !string.Equals(resolvedBindAddress, bindAddress, StringComparison.OrdinalIgnoreCase))
        {
            _log.Warning("Remote bind address '{BindAddress}' ignored; binding to localhost only.", bindAddress);
        }
        if (allowRemoteAccess && string.IsNullOrWhiteSpace(_accessToken))
        {
            _log.Warning("Remote status access enabled without an access token; remote requests will be rejected.");
        }
        _listener.Prefixes.Add($"http://{resolvedBindAddress}:{port}/");
    }

    /// <summary>
    /// Gets the shared StatusEndpointHandlers instance.
    /// This can be used to share handlers with the ASP.NET Core UiServer.
    /// </summary>
    public StatusEndpointHandlers Handlers => _handlers;

    public void Start()
    {
        _listener.Start();
        _loop = HandleAsync();
        _log.Information("StatusHttpServer started");
    }

    /// <summary>
    /// Registers extended providers for detailed health, backpressure, and provider latency endpoints.
    /// </summary>
    public void RegisterExtendedProviders(
        Func<Task<DetailedHealthReport>>? detailedHealth = null,
        Func<BackpressureStatus>? backpressure = null,
        Func<ProviderLatencySummary>? providerLatency = null,
        Func<ConnectionHealthSnapshot>? connectionHealth = null)
    {
        _handlers.RegisterExtendedProviders(detailedHealth, backpressure, providerLatency, connectionHealth);
        _log.Debug("Extended providers registered for StatusHttpServer");
    }

    private async Task HandleAsync(CancellationToken ct = default)
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            { ctx = await _listener.GetContextAsync(); }
            catch (HttpListenerException) when (_cts.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }

            try
            {
                await _requestLimiter.WaitAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = ProcessRequestAsync(ctx);
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext ctx, CancellationToken ct = default)
    {
        try
        {
            await HandleRequestAsync(ctx);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Unhandled error processing HTTP request to {Path}",
                ctx.Request.Url?.AbsolutePath);
        }
        finally
        {
            _requestLimiter.Release();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct = default)
    {
        try
        {
            if (!IsAuthorized(ctx.Request))
            {
                await WriteUnauthorizedAsync(ctx.Response);
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath?.Trim('/')?.ToLowerInvariant() ?? string.Empty;

            // Support both /api/* and /* routes for client compatibility
            if (path.StartsWith("api/"))
                path = path.Substring(4);

            switch (path)
            {
                case "health":
                case "healthz":
                    await WriteHealthCheckAsync(ctx.Response);
                    break;
                case "health/detailed":
                    await WriteDetailedHealthAsync(ctx.Response);
                    break;
                case "ready":
                case "readyz":
                    await WriteReadinessAsync(ctx.Response);
                    break;
                case "live":
                case "livez":
                    await WriteLivenessAsync(ctx.Response);
                    break;
                case "metrics":
                    await WriteMetricsAsync(ctx.Response);
                    break;
                case "status":
                    await WriteStatusAsync(ctx.Response);
                    break;
                case "errors":
                    await WriteErrorsAsync(ctx.Response, ctx.Request.QueryString);
                    break;
                case "backpressure":
                    await WriteBackpressureAsync(ctx.Response);
                    break;
                case "providers/latency":
                    await WriteProviderLatencyAsync(ctx.Response);
                    break;
                case "connections":
                    await WriteConnectionHealthAsync(ctx.Response);
                    break;
                case "backfill/providers":
                    await WriteBackfillProvidersAsync(ctx.Response);
                    break;
                case "backfill/status":
                    await WriteBackfillStatusAsync(ctx.Response);
                    break;
                default:
                    await WriteDashboardAsync(ctx.Response);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Error handling HTTP request to {Path}", ctx.Request.Url?.AbsolutePath);
        }
        finally
        {
            ctx.Response.Close();
        }
    }

    /// <summary>
    /// Comprehensive health check endpoint using shared handlers.
    /// Returns 200 OK if healthy, 503 if degraded, with detailed status.
    /// </summary>
    private Task WriteHealthCheckAsync(HttpListenerResponse resp)
    {
        var response = _handlers.GetHealthCheck();
        resp.StatusCode = _handlers.GetHealthStatusCode(response);
        resp.ContentType = "application/json";

        var json = JsonSerializer.Serialize(response, s_jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Kubernetes-style readiness probe using shared handlers.
    /// Returns 200 if ready to receive traffic, 503 otherwise.
    /// </summary>
    private Task WriteReadinessAsync(HttpListenerResponse resp)
    {
        var (isReady, message) = _handlers.CheckReadiness();
        resp.StatusCode = isReady ? 200 : 503;
        resp.ContentType = "text/plain";
        var bytes = Encoding.UTF8.GetBytes(message);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Kubernetes-style liveness probe.
    /// Returns 200 if the service is alive.
    /// </summary>
    private Task WriteLivenessAsync(HttpListenerResponse resp)
    {
        resp.StatusCode = 200;
        resp.ContentType = "text/plain";
        var bytes = Encoding.UTF8.GetBytes("alive");
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Prometheus metrics endpoint using shared handlers.
    /// </summary>
    private Task WriteMetricsAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "text/plain; version=0.0.4";
        var content = _handlers.GetPrometheusMetrics();
        var bytes = Encoding.UTF8.GetBytes(content);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Status endpoint using shared handlers.
    /// </summary>
    private Task WriteStatusAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "application/json";
        var response = _handlers.GetStatus();

        // Add integrity events for backwards compatibility
        var payload = new
        {
            response.IsConnected,
            response.TimestampUtc,
            response.Metrics,
            response.Pipeline,
            integrity = _integrityProvider()
        };

        var json = JsonSerializer.Serialize(payload, s_jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Returns list of available backfill providers using shared handlers.
    /// </summary>
    private Task WriteBackfillProvidersAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "application/json";
        var providers = StatusEndpointHandlers.GetBackfillProviderInfo();
        var json = JsonSerializer.Serialize(providers, s_jsonOptions);

        var bytes = Encoding.UTF8.GetBytes(json);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Returns current backfill status for the desktop app.
    /// </summary>
    private Task WriteBackfillStatusAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "application/json";
        // Return empty status when no backfill is running
        var status = new BackfillResultDto
        {
            Success = true,
            BarsWritten = 0
        };

        var json = JsonSerializer.Serialize(status, s_jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    private Task WriteDashboardAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "text/html";
        var html = @"<!doctype html>
<html><head><title>Meridian Status</title>
<style>body{font-family:Arial;margin:20px;} code{background:#f4f4f4;padding:4px;display:block;}
table{border-collapse:collapse;} td,th{border:1px solid #ccc;padding:4px 8px;}</style></head>
<body>
<h2>Meridian Status</h2>
<p><a href='/metrics'>Prometheus metrics</a> | <a href='/status'>JSON status</a> | <a href='/errors'>Recent errors</a></p>
<pre id='metrics'>Loading metrics...</pre>
<h3>Recent integrity events</h3>
<table id='integrity'><thead><tr><th>Timestamp</th><th>Symbol</th><th>Kind</th><th>Details</th></tr></thead><tbody></tbody></table>
<script>
async function refresh(){
 const status=await fetch('/status').then(r=>r.json());
 document.getElementById('metrics').textContent=JSON.stringify(status.metrics,null,2);
 const tbody=document.querySelector('#integrity tbody');
 tbody.innerHTML='';
 (status.integrity||[]).forEach(ev=>{
  const row=document.createElement('tr');
  row.innerHTML=`<td>${{ev.timestamp}}</td><td>${{ev.symbol}}</td><td>${{ev.kind}}</td><td>${{ev.description||''}}</td>`;
  tbody.appendChild(row);
 });
}
setInterval(refresh,2000);refresh();
</script>
</body></html>";

        var bytes = Encoding.UTF8.GetBytes(html);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Returns the last N errors endpoint using shared handlers.
    /// Supports query parameters: count (default 10), level (warning/error/critical), symbol
    /// </summary>
    private Task WriteErrorsAsync(HttpListenerResponse resp, System.Collections.Specialized.NameValueCollection queryString)
    {
        resp.ContentType = "application/json";

        // Parse query parameters
        var countStr = queryString["count"];
        var count = 10;
        if (!string.IsNullOrEmpty(countStr) && int.TryParse(countStr, out var parsedCount) && parsedCount > 0)
        {
            count = Math.Min(parsedCount, 100);
        }

        var response = _handlers.GetErrors(count, queryString["level"], queryString["symbol"]);
        var json = JsonSerializer.Serialize(response, s_jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Detailed health check endpoint using shared handlers.
    /// Returns comprehensive health information including dependencies.
    /// </summary>
    private async Task WriteDetailedHealthAsync(HttpListenerResponse resp, CancellationToken ct = default)
    {
        var (report, error) = await _handlers.GetDetailedHealthAsync();

        if (error != null || report is null)
        {
            resp.StatusCode = 501;
            resp.ContentType = "application/json";
            var json = JsonSerializer.Serialize(new { error = error ?? "Health report unavailable" }, s_jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            return;
        }

        resp.StatusCode = report.Status switch
        {
            DetailedHealthStatus.Healthy => 200,
            DetailedHealthStatus.Degraded => 200,
            DetailedHealthStatus.Unhealthy => 503,
            _ => 200
        };

        resp.ContentType = "application/json";
        var reportJson = JsonSerializer.Serialize(report, s_jsonOptions);
        var reportBytes = Encoding.UTF8.GetBytes(reportJson);
        await resp.OutputStream.WriteAsync(reportBytes, 0, reportBytes.Length);
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (request.IsLocal)
        {
            return true;
        }

        if (!_requireRemoteAuth)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_accessToken))
        {
            return false;
        }

        var headerToken = request.Headers["X-Meridian-Status-Token"];
        if (string.IsNullOrWhiteSpace(headerToken))
        {
            var authHeader = request.Headers["Authorization"];
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                headerToken = authHeader.Substring("Bearer ".Length).Trim();
            }
        }

        return string.Equals(headerToken, _accessToken, StringComparison.Ordinal);
    }

    private static string ResolveBindAddress(string bindAddress, bool allowRemoteAccess)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return "localhost";
        }

        if (!allowRemoteAccess && !IsLoopback(bindAddress))
        {
            return "localhost";
        }

        return bindAddress;
    }

    private static bool IsLoopback(string bindAddress)
    {
        return string.Equals(bindAddress, "localhost", StringComparison.OrdinalIgnoreCase)
               || string.Equals(bindAddress, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(bindAddress, "::1", StringComparison.OrdinalIgnoreCase);
    }

    private static Task WriteUnauthorizedAsync(HttpListenerResponse resp)
    {
        resp.StatusCode = 401;
        resp.ContentType = "application/json";
        var payload = JsonSerializer.Serialize(new { error = "Unauthorized" });
        var bytes = Encoding.UTF8.GetBytes(payload);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Backpressure status endpoint using shared handlers.
    /// Returns current pipeline backpressure information.
    /// </summary>
    private Task WriteBackpressureAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "application/json";
        var response = _handlers.GetBackpressure();
        var json = JsonSerializer.Serialize(response, s_jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Provider latency histogram endpoint using shared handlers.
    /// Returns latency statistics per data provider.
    /// </summary>
    private Task WriteProviderLatencyAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "application/json";
        var (summary, error) = _handlers.GetProviderLatency();

        if (error != null)
        {
            var json = JsonSerializer.Serialize(new { error, providers = Array.Empty<object>() }, s_jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        var responseJson = JsonSerializer.Serialize(summary, s_jsonOptions);
        var responseBytes = Encoding.UTF8.GetBytes(responseJson);
        return resp.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
    }

    /// <summary>
    /// Connection health endpoint using shared handlers.
    /// Returns health status of all monitored connections.
    /// </summary>
    private Task WriteConnectionHealthAsync(HttpListenerResponse resp)
    {
        resp.ContentType = "application/json";
        var (snapshot, error) = _handlers.GetConnectionHealth();

        if (error != null)
        {
            var json = JsonSerializer.Serialize(new { error, connections = Array.Empty<object>() }, s_jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            return resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        var responseJson = JsonSerializer.Serialize(snapshot, s_jsonOptions);
        var responseBytes = Encoding.UTF8.GetBytes(responseJson);
        return resp.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Close();
        if (_loop is not null)
        {
            try
            { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
        _requestLimiter.Dispose();
    }
}
