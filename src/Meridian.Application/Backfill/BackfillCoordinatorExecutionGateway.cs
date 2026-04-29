using Meridian.Application.UI;

namespace Meridian.Application.Backfill;

/// <summary>
/// Executes backfills through the existing <see cref="BackfillCoordinator"/> pipeline.
/// </summary>
public sealed class BackfillCoordinatorExecutionGateway : IBackfillExecutionGateway
{
    private readonly BackfillCoordinator _coordinator;

    public BackfillCoordinatorExecutionGateway(BackfillCoordinator coordinator)
    {
        _coordinator = coordinator;
    }

    public Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default)
        => _coordinator.RunAsync(request, ct);
}
