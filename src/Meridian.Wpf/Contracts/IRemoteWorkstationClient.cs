using System;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Wpf.Contracts;

/// <summary>
/// Desktop-owned remote workstation API seam for deployable WPF clients.
/// Centralizes base URL, health, and typed request access instead of having
/// desktop services create ad hoc HTTP clients or bind directly to the shared singleton.
/// </summary>
public interface IRemoteWorkstationClient : IDisposable
{
    /// <summary>Gets the configured workstation service base URL.</summary>
    string BaseUrl { get; }

    /// <summary>Configures the remote workstation service URL and request timeouts.</summary>
    void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60);

    /// <summary>Checks the unauthenticated host health endpoint.</summary>
    Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default);

    /// <summary>Checks the typed workstation status route and returns diagnostic detail.</summary>
    Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default);

    /// <summary>Gets the typed workstation status payload.</summary>
    Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Gets the typed workstation status payload with response metadata.</summary>
    Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default);

    /// <summary>Performs a typed GET request against the configured remote workstation service.</summary>
    Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class;

    /// <summary>Performs a typed GET request with response metadata.</summary>
    Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class;

    /// <summary>Performs a typed POST request against the configured remote workstation service.</summary>
    Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default) where T : class;

    /// <summary>Performs a typed POST request with response metadata.</summary>
    Task<ApiResponse<T>> PostWithResponseAsync<T>(string endpoint, object? body = null, CancellationToken ct = default)
        where T : class;

    /// <summary>Performs a typed DELETE request with response metadata.</summary>
    Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default) where T : class;
}
