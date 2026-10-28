using Meridian.Execution.Sdk;
using Microsoft.Extensions.Hosting;

namespace Meridian.Execution.Services;

/// <summary>
/// Starts the resolved order manager with its host and drains it before the service
/// provider releases its gateway, accounting publisher, or durable handoff store.
/// Alternative managers without an explicit lifecycle need no hosted callbacks.
/// </summary>
internal sealed class OrderManagementHostedService(IOrderManager orderManager) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => orderManager is IOrderManagementLifecycle lifecycle
            ? lifecycle.StartAsync(cancellationToken)
            : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
        => orderManager is IOrderManagementLifecycle lifecycle
            ? lifecycle.StopAsync(cancellationToken)
            : Task.CompletedTask;
}
