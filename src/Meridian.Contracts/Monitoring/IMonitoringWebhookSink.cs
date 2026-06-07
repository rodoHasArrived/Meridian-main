using System.Threading;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Contract for delivery services that can publish monitoring alert messages.
/// </summary>
public interface IMonitoringWebhookSink
{
    /// <summary>
    /// Sends a monitoring message to the configured notification destinations.
    /// </summary>
    Task SendMonitoringMessageAsync(string message, string? title = null, CancellationToken ct = default);
}
