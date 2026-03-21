using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Api;

namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for retrieving system status from the collector.
/// Enables testability and dependency injection.
/// </summary>
public interface IStatusService
{
    /// <summary>
    /// Gets the current service URL.
    /// </summary>
    string ServiceUrl { get; }

    /// <summary>
    /// Gets the status of the market data collector service.
    /// </summary>
    Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the status with full response details.
    /// </summary>
    Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks if the service is healthy and reachable.
    /// </summary>
    /// <returns>A <see cref="ServiceHealthResult"/> containing health status information.</returns>
    /// <remarks>
    /// ServiceHealthResult and ApiResponse&lt;T&gt; are now defined in
    /// Meridian.Contracts.Api.ClientModels.cs
    /// </remarks>
    Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default);
}
