namespace Meridian.Execution;

public sealed partial class OrderManagementSystem
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
        => _lifecycle.StartAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
        => _lifecycle.StopAsync(cancellationToken);
}
